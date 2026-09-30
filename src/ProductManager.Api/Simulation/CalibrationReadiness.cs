using ProductManager.Api.Contracts;
using ProductManager.Domain.Common;
using ProductManager.Domain.Facts;
using ProductManager.Domain.Market;
using ProductManager.Domain.Simulation;
using ProductManager.Engine;
using ProductManager.Engine.Calibration;
using static ProductManager.Api.Simulation.CalibrationEvidence;
using DistributionType = ProductManager.Domain.Common.DistributionType;

namespace ProductManager.Api.Simulation;

/// <summary>A firm as the readiness report needs it.</summary>
public sealed record ReadinessFirm(Guid Id, Guid? SegmentId, EpistemicStatus Status);

/// <summary>One of our products as the readiness report needs it.</summary>
public sealed record ReadinessProduct(string Name, bool IsModule);

/// <summary>Everything a readiness report reads: the stored run plus current reference data and ledger.</summary>
/// <param name="Run">The stored run (as-of date, observation window).</param>
/// <param name="Input">The run's input snapshot.</param>
/// <param name="Output">The run's output snapshot.</param>
/// <param name="PriorParameters">The prior set's parameters as snapshotted by the run (or, for a run stored before PRD8, as they stand now).</param>
/// <param name="PriorParametersFromCurrentSet">True when <paramref name="PriorParameters"/> had to be read from the current prior set.</param>
/// <param name="Segments">Segment names of the workspace.</param>
/// <param name="Firms">Every firm of the workspace's markets.</param>
/// <param name="Relationships">Every relationship with one of our products.</param>
/// <param name="OurProducts">Our products (not competitors').</param>
/// <param name="Facts">The workspace's OpportunityDecided, Churn and SegmentArrivalObserved facts, any recorded date.</param>
/// <param name="IntegrationCount">Integration rows (asserted quality scores).</param>
/// <param name="ChannelProductBiasCount">Channel product-affinity rows (asserted channel bias).</param>
/// <param name="HoldoutThrough">Optional end of the held-out period; it starts the day after the as-of date.</param>
public sealed record ReadinessWorld(
    CalibrationRun Run,
    CalibrationInput Input,
    CalibrationOutput Output,
    IReadOnlyList<ParameterDto> PriorParameters,
    bool PriorParametersFromCurrentSet,
    IReadOnlyDictionary<Guid, string> Segments,
    IReadOnlyList<ReadinessFirm> Firms,
    IReadOnlyList<FirmProductRelationship> Relationships,
    IReadOnlyDictionary<Guid, ReadinessProduct> OurProducts,
    IReadOnlyList<FactEvent> Facts,
    int IntegrationCount,
    int ChannelProductBiasCount,
    DateOnly? HoldoutThrough);

/// <summary>
/// The PRD8 readiness report: computed on read, deterministically, from a stored calibration run's
/// snapshots plus current reference data and ledger counts. No inference happens here — the counts
/// are the engine's own selections (<see cref="CalibrationEvidence.Summarize"/>), the verdicts are
/// rules over them, and the holdout is a posterior-predictive comparison of what the fit already produced.
/// </summary>
public static class CalibrationReadiness
{
    /// <summary>Probability mass of the central posterior-predictive interval used by the holdout comparison.</summary>
    public const double HoldoutIntervalMass = 0.90;

    public const string RelationshipHistoryNote =
        "Relationship spans (the client-month exposure of ChurnHazard and CrossSellRate) are read from current " +
        "relationship rows and cut at the as-of date by their effective acquisition and end dates. The as-of fit " +
        "does not reconstruct what was known about relationships at the as-of date: a relationship entered later " +
        "but acquired earlier still contributes exposure, so exposure is an upper bound on what was known then.";

    public const string CurrentStateNote =
        "Firm counts, segment membership and module holdings are current state, not as-of reconstructions; " +
        "decision, churn and arrival counts are the run's own snapshot.";

    public const string NotAccuracyNote =
        "Readiness is about evidence: a Ready parameter has a known, non-reused prior and a complete denominator. " +
        "It says nothing about forecast accuracy.";

    public const string ChoiceDefaultsNote =
        "Choice-model quantities the set does not define are listed as asserted with provenance Default and the " +
        "firm-level engine's documented default value; the flows are never evidence for them.";

    private static readonly FactEventKind[] CalibrationKinds =
        [FactEventKind.OpportunityDecided, FactEventKind.Churn, FactEventKind.SegmentArrivalObserved];

