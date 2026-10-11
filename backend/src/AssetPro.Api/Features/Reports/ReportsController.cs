using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Features.Assets;
using AssetPro.Api.Features.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Reports;

public sealed class EmployeeAssetReportDto
{
    public int Id { get; init; }
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string? Department { get; init; }
    public int AssetId { get; init; }
    public string AssetTag { get; init; } = "";
    public string AssetName { get; init; } = "";
    public string AssetType { get; init; } = "";
    public DateTime AssignedAt { get; init; }
    public DateTime? ReturnedAt { get; init; }
    public bool IsReturned { get; init; }
    public AssetCondition ConditionAtAssignment { get; init; }
    public AssetCondition? ReturnCondition { get; init; }
    public string AssignedBy { get; init; } = "";
}

public sealed class ScrapReportDto
{
    public int Id { get; init; }
    public DateOnly ScrapDate { get; init; }
    public int AssetId { get; init; }
    public string AssetTag { get; init; } = "";
    public string AssetName { get; init; } = "";
    public string AssetType { get; init; } = "";
    public string Reason { get; init; } = "";
    public int Quantity { get; init; }
    public decimal? PurchaseCost { get; init; }
    public string PerformedBy { get; init; } = "";
    public string? Notes { get; init; }
}

/// <summary>Read-only reports. All support the same paging/sort/filter/search parameters as the tables, plus CSV export client-side.</summary>
[ApiController]
[Authorize(Policy = Permissions.ReportsView)]
[Route("api/reports")]
public sealed class ReportsController(AppDbContext db) : ControllerBase
{
    [HttpGet("assets")]
    public Task<PagedResult<AssetListDto>> Assets([FromQuery] GridQuery grid, CancellationToken ct) =>
        db.Assets.AsNoTracking().Select(AssetService.ToListDto).ToPagedResultAsync(grid, nameof(AssetListDto.AssetTag), ct: ct);

    [HttpGet("employee-assets")]
    public Task<PagedResult<EmployeeAssetReportDto>> EmployeeAssets([FromQuery] GridQuery grid, CancellationToken ct) =>
        db.AssetAssignments.AsNoTracking().Select(x => new EmployeeAssetReportDto
        {
            Id = x.Id,
            EmployeeId = x.EmployeeId,
            EmployeeCode = x.Employee!.EmployeeCode,
            EmployeeName = x.Employee.FullName,
            Department = x.Employee.Department != null ? x.Employee.Department.Name : null,
            AssetId = x.AssetId,
            AssetTag = x.Asset!.AssetTag,
            AssetName = x.Asset.Name,
            AssetType = x.Asset.AssetType!.Name,
            AssignedAt = x.AssignedAt,
            ReturnedAt = x.ReturnedAt,
            IsReturned = x.ReturnedAt != null,
            ConditionAtAssignment = x.ConditionAtAssignment,
            ReturnCondition = x.ReturnCondition,
            AssignedBy = x.AssignedBy!.Employee != null ? x.AssignedBy.Employee.FullName : x.AssignedBy.Email
        }).ToPagedResultAsync(grid, nameof(EmployeeAssetReportDto.AssignedAt), true, ct);

    [HttpGet("history")]
    public Task<PagedResult<AssetHistoryDto>> History([FromQuery] GridQuery grid, CancellationToken ct) =>
        db.AssetHistory.AsNoTracking().Select(AssetService.ToHistoryDto).ToPagedResultAsync(grid, nameof(AssetHistoryDto.OccurredAt), true, ct);

    [HttpGet("requests")]
    public Task<PagedResult<RequestListDto>> Requests([FromQuery] GridQuery grid, CancellationToken ct) =>
        db.Requests.AsNoTracking().Select(RequestService.ToListDto).ToPagedResultAsync(grid, nameof(RequestListDto.CreatedAt), true, ct);

    [HttpGet("scrap")]
    public Task<PagedResult<ScrapReportDto>> Scrap([FromQuery] GridQuery grid, CancellationToken ct) =>
        db.ScrapRecords.AsNoTracking().Select(s => new ScrapReportDto
        {
            Id = s.Id,
            ScrapDate = s.ScrapDate,
            AssetId = s.AssetId,
            AssetTag = s.Asset!.AssetTag,
            AssetName = s.Asset.Name,
            AssetType = s.Asset.AssetType!.Name,
            Reason = s.Reason,
            Quantity = s.Quantity,
            PurchaseCost = s.Asset.PurchaseCost,
            PerformedBy = s.PerformedBy!.Employee != null ? s.PerformedBy.Employee.FullName : s.PerformedBy.Email,
            Notes = s.Notes
        }).ToPagedResultAsync(grid, nameof(ScrapReportDto.ScrapDate), true, ct);

    [HttpGet("repairs")]
    public Task<PagedResult<AssetHistoryDto>> Repairs([FromQuery] GridQuery grid, CancellationToken ct) =>
        db.AssetHistory.AsNoTracking().Where(h => h.Action == AssetAction.RepairRequested || h.Action == AssetAction.RepairStarted
                        || h.Action == AssetAction.RepairCompleted || h.Action == AssetAction.ComponentReplaced)
            .Select(AssetService.ToHistoryDto).ToPagedResultAsync(grid, nameof(AssetHistoryDto.OccurredAt), true, ct);
}
