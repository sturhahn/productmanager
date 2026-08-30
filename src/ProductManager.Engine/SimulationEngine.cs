using System.Diagnostics;

namespace ProductManager.Engine;

/// <summary>
/// Engine v0: aggregate stock-and-flow Monte Carlo per segment.
///
/// Per path: every parameter is sampled ONCE from its distribution (parameter uncertainty),
/// then each monthly tick applies process noise through counting draws:
///   1. newFirms   ~ Poisson(newFirmArrivalRate * eventMult * covMult)          -> prospect pool
///   2. opps       ~ Poisson(pool * conversionToMarketRate * eventMult * covMult), capped at floor(pool)
///   3. wins       ~ Binomial(opps, clamp01(winRate * attractiveness * eventMult * covMult))
///   4. churn      ~ Binomial(clientsAtMonthStart, clamp01(churnHazard * eventMult * covMult))
///   5. per module: adds ~ Binomial(clients - moduleClients, clamp01(crossSellRate * eventMult * covMult))
/// where covMult = (x_t / x_0)^beta — a constant-elasticity transfer function, so beta reads as
/// "+1% covariate =&gt; +beta% rate" and the answer is invariant to the covariate's units. The
/// combined multiplier (covariate x events x attractiveness) is capped at
/// <see cref="MaxFlowMultiplier"/>; exceeding it throws rather than returning nonsense.
/// Losers stay in the pool; churned clients leave the market (v0). Module holder counts are
/// scaled down proportionally on churn so moduleClients &lt;= clients always holds.
///
/// Money per tick:
///   bookings    = wins * (arpu*12 + implementationFee) + sum_m(moduleAdds_m * moduleArpu_m * 12)
///   recognized ARR = 12 * (clients*arpu + sum_m moduleClients_m * moduleArpu_m)   (end of month)
///   margin      = (clients*arpu + sum_m moduleClients_m*moduleArpu_m + wins*implFee)
///               - (clients*(servicingPerClient + infraPerClient) + infraFixed + rdDragPerMonth)
/// NPV per path = sum_t totalMargin[t]/(1+rm)^(t+1) + Gordon terminal value on the final
/// month's annualized margin (only when discountRateAnnual &gt; terminalGrowthAnnual).
///
/// Deterministic for a given seed: per-path RNG streams derive stably from (seed, pathIndex).
/// Managed arrays only; no LINQ in hot loops; sampler layer is the seam where a BLAS-backed
/// provider could be swapped in later.
/// </summary>
public static class SimulationEngine
{
    private const int MetricCount = EngineInternals.MetricCount;

    /// <summary>
    /// Ceiling on the combined per-tick rate multiplier (covariate x events x attractiveness).
    /// Deliberately generous — a 1000x shock is far beyond anything a plausible scenario asserts —
    /// so it never trips on real inputs but does trip before mis-specified ones can return
    /// astronomical trajectories that read as a valid answer.
    /// </summary>
    public const double MaxFlowMultiplier = 1000.0;

