using System.Linq.Expressions;
using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Assets;

/// <summary>
/// All asset lifecycle operations. Every state change writes an AssetHistory row, an ActivityLog row and,
/// when stock availability changes, an InventoryTransaction - all in the same SaveChanges so they commit together.
/// </summary>
public sealed class AssetService(AppDbContext db, ICurrentUser currentUser, ActivityLogger activity, ILogger<AssetService> logger)
{
    public static readonly Expression<Func<Asset, AssetListDto>> ToListDto = a => new AssetListDto
    {
        Id = a.Id,
        AssetTag = a.AssetTag,
        Name = a.Name,
        AssetTypeId = a.AssetTypeId,
        AssetType = a.AssetType!.Name,
        BrandId = a.BrandId,
        Brand = a.Brand != null ? a.Brand.Name : null,
        Model = a.Model,
        SerialNumber = a.SerialNumber,
        PurchaseDate = a.PurchaseDate,
        PurchaseCost = a.PurchaseCost,
        WarrantyExpiry = a.WarrantyExpiry,
        Condition = a.Condition,
        Status = a.Status,
        LocationId = a.LocationId,
        Location = a.Location != null ? a.Location.Name : null,
        AssignedToEmployeeId = a.Assignments.Where(x => x.ReturnedAt == null).Select(x => (int?)x.EmployeeId).FirstOrDefault(),
        AssignedTo = a.Assignments.Where(x => x.ReturnedAt == null).Select(x => x.Employee!.FullName).FirstOrDefault(),
        AssignedAt = a.Assignments.Where(x => x.ReturnedAt == null).Select(x => (DateTime?)x.AssignedAt).FirstOrDefault(),
        CreatedAt = a.CreatedAt
    };

    public static readonly Expression<Func<AssetHistory, AssetHistoryDto>> ToHistoryDto = h => new AssetHistoryDto
    {
        Id = h.Id,
        OccurredAt = h.OccurredAt,
        AssetId = h.AssetId,
        AssetTag = h.Asset!.AssetTag,
        AssetName = h.Asset.Name,
        AssetType = h.Asset.AssetType!.Name,
        EmployeeId = h.EmployeeId,
        EmployeeName = h.Employee != null ? h.Employee.FullName : null,
        Action = h.Action,
        PreviousStatus = h.PreviousStatus,
        NewStatus = h.NewStatus,
        PerformedBy = h.PerformedBy != null ? (h.PerformedBy.Employee != null ? h.PerformedBy.Employee.FullName : h.PerformedBy.Email) : null,
        Notes = h.Notes
    };

    public Task<PagedResult<AssetListDto>> ListAsync(GridQuery grid, CancellationToken ct) =>
        db.Assets.AsNoTracking().Select(ToListDto).ToPagedResultAsync(grid, nameof(AssetListDto.CreatedAt), true, ct);

