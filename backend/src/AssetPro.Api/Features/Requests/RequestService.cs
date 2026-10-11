using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Features.Assets;
using AssetPro.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Requests;

public sealed class RequestListDto
{
    public int Id { get; init; }
    public string RequestNumber { get; init; } = "";
    public RequestType Type { get; init; }
    public RequestStatus Status { get; init; }
    public int? AssetId { get; init; }
    public string? AssetTag { get; init; }
    public string? AssetName { get; init; }
    public int RequesterEmployeeId { get; init; }
    public string RequesterName { get; init; } = "";
    public string? Department { get; init; }
    public string Comments { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record RequestStatusHistoryDto(int Id, RequestStatus? FromStatus, RequestStatus ToStatus, string ChangedBy, string? Note, DateTime ChangedAt);

public sealed class RequestDetailDto
{
    public required RequestListDto Request { get; init; }
    public IReadOnlyList<RequestStatusHistoryDto> History { get; init; } = [];
    /// <summary>Workflow actions the current user may perform right now (e.g. "approve", "check").</summary>
    public IReadOnlyList<string> AllowedActions { get; init; } = [];
}

public sealed class CreateRequestRequest
{
    [EnumDataType(typeof(RequestType))]
    public RequestType Type { get; set; }

    public int? AssetId { get; set; }

    [Required, StringLength(2000, MinimumLength = 5, ErrorMessage = "Please describe the request (5-2000 characters).")]
    public string Comments { get; set; } = "";
}

public sealed class RequestTransitionRequest
{
    [StringLength(1000)]
    public string? Note { get; set; }
}

/// <summary>
/// Request workflow: Submitted → Approved → InProgress → Delivered → Checking → Received → Done, or Submitted → Rejected.
/// Each transition is guarded by both the current status and who is allowed to perform it.
/// </summary>
public sealed class RequestService(AppDbContext db, ICurrentUser currentUser, ActivityLogger activity, AssetService assets)
{
    private enum Actor { Approver, Processor, Completer, Requester }

    private sealed record Transition(string Action, RequestStatus From, RequestStatus To, Actor Actor, string ActivityAction);

    private static readonly Transition[] Workflow =
    [
        new("approve", RequestStatus.Submitted, RequestStatus.Approved, Actor.Approver, ActivityActions.RequestApproved),
        new("reject", RequestStatus.Submitted, RequestStatus.Rejected, Actor.Approver, ActivityActions.RequestRejected),
        new("start", RequestStatus.Approved, RequestStatus.InProgress, Actor.Processor, ActivityActions.RequestStatusChanged),
        new("deliver", RequestStatus.InProgress, RequestStatus.Delivered, Actor.Processor, ActivityActions.RequestStatusChanged),
        new("check", RequestStatus.Delivered, RequestStatus.Checking, Actor.Requester, ActivityActions.RequestStatusChanged),
        new("receive", RequestStatus.Checking, RequestStatus.Received, Actor.Requester, ActivityActions.RequestStatusChanged),
        new("complete", RequestStatus.Received, RequestStatus.Done, Actor.Completer, ActivityActions.RequestStatusChanged)
    ];

    public static readonly Expression<Func<AssetRequest, RequestListDto>> ToListDto = r => new RequestListDto
    {
        Id = r.Id,
        RequestNumber = r.RequestNumber,
        Type = r.Type,
        Status = r.Status,
        AssetId = r.AssetId,
        AssetTag = r.Asset != null ? r.Asset.AssetTag : null,
        AssetName = r.Asset != null ? r.Asset.Name : null,
        RequesterEmployeeId = r.RequesterEmployeeId,
        RequesterName = r.RequesterEmployee!.FullName,
        Department = r.RequesterEmployee.Department != null ? r.RequesterEmployee.Department.Name : null,
        Comments = r.Comments,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    public Task<PagedResult<RequestListDto>> ListAsync(GridQuery grid, bool mineOnly, CancellationToken ct)
    {
        var query = db.Requests.AsNoTracking();
        if (mineOnly || !CanSeeAll) query = query.Where(r => r.RequesterUserId == currentUser.UserId);
        return query.Select(ToListDto).ToPagedResultAsync(grid, nameof(RequestListDto.CreatedAt), true, ct);
    }

    public async Task<RequestDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var request = await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null || (!CanSeeAll && request.RequesterUserId != currentUser.UserId))
            throw new NotFoundException("Request not found.");

        var dto = await db.Requests.AsNoTracking().Where(r => r.Id == id).Select(ToListDto).FirstAsync(ct);
        var history = await db.RequestStatusHistory.AsNoTracking().Where(h => h.RequestId == id)
            .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
            .Select(h => new RequestStatusHistoryDto(h.Id, h.FromStatus, h.ToStatus,
                h.ChangedBy!.Employee != null ? h.ChangedBy.Employee.FullName : h.ChangedBy.Email, h.Note, h.ChangedAt))
            .ToListAsync(ct);

        return new RequestDetailDto
        {
            Request = dto,
            History = history,
            AllowedActions = Workflow.Where(t => t.From == request.Status && IsAllowed(t.Actor, request)).Select(t => t.Action).ToList()
        };
    }

    public async Task<RequestDetailDto> CreateAsync(CreateRequestRequest input)
    {
        var employeeId = currentUser.EmployeeId
                         ?? throw new BusinessRuleException("Your account is not linked to an employee profile, so you cannot raise requests.");

        var needsAsset = input.Type is RequestType.Repair or RequestType.Replacement or RequestType.Return;
        if (needsAsset && input.AssetId is null)
            throw ValidationFailedException.For("assetId", "Select the asset this request is about.");

        Asset? asset = null;
        if (input.AssetId is { } assetId)
        {
            asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId)
                    ?? throw ValidationFailedException.For("assetId", "Select a valid asset.");
            // Never trust the asset id from the client: it must be one the requester actually holds.
            var holdsAsset = await db.AssetAssignments.AnyAsync(x => x.AssetId == assetId && x.EmployeeId == employeeId && x.ReturnedAt == null);
            if (!holdsAsset && !currentUser.Has(Permissions.AssetsView))
                throw ValidationFailedException.For("assetId", "You can only raise requests for assets assigned to you.");

            var duplicate = await db.Requests.AnyAsync(r => r.AssetId == assetId && r.Type == input.Type
                && r.Status != RequestStatus.Done && r.Status != RequestStatus.Rejected);
            if (duplicate) throw new BusinessRuleException($"There is already an open {input.Type} request for {asset.AssetTag}.");
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        var request = new AssetRequest
        {
            RequestNumber = await NextNumberAsync(),
            Type = input.Type,
            AssetId = asset?.Id,
            RequesterEmployeeId = employeeId,
            RequesterUserId = currentUser.UserId,
            Comments = input.Comments.Trim(),
            Status = RequestStatus.Submitted
        };
        request.StatusHistory.Add(new RequestStatusHistory
        {
            ToStatus = RequestStatus.Submitted, ChangedById = currentUser.UserId, ChangedAt = DateTime.UtcNow, Note = "Request submitted"
        });
        db.Requests.Add(request);

        if (asset is not null && input.Type == RequestType.Repair)
            assets.AddHistory(asset, AssetAction.RepairRequested, asset.Status, asset.Status, employeeId, $"{request.RequestNumber}: {request.Comments}");

        await db.SaveChangesAsync();
        activity.Add(ActivityActions.RequestCreated, $"{request.RequestNumber} ({request.Type}) submitted", "Request", request.Id);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return await GetAsync(request.Id, CancellationToken.None);
    }

    public async Task<RequestDetailDto> TransitionAsync(int id, string action, string? note)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id);
        if (request is null || (!CanSeeAll && request.RequesterUserId != currentUser.UserId))
            throw new NotFoundException("Request not found.");