    public static EngineResult Run(EngineInput input)
    {
        Validate(input);
        var sw = Stopwatch.StartNew();

        int segCount = input.Segments.Count;
        int months = input.HorizonMonths;
        int paths = input.Paths;

        // ---- Precompute static lookup tables (shared across paths) ----

        var segIndex = new Dictionary<string, int>(segCount, StringComparer.Ordinal);
        for (int s = 0; s < segCount; s++) segIndex[input.Segments[s].Id] = s;

        // Event multipliers per (segment, flowKind, month), default 1.
        var eventMult = EngineInternals.BuildEventMultipliers(input.Events, segIndex, segCount, months);

        // Attractiveness (WinRate multiplier) per (segment, month), default 1.
        var attractiveness = new double[segCount][];
        for (int s = 0; s < segCount; s++)
        {
            attractiveness[s] = new double[months];
            Array.Fill(attractiveness[s], 1.0);
        }
        foreach (var a in input.AttractivenessSchedule)
        {
            if (!segIndex.TryGetValue(a.SegmentId, out int s)) continue;
            if (a.Month < 0 || a.Month >= months) continue;
            attractiveness[s][a.Month] = a.Multiplier;
        }

        // Covariate index and resolved deterministic paths.
        int covCount = input.Covariates.Count;
        var covIndex = new Dictionary<string, int>(covCount, StringComparer.Ordinal);
        for (int c = 0; c < covCount; c++) covIndex[input.Covariates[c].Id] = c;

        // Resolved parameter specs per segment: [arrival, conversion, win, churn] + per-module cross-sell.
        var resolved = new ResolvedSegment[segCount];
        for (int s = 0; s < segCount; s++)
            resolved[s] = ResolveSegment(input, input.Segments[s], covIndex);

        // ---- Per-path simulation ----

        // Metric storage: [segment or total][metric][path * months + month]
        int slots = segCount + 1; // last slot = total
        var metrics = new double[slots][][];
        for (int s = 0; s < slots; s++)
        {
            metrics[s] = new double[MetricCount][];
            for (int m = 0; m < MetricCount; m++) metrics[s][m] = new double[paths * months];
        }

        var npvPerPath = new double[paths];

        // Reusable per-path buffers.
        var covValues = new double[covCount * months];
        var totalMargin = new double[months];

        for (int p = 0; p < paths; p++)
        {
            var rng = new EngineRandom(input.Seed, p);

            // 1) Sample all parameters once per path, in canonical order (segments in input
            //    order; arrival, conversion, win, churn, then modules in input order).
            for (int s = 0; s < segCount; s++) resolved[s].SampleRates(rng);

            // 2) Resolve covariate values for this path, in input order.
            EngineInternals.ResolveCovariatePaths(input.Covariates, months, rng, covValues);

            // 3) Monthly ticks per segment.
            Array.Clear(totalMargin, 0, months);
            for (int s = 0; s < segCount; s++)
            {
                var seg = input.Segments[s];
                var rs = resolved[s];
                double[] evRow = eventMult[s];
                double[] attrRow = attractiveness[s];

                double pool = seg.InitialProspectPool;
                long clients = seg.InitialClientCount;
                int moduleCount = rs.ModuleIds.Length;
                for (int m = 0; m < moduleCount; m++)
                {
                    long held = (long)Math.Round(seg.InitialClientCount * seg.PerModulePenetration[m].Penetration);
                    rs.ModuleClients[m] = Math.Clamp(held, 0, clients);
                }

                double servicing = seg.CostRates.ServicingPerClient + seg.CostRates.InfraPerClient;
                double fixedCost = seg.CostRates.InfraFixed + seg.CostRates.RdDragPerMonth;

                for (int t = 0; t < months; t++)
                {
                    // -- flows --
                    // Each rate = sampled base * (covariate elasticity x event x attractiveness),
                    // with the combined multiplier guarded before it reaches the sampler.
                    double arrivalMult = CheckedMultiplier(
                        CovariateMultiplier(rs.ArrivalCov, covValues, months, t)
                        * evRow[(int)FlowKind.NewFirmArrival * months + t],
                        seg.Id, FlowKind.NewFirmArrival, t, null);
                    long newFirms = rng.NextPoisson(Math.Max(0.0, rs.ArrivalRate * arrivalMult));
                    pool += newFirms;

                    double convMult = CheckedMultiplier(
                        CovariateMultiplier(rs.ConversionCov, covValues, months, t)
                        * evRow[(int)FlowKind.ConversionToMarket * months + t],
                        seg.Id, FlowKind.ConversionToMarket, t, null);
                    double convRate = rs.ConversionRate * convMult;
                    long opportunities = rng.NextPoisson(Math.Max(0.0, pool * convRate));
                    long poolFloor = (long)Math.Floor(pool);
                    if (opportunities > poolFloor) opportunities = poolFloor;

                    double winMult = CheckedMultiplier(
                        CovariateMultiplier(rs.WinCov, covValues, months, t)
                        * attrRow[t]
                        * evRow[(int)FlowKind.WinRate * months + t],
                        seg.Id, FlowKind.WinRate, t, null);
                    double winP = rs.WinRate * winMult;
                    long wins = rng.NextBinomial(opportunities, Clamp01(winP));

                    double churnMult = CheckedMultiplier(
                        CovariateMultiplier(rs.ChurnCov, covValues, months, t)
                        * evRow[(int)FlowKind.ChurnHazard * months + t],
                        seg.Id, FlowKind.ChurnHazard, t, null);
                    double churnP = rs.ChurnRate * churnMult;
                    long churned = rng.NextBinomial(clients, Clamp01(churnP));

                    long clientsBefore = clients;
                    long clientsAfterChurn = clients - churned;

                    // Scale module holders proportionally with churn; never exceed clients.
                    if (churned > 0 && clientsBefore > 0)
                    {
                        double keep = (double)clientsAfterChurn / clientsBefore;
                        for (int m = 0; m < moduleCount; m++)
                        {
                            long scaled = (long)Math.Round(rs.ModuleClients[m] * keep);
                            rs.ModuleClients[m] = Math.Clamp(scaled, 0, clientsAfterChurn);
                        }
                    }

                    pool -= wins;
                    if (pool < 0.0) pool = 0.0;
                    clients = clientsAfterChurn + wins;

                    double evCross = evRow[(int)FlowKind.CrossSellRate * months + t];
                    double moduleBookings = 0.0;
                    for (int m = 0; m < moduleCount; m++)
                    {
                        double crossMult = CheckedMultiplier(
                            CovariateMultiplier(rs.CrossSellCovs[m], covValues, months, t) * evCross,
                            seg.Id, FlowKind.CrossSellRate, t, rs.ModuleIds[m]);
                        double crossP = rs.CrossSellRates[m] * crossMult;
                        long eligible = clients - rs.ModuleClients[m];
                        long adds = rng.NextBinomial(eligible, Clamp01(crossP));
                        rs.ModuleClients[m] += adds;
                        moduleBookings += adds * rs.ModuleArpus[m] * 12.0;
                    }

                    // -- money --
                    double moduleMrr = 0.0;
                    for (int m = 0; m < moduleCount; m++) moduleMrr += rs.ModuleClients[m] * rs.ModuleArpus[m];

                    double bookings = wins * (seg.ArpuPerMonth * 12.0 + seg.ImplementationFee) + moduleBookings;
                    double mrr = clients * seg.ArpuPerMonth + moduleMrr;
                    double arr = mrr * 12.0;
                    double revenue = mrr + wins * seg.ImplementationFee;
                    double costs = clients * servicing + fixedCost;
                    double margin = revenue - costs;

                    int idx = p * months + t;
                    metrics[s][0][idx] = clients;
                    metrics[s][1][idx] = pool;
                    metrics[s][2][idx] = wins;
                    metrics[s][3][idx] = churned;
                    metrics[s][4][idx] = bookings;
                    metrics[s][5][idx] = arr;
                    metrics[s][6][idx] = margin;

                    totalMargin[t] += margin;
                }
            }

            // 4) Totals across segments, and per-path NPV.
            int totalSlot = segCount;
            for (int t = 0; t < months; t++)
            {
                int idx = p * months + t;
                for (int m = 0; m < MetricCount; m++)
                {
                    double sum = 0.0;
                    for (int s = 0; s < segCount; s++) sum += metrics[s][m][idx];
                    metrics[totalSlot][m][idx] = sum;
                }
            }

            npvPerPath[p] = EngineInternals.NpvForPath(
                totalMargin, months, input.DiscountRateAnnual, input.TerminalGrowthAnnual);
        }

        // ---- Aggregate across paths ----

        var segmentResults = new SegmentTrajectories[segCount];
        for (int s = 0; s < segCount; s++)
            segmentResults[s] = Summarize(input.Segments[s].Id, metrics[s], paths, months);
        var total = Summarize("total", metrics[segCount], paths, months);

        var npvSummary = EngineInternals.SummarizeScalar(npvPerPath);

        sw.Stop();
        return new EngineResult(
            segmentResults,
            total,
            npvSummary,
            npvPerPath,
            paths,
            input.Seed,
            months,
            sw.ElapsedMilliseconds);
    }

