using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetPro.Api.Features.Requests;

/// <summary>Everyone signed in may list/open their own requests; the service widens scope for approvers/processors.</summary>
[ApiController]
[Authorize]
[Route("api/requests")]
public sealed class RequestsController(RequestService requests, RecordDeletionService deletion) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<RequestListDto>> List([FromQuery] GridQuery grid, [FromQuery] bool mine = false, CancellationToken ct = default) =>
        requests.ListAsync(grid, mine, ct);

    [HttpGet("{id:int}")]
    public Task<RequestDetailDto> Get(int id, CancellationToken ct) => requests.GetAsync(id, ct);

    /// <summary>Permanently removes the record. Administrators only.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await deletion.DeleteRequestAsync(id, ct);
        return NoContent();
    }

    [HttpPost]
    [Authorize(Policy = Permissions.RequestsCreate)]
    public async Task<ActionResult<RequestDetailDto>> Create(CreateRequestRequest request)
    {
        var created = await requests.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Request.Id }, created);
    }

    [HttpPost("{id:int}/approve")]
    public Task<RequestDetailDto> Approve(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestTransitionRequest? body) => requests.TransitionAsync(id, "approve", body?.Note);

    [HttpPost("{id:int}/reject")]
    public Task<RequestDetailDto> Reject(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestTransitionRequest? body) => requests.TransitionAsync(id, "reject", body?.Note);

    [HttpPost("{id:int}/start")]
    public Task<RequestDetailDto> Start(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestTransitionRequest? body) => requests.TransitionAsync(id, "start", body?.Note);

    [HttpPost("{id:int}/deliver")]
    public Task<RequestDetailDto> Deliver(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestTransitionRequest? body) => requests.TransitionAsync(id, "deliver", body?.Note);

    [HttpPost("{id:int}/check")]
    public Task<RequestDetailDto> Check(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestTransitionRequest? body) => requests.TransitionAsync(id, "check", body?.Note);

    [HttpPost("{id:int}/receive")]
    public Task<RequestDetailDto> Receive(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestTransitionRequest? body) => requests.TransitionAsync(id, "receive", body?.Note);

    [HttpPost("{id:int}/complete")]
    public Task<RequestDetailDto> Complete(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestTransitionRequest? body) => requests.TransitionAsync(id, "complete", body?.Note);
}
