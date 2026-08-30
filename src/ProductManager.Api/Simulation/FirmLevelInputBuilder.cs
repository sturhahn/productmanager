using System.Text.Json;
using ProductManager.Domain.Common;
using ProductManager.Domain.Market;
using ProductManager.Domain.Simulation;
using ProductManager.Engine;
using DomainCoverage = ProductManager.Domain.Market.Coverage;
using DomainProduct = ProductManager.Domain.Market.Product;
using EngineProductPrice = ProductManager.Engine.ProductPrice;

namespace ProductManager.Api.Simulation;

/// <summary>Everything the firm-level builder needs, already loaded — kept pure so it is directly testable.</summary>
public sealed record FirmLevelWorld(
    IReadOnlyList<ClientSegment> Segments,
    IReadOnlyList<BusinessProcess> Processes,
    IReadOnlyList<Firm> Firms,
    IReadOnlyList<FirmProductRelationship> Relationships,
    IReadOnlyList<DomainProduct> Products,
    IReadOnlyList<DomainCoverage> Coverages,
    IReadOnlyList<Channel> Channels,
    IReadOnlyList<Integration> Integrations,
    IReadOnlyList<Need> Needs,
    IReadOnlyList<Parameter> Parameters,
    IReadOnlyList<Covariate> Covariates,
    IReadOnlyList<CovariateObservation> CovariateObservations,
    IReadOnlyList<MarketEvent> Events,
    IReadOnlyList<PlannedCoverageDelta> PlannedCoverageDeltas);

/// <summary>
/// Maps observed domain state onto engine v1's firm-level contract, the way
/// <see cref="SimulationInputBuilder"/> does for engine v0. Nothing here touches the database: the
/// resulting <see cref="FirmLevelInput"/> is the complete snapshot a run is reproducible from.
///
/// <para><b>Where each piece of the choice model comes from</b></para>
/// <list type="bullet">
/// <item>processes and their clusters: the BusinessProcess catalog — a process's cluster is its
/// parent process (a root process forms its own cluster), which is what makes "bought together"
/// derive from the catalog rather than from a second, redundant grouping concept;</item>
/// <item>demand: segment-level <see cref="Need"/> rows (importance, hard/soft, workaround cost),
/// with the importance <em>distribution</em> optionally overridden per (segment, process) from the
/// assumption set — see <see cref="ChoiceParameterKeys"/>;</item>
/// <item>supply: the <see cref="DomainCoverage"/> grid, numerically scored;</item>
/// <item>incumbency: active <see cref="FirmProductRelationship"/> edges, including which processes
/// each holds and the price level actually paid;</item>
/// <item>routes to market: <see cref="Channel"/> reach × origination share, product bias, and the
/// carried-product filter;</item>
/// <item>knobs: assumption-set parameters under the <c>choice.*</c> / <c>utility.*</c> keys;</item>
/// <item>flows, covariates and events: exactly the v0 records, reused rather than re-mapped.</item>
/// </list>
/// </summary>
public static class FirmLevelInputBuilder
{
    /// <summary>Identifies the mapping + engine version a stored run was produced by.</summary>
    public const string ModelVersion = "engine-v1-firm";

    /// <summary>
    /// Required coverage assumed for a hard need whose depth JSON does not state one: the candidate
    /// has to be at least playing (bronze). A hard need with a required coverage of 0 would never
    /// disqualify anybody, which is not what "hard" means.
    /// </summary>
    public const double HardNeedDefaultRequiredCoverage = 0.4;

    /// <summary>Property read out of <c>Need.RequiredDepthJson</c> for the coverage floor, in [0, 1].</summary>
    public const string RequiredCoverageProperty = "requiredCoverage";

    /// <summary>Property read out of <c>Coverage.ScaleLimits</c> to mark a cell as unable to onboard new clients.</summary>
    public const string HasCapacityProperty = "hasCapacity";

