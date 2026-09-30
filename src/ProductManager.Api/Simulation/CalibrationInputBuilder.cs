using System.Text.Json;
using ProductManager.Domain.Common;
using ProductManager.Domain.Facts;
using ProductManager.Domain.Market;
using ProductManager.Domain.Simulation;
using ProductManager.Engine;
using ProductManager.Engine.Calibration;
using DomainDistributionType = ProductManager.Domain.Common.DistributionType;
using EngineDistributionType = ProductManager.Engine.DistributionType;

namespace ProductManager.Api.Simulation;

/// <summary>Everything a calibration run reads, already loaded and already filtered to "as of".</summary>
/// <param name="AsOfDate">The date the fit reconstructs the world as of.</param>
/// <param name="Prior">The assumption set being updated.</param>
/// <param name="Facts">
/// Ledger entries with <c>recordedDate &lt;= asOf</c>. Filtering happens before this record is
/// built — the bi-temporal cut is the caller's responsibility, and the calibration module documents
/// that it receives only what was known.
/// </param>
/// <param name="Relationships">Active and ended relationships with our products, for client-month exposure.</param>
/// <param name="SegmentByFirm">Segment of each firm, for facts whose payload omits it.</param>
/// <param name="ObservationsRecordedAfter">
/// Lower bound of the observation window (PRD8). The caller has already dropped facts recorded on or
/// before it; here it clips relationship exposure to start at it, so the denominator covers the same
/// window as the numerator.
/// </param>
public sealed record CalibrationWorld(
    DateOnly AsOfDate,
    AssumptionSet Prior,
    IReadOnlyList<FactEvent> Facts,
    IReadOnlyList<FirmProductRelationship> Relationships,
    IReadOnlyDictionary<Guid, Guid> SegmentByFirm,
    DateOnly? ObservationsRecordedAfter = null);

/// <summary>The ledger observations one set of facts maps to, in the calibration contract's own shapes.</summary>
public sealed record CalibrationObservations(
    IReadOnlyList<OpportunityOutcome> Opportunities,
    IReadOnlyList<ChurnEvent> ChurnEvents,
    IReadOnlyList<ArrivalCount> ArrivalCounts);

/// <summary>
/// Maps the prior assumption set plus the bi-temporal facts ledger onto the pure calibration
/// module's input contract, and maps its output back into a fitted assumption set.
///
/// <para>
/// The ledger is the only source of history here. Opportunity decisions, churns and arrival counts
/// are read from facts (each carrying a self-contained payload — see
/// <see cref="OpportunityDecisionFact"/>, <see cref="ChurnFact"/>, <see cref="SegmentArrivalFact"/>),
/// never from the mutable market rows, so "what we knew as of D" survives later edits. Relationship
/// spans are the one exception and are read from current state, cut at the as-of date on both ends —
/// a relationship acquired after D contributes nothing, and one still open (or ended after D) is
/// clipped at D: a relationship's start and end are themselves near-immutable, and there is no
/// ledger of them yet.
/// </para>
///
/// <para>
/// <b>Remaining approximation.</b> That cut is by <em>effective</em> date, while the fact-derived
/// numerators are cut by <em>recorded</em> date, so a relationship row entered after D but acquired
/// before it still contributes exposure the numerator cannot match. Spans are therefore an upper
/// bound on the client-months we knew about, which biases exposure-denominated hazards downward.
/// The calibration module refuses to fit those parameters at all when the event count is zero,
/// which is where that bias would otherwise do real damage.
/// </para>
/// </summary>
public static class CalibrationInputBuilder
{
    /// <summary>Identifies the mapping + calibration version a stored run was produced by.</summary>
    public const string ModelVersion = "calibration-v1";

    public static CalibrationInput Build(CalibrationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var flowPriors = world.Prior.Parameters
            .Where(p => p.IsEngineParameter)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new FlowPrior(
                p.ClientSegmentId!.Value.ToString(),
                SimulationInputBuilder.ToEngineFlow(p.FlowKind!.Value),
                p.ModuleProductId?.ToString(),
                SimulationInputBuilder.ToEngineDistribution(p.Distribution),
                ToCalibrationProvenance(p.Provenance),
                p.Rationale))
            .ToList();

        var salesCyclePriors = BuildSalesCyclePriors(world.Prior);
        var observations = MapObservations(world.Facts, world.SegmentByFirm);

        var windowStart = world.ObservationsRecordedAfter;
        var spans = world.Relationships
            .Where(r => r.AcquisitionDate is not null && r.AcquisitionDate.Value <= world.AsOfDate)
            .Where(r => windowStart is null || r.EndDate is null || r.EndDate > windowStart)
            .Select(r => (Relationship: r, SegmentId: world.SegmentByFirm.GetValueOrDefault(r.FirmId)))
            .Where(x => x.SegmentId != Guid.Empty)
            .OrderBy(x => x.Relationship.Id)
            .Select(x => new RelationshipSpan(
                x.SegmentId.ToString(),
                windowStart is { } w && w > x.Relationship.AcquisitionDate!.Value ? w : x.Relationship.AcquisitionDate!.Value,
                x.Relationship.EndDate is { } end && end <= world.AsOfDate ? end : null))
            .ToList();

