using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AssetPro.Api.Features.Assets;

[ApiController]
[Authorize]
[Route("api/assets")]
public sealed class AssetsController(AssetService assets, RecordDeletionService deletion) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.AssetsView)]
    public Task<PagedResult<AssetListDto>> List([FromQuery] GridQuery grid, CancellationToken ct) => assets.ListAsync(grid, ct);

    /// <summary>Asset holders may open their own asset; everyone else needs Assets.View (enforced in the service).</summary>
    [HttpGet("{id:int}")]
    public Task<AssetDetailDto> Get(int id, CancellationToken ct) => assets.GetAsync(id, ct);

    [HttpGet("{id:int}/history")]
    public Task<IReadOnlyList<AssetHistoryDto>> History(int id, CancellationToken ct) => assets.GetHistoryAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Permissions.AssetsCreate)]
    public async Task<ActionResult<AssetListDto>> Create(SaveAssetRequest request)
    {
        var created = await assets.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.AssetsEdit)]
    public Task<AssetListDto> Update(int id, SaveAssetRequest request) => assets.UpdateAsync(id, request);

    /// <summary>Permanently removes the asset and its own history. Administrators only.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await deletion.DeleteAssetAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/assign")]
    [Authorize(Policy = Permissions.AssetsAssign)]
    public Task<AssetListDto> Assign(int id, AssignAssetRequest request) => assets.AssignAsync(id, request);

    [HttpPost("{id:int}/return")]
    [Authorize(Policy = Permissions.AssetsReturn)]
    public Task<AssetListDto> Return(int id, ReturnAssetRequest request) => assets.ReturnAsync(id, request);

    [HttpPost("{id:int}/repair")]
    [Authorize(Policy = Permissions.AssetsRepair)]
    public Task<AssetListDto> Repair(int id, RepairAssetRequest request) => assets.RepairAsync(id, request);

    [HttpPost("{id:int}/replace-component")]
    [Authorize(Policy = Permissions.AssetsRepair)]
    public Task<ComponentReplacementDto> ReplaceComponent(int id, ReplaceComponentRequest request) => assets.ReplaceComponentAsync(id, request);

    [HttpPost("{id:int}/scrap")]
    [Authorize(Policy = Permissions.AssetsScrap)]
    public Task<AssetListDto> Scrap(int id, ScrapAssetRequest request) => assets.ScrapAsync(id, request);

    [HttpPost("{id:int}/status")]
    [Authorize(Policy = Permissions.AssetsEdit)]
    public Task<AssetListDto> ChangeStatus(int id, ChangeAssetStatusRequest request) => assets.ChangeStatusAsync(id, request);
}

[ApiController]
[Authorize(Policy = Permissions.AssetsView)]
[Route("api/history")]
public sealed class AssetHistoryController(AssetService assets) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AssetHistoryDto>> List([FromQuery] GridQuery grid, CancellationToken ct) => assets.ListHistoryAsync(grid, ct);
}
