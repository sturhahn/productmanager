namespace ProductManager.Engine.Calibration;

/// <summary>
/// Fits assumption-set parameters from historical facts with simple conjugate / moment updates
/// (deliberately no MCMC). Pure and fully deterministic — no RNG, no I/O.
///
/// <para><b>Updates per flow kind:</b></para>
/// <list type="bullet">
/// <item><b>WinRate</b> (per segment): Beta prior + Binomial(wins, decided) -> Beta(a + wins,
/// b + losses). Decided = Won + Lost; NoDecision is a real outcome but not a vendor choice, so
/// it is excluded from the denominator (win rate = P(win | a decision was made)).</item>
/// <item><b>ChurnHazard</b> (per segment): Beta prior + client-months as Bernoulli trials ->
/// Beta(a + churns, b + clientMonths - churns). Exposure comes from relationship spans clipped
/// at AsOfDate; without spans it is approximated as churnCount * windowMonths / 2 (each churned
/// relationship assumed live for half the observation window; surviving clients uncounted, so
/// the hazard is overstated when survivors exist — supply spans for a correct denominator).
/// <b>Zero churn events = no fit</b> (see below).</item>
/// <item><b>CrossSellRate</b> (per segment x module): Beta prior + won cross-sell opportunities
/// for the module vs client-months -> Beta(a + wins, b + clientMonths - wins). Requires span
/// exposure (no fallback: unlike churn, cross-sell wins do not witness the population).
/// <b>Zero wins = no fit</b> (see below).</item>
/// <item><b>NewFirmArrival / ConversionToMarket</b>: Gamma prior + Poisson monthly counts ->
/// Gamma(a + sum(count), b + sum(exposure)); exposure is 1/month for NewFirmArrival and the
/// prospect pool for ConversionToMarket. The Gamma posterior is reported moment-matched as a
/// LogNormal (DistributionSpec has no Gamma family; LogNormal keeps positive support).</item>
/// <item><b>Sales cycle</b> (per segment [x product]): lognormal log-moment blend. With n
/// observed log-durations of mean m and variance s2, and prior (Mu0, Sigma0, weight w):
/// Mu' = (w*Mu0 + n*m)/(w+n), Sigma'^2 = (w*Sigma0^2 + n*s2)/(w+n).</item>
/// </list>
///
/// <para><b>Prior conversion.</b> Probability priors become effective Betas: a Beta prior is
/// used directly; Normal/LogNormal priors are moment-matched (k = m(1-m)/s^2 - 1, a = m*k,
/// b = (1-m)*k, k floored at <see cref="MinBetaStrength"/> for very diffuse priors); a Point
/// prior — asserted certainty — gets an equivalent sample size of
/// <see cref="PointPriorStrength"/> so sufficient evidence can still move it. Rate priors
/// become effective Gammas the same way (a = m^2/s^2, b = m/s^2; Point -> pseudo-exposure of
/// <see cref="PointPriorStrength"/> months). The conjugate form is what makes tiny samples
/// barely move the prior while large samples converge to the empirical rate.</para>
///
/// <para><b>No fit on zero evidence.</b> The exposure-denominated fits (ChurnHazard,
/// CrossSellRate) divide an event count by client-months, and the two are cut differently: the
/// count is what was <em>recorded</em> by the as-of date, the exposure is what had merely
/// <em>happened</em> by then. With no events at all that asymmetry is indistinguishable from a
/// genuinely low rate, and a Beta(a + 0, b + clientMonths) update would drive the parameter to
/// nearly zero while reporting a sample size of 0 and a Calibrated provenance. So a parameter
/// with no observed events passes through unfitted, with the reason naming the as-of date. The
/// count-based fits are exempt on purpose: a WinRate needs decided deals (already required), and
/// an arrival count of zero in an observed month is honest Poisson evidence, because there the
/// exposure <em>is</em> the observation window.</para>
///
/// <para><b>Bi-temporality is honored upstream</b>: the API filters facts by
/// recordedDate &lt;= asOf before mapping observations (see CalibrationContracts.cs). Relationship
/// spans are the documented remaining approximation: they are read from current relationship rows
/// (there is no ledger of relationship starts and ends yet) and cut by <em>effective</em> date at
/// both ends here, so a relationship entered into the system after the as-of date but acquired
/// before it still contributes exposure that the recorded-date-cut numerator cannot see. The
/// denominator is therefore an upper bound on what was known, which biases these hazards
/// <em>downward</em>; refusing to fit on zero events is what keeps that bias from being reported
/// as a calibrated result.</para>
/// </summary>
public static class CalibrationEngine
{
    /// <summary>Equivalent sample size given to a Point prior so evidence can still move it.</summary>
    public const double PointPriorStrength = 50.0;