    public static FirmLevelInput Build(
        FirmLevelWorld world,
        DateOnly startDate,
        int horizonMonths,
        int paths,
        int seed,
        double discountRateAnnual,
        double terminalGrowthAnnual)
    {
        ArgumentNullException.ThrowIfNull(world);

        var processes = world.Processes.OrderBy(p => p.Id).ToList();
        var processIds = processes.Select(p => p.Id).ToHashSet();
        var segments = world.Segments.OrderBy(s => s.Id).ToList();
        var segmentIds = segments.Select(s => s.Id).ToHashSet();
        var products = world.Products.OrderBy(p => p.Id).ToList();
        var productIds = products.Select(p => p.Id).ToHashSet();

        var knobs = new ParameterLookup(world.Parameters);

        // A process's cluster is its parent; a root process is its own cluster. Sub-processes of one
        // parent therefore tend to be scoped together, and the parent travels with them.
        var processInputs = processes
            .Select(p => new ProcessInput(p.Id.ToString(), (p.ParentProcessId ?? p.Id).ToString()))
            .ToList();

        var coverage = BuildCoverageIndex(world, segmentIds, processIds, startDate, horizonMonths);

        var segmentInputs = segments
            .Select(s => BuildSegment(s, world, products, knobs, processIds))
            .ToList();

        var productInputs = products
            .Select(p => new FirmLevelProductInput(
                p.Id.ToString(),
                p.IsOurs,
                coverage.Cells.TryGetValue(p.Id, out var cells) ? cells : [],
                BuildPricing(p, segments, coverage),
                world.Integrations
                    .Where(i => (i.ProductAId == p.Id && productIds.Contains(i.ProductBId))
                                || (i.ProductBId == p.Id && productIds.Contains(i.ProductAId)))
                    .OrderBy(i => i.ProductAId == p.Id ? i.ProductBId : i.ProductAId)
                    .Select(i => new IntegrationEdge(
                        (i.ProductAId == p.Id ? i.ProductBId : i.ProductAId).ToString(), i.QualityScore))
                    .ToList(),
                segments
                    .Select(s => (s.Id, Bias: knobs.Scalar(ChoiceParameterKeys.BrandBias(p.Id, s.Id), 0.0)))
                    .Where(x => x.Bias != 0.0)
                    .Select(x => new BrandBias(x.Id.ToString(), x.Bias))
                    .ToList(),
                coverage.Steps.TryGetValue(p.Id, out var steps) ? steps : null))
            .ToList();

        var firmInputs = BuildFirms(world, segmentIds, processIds, productIds, coverage.Cells);

        var channelInputs = world.Channels
            .OrderBy(c => c.Id)
            .Select(c => new ChannelInput(
                c.Id.ToString(),
                c.SegmentReaches
                    .Where(r => segmentIds.Contains(r.ClientSegmentId))
                    .OrderBy(r => r.ClientSegmentId)
                    .Select(r => new SegmentShare(r.ClientSegmentId.ToString(), r.Reach))
                    .ToList(),
                c.OriginatesOpportunities
                    ? c.SegmentReaches
                        .Where(r => segmentIds.Contains(r.ClientSegmentId) && r.OriginationShare > 0)
                        .OrderBy(r => r.ClientSegmentId)
                        .Select(r => new SegmentShare(r.ClientSegmentId.ToString(), r.OriginationShare))
                        .ToList()
                    : [],
                c.ProductAffinities
                    .Where(a => productIds.Contains(a.ProductId))
                    .OrderBy(a => a.ProductId)
                    .Select(a => new ChannelProductBias(a.ProductId.ToString(), a.Bias))
                    .ToList(),
                c.CarriesOnlyListedProducts
                    ? c.ProductAffinities
                        .Where(a => productIds.Contains(a.ProductId))
                        .OrderBy(a => a.ProductId)
                        .Select(a => a.ProductId.ToString())
                        .ToList()
                    : null))
            .ToList();

        var choice = new ChoiceParameters(
            knobs.Scalar(ChoiceParameterKeys.SwitchingCostBase, ChoiceParameterKeys.DefaultSwitchingCostBase),
            knobs.Scalar(ChoiceParameterKeys.WorkaroundCostWeight, ChoiceParameterKeys.DefaultWorkaroundCostWeight),
            knobs.Scalar(ChoiceParameterKeys.ExpansionPropensity, ChoiceParameterKeys.DefaultExpansionPropensity),
            BuildSalesCycles(knobs, segmentIds, productIds),
            knobs.Scalar(ChoiceParameterKeys.NoDecisionUtility, ChoiceParameterKeys.DefaultNoDecisionUtility),
            knobs.Scalar(ChoiceParameterKeys.IntegrationWeight, ChoiceParameterKeys.DefaultIntegrationWeight),
            knobs.Scalar(ChoiceParameterKeys.PriceScale, ChoiceParameterKeys.DefaultPriceScale));

        var scoping = segments
            .Select(s => new OpportunityScoping(
                s.Id.ToString(),
                s.SuiteAffinity,
                knobs.Distribution(ChoiceParameterKeys.ScopeClusterCount(s.Id))
                    ?? DistributionSpec.Point(ChoiceParameterKeys.DefaultScopeClusterCount)))
            .ToList();

        // Flow parameters, covariates and events are the v0 records verbatim — the firm-level engine
        // reads the same arrival/conversion/churn/cross-sell rates.
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
            .Select(c => SimulationInputBuilder.BuildCovariate(
                c, world.CovariateObservations, startDate, horizonMonths))
            .ToList();

        var includedCovariateIds = covariates.Select(c => Guid.Parse(c.Id)).ToHashSet();

        var parameters = engineParameters
            .Select(p => new ParameterSpec(
                p.ClientSegmentId!.Value.ToString(),
                SimulationInputBuilder.ToEngineFlow(p.FlowKind!.Value),
                p.ModuleProductId?.ToString(),
                SimulationInputBuilder.ToEngineDistribution(p.Distribution),
                p.CovariateId is not null && includedCovariateIds.Contains(p.CovariateId.Value)
                    ? new CovariateLink(p.CovariateId.Value.ToString(), p.CovariateBeta ?? 0.0)
                    : null))
            .ToList();

        var events = world.Events
            .OrderBy(e => e.StartDate).ThenBy(e => e.Id)
            .Select(e => SimulationInputBuilder.BuildEvent(e, startDate, horizonMonths))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        return new FirmLevelInput(
            segmentInputs,
            processInputs,
            firmInputs,
            productInputs,
            channelInputs,
            choice,
            scoping,
            parameters,
            covariates,
            events,
            horizonMonths,
            paths,
            seed,
            discountRateAnnual,
            terminalGrowthAnnual);
    }