    private static void Validate(EngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Segments is null || input.Segments.Count == 0)
            throw new ArgumentException("At least one segment is required.", nameof(input));
        if (input.HorizonMonths < 1)
            throw new ArgumentException("HorizonMonths must be >= 1.", nameof(input));
        if (input.Paths < 1)
            throw new ArgumentException("Paths must be >= 1.", nameof(input));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seg in input.Segments)
        {
            if (string.IsNullOrEmpty(seg.Id))
                throw new ArgumentException("Segment id must be non-empty.", nameof(input));
            if (!seen.Add(seg.Id))
                throw new ArgumentException($"Duplicate segment id '{seg.Id}'.", nameof(input));
        }

        // Covariate spec shape and strict positivity for elasticity-linked series (shared with v1).
        EngineInternals.ValidateCovariates(
            input.Covariates ?? [], input.Parameters ?? [], input.HorizonMonths, nameof(input));

        foreach (var param in input.Parameters ?? [])
        {
            if (param.ModuleId is not null && param.FlowKind != FlowKind.CrossSellRate)
                throw new ArgumentException(
                    $"Parameter ({param.SegmentId}, {param.FlowKind}) has a moduleId but only CrossSellRate parameters may.",
                    nameof(input));
            if (param.Covariate is not null && input.Covariates?.Any(c => c.Id == param.Covariate.CovariateId) != true)
                throw new ArgumentException(
                    $"Parameter ({param.SegmentId}, {param.FlowKind}) links unknown covariate '{param.Covariate.CovariateId}'.",
                    nameof(input));
        }
    }

    private static double Clamp01(double v) => EngineInternals.Clamp01(v);

    private static double CovariateMultiplier(CovLink cov, double[] covValues, int months, int t)
        => EngineInternals.CovariateMultiplier(cov, covValues, months, t);

    private static double CheckedMultiplier(
        double multiplier, string segmentId, FlowKind flow, int month, string? moduleId)
        => EngineInternals.CheckedMultiplier(multiplier, segmentId, flow, month, moduleId);

    /// <summary>Per-segment resolved parameter specs plus per-path sampled values and mutable stocks.</summary>
    private sealed class ResolvedSegment
    {
        internal DistributionSpec ArrivalSpec = DistributionSpec.Point(0);
        internal DistributionSpec ConversionSpec = DistributionSpec.Point(0);
        internal DistributionSpec WinSpec = DistributionSpec.Point(0);
        internal DistributionSpec ChurnSpec = DistributionSpec.Point(0);
        internal CovLink ArrivalCov = CovLink.None;
        internal CovLink ConversionCov = CovLink.None;
        internal CovLink WinCov = CovLink.None;
        internal CovLink ChurnCov = CovLink.None;

        internal string[] ModuleIds = [];
        internal double[] ModuleArpus = [];
        internal DistributionSpec[] CrossSellSpecs = [];
        internal CovLink[] CrossSellCovs = [];

        // Sampled once per path:
        internal double ArrivalRate, ConversionRate, WinRate, ChurnRate;
        internal double[] CrossSellRates = [];

        // Mutable per-path stocks:
        internal long[] ModuleClients = [];

        internal void SampleRates(EngineRandom rng)
        {
            ArrivalRate = Math.Max(0.0, rng.Sample(ArrivalSpec));
            ConversionRate = Math.Max(0.0, rng.Sample(ConversionSpec));
            WinRate = Math.Max(0.0, rng.Sample(WinSpec));
            ChurnRate = Math.Max(0.0, rng.Sample(ChurnSpec));
            for (int m = 0; m < CrossSellSpecs.Length; m++)
                CrossSellRates[m] = Math.Max(0.0, rng.Sample(CrossSellSpecs[m]));
        }
    }

    private static ResolvedSegment ResolveSegment(
        EngineInput input, SegmentInput seg, Dictionary<string, int> covIndex)
    {
        var rs = new ResolvedSegment();
        int moduleCount = seg.PerModulePenetration?.Count ?? 0;
        rs.ModuleIds = new string[moduleCount];
        rs.ModuleArpus = new double[moduleCount];
        rs.CrossSellSpecs = new DistributionSpec[moduleCount];
        rs.CrossSellCovs = new CovLink[moduleCount];
        rs.CrossSellRates = new double[moduleCount];
        rs.ModuleClients = new long[moduleCount];
        for (int m = 0; m < moduleCount; m++)
        {
            rs.ModuleIds[m] = seg.PerModulePenetration![m].ModuleId;
            rs.ModuleArpus[m] = seg.PerModulePenetration[m].ArpuPerMonth;
            rs.CrossSellSpecs[m] = DistributionSpec.Point(0);
            rs.CrossSellCovs[m] = CovLink.None;
        }

        foreach (var param in input.Parameters ?? [])
        {
            if (!string.Equals(param.SegmentId, seg.Id, StringComparison.Ordinal)) continue;
            CovLink link = param.Covariate is null
                ? CovLink.None
                : new CovLink(covIndex[param.Covariate.CovariateId], param.Covariate.Beta);

            switch (param.FlowKind)
            {
                case FlowKind.NewFirmArrival:
                    rs.ArrivalSpec = param.Distribution;
                    rs.ArrivalCov = link;
                    break;
                case FlowKind.ConversionToMarket:
                    rs.ConversionSpec = param.Distribution;
                    rs.ConversionCov = link;
                    break;
                case FlowKind.WinRate:
                    rs.WinSpec = param.Distribution;
                    rs.WinCov = link;
                    break;
                case FlowKind.ChurnHazard:
                    rs.ChurnSpec = param.Distribution;
                    rs.ChurnCov = link;
                    break;
                case FlowKind.CrossSellRate:
                    for (int m = 0; m < moduleCount; m++)
                    {
                        if (!string.Equals(rs.ModuleIds[m], param.ModuleId, StringComparison.Ordinal)) continue;
                        rs.CrossSellSpecs[m] = param.Distribution;
                        rs.CrossSellCovs[m] = link;
                    }
                    break;
            }
        }

        return rs;
    }

    private static SegmentTrajectories Summarize(string id, double[][] segMetrics, int paths, int months)
        => EngineInternals.Summarize(id, segMetrics, paths, months);
}