    public static CalibrationReadinessDto Build(ReadinessWorld world)
    {
        var run = world.Run;
        var input = world.Input;
        var asOf = run.FactsAsOfDate;
        var windowStart = run.ObservationsRecordedAfter;
        var cutoff = Midnight(asOf.AddDays(1));
        DateTimeOffset? windowCut = windowStart is { } w ? Midnight(w.AddDays(1)) : null;

        var segmentByFirm = world.Firms.Where(f => f.SegmentId is not null)
            .ToDictionary(f => f.Id, f => f.SegmentId!.Value);
        var fitFacts = world.Facts
            .Where(f => f.RecordedDate < cutoff && (windowCut is null || f.RecordedDate >= windowCut))
            .ToList();
        var mappedNow = CalibrationInputBuilder.MapObservations(fitFacts, segmentByFirm);

        var exclusions = CalibrationKinds.Select(kind =>
        {
            int mapped = MappedCount(mappedNow.Opportunities.Count, mappedNow.ChurnEvents.Count, mappedNow.ArrivalCounts.Count, kind);
            int snapshot = MappedCount(input.Opportunities.Count, input.ChurnEvents.Count, input.ArrivalCounts.Count, kind);
            return new ReadinessExclusionDto(
                kind,
                world.Facts.Count(f => f.Kind == kind && f.RecordedDate >= cutoff && f.EffectiveDate <= asOf),
                windowCut is null ? 0 : world.Facts.Count(f => f.Kind == kind && f.RecordedDate < windowCut),
                fitFacts.Count(f => f.Kind == kind) - mapped,
                Math.Max(0, mapped - snapshot));
        }).ToList();
        int Unattributed(FactEventKind kind) => exclusions.Single(e => e.Kind == kind).Unattributed;

        var unsegmentedFirms = world.Firms.Where(f => f.SegmentId is null).Select(f => f.Id).ToHashSet();
        int unsegmentedHolders = world.Relationships.Select(r => r.FirmId).Distinct().Count(unsegmentedFirms.Contains);

        var populations = BuildPopulations(world, input, fitFacts, segmentByFirm);
        var populationBySegment = populations.ToDictionary(p => p.ClientSegmentId.ToString(), StringComparer.Ordinal);
        var arrivals = BuildArrivalSeries(input);
        var priorByKey = world.PriorParameters.ToDictionary(p => p.Key, StringComparer.Ordinal);

        var parameters = new List<ReadinessParameterDto>();
        var targets = input.FlowPriors.Select(p => (Target: ForFlow(p.SegmentId, p.FlowKind, p.ModuleId),
                Prior: (DistributionSpecDto?)DistributionSpecDto.From(CalibrationInputBuilder.ToDomainDistribution(p.Distribution)),
                Provenance: (ParameterProvenance?)CalibrationInputBuilder.ToDomainProvenance(p.Provenance)))
            .Concat(input.SalesCyclePriors.Select(p => (Target: ForSalesCycle(p.SegmentId, p.ProductId),
                Prior: (DistributionSpecDto?)new DistributionSpecDto(DistributionType.Point, p.Mu),
                Provenance: (ParameterProvenance?)null)));

        foreach (var (target, prior, snapshotProvenance) in targets)
        {
            var summary = Summarize(input, target);
            var priorParameter = priorByKey.GetValueOrDefault(target.Key);
            var fittedFlow = world.Output.FittedFlows.FirstOrDefault(f => target.Flow == f.FlowKind
                && f.SegmentId == target.SegmentId && f.ModuleId == target.ModuleId);
            var fittedCycle = target.Flow is null
                ? world.Output.FittedSalesCycles.FirstOrDefault(c => c.SegmentId == target.SegmentId && c.ProductId == target.ProductId)
                : null;
            bool fitted = fittedFlow is not null || fittedCycle is not null;
            string? unfittedReason = target.Flow is not null
                ? world.Output.UnfittedFlows.FirstOrDefault(u => u.FlowKind == target.Flow && u.SegmentId == target.SegmentId
                                                                 && u.ModuleId == target.ModuleId)?.Reason
                : world.Output.UnfittedSalesCycles.FirstOrDefault(u => u.SegmentId == target.SegmentId
                                                                       && u.ProductId == target.ProductId)?.Reason;

            var issues = new List<ReadinessIssueDto>();
            if (fitted && priorParameter?.PriorEvidence is null)
                issues.Add(new(ReadinessIssueCode.PriorEvidenceUnknown,
                    "The prior carries no evidence metadata, so overlap between the evidence behind it and this fit's " +
                    "observations cannot be ruled out. Declare it (Independent, or Observations with a period) and refit."));
            var population = populationBySegment.GetValueOrDefault(target.SegmentId);
            AddExposureIssues(issues, target, summary, population, unsegmentedHolders, Unattributed(FactEventKind.Churn));
            if (target.Flow is FlowKind.WinRate or FlowKind.CrossSellRate or null
                && Unattributed(FactEventKind.OpportunityDecided) is > 0 and var decisions)
                issues.Add(new(ReadinessIssueCode.UnsegmentedEvidence,
                    $"{decisions} decision fact(s) in the fit's window could not be attributed to a segment and were left out."));
            if (target.Flow is FlowKind.NewFirmArrival or FlowKind.ConversionToMarket)
                AddArrivalIssues(issues, target, arrivals, Unattributed(FactEventKind.SegmentArrivalObserved));
            var readsKind = target.Flow switch
            {
                FlowKind.ChurnHazard => FactEventKind.Churn,
                FlowKind.NewFirmArrival or FlowKind.ConversionToMarket => FactEventKind.SegmentArrivalObserved,
                _ => FactEventKind.OpportunityDecided
            };
            if (exclusions.Single(e => e.Kind == readsKind).AddedAfterRun is > 0 and var added)
                issues.Add(new(ReadinessIssueCode.LedgerChangedSinceRun,
                    $"{added} {readsKind} fact(s) recorded on or before the as-of date entered the ledger after this run " +
                    "was stored; its snapshot no longer matches the ledger. Refit."));

            // Show the exposure the engine divided by: without spans it approximated churns x window / 2.
            double exposure = summary.Exposure;
            if (target.Flow == FlowKind.ChurnHazard && summary.Successes > 0 && exposure <= 0 && summary.From is { } firstChurn)
            {
                double windowMonths = Math.Max((asOf.DayNumber - firstChurn.DayNumber) / CalibrationEngine.DaysPerMonth, 1.0);
                exposure = Math.Max(summary.Successes * windowMonths / 2.0, summary.Successes);
            }

            DistributionSpecDto? posterior = fittedFlow is not null
                ? DistributionSpecDto.From(CalibrationInputBuilder.ToDomainDistribution(fittedFlow.Posterior))
                : fittedCycle is not null ? new DistributionSpecDto(DistributionType.Point, fittedCycle.Mu) : null;

            parameters.Add(new ReadinessParameterDto(
                target.Key,
                target.Flow is { } flow ? CalibrationInputBuilder.ToDomainFlow(flow) : null,
                ParseGuid(target.SegmentId),
                ParseGuid(target.ModuleId),
                ParseGuid(target.ProductId),
                !fitted ? CalibrationReadinessStatus.Unfitted
                    : issues.Count == 0 ? CalibrationReadinessStatus.Ready : CalibrationReadinessStatus.NotReady,
                Units(target.Flow),
                Denominator(target.Flow),
                summary.From,
                summary.Through,
                summary.Observations,
                target.Flow is FlowKind.WinRate or FlowKind.ChurnHazard or FlowKind.CrossSellRate
                    or FlowKind.NewFirmArrival or FlowKind.ConversionToMarket ? summary.Successes : null,
                target.Flow is FlowKind.WinRate or FlowKind.CrossSellRate ? summary.Failures : null,
                target.Flow is FlowKind.WinRate or FlowKind.CrossSellRate ? summary.Excluded : null,
                target.Flow is null or FlowKind.WinRate ? null : exposure,
                prior,
                snapshotProvenance ?? priorParameter?.Provenance,
                priorParameter?.PriorEvidence,
                posterior,
                fittedFlow?.Diff.PosteriorMean ?? fittedCycle?.Diff.PosteriorMean,
                fitted ? null : unfittedReason,
                issues));
        }

        var asserted = BuildAsserted(world);
        var reasons = new List<string>();
        if (parameters.Count == 0) reasons.Add("The prior set has no parameter calibration can fit.");
        var notReady = parameters.Where(p => p.Status == CalibrationReadinessStatus.NotReady).ToList();
        if (notReady.Count > 0)
            reasons.Add($"{notReady.Count} fitted parameter(s) have open issues: " + string.Join("; ",
                notReady.Select(p => $"{p.Key} [{string.Join(", ", p.Issues.Select(i => i.Code).Distinct())}]")) + ".");
        int unfittedCount = parameters.Count(p => p.Status == CalibrationReadinessStatus.Unfitted);
        if (unfittedCount > 0)
            reasons.Add($"{unfittedCount} parameter(s) were not fitted; their priors stand as assumptions.");
        if (asserted.Count > 0)
            reasons.Add($"{asserted.Count} asserted choice-model quantit{(asserted.Count == 1 ? "y is" : "ies are")} " +
                        "not validated by this fit.");

        var notes = new List<string> { RelationshipHistoryNote, CurrentStateNote, NotAccuracyNote, ChoiceDefaultsNote };
        if (windowStart is { } after)
            notes.Add($"Only facts recorded after {after:yyyy-MM-dd} were fitted, and relationship exposure starts on that date.");
        if (world.PriorParametersFromCurrentSet)
            notes.Add("This run was stored before prior parameters were snapshotted; prior evidence and asserted " +
                      "quantities are read from the prior set as it stands now.");

        return new CalibrationReadinessDto(
            run.Id, asOf, windowStart, run.PriorAssumptionSetId, run.FittedAssumptionSetId,
            reasons.Count == 0 ? CalibrationReadinessOverall.Ready : CalibrationReadinessOverall.Exploratory,
            reasons, parameters, asserted, populations, exclusions, arrivals, notes,
            world.HoldoutThrough is { } h ? BuildHoldout(world, segmentByFirm, h) : null);
    }