    // ---------- Segments ----------

    private static FirmLevelSegmentInput BuildSegment(
        ClientSegment segment,
        FirmLevelWorld world,
        IReadOnlyList<DomainProduct> products,
        ParameterLookup knobs,
        IReadOnlySet<Guid> processIds)
    {
        var needs = world.Needs
            .Where(n => n.ClientSegmentId == segment.Id && processIds.Contains(n.BusinessProcessId))
            .OrderBy(n => n.BusinessProcessId)
            .Select(n => new ProcessNeed(
                n.BusinessProcessId.ToString(),
                knobs.Distribution(ChoiceParameterKeys.ImportanceWeight(segment.Id, n.BusinessProcessId))
                    ?? DistributionSpec.Point(n.ImportanceWeight),
                n.IsHard,
                ReadRequiredCoverage(n),
                (double)(n.WorkaroundCostPerPeriod?.Amount ?? 0m)))
            .ToList();

        return new FirmLevelSegmentInput(
            segment.Id.ToString(),
            segment.StockState.InitialProspectPool,
            BuildClientTargets(segment, products),
            segment.ApproachAdoptions
                .OrderBy(a => a.ApproachId)
                .Select(a => new ApproachShare(a.ApproachId.ToString(), a.Share))
                .ToList(),
            needs,
            knobs.Distribution(ChoiceParameterKeys.PriceSensitivity(segment.Id))
                ?? DistributionSpec.Point(ChoiceParameterKeys.DefaultPriceSensitivity),
            segment.ConsolidationPreference,
            new CostRates(
                segment.StockState.ServicingCostPerClient,
                segment.StockState.InfraFixedCostPerMonth,
                segment.StockState.InfraCostPerClient,
                segment.StockState.RdDragPerMonth));
    }

