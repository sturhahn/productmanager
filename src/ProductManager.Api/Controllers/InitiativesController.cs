using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Execution;

namespace ProductManager.Api.Controllers;

/// <summary>Strategic bets — the *why*. Initiatives contain Projects and target market-side segments and needs.</summary>
[Route("api/initiatives")]
public sealed class InitiativesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<InitiativeDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<InitiativeDto>> List([FromQuery] Guid? workspaceId, [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Initiatives.AsNoTracking()
            .Where(i => workspaceId == null || i.WorkspaceId == workspaceId)
            .OrderBy(i => i.Name)
            .ToPagedResponseAsync(paging, ExecutionMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InitiativeDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InitiativeDto>> Get(Guid id, CancellationToken ct)
    {
        var initiative = await db.Initiatives.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return initiative is null ? NotFoundProblem("Initiative", id) : Ok(initiative.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(InitiativeDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<InitiativeDto>> Create([FromBody] CreateInitiativeRequest request,
        CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == request.WorkspaceId, ct))
            return NotFoundProblem("Workspace", request.WorkspaceId);

        var initiative = new Initiative(request.WorkspaceId, request.Name, request.Outcome, request.HypothesizedValue);
        initiative.SetStrategicIntent(request.StrategicIntent);
        ApplyTargets(initiative, request.TargetSegmentIds, request.TargetNeedIds);

        db.Initiatives.Add(initiative);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = initiative.Id }, initiative.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(InitiativeDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InitiativeDto>> Update(Guid id, [FromBody] UpdateInitiativeRequest request,
        CancellationToken ct)
    {
        var initiative = await db.Initiatives.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (initiative is null) return NotFoundProblem("Initiative", id);

        initiative.Rename(request.Name);
        initiative.Outcome = request.Outcome;
        initiative.HypothesizedValue = request.HypothesizedValue;
        initiative.SetStrategicIntent(request.StrategicIntent);
        ApplyTargets(initiative, request.TargetSegmentIds, request.TargetNeedIds);

        await db.SaveChangesAsync(ct);
        return Ok(initiative.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var initiative = await db.Initiatives.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (initiative is null) return NotFoundProblem("Initiative", id);

        db.Initiatives.Remove(initiative);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void ApplyTargets(Initiative initiative, IReadOnlyList<Guid>? segmentIds,
        IReadOnlyList<Guid>? needIds)
    {
        if (segmentIds is not null)
        {
            initiative.TargetSegmentIds.Clear();
            initiative.TargetSegmentIds.AddRange(segmentIds.Distinct());
        }

        if (needIds is not null)
        {
            initiative.TargetNeedIds.Clear();
            initiative.TargetNeedIds.AddRange(needIds.Distinct());
        }
    }
}
