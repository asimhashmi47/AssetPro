namespace AssetPro.Api.Domain;

public abstract class AuditableEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Simple named lookup (Department, Designation, Location, Brand, AssetType).</summary>
public abstract class LookupEntity : AuditableEntity
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Department : LookupEntity;
public class Designation : LookupEntity;
public class Location : LookupEntity;
public class Brand : LookupEntity;

public class AssetType : LookupEntity
{
    /// <summary>Short prefix used when generating asset tags, e.g. LAP.</summary>
    public string TagPrefix { get; set; } = "AST";
}

public class User : AuditableEntity
{
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsActive { get; set; } = true;
    /// <summary>Rotated whenever role, permissions, password or status change; invalidates old JWTs.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndsAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public List<UserPermission> Permissions { get; set; } = [];
}

public class UserPermission
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Permission { get; set; } = "";
}

public class Employee : AuditableEntity
{
    public string EmployeeCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string? ContactNumber { get; set; }
    public string? CompanyName { get; set; }
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? DesignationId { get; set; }
    public Designation? Designation { get; set; }

    public User? User { get; set; }
}

/// <summary>
/// A stock-keeping item (type + brand + model). Individual assets are units of an item,
/// so inventory quantities are always derived from real asset records and can never drift or go negative.
/// </summary>
public class InventoryItem : AuditableEntity
{
    public string Name { get; set; } = "";
    public int AssetTypeId { get; set; }
    public AssetType? AssetType { get; set; }
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public string? Model { get; set; }
    public int ReorderLevel { get; set; }

    public List<Asset> Assets { get; set; } = [];
}

public class Asset : AuditableEntity
{
    public string AssetTag { get; set; } = "";
    public string Name { get; set; } = "";
    public int AssetTypeId { get; set; }
    public AssetType? AssetType { get; set; }
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public DateOnly? WarrantyExpiry { get; set; }
    public AssetCondition Condition { get; set; } = AssetCondition.New;
    public AssetStatus Status { get; set; } = AssetStatus.InStock;
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public string? Notes { get; set; }

    public int InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public List<AssetAssignment> Assignments { get; set; } = [];
}

public class AssetAssignment
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int AssignedById { get; set; }
    public User? AssignedBy { get; set; }
    public DateTime AssignedAt { get; set; }
    public AssetCondition ConditionAtAssignment { get; set; }
    public string? Notes { get; set; }

    // Return details - the assignment row itself is never deleted.
    public DateTime? ReturnedAt { get; set; }
    public string? ReturnedBy { get; set; }
    public int? ReceivedById { get; set; }
    public User? ReceivedBy { get; set; }
    public AssetCondition? ReturnCondition { get; set; }
    public string? DamageDetails { get; set; }
    public string? ReturnNotes { get; set; }
}

public class AssetHistory
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public AssetAction Action { get; set; }
    public AssetStatus? PreviousStatus { get; set; }
    public AssetStatus? NewStatus { get; set; }
    public int? PerformedById { get; set; }
    public User? PerformedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime OccurredAt { get; set; }
}

public class ComponentReplacement
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    public string Component { get; set; } = "";
    public string? OldComponent { get; set; }
    public string NewComponent { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateOnly ReplacedOn { get; set; }
    public int PerformedById { get; set; }
    public User? PerformedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ScrapRecord
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    public DateOnly ScrapDate { get; set; }
    public string Reason { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string? Notes { get; set; }
    public int PerformedById { get; set; }
    public User? PerformedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InventoryTransaction
{
    public int Id { get; set; }
    public int InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public InventoryTransactionType Type { get; set; }
    public int Quantity { get; set; }
    /// <summary>Units in stock for the item right after this transaction.</summary>
    public int BalanceAfter { get; set; }
    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string? Notes { get; set; }
    public int? PerformedById { get; set; }
    public User? PerformedBy { get; set; }
    public DateTime OccurredAt { get; set; }
}

public class AssetRequest : AuditableEntity
{
    public string RequestNumber { get; set; } = "";
    public RequestType Type { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Submitted;
    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }
    public int RequesterEmployeeId { get; set; }
    public Employee? RequesterEmployee { get; set; }
    public int RequesterUserId { get; set; }
    public User? RequesterUser { get; set; }
    public string Comments { get; set; } = "";

    public List<RequestStatusHistory> StatusHistory { get; set; } = [];
}

public class RequestStatusHistory
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public RequestStatus? FromStatus { get; set; }
    public RequestStatus ToStatus { get; set; }
    public int ChangedById { get; set; }
    public User? ChangedBy { get; set; }
    public string? Note { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class ActivityLog
{
    public int Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public int? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string Action { get; set; } = "";
    public string? Entity { get; set; }
    public string? EntityId { get; set; }
    public string Description { get; set; } = "";
    public string? IpAddress { get; set; }
    public ActivityResult Result { get; set; } = ActivityResult.Success;
}

public class ApplicationLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Exception { get; set; }
    public string? TraceId { get; set; }
    public string? RequestPath { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