    public async Task<AssetDetailDto> GetAsync(int id, CancellationToken ct)
    {
        EnsureCanViewAsset(id);
        var asset = await db.Assets.AsNoTracking().Where(a => a.Id == id).Select(ToListDto).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException("Asset not found.");
        if (!currentUser.Has(Permissions.AssetsView) && asset.AssignedToEmployeeId != currentUser.EmployeeId)
            throw new NotFoundException("Asset not found.");

        var extra = await db.Assets.AsNoTracking().Where(a => a.Id == id)
            .Select(a => new { a.Notes, a.InventoryItemId }).FirstAsync(ct);

        var current = await db.AssetAssignments.AsNoTracking()
            .Where(x => x.AssetId == id && x.ReturnedAt == null)
            .Select(x => new CurrentAssignmentDto(x.Id, x.EmployeeId, x.Employee!.FullName, x.Employee.EmployeeCode, x.AssignedAt,
                x.AssignedBy!.Employee != null ? x.AssignedBy.Employee.FullName : x.AssignedBy.Email, x.ConditionAtAssignment, x.Notes))
            .FirstOrDefaultAsync(ct);

        var canSeeAll = currentUser.Has(Permissions.AssetsView);
        var assignments = canSeeAll
            ? await db.AssetAssignments.AsNoTracking().Where(x => x.AssetId == id).OrderByDescending(x => x.AssignedAt)
                .Select(x => new AssignmentRecordDto(x.Id, x.EmployeeId, x.Employee!.FullName, x.AssignedAt, x.ReturnedAt,
                    x.ConditionAtAssignment, x.ReturnCondition, x.DamageDetails))
                .ToListAsync(ct)
            : [];

        var replacements = await db.ComponentReplacements.AsNoTracking().Where(x => x.AssetId == id)
            .OrderByDescending(x => x.ReplacedOn).ThenByDescending(x => x.Id)
            .Select(x => new ComponentReplacementDto(x.Id, x.Component, x.OldComponent, x.NewComponent, x.Reason, x.ReplacedOn,
                x.PerformedBy!.Employee != null ? x.PerformedBy.Employee.FullName : x.PerformedBy.Email, x.Notes))
            .ToListAsync(ct);

        var scrap = await db.ScrapRecords.AsNoTracking().Where(x => x.AssetId == id)
            .Select(x => new ScrapRecordDto(x.Id, x.ScrapDate, x.Reason, x.Quantity, x.Notes,
                x.PerformedBy!.Employee != null ? x.PerformedBy.Employee.FullName : x.PerformedBy.Email))
            .FirstOrDefaultAsync(ct);

        return new AssetDetailDto
        {
            Asset = asset,
            Notes = extra.Notes,
            InventoryItemId = extra.InventoryItemId,
            CurrentAssignment = current,
            Assignments = assignments,
            Replacements = replacements,
            Scrap = scrap
        };
    }