    /// <summary>Floor on the effective Beta sample size for very diffuse moment-matched priors.</summary>
    public const double MinBetaStrength = 0.1;

    /// <summary>Average month length in days, used to convert date spans to months.</summary>
    public const double DaysPerMonth = 30.4375;

    public static CalibrationOutput Run(CalibrationInput input)
    {
        Validate(input);

        var fittedFlows = new List<FittedFlowParameter>();
        var unfittedFlows = new List<UnfittedFlowParameter>();
        var fittedCycles = new List<FittedSalesCycle>();
        var unfittedCycles = new List<UnfittedSalesCycle>();

        // Client-month exposure per segment from relationship spans, cut at AsOfDate on BOTH ends:
        // a span that had not started by the as-of date contributes nothing, and one still open (or
        // ending later) is clipped there. The numerator of these Beta updates is the recorded-date
        // cut of the ledger, so an exposure denominator that ran past the as-of date would be
        // measuring a longer window than the events it divides — the arithmetic that made a
        // zero-event hazard collapse toward zero and still call itself Calibrated.
        var spanExposure = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var span in input.RelationshipSpans)
        {
            if (span.StartDate >= input.AsOfDate) continue; // not yet a client as of the cut
            DateOnly end = span.EndDate is { } e && e < input.AsOfDate ? e : input.AsOfDate;
            double months = (end.DayNumber - span.StartDate.DayNumber) / DaysPerMonth;
            if (months <= 0.0) continue;
            spanExposure.TryGetValue(span.SegmentId, out double acc);
            spanExposure[span.SegmentId] = acc + months;
        }

        foreach (var prior in input.FlowPriors)
        {
            var (fitted, unfitted) = prior.FlowKind switch
            {
                FlowKind.WinRate => FitWinRate(prior, input),
                FlowKind.ChurnHazard => FitChurnHazard(prior, input, spanExposure),
                FlowKind.CrossSellRate => FitCrossSellRate(prior, input, spanExposure),
                FlowKind.NewFirmArrival or FlowKind.ConversionToMarket => FitArrival(prior, input),
                _ => (null, PassThrough(prior, $"Flow kind {prior.FlowKind} has no calibration update.")),
            };
            if (fitted is not null) fittedFlows.Add(fitted);
            if (unfitted is not null) unfittedFlows.Add(unfitted);
        }

        foreach (var prior in input.SalesCyclePriors)
        {
            var (fitted, unfitted) = FitSalesCycle(prior, input);
            if (fitted is not null) fittedCycles.Add(fitted);
            if (unfitted is not null) unfittedCycles.Add(unfitted);
        }

