using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Domain.Market;
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
    /// Refused with 409 <see cref="ProblemTypes.PriorEvidenceReuse"/>, storing nothing, when a prior
    /// the fit would move was informed by observations the fit would apply again.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CalibrationRunResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(PriorEvidenceReuseProblem), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CalibrationRunResultDto>> Create([FromBody] CreateCalibrationRunRequest request,
        CancellationToken ct)
    {
        var prior = await db.AssumptionSets.FirstOrDefaultAsync(a => a.Id == request.AssumptionSetId, ct);
        if (prior is null) return NotFoundProblem("AssumptionSet", request.AssumptionSetId);

        var asOf = request.AsOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var windowStart = request.ObservationsRecordedAfter;
        if (windowStart >= asOf)
            return BadRequestProblem("observationsRecordedAfter must be before asOfDate.");
        var workspaceId = prior.WorkspaceId;

        // The bi-temporal cut: everything recorded up to the end of the as-of day, nothing after —
        // and, with a window, nothing recorded on or before its start.
        var cutoff = Midnight(asOf.AddDays(1));
        var windowCut = windowStart is { } w ? Midnight(w.AddDays(1)) : DateTimeOffset.MinValue;

        var facts = await db.FactEvents.AsNoTracking()
            .Where(f => f.WorkspaceId == workspaceId
                        && f.RecordedDate < cutoff
                        && f.RecordedDate >= windowCut
                        && (f.Kind == FactEventKind.OpportunityDecided
                            || f.Kind == FactEventKind.Churn
                            || f.Kind == FactEventKind.SegmentArrivalObserved))
            .ToListAsync(ct);

        var reference = await LoadReferenceAsync(workspaceId, ct);
        var segmentByFirm = reference.Firms.Where(f => f.SegmentId is not null)
            .ToDictionary(f => f.Id, f => f.SegmentId!.Value);

        var world = new CalibrationWorld(asOf, prior, facts, reference.Relationships, segmentByFirm, windowStart);
        var input = CalibrationInputBuilder.Build(world);
        CalibrationOutput output;
        try
        {
            output = CalibrationEngine.Run(input);
        }
        catch (ArgumentException ex) when (windowStart is not null)
        {
            // The engine blames the relationship spans; with a window the usual cause is the window itself.
            return BadRequestProblem(ex.Message + $" With observationsRecordedAfter = {windowStart:yyyy-MM-dd}, " +
                                     "relationship exposure starts on that date, but a fact recorded after it may be " +
                                     "effective before it (a backfilled churn) and so has no exposure in the window. " +
                                     "Move the window earlier or fit without it.");
        }

        var overlaps = CalibrationEvidence.FindOverlaps(world, input, output);
        if (overlaps.Count > 0) return EvidenceReuseProblem(overlaps);

        var run = new CalibrationRun(workspaceId, asOf, prior.Id, request.Name, windowStart) { Notes = request.Notes };
        var fitted = CalibrationInputBuilder.BuildFittedSet(
            prior,
            output,
            request.Name is { Length: > 0 } name ? name : $"{prior.Name} (calibrated {asOf:yyyy-MM-dd})",
            CalibrationEvidence.Stamps(run.Id, world, input, output));
        fitted.Notes = $"Fitted from the facts ledger as of {asOf:yyyy-MM-dd} " +
                       $"({output.FittedFlows.Count} flow parameter(s), {output.FittedSalesCycles.Count} sales cycle(s) moved).";
        db.AssumptionSets.Add(fitted);

        var priorParameters = prior.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.ToDto()).ToList();
        run.RecordFit(fitted.Id, SimulationJson.Serialize(input), SimulationJson.Serialize(output),
            SimulationJson.Serialize(priorParameters));
        db.CalibrationRuns.Add(run);

        await db.SaveChangesAsync(ct);

        var result = new CalibrationRunResultDto(run.ToDto(), BuildDiff(run, output, prior));
        return CreatedAtAction(nameof(Get), new { id = run.Id }, result);
    }

    /// <summary>
    /// The run's readiness report (PRD8), computed on read from its stored snapshots plus current
    /// reference data and ledger counts: per parameter and segment the evidence used, its units and
    /// denominator, prior evidence, posterior and open issues; populations at the as-of date; facts the
    /// cut excluded; arrival months observed and missing; asserted choice-model quantities; and, with
    /// <c>holdoutThrough</c>, a comparison against outcomes effective after the as-of date.
    /// </summary>
    [HttpGet("{id:guid}/readiness")]
    [ProducesResponseType(typeof(CalibrationReadinessDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CalibrationReadinessDto>> Readiness(Guid id, [FromQuery] DateOnly? holdoutThrough,
        CancellationToken ct)
    {
        var run = await db.CalibrationRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFoundProblem("CalibrationRun", id);
        if (run.OutputJson is null || run.InputSnapshotJson is null) return NotFoundProblem("Result for calibration run", id);
        if (holdoutThrough <= run.FactsAsOfDate)
            return BadRequestProblem("holdoutThrough must be after the run's as-of date.");

        IReadOnlyList<ParameterDto> priorParameters;
        if (run.PriorParametersJson is not null)
        {
            priorParameters = SimulationJson.Deserialize<List<ParameterDto>>(run.PriorParametersJson);
        }
        else
        {
            var prior = await db.AssumptionSets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == run.PriorAssumptionSetId, ct);
            priorParameters = prior?.Parameters.Select(p => p.ToDto()).ToList() ?? [];
        }

        var reference = await LoadReferenceAsync(run.WorkspaceId, ct);
        var facts = await db.FactEvents.AsNoTracking()
            .Where(f => f.WorkspaceId == run.WorkspaceId
                        && (f.Kind == FactEventKind.OpportunityDecided
                            || f.Kind == FactEventKind.Churn
                            || f.Kind == FactEventKind.SegmentArrivalObserved))
            .ToListAsync(ct);

        var world = new ReadinessWorld(
            run,
            SimulationJson.Deserialize<CalibrationInput>(run.InputSnapshotJson),
            SimulationJson.Deserialize<CalibrationOutput>(run.OutputJson),
            priorParameters,
            run.PriorParametersJson is null,
            reference.Segments,
            reference.Firms,
            reference.Relationships,
            reference.OurProducts,
            facts,
            reference.IntegrationCount,
            reference.ChannelProductBiasCount,
            holdoutThrough);
        return Ok(CalibrationReadiness.Build(world));
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

    private sealed record Reference(
        IReadOnlyDictionary<Guid, string> Segments,
        IReadOnlyList<ReadinessFirm> Firms,
        IReadOnlyList<FirmProductRelationship> Relationships,
        IReadOnlyDictionary<Guid, ReadinessProduct> OurProducts,
        int IntegrationCount,
        int ChannelProductBiasCount);

    /// <summary>Current reference data of the workspace's markets: segments, firms, our products and every relationship with them.</summary>
    private async Task<Reference> LoadReferenceAsync(Guid workspaceId, CancellationToken ct)
    {
        var marketIds = await db.Markets.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId)
            .Select(m => m.Id)
            .ToListAsync(ct);

        var segments = await db.ClientSegments.AsNoTracking()
            .Where(s => marketIds.Contains(s.MarketId))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var firms = await db.Firms.AsNoTracking()
            .Where(f => marketIds.Contains(f.MarketId))
            .Select(f => new ReadinessFirm(f.Id, f.ClientSegmentId, f.EpistemicStatus))
            .ToListAsync(ct);

        var products = await db.Products.AsNoTracking()
            .Where(p => marketIds.Contains(p.MarketId))
            .Select(p => new { p.Id, p.Name, p.CompetitorId, p.ParentProductId })
            .ToListAsync(ct);
        var ours = products.Where(p => p.CompetitorId == null)
            .ToDictionary(p => p.Id, p => new ReadinessProduct(p.Name, p.ParentProductId != null));
        var ourIds = ours.Keys.ToList();
        var productIds = products.Select(p => p.Id).ToList();

        var relationships = await db.FirmProductRelationships.AsNoTracking()
            .Where(r => ourIds.Contains(r.ProductId))
            .ToListAsync(ct);

        var integrations = await db.Integrations.AsNoTracking().CountAsync(i => productIds.Contains(i.ProductAId), ct);
        var channels = await db.Channels.AsNoTracking().Where(c => marketIds.Contains(c.MarketId)).ToListAsync(ct);

        return new Reference(segments, firms, relationships, ours, integrations,
            channels.Sum(c => c.ProductAffinities.Count));
    }

    private ObjectResult EvidenceReuseProblem(IReadOnlyList<PriorEvidenceOverlapDto> overlaps)
    {
        var lines = overlaps.Select(o =>
            $"{o.Key}: {o.OverlappingObservations} of {o.FitObservations} observation(s)" +
            (o.OverlappingExposure is > 0 and var e ? $" and {e:0.#} client-month(s)" : "") +
            (o.OverlapFrom is { } from ? $" effective {from:yyyy-MM-dd}..{o.OverlapThrough:yyyy-MM-dd}" : "") +
            (o.Rule == PriorEvidenceRule.RecordedThrough
                ? $" were already in the prior (recorded through {o.PriorEvidence.RecordedThrough:yyyy-MM-dd}; use " +
                  $"observationsRecordedAfter >= {o.PriorEvidence.RecordedThrough:yyyy-MM-dd})"
                : $" fall inside the prior's observation period {o.PriorEvidence.ObservedFrom:yyyy-MM-dd}.." +
                  $"{o.PriorEvidence.ObservedThrough:yyyy-MM-dd} (the fit's observations must be effective after " +
                  $"{o.PriorEvidence.ObservedThrough:yyyy-MM-dd}" +
                  (o.OverlappingExposure is not null
                      ? $", and client-months start after it only with observationsRecordedAfter >= " +
                        $"{o.PriorEvidence.ObservedThrough!.Value.AddDays(1):yyyy-MM-dd}"
                      : "") + "; a window does not move an old deal recorded late)"));
        var problem = new PriorEvidenceReuseProblem
        {
            Type = ProblemTypes.PriorEvidenceReuse,
            Title = "Prior evidence reuse",
            Status = StatusCodes.Status409Conflict,
            Detail = "The fit would apply evidence its prior already contains. Nothing was fitted or stored. " +
                     string.Join("; ", lines) + ".",
            Overlaps = overlaps
        };
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status409Conflict,
            ContentTypes = { "application/problem+json" }
        };
    }

    private static DateTimeOffset Midnight(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

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
