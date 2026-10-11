namespace AssetPro.Api.Domain;

public enum UserRole
{
    Admin,
    User
}

public enum EmployeeStatus
{
    Active,
    Inactive
}

public enum AssetStatus
{
    InStock,
    Assigned,
    UnderRepair,
    Damaged,
    Scrapped,
    Retired
}

public enum AssetCondition
{
    New,
    Good,
    Fair,
    Damaged,
    Critical
}

public enum AssetAction
{
    Created,
    StockIn,
    Assigned,
    Returned,
    RepairRequested,
    RepairStarted,
    RepairCompleted,
    ComponentReplaced,
    Damaged,
    Scrapped,
    Retired,
    StatusChanged,
    Updated
}

public enum InventoryTransactionType
{
    StockIn,
    StockOut,
    Return,
    Scrap,
    Adjustment
}

public enum RequestType
{
    Repair,
    Replacement,
    NewAsset,
    Return,
    Other
}

public enum RequestStatus
{
    Submitted,
    Approved,
    Rejected,
    InProgress,
    Delivered,
    Checking,
    Received,
    Done
}

public enum ActivityResult
{
    Success,
    Failure
}