        return new CalibrationOutput(input.AsOfDate, fittedFlows, unfittedFlows, fittedCycles, unfittedCycles);
    }

    // ---------------------------------------------------------------- per-flow fits

    private static (FittedFlowParameter?, UnfittedFlowParameter?) FitWinRate(
        FlowPrior prior, CalibrationInput input)
    {
        int wins = 0, losses = 0;
        DateOnly? min = null, max = null;
        foreach (var opp in input.Opportunities)
        {
            if (opp.IsCrossSell || opp.Outcome is not { } outcome) continue;
            if (!string.Equals(opp.SegmentId, prior.SegmentId, StringComparison.Ordinal)) continue;
            if (outcome == OpportunityResult.Won) wins++;
            else if (outcome == OpportunityResult.Lost) losses++;
            else continue; // NoDecision: excluded from the win-rate denominator (documented).
            DateOnly d = opp.DecidedDate ?? opp.OpenedDate;
            if (min is null || d < min) min = d;
            if (max is null || d > max) max = d;
        }

        int decided = wins + losses;
        if (decided == 0)
            return (null, PassThrough(prior,
                $"No decided (won/lost) opportunities for segment '{prior.SegmentId}' as of {input.AsOfDate:yyyy-MM-dd}."));

        if (!TryBetaPrior(prior.Distribution, out double a, out double b, out string? reason))
            return (null, PassThrough(prior, reason!));

        double aPost = a + wins;
        double bPost = b + losses;
        string rationale =
            $"Fitted from {decided} decided opportunities {min:yyyy-MM}..{max:yyyy-MM} " +
            $"(as of {input.AsOfDate:yyyy-MM-dd}): {wins} won / {decided} decided; " +
            "no-decision outcomes excluded from the denominator.";
        return (BetaFit(prior, aPost, bPost, rationale, decided), null);
    }

    private static (FittedFlowParameter?, UnfittedFlowParameter?) FitChurnHazard(
        FlowPrior prior, CalibrationInput input, Dictionary<string, double> spanExposure)
    {
        int churns = 0;
        DateOnly? min = null;
        foreach (var ev in input.ChurnEvents)
        {
            if (!string.Equals(ev.SegmentId, prior.SegmentId, StringComparison.Ordinal)) continue;
            churns++;
            if (min is null || ev.EffectiveDate < min) min = ev.EffectiveDate;
        }

        bool approximated = false;
        if (!spanExposure.TryGetValue(prior.SegmentId, out double exposure) || exposure <= 0.0)
        {
            if (churns == 0)
                return (null, PassThrough(prior,
                    $"No churn events and no relationship spans for segment '{prior.SegmentId}' — " +
                    "no exposure denominator to fit a hazard from."));

            // Documented approximation: each churned relationship assumed live for half the
            // observation window (earliest churn .. asOf); surviving clients uncounted.
            double windowMonths = Math.Max((input.AsOfDate.DayNumber - min!.Value.DayNumber) / DaysPerMonth, 1.0);
            exposure = Math.Max(churns * windowMonths / 2.0, churns);
            approximated = true;
        }

        // Zero observed events is not evidence of a low rate here: the numerator is cut at the
        // as-of date by RECORDED date while the denominator is exposure that merely *happened* by
        // then, so "no churn recorded yet" would otherwise drive the hazard toward zero with a
        // sample size of 0 and a Calibrated stamp on it. No events, no fit.
        if (churns == 0)
            return (null, PassThrough(prior,
                $"No observed churn events for segment '{prior.SegmentId}' as of " +
                $"{input.AsOfDate:yyyy-MM-dd} — {exposure:0.#} client-months of exposure alone is not " +
                "evidence of a rate, so the prior stands."));

        if (exposure < churns)
            throw new ArgumentException(
                $"ChurnHazard for segment '{prior.SegmentId}': {churns} churn events but only " +
                $"{exposure:0.##} client-months of relationship-span exposure. Exposure cannot be " +
                "smaller than the churn count — check the relationship spans.",
                "input");

        if (!TryBetaPrior(prior.Distribution, out double a, out double b, out string? reason))
            return (null, PassThrough(prior, reason!));

        double aPost = a + churns;
        double bPost = b + (exposure - churns);
        string rationale =
            $"Fitted from {churns} churn events over {exposure:0.#} client-months " +
            $"(as of {input.AsOfDate:yyyy-MM-dd})." +
            (approximated
                ? " Exposure approximated as churnCount x windowMonths/2 — no relationship spans provided."
                : "");
        return (BetaFit(prior, aPost, bPost, rationale, churns), null);
    }

    private static (FittedFlowParameter?, UnfittedFlowParameter?) FitCrossSellRate(
        FlowPrior prior, CalibrationInput input, Dictionary<string, double> spanExposure)
    {
        int wins = 0, decided = 0;
        foreach (var opp in input.Opportunities)
        {
            if (!opp.IsCrossSell || opp.Outcome is not { } outcome) continue;
            if (!string.Equals(opp.SegmentId, prior.SegmentId, StringComparison.Ordinal)) continue;
            if (!string.Equals(opp.ModuleId, prior.ModuleId, StringComparison.Ordinal)) continue;
            decided++;
            if (outcome == OpportunityResult.Won) wins++;
        }

        if (!spanExposure.TryGetValue(prior.SegmentId, out double exposure) || exposure <= 0.0)
            return (null, PassThrough(prior,
                $"No client-month exposure (relationship spans) for segment '{prior.SegmentId}' — " +
                $"cannot fit a cross-sell rate for module '{prior.ModuleId}'."));

        // Same rule as churn: an empty numerator against a full client-month denominator is not
        // evidence that nobody cross-sells, it is evidence that nothing has been recorded yet.
        if (wins == 0)
            return (null, PassThrough(prior,
                $"No observed cross-sell wins for module '{prior.ModuleId}' in segment " +
                $"'{prior.SegmentId}' as of {input.AsOfDate:yyyy-MM-dd} " +
                $"({decided} decided cross-sell opportunit{(decided == 1 ? "y" : "ies")}, " +
                $"{exposure:0.#} client-months of exposure) — the prior stands."));

        if (exposure < wins)
            throw new ArgumentException(
                $"CrossSellRate for segment '{prior.SegmentId}', module '{prior.ModuleId}': {wins} won " +
                $"cross-sells but only {exposure:0.##} client-months of exposure. Check the relationship spans.",
                "input");

        if (!TryBetaPrior(prior.Distribution, out double a, out double b, out string? reason))
            return (null, PassThrough(prior, reason!));

        double aPost = a + wins;
        double bPost = b + (exposure - wins);
        string rationale =
            $"Fitted from {decided} decided cross-sell opportunities for module '{prior.ModuleId}': " +
            $"{wins} won over {exposure:0.#} client-months (as of {input.AsOfDate:yyyy-MM-dd}).";
        return (BetaFit(prior, aPost, bPost, rationale, decided), null);
    }

    private static (FittedFlowParameter?, UnfittedFlowParameter?) FitArrival(
        FlowPrior prior, CalibrationInput input)
    {
        int rows = 0;
        long total = 0;
        double exposure = 0.0;
        DateOnly? min = null, max = null;
        foreach (var row in input.ArrivalCounts)
        {
            if (row.FlowKind != prior.FlowKind) continue;
            if (!string.Equals(row.SegmentId, prior.SegmentId, StringComparison.Ordinal)) continue;
            rows++;
            total += row.Count;
            exposure += row.Exposure;
            if (min is null || row.Month < min) min = row.Month;
            if (max is null || row.Month > max) max = row.Month;
        }

        if (rows == 0)
            return (null, PassThrough(prior,
                $"No monthly {prior.FlowKind} counts for segment '{prior.SegmentId}' as of {input.AsOfDate:yyyy-MM-dd}."));

        if (!TryGammaPrior(prior.Distribution, out double alpha, out double beta, out string? reason))
            return (null, PassThrough(prior, reason!));

        double alphaPost = alpha + total;
        double betaPost = beta + exposure;

        // Report the Gamma posterior moment-matched as a LogNormal (no Gamma family in
        // DistributionSpec; LogNormal keeps positive support). mean = a/b, var = a/b^2 =>
        // sigma^2 = ln(1 + 1/a), mu = ln(mean) - sigma^2/2.
        double mean = alphaPost / betaPost;
        double sigmaSq = Math.Log(1.0 + 1.0 / alphaPost);
        double mu = Math.Log(mean) - 0.5 * sigmaSq;
        var posterior = new DistributionSpec(DistributionType.LogNormal, mu, Math.Sqrt(sigmaSq));

        var (postMean, postSd) = Moments(posterior);
        var (priorMean, priorSd) = Moments(prior.Distribution);
        string rationale =
            $"Fitted from {rows} monthly counts {min:yyyy-MM}..{max:yyyy-MM} " +
            $"(as of {input.AsOfDate:yyyy-MM-dd}): {total} events over {exposure:0.#} exposure-months; " +
            "Gamma-Poisson posterior reported as a moment-matched LogNormal.";
        var fitted = new FittedFlowParameter(
            prior.SegmentId, prior.FlowKind, prior.ModuleId, posterior,
            CalibrationProvenance.Calibrated, rationale, rows,
            new ParameterDiff(priorMean, priorSd, postMean, postSd, postMean - priorMean));
        return (fitted, null);
    }

    private static (FittedSalesCycle?, UnfittedSalesCycle?) FitSalesCycle(
        SalesCyclePrior prior, CalibrationInput input)
    {
        // Durations from every decided opportunity (won, lost, or no-decision — all three mark a
        // real cycle end) in the segment; when the prior is product-specific, only opportunities
        // won by that product identify a per-product cycle.
        var logDurations = new List<double>();
        foreach (var opp in input.Opportunities)
        {
            if (opp.Outcome is null || opp.DecidedDate is not { } decidedDate) continue;
            if (!string.Equals(opp.SegmentId, prior.SegmentId, StringComparison.Ordinal)) continue;
            if (prior.ProductId is not null
                && !string.Equals(opp.WinnerProductId, prior.ProductId, StringComparison.Ordinal))
                continue;
            double months = Math.Max(
                (decidedDate.DayNumber - opp.OpenedDate.DayNumber) / DaysPerMonth,
                1.0 / DaysPerMonth); // same-day decisions clamped to one day
            logDurations.Add(Math.Log(months));
        }

        int n = logDurations.Count;
        if (n == 0)
        {
            string scope = prior.ProductId is null ? "" : $", product '{prior.ProductId}'";
            return (null, new UnfittedSalesCycle(prior.SegmentId, prior.ProductId, prior.Mu, prior.Sigma,
                $"No decided opportunities with dates for segment '{prior.SegmentId}'{scope} " +
                $"as of {input.AsOfDate:yyyy-MM-dd}."));
        }

        double m = 0.0;
        foreach (double x in logDurations) m += x;
        m /= n;
        double ss = 0.0;
        foreach (double x in logDurations) ss += (x - m) * (x - m);
        double s2 = ss / n;

        // Precision-weighted moment blend (not a full Normal-Inverse-Gamma update — deliberate).
        double w = prior.PriorWeight;
        double muPost = (w * prior.Mu + n * m) / (w + n);
        double sigmaPost = Math.Sqrt((w * prior.Sigma * prior.Sigma + n * s2) / (w + n));

        var (priorMean, priorSd) = Moments(new DistributionSpec(DistributionType.LogNormal, prior.Mu, prior.Sigma));
        var (postMean, postSd) = Moments(new DistributionSpec(DistributionType.LogNormal, muPost, sigmaPost));
        string rationale =
            $"Fitted from {n} decided opportunities (as of {input.AsOfDate:yyyy-MM-dd}): " +
            $"geometric-mean cycle {Math.Exp(m):0.#} months, prior weight {w:0.#} pseudo-observations.";
        var fitted = new FittedSalesCycle(
            prior.SegmentId, prior.ProductId, muPost, sigmaPost,
            CalibrationProvenance.Calibrated, rationale, n,
            new ParameterDiff(priorMean, priorSd, postMean, postSd, postMean - priorMean));
        return (fitted, null);
    }

    // ---------------------------------------------------------------- prior conversion & moments

    private static FittedFlowParameter BetaFit(
        FlowPrior prior, double aPost, double bPost, string rationale, int sampleSize)
    {
        var posterior = new DistributionSpec(DistributionType.Beta, aPost, bPost);
        var (priorMean, priorSd) = Moments(prior.Distribution);
        var (postMean, postSd) = Moments(posterior);
        return new FittedFlowParameter(
            prior.SegmentId, prior.FlowKind, prior.ModuleId, posterior,
            CalibrationProvenance.Calibrated, rationale, sampleSize,
            new ParameterDiff(priorMean, priorSd, postMean, postSd, postMean - priorMean));
    }

    private static UnfittedFlowParameter PassThrough(FlowPrior prior, string reason) =>
        new(prior.SegmentId, prior.FlowKind, prior.ModuleId, prior.Distribution, prior.Provenance, reason);

    /// <summary>Mean and standard deviation of a distribution spec.</summary>
    internal static (double Mean, double StdDev) Moments(DistributionSpec spec) => spec.Type switch
    {
        DistributionType.Point => (spec.P1, 0.0),
        DistributionType.Normal => (spec.P1, Math.Max(spec.P2, 0.0)),
        DistributionType.LogNormal => LogNormalMoments(spec.P1, spec.P2),
        DistributionType.Beta => BetaMoments(spec.P1, spec.P2),
        _ => throw new ArgumentException($"Unknown distribution type {spec.Type}.", nameof(spec)),
    };

    private static (double, double) LogNormalMoments(double mu, double sigma)
    {
        double mean = Math.Exp(mu + 0.5 * sigma * sigma);
        double sd = mean * Math.Sqrt(Math.Exp(sigma * sigma) - 1.0);
        return (mean, sd);
    }

    private static (double, double) BetaMoments(double a, double b)
    {
        double sum = a + b;
        double mean = a / sum;
        double sd = Math.Sqrt(a * b / (sum * sum * (sum + 1.0)));
        return (mean, sd);
    }

    /// <summary>
    /// Converts a probability prior to an effective Beta(a, b). Beta priors pass through;
    /// others are moment-matched; a Point prior gets <see cref="PointPriorStrength"/> as its
    /// equivalent sample size. Returns false with an actionable reason when the prior cannot
    /// represent a probability.
    /// </summary>
    private static bool TryBetaPrior(DistributionSpec spec, out double a, out double b, out string? reason)
    {
        a = b = 0.0;
        reason = null;
        if (spec.Type == DistributionType.Beta)
        {
            if (spec.P1 <= 0.0 || spec.P2 <= 0.0)
            {
                reason = $"Beta prior has non-positive shape parameters ({spec.P1:G4}, {spec.P2:G4}).";
                return false;
            }
            a = spec.P1;
            b = spec.P2;
            return true;
        }

        var (m, s) = Moments(spec);
        if (m <= 0.0 || m >= 1.0)
        {
            reason = $"Prior mean {m:G4} is not inside (0, 1) — cannot moment-match a probability " +
                     "parameter to a Beta. Fix the prior distribution.";
            return false;
        }

        double strength;
        if (s <= 0.0)
        {
            strength = PointPriorStrength; // asserted certainty, but still updatable
        }
        else
        {
            strength = m * (1.0 - m) / (s * s) - 1.0;
            if (strength < MinBetaStrength) strength = MinBetaStrength; // very diffuse prior
        }
        a = m * strength;
        b = (1.0 - m) * strength;
        return true;
    }

    /// <summary>
    /// Converts a rate prior to an effective Gamma(alpha, beta[rate]) via moment matching
    /// (alpha = m^2/s^2, beta = m/s^2); a Point prior gets a pseudo-exposure of
    /// <see cref="PointPriorStrength"/> months.
    /// </summary>
    private static bool TryGammaPrior(DistributionSpec spec, out double alpha, out double beta, out string? reason)
    {
        alpha = beta = 0.0;
        reason = null;
        var (m, s) = Moments(spec);
        if (m <= 0.0)
        {
            reason = $"Prior mean {m:G4} is not positive — cannot moment-match a rate parameter " +
                     "to a Gamma. Fix the prior distribution.";
            return false;
        }
        if (s <= 0.0)
        {
            beta = PointPriorStrength;
            alpha = m * beta;
        }
        else
        {
            alpha = m * m / (s * s);
            beta = m / (s * s);
        }
        return true;
    }

    // ---------------------------------------------------------------- validation

    private static void Validate(CalibrationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var flowKeys = new HashSet<(string, FlowKind, string?)>();
        foreach (var prior in input.FlowPriors)
        {
            if (string.IsNullOrEmpty(prior.SegmentId))
                throw new ArgumentException("Flow prior has an empty segmentId.", nameof(input));
            if (prior.ModuleId is not null && prior.FlowKind != FlowKind.CrossSellRate)
                throw new ArgumentException(
                    $"Flow prior ({prior.SegmentId}, {prior.FlowKind}) has moduleId '{prior.ModuleId}' " +
                    "but moduleId is only meaningful for CrossSellRate.", nameof(input));
            if (prior.FlowKind == FlowKind.CrossSellRate && string.IsNullOrEmpty(prior.ModuleId))
                throw new ArgumentException(
                    $"CrossSellRate prior for segment '{prior.SegmentId}' must carry a moduleId.", nameof(input));
            if (!flowKeys.Add((prior.SegmentId, prior.FlowKind, prior.ModuleId)))
                throw new ArgumentException(
                    $"Duplicate flow prior key ({prior.SegmentId}, {prior.FlowKind}" +
                    $"{(prior.ModuleId is null ? "" : $", {prior.ModuleId}")}).", nameof(input));
        }

        var cycleKeys = new HashSet<(string, string?)>();
        foreach (var prior in input.SalesCyclePriors)
        {
            if (string.IsNullOrEmpty(prior.SegmentId))
                throw new ArgumentException("Sales-cycle prior has an empty segmentId.", nameof(input));
            if (!(prior.Sigma >= 0.0))
                throw new ArgumentException(
                    $"Sales-cycle prior ({prior.SegmentId}, {prior.ProductId ?? "*"}) has sigma " +
                    $"{prior.Sigma:G4}; sigma must be >= 0.", nameof(input));
            if (!(prior.PriorWeight > 0.0))
                throw new ArgumentException(
                    $"Sales-cycle prior ({prior.SegmentId}, {prior.ProductId ?? "*"}) has priorWeight " +
                    $"{prior.PriorWeight:G4}; it must be > 0 pseudo-observations.", nameof(input));
            if (!cycleKeys.Add((prior.SegmentId, prior.ProductId)))
                throw new ArgumentException(
                    $"Duplicate sales-cycle prior key ({prior.SegmentId}, {prior.ProductId ?? "*"}).",
                    nameof(input));
        }

        foreach (var opp in input.Opportunities)
        {
            if (opp.DecidedDate is { } decided && decided < opp.OpenedDate)
                throw new ArgumentException(
                    $"Opportunity in segment '{opp.SegmentId}' decided {decided:yyyy-MM-dd} before it " +
                    $"opened {opp.OpenedDate:yyyy-MM-dd} — fix the fact dates.", nameof(input));
            if (opp.IsCrossSell && string.IsNullOrEmpty(opp.ModuleId))
                throw new ArgumentException(
                    $"Cross-sell opportunity in segment '{opp.SegmentId}' must carry the moduleId it " +
                    "was selling.", nameof(input));
        }

        foreach (var row in input.ArrivalCounts)
        {
            if (row.FlowKind is not (FlowKind.NewFirmArrival or FlowKind.ConversionToMarket))
                throw new ArgumentException(
                    $"ArrivalCount for segment '{row.SegmentId}' has flowKind {row.FlowKind}; only " +
                    "NewFirmArrival and ConversionToMarket take monthly count observations.", nameof(input));
            if (row.Count < 0)
                throw new ArgumentException(
                    $"ArrivalCount for segment '{row.SegmentId}', month {row.Month:yyyy-MM} has a " +
                    $"negative count ({row.Count}).", nameof(input));
            if (!(row.Exposure > 0.0))
                throw new ArgumentException(
                    $"ArrivalCount for segment '{row.SegmentId}', month {row.Month:yyyy-MM} has " +
                    $"non-positive exposure ({row.Exposure:G4}); use 1.0 for NewFirmArrival and the " +
                    "prospect-pool size for ConversionToMarket.", nameof(input));
        }
    }
}