    // ---------------------------------------------------------------- issues

    private static void AddExposureIssues(List<ReadinessIssueDto> issues, Target target, Summary summary,
        ReadinessSegmentPopulationDto? population, int unsegmentedHolders, int unattributedChurns)
    {
        if (target.Flow is not (FlowKind.ChurnHazard or FlowKind.CrossSellRate)) return;

        if (population is { RelationshipsWithoutAcquisitionDate: > 0 and var missing })
            issues.Add(new(ReadinessIssueCode.MissingAcquisitionDate,
                $"{missing} relationship(s) with our products in this segment have no acquisition date and contribute " +
                "no client-months."));
        if (unsegmentedHolders > 0 || (target.Flow == FlowKind.ChurnHazard && unattributedChurns > 0))
            issues.Add(new(ReadinessIssueCode.UnsegmentedEvidence,
                $"{unsegmentedHolders} firm(s) holding our products have no segment" +
                (target.Flow == FlowKind.ChurnHazard ? $" and {unattributedChurns} churn fact(s) could not be attributed" : "") +
                "; they are left out of every segment's fit."));

        if (target.Flow != FlowKind.ChurnHazard || population is null) return;
        if (population.ChurnEvents > population.EndedRelationships)
            issues.Add(new(ReadinessIssueCode.ChurnWithoutEndDate,
                $"{population.ChurnEvents} churn event(s) but {population.EndedRelationships} ended relationship(s): " +
                "a churned relationship without an end date keeps contributing exposure."));
        if (population is { MultiProductFirms: > 0, ProductlessChurnEvents: > 0 })
            issues.Add(new(ReadinessIssueCode.LogoLevelChurn,
                $"{population.ProductlessChurnEvents} churn fact(s) name no product while {population.MultiProductFirms} " +
                "firm(s) hold more than one of our products: logo-level events against relationship-level client-months. " +
                "Review the churn definition."));
        if (summary.Successes > 0 && summary.Exposure <= 0)
            issues.Add(new(ReadinessIssueCode.ExposureApproximated,
                "No relationship spans in this segment: the engine approximated exposure as churns x window / 2 " +
                "(the exposure shown is that approximation)."));
    }