    /// <summary>
    /// Turns the segment's measured stocks into per-product client targets the engine fills with
    /// synthetic firms. The measured state carries one client count, so it belongs to one product:
    /// the lowest-id of our root products priced in the segment (the flagship). Modules take their
    /// share of that count from the recorded penetration — which is exactly what penetration means.
    /// </summary>
    private static List<InitialClientTarget> BuildClientTargets(
        ClientSegment segment, IReadOnlyList<DomainProduct> products)
    {
        var targets = new List<InitialClientTarget>();
        int clients = segment.StockState.InitialClientCount;
        if (clients <= 0) return targets;

        var flagship = products.FirstOrDefault(p => p.IsOurs && !p.IsModule && HasPriceIn(p, segment.Id));
        if (flagship is not null)
            targets.Add(new InitialClientTarget(flagship.Id.ToString(), clients));

        foreach (var penetration in segment.ModulePenetrations.OrderBy(m => m.ModuleProductId))
        {
            var module = products.FirstOrDefault(p => p.Id == penetration.ModuleProductId);
            if (module is null || !module.IsOurs) continue;
            int count = (int)Math.Round(clients * penetration.Penetration, MidpointRounding.AwayFromZero);
            if (count > 0) targets.Add(new InitialClientTarget(module.Id.ToString(), count));
        }

        return targets;
    }

    private static bool HasPriceIn(DomainProduct product, Guid segmentId) =>
        product.Prices.Any(p => p.ClientSegmentId == segmentId || p.ClientSegmentId is null);

    /// <summary>
    /// Maps the domain's per-segment list price onto the engine's <em>per-process</em> ARPU.
    ///
    /// <para>
    /// The units matter and were the source of a real distortion. The domain records what a firm
    /// pays for the whole product per period; the engine multiplies
    /// <see cref="EngineProductPrice.ArpuPerProcessPerMonth"/> by the number of in-scope processes
    /// the candidate actually covers — both to price the deal in the utility term and to book its
    /// ARR. Handing it the whole-product price therefore charged (and booked) the full subscription
    /// once per covered process, inflating a full-scope win by the product's process count.
    /// </para>
    ///
    /// <para>
    /// Dividing by the product's covered-process count in that segment — the distinct processes it
    /// has any positive coverage for, now or at any roadmap step, floored at 1 — restores the
    /// intended semantics: a win on the product's full scope earns approximately the whole list
    /// price, and a win on part of it earns that fraction. Implementation fees are per relationship,
    /// not per process, and are passed through untouched.
    /// </para>
    ///
    /// <para>
    /// Roadmap steps count toward the divisor even before they land, because pricing has no time
    /// dimension and one number has to serve the whole run. That slightly understates the revenue of
    /// a win taken before a new process is delivered; the alternative — a month-0-only divisor —
    /// would let a full-scope win after delivery earn <em>more</em> than the list price, which is
    /// the class of error this whole method exists to remove. Deltas that raise an existing cell's
    /// tier (the common case) do not move the divisor at all.
    /// </para>
    ///
    /// <para>
    /// The divisor ignores the approach dimension, so a process a product only covers under a
    /// specific approach counts in full even where the segment's approach mix resolves it to zero.
    /// The effect is a slightly low per-process ARPU for such products; the alternative would be to
    /// re-implement the engine's approach-mix resolution here, which is worse.
    /// </para>
    /// </summary>
    private static List<EngineProductPrice> BuildPricing(
        DomainProduct product, IReadOnlyList<ClientSegment> segments, CoverageIndex coverage)
    {
        var prices = new List<EngineProductPrice>();
        foreach (var segment in segments)
        {
            var price = product.Prices.FirstOrDefault(p => p.ClientSegmentId == segment.Id)
                        ?? product.Prices.FirstOrDefault(p => p.ClientSegmentId is null);
            if (price is null) continue; // not sellable in this segment: excluded from consideration

            prices.Add(new EngineProductPrice(
                segment.Id.ToString(),
                (double)price.RecurringPricePerPeriod.Amount / coverage.CoveredProcessCount(product.Id, segment.Id),
                (double)price.ImplementationFee.Amount));
        }

        return prices;
    }