        var transition = Workflow.FirstOrDefault(t => t.Action == action) ?? throw new NotFoundException("Unknown action.");
        if (!IsAllowed(transition.Actor, request))
            throw new ForbiddenException(transition.Actor == Actor.Requester
                ? "Only the employee who raised this request can do this."
                : "You do not have permission to perform this step.");
        if (request.Status != transition.From)
            throw new BusinessRuleException($"A request that is {Label(request.Status)} cannot be moved to {Label(transition.To)}.");
        if (action == "reject" && string.IsNullOrWhiteSpace(note))
            throw ValidationFailedException.For("note", "Please give a reason for rejecting the request.");

        var from = request.Status;
        request.Status = transition.To;
        db.RequestStatusHistory.Add(new RequestStatusHistory
        {
            RequestId = request.Id, FromStatus = from, ToStatus = transition.To, ChangedById = currentUser.UserId,
            ChangedAt = DateTime.UtcNow, Note = AssetService.Clean(note)
        });
        activity.Add(transition.ActivityAction, $"{request.RequestNumber}: {Label(from)} → {Label(transition.To)}", "Request", request.Id);
        await db.SaveChangesAsync();
        return await GetAsync(id, CancellationToken.None);
    }

    private bool CanSeeAll => currentUser.Has(Permissions.RequestsView) || currentUser.Has(Permissions.RequestsApprove)
                              || currentUser.Has(Permissions.RequestsProcess) || currentUser.Has(Permissions.RequestsComplete);

    private bool IsAllowed(Actor actor, AssetRequest request) => actor switch
    {
        Actor.Approver => currentUser.Has(Permissions.RequestsApprove),
        Actor.Processor => currentUser.Has(Permissions.RequestsProcess),
        Actor.Completer => currentUser.Has(Permissions.RequestsComplete),
        Actor.Requester => request.RequesterUserId == currentUser.UserId,
        _ => false
    };

    private async Task<string> NextNumberAsync()
    {
        var numbers = await db.Requests.Select(r => r.RequestNumber).ToListAsync();
        var max = numbers.Select(n => int.TryParse(n.Replace("REQ-", ""), out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"REQ-{max + 1:D4}";
    }

    public static string Label(RequestStatus status) => status switch
    {
        RequestStatus.InProgress => "In Progress",
        _ => status.ToString()
    };
}
