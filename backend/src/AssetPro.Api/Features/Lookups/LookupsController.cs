using System.ComponentModel.DataAnnotations;
using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Lookups;

public sealed class LookupDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsActive { get; init; }
    public string? TagPrefix { get; init; }
    public int UsageCount { get; init; }
}

public sealed class SaveLookupRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = "";

    [StringLength(6), RegularExpression("^[A-Za-z0-9]{2,6}$", ErrorMessage = "Tag prefix must be 2-6 letters or digits.")]
    public string? TagPrefix { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Master data: departments, designations, locations, brands, asset-types.
/// Anyone signed in can read; users who can create employees/assets may add values inline (smart dropdowns);
/// editing and deleting requires Settings.Manage.
/// </summary>
[ApiController]
[Authorize]
[Route("api/lookups/{kind}")]
public sealed class LookupsController(AppDbContext db, ICurrentUser currentUser, ActivityLogger activity) : ControllerBase
{
    private static readonly Dictionary<string, (string[] CreatePermissions, string Label)> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["departments"] = ([Permissions.EmployeesCreate, Permissions.EmployeesEdit], "Department"),
        ["designations"] = ([Permissions.EmployeesCreate, Permissions.EmployeesEdit], "Designation"),
        ["locations"] = ([Permissions.AssetsCreate, Permissions.AssetsEdit, Permissions.InventoryStockIn], "Location"),
        ["brands"] = ([Permissions.AssetsCreate, Permissions.AssetsEdit, Permissions.InventoryStockIn], "Brand"),
        ["asset-types"] = ([Permissions.AssetsCreate, Permissions.AssetsEdit, Permissions.InventoryStockIn], "Asset type")
    };

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LookupDto>>> List(string kind, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        EnsureKind(kind);
        var items = await QueryWithUsage(kind)
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<LookupDto>> Create(string kind, SaveLookupRequest request)
    {
        var (createPermissions, label) = EnsureKind(kind);
        if (!createPermissions.Any(currentUser.Has) && !currentUser.Has(Permissions.SettingsManage)) throw new ForbiddenException();

        var name = request.Name.Trim();
        await EnsureUniqueAsync(kind, name, null);

        LookupEntity entity = kind.ToLowerInvariant() switch
        {
            "departments" => new Department(),
            "designations" => new Designation(),
            "locations" => new Location(),
            "brands" => new Brand(),
            _ => new AssetType { TagPrefix = NormalizePrefix(request.TagPrefix, name) }
        };
        entity.Name = name;
        entity.IsActive = true;
        db.Add(entity);
        activity.Add(ActivityActions.MasterDataChanged, $"{label} '{name}' added", label, null);
        await db.SaveChangesAsync();

        return Ok(new LookupDto { Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive, TagPrefix = (entity as AssetType)?.TagPrefix, UsageCount = 0 });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.SettingsManage)]
    public async Task<ActionResult<LookupDto>> Update(string kind, int id, SaveLookupRequest request)
    {
        var (_, label) = EnsureKind(kind);
        var entity = await FindAsync(kind, id);
        var name = request.Name.Trim();
        await EnsureUniqueAsync(kind, name, id);

        entity.Name = name;
        entity.IsActive = request.IsActive;
        if (entity is AssetType type && !string.IsNullOrWhiteSpace(request.TagPrefix))
            type.TagPrefix = NormalizePrefix(request.TagPrefix, name);

        activity.Add(ActivityActions.MasterDataChanged, $"{label} '{name}' updated", label, id);
        await db.SaveChangesAsync();
        return Ok(new LookupDto { Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive, TagPrefix = (entity as AssetType)?.TagPrefix, UsageCount = 0 });
    }

    /// <summary>Deletes an unused value. Values that are referenced must be deactivated instead, preserving history.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permissions.SettingsManage)]
    public async Task<IActionResult> Delete(string kind, int id)
    {
        var (_, label) = EnsureKind(kind);
        var entity = await FindAsync(kind, id);
        var usage = await QueryWithUsage(kind).Where(x => x.Id == id).Select(x => x.UsageCount).FirstAsync();
        if (usage > 0)
            throw new BusinessRuleException($"{label} '{entity.Name}' is used by {usage} record(s). Deactivate it instead of deleting.");

        db.Remove(entity);
        activity.Add(ActivityActions.MasterDataChanged, $"{label} '{entity.Name}' deleted", label, id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static (string[] CreatePermissions, string Label) EnsureKind(string kind) =>
        Kinds.TryGetValue(kind, out var info) ? info : throw new NotFoundException("Unknown master data type.");

    private IQueryable<LookupDto> QueryWithUsage(string kind) => kind.ToLowerInvariant() switch
    {
        "departments" => db.Departments.AsNoTracking().Select(x => new LookupDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive, TagPrefix = null, UsageCount = db.Employees.Count(e => e.DepartmentId == x.Id) }),
        "designations" => db.Designations.AsNoTracking().Select(x => new LookupDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive, TagPrefix = null, UsageCount = db.Employees.Count(e => e.DesignationId == x.Id) }),
        "locations" => db.Locations.AsNoTracking().Select(x => new LookupDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive, TagPrefix = null, UsageCount = db.Assets.Count(a => a.LocationId == x.Id) }),
        "brands" => db.Brands.AsNoTracking().Select(x => new LookupDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive, TagPrefix = null, UsageCount = db.Assets.Count(a => a.BrandId == x.Id) + db.InventoryItems.Count(i => i.BrandId == x.Id) }),
        _ => db.AssetTypes.AsNoTracking().Select(x => new LookupDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive, TagPrefix = x.TagPrefix, UsageCount = db.Assets.Count(a => a.AssetTypeId == x.Id) + db.InventoryItems.Count(i => i.AssetTypeId == x.Id) })
    };

    private async Task<LookupEntity> FindAsync(string kind, int id)
    {
        LookupEntity? entity = kind.ToLowerInvariant() switch
        {
            "departments" => await db.Departments.FindAsync(id),
            "designations" => await db.Designations.FindAsync(id),
            "locations" => await db.Locations.FindAsync(id),
            "brands" => await db.Brands.FindAsync(id),
            _ => await db.AssetTypes.FindAsync(id)
        };
        return entity ?? throw new NotFoundException("Record not found.");
    }

    private async Task EnsureUniqueAsync(string kind, string name, int? exceptId)
    {
        var lower = name.ToLower();
        var exists = await QueryWithUsage(kind).AnyAsync(x => x.Name.ToLower() == lower && x.Id != exceptId);
        if (exists) throw ValidationFailedException.For("name", $"'{name}' already exists.");
    }

    private static string NormalizePrefix(string? prefix, string name)
    {
        if (!string.IsNullOrWhiteSpace(prefix)) return prefix.Trim().ToUpperInvariant();
        var letters = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return letters.Length >= 3 ? letters[..3] : (letters + "AST")[..3];
    }
}
