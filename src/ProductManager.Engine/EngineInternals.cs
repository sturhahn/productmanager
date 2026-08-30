namespace ProductManager.Engine;

/// <summary>
/// A resolved covariate link: index into the covariate table plus elasticity. Shared by the
/// aggregate (v0) and firm-level (v1) engines.
/// </summary>
internal readonly record struct CovLink(int CovIdx, double Beta)
{
    internal static readonly CovLink None = new(-1, 0.0);
}

/// <summary>
/// Numeric and structural helpers shared by <see cref="SimulationEngine"/> (aggregate v0) and
/// <see cref="FirmLevelEngine"/> (firm-level v1): distribution summaries, covariate elasticity,
/// event-multiplier tables, per-path covariate resolution, the runaway-multiplier guard, and
/// NPV/terminal-value math. Both engines must produce the same trajectory output shape, so the
/// summarisation lives here exactly once.
/// </summary>
internal static class EngineInternals
{
    internal const int FlowKindCount = 5;
    internal const int MetricCount = 7; // clients, pool, wins, churned, bookings, arr, margin

    internal static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

    /// <summary>Linear-interpolation quantile of a sorted array.</summary>
    internal static double QuantileSorted(double[] sorted, double q)
    {
        int n = sorted.Length;
        if (n == 1) return sorted[0];
        double pos = q * (n - 1);
        int lo = (int)Math.Floor(pos);
        int hi = lo + 1;
        if (hi >= n) return sorted[n - 1];
        double frac = pos - lo;
        return sorted[lo] + frac * (sorted[hi] - sorted[lo]);
    }

    /// <summary>
    /// Covariate multiplier for month t: (x_t / x_0)^beta — a constant-elasticity transfer function.
    /// Beta is an elasticity, so a +1% move in the covariate moves the rate by about +beta%, and the
    /// result depends only on the covariate's *ratio* to its month-0 level. Positivity of the path is
    /// guaranteed at validation time for every nonzero-beta link.
    /// </summary>
    internal static double CovariateMultiplier(CovLink cov, double[] covValues, int months, int t)
    {
        if (cov.CovIdx < 0 || cov.Beta == 0.0) return 1.0;
        double x0 = covValues[cov.CovIdx * months];
        double xt = covValues[cov.CovIdx * months + t];
        return Math.Pow(xt / x0, cov.Beta);
    }

    /// <summary>
    /// Guards the combined per-tick rate multiplier (covariate x events x attractiveness). A runaway
    /// multiplier means the inputs are mis-specified and silently returning astronomical trajectories
    /// is far worse than failing here. Throws with the offending segment/flow/month.
    /// </summary>
    internal static double CheckedMultiplier(
        double multiplier, string segmentId, FlowKind flow, int month, string? moduleId)
    {
        if (multiplier <= SimulationEngine.MaxFlowMultiplier && !double.IsNaN(multiplier)) return multiplier;

        string module = moduleId is null ? "" : $", module '{moduleId}'";
        throw new ArgumentException(
            $"Flow-rate multiplier for segment '{segmentId}', flow {flow}{module}, month {month} is " +
            $"{multiplier:G6}x, above the {SimulationEngine.MaxFlowMultiplier:G0}x limit. That multiplier is " +
            "covariate x events x attractiveness combined: check the parameter's covariate beta " +
            "(an elasticity — +1% covariate gives +beta% rate), the event rate multipliers active " +
            "in that month, and the attractiveness schedule.",
            "input");
    }

    /// <summary>
    /// Event multipliers per (segment, flowKind, month) as [segment][flow * months + t], default 1.
    /// </summary>
    internal static double[][] BuildEventMultipliers(
        IReadOnlyList<EventInput> events, Dictionary<string, int> segIndex, int segCount, int months)
    {
        var eventMult = new double[segCount][];
        for (int s = 0; s < segCount; s++)
        {
            eventMult[s] = new double[FlowKindCount * months];
            Array.Fill(eventMult[s], 1.0);
        }
        foreach (var ev in events)
        {
            int from = Math.Max(0, ev.StartMonth);
            int to = Math.Min(months - 1, ev.EndMonth);
            foreach (var rm in ev.RateMultipliers)
            {
                int f = (int)rm.FlowKind;
                for (int s = 0; s < segCount; s++)
                {
                    if (rm.SegmentId is not null && !segIndex.TryGetValue(rm.SegmentId, out int only))
                        continue;
                    if (rm.SegmentId is not null && segIndex[rm.SegmentId] != s) continue;
                    double[] row = eventMult[s];
                    for (int t = from; t <= to; t++) row[f * months + t] *= rm.Multiplier;
                }
            }
        }
        return eventMult;
    }

    /// <summary>
    /// Resolves covariate values for one MC path into covValues[c * months + t], in input order:
    /// pre-resolved monthly paths are copied, GBM specs are sampled from the path's RNG stream.
    /// </summary>
    internal static void ResolveCovariatePaths(
        IReadOnlyList<CovariateInput> covariates, int months, EngineRandom rng, double[] covValues)
    {
        for (int c = 0; c < covariates.Count; c++)
        {
            var cov = covariates[c];
            if (cov.MonthlyPath is not null)
            {
                for (int t = 0; t < months; t++) covValues[c * months + t] = cov.MonthlyPath[t];
            }
            else
            {
                var gbm = cov.Gbm!;
                double x = gbm.Start;
                double driftTerm = gbm.Drift - 0.5 * gbm.Vol * gbm.Vol;
                for (int t = 0; t < months; t++)
                {
                    if (t > 0) x *= Math.Exp(driftTerm + gbm.Vol * rng.NextGaussian());
                    covValues[c * months + t] = x;
                }
            }
        }
    }