        return new CalibrationInput(world.AsOfDate, flowPriors, salesCyclePriors, observations.Opportunities,
            observations.ChurnEvents, observations.ArrivalCounts, spans);
    }

    /// <summary>
    /// Maps ledger facts onto observations. Facts that cannot be attributed (no segment, an unreadable
    /// payload, an unknown outcome) are dropped — the readiness report counts them.
    /// </summary>
    public static CalibrationObservations MapObservations(IEnumerable<FactEvent> facts,
        IReadOnlyDictionary<Guid, Guid> segmentByFirm)
    {
        var opportunities = new List<OpportunityOutcome>();
        var churns = new List<ChurnEvent>();
        var arrivals = new List<ArrivalCount>();

        foreach (var fact in facts.OrderBy(f => f.EffectiveDate).ThenBy(f => f.RecordedDate).ThenBy(f => f.Id))
        {
            switch (fact.Kind)
            {
                case FactEventKind.OpportunityDecided:
                    if (TryRead<OpportunityDecisionFact>(fact.PayloadJson, out var decision))
                    {
                        var segmentId = decision.ClientSegmentId
                                        ?? segmentByFirm.GetValueOrDefault(decision.FirmId);
                        if (segmentId == Guid.Empty) break;
                        var outcome = ToOutcome(decision.Outcome);
                        if (outcome is null) break;

                        opportunities.Add(new OpportunityOutcome(
                            segmentId.ToString(),
                            decision.ChannelId?.ToString(),
                            decision.OpenedDate,
                            decision.DecidedDate,
                            outcome,
                            decision.ScopeProcessIds.Select(p => p.ToString()).ToList(),
                            decision.WinningProductId?.ToString(),
                            decision.IsCrossSell,
                            decision.CrossSellModuleProductId?.ToString()));
                    }

                    break;

                case FactEventKind.Churn:
                    var churnSegment = TryRead<ChurnFact>(fact.PayloadJson, out var churn)
                        ? churn.ClientSegmentId
                        : Guid.Empty;
                    if (churnSegment == Guid.Empty && fact.SubjectId is { } subject)
                        churnSegment = segmentByFirm.GetValueOrDefault(subject);
                    if (churnSegment != Guid.Empty)
                        churns.Add(new ChurnEvent(churnSegment.ToString(), fact.EffectiveDate));
                    break;

                case FactEventKind.SegmentArrivalObserved:
                    if (TryRead<SegmentArrivalFact>(fact.PayloadJson, out var arrival)
                        && arrival.ClientSegmentId != Guid.Empty
                        && arrival.Count >= 0
                        && arrival.Exposure > 0
                        && arrival.FlowKind is SimulationFlowKind.NewFirmArrival
                            or SimulationFlowKind.ConversionToMarket)
                    {
                        arrivals.Add(new ArrivalCount(
                            arrival.ClientSegmentId.ToString(),
                            SimulationInputBuilder.ToEngineFlow(arrival.FlowKind),
                            new DateOnly(fact.EffectiveDate.Year, fact.EffectiveDate.Month, 1),
                            arrival.Count,
                            arrival.FlowKind == SimulationFlowKind.NewFirmArrival ? 1.0 : arrival.Exposure));
                    }

                    break;
            }
        }

        return new CalibrationObservations(opportunities, churns, arrivals);
    }

    /// <summary>
    /// Builds the fitted assumption set: a branch of the prior (so every unfitted parameter travels
    /// unchanged and the lineage is explicit) with each fitted posterior written back over its key,
    /// provenance Calibrated and the module's own rationale. Covariate links survive the fit — a
    /// refitted rate is still the same rate, still elastic to the same series. <paramref name="stamps"/>
    /// (by parameter key; sales cycles by their mu key) is the prior evidence each fitted parameter now
    /// carries — see <see cref="CalibrationEvidence.Stamps"/>. Unfitted parameters keep their own.
    /// </summary>
    public static AssumptionSet BuildFittedSet(AssumptionSet prior, CalibrationOutput output, string name,
        IReadOnlyDictionary<string, PriorEvidence>? stamps = null)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(output);

        var fitted = prior.Branch(name);
        var priorByKey = prior.Parameters.ToDictionary(p => p.Key, StringComparer.Ordinal);

        foreach (var flow in output.FittedFlows)
        {
            if (!Guid.TryParse(flow.SegmentId, out var segmentId)) continue;
            Guid? moduleId = Guid.TryParse(flow.ModuleId, out var module) ? module : null;
            var flowKind = ToDomainFlow(flow.FlowKind);
            var key = Parameter.EngineKey(flowKind, segmentId, moduleId);
            priorByKey.TryGetValue(key, out var previous);

            fitted.SetEngineParameter(
                segmentId,
                flowKind,
                moduleId,
                ToDomainDistribution(flow.Posterior),
                ParameterProvenance.Calibrated,
                flow.AutoRationale,
                previous?.CovariateId,
                previous?.CovariateBeta,
                stamps?.GetValueOrDefault(key));
        }

        foreach (var cycle in output.FittedSalesCycles)
        {
            if (!Guid.TryParse(cycle.SegmentId, out var segmentId)) continue;
            Guid? productId = Guid.TryParse(cycle.ProductId, out var product) ? product : null;
            var stamp = stamps?.GetValueOrDefault(ChoiceParameterKeys.SalesCycleMu(segmentId, productId));

            fitted.SetParameter(ChoiceParameterKeys.SalesCycleMu(segmentId, productId),
                Distribution.PointValue(cycle.Mu), ParameterProvenance.Calibrated, cycle.AutoRationale, stamp);
            fitted.SetParameter(ChoiceParameterKeys.SalesCycleSigma(segmentId, productId),
                Distribution.PointValue(cycle.Sigma), ParameterProvenance.Calibrated, cycle.AutoRationale, stamp?.Copy());
        }

        return fitted;
    }

    private static List<SalesCyclePrior> BuildSalesCyclePriors(AssumptionSet prior)
    {
        var byKey = prior.Parameters
            .Where(p => !p.IsEngineParameter)
            .GroupBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        double priorWeight = byKey.TryGetValue(ChoiceParameterKeys.SalesCyclePriorWeight, out var weight)
            ? ChoiceParameterKeys.Mean(weight.Distribution)
            : ChoiceParameterKeys.DefaultSalesCyclePriorWeight;
        if (priorWeight <= 0) priorWeight = ChoiceParameterKeys.DefaultSalesCyclePriorWeight;

        var priors = new List<SalesCyclePrior>();
        foreach (var (key, parameter) in byKey.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!ChoiceParameterKeys.TryParseSalesCycleKey(
                    key, ChoiceParameterKeys.SalesCycleMuPrefix, out var segmentId, out var productId))
                continue;

            double sigma = byKey.TryGetValue(ChoiceParameterKeys.SalesCycleSigma(segmentId, productId), out var s)
                ? ChoiceParameterKeys.Mean(s.Distribution)
                : 0.0;

            priors.Add(new SalesCyclePrior(
                segmentId.ToString(),
                productId?.ToString(),
                ChoiceParameterKeys.Mean(parameter.Distribution),
                Math.Max(1e-6, sigma),
                priorWeight));
        }

        return priors;
    }

    private static OpportunityResult? ToOutcome(string outcome) => outcome switch
    {
        OpportunityDecisionFact.WonOutcome => OpportunityResult.Won,
        OpportunityDecisionFact.LostOutcome => OpportunityResult.Lost,
        OpportunityDecisionFact.NoDecisionOutcome => OpportunityResult.NoDecision,
        _ => null
    };

    /// <summary>Mapped by member name so a reordering of either enum fails loudly instead of mis-labelling a fit.</summary>
    public static CalibrationProvenance ToCalibrationProvenance(ParameterProvenance provenance) => provenance switch
    {
        ParameterProvenance.Asserted => CalibrationProvenance.Asserted,
        ParameterProvenance.Calibrated => CalibrationProvenance.Calibrated,
        ParameterProvenance.Default => CalibrationProvenance.Default,
        _ => throw new ArgumentOutOfRangeException(nameof(provenance), provenance, "Unknown provenance.")
    };

    public static ParameterProvenance ToDomainProvenance(CalibrationProvenance provenance) => provenance switch
    {
        CalibrationProvenance.Asserted => ParameterProvenance.Asserted,
        CalibrationProvenance.Calibrated => ParameterProvenance.Calibrated,
        CalibrationProvenance.Default => ParameterProvenance.Default,
        _ => throw new ArgumentOutOfRangeException(nameof(provenance), provenance, "Unknown provenance.")
    };

    public static SimulationFlowKind ToDomainFlow(FlowKind flowKind) => flowKind switch
    {
        FlowKind.NewFirmArrival => SimulationFlowKind.NewFirmArrival,
        FlowKind.ConversionToMarket => SimulationFlowKind.ConversionToMarket,
        FlowKind.WinRate => SimulationFlowKind.WinRate,
        FlowKind.ChurnHazard => SimulationFlowKind.ChurnHazard,
        FlowKind.CrossSellRate => SimulationFlowKind.CrossSellRate,
        _ => throw new ArgumentOutOfRangeException(nameof(flowKind), flowKind, "Unknown flow kind.")
    };

    public static Distribution ToDomainDistribution(DistributionSpec spec) => new(
        spec.Type switch
        {
            EngineDistributionType.Point => DomainDistributionType.Point,
            EngineDistributionType.Normal => DomainDistributionType.Normal,
            EngineDistributionType.LogNormal => DomainDistributionType.Lognormal,
            EngineDistributionType.Beta => DomainDistributionType.Beta,
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Type, "Unknown distribution.")
        },
        spec.P1,
        spec.Type == EngineDistributionType.Point ? 0.0 : spec.P2);

    private static bool TryRead<T>(string? json, out T value) where T : class
    {
        value = null!;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(json, SimulationJson.Options);
            if (parsed is null) return false;
            value = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
