using ProductManager.Domain.Common;
using ProductManager.Domain.Market;
using ProductManager.Domain.Simulation;
using ProductManager.Engine;
using DomainDistributionType = ProductManager.Domain.Common.DistributionType;
using EngineDistributionType = ProductManager.Engine.DistributionType;
using EngineRateMultiplier = ProductManager.Engine.EventRateMultiplier;

namespace ProductManager.Api.Simulation;

/// <summary>One observed covariate reading, lifted out of the bi-temporal facts ledger.</summary>
/// <param name="CovariateId">Which series the reading belongs to.</param>
/// <param name="EffectiveDate">When the value was true in the world.</param>
/// <param name="Value">The observed number.</param>
public sealed record CovariateObservation(Guid CovariateId, DateOnly EffectiveDate, double Value);

/// <summary>
/// A feature's coverage delta with the date the roadmap makes it real: the project that delivers
/// the feature is planned to finish then.
/// </summary>
/// <param name="ClientSegmentId">Segment the delta applies to; null = every segment.</param>
/// <param name="BusinessProcessId">Process whose coverage moves.</param>
/// <param name="ToTier">Tier the process reaches once the delta is live.</param>
/// <param name="LiveFrom">Planned end date of the delivering project.</param>
public sealed record PlannedCoverageDelta(
    Guid? ClientSegmentId,
    Guid BusinessProcessId,
    CoverageTier ToTier,
    DateOnly LiveFrom);

/// <summary>Everything the builder needs, already loaded — kept pure so it is directly testable.</summary>
public sealed record SimulationWorld(
    IReadOnlyList<ClientSegment> Segments,
    IReadOnlyList<Parameter> Parameters,
    IReadOnlyList<Covariate> Covariates,
    IReadOnlyList<CovariateObservation> CovariateObservations,
    IReadOnlyList<MarketEvent> Events,
    IReadOnlyList<Coverage> Coverages,
    IReadOnlySet<Guid> OurProductIds,
    IReadOnlyList<PlannedCoverageDelta> PlannedCoverageDeltas);

/// <summary>
/// Maps observed domain state onto the engine's pure input contract. Everything the engine reads
/// comes from here, and nothing here reaches back into the database — the resulting
/// <see cref="EngineInput"/> is the complete, self-contained snapshot a run is reproducible from.
/// </summary>
public static class SimulationInputBuilder
{
    /// <summary>Identifies the mapping + engine version a stored run was produced by.</summary>
    public const string ModelVersion = "engine-v0";

    // ---- Attractiveness v0 constants (see AttractivenessSchedule) ----

    /// <summary>Win-rate sensitivity to one full tier-scale of coverage advantage over the best rival.</summary>
    public const double AttractivenessSensitivity = 0.5;

    /// <summary>Floor on the win-rate multiplier: losing on coverage never zeroes the pipeline outright.</summary>
    public const double MinAttractiveness = 0.5;

    /// <summary>Cap on the win-rate multiplier: coverage alone never more than half again the win rate.</summary>
    public const double MaxAttractiveness = 1.5;

    private const double NeutralAttractiveness = 1.0;

    public static EngineInput Build(
        SimulationWorld world,
        DateOnly startDate,
        int horizonMonths,
        int paths,
        int seed,
        double discountRateAnnual,
        double terminalGrowthAnnual)
    {
        ArgumentNullException.ThrowIfNull(world);

        var segments = world.Segments
            .Select(s => new SegmentInput(
                s.Id.ToString(),
                s.StockState.InitialProspectPool,
                s.StockState.InitialClientCount,
                s.StockState.ArpuPerMonth,
                s.StockState.ImplementationFee,
                s.ModulePenetrations
                    .OrderBy(m => m.ModuleProductId)
                    .Select(m => new ModulePenetration(m.ModuleProductId.ToString(), m.Penetration, m.ArpuPerMonth))
                    .ToList(),
                new CostRates(
                    s.StockState.ServicingCostPerClient,
                    s.StockState.InfraFixedCostPerMonth,
                    s.StockState.InfraCostPerClient,
                    s.StockState.RdDragPerMonth)))
            .ToList();

        var segmentIds = world.Segments.Select(s => s.Id).ToHashSet();

        // Only parameters keyed the engine's way are engine input; free-form assumptions are
        // documentation and are deliberately ignored here rather than guessed at.
        var engineParameters = world.Parameters
            .Where(p => p.IsEngineParameter && segmentIds.Contains(p.ClientSegmentId!.Value))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToList();

        var linkedCovariateIds = engineParameters
            .Where(p => p.CovariateId is not null)
            .Select(p => p.CovariateId!.Value)
            .ToHashSet();

        var covariates = world.Covariates
            .Where(c => linkedCovariateIds.Contains(c.Id))
            .OrderBy(c => c.Id)
            .Select(c => BuildCovariate(c, world.CovariateObservations, startDate, horizonMonths))
            .ToList();

        var includedCovariateIds = covariates.Select(c => Guid.Parse(c.Id)).ToHashSet();

        var parameters = engineParameters
            .Select(p => new ParameterSpec(
                p.ClientSegmentId!.Value.ToString(),
                ToEngineFlow(p.FlowKind!.Value),
                p.ModuleProductId?.ToString(),
                ToEngineDistribution(p.Distribution),
                p.CovariateId is not null && includedCovariateIds.Contains(p.CovariateId.Value)
                    ? new CovariateLink(p.CovariateId.Value.ToString(), p.CovariateBeta ?? 0.0)
                    : null))
            .ToList();

        var events = world.Events
            .OrderBy(e => e.StartDate).ThenBy(e => e.Id)
            .Select(e => BuildEvent(e, startDate, horizonMonths))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        var attractiveness = BuildAttractivenessSchedule(world, startDate, horizonMonths);

        return new EngineInput(
            segments,
            parameters,
            covariates,
            events,
            attractiveness,
            horizonMonths,
            paths,
            seed,
            discountRateAnnual,
            terminalGrowthAnnual);
    }

