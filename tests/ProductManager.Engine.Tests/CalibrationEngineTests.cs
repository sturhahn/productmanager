using System.Text.Json;
using ProductManager.Engine;
using ProductManager.Engine.Calibration;

namespace ProductManager.Engine.Tests;

public class CalibrationEngineTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 29);
    private const string Seg = "seg-a";
    private const double DaysPerMonth = CalibrationEngine.DaysPerMonth;

    private static CalibrationInput Input(
        IReadOnlyList<FlowPrior>? flowPriors = null,
        IReadOnlyList<SalesCyclePrior>? cyclePriors = null,
        IReadOnlyList<OpportunityOutcome>? opportunities = null,
        IReadOnlyList<ChurnEvent>? churnEvents = null,
        IReadOnlyList<ArrivalCount>? arrivalCounts = null,
        IReadOnlyList<RelationshipSpan>? spans = null) =>
        new(AsOf,
            flowPriors ?? [],
            cyclePriors ?? [],
            opportunities ?? [],
            churnEvents ?? [],
            arrivalCounts ?? [],
            spans ?? []);

    private static OpportunityOutcome Opp(
        OpportunityResult? outcome,
        string segmentId = Seg,
        DateOnly? opened = null,
        DateOnly? decided = null,
        bool isCrossSell = false,
        string? moduleId = null,
        string? winnerProductId = null) =>
        new(segmentId, ChannelId: null,
            opened ?? new DateOnly(2026, 1, 15),
            decided ?? (outcome is null ? null : new DateOnly(2026, 4, 15)),
            outcome, ScopeProcessIds: [], winnerProductId, isCrossSell, moduleId);

    private static IReadOnlyList<OpportunityOutcome> Decided(int won, int lost, int noDecision = 0)
    {
        var list = new List<OpportunityOutcome>();
        for (int i = 0; i < won; i++) list.Add(Opp(OpportunityResult.Won));
        for (int i = 0; i < lost; i++) list.Add(Opp(OpportunityResult.Lost));
        for (int i = 0; i < noDecision; i++) list.Add(Opp(OpportunityResult.NoDecision));
        return list;
    }

    private static FlowPrior WinRatePrior(DistributionSpec dist, string segmentId = Seg) =>
        new(segmentId, FlowKind.WinRate, null, dist, CalibrationProvenance.Asserted);

    // ---------------------------------------------------------------- win rate

    [Fact]
    public void WinRate_LargeSample_RecoversEmpiricalRate_AndTightensPrior()
    {
        var prior = WinRatePrior(new DistributionSpec(DistributionType.Beta, 2, 2));
        var output = CalibrationEngine.Run(Input(flowPriors: [prior], opportunities: Decided(350, 150)));

        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal(DistributionType.Beta, fitted.Posterior.Type);
        Assert.Equal(352.0, fitted.Posterior.P1, 9);
        Assert.Equal(152.0, fitted.Posterior.P2, 9);
        Assert.Equal(CalibrationProvenance.Calibrated, fitted.Provenance);
        Assert.Equal(500, fitted.SampleSize);
        Assert.True(Math.Abs(fitted.Diff.PosteriorMean - 0.7) < 0.02);
        // Posterior is tighter than the prior.
        Assert.True(fitted.Diff.PosteriorStdDev < fitted.Diff.PriorStdDev);
        Assert.Equal(fitted.Diff.PosteriorMean - fitted.Diff.PriorMean, fitted.Diff.Shift, 12);
        Assert.Contains("500 decided opportunities", fitted.AutoRationale);
        Assert.Contains("no-decision outcomes excluded", fitted.AutoRationale);
        Assert.Contains("2026-08-29", fitted.AutoRationale);
        Assert.Empty(output.UnfittedFlows);
    }

    [Fact]
    public void WinRate_TinySample_BarelyMovesPrior_LargeSample_Converges()
    {
        // Strong prior at 0.3 (equivalent sample size 100), two winning observations.
        var prior = WinRatePrior(new DistributionSpec(DistributionType.Beta, 30, 70));
        var small = CalibrationEngine.Run(Input(flowPriors: [prior], opportunities: Decided(2, 0)));
        var smallFit = Assert.Single(small.FittedFlows);
        Assert.True(smallFit.Diff.Shift > 0.0);
        Assert.True(Math.Abs(smallFit.Diff.Shift) < 0.02,
            $"tiny sample moved the prior by {smallFit.Diff.Shift}");

        // 5000 decided at 70% overwhelm the same prior.
        var large = CalibrationEngine.Run(Input(flowPriors: [prior], opportunities: Decided(3500, 1500)));
        var largeFit = Assert.Single(large.FittedFlows);
        Assert.True(Math.Abs(largeFit.Diff.PosteriorMean - 0.7) < 0.02);
    }

    [Fact]
    public void WinRate_NoDecisionAndOpenOpportunities_ExcludedFromDenominator()
    {
        var prior = WinRatePrior(new DistributionSpec(DistributionType.Beta, 1, 1));
        var opps = Decided(10, 10, noDecision: 80).Concat([Opp(outcome: null)]).ToList();
        var output = CalibrationEngine.Run(Input(flowPriors: [prior], opportunities: opps));

        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal(20, fitted.SampleSize); // 80 no-decisions and 1 open excluded
        Assert.Equal(11.0, fitted.Posterior.P1, 9);
        Assert.Equal(11.0, fitted.Posterior.P2, 9);
    }

    [Fact]
    public void WinRate_NormalPrior_MomentMatchedToBeta()
    {
        // Normal(0.4, 0.1): k = 0.4*0.6/0.01 - 1 = 23 => Beta(9.2, 13.8).
        var prior = WinRatePrior(new DistributionSpec(DistributionType.Normal, 0.4, 0.1));
        var output = CalibrationEngine.Run(Input(flowPriors: [prior], opportunities: Decided(10, 5)));

        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal(9.2 + 10, fitted.Posterior.P1, 9);
        Assert.Equal(13.8 + 5, fitted.Posterior.P2, 9);
        Assert.Equal(0.4, fitted.Diff.PriorMean, 9);
        Assert.Equal(0.1, fitted.Diff.PriorStdDev, 9);
    }

    [Fact]
    public void WinRate_PointPrior_GetsEquivalentSampleSize_SoEvidenceStillMovesIt()
    {
        // Point(0.5) -> Beta(25, 25); 40 won / 10 lost -> Beta(65, 35), mean 0.65.
        var prior = WinRatePrior(DistributionSpec.Point(0.5));
        var output = CalibrationEngine.Run(Input(flowPriors: [prior], opportunities: Decided(40, 10)));

        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal(0.5 * CalibrationEngine.PointPriorStrength + 40, fitted.Posterior.P1, 9);
        Assert.Equal(0.5 * CalibrationEngine.PointPriorStrength + 10, fitted.Posterior.P2, 9);
        Assert.Equal(0.65, fitted.Diff.PosteriorMean, 9);
    }

    /// <summary>PRD8 acceptance criterion 1: an independent Beta(1,1) prior and 6 wins / 4 losses give Beta(7,5).</summary>
    [Fact]
    public void Prd8_IndependentUniformPrior_SixWinsFourLosses_YieldsBeta7_5()
    {
        var prior = WinRatePrior(new DistributionSpec(DistributionType.Beta, 1, 1));
        var output = CalibrationEngine.Run(Input(flowPriors: [prior], opportunities: Decided(6, 4)));

        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal(7.0, fitted.Posterior.P1, 12);
        Assert.Equal(5.0, fitted.Posterior.P2, 12);
        Assert.Equal(10, fitted.SampleSize);
    }

    /// <summary>
    /// PRD8 requirement 5: an observed zero-arrival month is Poisson evidence and adds one month of
    /// exposure; a month with no observation adds nothing — the engine never invents a zero for it.
    /// </summary>
    [Fact]
    public void Prd8_ObservedZeroArrivalMonth_AddsExposure_UnobservedMonthAddsNothing()
    {
        var prior = new FlowPrior(Seg, FlowKind.NewFirmArrival, null, DistributionSpec.Point(2.0), CalibrationProvenance.Asserted);
        ArrivalCount Month(int m, int count) => new(Seg, FlowKind.NewFirmArrival, new DateOnly(2026, m, 1), count);

        // Jan 4, Feb observed zero, Mar missing, Apr 4.
        var withZero = CalibrationEngine.Run(Input(flowPriors: [prior], arrivalCounts: [Month(1, 4), Month(2, 0), Month(4, 4)]));
        var withoutZero = CalibrationEngine.Run(Input(flowPriors: [prior], arrivalCounts: [Month(1, 4), Month(4, 4)]));

        double beta = CalibrationEngine.PointPriorStrength;
        double alpha = 2.0 * beta;
        static double Mean(FittedFlowParameter f) => Math.Exp(f.Posterior.P1 + 0.5 * f.Posterior.P2 * f.Posterior.P2);

        var zeroFit = Assert.Single(withZero.FittedFlows);
        Assert.Equal(3, zeroFit.SampleSize);
        Assert.Equal((alpha + 8) / (beta + 3), Mean(zeroFit), 9);

        var gapFit = Assert.Single(withoutZero.FittedFlows);
        Assert.Equal(2, gapFit.SampleSize);
        Assert.Equal((alpha + 8) / (beta + 2), Mean(gapFit), 9);
    }

    // ---------------------------------------------------------------- churn hazard

    [Fact]
    public void ChurnHazard_UsesClientMonthExposureFromSpans()
    {
        var prior = new FlowPrior(Seg, FlowKind.ChurnHazard, null,
            new DistributionSpec(DistributionType.Beta, 1, 99), CalibrationProvenance.Default);
        // 9 closed spans of exactly 365 days, one open span clipped at AsOf (also 365 days).
        var spans = Enumerable.Range(0, 9)
            .Select(_ => new RelationshipSpan(Seg, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1)))
            .Concat([new RelationshipSpan(Seg, new DateOnly(2025, 8, 29), null)])
            .ToList();
        var churns = Enumerable.Range(0, 6)
            .Select(_ => new ChurnEvent(Seg, new DateOnly(2026, 2, 1))).ToList();

        var output = CalibrationEngine.Run(Input(flowPriors: [prior], churnEvents: churns, spans: spans));

        double exposure = 10 * 365 / DaysPerMonth;
        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal(1 + 6, fitted.Posterior.P1, 9);
        Assert.Equal(99 + exposure - 6, fitted.Posterior.P2, 9);
        Assert.Equal(6, fitted.SampleSize);
        Assert.DoesNotContain("approximated", fitted.AutoRationale);
    }

    [Fact]
    public void ChurnHazard_WithoutSpans_UsesDocumentedExposureApproximation()
    {
        var prior = new FlowPrior(Seg, FlowKind.ChurnHazard, null,
            new DistributionSpec(DistributionType.Beta, 1, 99), CalibrationProvenance.Asserted);
        var churns = Enumerable.Range(0, 4)
            .Select(i => new ChurnEvent(Seg, new DateOnly(2025, 8, 29).AddMonths(i))).ToList();

        var output = CalibrationEngine.Run(Input(flowPriors: [prior], churnEvents: churns));

        // Window = earliest churn (2025-08-29) .. AsOf = 365 days; E = max(4 * W/2, 4) = 2W.
        double window = 365 / DaysPerMonth;
        double exposure = Math.Max(4 * window / 2.0, 4.0);
        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal(1 + 4, fitted.Posterior.P1, 9);
        Assert.Equal(99 + exposure - 4, fitted.Posterior.P2, 9);
        Assert.Contains("approximated", fitted.AutoRationale);
    }

    // ---------------------------------------------------------------- arrivals (Gamma-Poisson)

    [Fact]
    public void NewFirmArrival_GammaPoisson_RecoversMonthlyIntensity()
    {
        // Prior Normal(5, 3) -> Gamma(25/9, 5/9); 24 months of 8 arrivals each.
        var prior = new FlowPrior(Seg, FlowKind.NewFirmArrival, null,
            new DistributionSpec(DistributionType.Normal, 5, 3), CalibrationProvenance.Asserted);
        var counts = Enumerable.Range(0, 24)
            .Select(i => new ArrivalCount(Seg, FlowKind.NewFirmArrival, new DateOnly(2024, 9, 1).AddMonths(i), 8))
            .ToList();

        var output = CalibrationEngine.Run(Input(flowPriors: [prior], arrivalCounts: counts));

        double alphaPost = 25.0 / 9.0 + 24 * 8;
        double betaPost = 5.0 / 9.0 + 24;
        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal(DistributionType.LogNormal, fitted.Posterior.Type);
        // Moment-matched: the lognormal's implied mean equals the Gamma posterior mean exactly.
        double impliedMean = Math.Exp(fitted.Posterior.P1 + 0.5 * fitted.Posterior.P2 * fitted.Posterior.P2);
        Assert.Equal(alphaPost / betaPost, impliedMean, 9);
        Assert.True(Math.Abs(fitted.Diff.PosteriorMean - 8.0) < 0.2);
        Assert.True(fitted.Diff.PosteriorStdDev < fitted.Diff.PriorStdDev);
        Assert.Equal(24, fitted.SampleSize);
    }

    [Fact]
    public void ConversionToMarket_UsesPoolExposure()
    {
        // Point(0.05) -> Gamma(0.05 * 50, 50); 12 months of 20 conversions over a pool of 1000.
        var prior = new FlowPrior(Seg, FlowKind.ConversionToMarket, null,
            DistributionSpec.Point(0.05), CalibrationProvenance.Asserted);
        var counts = Enumerable.Range(0, 12)
            .Select(i => new ArrivalCount(
                Seg, FlowKind.ConversionToMarket, new DateOnly(2025, 9, 1).AddMonths(i), 20, Exposure: 1000.0))
            .ToList();

        var output = CalibrationEngine.Run(Input(flowPriors: [prior], arrivalCounts: counts));

        double alphaPost = 0.05 * CalibrationEngine.PointPriorStrength + 240;
        double betaPost = CalibrationEngine.PointPriorStrength + 12_000;
        var fitted = Assert.Single(output.FittedFlows);
        double impliedMean = Math.Exp(fitted.Posterior.P1 + 0.5 * fitted.Posterior.P2 * fitted.Posterior.P2);
        Assert.Equal(alphaPost / betaPost, impliedMean, 9);
        Assert.True(Math.Abs(fitted.Diff.PosteriorMean - 0.02) < 0.001);
    }

    // ---------------------------------------------------------------- cross-sell

    [Fact]
    public void CrossSellRate_BetaOnModuleWinsVsClientMonths()
    {
        var prior = new FlowPrior(Seg, FlowKind.CrossSellRate, "m1",
            new DistributionSpec(DistributionType.Beta, 1, 199), CalibrationProvenance.Default);
        var spans = new List<RelationshipSpan>
        {
            new(Seg, new DateOnly(2021, 8, 29), null), // 5 years, clipped at AsOf
        };
        var opps = new List<OpportunityOutcome>();
        for (int i = 0; i < 3; i++) opps.Add(Opp(OpportunityResult.Won, isCrossSell: true, moduleId: "m1"));
        for (int i = 0; i < 2; i++) opps.Add(Opp(OpportunityResult.Lost, isCrossSell: true, moduleId: "m1"));
        opps.Add(Opp(OpportunityResult.Won, isCrossSell: true, moduleId: "m2")); // other module: ignored
        opps.AddRange(Decided(3, 0)); // plain new-business opportunities: ignored by cross-sell

        var output = CalibrationEngine.Run(Input(flowPriors: [prior], opportunities: opps, spans: spans));

        double exposure = (AsOf.DayNumber - new DateOnly(2021, 8, 29).DayNumber) / DaysPerMonth;
        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal("m1", fitted.ModuleId);
        Assert.Equal(1 + 3, fitted.Posterior.P1, 9);
        Assert.Equal(199 + exposure - 3, fitted.Posterior.P2, 9);
        Assert.Equal(5, fitted.SampleSize);
        Assert.Contains("module 'm1'", fitted.AutoRationale);
    }

    // ---------------------------------------------------------------- sales cycle

    [Fact]
    public void SalesCycle_LogMomentBlend_RecoversDurations()
    {
        var prior = new SalesCyclePrior(Seg, null, Mu: Math.Log(6.0), Sigma: 0.5, PriorWeight: 10.0);
        var opened = new DateOnly(2026, 1, 1);
        var opps = Enumerable.Range(0, 200)
            .Select(_ => Opp(OpportunityResult.Won, opened: opened, decided: opened.AddDays(91)))
            .ToList();

        var output = CalibrationEngine.Run(Input(cyclePriors: [prior], opportunities: opps));

        double logDur = Math.Log(91 / DaysPerMonth);
        double expectedMu = (10.0 * Math.Log(6.0) + 200 * logDur) / 210.0;
        double expectedSigma = Math.Sqrt(10.0 * 0.25 / 210.0); // observed variance is 0
        var fitted = Assert.Single(output.FittedSalesCycles);
        Assert.Equal(expectedMu, fitted.Mu, 9);
        Assert.Equal(expectedSigma, fitted.Sigma, 9);
        Assert.Equal(200, fitted.SampleSize);
        Assert.Equal(CalibrationProvenance.Calibrated, fitted.Provenance);
        // Converged near the empirical geometric mean (~3 months), far from the 6-month prior.
        Assert.True(Math.Abs(fitted.Mu - logDur) < 0.05);
        Assert.Contains("200 decided opportunities", fitted.AutoRationale);
    }

    [Fact]
    public void SalesCycle_TinySample_BarelyMovesPrior_AndProductPriorFiltersByWinner()
    {
        var segPrior = new SalesCyclePrior(Seg, null, Mu: Math.Log(6.0), Sigma: 0.5, PriorWeight: 10.0);
        var productPrior = new SalesCyclePrior(Seg, "p1", Mu: Math.Log(6.0), Sigma: 0.5, PriorWeight: 10.0);
        var opened = new DateOnly(2026, 1, 1);
        var opps = new List<OpportunityOutcome>
        {
            Opp(OpportunityResult.Won, opened: opened, decided: opened.AddDays(91), winnerProductId: "p1"),
            Opp(OpportunityResult.Lost, opened: opened, decided: opened.AddDays(300), winnerProductId: "p2"),
        };

        var output = CalibrationEngine.Run(Input(cyclePriors: [segPrior, productPrior], opportunities: opps));

        Assert.Equal(2, output.FittedSalesCycles.Count);
        var seg = output.FittedSalesCycles.Single(f => f.ProductId is null);
        var p1 = output.FittedSalesCycles.Single(f => f.ProductId == "p1");
        Assert.Equal(2, seg.SampleSize);   // both decided opportunities
        Assert.Equal(1, p1.SampleSize);    // only the one won by p1
        // One observation against prior weight 10 barely moves mu.
        double logDur = Math.Log(91 / DaysPerMonth);
        Assert.Equal((10.0 * Math.Log(6.0) + logDur) / 11.0, p1.Mu, 9);
        Assert.True(Math.Abs(p1.Mu - Math.Log(6.0)) < 0.1);
    }

    // ---------------------------------------------------------------- no fit on zero evidence

    /// <summary>
    /// Exposure is not evidence. A segment with years of client-months but no churn recorded by the
    /// as-of date must not be "fitted" to a near-zero hazard: the numerator is cut by recorded date
    /// and the denominator is not, so an empty numerator says nothing about the rate.
    /// </summary>
    [Fact]
    public void ChurnHazard_WithExposureButNoEvents_PassesThroughUntouched()
    {
        var prior = new FlowPrior(Seg, FlowKind.ChurnHazard, null,
            new DistributionSpec(DistributionType.Beta, 2, 98), CalibrationProvenance.Asserted);
        var spans = Enumerable.Range(0, 20)
            .Select(_ => new RelationshipSpan(Seg, new DateOnly(2021, 1, 1), null))
            .ToList();

        var output = CalibrationEngine.Run(Input(flowPriors: [prior], spans: spans));

        Assert.Empty(output.FittedFlows);
        var unfitted = Assert.Single(output.UnfittedFlows);
        Assert.Equal(FlowKind.ChurnHazard, unfitted.FlowKind);
        Assert.Equal(prior.Distribution, unfitted.Distribution); // prior untouched, shape and both shapes
        Assert.Equal(prior.Provenance, unfitted.Provenance);     // still Asserted, never stamped Calibrated
        Assert.Contains("No observed churn events", unfitted.Reason);
        Assert.Contains("2026-08-29", unfitted.Reason);

        // One churn is enough to fit: the rule is "no events", not "few events".
        var withOne = CalibrationEngine.Run(Input(
            flowPriors: [prior], spans: spans, churnEvents: [new ChurnEvent(Seg, new DateOnly(2026, 5, 1))]));
        Assert.Equal(1, Assert.Single(withOne.FittedFlows).SampleSize);
    }

    [Fact]
    public void CrossSellRate_WithExposureButNoWins_PassesThroughUntouched()
    {
        var prior = new FlowPrior(Seg, FlowKind.CrossSellRate, "m1",
            new DistributionSpec(DistributionType.Beta, 1, 199), CalibrationProvenance.Calibrated);
        var spans = new List<RelationshipSpan> { new(Seg, new DateOnly(2021, 8, 29), null) };

        // Two cross-sell attempts, both lost: still zero wins, so still no numerator.
        var opps = new List<OpportunityOutcome>
        {
            Opp(OpportunityResult.Lost, isCrossSell: true, moduleId: "m1"),
            Opp(OpportunityResult.Lost, isCrossSell: true, moduleId: "m1"),
        };

        var output = CalibrationEngine.Run(Input(flowPriors: [prior], opportunities: opps, spans: spans));

        Assert.Empty(output.FittedFlows);
        var unfitted = Assert.Single(output.UnfittedFlows);
        Assert.Equal(prior.Distribution, unfitted.Distribution);
        Assert.Contains("No observed cross-sell wins", unfitted.Reason);
        Assert.Contains("2 decided cross-sell opportunities", unfitted.Reason);
    }

    /// <summary>
    /// Span exposure is cut at the as-of date on both ends: a relationship acquired after the cut
    /// contributes nothing, and one still open is clipped there.
    /// </summary>
    [Fact]
    public void SpanExposure_IsCutAtTheAsOfDateOnBothEnds()
    {
        var prior = new FlowPrior(Seg, FlowKind.ChurnHazard, null,
            new DistributionSpec(DistributionType.Beta, 1, 99), CalibrationProvenance.Asserted);
        var spans = new List<RelationshipSpan>
        {
            new(Seg, new DateOnly(2025, 8, 29), null),              // open: clipped at AsOf = 365 days
            new(Seg, new DateOnly(2026, 9, 1), null),               // starts after the cut: ignored
            new(Seg, new DateOnly(2025, 8, 29), new DateOnly(2027, 1, 1)), // ends after the cut: clipped
        };

        var output = CalibrationEngine.Run(Input(
            flowPriors: [prior], spans: spans, churnEvents: [new ChurnEvent(Seg, new DateOnly(2026, 5, 1))]));

        double exposure = 2 * 365 / DaysPerMonth;
        var fitted = Assert.Single(output.FittedFlows);
        Assert.Equal(1 + 1, fitted.Posterior.P1, 9);
        Assert.Equal(99 + exposure - 1, fitted.Posterior.P2, 9);
    }

    // ---------------------------------------------------------------- pass-through

    [Fact]
    public void UnfittableParameters_PassThroughUnchangedWithReasons()
    {
        var winNoData = WinRatePrior(new DistributionSpec(DistributionType.Beta, 3, 7), segmentId: "seg-empty");
        var winBadPrior = WinRatePrior(DistributionSpec.Point(1.5)); // mean outside (0,1)
        var churnNoData = new FlowPrior("seg-empty", FlowKind.ChurnHazard, null,
            new DistributionSpec(DistributionType.Beta, 1, 99), CalibrationProvenance.Asserted);
        var crossNoSpans = new FlowPrior(Seg, FlowKind.CrossSellRate, "m1",
            new DistributionSpec(DistributionType.Beta, 1, 199), CalibrationProvenance.Default);
        var arrivalNoData = new FlowPrior(Seg, FlowKind.NewFirmArrival, null,
            DistributionSpec.Point(5.0), CalibrationProvenance.Asserted);
        var cycleNoData = new SalesCyclePrior("seg-empty", null, Math.Log(4.0), 0.3);

        var output = CalibrationEngine.Run(Input(
            flowPriors: [winNoData, winBadPrior, churnNoData, crossNoSpans, arrivalNoData],
            cyclePriors: [cycleNoData],
            opportunities: Decided(5, 5)));

        Assert.Empty(output.FittedFlows);
        Assert.Equal(5, output.UnfittedFlows.Count);
        foreach (var u in output.UnfittedFlows) Assert.False(string.IsNullOrWhiteSpace(u.Reason));

        var win = output.UnfittedFlows.Single(u => u.FlowKind == FlowKind.WinRate && u.SegmentId == "seg-empty");
        Assert.Equal(winNoData.Distribution, win.Distribution); // prior passed through unchanged
        Assert.Equal(CalibrationProvenance.Asserted, win.Provenance);
        Assert.Contains("No decided", win.Reason);

        var bad = output.UnfittedFlows.Single(u => u.FlowKind == FlowKind.WinRate && u.SegmentId == Seg);
        Assert.Contains("not inside (0, 1)", bad.Reason);

        Assert.Contains("no exposure denominator",
            output.UnfittedFlows.Single(u => u.FlowKind == FlowKind.ChurnHazard).Reason);
        Assert.Contains("relationship spans",
            output.UnfittedFlows.Single(u => u.FlowKind == FlowKind.CrossSellRate).Reason);
        Assert.Contains("No monthly NewFirmArrival counts",
            output.UnfittedFlows.Single(u => u.FlowKind == FlowKind.NewFirmArrival).Reason);

        var cycle = Assert.Single(output.UnfittedSalesCycles);
        Assert.Equal(Math.Log(4.0), cycle.Mu, 12);
        Assert.Contains("No decided opportunities", cycle.Reason);
        Assert.Empty(output.FittedSalesCycles);
    }

    // ---------------------------------------------------------------- determinism

    [Fact]
    public void Run_IsExactlyReproducible()
    {
        var input = Input(
            flowPriors:
            [
                WinRatePrior(new DistributionSpec(DistributionType.Normal, 0.35, 0.12)),
                new FlowPrior(Seg, FlowKind.ChurnHazard, null,
                    new DistributionSpec(DistributionType.Beta, 2, 98), CalibrationProvenance.Default),
                new FlowPrior(Seg, FlowKind.NewFirmArrival, null,
                    new DistributionSpec(DistributionType.LogNormal, Math.Log(4.0), 0.4),
                    CalibrationProvenance.Asserted),
                new FlowPrior(Seg, FlowKind.CrossSellRate, "m1",
                    new DistributionSpec(DistributionType.Beta, 1, 199), CalibrationProvenance.Default),
                WinRatePrior(new DistributionSpec(DistributionType.Beta, 3, 7), segmentId: "seg-empty"),
            ],
            cyclePriors: [new SalesCyclePrior(Seg, null, Math.Log(5.0), 0.6)],
            opportunities: Decided(17, 9, noDecision: 4)
                .Concat([Opp(OpportunityResult.Won, isCrossSell: true, moduleId: "m1")]).ToList(),
            churnEvents: [new ChurnEvent(Seg, new DateOnly(2026, 3, 1))],
            arrivalCounts:
            [
                new ArrivalCount(Seg, FlowKind.NewFirmArrival, new DateOnly(2026, 1, 1), 5),
                new ArrivalCount(Seg, FlowKind.NewFirmArrival, new DateOnly(2026, 2, 1), 7),
            ],
            spans: [new RelationshipSpan(Seg, new DateOnly(2024, 1, 1), null)]);

        var a = CalibrationEngine.Run(input);
        var b = CalibrationEngine.Run(input);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
        // Sanity: everything with data actually fitted.
        Assert.Equal(4, a.FittedFlows.Count);
        Assert.Single(a.UnfittedFlows);
        Assert.Single(a.FittedSalesCycles);
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void Validation_RejectsMalformedInputWithActionableMessages()
    {
        var winPrior = WinRatePrior(new DistributionSpec(DistributionType.Beta, 1, 1));

        var dup = Assert.Throws<ArgumentException>(() =>
            CalibrationEngine.Run(Input(flowPriors: [winPrior, winPrior])));
        Assert.Contains("Duplicate flow prior key", dup.Message);

        var moduleOnWin = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            flowPriors: [new FlowPrior(Seg, FlowKind.WinRate, "m1",
                new DistributionSpec(DistributionType.Beta, 1, 1), CalibrationProvenance.Asserted)])));
        Assert.Contains("only meaningful for CrossSellRate", moduleOnWin.Message);

        var crossNoModule = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            flowPriors: [new FlowPrior(Seg, FlowKind.CrossSellRate, null,
                new DistributionSpec(DistributionType.Beta, 1, 1), CalibrationProvenance.Asserted)])));
        Assert.Contains("must carry a moduleId", crossNoModule.Message);

        var badArrivalKind = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            arrivalCounts: [new ArrivalCount(Seg, FlowKind.WinRate, new DateOnly(2026, 1, 1), 3)])));
        Assert.Contains("only NewFirmArrival and ConversionToMarket", badArrivalKind.Message);

        var negativeCount = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            arrivalCounts: [new ArrivalCount(Seg, FlowKind.NewFirmArrival, new DateOnly(2026, 1, 1), -1)])));
        Assert.Contains("negative count", negativeCount.Message);

        var zeroExposure = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            arrivalCounts: [new ArrivalCount(Seg, FlowKind.ConversionToMarket, new DateOnly(2026, 1, 1), 3, 0.0)])));
        Assert.Contains("non-positive exposure", zeroExposure.Message);

        var decidedBeforeOpened = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            opportunities: [Opp(OpportunityResult.Won,
                opened: new DateOnly(2026, 5, 1), decided: new DateOnly(2026, 4, 1))])));
        Assert.Contains("before it opened", decidedBeforeOpened.Message);

        var crossOppNoModule = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            opportunities: [Opp(OpportunityResult.Won, isCrossSell: true)])));
        Assert.Contains("must carry the moduleId", crossOppNoModule.Message);

        var badSigma = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            cyclePriors: [new SalesCyclePrior(Seg, null, 1.0, -0.5)])));
        Assert.Contains("sigma must be >= 0", badSigma.Message);

        var badWeight = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            cyclePriors: [new SalesCyclePrior(Seg, null, 1.0, 0.5, PriorWeight: 0.0)])));
        Assert.Contains("must be > 0 pseudo-observations", badWeight.Message);

        // Exposure smaller than the churn count is inconsistent span data.
        var lowExposure = Assert.Throws<ArgumentException>(() => CalibrationEngine.Run(Input(
            flowPriors: [new FlowPrior(Seg, FlowKind.ChurnHazard, null,
                new DistributionSpec(DistributionType.Beta, 1, 99), CalibrationProvenance.Asserted)],
            churnEvents: [new ChurnEvent(Seg, new DateOnly(2026, 8, 1))],
            spans: [new RelationshipSpan(Seg, new DateOnly(2026, 8, 20), null)]))); // ~0.3 client-months
        Assert.Contains("Exposure cannot be smaller", lowExposure.Message);
    }
}