    private static void AddArrivalIssues(List<ReadinessIssueDto> issues, Target target,
        IReadOnlyList<ReadinessArrivalSeriesDto> arrivals, int unattributed)
    {
        var flow = CalibrationInputBuilder.ToDomainFlow(target.Flow!.Value);
        var series = arrivals.FirstOrDefault(a => a.FlowKind == flow && a.ClientSegmentId?.ToString() == target.SegmentId);
        if (series is { MissingMonths.Count: > 0 })
            issues.Add(new(ReadinessIssueCode.ArrivalMonthsMissing,
                $"{series.MissingMonths.Count} month(s) between {series.FirstMonth:yyyy-MM} and {series.LastMonth:yyyy-MM} " +
                "have no observation. They are missing, not zero; load them or confirm the source has none."));
        if (series is { DuplicateMonths.Count: > 0 })
            issues.Add(new(ReadinessIssueCode.DuplicateArrivalMonth,
                $"{series.DuplicateMonths.Count} month(s) were observed more than once; each copy counted as a month of exposure."));
        if (unattributed > 0)
            issues.Add(new(ReadinessIssueCode.UnsegmentedEvidence,
                $"{unattributed} arrival fact(s) in the fit's window were unreadable or named no segment and were left out."));
    }

    // ---------------------------------------------------------------- populations