    // ---------- Coverage ----------

    /// <summary>The coverage grid as the engine takes it: month-0 cells, roadmap steps, and the
    /// (product, segment) process counts the per-process ARPU is derived from.</summary>
    private sealed record CoverageIndex(
        Dictionary<Guid, List<CoverageCell>> Cells,
        Dictionary<Guid, List<CoverageStep>> Steps,
        Dictionary<Guid, HashSet<(Guid Process, string? Segment)>> Covered)
    {
        /// <summary>
        /// Distinct processes a product covers in a segment at any point in the horizon, floored at
        /// 1 — the divisor turning a whole-product list price into the engine's per-process ARPU.
        /// Generic cells count everywhere; a cell scoped to another segment does not count here,
        /// so a product does not look broader (and therefore cheaper per process) in a segment than
        /// it actually is.
        /// </summary>
        public int CoveredProcessCount(Guid productId, Guid segmentId)
        {
            if (!Covered.TryGetValue(productId, out var covered)) return 1;
            string segment = segmentId.ToString();
            int count = covered
                .Where(x => x.Segment is null || x.Segment == segment)
                .Select(x => x.Process)
                .Distinct()
                .Count();
            return Math.Max(1, count);
        }
    }

    /// <summary>
    /// Maps the coverage grid onto the engine's cells per product and folds the roadmap in as
    /// time-phased steps.
    ///
    /// <para>
    /// <b>Segments are carried through, not collapsed.</b> A coverage row assessed for one segment
    /// becomes a segment-scoped <see cref="CoverageCell"/> that applies to firms in that segment
    /// only and shadows the generic (segment-less) cell with the same (process, approach) key.
    /// Taking the best assessment across segments — the previous behaviour — quietly granted every
    /// segment the strongest cell anyone had, which is the opposite of what a per-segment
    /// assessment means.
    /// </para>
    ///
    /// <para>
    /// <b>The roadmap enters as steps at the delivering project's planned end month.</b> Each
    /// planned coverage delta becomes a <see cref="CoverageStep"/> on the (process, approach?,
    /// segment?) cell it targets, at <c>MonthIndex(startDate, LiveFrom)</c>: before that month the
    /// cell keeps its assessed score, from it onward the delivered tier. That replaces the old
    /// horizon-mean approximation (one blended score for the whole run), so a deal opened in month
    /// 2 sees month-2 coverage rather than an average of the whole horizon — which is what makes
    /// "ship it in Q1 vs Q3" a real difference in early wins rather than a uniform dilution.
    /// A delta whose tier does not exceed what the cell already resolves to produces no step
    /// (already shipped, or at/below the assessed tier); steps on one cell are made monotone, so a
    /// later, smaller delta never walks coverage back down. A delta landing outside the horizon has
    /// no effect on the run; one dated in the past applies from month 0.
    /// </para>
    ///
    /// <para>
    /// Deltas are not product-targeted: a coverage delta names a process (and optionally an approach
    /// and a segment), so it lifts every one of our products that plays there — a known
    /// approximation, tolerable while our products are a suite plus its own modules.
    /// </para>
    /// </summary>
    private static CoverageIndex BuildCoverageIndex(
        FirmLevelWorld world,
        IReadOnlySet<Guid> segmentIds,
        IReadOnlySet<Guid> processIds,
        DateOnly startDate,
        int horizonMonths)
    {
        var byProduct = new Dictionary<Guid, List<CoverageCell>>();
        var stepsByProduct = new Dictionary<Guid, List<CoverageStep>>();
        var covered = new Dictionary<Guid, HashSet<(Guid Process, string? Segment)>>();
        var ourProductIds = world.Products.Where(p => p.IsOurs).Select(p => p.Id).ToHashSet();

        var applicable = world.Coverages
            .Where(c => !c.IsUnknown
                        && processIds.Contains(c.BusinessProcessId)
                        && (c.ClientSegmentId is null || segmentIds.Contains(c.ClientSegmentId.Value)))
            .ToList();

        // Month-0 cells. The key keeps the segment, so a segment-specific assessment stays scoped to
        // its segment; duplicate rows for one key still collapse to the best score, and any one of
        // them without capacity gates the cell.
        var baseScores = new Dictionary<(Guid Product, Guid Process, Guid? Approach, Guid? Segment), double>();
        foreach (var group in applicable
                     .GroupBy(c => (c.ProductId, c.BusinessProcessId, c.ApproachId, c.ClientSegmentId))
                     .OrderBy(g => g.Key.ProductId)
                     .ThenBy(g => g.Key.BusinessProcessId)
                     .ThenBy(g => g.Key.ApproachId)
                     .ThenBy(g => g.Key.ClientSegmentId))
        {
            double score = group.Max(c => c.NumericScore ?? 0.0);
            bool hasCapacity = group.All(HasCapacity);
            baseScores[group.Key] = score;

            if (!byProduct.TryGetValue(group.Key.ProductId, out var cells))
                byProduct[group.Key.ProductId] = cells = [];

            cells.Add(new CoverageCell(
                group.Key.BusinessProcessId.ToString(),
                group.Key.ApproachId?.ToString(),
                score,
                hasCapacity,
                group.Key.ClientSegmentId?.ToString()));

            if (score > 0)
                Cover(covered, group.Key.ProductId, group.Key.BusinessProcessId,
                    group.Key.ClientSegmentId?.ToString());
        }

        // Roadmap steps, one cell at a time so a cell's steps can be made monotone in month order.
        var deltas = world.PlannedCoverageDeltas
            .Where(d => processIds.Contains(d.BusinessProcessId)
                        && (d.ClientSegmentId is null || segmentIds.Contains(d.ClientSegmentId.Value)))
            .ToList();

        foreach (var productId in ourProductIds.OrderBy(id => id))
        {
            var byCell = deltas
                .GroupBy(d => (d.BusinessProcessId, d.ApproachId, d.ClientSegmentId))
                .OrderBy(g => g.Key.BusinessProcessId)
                .ThenBy(g => g.Key.ApproachId)
                .ThenBy(g => g.Key.ClientSegmentId);

            foreach (var group in byCell)
            {
                double baseScore = ResolveBaseScore(
                    baseScores, productId, group.Key.BusinessProcessId, group.Key.ApproachId,
                    group.Key.ClientSegmentId);

                // One entry per month: several features landing in the same month deliver the best
                // tier among them, and the running maximum keeps the sequence monotone.
                var byMonth = new SortedDictionary<int, double>();
                foreach (var delta in group)
                {
                    int raw = SimulationInputBuilder.MonthIndex(startDate, delta.LiveFrom);
                    if (raw >= horizonMonths) continue; // lands after the horizon: no effect on this run
                    int month = Math.Max(0, raw);       // already live when the run starts
                    double score = NumericTier(delta.ToTier);
                    if (!byMonth.TryGetValue(month, out double best) || score > best) byMonth[month] = score;
                }

                double reached = baseScore;
                foreach (var delta in byMonth.Select(kv => (Month: kv.Key, Score: kv.Value)))
                {
                    if (delta.Score <= reached) continue; // already shipped, or at/below what we have
                    reached = delta.Score;

                    if (!stepsByProduct.TryGetValue(productId, out var steps))
                        stepsByProduct[productId] = steps = [];

                    steps.Add(new CoverageStep(
                        group.Key.BusinessProcessId.ToString(),
                        group.Key.ApproachId?.ToString(),
                        group.Key.ClientSegmentId?.ToString(),
                        delta.Month,
                        delta.Score));

                    Cover(covered, productId, group.Key.BusinessProcessId,
                        group.Key.ClientSegmentId?.ToString());
                }
            }
        }

        foreach (var cells in byProduct.Values) cells.Sort(CompareCells);
        foreach (var steps in stepsByProduct.Values) steps.Sort(CompareSteps);

        return new CoverageIndex(byProduct, stepsByProduct, covered);
    }