    /// <summary>
    /// Validates covariate specs and their links: exactly one of monthlyPath/gbm, path length,
    /// and strict positivity for every series linked with a nonzero elasticity.
    /// </summary>
    internal static void ValidateCovariates(
        IReadOnlyList<CovariateInput> covariates,
        IReadOnlyList<ParameterSpec> parameters,
        int horizonMonths,
        string paramName)
    {
        foreach (var cov in covariates)
        {
            if (cov.MonthlyPath is null == (cov.Gbm is null))
                throw new ArgumentException(
                    $"Covariate '{cov.Id}' must specify exactly one of monthlyPath or gbm.", paramName);
            if (cov.MonthlyPath is not null && cov.MonthlyPath.Length < horizonMonths)
                throw new ArgumentException(
                    $"Covariate '{cov.Id}' monthlyPath is shorter than horizonMonths.", paramName);
        }

        var elasticityLinked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var param in parameters)
            if (param.Covariate is not null && param.Covariate.Beta != 0.0)
                elasticityLinked.Add(param.Covariate.CovariateId);

        foreach (var cov in covariates)
        {
            if (!elasticityLinked.Contains(cov.Id)) continue;

            if (cov.MonthlyPath is not null)
            {
                for (int t = 0; t < horizonMonths; t++)
                {
                    double x = cov.MonthlyPath[t];
                    if (x > 0.0 && !double.IsNaN(x)) continue;
                    throw new ArgumentException(
                        $"Covariate '{cov.Id}' is linked with a nonzero beta but its monthlyPath is " +
                        $"non-positive at month {t} (value {x:G6}). Beta is an elasticity — the engine " +
                        "applies (x_t / x_0)^beta — so the whole path must be strictly positive. " +
                        "Rebase or shift the series onto a positive scale, or drop the link.",
                        paramName);
                }
            }
            else if (!(cov.Gbm!.Start > 0.0))
            {
                throw new ArgumentException(
                    $"Covariate '{cov.Id}' is linked with a nonzero beta but its gbm start is " +
                    $"{cov.Gbm.Start:G6}. Beta is an elasticity — the engine applies (x_t / x_0)^beta — " +
                    "so the series must start strictly positive.",
                    paramName);
            }
        }
    }

    /// <summary>
    /// NPV of one path's monthly total margin: discounted monthly cash flows plus a Gordon terminal
    /// value on the final month's annualized margin (only when discount &gt; terminal growth).
    /// </summary>
    internal static double NpvForPath(
        double[] totalMargin, int months, double discountRateAnnual, double terminalGrowthAnnual)
    {
        double monthlyRate = Math.Pow(1.0 + discountRateAnnual, 1.0 / 12.0) - 1.0;
        double npv = 0.0;
        double discount = 1.0;
        for (int t = 0; t < months; t++)
        {
            discount /= 1.0 + monthlyRate;
            npv += totalMargin[t] * discount;
        }
        if (discountRateAnnual > terminalGrowthAnnual && months > 0)
        {
            double finalAnnualMargin = totalMargin[months - 1] * 12.0;
            double terminal = finalAnnualMargin * (1.0 + terminalGrowthAnnual)
                              / (discountRateAnnual - terminalGrowthAnnual);
            npv += terminal * discount;
        }
        return npv;
    }

    /// <summary>Mean/P10/P50/P90 across paths of a per-path scalar (e.g. NPV).</summary>
    internal static DistributionSummary SummarizeScalar(double[] perPath)
    {
        int paths = perPath.Length;
        var sorted = new double[paths];
        Array.Copy(perPath, sorted, paths);
        Array.Sort(sorted);
        double mean = 0.0;
        for (int p = 0; p < paths; p++) mean += perPath[p];
        mean /= paths;
        return new DistributionSummary(
            mean,
            QuantileSorted(sorted, 0.10),
            QuantileSorted(sorted, 0.50),
            QuantileSorted(sorted, 0.90));
    }

    /// <summary>
    /// Summarizes one slot's 7 metric matrices ([metric][path * months + month]) into the shared
    /// <see cref="SegmentTrajectories"/> output shape.
    /// </summary>
    internal static SegmentTrajectories Summarize(string id, double[][] segMetrics, int paths, int months)
    {
        var trajectories = new MetricTrajectory[MetricCount];
        var column = new double[paths];
        for (int m = 0; m < MetricCount; m++)
        {
            var mean = new double[months];
            var p10 = new double[months];
            var p50 = new double[months];
            var p90 = new double[months];
            double[] data = segMetrics[m];
            for (int t = 0; t < months; t++)
            {
                double sum = 0.0;
                for (int p = 0; p < paths; p++)
                {
                    double v = data[p * months + t];
                    column[p] = v;
                    sum += v;
                }
                Array.Sort(column);
                mean[t] = sum / paths;
                p10[t] = QuantileSorted(column, 0.10);
                p50[t] = QuantileSorted(column, 0.50);
                p90[t] = QuantileSorted(column, 0.90);
            }
            trajectories[m] = new MetricTrajectory(mean, p10, p50, p90);
        }

        return new SegmentTrajectories(
            id,
            Clients: trajectories[0],
            ProspectPool: trajectories[1],
            NewWins: trajectories[2],
            Churned: trajectories[3],
            Bookings: trajectories[4],
            RecognizedArr: trajectories[5],
            ContributionMargin: trajectories[6]);
    }
}
