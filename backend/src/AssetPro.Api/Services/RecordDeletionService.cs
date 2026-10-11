using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Services;

/// <summary>
/// Permanent deletion of business records. Administrators only: every other role works through the
/// lifecycle actions (scrap, retire, deactivate), which keep the audit trail intact.
///
/// Each delete removes the rows that only describe the deleted record and detaches the ones that can
/// stand without it, in a single transaction. Deletion is refused only where it would leave the data
/// inconsistent — never merely because history exists. Stock levels need no repair: they are counted
/// from the asset rows themselves.
/// </summary>
public sealed class RecordDeletionService(AppDbContext db, ICurrentUser currentUser, ActivityLogger activity)
{
    public async Task DeleteAssetAsync(int id, CancellationToken ct = default)
    {
        EnsureAdmin();
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Asset not found.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.AssetAssignments.Where(x => x.AssetId == id).ExecuteDeleteAsync(ct);
        await db.AssetHistory.Where(x => x.AssetId == id).ExecuteDeleteAsync(ct);
        await db.ComponentReplacements.Where(x => x.AssetId == id).ExecuteDeleteAsync(ct);
        await db.ScrapRecords.Where(x => x.AssetId == id).ExecuteDeleteAsync(ct);
        // Stock movements and requests are records of their own; they only lose the link to this asset.
        await db.InventoryTransactions.Where(x => x.AssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AssetId, (int?)null), ct);
        await db.Requests.Where(x => x.AssetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AssetId, (int?)null), ct);

        db.Assets.Remove(asset);
        activity.Add(ActivityActions.RecordDeleted, $"Asset {asset.AssetTag} ({asset.Name}) deleted", "Asset", id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteEmployeeAsync(int id, CancellationToken ct = default)
    {
        EnsureAdmin();
        var employee = await db.Employees.Include(e => e.User).FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Employee not found.");

        var holding = await db.AssetAssignments.CountAsync(a => a.EmployeeId == id && a.ReturnedAt == null, ct);
        if (holding > 0)
            throw new BusinessRuleException(
                $"{employee.FullName} still holds {holding} asset(s). Return them first so the assets are not left assigned to a deleted person.");

        if (employee.User is { } login)
        {
            if (login.Id == currentUser.UserId)
                throw new BusinessRuleException("You cannot delete your own employee record.");
            await EnsureNotLastAdminAsync(login, ct);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var requestIds = await db.Requests.Where(r => r.RequesterEmployeeId == id).Select(r => r.Id).ToListAsync(ct);
        if (requestIds.Count > 0)
        {
            await db.RequestStatusHistory.Where(h => requestIds.Contains(h.RequestId)).ExecuteDeleteAsync(ct);
            await db.Requests.Where(r => requestIds.Contains(r.Id)).ExecuteDeleteAsync(ct);
        }

        // Past assignments name the employee and cannot outlive them; the asset's other history can.
        await db.AssetAssignments.Where(a => a.EmployeeId == id).ExecuteDeleteAsync(ct);
        await db.AssetHistory.Where(h => h.EmployeeId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.EmployeeId, (int?)null), ct);
        await db.InventoryTransactions.Where(t => t.EmployeeId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.EmployeeId, (int?)null), ct);

        if (employee.User is { } account)
        {
            // The sign-in must go with the person, or it would survive as an orphaned login.
            await DetachUserAsync(account, ct);
            db.Users.Remove(account);
        }

        db.Employees.Remove(employee);
        activity.Add(ActivityActions.RecordDeleted, $"Employee {employee.FullName} ({employee.EmployeeCode}) deleted", "Employee", id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteUserAsync(int id, CancellationToken ct = default)
    {
        EnsureAdmin();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("User not found.");

        if (user.Id == currentUser.UserId)
            throw new BusinessRuleException("You cannot delete your own sign-in account.");
        await EnsureNotLastAdminAsync(user, ct);

        var requests = await db.Requests.CountAsync(r => r.RequesterUserId == id, ct);
        if (requests > 0)
            throw new BusinessRuleException(
                $"This account raised {requests} request(s), which name it as the requester. Delete the employee to remove the person and their requests together.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DetachUserAsync(user, ct);
        db.Users.Remove(user);
        activity.Add(ActivityActions.RecordDeleted, $"Sign-in account {user.Email} deleted", "User", id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteRequestAsync(int id, CancellationToken ct = default)
    {
        EnsureAdmin();
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Request not found.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.RequestStatusHistory.Where(h => h.RequestId == id).ExecuteDeleteAsync(ct);
        db.Requests.Remove(request);
        activity.Add(ActivityActions.RecordDeleted, $"Request {request.RequestNumber} deleted", "Request", id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteInventoryItemAsync(int id, CancellationToken ct = default)
    {
        EnsureAdmin();
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("Inventory item not found.");

        var assets = await db.Assets.CountAsync(a => a.InventoryItemId == id, ct);
        if (assets > 0)
            throw new BusinessRuleException(
                $"{item.Name} still has {assets} asset(s) registered against it. Delete those assets first.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.InventoryTransactions.Where(t => t.InventoryItemId == id).ExecuteDeleteAsync(ct);
        db.InventoryItems.Remove(item);
        activity.Add(ActivityActions.RecordDeleted, $"Inventory item {item.Name} deleted", "InventoryItem", id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Clears what points at a sign-in account, keeping the activity trail (it stores the email too).</summary>
    private async Task DetachUserAsync(User user, CancellationToken ct)
    {
        await db.UserPermissions.Where(p => p.UserId == user.Id).ExecuteDeleteAsync(ct);
        await db.ActivityLogs.Where(l => l.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, (int?)null), ct);
        await db.AssetAssignments.Where(a => a.AssignedById == user.Id).ExecuteDeleteAsync(ct);
        await db.AssetAssignments.Where(a => a.ReceivedById == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReceivedById, (int?)null), ct);
        await db.AssetHistory.Where(h => h.PerformedById == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PerformedById, (int?)null), ct);
        await db.InventoryTransactions.Where(t => t.PerformedById == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PerformedById, (int?)null), ct);
        await db.ComponentReplacements.Where(c => c.PerformedById == user.Id).ExecuteDeleteAsync(ct);
        await db.ScrapRecords.Where(r => r.PerformedById == user.Id).ExecuteDeleteAsync(ct);
        await db.RequestStatusHistory.Where(h => h.ChangedById == user.Id).ExecuteDeleteAsync(ct);
    }

    private async Task EnsureNotLastAdminAsync(User user, CancellationToken ct)
    {
        if (user.Role != UserRole.Admin) return;
        var admins = await db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive && u.Id != user.Id, ct);
        if (admins == 0)
            throw new BusinessRuleException("This is the last administrator account. Promote another user before deleting it.");
    }

    private void EnsureAdmin()
    {
        if (!currentUser.IsAdmin)
            throw new ForbiddenException("Only administrators can permanently delete records.");
    }
}