    /// <summary>
    /// Attractiveness schedule v0 — how the roadmap reaches the market.
    ///
    /// For each segment s and month t:
    ///
    ///   coverageIndex(s,t) = mean over relevant processes p of ourTier(s,p,t)      in [0,1]
    ///   rivalIndex(s)      = mean over relevant processes p of bestRivalTier(s,p)  in [0,1]
    ///   multiplier(s,t)    = clamp(1 + K * (coverageIndex(s,t) - rivalIndex(s)), 0.5, 1.5),  K = 0.5
    ///
    /// where tiers use the domain's numeric mapping (gold 1.0 / silver 0.7 / bronze 0.4 / not
    /// playing 0.0), an unknown assessment contributes nothing, "relevant processes" are those any
    /// side has an assessment for or any planned feature touches, and
    ///
    ///   ourTier(s,p,t) = max(assessed tier today,
    ///                        best tier any coverage delta reaches whose delivering project is
    ///                        planned to finish in month t or earlier)
    ///
    /// so a feature raises attractiveness from the month its project lands, and never lowers it.
    /// The multiplier is 1.0 at parity with the best rival, monotone increasing in our coverage and
    /// monotone decreasing in theirs, and clamped so coverage alone can neither kill nor carry a
    /// win rate that the assumption set is responsible for. Rivals are static in v0 (competitor
    /// moves are a Phase 3/4 concern), so all movement over the horizon comes from the roadmap.
    ///
    /// Only months whose multiplier differs from 1.0 are emitted; the engine defaults the rest.
    /// </summary>
    private static List<AttractivenessEntry> BuildAttractivenessSchedule(
        SimulationWorld world, DateOnly startDate, int horizonMonths)
    {
        var schedule = new List<AttractivenessEntry>();

        foreach (var segment in world.Segments)
        {
            var applicableCoverages = world.Coverages
                .Where(c => !c.IsUnknown
                            && (c.ClientSegmentId is null || c.ClientSegmentId == segment.Id))
                .ToList();

            var applicableDeltas = world.PlannedCoverageDeltas
                .Where(d => d.ClientSegmentId is null || d.ClientSegmentId == segment.Id)
                .ToList();

            var processes = applicableCoverages.Select(c => c.BusinessProcessId)
                .Concat(applicableDeltas.Select(d => d.BusinessProcessId))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            if (processes.Count == 0) continue; // nothing assessed: stay neutral

            double rivalSum = 0.0;
            var ourBase = new Dictionary<Guid, double>(processes.Count);
            foreach (var process in processes)
            {
                double ours = 0.0;
                double rival = 0.0;
                foreach (var coverage in applicableCoverages)
                {
                    if (coverage.BusinessProcessId != process) continue;
                    double score = coverage.NumericScore ?? 0.0;
                    if (world.OurProductIds.Contains(coverage.ProductId))
                        ours = Math.Max(ours, score);
                    else
                        rival = Math.Max(rival, score);
                }

                ourBase[process] = ours;
                rivalSum += rival;
            }

            double rivalIndex = rivalSum / processes.Count;

            for (int month = 0; month < horizonMonths; month++)
            {
                double ourSum = 0.0;
                foreach (var process in processes)
                {
                    double best = ourBase[process];
                    foreach (var delta in applicableDeltas)
                    {
                        if (delta.BusinessProcessId != process) continue;
                        if (MonthIndex(startDate, delta.LiveFrom) > month) continue;
                        best = Math.Max(best, NumericTier(delta.ToTier));
                    }

                    ourSum += best;
                }

                double multiplier = Math.Clamp(
                    1.0 + AttractivenessSensitivity * (ourSum / processes.Count - rivalIndex),
                    MinAttractiveness,
                    MaxAttractiveness);

                if (Math.Abs(multiplier - NeutralAttractiveness) > 1e-12)
                    schedule.Add(new AttractivenessEntry(segment.Id.ToString(), month, multiplier));
            }
        }

        return schedule;
    }