    private static List<ReadinessSegmentPopulationDto> BuildPopulations(ReadinessWorld world, CalibrationInput input,
        IReadOnlyList<FactEvent> fitFacts, IReadOnlyDictionary<Guid, Guid> segmentByFirm)
    {
        var asOf = world.Run.FactsAsOfDate;
        var windowStart = world.Run.ObservationsRecordedAfter;
        var segmentIds = world.Segments.Keys
            .Concat(input.FlowPriors.Select(p => ParseGuid(p.SegmentId)).OfType<Guid>())
            .Distinct()
            .OrderBy(id => world.Segments.GetValueOrDefault(id) ?? "￿", StringComparer.Ordinal)
            .ThenBy(id => id);

        var productlessChurns = fitFacts
            .Where(f => f.Kind == FactEventKind.Churn)
            .Select(f => (Fact: f, Payload: TryRead<ChurnFact>(f.PayloadJson)))
            .Where(x => x.Payload is null || x.Payload.ProductId is null)
            .Select(x => x.Payload is { ClientSegmentId: var s } && s != Guid.Empty
                ? s
                : x.Fact.SubjectId is { } firm ? segmentByFirm.GetValueOrDefault(firm) : Guid.Empty)
            .ToList();

        bool ActiveAt(FirmProductRelationship r) =>
            (r.AcquisitionDate is null || r.AcquisitionDate <= asOf) && (r.EndDate is null || r.EndDate > asOf);

        var result = new List<ReadinessSegmentPopulationDto>();
        foreach (var segmentId in segmentIds)
        {
            string seg = segmentId.ToString();
            var firms = world.Firms.Where(f => f.SegmentId == segmentId).ToList();
            var firmIds = firms.Select(f => f.Id).ToHashSet();
            var relationships = world.Relationships.Where(r => firmIds.Contains(r.FirmId)).ToList();
            var opportunities = input.Opportunities.Where(o => o.SegmentId == seg).ToList();

            int Decided(bool crossSell, OpportunityResult result) =>
                opportunities.Count(o => o.IsCrossSell == crossSell && o.Outcome == result);

            var moduleHolders = world.OurProducts.Where(p => p.Value.IsModule)
                .OrderBy(p => p.Value.Name, StringComparer.Ordinal)
                .Select(p => new ReadinessModuleHoldersDto(p.Key, p.Value.Name,
                    relationships.Where(r => r.ProductId == p.Key && ActiveAt(r)).Select(r => r.FirmId).Distinct().Count()))
                .ToList();

            var exposure = Summarize(input, new Target("", FlowKind.ChurnHazard, seg, null, null)).Exposure;

            result.Add(new ReadinessSegmentPopulationDto(
                segmentId,
                world.Segments.GetValueOrDefault(segmentId),
                firms.Count(f => f.Status == EpistemicStatus.Observed),
                firms.Count(f => f.Status == EpistemicStatus.Inferred),
                firms.Count(f => f.Status == EpistemicStatus.Synthetic),
                relationships
                    .Where(r => world.OurProducts.TryGetValue(r.ProductId, out var p) && !p.IsModule && ActiveAt(r))
                    .Select(r => r.FirmId).Distinct().Count(),
                moduleHolders,
                new ReadinessDecisionCountsDto(
                    Decided(false, OpportunityResult.Won), Decided(false, OpportunityResult.Lost),
                    Decided(false, OpportunityResult.NoDecision), Decided(true, OpportunityResult.Won),
                    Decided(true, OpportunityResult.Lost), Decided(true, OpportunityResult.NoDecision)),
                input.ChurnEvents.Count(c => c.SegmentId == seg),
                productlessChurns.Count(s => s == segmentId),
                relationships.Count,
                relationships.Count(r => r.AcquisitionDate is null),
                relationships.Count(r => r.EndDate is { } end && end <= asOf && (windowStart is null || end > windowStart)),
                relationships.GroupBy(r => r.FirmId).Count(g => g.Select(r => r.ProductId).Distinct().Count() > 1),
                exposure));
        }

        return result;
    }

