using AssetPro.Api.Domain;

namespace AssetPro.Api.Auth;

/// <summary>
/// Permission names. Each one is also an authorization policy name.
/// Administrators implicitly hold every permission; standard users get <see cref="UserDefaults"/>
/// plus whatever an administrator grants them.
/// </summary>
public static class Permissions
{
    public const string DashboardView = "Dashboard.View";
    public const string AssetsView = "Assets.View";
    public const string AssetsCreate = "Assets.Create";
    public const string AssetsEdit = "Assets.Edit";
    public const string AssetsAssign = "Assets.Assign";
    public const string AssetsReturn = "Assets.Return";
    public const string AssetsRepair = "Assets.Repair";
    public const string AssetsScrap = "Assets.Scrap";
    public const string InventoryView = "Inventory.View";
    public const string InventoryStockIn = "Inventory.StockIn";
    public const string InventoryStockOut = "Inventory.StockOut";
    public const string EmployeesView = "Employees.View";
    public const string EmployeesCreate = "Employees.Create";
    public const string EmployeesEdit = "Employees.Edit";
    public const string RequestsView = "Requests.View";
    public const string RequestsCreate = "Requests.Create";
    public const string RequestsApprove = "Requests.Approve";
    public const string RequestsProcess = "Requests.Process";
    public const string RequestsComplete = "Requests.Complete";
    public const string ReportsView = "Reports.View";
    public const string ActivityLogsView = "ActivityLogs.View";
    public const string UsersManage = "Users.Manage";
    public const string SettingsManage = "Settings.Manage";

    public const string ClaimType = "perm";

    public static readonly IReadOnlyList<string> All =
    [
        DashboardView, AssetsView, AssetsCreate, AssetsEdit, AssetsAssign, AssetsReturn, AssetsRepair, AssetsScrap,
        InventoryView, InventoryStockIn, InventoryStockOut, EmployeesView, EmployeesCreate, EmployeesEdit,
        RequestsView, RequestsCreate, RequestsApprove, RequestsProcess, RequestsComplete,
        ReportsView, ActivityLogsView, UsersManage, SettingsManage
    ];

    /// <summary>Every standard user can raise requests. Own profile/assets/requests need no permission.</summary>
    public static readonly IReadOnlyList<string> UserDefaults = [RequestsCreate];

    public static IReadOnlyList<string> Effective(UserRole role, IEnumerable<string> granted) =>
        role == UserRole.Admin
            ? All
            : UserDefaults.Concat(granted).Where(All.Contains).Distinct().OrderBy(p => p).ToList();
}