    /// <summary>
    /// What a (process, approach?, segment?) cell resolves to before the roadmap touches it, using
    /// the engine's own precedence — exact-then-generic on both axes, approach first.
    /// </summary>
    private static double ResolveBaseScore(
        IReadOnlyDictionary<(Guid Product, Guid Process, Guid? Approach, Guid? Segment), double> baseScores,
        Guid productId,
        Guid processId,
        Guid? approachId,
        Guid? segmentId)
    {
        if (approachId is not null)
        {
            if (segmentId is not null
                && baseScores.TryGetValue((productId, processId, approachId, segmentId), out double exact))
                return exact;
            if (baseScores.TryGetValue((productId, processId, approachId, null), out double approachGeneric))
                return approachGeneric;
        }

        if (segmentId is not null
            && baseScores.TryGetValue((productId, processId, null, segmentId), out double segmentDefault))
            return segmentDefault;
        return baseScores.TryGetValue((productId, processId, null, null), out double generic) ? generic : 0.0;
    }

    private static void Cover(
        Dictionary<Guid, HashSet<(Guid Process, string? Segment)>> covered,
        Guid productId, Guid processId, string? segmentId)
    {
        if (!covered.TryGetValue(productId, out var processes)) covered[productId] = processes = [];
        processes.Add((processId, segmentId));
    }