    /// <summary>Asset timeline. Users without Assets.View only see entries that involve themselves.</summary>
    public async Task<IReadOnlyList<AssetHistoryDto>> GetHistoryAsync(int id, CancellationToken ct)
    {
        EnsureCanViewAsset(id);
        if (!await db.Assets.AnyAsync(a => a.Id == id, ct)) throw new NotFoundException("Asset not found.");

        var query = db.AssetHistory.AsNoTracking().Where(h => h.AssetId == id);
        if (!currentUser.Has(Permissions.AssetsView))
        {
            var employeeId = currentUser.EmployeeId;
            var isCurrentHolder = await db.AssetAssignments.AnyAsync(x => x.AssetId == id && x.ReturnedAt == null && x.EmployeeId == employeeId, ct);
            if (!isCurrentHolder) throw new NotFoundException("Asset not found.");
            query = query.Where(h => h.EmployeeId == employeeId);
        }
        return await query.OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id).Select(ToHistoryDto).ToListAsync(ct);
    }

    public Task<PagedResult<AssetHistoryDto>> ListHistoryAsync(GridQuery grid, CancellationToken ct) =>
        db.AssetHistory.AsNoTracking().Select(ToHistoryDto).ToPagedResultAsync(grid, nameof(AssetHistoryDto.OccurredAt), true, ct);

    public async Task<AssetListDto> CreateAsync(SaveAssetRequest request)
    {
        var type = await db.AssetTypes.FirstOrDefaultAsync(t => t.Id == request.AssetTypeId && t.IsActive)
                   ?? throw ValidationFailedException.For("assetTypeId", "Select a valid asset type.");
        await ValidateReferencesAsync(request);
        var serial = Clean(request.SerialNumber);
        await EnsureUniqueSerialAsync(serial, null);

        var tag = Clean(request.AssetTag)?.ToUpperInvariant();
        if (tag is null) tag = (await NextTagsAsync(type, 1))[0];
        else await EnsureUniqueTagAsync(tag, null);

        var item = await FindOrCreateItemAsync(type, request.BrandId, Clean(request.Model), request.Name.Trim());
        var asset = new Asset
        {
            AssetTag = tag,
            InventoryItem = item,
            Status = AssetStatus.InStock
        };
        Apply(asset, request, serial);
        db.Assets.Add(asset);

        var balance = await InStockCountAsync(item.Id) + 1;
        AddHistory(asset, AssetAction.Created, null, AssetStatus.InStock, null, "Asset registered");
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            InventoryItem = item, Asset = asset, Type = InventoryTransactionType.StockIn, Quantity = 1, BalanceAfter = balance,
            Notes = "Asset registered", PerformedById = currentUser.UserId, OccurredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        activity.Add(ActivityActions.AssetCreated, $"Created asset {asset.AssetTag} ({asset.Name})", "Asset", asset.Id);
        await db.SaveChangesAsync();
        logger.LogInformation("Asset {AssetTag} created by user {UserId}", asset.AssetTag, currentUser.UserId);
        return await ProjectAsync(asset.Id);
    }

    public async Task<AssetListDto> UpdateAsync(int id, SaveAssetRequest request)
    {
        var asset = await db.Assets.Include(a => a.InventoryItem).FirstOrDefaultAsync(a => a.Id == id)
                    ?? throw new NotFoundException("Asset not found.");
        var type = await db.AssetTypes.FirstOrDefaultAsync(t => t.Id == request.AssetTypeId)
                   ?? throw ValidationFailedException.For("assetTypeId", "Select a valid asset type.");
        await ValidateReferencesAsync(request);
        var serial = Clean(request.SerialNumber);
        await EnsureUniqueSerialAsync(serial, id);

        var tag = Clean(request.AssetTag)?.ToUpperInvariant();
        if (tag is not null && tag != asset.AssetTag)
        {
            await EnsureUniqueTagAsync(tag, id);
            asset.AssetTag = tag;
        }

        var model = Clean(request.Model);
        if (asset.AssetTypeId != request.AssetTypeId || asset.BrandId != request.BrandId || asset.Model != model)
            asset.InventoryItem = await FindOrCreateItemAsync(type, request.BrandId, model, request.Name.Trim());

        Apply(asset, request, serial);
        AddHistory(asset, AssetAction.Updated, asset.Status, asset.Status, CurrentHolderId(asset.Id), "Asset details updated");
        activity.Add(ActivityActions.AssetUpdated, $"Updated asset {asset.AssetTag}", "Asset", asset.Id);
        await db.SaveChangesAsync();
        return await ProjectAsync(asset.Id);
    }

    public async Task<AssetListDto> AssignAsync(int id, AssignAssetRequest request)
    {
        var asset = await LoadForUpdateAsync(id);
        var employee = await LoadAssignableEmployeeAsync(request.EmployeeId);
        var assignedAt = ValidatePastOrNow(request.AssignedAt, "assignedAt");

        AssignCore(asset, employee, assignedAt, request.Condition, request.Notes);
        var balance = await InStockCountAsync(asset.InventoryItemId) - 1;
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            InventoryItemId = asset.InventoryItemId, AssetId = asset.Id, EmployeeId = employee.Id,
            Type = InventoryTransactionType.StockOut, Quantity = 1, BalanceAfter = balance,
            Notes = $"Assigned to {employee.FullName}", PerformedById = currentUser.UserId, OccurredAt = DateTime.UtcNow
        });
        activity.Add(ActivityActions.AssetAssigned, $"Assigned {asset.AssetTag} to {employee.FullName}", "Asset", asset.Id);
        await db.SaveChangesAsync();
        return await ProjectAsync(asset.Id);
    }

    /// <summary>Shared by single assignment and inventory stock-out. Caller records the inventory transaction.</summary>
    internal void AssignCore(Asset asset, Employee employee, DateTime assignedAt, AssetCondition? condition, string? notes)
    {
        switch (asset.Status)
        {
            case AssetStatus.InStock:
                break;
            case AssetStatus.Assigned:
                throw new BusinessRuleException($"{asset.AssetTag} is already assigned. Return it before assigning it again.");
            case AssetStatus.Scrapped:
                throw new BusinessRuleException($"{asset.AssetTag} is scrapped and cannot be assigned.");
            case AssetStatus.Retired:
                throw new BusinessRuleException($"{asset.AssetTag} is retired and cannot be assigned.");
            case AssetStatus.UnderRepair:
                throw new BusinessRuleException($"{asset.AssetTag} is under repair. Complete the repair before assigning it.");
            case AssetStatus.Damaged:
                throw new BusinessRuleException($"{asset.AssetTag} is marked damaged. Repair or release it to stock first.");
        }

        if (condition is { } c) asset.Condition = c;
        db.AssetAssignments.Add(new AssetAssignment
        {
            AssetId = asset.Id, EmployeeId = employee.Id, AssignedById = currentUser.UserId, AssignedAt = assignedAt,
            ConditionAtAssignment = asset.Condition, Notes = Clean(notes)
        });
        var previous = asset.Status;
        asset.Status = AssetStatus.Assigned;
        AddHistory(asset, AssetAction.Assigned, previous, AssetStatus.Assigned, employee.Id,
            Clean(notes) ?? $"Assigned to {employee.FullName}");
    }

    public async Task<AssetListDto> ReturnAsync(int id, ReturnAssetRequest request)
    {
        var asset = await LoadForUpdateAsync(id);
        var assignment = await db.AssetAssignments.Include(x => x.Employee)
                             .FirstOrDefaultAsync(x => x.AssetId == id && x.ReturnedAt == null)
                         ?? throw new BusinessRuleException($"{asset.AssetTag} is not currently assigned, so it cannot be returned.");

        if (request.ResultingStatus is not (AssetStatus.InStock or AssetStatus.UnderRepair or AssetStatus.Damaged or AssetStatus.Scrapped))
            throw ValidationFailedException.For("resultingStatus", "After a return the asset can go to In Stock, Under Repair, Damaged or Scrapped.");
        if (request.ResultingStatus == AssetStatus.Scrapped && string.IsNullOrWhiteSpace(request.ScrapReason))
            throw ValidationFailedException.For("scrapReason", "A scrap reason is required.");
        if (request.ResultingStatus == AssetStatus.InStock && request.Condition is AssetCondition.Damaged or AssetCondition.Critical)
            throw ValidationFailedException.For("resultingStatus", "A damaged asset cannot go straight back to stock. Choose Under Repair, Damaged or Scrapped.");

        var returnedAt = ValidatePastOrNow(request.ReturnedAt, "returnedAt");
        if (returnedAt < assignment.AssignedAt)
            throw ValidationFailedException.For("returnedAt", "Return date cannot be before the assignment date.");

        assignment.ReturnedAt = returnedAt;
        assignment.ReturnedBy = Clean(request.ReturnedBy) ?? assignment.Employee!.FullName;
        assignment.ReceivedById = currentUser.UserId;
        assignment.ReturnCondition = request.Condition;
        assignment.DamageDetails = Clean(request.DamageDetails);
        assignment.ReturnNotes = Clean(request.Notes);

        var previous = asset.Status;
        asset.Condition = request.Condition;
        asset.Status = request.ResultingStatus == AssetStatus.Scrapped ? AssetStatus.Scrapped : request.ResultingStatus;
        AddHistory(asset, AssetAction.Returned, previous, asset.Status, assignment.EmployeeId,
            Clean(request.Notes) ?? $"Returned by {assignment.ReturnedBy}" + (assignment.DamageDetails is null ? "" : $" - {assignment.DamageDetails}"));

        if (asset.Status == AssetStatus.InStock)
        {
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                InventoryItemId = asset.InventoryItemId, AssetId = asset.Id, EmployeeId = assignment.EmployeeId,
                Type = InventoryTransactionType.Return, Quantity = 1, BalanceAfter = await InStockCountAsync(asset.InventoryItemId) + 1,
                Notes = $"Returned by {assignment.ReturnedBy}", PerformedById = currentUser.UserId, OccurredAt = DateTime.UtcNow
            });
        }
        else if (asset.Status == AssetStatus.UnderRepair)
        {
            AddHistory(asset, AssetAction.RepairStarted, AssetStatus.UnderRepair, AssetStatus.UnderRepair, null, "Sent to repair after return");
        }
        else if (asset.Status == AssetStatus.Damaged)
        {
            AddHistory(asset, AssetAction.Damaged, AssetStatus.Damaged, AssetStatus.Damaged, null, assignment.DamageDetails ?? "Marked damaged on return");
        }
        else if (asset.Status == AssetStatus.Scrapped)
        {
            await AddScrapAsync(asset, request.ScrapReason!.Trim(), DateOnly.FromDateTime(returnedAt), request.Notes, wasInStock: false);
        }

        activity.Add(ActivityActions.AssetReturned, $"{asset.AssetTag} returned by {assignment.Employee!.FullName} ({asset.Status})", "Asset", asset.Id);
        await db.SaveChangesAsync();
        return await ProjectAsync(asset.Id);
    }

    public async Task<AssetListDto> RepairAsync(int id, RepairAssetRequest request)
    {
        var asset = await LoadForUpdateAsync(id);
        var holderId = CurrentHolderId(asset.Id);
        var previous = asset.Status;

        if (request.Action == RepairAction.Start)
        {
            if (asset.Status is not (AssetStatus.InStock or AssetStatus.Assigned or AssetStatus.Damaged))
                throw new BusinessRuleException($"{asset.AssetTag} is {Describe(asset.Status)} and cannot be sent to repair.");

            asset.Status = AssetStatus.UnderRepair;
            AddHistory(asset, AssetAction.RepairStarted, previous, AssetStatus.UnderRepair, holderId, Clean(request.Notes) ?? "Repair started");
            if (previous == AssetStatus.InStock)
                await AddAdjustmentAsync(asset, -1, "Sent to repair");
            activity.Add(ActivityActions.AssetRepaired, $"Repair started for {asset.AssetTag}", "Asset", asset.Id);
        }
        else
        {
            if (asset.Status != AssetStatus.UnderRepair)
                throw new BusinessRuleException($"{asset.AssetTag} is not under repair.");

            // An asset repaired while still assigned goes back to its holder; otherwise it is released to stock.
            var stillAssigned = await db.AssetAssignments.AnyAsync(x => x.AssetId == id && x.ReturnedAt == null);
            asset.Status = stillAssigned ? AssetStatus.Assigned : AssetStatus.InStock;
            asset.Condition = request.ResultingCondition ?? AssetCondition.Good;
            AddHistory(asset, AssetAction.RepairCompleted, previous, asset.Status, holderId, Clean(request.Notes) ?? "Repair completed");
            if (asset.Status == AssetStatus.InStock)
                await AddAdjustmentAsync(asset, +1, "Repaired and released to stock");
            activity.Add(ActivityActions.AssetRepaired, $"Repair completed for {asset.AssetTag}", "Asset", asset.Id);
        }

        await db.SaveChangesAsync();
        return await ProjectAsync(asset.Id);
    }

    public async Task<ComponentReplacementDto> ReplaceComponentAsync(int id, ReplaceComponentRequest request)
    {
        var asset = await LoadForUpdateAsync(id);
        if (asset.Status is AssetStatus.Scrapped or AssetStatus.Retired)
            throw new BusinessRuleException($"{asset.AssetTag} is {Describe(asset.Status)}; components cannot be replaced.");

        var replacedOn = request.ReplacedOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (replacedOn > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            throw ValidationFailedException.For("replacedOn", "Replacement date cannot be in the future.");

        var replacement = new ComponentReplacement
        {
            AssetId = asset.Id,
            Component = request.Component.Trim(),
            OldComponent = Clean(request.OldComponent),
            NewComponent = request.NewComponent.Trim(),
            Reason = request.Reason.Trim(),
            ReplacedOn = replacedOn,
            PerformedById = currentUser.UserId,
            Notes = Clean(request.Notes),
            CreatedAt = DateTime.UtcNow
        };
        db.ComponentReplacements.Add(replacement);

        var summary = $"{replacement.Component}: {replacement.OldComponent ?? "old part"} → {replacement.NewComponent} ({replacement.Reason})";
        AddHistory(asset, AssetAction.ComponentReplaced, asset.Status, asset.Status, CurrentHolderId(asset.Id),
            replacement.Notes is null ? summary : $"{summary}. {replacement.Notes}");
        activity.Add(ActivityActions.ComponentReplaced, $"Replaced {replacement.Component} on {asset.AssetTag}", "Asset", asset.Id);
        await db.SaveChangesAsync();

        var performer = await db.Users.Where(u => u.Id == currentUser.UserId)
            .Select(u => u.Employee != null ? u.Employee.FullName : u.Email).FirstAsync();
        return new ComponentReplacementDto(replacement.Id, replacement.Component, replacement.OldComponent, replacement.NewComponent,
            replacement.Reason, replacement.ReplacedOn, performer, replacement.Notes);
    }

    public async Task<AssetListDto> ScrapAsync(int id, ScrapAssetRequest request)
    {
        var asset = await LoadForUpdateAsync(id);
        switch (asset.Status)
        {
            case AssetStatus.Scrapped:
                throw new BusinessRuleException($"{asset.AssetTag} is already scrapped.");
            case AssetStatus.Assigned:
                throw new BusinessRuleException($"{asset.AssetTag} is assigned. Process the return before scrapping it.");
        }
        if (await db.AssetAssignments.AnyAsync(x => x.AssetId == id && x.ReturnedAt == null))
            throw new BusinessRuleException($"{asset.AssetTag} still has an open assignment. Process the return first.");

        var scrapDate = request.ScrapDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (scrapDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            throw ValidationFailedException.For("scrapDate", "Scrap date cannot be in the future.");

        var previous = asset.Status;
        asset.Status = AssetStatus.Scrapped;
        AddHistory(asset, AssetAction.Scrapped, previous, AssetStatus.Scrapped, null, request.Reason.Trim());
        await AddScrapAsync(asset, request.Reason.Trim(), scrapDate, request.Notes, wasInStock: previous == AssetStatus.InStock, addHistory: false);
        activity.Add(ActivityActions.AssetScrapped, $"{asset.AssetTag} moved to scrap: {request.Reason.Trim()}", "Asset", asset.Id);
        await db.SaveChangesAsync();
        return await ProjectAsync(asset.Id);
    }

    public async Task<AssetListDto> ChangeStatusAsync(int id, ChangeAssetStatusRequest request)
    {
        var asset = await LoadForUpdateAsync(id);
        var previous = asset.Status;
        var target = request.Status;

        var allowed = (previous, target) switch
        {
            (AssetStatus.InStock, AssetStatus.Damaged) => true,
            (AssetStatus.InStock, AssetStatus.Retired) => true,
            (AssetStatus.Damaged, AssetStatus.InStock) => true,
            (AssetStatus.Damaged, AssetStatus.Retired) => true,
            _ => false
        };
        if (!allowed)
            throw new BusinessRuleException(previous == target
                ? $"{asset.AssetTag} is already {Describe(target)}."
                : $"{asset.AssetTag} cannot move from {Describe(previous)} to {Describe(target)}. Use assign, return, repair or scrap instead.");

        asset.Status = target;
        if (target == AssetStatus.Damaged && asset.Condition is not (AssetCondition.Damaged or AssetCondition.Critical))
            asset.Condition = AssetCondition.Damaged;

        var action = target switch
        {
            AssetStatus.Damaged => AssetAction.Damaged,
            AssetStatus.Retired => AssetAction.Retired,
            _ => AssetAction.StatusChanged
        };
        AddHistory(asset, action, previous, target, null, Clean(request.Notes) ?? $"Status changed to {Describe(target)}");
        if (previous == AssetStatus.InStock) await AddAdjustmentAsync(asset, -1, $"Marked {Describe(target)}");
        if (target == AssetStatus.InStock) await AddAdjustmentAsync(asset, +1, "Released to stock");
        activity.Add(ActivityActions.AssetStatusChanged, $"{asset.AssetTag}: {Describe(previous)} → {Describe(target)}", "Asset", asset.Id);
        await db.SaveChangesAsync();
        return await ProjectAsync(asset.Id);
    }

    // ---------- helpers ----------

    internal void AddHistory(Asset asset, AssetAction action, AssetStatus? previous, AssetStatus? next, int? employeeId, string? notes)
    {
        db.AssetHistory.Add(new AssetHistory
        {
            Asset = asset.Id == 0 ? asset : null,
            AssetId = asset.Id,
            Action = action,
            PreviousStatus = previous,
            NewStatus = next,
            EmployeeId = employeeId,
            PerformedById = currentUser.IsAuthenticated ? currentUser.UserId : null,
            Notes = notes,
            OccurredAt = DateTime.UtcNow
        });
    }

    private async Task AddScrapAsync(Asset asset, string reason, DateOnly date, string? notes, bool wasInStock, bool addHistory = true)
    {
        if (addHistory) AddHistory(asset, AssetAction.Scrapped, AssetStatus.Scrapped, AssetStatus.Scrapped, null, reason);
        db.ScrapRecords.Add(new ScrapRecord
        {
            AssetId = asset.Id, ScrapDate = date, Reason = reason, Quantity = 1, Notes = Clean(notes),
            PerformedById = currentUser.UserId, CreatedAt = DateTime.UtcNow
        });
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            InventoryItemId = asset.InventoryItemId, AssetId = asset.Id, Type = InventoryTransactionType.Scrap, Quantity = 1,
            BalanceAfter = await InStockCountAsync(asset.InventoryItemId) - (wasInStock ? 1 : 0),
            Notes = reason, PerformedById = currentUser.UserId, OccurredAt = DateTime.UtcNow
        });
    }

    private async Task AddAdjustmentAsync(Asset asset, int delta, string notes)
    {
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            InventoryItemId = asset.InventoryItemId, AssetId = asset.Id, Type = InventoryTransactionType.Adjustment, Quantity = delta,
            BalanceAfter = await InStockCountAsync(asset.InventoryItemId) + delta,
            Notes = notes, PerformedById = currentUser.UserId, OccurredAt = DateTime.UtcNow
        });
    }

    /// <summary>Units currently in stock according to the database (pending changes are not included).</summary>
    internal Task<int> InStockCountAsync(int itemId) =>
        db.Assets.CountAsync(a => a.InventoryItemId == itemId && a.Status == AssetStatus.InStock);

    private int? CurrentHolderId(int assetId) =>
        db.AssetAssignments.Where(x => x.AssetId == assetId && x.ReturnedAt == null).Select(x => (int?)x.EmployeeId).FirstOrDefault();

    private async Task<Asset> LoadForUpdateAsync(int id) =>
        await db.Assets.FirstOrDefaultAsync(a => a.Id == id) ?? throw new NotFoundException("Asset not found.");

    internal async Task<Employee> LoadAssignableEmployeeAsync(int employeeId)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId)
                       ?? throw ValidationFailedException.For("employeeId", "Select a valid employee.");
        if (employee.Status != EmployeeStatus.Active)
            throw new BusinessRuleException($"{employee.FullName} is inactive and cannot receive assets.");
        return employee;
    }

    internal async Task<InventoryItem> FindOrCreateItemAsync(AssetType type, int? brandId, string? model, string fallbackName)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.AssetTypeId == type.Id && i.BrandId == brandId && i.Model == model);
        if (item is not null) return item;

        var local = db.InventoryItems.Local.FirstOrDefault(i => i.AssetTypeId == type.Id && i.BrandId == brandId && i.Model == model);
        if (local is not null) return local;

        var brandName = brandId is null ? null : await db.Brands.Where(b => b.Id == brandId).Select(b => b.Name).FirstOrDefaultAsync();
        var name = string.Join(' ', new[] { brandName, model }.Where(s => !string.IsNullOrWhiteSpace(s)));
        item = new InventoryItem
        {
            AssetTypeId = type.Id,
            BrandId = brandId,
            Model = model,
            Name = string.IsNullOrWhiteSpace(name) ? fallbackName : $"{name} {type.Name}".Trim()
        };
        db.InventoryItems.Add(item);
        return item;
    }

    internal async Task<List<string>> NextTagsAsync(AssetType type, int count)
    {
        var prefix = $"{type.TagPrefix}-";
        var existing = await db.Assets.Where(a => a.AssetTag.StartsWith(prefix)).Select(a => a.AssetTag).ToListAsync();
        var max = existing.Select(t => int.TryParse(t[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return Enumerable.Range(max + 1, count).Select(n => $"{prefix}{n:D5}").ToList();
    }

    internal async Task EnsureUniqueSerialAsync(string? serial, int? exceptId)
    {
        if (serial is null) return;
        var upper = serial.ToUpper();
        if (await db.Assets.AnyAsync(a => a.SerialNumber != null && a.SerialNumber.ToUpper() == upper && a.Id != exceptId))
            throw ValidationFailedException.For("serialNumber", $"Serial number '{serial}' is already registered.");
    }

    private async Task EnsureUniqueTagAsync(string tag, int? exceptId)
    {
        if (await db.Assets.AnyAsync(a => a.AssetTag == tag && a.Id != exceptId))
            throw ValidationFailedException.For("assetTag", $"Asset tag '{tag}' is already in use.");
    }

    private async Task ValidateReferencesAsync(SaveAssetRequest request)
    {
        if (request.BrandId is { } brandId && !await db.Brands.AnyAsync(b => b.Id == brandId))
            throw ValidationFailedException.For("brandId", "Select a valid brand.");
        if (request.LocationId is { } locationId && !await db.Locations.AnyAsync(l => l.Id == locationId))
            throw ValidationFailedException.For("locationId", "Select a valid location.");
        if (request.PurchaseDate is { } purchased && purchased > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            throw ValidationFailedException.For("purchaseDate", "Purchase date cannot be in the future.");
        if (request.PurchaseDate is { } p && request.WarrantyExpiry is { } w && w < p)
            throw ValidationFailedException.For("warrantyExpiry", "Warranty expiry cannot be before the purchase date.");
    }

    private static void Apply(Asset asset, SaveAssetRequest request, string? serial)
    {
        asset.Name = request.Name.Trim();
        asset.AssetTypeId = request.AssetTypeId;
        asset.BrandId = request.BrandId;
        asset.Model = Clean(request.Model);
        asset.SerialNumber = serial;
        asset.PurchaseDate = request.PurchaseDate;
        asset.PurchaseCost = request.PurchaseCost;
        asset.WarrantyExpiry = request.WarrantyExpiry;
        asset.Condition = request.Condition;
        asset.LocationId = request.LocationId;
        asset.Notes = Clean(request.Notes);
    }

    private void EnsureCanViewAsset(int id)
    {
        if (!currentUser.Has(Permissions.AssetsView) && currentUser.EmployeeId is null)
            throw new NotFoundException("Asset not found.");
    }

    private async Task<AssetListDto> ProjectAsync(int id) =>
        await db.Assets.AsNoTracking().Where(a => a.Id == id).Select(ToListDto).FirstAsync();

    internal static DateTime ValidatePastOrNow(DateTime? value, string field)
    {
        if (value is null) return DateTime.UtcNow;
        var utc = value.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value.Value.ToUniversalTime();
        if (utc > DateTime.UtcNow.AddMinutes(5)) throw ValidationFailedException.For(field, "Date cannot be in the future.");
        if (utc < new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)) throw ValidationFailedException.For(field, "Date is not valid.");
        return utc;
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Describe(AssetStatus status) => status switch
    {
        AssetStatus.InStock => "in stock",
        AssetStatus.UnderRepair => "under repair",
        _ => status.ToString().ToLowerInvariant()
    };
}
