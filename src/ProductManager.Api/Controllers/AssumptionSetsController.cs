using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Simulation;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Versioned, branchable parameter collections. Every parameter carries a distribution, a
/// provenance (asserted / calibrated / default) and a rationale — "what if churn is 2×" is a
/// one-parameter branch, not an edit that quietly rewrites what past runs were executed against.
/// </summary>
[Route("api/assumption-sets")]
public sealed class AssumptionSetsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<AssumptionSetDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<AssumptionSetDto>> List(
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? parentAssumptionSetId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.AssumptionSets.AsNoTracking()
            .Where(a => (workspaceId == null || a.WorkspaceId == workspaceId)
                        && (parentAssumptionSetId == null || a.ParentAssumptionSetId == parentAssumptionSetId))
            .OrderBy(a => a.CreatedAt)
            .ToPagedResponseAsync(paging, SimulationMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AssumptionSetDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AssumptionSetDto>> Get(Guid id, CancellationToken ct)
    {
        var set = await db.AssumptionSets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return set is null ? NotFoundProblem("AssumptionSet", id) : Ok(set.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(AssumptionSetDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AssumptionSetDto>> Create([FromBody] CreateAssumptionSetRequest request,
        CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == request.WorkspaceId, ct))
            return NotFoundProblem("Workspace", request.WorkspaceId);

        var set = new AssumptionSet(request.WorkspaceId, request.Name) { Notes = request.Notes };
        db.AssumptionSets.Add(set);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = set.Id }, set.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AssumptionSetDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AssumptionSetDto>> Update(Guid id, [FromBody] UpdateAssumptionSetRequest request,
        CancellationToken ct)
    {
        var set = await db.AssumptionSets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (set is null) return NotFoundProblem("AssumptionSet", id);

        set.Rename(request.Name);
        set.Notes = request.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(set.ToDto());
    }

    /// <summary>Refused while simulation runs still reference the set — a run must stay reproducible.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var set = await db.AssumptionSets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (set is null) return NotFoundProblem("AssumptionSet", id);
        if (await db.SimulationRuns.AnyAsync(r => r.AssumptionSetId == id, ct))
            return ConflictProblem("Simulation runs reference this assumption set; delete those runs first.");
        if (await db.AssumptionSets.AnyAsync(a => a.ParentAssumptionSetId == id, ct))
            return ConflictProblem("Other assumption sets are branched from this one; delete those first.");

        db.AssumptionSets.Remove(set);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Branches the set: a new set with a deep copy of every parameter and a pointer back here.
    /// The branch is fully independent — editing it never touches the parent.
    /// </summary>
    [HttpPost("{id:guid}/branch")]
    [ProducesResponseType(typeof(AssumptionSetDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AssumptionSetDto>> Branch(Guid id, [FromBody] BranchAssumptionSetRequest request,
        CancellationToken ct)
    {
        var set = await db.AssumptionSets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (set is null) return NotFoundProblem("AssumptionSet", id);

        var branch = set.Branch(request.Name);
        db.AssumptionSets.Add(branch);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = branch.Id }, branch.ToDto());
    }

    /// <summary>
    /// Sets (upserting) one parameter. Supply <c>clientSegmentId</c> + <c>flowKind</c>
    /// (+ <c>moduleProductId</c> for CrossSellRate) for a parameter the engine reads — that triple
    /// is the engine's own parameter key, so writing it twice replaces rather than duplicates.
    /// Supply <c>key</c> instead for a free-form assumption the engine ignores.
    /// </summary>
    [HttpPut("{id:guid}/parameters")]
    [ProducesResponseType(typeof(AssumptionSetDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AssumptionSetDto>> UpsertParameter(Guid id,
        [FromBody] UpsertParameterRequest request, CancellationToken ct)
    {
        var set = await db.AssumptionSets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (set is null) return NotFoundProblem("AssumptionSet", id);

        var isEngineParameter = request.ClientSegmentId is not null || request.FlowKind is not null;
        if (isEngineParameter && (request.ClientSegmentId is null || request.FlowKind is null))
            return Problem(
                detail: "An engine parameter needs both clientSegmentId and flowKind.",
                statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");
        if (!isEngineParameter && string.IsNullOrWhiteSpace(request.Key))
            return Problem(
                detail: "Supply clientSegmentId + flowKind for an engine parameter, or key for a free-form one.",
                statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");

        if (request.ClientSegmentId is not null &&
            !await db.ClientSegments.AnyAsync(s => s.Id == request.ClientSegmentId, ct))
            return NotFoundProblem("ClientSegment", request.ClientSegmentId.Value);
        if (request.ModuleProductId is not null &&
            !await db.Products.AnyAsync(p => p.Id == request.ModuleProductId, ct))
            return NotFoundProblem("Product", request.ModuleProductId.Value);
        if (request.CovariateId is not null &&
            !await db.Covariates.AnyAsync(c => c.Id == request.CovariateId, ct))
            return NotFoundProblem("Covariate", request.CovariateId.Value);

        var distribution = request.Distribution.ToDomain();
        if (request.ClearPriorEvidence && request.PriorEvidence is not null)
            return BadRequestProblem("Give priorEvidence or clearPriorEvidence, not both.");

        // Evidence is sticky: an edit that says nothing about it keeps what the parameter had (a fit's stamp
        // included); clearing it is a deliberate clearPriorEvidence. A fitted posterior's stamp cannot be
        // relabelled while its distribution is unchanged — that would hide the evidence the run applied.
        var key = isEngineParameter
            ? Parameter.EngineKey(request.FlowKind!.Value, request.ClientSegmentId!.Value, request.ModuleProductId)
            : request.Key!.Trim();
        var existing = set.Parameters.FirstOrDefault(p => p.Key == key);
        var priorEvidence = request.ClearPriorEvidence ? null
            : request.PriorEvidence?.ToDomain() ?? existing?.PriorEvidence?.Copy();
        if (existing?.PriorEvidence is { SourceCalibrationRunId: { } runId } stamp
            && request.PriorEvidence is not null && priorEvidence != stamp && existing.Distribution == distribution)
            return CorrectionRequiredProblem(
                $"This distribution is the posterior of calibration run {runId}, whose evidence is recorded on it " +
                $"(recorded through {stamp.RecordedThrough:yyyy-MM-dd}). Declaring different evidence for the same " +
                "distribution would hide what the fit applied; change the distribution, or clear the evidence " +
                "deliberately with clearPriorEvidence.");

        // Parameters are non-owned children with domain-assigned ids, so EF has to be told which
        // ones this call actually created.
        db.MutateChildren(() => set.Parameters, () =>
        {
            if (isEngineParameter)
                set.SetEngineParameter(request.ClientSegmentId!.Value, request.FlowKind!.Value,
                    request.ModuleProductId, distribution, request.Provenance, request.Rationale,
                    request.CovariateId, request.CovariateBeta, priorEvidence);
            else
                set.SetParameter(request.Key!, distribution, request.Provenance, request.Rationale, priorEvidence);
        });

        await db.SaveChangesAsync(ct);
        return Ok(set.ToDto());
    }

    [HttpDelete("{id:guid}/parameters/{parameterId:guid}")]
    [ProducesResponseType(typeof(AssumptionSetDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AssumptionSetDto>> RemoveParameter(Guid id, Guid parameterId, CancellationToken ct)
    {
        var set = await db.AssumptionSets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (set is null) return NotFoundProblem("AssumptionSet", id);
        if (!set.RemoveParameter(parameterId)) return NotFoundProblem("Parameter", parameterId);

        await db.SaveChangesAsync(ct);
        return Ok(set.ToDto());
    }
}
