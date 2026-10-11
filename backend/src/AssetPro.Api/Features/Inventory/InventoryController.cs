using System.ComponentModel.DataAnnotations;
using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Features.Assets;
using AssetPro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Inventory;

public sealed class InventoryItemDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int AssetTypeId { get; init; }
    public string AssetType { get; init; } = "";
    public int? BrandId { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public int ReorderLevel { get; init; }
    public int Total { get; init; }
    public int InStock { get; init; }
    public int Assigned { get; init; }
    public int UnderRepair { get; init; }
    public int Damaged { get; init; }
    public int Scrapped { get; init; }
    public int Retired { get; init; }
    public bool IsLowStock { get; init; }
}

public sealed class InventoryTransactionDto
{
    public int Id { get; init; }
    public DateTime OccurredAt { get; init; }
    public int InventoryItemId { get; init; }
    public string ItemName { get; init; } = "";
    public string AssetType { get; init; } = "";
    public InventoryTransactionType Type { get; init; }
    public int Quantity { get; init; }
    public int BalanceAfter { get; init; }
    public int? AssetId { get; init; }
    public string? AssetTag { get; init; }
    public int? EmployeeId { get; init; }
    public string? EmployeeName { get; init; }
    public string? PerformedBy { get; init; }
    public string? Notes { get; init; }
}

