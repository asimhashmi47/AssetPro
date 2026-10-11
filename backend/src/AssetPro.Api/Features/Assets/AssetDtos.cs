using System.ComponentModel.DataAnnotations;
using AssetPro.Api.Domain;

namespace AssetPro.Api.Features.Assets;

public sealed class AssetListDto
{
    public int Id { get; init; }
    public string AssetTag { get; init; } = "";
    public string Name { get; init; } = "";
    public int AssetTypeId { get; init; }
    public string AssetType { get; init; } = "";
    public int? BrandId { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchaseCost { get; init; }
    public DateOnly? WarrantyExpiry { get; init; }
    public AssetCondition Condition { get; init; }
    public AssetStatus Status { get; init; }
    public int? LocationId { get; init; }
    public string? Location { get; init; }
    public int? AssignedToEmployeeId { get; init; }
    public string? AssignedTo { get; init; }
    public DateTime? AssignedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record CurrentAssignmentDto(
    int Id, int EmployeeId, string EmployeeName, string EmployeeCode, DateTime AssignedAt,
    string AssignedBy, AssetCondition ConditionAtAssignment, string? Notes);

public sealed record ComponentReplacementDto(
    int Id, string Component, string? OldComponent, string NewComponent, string Reason,
    DateOnly ReplacedOn, string PerformedBy, string? Notes);

public sealed record ScrapRecordDto(int Id, DateOnly ScrapDate, string Reason, int Quantity, string? Notes, string PerformedBy);

public sealed record AssignmentRecordDto(
    int Id, int EmployeeId, string EmployeeName, DateTime AssignedAt, DateTime? ReturnedAt,
    AssetCondition ConditionAtAssignment, AssetCondition? ReturnCondition, string? DamageDetails);

public sealed class AssetDetailDto
{
    public required AssetListDto Asset { get; init; }
    public string? Notes { get; init; }
    public int InventoryItemId { get; init; }
    public CurrentAssignmentDto? CurrentAssignment { get; init; }
    public IReadOnlyList<AssignmentRecordDto> Assignments { get; init; } = [];
    public IReadOnlyList<ComponentReplacementDto> Replacements { get; init; } = [];
    public ScrapRecordDto? Scrap { get; init; }
}

public sealed class AssetHistoryDto
{
    public int Id { get; init; }
    public DateTime OccurredAt { get; init; }
    public int AssetId { get; init; }
    public string AssetTag { get; init; } = "";
    public string AssetName { get; init; } = "";
    public string AssetType { get; init; } = "";
    public int? EmployeeId { get; init; }
    public string? EmployeeName { get; init; }
    public AssetAction Action { get; init; }
    public AssetStatus? PreviousStatus { get; init; }
    public AssetStatus? NewStatus { get; init; }
    public string? PerformedBy { get; init; }
    public string? Notes { get; init; }
}

public sealed class SaveAssetRequest
{
    /// <summary>Optional; generated from the asset type prefix when empty.</summary>
    [StringLength(30), RegularExpression(@"^[A-Za-z0-9\-_/]*$", ErrorMessage = "Asset tag may contain letters, digits, - _ /.")]
    public string? AssetTag { get; set; }

    [Required, StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Asset type is required.")]
    public int AssetTypeId { get; set; }

    public int? BrandId { get; set; }

    [StringLength(100)]
    public string? Model { get; set; }

    [StringLength(100)]
    public string? SerialNumber { get; set; }

    public DateOnly? PurchaseDate { get; set; }

    [Range(0, 100_000_000)]
    public decimal? PurchaseCost { get; set; }

    public DateOnly? WarrantyExpiry { get; set; }

    [EnumDataType(typeof(AssetCondition))]
    public AssetCondition Condition { get; set; } = AssetCondition.New;

    public int? LocationId { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class AssignAssetRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Employee is required.")]
    public int EmployeeId { get; set; }

    public DateTime? AssignedAt { get; set; }

    [EnumDataType(typeof(AssetCondition))]
    public AssetCondition? Condition { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class ReturnAssetRequest
{
    public DateTime? ReturnedAt { get; set; }

    /// <summary>Name of the person physically handing the asset back (defaults to the assignee).</summary>
    [StringLength(150)]
    public string? ReturnedBy { get; set; }

    [EnumDataType(typeof(AssetCondition))]
    public AssetCondition Condition { get; set; } = AssetCondition.Good;

    [StringLength(2000)]
    public string? DamageDetails { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    /// <summary>InStock, UnderRepair, Damaged or Scrapped.</summary>
    [EnumDataType(typeof(AssetStatus))]
    public AssetStatus ResultingStatus { get; set; } = AssetStatus.InStock;

    [StringLength(500)]
    public string? ScrapReason { get; set; }
}

public enum RepairAction
{
    Start,
    Complete
}

public sealed class RepairAssetRequest
{
    [EnumDataType(typeof(RepairAction))]
    public RepairAction Action { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    [EnumDataType(typeof(AssetCondition))]
    public AssetCondition? ResultingCondition { get; set; }
}

public sealed class ReplaceComponentRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Component { get; set; } = "";

    [StringLength(150)]
    public string? OldComponent { get; set; }

    [Required, StringLength(150, MinimumLength = 1)]
    public string NewComponent { get; set; } = "";

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = "";

    public DateOnly? ReplacedOn { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class ScrapAssetRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = "";

    public DateOnly? ScrapDate { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class ChangeAssetStatusRequest
{
    /// <summary>Damaged, Retired or InStock (release a damaged asset back to stock).</summary>
    [EnumDataType(typeof(AssetStatus))]
    public AssetStatus Status { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}