    private static int CompareCells(CoverageCell a, CoverageCell b)
    {
        int byProcess = string.CompareOrdinal(a.ProcessId, b.ProcessId);
        if (byProcess != 0) return byProcess;
        int byApproach = string.CompareOrdinal(a.ApproachId, b.ApproachId);
        return byApproach != 0 ? byApproach : string.CompareOrdinal(a.SegmentId, b.SegmentId);
    }

    private static int CompareSteps(CoverageStep a, CoverageStep b)
    {
        int byProcess = string.CompareOrdinal(a.ProcessId, b.ProcessId);
        if (byProcess != 0) return byProcess;
        int byApproach = string.CompareOrdinal(a.ApproachId, b.ApproachId);
        if (byApproach != 0) return byApproach;
        int bySegment = string.CompareOrdinal(a.SegmentId, b.SegmentId);
        return bySegment != 0 ? bySegment : a.Month.CompareTo(b.Month);
    }

    private static double NumericTier(CoverageTier tier) => tier switch
    {
        CoverageTier.Gold => 1.0,
        CoverageTier.Silver => 0.7,
        CoverageTier.Bronze => 0.4,
        _ => 0.0
    };

    private static bool HasCapacity(DomainCoverage coverage) =>
        !TryReadBool(coverage.ScaleLimits, HasCapacityProperty, out bool value) || value;

    // ---------- Firms ----------

    private static List<FirmInput> BuildFirms(
        FirmLevelWorld world,
        IReadOnlySet<Guid> segmentIds,
        IReadOnlySet<Guid> processIds,
        IReadOnlySet<Guid> productIds,
        IReadOnlyDictionary<Guid, List<CoverageCell>> coverageByProduct)
    {
        var relationshipsByFirm = world.Relationships
            .Where(r => r.IsActive && productIds.Contains(r.ProductId))
            .GroupBy(r => r.FirmId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.ProductId).ToList());

