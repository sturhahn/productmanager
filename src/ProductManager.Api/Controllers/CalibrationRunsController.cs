using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Domain.Simulation;
using ProductManager.Engine.Calibration;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Immutable fits: the prior assumption set plus the facts ledger <em>as of a date</em> produce a
/// new, fitted assumption set, diffable against the prior.
///
/// <para>
/// The as-of cut is the whole point. Only facts whose <c>recordedDate</c> falls on or before the
/// date are read, so a fit reconstructs what was actually known then rather than quietly borrowing
/// hindsight — which is the difference between a backtest and a flattering story about one.
/// </para>
///
/// <para>
/// A fit never edits the prior: it creates a branch whose fitted parameters carry provenance
/// Calibrated and an auto-generated rationale naming the evidence ("fitted from 42 decided
/// opportunities"), so every number stays traceable to what moved it.
/// </para>
/// </summary>
[Route("api/calibration-runs")]
public sealed class CalibrationRunsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<CalibrationRunDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<CalibrationRunDto>> List(
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? priorAssumptionSetId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.CalibrationRuns.AsNoTracking()
            .Where(r => (workspaceId == null || r.WorkspaceId == workspaceId)
                        && (priorAssumptionSetId == null || r.PriorAssumptionSetId == priorAssumptionSetId))
            .OrderByDescending(r => r.CreatedAt)
            .ToPagedResponseAsync(paging, Phase3Mapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CalibrationRunDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CalibrationRunDto>> Get(Guid id, CancellationToken ct)
    {
        var run = await db.CalibrationRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return run is null ? NotFoundProblem("CalibrationRun", id) : Ok(run.ToDto());
    }

    /// <summary>Prior vs posterior for every parameter the fit touched, and the reason for each one it could not.</summary>
    [HttpGet("{id:guid}/diff")]
    [ProducesResponseType(typeof(CalibrationDiffDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CalibrationDiffDto>> Diff(Guid id, CancellationToken ct)
    {
        var run = await db.CalibrationRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFoundProblem("CalibrationRun", id);
        if (run.OutputJson is null) return NotFoundProblem("Result for calibration run", id);

        var prior = await db.AssumptionSets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == run.PriorAssumptionSetId, ct);

        var output = SimulationJson.Deserialize<CalibrationOutput>(run.OutputJson);
        return Ok(BuildDiff(run, output, prior));
    }

    /// <summary>The exact calibration input this run was executed against — priors plus the observations as of the date.</summary>
    [HttpGet("{id:guid}/input")]
    [ProducesResponseType(typeof(CalibrationInput), StatusCodes.Status200OK)]
    public async Task<IActionResult> Input(Guid id, CancellationToken ct)
    {
        var run = await db.CalibrationRuns.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.InputSnapshotJson })
            .FirstOrDefaultAsync(ct);

        if (run is null) return NotFoundProblem("CalibrationRun", id);
        if (run.InputSnapshotJson is null) return NotFoundProblem("Input snapshot for calibration run", id);
        return Content(run.InputSnapshotJson, "application/json");
    }

    /// <summary>
    /// Runs the fit synchronously: maps the ledger as of the date onto the calibration contract,
    /// executes the pure calibration module, stores the run, and creates the fitted assumption set.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CalibrationRunResultDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CalibrationRunResultDto>> Create([FromBody] CreateCalibrationRunRequest request,
        CancellationToken ct)
    {
        var prior = await db.AssumptionSets.FirstOrDefaultAsync(a => a.Id == request.AssumptionSetId, ct);
        if (prior is null) return NotFoundProblem("AssumptionSet", request.AssumptionSetId);

        var asOf = request.AsOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var workspaceId = prior.WorkspaceId;

        // The bi-temporal cut: everything recorded up to the end of the as-of day, nothing after.
        var cutoff = new DateTimeOffset(asOf.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var facts = await db.FactEvents.AsNoTracking()
            .Where(f => f.WorkspaceId == workspaceId
                        && f.RecordedDate < cutoff
                        && (f.Kind == FactEventKind.OpportunityDecided
                            || f.Kind == FactEventKind.Churn
                            || f.Kind == FactEventKind.SegmentArrivalObserved))
            .ToListAsync(ct);

        var marketIds = await db.Markets.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId)
            .Select(m => m.Id)
            .ToListAsync(ct);

        var firms = await db.Firms.AsNoTracking()
            .Where(f => marketIds.Contains(f.MarketId) && f.ClientSegmentId != null)
            .Select(f => new { f.Id, f.ClientSegmentId })
            .ToListAsync(ct);
        var segmentByFirm = firms.ToDictionary(f => f.Id, f => f.ClientSegmentId!.Value);

        var ourProductIds = await db.Products.AsNoTracking()
            .Where(p => marketIds.Contains(p.MarketId) && p.CompetitorId == null)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var firmIds = firms.Select(f => f.Id).ToList();
        var relationships = await db.FirmProductRelationships.AsNoTracking()
            .Where(r => firmIds.Contains(r.FirmId) && ourProductIds.Contains(r.ProductId))
            .ToListAsync(ct);

        var world = new CalibrationWorld(asOf, prior, facts, relationships, segmentByFirm);
        var input = CalibrationInputBuilder.Build(world);
        var output = CalibrationEngine.Run(input);

        var fitted = CalibrationInputBuilder.BuildFittedSet(
            prior,
            output,
            request.Name is { Length: > 0 } name ? name : $"{prior.Name} (calibrated {asOf:yyyy-MM-dd})");
        fitted.Notes = $"Fitted from the facts ledger as of {asOf:yyyy-MM-dd} " +
                       $"({output.FittedFlows.Count} flow parameter(s), {output.FittedSalesCycles.Count} sales cycle(s) moved).";
        db.AssumptionSets.Add(fitted);

        var run = new CalibrationRun(workspaceId, asOf, prior.Id, request.Name) { Notes = request.Notes };
        run.RecordFit(fitted.Id, SimulationJson.Serialize(input), SimulationJson.Serialize(output));
        db.CalibrationRuns.Add(run);

        await db.SaveChangesAsync(ct);

        var result = new CalibrationRunResultDto(run.ToDto(), BuildDiff(run, output, prior));
        return CreatedAtAction(nameof(Get), new { id = run.Id }, result);
    }

    /// <summary>Deleting is the only write a stored fit allows — it is never edited.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var run = await db.CalibrationRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFoundProblem("CalibrationRun", id);

        db.CalibrationRuns.Remove(run);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static CalibrationDiffDto BuildDiff(CalibrationRun run, CalibrationOutput output, AssumptionSet? prior)
    {
        var priorByKey = prior?.Parameters.ToDictionary(p => p.Key, StringComparer.Ordinal)
                         ?? new Dictionary<string, Parameter>(StringComparer.Ordinal);

        var fitted = new List<CalibrationParameterDiffDto>();

        foreach (var flow in output.FittedFlows)
        {
            Guid? segmentId = Guid.TryParse(flow.SegmentId, out var segment) ? segment : null;
            Guid? moduleId = Guid.TryParse(flow.ModuleId, out var module) ? module : null;
            var flowKind = CalibrationInputBuilder.ToDomainFlow(flow.FlowKind);
            string key = segmentId is null
                ? $"{flowKind}:{flow.SegmentId}"
                : Parameter.EngineKey(flowKind, segmentId.Value, moduleId);

            fitted.Add(new CalibrationParameterDiffDto(
                key,
                segmentId,
                flowKind,
                moduleId,
                null,
                priorByKey.TryGetValue(key, out var previous) ? DistributionSpecDto.From(previous.Distribution) : null,
                DistributionSpecDto.From(CalibrationInputBuilder.ToDomainDistribution(flow.Posterior)),
                flow.Diff.PriorMean,
                flow.Diff.PosteriorMean,
                flow.Diff.PriorStdDev,
                flow.Diff.PosteriorStdDev,
                flow.Diff.Shift,
                flow.SampleSize,
                flow.AutoRationale));
        }

        foreach (var cycle in output.FittedSalesCycles)
        {
            if (!Guid.TryParse(cycle.SegmentId, out var segmentId)) continue;
            Guid? productId = Guid.TryParse(cycle.ProductId, out var product) ? product : null;
            string key = ChoiceParameterKeys.SalesCycleMu(segmentId, productId);

            fitted.Add(new CalibrationParameterDiffDto(
                key,
                segmentId,
                null,
                null,
                productId,
                priorByKey.TryGetValue(key, out var previous) ? DistributionSpecDto.From(previous.Distribution) : null,
                new DistributionSpecDto(DistributionType.Point, cycle.Mu),
                cycle.Diff.PriorMean,
                cycle.Diff.PosteriorMean,
                cycle.Diff.PriorStdDev,
                cycle.Diff.PosteriorStdDev,
                cycle.Diff.Shift,
                cycle.SampleSize,
                cycle.AutoRationale));
        }

        var unfitted = new List<CalibrationUnfittedDto>();

        foreach (var flow in output.UnfittedFlows)
        {
            Guid? segmentId = Guid.TryParse(flow.SegmentId, out var segment) ? segment : null;
            Guid? moduleId = Guid.TryParse(flow.ModuleId, out var module) ? module : null;
            var flowKind = CalibrationInputBuilder.ToDomainFlow(flow.FlowKind);
            string key = segmentId is null
                ? $"{flowKind}:{flow.SegmentId}"
                : Parameter.EngineKey(flowKind, segmentId.Value, moduleId);

            unfitted.Add(new CalibrationUnfittedDto(
                key, segmentId, flowKind, moduleId, null,
                DistributionSpecDto.From(CalibrationInputBuilder.ToDomainDistribution(flow.Distribution)),
                flow.Reason));
        }

        foreach (var cycle in output.UnfittedSalesCycles)
        {
            Guid? segmentId = Guid.TryParse(cycle.SegmentId, out var segment) ? segment : null;
            Guid? productId = Guid.TryParse(cycle.ProductId, out var product) ? product : null;
            string key = segmentId is null
                ? $"{ChoiceParameterKeys.SalesCycleMuPrefix}{cycle.SegmentId}"
                : ChoiceParameterKeys.SalesCycleMu(segmentId.Value, productId);

            unfitted.Add(new CalibrationUnfittedDto(
                key, segmentId, null, null, productId,
                new DistributionSpecDto(DistributionType.Point, cycle.Mu),
                cycle.Reason));
        }

        return new CalibrationDiffDto(run.Id, run.FactsAsOfDate, run.PriorAssumptionSetId, run.FittedAssumptionSetId,
            fitted, unfitted);
    }
}