    private static CovariateInput BuildCovariate(
        Covariate covariate,
        IReadOnlyList<CovariateObservation> observations,
        DateOnly startDate,
        int horizonMonths)
    {
        if (covariate.HasGbmSpec)
            return new CovariateInput(
                covariate.Id.ToString(),
                Gbm: new GbmSpec(covariate.GbmStart!.Value, covariate.GbmDrift!.Value, covariate.GbmVol!.Value));

        // No future-path spec: hold the latest observation flat. A constant series moves no rate
        // (the engine's covariate term is the elasticity form (x_t / x_0)^beta, which is 1 while
        // x_t == x_0), which is the honest reading of "we have history but no scenario". Holding
        // the level flat rather than normalising it is safe precisely because that term is
        // scale-free: an index held at 108.6 and one held at 1.0 give identical trajectories.
        var relevant = observations.Where(o => o.CovariateId == covariate.Id).ToList();
        var latest = relevant.Where(o => o.EffectiveDate <= startDate)
            .OrderByDescending(o => o.EffectiveDate)
            .FirstOrDefault()
            ?? relevant.OrderBy(o => o.EffectiveDate).FirstOrDefault();

        double level = latest?.Value ?? 1.0;
        var path = new double[horizonMonths];
        Array.Fill(path, level);
        return new CovariateInput(covariate.Id.ToString(), MonthlyPath: path);
    }

    private static EventInput? BuildEvent(MarketEvent marketEvent, DateOnly startDate, int horizonMonths)
    {
        int start = MonthIndex(startDate, marketEvent.StartDate);
        int end = marketEvent.EndDate is null ? horizonMonths - 1 : MonthIndex(startDate, marketEvent.EndDate.Value);
        if (end < 0 || start > horizonMonths - 1) return null; // entirely outside the horizon

        return new EventInput(
            marketEvent.Id.ToString(),
            Math.Max(0, start),
            Math.Min(horizonMonths - 1, end),
            marketEvent.RateMultipliers
                .Select(m => new EngineRateMultiplier(
                    m.ClientSegmentId?.ToString(),
                    ToEngineFlow(m.FlowKind),
                    m.Multiplier))
                .ToList());
    }

    /// <summary>Whole months from the run's month 0 to the month containing <paramref name="date"/>.</summary>
    public static int MonthIndex(DateOnly startDate, DateOnly date) =>
        (date.Year - startDate.Year) * 12 + (date.Month - startDate.Month);

    /// <summary>The domain's numeric mapping under the tier vocabulary, for choice math.</summary>
    private static double NumericTier(CoverageTier tier) => tier switch
    {
        CoverageTier.Gold => 1.0,
        CoverageTier.Silver => 0.7,
        CoverageTier.Bronze => 0.4,
        _ => 0.0
    };

    /// <summary>
    /// Domain flow kind to engine flow kind. Mapped by member name rather than by cast so a future
    /// reordering of either enum fails loudly here instead of silently mis-keying every parameter.
    /// </summary>
    public static FlowKind ToEngineFlow(SimulationFlowKind flowKind) => flowKind switch
    {
        SimulationFlowKind.NewFirmArrival => FlowKind.NewFirmArrival,
        SimulationFlowKind.ConversionToMarket => FlowKind.ConversionToMarket,
        SimulationFlowKind.WinRate => FlowKind.WinRate,
        SimulationFlowKind.ChurnHazard => FlowKind.ChurnHazard,
        SimulationFlowKind.CrossSellRate => FlowKind.CrossSellRate,
        _ => throw new ArgumentOutOfRangeException(nameof(flowKind), flowKind, "Unknown simulation flow kind.")
    };

    private static DistributionSpec ToEngineDistribution(Distribution distribution) => new(
        distribution.Type switch
        {
            DomainDistributionType.Point => EngineDistributionType.Point,
            DomainDistributionType.Normal => EngineDistributionType.Normal,
            DomainDistributionType.Lognormal => EngineDistributionType.LogNormal,
            DomainDistributionType.Beta => EngineDistributionType.Beta,
            _ => throw new ArgumentOutOfRangeException(nameof(distribution), distribution.Type, "Unknown distribution.")
        },
        distribution.ParamA,
        distribution.ParamB);
}
