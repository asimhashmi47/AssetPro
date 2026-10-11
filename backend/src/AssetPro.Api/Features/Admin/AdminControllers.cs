using System.ComponentModel.DataAnnotations;
using System.Reflection;
using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Admin;

public sealed class ActivityLogDto
{
    public int Id { get; init; }
    public DateTime OccurredAt { get; init; }
    public int? UserId { get; init; }
    public string? UserEmail { get; init; }
    public string? UserName { get; init; }
    public string Action { get; init; } = "";
    public string? Entity { get; init; }
    public string? EntityId { get; init; }
    public string Description { get; init; } = "";
    public string? IpAddress { get; init; }
    public ActivityResult Result { get; init; }
}

public sealed class ApplicationLogDto
{
    public int Id { get; init; }
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = "";
    public string Category { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Exception { get; init; }
    public string? TraceId { get; init; }
    public string? RequestPath { get; init; }
}

[ApiController]
[Authorize(Policy = Permissions.ActivityLogsView)]
[Route("api/activity-logs")]
public sealed class ActivityLogsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ActivityLogDto>> List([FromQuery] GridQuery grid, CancellationToken ct) =>
        db.ActivityLogs.AsNoTracking().Select(a => new ActivityLogDto
        {
            Id = a.Id,
            OccurredAt = a.OccurredAt,
            UserId = a.UserId,
            UserEmail = a.UserEmail,
            UserName = db.Users.Where(u => u.Id == a.UserId).Select(u => u.Employee != null ? u.Employee.FullName : u.Email).FirstOrDefault(),
            Action = a.Action,
            Entity = a.Entity,
            EntityId = a.EntityId,
            Description = a.Description,
            IpAddress = a.IpAddress,
            Result = a.Result
        }).ToPagedResultAsync(grid, nameof(ActivityLogDto.OccurredAt), true, ct);

    [HttpGet("application")]
    public Task<PagedResult<ApplicationLogDto>> ApplicationLogs([FromQuery] GridQuery grid, CancellationToken ct) =>
        db.ApplicationLogs.AsNoTracking().Select(l => new ApplicationLogDto
        {
            Id = l.Id,
            Timestamp = l.Timestamp,
            Level = l.Level,
            Category = l.Category,
            Message = l.Message,
            Exception = l.Exception,
            TraceId = l.TraceId,
            RequestPath = l.RequestPath
        }).ToPagedResultAsync(grid, nameof(ApplicationLogDto.Timestamp), true, ct);
}

public sealed record SettingsDto(string CompanyName, string Currency, int DefaultPageSize, int WarrantyAlertDays);

public sealed class UpdateSettingsRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string CompanyName { get; set; } = "";

    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Use a 3-letter currency code such as USD.")]
    public string Currency { get; set; } = "USD";

    [Range(5, 100)]
    public int DefaultPageSize { get; set; } = 10;

    [Range(1, 365)]
    public int WarrantyAlertDays { get; set; } = 30;
}

public sealed record AboutDto(string Product, string Version, string Environment, string Database, int Assets, int Employees, int Users, DateTime ServerTimeUtc);

[ApiController]
[Authorize]
[Route("api/settings")]
public sealed class SettingsController(AppDbContext db, ActivityLogger activity, IWebHostEnvironment env) : ControllerBase
{
    public static readonly Dictionary<string, string> Defaults = new()
    {
        ["CompanyName"] = "AssetPro Demo Co.",
        ["Currency"] = "USD",
        ["DefaultPageSize"] = "10",
        ["WarrantyAlertDays"] = "30"
    };

    [HttpGet]
    public async Task<SettingsDto> Get(CancellationToken ct) => ToDto(await LoadAsync(ct));

    [HttpPut]
    [Authorize(Policy = Permissions.SettingsManage)]
    public async Task<SettingsDto> Update(UpdateSettingsRequest request)
    {
        var values = new Dictionary<string, string>
        {
            ["CompanyName"] = request.CompanyName.Trim(),
            ["Currency"] = request.Currency,
            ["DefaultPageSize"] = request.DefaultPageSize.ToString(),
            ["WarrantyAlertDays"] = request.WarrantyAlertDays.ToString()
        };
        var existing = await db.Settings.ToDictionaryAsync(s => s.Key);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var row)) row.Value = value;
            else db.Settings.Add(new AppSetting { Key = key, Value = value });
        }
        activity.Add(ActivityActions.SettingsUpdated, "Updated application settings", "Settings", null);
        await db.SaveChangesAsync();
        return ToDto(values);
    }

    [HttpGet("about")]
    public async Task<AboutDto> About(CancellationToken ct) => new(
        "AssetPro",
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0",
        env.EnvironmentName,
        "SQLite",
        await db.Assets.CountAsync(ct),
        await db.Employees.CountAsync(ct),
        await db.Users.CountAsync(ct),
        DateTime.UtcNow);

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        var stored = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return Defaults.ToDictionary(d => d.Key, d => stored.GetValueOrDefault(d.Key, d.Value));
    }

    private static SettingsDto ToDto(IReadOnlyDictionary<string, string> v) => new(
        v["CompanyName"], v["Currency"],
        int.TryParse(v["DefaultPageSize"], out var size) ? size : 10,
        int.TryParse(v["WarrantyAlertDays"], out var days) ? days : 30);
}
