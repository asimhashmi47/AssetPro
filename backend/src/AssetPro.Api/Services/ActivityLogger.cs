using AssetPro.Api.Auth;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;

namespace AssetPro.Api.Services;

/// <summary>
/// Records user-visible activity. Entries are added to the current DbContext so they commit
/// atomically with the business change they describe; call <see cref="SaveNowAsync"/> for standalone events.
/// </summary>
public sealed class ActivityLogger(AppDbContext db, ICurrentUser currentUser)
{
    public void Add(string action, string description, string? entity = null, object? entityId = null,
        ActivityResult result = ActivityResult.Success, int? userId = null, string? userEmail = null)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            OccurredAt = DateTime.UtcNow,
            UserId = userId ?? (currentUser.IsAuthenticated ? currentUser.UserId : null),
            UserEmail = Truncate(userEmail ?? currentUser.Email, 200),
            Action = action,
            Entity = entity,
            EntityId = entityId?.ToString(),
            Description = Truncate(description, 1000) ?? "",
            IpAddress = currentUser.IpAddress,
            Result = result
        });
    }

    public async Task SaveNowAsync(string action, string description, string? entity = null, object? entityId = null,
        ActivityResult result = ActivityResult.Success, int? userId = null, string? userEmail = null)
    {
        Add(action, description, entity, entityId, result, userId, userEmail);
        await db.SaveChangesAsync();
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}

public static class ActivityActions
{
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string EmployeeCreated = "EmployeeCreated";
    public const string EmployeeUpdated = "EmployeeUpdated";
    public const string AssetCreated = "AssetCreated";
    public const string AssetUpdated = "AssetUpdated";
    public const string AssetAssigned = "AssetAssigned";
    public const string AssetReturned = "AssetReturned";
    public const string AssetRepaired = "AssetRepaired";
    public const string AssetStatusChanged = "AssetStatusChanged";
    public const string ComponentReplaced = "ComponentReplaced";
    public const string AssetScrapped = "AssetScrapped";
    public const string InventoryAdded = "InventoryAdded";
    public const string InventoryAdjusted = "InventoryAdjusted";
    public const string RequestCreated = "RequestCreated";
    public const string RequestApproved = "RequestApproved";
    public const string RequestRejected = "RequestRejected";
    public const string RequestStatusChanged = "RequestStatusChanged";
    public const string MasterDataChanged = "MasterDataChanged";
    public const string SettingsUpdated = "SettingsUpdated";
    public const string RecordDeleted = "RecordDeleted";
}
