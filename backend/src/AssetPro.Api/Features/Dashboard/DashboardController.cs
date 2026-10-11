using System.Globalization;
using AssetPro.Api.Auth;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Features.Assets;
using AssetPro.Api.Features.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Dashboard;

public sealed record NamedCount(string Name, int Count);

public sealed record MonthlyPoint(string Month, int Primary, int Secondary);

public sealed record LowStockItem(int Id, string Name, string AssetType, int InStock, int ReorderLevel);

public sealed class DashboardDto
{
    public int TotalAssets { get; init; }
    public int AssignedAssets { get; init; }
    public int AvailableAssets { get; init; }
    public int UnderRepair { get; init; }
    public int Damaged { get; init; }
    public int Scrapped { get; init; }
    public int TotalEmployees { get; init; }
    public int PendingRequests { get; init; }
    public int OpenRequests { get; init; }
    public int WarrantyExpiringSoon { get; init; }
    public decimal PortfolioValue { get; init; }
    public IReadOnlyList<NamedCount> StatusBreakdown { get; init; } = [];
    public IReadOnlyList<NamedCount> TypeBreakdown { get; init; } = [];
    public IReadOnlyList<NamedCount> RequestStatusBreakdown { get; init; } = [];
    /// <summary>Primary = requests created, Secondary = requests completed.</summary>
    public IReadOnlyList<MonthlyPoint> RequestTrend { get; init; } = [];
    /// <summary>Primary = assignments, Secondary = returns.</summary>
    public IReadOnlyList<MonthlyPoint> AssignmentTrend { get; init; } = [];
    public IReadOnlyList<AssetHistoryDto> RecentActivity { get; init; } = [];
    public IReadOnlyList<RequestListDto> RecentRequests { get; init; } = [];
    public IReadOnlyList<LowStockItem> LowStock { get; init; } = [];
}

[ApiController]
[Authorize(Policy = Permissions.DashboardView)]
[Route("api/dashboard")]
public sealed class DashboardController(AppDbContext db) : ControllerBase
{
    private const int TrendMonths = 6;

    [HttpGet]
    public async Task<DashboardDto> Get(CancellationToken ct)
    {
        var statusCounts = await db.Assets.AsNoTracking().GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(AssetStatus s) => statusCounts.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var typeCounts = await db.Assets.AsNoTracking()
            .Where(a => a.Status != AssetStatus.Scrapped && a.Status != AssetStatus.Retired)
            .GroupBy(a => a.AssetType!.Name)
            .Select(g => new NamedCount(g.Key, g.Count()))
            .ToListAsync(ct);

        var requestCounts = await db.Requests.AsNoTracking().GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var warrantySoon = await db.Assets.CountAsync(a => a.WarrantyExpiry != null && a.WarrantyExpiry >= today
            && a.WarrantyExpiry <= today.AddDays(30) && a.Status != AssetStatus.Scrapped && a.Status != AssetStatus.Retired, ct);

        // SQLite stores decimals as REAL here, so summing on the server is fine.
        var portfolio = await db.Assets.Where(a => a.Status != AssetStatus.Scrapped && a.Status != AssetStatus.Retired && a.PurchaseCost != null)
            .Select(a => a.PurchaseCost!.Value).ToListAsync(ct);

        var since = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(TrendMonths - 1));
        var requestCreated = await db.Requests.Where(r => r.CreatedAt >= since).Select(r => r.CreatedAt).ToListAsync(ct);
        var requestDone = await db.RequestStatusHistory.Where(h => h.ToStatus == RequestStatus.Done && h.ChangedAt >= since)
            .Select(h => h.ChangedAt).ToListAsync(ct);
        var assigned = await db.AssetAssignments.Where(a => a.AssignedAt >= since).Select(a => a.AssignedAt).ToListAsync(ct);
        var returned = await db.AssetAssignments.Where(a => a.ReturnedAt >= since).Select(a => a.ReturnedAt!.Value).ToListAsync(ct);

        var recent = await db.AssetHistory.AsNoTracking().OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id).Take(8)
            .Select(AssetService.ToHistoryDto).ToListAsync(ct);
        var recentRequests = await db.Requests.AsNoTracking().OrderByDescending(r => r.UpdatedAt).Take(6)
            .Select(RequestService.ToListDto).ToListAsync(ct);

        var lowStock = await db.InventoryItems.AsNoTracking()
            .Where(i => i.Assets.Count(a => a.Status == AssetStatus.InStock) <= i.ReorderLevel)
            .OrderBy(i => i.Assets.Count(a => a.Status == AssetStatus.InStock)).ThenBy(i => i.Name).Take(6)
            .Select(i => new LowStockItem(i.Id, i.Name, i.AssetType!.Name, i.Assets.Count(a => a.Status == AssetStatus.InStock), i.ReorderLevel))
            .ToListAsync(ct);

        return new DashboardDto
        {
            TotalAssets = statusCounts.Where(x => x.Status is not (AssetStatus.Scrapped or AssetStatus.Retired)).Sum(x => x.Count),
            AssignedAssets = Count(AssetStatus.Assigned),
            AvailableAssets = Count(AssetStatus.InStock),
            UnderRepair = Count(AssetStatus.UnderRepair),
            Damaged = Count(AssetStatus.Damaged),
            Scrapped = Count(AssetStatus.Scrapped),
            TotalEmployees = await db.Employees.CountAsync(e => e.Status == EmployeeStatus.Active, ct),
            PendingRequests = requestCounts.Where(x => x.Status == RequestStatus.Submitted).Sum(x => x.Count),
            OpenRequests = requestCounts.Where(x => x.Status is not (RequestStatus.Done or RequestStatus.Rejected)).Sum(x => x.Count),
            WarrantyExpiringSoon = warrantySoon,
            PortfolioValue = portfolio.Sum(),
            StatusBreakdown = Enum.GetValues<AssetStatus>().Select(s => new NamedCount(s.ToString(), Count(s))).ToList(),
            TypeBreakdown = typeCounts.OrderByDescending(t => t.Count).ThenBy(t => t.Name).ToList(),
            RequestStatusBreakdown = Enum.GetValues<RequestStatus>()
                .Select(s => new NamedCount(s.ToString(), requestCounts.FirstOrDefault(x => x.Status == s)?.Count ?? 0)).ToList(),
            RequestTrend = Monthly(since, requestCreated, requestDone),
            AssignmentTrend = Monthly(since, assigned, returned),
            RecentActivity = recent,
            RecentRequests = recentRequests,
            LowStock = lowStock
        };
    }

    private static List<MonthlyPoint> Monthly(DateTime since, List<DateTime> primary, List<DateTime> secondary) =>
        Enumerable.Range(0, TrendMonths).Select(i =>
        {
            var month = since.AddMonths(i);
            bool InMonth(DateTime d) => d.Year == month.Year && d.Month == month.Month;
            return new MonthlyPoint(month.ToString("MMM yyyy", CultureInfo.InvariantCulture), primary.Count(InMonth), secondary.Count(InMonth));
        }).ToList();
}
