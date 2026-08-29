using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Domain.Simulation;
using ProductManager.Engine;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Immutable simulation executions. A run references a roadmap branch, an assumption set, a
/// covariate scenario, an event overlay and a seed, snapshots the resulting engine input in full,
/// and stores the trajectories. Runs are never mutated — only created, read and deleted — because
/// comparing two runs is only honest if neither has changed underneath you.
/// </summary>
[Route("api/simulation-runs")]
public sealed class SimulationRunsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<SimulationRunDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<SimulationRunDto>> List(
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? roadmapId,
        [FromQuery] Guid? assumptionSetId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.SimulationRuns.AsNoTracking()
            .Where(r => (workspaceId == null || r.WorkspaceId == workspaceId)
                        && (roadmapId == null || r.RoadmapId == roadmapId)
                        && (assumptionSetId == null || r.AssumptionSetId == assumptionSetId))
            .OrderByDescending(r => r.CreatedAt)
            .ToPagedResponseAsync(paging, SimulationMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SimulationRunDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SimulationRunDto>> Get(Guid id, CancellationToken ct)
    {
        var run = await db.SimulationRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return run is null ? NotFoundProblem("SimulationRun", id) : Ok(run.ToDto());
    }

    /// <summary>The run's trajectories: per-segment and total bands (mean/P10/P50/P90) plus the NPV distribution.</summary>
    [HttpGet("{id:guid}/result")]
    [ProducesResponseType(typeof(EngineResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Result(Guid id, CancellationToken ct)
    {
        var run = await db.SimulationRuns.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.TrajectoriesJson })
            .FirstOrDefaultAsync(ct);

        if (run is null) return NotFoundProblem("SimulationRun", id);
        if (run.TrajectoriesJson is null) return NotFoundProblem("Result for simulation run", id);
        return Content(run.TrajectoriesJson, "application/json");
    }

    /// <summary>
    /// The exact engine input this run was executed against. Re-running the engine on this document
    /// alone — no database reads — reproduces the stored result byte for byte.
    /// </summary>
    [HttpGet("{id:guid}/input")]
    [ProducesResponseType(typeof(EngineInput), StatusCodes.Status200OK)]
    public async Task<IActionResult> Input(Guid id, CancellationToken ct)
    {
        var run = await db.SimulationRuns.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.InputSnapshotJson })
            .FirstOrDefaultAsync(ct);

        if (run is null) return NotFoundProblem("SimulationRun", id);
        if (run.InputSnapshotJson is null) return NotFoundProblem("Input snapshot for simulation run", id);
        return Content(run.InputSnapshotJson, "application/json");
    }

    /// <summary>
    /// Maps the workspace's observed market state onto the engine contract, runs it synchronously,
    /// and stores the run with its full input snapshot and result.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SimulationRunDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<SimulationRunDto>> Create([FromBody] CreateSimulationRunRequest request,
        CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RoadmapId, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", request.RoadmapId);

        var assumptionSet = await db.AssumptionSets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AssumptionSetId, ct);
        if (assumptionSet is null) return NotFoundProblem("AssumptionSet", request.AssumptionSetId);
        if (assumptionSet.WorkspaceId != roadmap.WorkspaceId)
            return ConflictProblem("The roadmap and the assumption set belong to different workspaces.");

        var workspaceId = roadmap.WorkspaceId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = request.StartDate is { } given
            ? new DateOnly(given.Year, given.Month, 1)
            : new DateOnly(today.Year, today.Month, 1);

        // ---- Segments in scope ----
        var marketIds = await db.Markets.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && (request.MarketId == null || m.Id == request.MarketId))
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (request.MarketId is not null && marketIds.Count == 0)
            return NotFoundProblem("Market", request.MarketId.Value);

        var segments = await db.ClientSegments.AsNoTracking()
            .Where(s => marketIds.Contains(s.MarketId))
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        if (segments.Count == 0)
            return ConflictProblem("This workspace has no client segments to simulate; create one first.");

        // ---- Event overlay ----
        var requestedEventIds = (request.EventIds ?? [])
            .Concat(request.HypotheticalEventIds ?? [])
            .Distinct()
            .ToList();

        var events = await db.MarketEvents.AsNoTracking()
            .Where(e => requestedEventIds.Contains(e.Id))
            .ToListAsync(ct);

        var missingEvent = requestedEventIds.FirstOrDefault(id => events.All(e => e.Id != id));
        if (missingEvent != Guid.Empty) return NotFoundProblem("MarketEvent", missingEvent);

        // ---- Covariates and their observed history ----
        var covariates = await db.Covariates.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId)
            .ToListAsync(ct);

        var covariateFacts = await db.FactEvents.AsNoTracking()
            .Where(f => f.WorkspaceId == workspaceId && f.Kind == FactEventKind.CovariateObserved)
            .ToListAsync(ct);

        // ---- Coverage grid and the roadmap's feature deltas ----
        var products = await db.Products.AsNoTracking()
            .Where(p => marketIds.Contains(p.MarketId))
            .Select(p => new { p.Id, p.CompetitorId })
            .ToListAsync(ct);

        var productIds = products.Select(p => p.Id).ToList();
        var ourProductIds = products.Where(p => p.CompetitorId is null).Select(p => p.Id).ToHashSet();

        var coverages = await db.Coverages.AsNoTracking()
            .Where(c => productIds.Contains(c.ProductId))
            .ToListAsync(ct);

        var plannedDeltas = await LoadPlannedCoverageDeltasAsync(roadmap.Id, ct);

        var world = new SimulationWorld(
            segments,
            assumptionSet.Parameters.ToList(),
            covariates,
            CovariateHistoryReader.ToObservations(covariateFacts),
            events,
            coverages,
            ourProductIds,
            plannedDeltas);

        var input = SimulationInputBuilder.Build(
            world, startDate, request.HorizonMonths, request.Paths, request.Seed,
            request.DiscountRateAnnual, request.TerminalGrowthAnnual);

        var result = SimulationEngine.Run(input);

        var run = new SimulationRun(
            workspaceId,
            roadmap.Id,
            assumptionSet.Id,
            startDate,
            request.HorizonMonths,
            request.Paths,
            request.Seed,
            request.DiscountRateAnnual,
            request.TerminalGrowthAnnual,
            request.Name,
            SimulationJson.Serialize(input.Covariates),
            SimulationJson.Serialize(input.Events),
            SimulationInputBuilder.ModelVersion);

        // Wall-clock time is measurement, not result: it is stored on the run, and zeroed inside the
        // result document so two runs of the same input are byte-identical — which is what makes the
        // reproducibility guarantee checkable rather than merely claimed.
        run.RecordResult(
            SimulationJson.Serialize(input),
            SimulationJson.Serialize(result with { ElapsedMs = 0 }),
            result.ElapsedMs);

        db.SimulationRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = run.Id }, run.ToDto());
    }

    /// <summary>Deletes a run. Deleting is the only write a stored run allows — it is never edited.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var run = await db.SimulationRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFoundProblem("SimulationRun", id);

        db.SimulationRuns.Remove(run);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Walks the one seam between the execution and market sides: roadmap → planned projects →
    /// delivered features → coverage deltas. A delta becomes real on the planned end date of the
    /// project that delivers it.
    /// </summary>
    private async Task<List<PlannedCoverageDelta>> LoadPlannedCoverageDeltasAsync(Guid roadmapId, CancellationToken ct)
    {
        var items = await db.RoadmapItems.AsNoTracking()
            .Where(i => i.RoadmapId == roadmapId)
            .Select(i => new { i.ProjectId, i.PlannedEnd })
            .ToListAsync(ct);

        if (items.Count == 0) return [];

        var projectIds = items.Select(i => i.ProjectId).ToList();

        // Flattened in memory rather than in SQL: projecting out of an included child collection
        // needs LATERAL/APPLY, which SQLite (the integration-test provider) cannot translate.
        var projects = await db.Projects.AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .ToListAsync(ct);

        var deliveries = projects
            .SelectMany(p => p.DeliveredFeatures.Select(f => (ProjectId: p.Id, f.FeatureId)))
            .ToList();

        if (deliveries.Count == 0) return [];

        var featureIds = deliveries.Select(d => d.FeatureId).Distinct().ToList();

        var features = await db.Features.AsNoTracking()
            .Where(f => featureIds.Contains(f.Id))
            .ToListAsync(ct);

        var plannedEnd = items.ToDictionary(i => i.ProjectId, i => i.PlannedEnd);

        var deltas = new List<PlannedCoverageDelta>();
        foreach (var delivery in deliveries)
        {
            if (!plannedEnd.TryGetValue(delivery.ProjectId, out var liveFrom)) continue;
            var feature = features.FirstOrDefault(f => f.Id == delivery.FeatureId);
            if (feature is null) continue;

            foreach (var delta in feature.Deltas)
            {
                if (delta.Kind != FeatureDeltaKind.Coverage) continue;
                if (delta.BusinessProcessId is null || delta.ToTier is null) continue;
                deltas.Add(new PlannedCoverageDelta(
                    delta.SegmentId, delta.BusinessProcessId.Value, delta.ToTier.Value, liveFrom));
            }
        }

        return deltas;
    }
}