    private static List<ReadinessArrivalSeriesDto> BuildArrivalSeries(CalibrationInput input) =>
        input.ArrivalCounts
            .GroupBy(a => (a.SegmentId, a.FlowKind))
            .OrderBy(g => g.Key.SegmentId, StringComparer.Ordinal).ThenBy(g => g.Key.FlowKind)
            .Select(g =>
            {
                var months = g.Select(a => a.Month).Distinct().Order().ToList();
                var missing = new List<DateOnly>();
                for (var m = months[0]; m < months[^1]; m = m.AddMonths(1))
                    if (!months.Contains(m)) missing.Add(m);
                return new ReadinessArrivalSeriesDto(
                    ParseGuid(g.Key.SegmentId),
                    CalibrationInputBuilder.ToDomainFlow(g.Key.FlowKind),
                    g.Count(),
                    g.Count(a => a.Count == 0),
                    months[0],
                    months[^1],
                    g.Sum(a => a.Exposure),
                    missing,
                    g.GroupBy(a => a.Month).Where(d => d.Count() > 1).Select(d => d.Key).Order().ToList());
            })
            .ToList();

    // ---------------------------------------------------------------- asserted

    private static List<ReadinessAssertedDto> BuildAsserted(ReadinessWorld world)
    {
        var asserted = world.PriorParameters
            .Where(p => !p.IsEngineParameter && Quantity(p.Key) is not null)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new ReadinessAssertedDto(p.Key, Quantity(p.Key)!, CalibrationReadinessStatus.Asserted,
                p.Provenance, p.Distribution, p.Rationale, 1))
            .ToList();
        if (world.IntegrationCount > 0)
            asserted.Add(new ReadinessAssertedDto("reference:integration-quality",
                "integration quality between products (Integration quality scores)", CalibrationReadinessStatus.Asserted,
                null, null, null, world.IntegrationCount));
        // The firm-level engine uses a documented default for every choice quantity the set does not define:
        // those are assumptions too (PRD8 R6), so they are listed rather than left implicit.
        var defined = world.PriorParameters.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var defaults = new List<(string Key, double Value)>
        {
            (ChoiceParameterKeys.SwitchingCostBase, ChoiceParameterKeys.DefaultSwitchingCostBase),
            (ChoiceParameterKeys.WorkaroundCostWeight, ChoiceParameterKeys.DefaultWorkaroundCostWeight),
            (ChoiceParameterKeys.ExpansionPropensity, ChoiceParameterKeys.DefaultExpansionPropensity),
            (ChoiceParameterKeys.NoDecisionUtility, ChoiceParameterKeys.DefaultNoDecisionUtility),
            (ChoiceParameterKeys.IntegrationWeight, ChoiceParameterKeys.DefaultIntegrationWeight),
            (ChoiceParameterKeys.PriceScale, ChoiceParameterKeys.DefaultPriceScale),
        };
        foreach (var segment in world.Segments.Keys.Order())
        {
            defaults.Add((ChoiceParameterKeys.PriceSensitivity(segment), ChoiceParameterKeys.DefaultPriceSensitivity));
            defaults.Add((ChoiceParameterKeys.ScopeClusterCount(segment), ChoiceParameterKeys.DefaultScopeClusterCount));
        }
        foreach (var (key, value) in defaults.Where(d => !defined.Contains(d.Key)).OrderBy(d => d.Key, StringComparer.Ordinal))
            asserted.Add(new ReadinessAssertedDto(key, Quantity(key)!, CalibrationReadinessStatus.Asserted,
                ParameterProvenance.Default, new DistributionSpecDto(DistributionType.Point, value),
                "Not defined in the set: the firm-level engine's documented default.", 1));
        if (world.ChannelProductBiasCount > 0)
            asserted.Add(new ReadinessAssertedDto("reference:channel-product-bias",
                "channel bias toward products (Channel product affinities)", CalibrationReadinessStatus.Asserted,
                null, null, null, world.ChannelProductBiasCount));
        return asserted;
    }

    /// <summary>The choice-model quantity a key names; null for keys calibration fits or the engine ignores.</summary>
    private static string? Quantity(string key) => key switch
    {
        ChoiceParameterKeys.SwitchingCostBase => "switching cost (base)",
        ChoiceParameterKeys.WorkaroundCostWeight => "workaround cost weight",
        ChoiceParameterKeys.ExpansionPropensity => "expansion propensity",
        ChoiceParameterKeys.NoDecisionUtility => "no-decision utility",
        ChoiceParameterKeys.IntegrationWeight => "integration weight",
        ChoiceParameterKeys.PriceScale => "price scale",
        _ when key.StartsWith(ChoiceParameterKeys.PriceSensitivityPrefix, StringComparison.Ordinal) => "price sensitivity",
        _ when key.StartsWith(ChoiceParameterKeys.ScopeClusterCountPrefix, StringComparison.Ordinal) => "scope cluster count",
        _ when key.StartsWith(ChoiceParameterKeys.ImportanceWeightPrefix, StringComparison.Ordinal) => "process importance weight (feature effect)",
        _ when key.StartsWith(ChoiceParameterKeys.BrandBiasPrefix, StringComparison.Ordinal) => "brand bias",
        _ => null // sales-cycle keys are fitted (or are the fit's own prior weight); other free-form keys are documentation
    };

    // ---------------------------------------------------------------- holdout

    private static ReadinessHoldoutDto BuildHoldout(ReadinessWorld world, IReadOnlyDictionary<Guid, Guid> segmentByFirm,
        DateOnly through)
    {
        var asOf = world.Run.FactsAsOfDate;
        // Recorded after the cut-off too: a fact effective later but recorded by then was already in the fit.
        var cutoff = Midnight(asOf.AddDays(1));
        var held = CalibrationInputBuilder.MapObservations(
            world.Facts.Where(f => f.EffectiveDate > asOf && f.EffectiveDate <= through && f.RecordedDate >= cutoff),
            segmentByFirm);
        var heldInput = world.Input with
        {
            AsOfDate = through,
            Opportunities = held.Opportunities,
            ChurnEvents = held.ChurnEvents,
            ArrivalCounts = held.ArrivalCounts,
            RelationshipSpans = []
        };

        var comparisons = new List<ReadinessHoldoutComparisonDto>();
        foreach (var flow in world.Output.FittedFlows)
        {
            var target = ForFlow(flow.SegmentId, flow.FlowKind, flow.ModuleId);
            var summary = Summarize(heldInput, target);
            ReadinessHoldoutComparisonDto Row(HoldoutComparisonStatus status, string? reason, double? exposure = null,
                double? rate = null, double? mean = null, (double, double)? interval = null, bool? inside = null) =>
                new(target.Key, CalibrationInputBuilder.ToDomainFlow(flow.FlowKind), ParseGuid(target.SegmentId),
                    ParseGuid(target.ModuleId), null, status, reason, summary.Observations,
                    status == HoldoutComparisonStatus.Compared ? summary.Successes : null, exposure, rate, mean,
                    interval?.Item1, interval?.Item2, inside);

            switch (flow.FlowKind)
            {
                case FlowKind.ChurnHazard or FlowKind.CrossSellRate:
                    comparisons.Add(Row(HoldoutComparisonStatus.Unsupported,
                        "Needs client-month exposure over the held-out period. Relationships are current state with no " +
                        "history of what was known when, so the exposure is not reconstructed from current data."));
                    break;
                case var _ when summary.Observations == 0:
                    comparisons.Add(Row(HoldoutComparisonStatus.Unsupported,
                        $"No held-out {(flow.FlowKind == FlowKind.WinRate ? "decided new-business opportunities" : "monthly observations")} " +
                        $"effective {asOf.AddDays(1):yyyy-MM-dd}..{through:yyyy-MM-dd}."));
                    break;
                case FlowKind.WinRate:
                {
                    double a = flow.Posterior.P1, b = flow.Posterior.P2;
                    int n = summary.Observations, k = summary.Successes;
                    var (lo, hi) = BetaBinomialInterval(n, a, b, HoldoutIntervalMass);
                    comparisons.Add(Row(HoldoutComparisonStatus.Compared, null, null, (double)k / n, a / (a + b),
                        ((double)lo / n, (double)hi / n), k >= lo && k <= hi));
                    break;
                }
                default: // arrivals: Gamma posterior recovered from its moment-matched LogNormal
                {
                    double sigmaSq = flow.Posterior.P2 * flow.Posterior.P2;
                    double mean = Math.Exp(flow.Posterior.P1 + 0.5 * sigmaSq);
                    double alpha = 1.0 / (Math.Exp(sigmaSq) - 1.0);
                    double beta = alpha / mean;
                    double e = summary.Exposure;
                    var (lo, hi) = GammaPoissonInterval(alpha, beta, e, HoldoutIntervalMass);
                    comparisons.Add(Row(HoldoutComparisonStatus.Compared, null, e, summary.Successes / e, mean,
                        (lo / e, hi / e), summary.Successes >= lo && summary.Successes <= hi));
                    break;
                }
            }
        }

        foreach (var cycle in world.Output.FittedSalesCycles)
        {
            var target = ForSalesCycle(cycle.SegmentId, cycle.ProductId);
            comparisons.Add(new ReadinessHoldoutComparisonDto(target.Key, null, ParseGuid(target.SegmentId), null,
                ParseGuid(target.ProductId), HoldoutComparisonStatus.Unsupported,
                "No held-out comparison is implemented for sales-cycle durations.",
                Summarize(heldInput, target).Observations, null, null, null, null, null, null, null));
        }

        return new ReadinessHoldoutDto(asOf.AddDays(1), through, HoldoutIntervalMass,
            $"Outcomes effective after the as-of date and on or before {through:yyyy-MM-dd}, as recorded now, against the " +
            $"central {HoldoutIntervalMass:P0} posterior-predictive interval for that sample size. A value inside the " +
            "interval is not a pass and one outside is a discrepancy to explain; small samples say little either way.",
            comparisons);
    }

    /// <summary>Central interval of the Beta-Binomial(n, a, b) predictive distribution of successes.</summary>
    internal static (int Lower, int Upper) BetaBinomialInterval(int n, double a, double b, double mass)
    {
        double tail = (1 - mass) / 2, cdf = 0;
        int lower = -1;
        for (int k = 0; k <= n; k++)
        {
            cdf += BetaBinomialPmf(n, k, a, b);
            if (lower < 0 && cdf >= tail) lower = k;
            if (cdf >= 1 - tail) return (lower, k);
        }
        return (Math.Max(lower, 0), n);
    }

    /// <summary>Central interval of the Gamma(alpha, beta)-Poisson predictive of the count over exposure e (negative binomial).</summary>
    internal static (int Lower, int Upper) GammaPoissonInterval(double alpha, double beta, double e, double mass)
    {
        double tail = (1 - mass) / 2, cdf = 0;
        int lower = -1;
        for (int k = 0; k < 100_000_000; k++)
        {
            cdf += GammaPoissonPmf(k, alpha, beta, e);
            if (lower < 0 && cdf >= tail) lower = k;
            if (cdf >= 1 - tail) return (lower, k);
        }
        throw new InvalidOperationException("Predictive interval did not converge.");
    }

    /// <summary>P(k successes in n) under Beta(a, b)-Binomial.</summary>
    internal static double BetaBinomialPmf(int n, int k, double a, double b) =>
        Math.Exp(LogChoose(n, k) + LogBeta(k + a, n - k + b) - LogBeta(a, b));

    /// <summary>P(count = k over exposure e) under a Gamma(alpha, beta)-Poisson: negative binomial with p = beta / (beta + e).</summary>
    internal static double GammaPoissonPmf(int k, double alpha, double beta, double e)
    {
        double p = beta / (beta + e);
        return Math.Exp(LogGamma(k + alpha) - LogGamma(alpha) - LogGamma(k + 1) + alpha * Math.Log(p) + k * Math.Log(1 - p));
    }

    private static double LogChoose(int n, int k) => LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);

    private static double LogBeta(double a, double b) => LogGamma(a) + LogGamma(b) - LogGamma(a + b);

    private static readonly double[] Lanczos =
    [
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
        -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
        1.5056327351493116e-7
    ];

    /// <summary>ln Γ(x) for x &gt; 0 (Lanczos, g = 7; ~15 significant digits).</summary>
    internal static double LogGamma(double x)
    {
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        x -= 1;
        double sum = Lanczos[0];
        for (int i = 1; i < Lanczos.Length; i++) sum += Lanczos[i] / (x + i);
        double t = x + 7.5;
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(sum);
    }

    // ---------------------------------------------------------------- helpers

    private static int MappedCount(int opportunities, int churns, int arrivals, FactEventKind kind) => kind switch
    {
        FactEventKind.OpportunityDecided => opportunities,
        FactEventKind.Churn => churns,
        _ => arrivals
    };

    private static DateTimeOffset Midnight(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static Guid? ParseGuid(string? s) => Guid.TryParse(s, out var g) ? g : null;

    private static T? TryRead<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return SimulationJson.Deserialize<T>(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