public sealed class StockInRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Asset type is required.")]
    public int AssetTypeId { get; set; }

    public int? BrandId { get; set; }

    [StringLength(100)]
    public string? Model { get; set; }

    [Required, StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [Range(1, 500, ErrorMessage = "Quantity must be between 1 and 500.")]
    public int Quantity { get; set; } = 1;

    /// <summary>Optional serial numbers, one per unit (may be fewer than quantity).</summary>
    [MaxLength(500)]
    public List<string>? SerialNumbers { get; set; }

    public DateOnly? PurchaseDate { get; set; }

    [Range(0, 100_000_000)]
    public decimal? UnitCost { get; set; }

    public DateOnly? WarrantyExpiry { get; set; }

    [EnumDataType(typeof(AssetCondition))]
    public AssetCondition Condition { get; set; } = AssetCondition.New;

    public int? LocationId { get; set; }

    [Range(0, 100_000)]
    public int? ReorderLevel { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class StockOutRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Item is required.")]
    public int InventoryItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Employee is required.")]
    public int EmployeeId { get; set; }

    [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100.")]
    public int Quantity { get; set; } = 1;

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class UpdateInventoryItemRequest
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [Range(0, 100_000)]
    public int ReorderLevel { get; set; }
}

public sealed record StockMovementResult(int InventoryItemId, int Quantity, IReadOnlyList<string> AssetTags, int InStockAfter);

[ApiController]
[Authorize]
[Route("api/inventory")]
public sealed class InventoryController(AppDbContext db, AssetService assets, ICurrentUser currentUser, ActivityLogger activity, RecordDeletionService deletion) : ControllerBase
{
    private IQueryable<InventoryItemDto> Items() => db.InventoryItems.AsNoTracking().Select(i => new InventoryItemDto
    {
        Id = i.Id,
        Name = i.Name,
        AssetTypeId = i.AssetTypeId,
        AssetType = i.AssetType!.Name,
        BrandId = i.BrandId,
        Brand = i.Brand != null ? i.Brand.Name : null,
        Model = i.Model,
        ReorderLevel = i.ReorderLevel,
        Total = i.Assets.Count(a => a.Status != AssetStatus.Scrapped && a.Status != AssetStatus.Retired),
        InStock = i.Assets.Count(a => a.Status == AssetStatus.InStock),
        Assigned = i.Assets.Count(a => a.Status == AssetStatus.Assigned),
        UnderRepair = i.Assets.Count(a => a.Status == AssetStatus.UnderRepair),
        Damaged = i.Assets.Count(a => a.Status == AssetStatus.Damaged),
        Scrapped = i.Assets.Count(a => a.Status == AssetStatus.Scrapped),
        Retired = i.Assets.Count(a => a.Status == AssetStatus.Retired),
        IsLowStock = i.Assets.Count(a => a.Status == AssetStatus.InStock) <= i.ReorderLevel
    });

    [HttpGet]
    [Authorize(Policy = Permissions.InventoryView)]
    public Task<PagedResult<InventoryItemDto>> List([FromQuery] GridQuery grid, CancellationToken ct) =>
        Items().ToPagedResultAsync(grid, nameof(InventoryItemDto.Name), ct: ct);

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.InventoryView)]
    public async Task<InventoryItemDto> Get(int id, CancellationToken ct) =>
        await Items().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Inventory item not found.");

    [HttpGet("transactions")]
    [Authorize(Policy = Permissions.InventoryView)]
    public Task<PagedResult<InventoryTransactionDto>> Transactions([FromQuery] GridQuery grid, CancellationToken ct) =>
        db.InventoryTransactions.AsNoTracking().Select(t => new InventoryTransactionDto
        {
            Id = t.Id,
            OccurredAt = t.OccurredAt,
            InventoryItemId = t.InventoryItemId,
            ItemName = t.InventoryItem!.Name,
            AssetType = t.InventoryItem.AssetType!.Name,
            Type = t.Type,
            Quantity = t.Quantity,
            BalanceAfter = t.BalanceAfter,
            AssetId = t.AssetId,
            AssetTag = t.Asset != null ? t.Asset.AssetTag : null,
            EmployeeId = t.EmployeeId,
            EmployeeName = t.Employee != null ? t.Employee.FullName : null,
            PerformedBy = t.PerformedBy != null ? (t.PerformedBy.Employee != null ? t.PerformedBy.Employee.FullName : t.PerformedBy.Email) : null,
            Notes = t.Notes
        }).ToPagedResultAsync(grid, nameof(InventoryTransactionDto.OccurredAt), true, ct);

    /// <summary>Receives N units: creates N tracked assets with generated tags and one StockIn transaction.</summary>
    [HttpPost("stock-in")]
    [Authorize(Policy = Permissions.InventoryStockIn)]
    public async Task<StockMovementResult> StockIn(StockInRequest request)
    {
        var type = await db.AssetTypes.FirstOrDefaultAsync(t => t.Id == request.AssetTypeId && t.IsActive)
                   ?? throw ValidationFailedException.For("assetTypeId", "Select a valid asset type.");
        if (request.BrandId is { } brandId && !await db.Brands.AnyAsync(b => b.Id == brandId))
            throw ValidationFailedException.For("brandId", "Select a valid brand.");
        if (request.LocationId is { } locationId && !await db.Locations.AnyAsync(l => l.Id == locationId))
            throw ValidationFailedException.For("locationId", "Select a valid location.");
        if (request.PurchaseDate is { } purchased && purchased > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            throw ValidationFailedException.For("purchaseDate", "Purchase date cannot be in the future.");

        var serials = (request.SerialNumbers ?? []).Select(AssetService.Clean).Where(s => s is not null).Cast<string>().ToList();
        if (serials.Count > request.Quantity)
            throw ValidationFailedException.For("serialNumbers", "There are more serial numbers than units.");
        if (serials.Count != serials.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw ValidationFailedException.For("serialNumbers", "Serial numbers must be unique.");
        foreach (var serial in serials) await assets.EnsureUniqueSerialAsync(serial, null);

        await using var tx = await db.Database.BeginTransactionAsync();
        var item = await assets.FindOrCreateItemAsync(type, request.BrandId, AssetService.Clean(request.Model), request.Name.Trim());
        if (request.ReorderLevel is { } reorder) item.ReorderLevel = reorder;
        if (item.Id == 0) await db.SaveChangesAsync();

        var tags = await assets.NextTagsAsync(type, request.Quantity);
        var balanceBefore = await assets.InStockCountAsync(item.Id);
        var created = new List<Asset>();
        for (var i = 0; i < request.Quantity; i++)
        {
            var asset = new Asset
            {
                AssetTag = tags[i],
                Name = request.Name.Trim(),
                AssetTypeId = type.Id,
                BrandId = request.BrandId,
                Model = AssetService.Clean(request.Model),
                SerialNumber = i < serials.Count ? serials[i] : null,
                PurchaseDate = request.PurchaseDate,
                PurchaseCost = request.UnitCost,
                WarrantyExpiry = request.WarrantyExpiry,
                Condition = request.Condition,
                Status = AssetStatus.InStock,
                LocationId = request.LocationId,
                Notes = AssetService.Clean(request.Notes),
                InventoryItemId = item.Id
            };
            db.Assets.Add(asset);
            created.Add(asset);
        }
        await db.SaveChangesAsync();

        foreach (var asset in created)
            assets.AddHistory(asset, AssetAction.StockIn, null, AssetStatus.InStock, null, AssetService.Clean(request.Notes) ?? "Received into inventory");

        db.InventoryTransactions.Add(new InventoryTransaction
        {
            InventoryItemId = item.Id, Type = InventoryTransactionType.StockIn, Quantity = request.Quantity,
            BalanceAfter = balanceBefore + request.Quantity, Notes = AssetService.Clean(request.Notes) ?? $"{tags[0]} – {tags[^1]}",
            PerformedById = currentUser.UserId, OccurredAt = DateTime.UtcNow
        });
        activity.Add(ActivityActions.InventoryAdded, $"Stocked in {request.Quantity} × {item.Name}", "InventoryItem", item.Id);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new StockMovementResult(item.Id, request.Quantity, tags, balanceBefore + request.Quantity);
    }

    /// <summary>Issues N in-stock units of an item to an employee. Fails without changes if not enough stock.</summary>
    [HttpPost("stock-out")]
    [Authorize(Policy = Permissions.InventoryStockOut)]
    public async Task<StockMovementResult> StockOut(StockOutRequest request)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == request.InventoryItemId)
                   ?? throw ValidationFailedException.For("inventoryItemId", "Select a valid item.");
        var employee = await assets.LoadAssignableEmployeeAsync(request.EmployeeId);

        await using var tx = await db.Database.BeginTransactionAsync();
        var units = await db.Assets.Where(a => a.InventoryItemId == item.Id && a.Status == AssetStatus.InStock)
            .OrderBy(a => a.AssetTag).Take(request.Quantity).ToListAsync();
        if (units.Count < request.Quantity)
            throw new BusinessRuleException($"Only {units.Count} unit(s) of {item.Name} are in stock; cannot issue {request.Quantity}.");

        var balanceBefore = await assets.InStockCountAsync(item.Id);
        foreach (var unit in units)
            assets.AssignCore(unit, employee, DateTime.UtcNow, null, request.Notes);

        db.InventoryTransactions.Add(new InventoryTransaction
        {
            InventoryItemId = item.Id, EmployeeId = employee.Id, AssetId = units.Count == 1 ? units[0].Id : null,
            Type = InventoryTransactionType.StockOut, Quantity = request.Quantity, BalanceAfter = balanceBefore - request.Quantity,
            Notes = AssetService.Clean(request.Notes) ?? $"Issued to {employee.FullName}: {string.Join(", ", units.Select(u => u.AssetTag))}",
            PerformedById = currentUser.UserId, OccurredAt = DateTime.UtcNow
        });
        activity.Add(ActivityActions.AssetAssigned, $"Issued {request.Quantity} × {item.Name} to {employee.FullName}", "InventoryItem", item.Id);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new StockMovementResult(item.Id, request.Quantity, units.Select(u => u.AssetTag).ToList(), balanceBefore - request.Quantity);
    }

    /// <summary>Permanently removes the record. Administrators only.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await deletion.DeleteInventoryItemAsync(id, ct);
        return NoContent();
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.InventoryStockIn)]
    public async Task<InventoryItemDto> Update(int id, UpdateInventoryItemRequest request)
    {
        var item = await db.InventoryItems.FindAsync(id) ?? throw new NotFoundException("Inventory item not found.");
        item.Name = request.Name.Trim();
        item.ReorderLevel = request.ReorderLevel;
        activity.Add(ActivityActions.InventoryAdjusted, $"Updated item {item.Name} (reorder level {item.ReorderLevel})", "InventoryItem", id);
        await db.SaveChangesAsync();
        return await Items().FirstAsync(i => i.Id == id);
    }
}