        var firmNeeds = world.Needs
            .Where(n => n.FirmId is not null && processIds.Contains(n.BusinessProcessId))
            .GroupBy(n => n.FirmId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.BusinessProcessId).ToList());

        var firms = new List<FirmInput>();
        foreach (var firm in world.Firms.Where(f => f.ClientSegmentId is not null
                                                    && segmentIds.Contains(f.ClientSegmentId.Value))
                     .OrderBy(f => f.Id))
        {
            var relationships = new List<IncumbentRelationship>();
            foreach (var relationship in relationshipsByFirm.GetValueOrDefault(firm.Id, []))
            {
                var served = relationship.ServedProcessIds.Where(processIds.Contains).ToList();
                if (served.Count == 0)
                {
                    // Nobody has decomposed this relationship into modules: it holds whatever the
                    // product actually covers *in this firm's segment*, which beats pretending it
                    // holds nothing — and beats crediting it with another segment's coverage.
                    string segmentId = firm.ClientSegmentId!.Value.ToString();
                    served = coverageByProduct.GetValueOrDefault(relationship.ProductId, [])
                        .Where(c => c.Score > 0 && (c.SegmentId is null || c.SegmentId == segmentId))
                        .Select(c => c.ProcessId)
                        .Distinct()
                        .Select(Guid.Parse)
                        .ToList();
                }

                relationships.Add(new IncumbentRelationship(
                    relationship.ProductId.ToString(),
                    served.OrderBy(p => p).Select(p => p.ToString()).ToList(),
                    relationship.PriceLevel));
            }

            var observedWeights = firmNeeds.GetValueOrDefault(firm.Id, [])
                .Select(n => new FirmImportance(n.BusinessProcessId.ToString(), n.ImportanceWeight))
                .ToList();

            firms.Add(new FirmInput(
                firm.Id.ToString(),
                firm.ClientSegmentId!.Value.ToString(),
                firm.EpistemicStatus == EpistemicStatus.Synthetic,
                relationships,
                observedWeights.Count > 0 ? observedWeights : null));
        }

        return firms;
    }

    // ---------- Sales cycles ----------

    private static List<SalesCycleSpec> BuildSalesCycles(
        ParameterLookup knobs, IReadOnlySet<Guid> segmentIds, IReadOnlySet<Guid> productIds)
    {
        var specs = new List<SalesCycleSpec>();
        foreach (var (key, parameter) in knobs.Ordered)
        {
            if (!ChoiceParameterKeys.TryParseSalesCycleKey(
                    key, ChoiceParameterKeys.SalesCycleMuPrefix, out var segmentId, out var productId))
                continue;
            if (!segmentIds.Contains(segmentId)) continue;
            if (productId is not null && !productIds.Contains(productId.Value)) continue;

            double sigma = knobs.Scalar(ChoiceParameterKeys.SalesCycleSigma(segmentId, productId), 0.0);
            specs.Add(new SalesCycleSpec(
                segmentId.ToString(),
                productId?.ToString(),
                ChoiceParameterKeys.Mean(parameter.Distribution),
                Math.Max(0.0, sigma)));
        }

        return specs;
    }

    // ---------- JSON side-channels (industry specifics live in data, not schema) ----------

    private static double ReadRequiredCoverage(Need need)
    {
        if (TryReadDouble(need.RequiredDepthJson, RequiredCoverageProperty, out double value))
            return Math.Clamp(value, 0.0, 1.0);
        return need.IsHard ? HardNeedDefaultRequiredCoverage : 0.0;
    }

    private static bool TryReadDouble(string? json, string property, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty(property, out var element)) return false;
            if (element.ValueKind != JsonValueKind.Number) return false;
            value = element.GetDouble();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadBool(string? json, string property, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty(property, out var element)) return false;
            if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            value = element.GetBoolean();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Free-form assumption-set parameters by key, in a stable order.</summary>
    private sealed class ParameterLookup
    {
        private readonly Dictionary<string, Parameter> _byKey;

        public ParameterLookup(IEnumerable<Parameter> parameters)
        {
            _byKey = parameters
                .Where(p => !p.IsEngineParameter)
                .GroupBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }

        public IEnumerable<KeyValuePair<string, Parameter>> Ordered =>
            _byKey.OrderBy(p => p.Key, StringComparer.Ordinal);

        public double Scalar(string key, double fallback) =>
            _byKey.TryGetValue(key, out var parameter) ? ChoiceParameterKeys.Mean(parameter.Distribution) : fallback;

        public DistributionSpec? Distribution(string key) =>
            _byKey.TryGetValue(key, out var parameter)
                ? SimulationInputBuilder.ToEngineDistribution(parameter.Distribution)
                : null;
    }
}
