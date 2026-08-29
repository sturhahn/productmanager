using System.Diagnostics;
using System.Text.Json;
using ProductManager.Engine;

namespace ProductManager.Engine.Tests;

public class SimulationEngineTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = false };

    private static SegmentInput Segment(
        string id = "seg-a",
        double pool = 500,
        int clients = 100,
        double arpu = 1000,
        double implFee = 5000,
        IReadOnlyList<ModulePenetration>? modules = null,
        CostRates? costs = null)
        => new(id, pool, clients, arpu, implFee, modules ?? [], costs ?? new CostRates(50, 2000, 10, 1000));

    private static ParameterSpec Param(
        string segId, FlowKind flow, DistributionSpec dist, string? moduleId = null, CovariateLink? cov = null)
        => new(segId, flow, moduleId, dist, cov);

    private static EngineInput BaseInput(
        IReadOnlyList<SegmentInput>? segments = null,
        IReadOnlyList<ParameterSpec>? parameters = null,
        IReadOnlyList<CovariateInput>? covariates = null,
        IReadOnlyList<EventInput>? events = null,
        IReadOnlyList<AttractivenessEntry>? attractiveness = null,
        int months = 24,
        int paths = 200,
        int seed = 42)
        => new(
            segments ?? [Segment()],
            parameters ??
            [
                Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(5)),
                Param("seg-a", FlowKind.ConversionToMarket, DistributionSpec.Point(0.05)),
                Param("seg-a", FlowKind.WinRate, DistributionSpec.Point(0.3)),
                Param("seg-a", FlowKind.ChurnHazard, DistributionSpec.Point(0.01)),
            ],
            covariates ?? [],
            events ?? [],
            attractiveness ?? [],
            months,
            paths,
            seed);

    // ---------- determinism ----------

    [Fact]
    public void SameSeed_ProducesByteIdenticalResults()
    {
        var input = BaseInput(parameters:
        [
            Param("seg-a", FlowKind.NewFirmArrival, new DistributionSpec(DistributionType.LogNormal, 1.5, 0.4)),
            Param("seg-a", FlowKind.ConversionToMarket, new DistributionSpec(DistributionType.Beta, 2, 30)),
            Param("seg-a", FlowKind.WinRate, new DistributionSpec(DistributionType.Normal, 0.3, 0.05)),
            Param("seg-a", FlowKind.ChurnHazard, new DistributionSpec(DistributionType.Beta, 1, 60)),
        ]);

        var a = SimulationEngine.Run(input);
        var b = SimulationEngine.Run(input);

        string jsonA = JsonSerializer.Serialize(a with { ElapsedMs = 0 }, JsonOptions);
        string jsonB = JsonSerializer.Serialize(b with { ElapsedMs = 0 }, JsonOptions);
        Assert.Equal(jsonA, jsonB);
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentResults()
    {
        var a = SimulationEngine.Run(BaseInput(seed: 1));
        var b = SimulationEngine.Run(BaseInput(seed: 2));
        Assert.NotEqual(a.Npv.Mean, b.Npv.Mean);
    }

    [Fact]
    public void IncreasingPathCount_LeavesEarlierPathsUnchanged()
    {
        var small = SimulationEngine.Run(BaseInput(paths: 50));
        var large = SimulationEngine.Run(BaseInput(paths: 120));
        for (int p = 0; p < 50; p++)
            Assert.Equal(small.NpvPerPath[p], large.NpvPerPath[p]);
    }

    // ---------- static / analytic checks ----------

    [Fact]
    public void AllPointZeroRates_StaticStocksAndZeroBookings()
    {
        var input = BaseInput(
            parameters:
            [
                Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(0)),
                Param("seg-a", FlowKind.ConversionToMarket, DistributionSpec.Point(0)),
                Param("seg-a", FlowKind.WinRate, DistributionSpec.Point(0)),
                Param("seg-a", FlowKind.ChurnHazard, DistributionSpec.Point(0)),
            ],
            months: 12, paths: 20);

        var result = SimulationEngine.Run(input);
        var seg = result.Segments[0];
        for (int t = 0; t < 12; t++)
        {
            Assert.Equal(100, seg.Clients.Mean[t]);
            Assert.Equal(100, seg.Clients.P10[t]);
            Assert.Equal(100, seg.Clients.P90[t]);
            Assert.Equal(500, seg.ProspectPool.Mean[t]);
            Assert.Equal(0, seg.NewWins.Mean[t]);
            Assert.Equal(0, seg.Churned.Mean[t]);
            Assert.Equal(0, seg.Bookings.Mean[t]);
            Assert.Equal(100 * 1000 * 12, seg.RecognizedArr.Mean[t]);
            // margin constant: 100*1000 - (100*(50+10) + 2000 + 1000)
            Assert.Equal(100_000 - 9_000, seg.ContributionMargin.Mean[t]);
        }
    }

    [Fact]
    public void ChurnOnly_MeanClientsFollowAnalyticDecay()
    {
        const double hazard = 0.05;
        const int initialClients = 1000;
        var input = BaseInput(
            segments: [Segment(clients: initialClients, pool: 0)],
            parameters: [Param("seg-a", FlowKind.ChurnHazard, DistributionSpec.Point(hazard))],
            months: 24, paths: 2000, seed: 7);

        var result = SimulationEngine.Run(input);
        var clients = result.Segments[0].Clients.Mean;
        for (int t = 0; t < 24; t++)
        {
            double expected = initialClients * Math.Pow(1 - hazard, t + 1);
            // MC tolerance: std error of Binomial mean over 2000 paths is small; allow 3%.
            Assert.InRange(clients[t], expected * 0.97, expected * 1.03);
        }
    }

    // ---------- covariates ----------

    [Fact]
    public void CovariateBeta_RaisesArrivalsMonotonically()
    {
        var covPath = new double[36];
        for (int t = 0; t < 36; t++) covPath[t] = 1.0 + t * 0.1; // rising path from 1.0

        double FinalPool(double beta)
        {
            var input = BaseInput(
                segments: [Segment(pool: 0, clients: 0)],
                parameters:
                [
                    Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(10),
                        cov: new CovariateLink("index", beta)),
                ],
                covariates: [new CovariateInput("index", MonthlyPath: covPath)],
                months: 36, paths: 400, seed: 11);
            var result = SimulationEngine.Run(input);
            return result.Segments[0].ProspectPool.Mean[35];
        }

        double none = FinalPool(0.0);
        double mild = FinalPool(0.3);
        double strong = FinalPool(0.6);
        Assert.True(none < mild, $"expected {none} < {mild}");
        Assert.True(mild < strong, $"expected {mild} < {strong}");
    }

    [Fact]
    public void GbmCovariate_RunsDeterministicallyAndAffectsFlows()
    {
        var input = BaseInput(
            segments: [Segment(pool: 0, clients: 0)],
            parameters:
            [
                Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(10),
                    cov: new CovariateLink("index", 0.5)),
            ],
            covariates: [new CovariateInput("index", Gbm: new GbmSpec(Start: 1.0, Drift: 0.01, Vol: 0.05))],
            months: 24, paths: 100, seed: 3);

        var a = SimulationEngine.Run(input);
        var b = SimulationEngine.Run(input);
        Assert.Equal(a.Segments[0].ProspectPool.Mean[23], b.Segments[0].ProspectPool.Mean[23]);
        Assert.True(a.Segments[0].ProspectPool.Mean[23] > 0);
    }

    /// <summary>
    /// Beta is an elasticity, so the covariate enters as a ratio and its units cannot matter: the
    /// same shape quoted as an index around 100 and normalised to 1.0 must give identical numbers.
    /// The old semi-log form (exp(beta * (x_t - x_0))) failed exactly here — it read raw level
    /// differences, so a level-scale series produced an astronomically larger multiplier.
    /// </summary>
    [Fact]
    public void CovariateEffect_IsScaleFree_SameShapeAtIndexAndNormalisedUnits()
    {
        var shape = new double[36];
        for (int t = 0; t < 36; t++) shape[t] = 1.0 + 0.004 * t; // +0.4%/month

        double FinalPool(double scale)
        {
            var path = shape.Select(v => v * scale).ToArray();
            var input = BaseInput(
                segments: [Segment(pool: 0, clients: 0)],
                parameters:
                [
                    Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(10),
                        cov: new CovariateLink("index", 0.6)),
                ],
                covariates: [new CovariateInput("index", MonthlyPath: path)],
                months: 36, paths: 200, seed: 23);
            return SimulationEngine.Run(input).Segments[0].ProspectPool.Mean[35];
        }

        Assert.Equal(FinalPool(1.0), FinalPool(108.6));
        Assert.Equal(FinalPool(1.0), FinalPool(0.01));
    }

    /// <summary>Elasticity semantics: doubling the covariate scales the rate by 2^beta.</summary>
    [Theory]
    [InlineData(1.0, 2.0)]
    [InlineData(0.5, 1.41421356)]
    [InlineData(-1.0, 0.5)]
    public void CovariateBeta_ScalesRateByRatioToThePowerBeta(double beta, double expectedFactor)
    {
        // Flat at 100 for the first 24 months, flat at 200 for the next 24.
        var path = new double[48];
        for (int t = 0; t < 48; t++) path[t] = t < 24 ? 100.0 : 200.0;

        var input = BaseInput(
            segments: [Segment(pool: 0, clients: 0)],
            parameters:
            [
                Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(20),
                    cov: new CovariateLink("index", beta)),
            ],
            covariates: [new CovariateInput("index", MonthlyPath: path)],
            months: 48, paths: 3000, seed: 31);

        var pool = SimulationEngine.Run(input).Segments[0].ProspectPool.Mean;
        double beforeRate = pool[23] / 24.0;                 // mean arrivals/month at x = x_0
        double afterRate = (pool[47] - pool[23]) / 24.0;      // mean arrivals/month at x = 2 * x_0

        Assert.InRange(beforeRate, 20 * 0.97, 20 * 1.03);
        Assert.InRange(afterRate / beforeRate, expectedFactor * 0.95, expectedFactor * 1.05);
    }

    /// <summary>
    /// Regression: a level-scale covariate (an equity index around 108, as seed/phase2-seed.json
    /// ships) linked at beta 0.6 over five years must stay sane. Under the old semi-log form the
    /// month-59 multiplier reached ~1e5 and NPV ~1e24; the elasticity form keeps it near 1.
    /// </summary>
    [Fact]
    public void EquityIndexScaleCovariate_OverFiveYears_KeepsMultiplierSane()
    {
        const double baseArrival = 10.0;

        EngineInput Input(double beta) => BaseInput(
            segments: [Segment(pool: 0, clients: 0)],
            parameters:
            [
                Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(baseArrival),
                    cov: new CovariateLink("equity", beta)),
            ],
            // Matches the seeded "Equity Market Index": start 108.6, drift 0.004, vol 0.045.
            covariates: [new CovariateInput("equity", Gbm: new GbmSpec(108.6, 0.004, 0.045))],
            months: 60, paths: 1000, seed: 4242);

        var linked = SimulationEngine.Run(Input(0.6));
        var unlinked = SimulationEngine.Run(Input(0.0));

        var linkedPool = linked.Segments[0].ProspectPool;
        var unlinkedPool = unlinked.Segments[0].ProspectPool;

        // Realised multiplier in the final month: mean arrivals in month 59 over the base rate.
        double multiplier = (linkedPool.Mean[59] - linkedPool.Mean[58]) / baseArrival;
        Assert.InRange(multiplier, 0.5, 5.0);

        // The whole trajectory stays within a factor of a few of the unlinked run — no drift into
        // a regime where the covariate, not the assumption set, decides the answer.
        for (int t = 0; t < 60; t++)
            Assert.InRange(linkedPool.Mean[t] / Math.Max(1e-9, unlinkedPool.Mean[t]), 0.5, 5.0);

        // Even the upper tail is a business-plausible number, and NPV is finite and small.
        Assert.InRange(linkedPool.P90[59] / unlinkedPool.Mean[59], 0.5, 5.0);
        Assert.True(double.IsFinite(linked.Npv.Mean));
        Assert.True(Math.Abs(linked.Npv.Mean) < 1e9, $"NPV {linked.Npv.Mean:G6} is not a sane magnitude");
    }

    [Fact]
    public void NonPositiveCovariatePath_IsRejectedWhenLinkedWithNonZeroBeta()
    {
        static EngineInput WithPath(double[] path, double beta) => BaseInput(
            parameters:
            [
                Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(5),
                    cov: new CovariateLink("index", beta)),
            ],
            covariates: [new CovariateInput("index", MonthlyPath: path)],
            months: 24, paths: 10);

        var startsAtZero = new double[24];
        for (int t = 0; t < 24; t++) startsAtZero[t] = t * 0.1; // the old tests' rising-from-zero path

        var ex = Assert.Throws<ArgumentException>(() => SimulationEngine.Run(WithPath(startsAtZero, 0.6)));
        Assert.Contains("index", ex.Message, StringComparison.Ordinal);
        Assert.Contains("month 0", ex.Message, StringComparison.Ordinal);

        var goesNegative = new double[24];
        for (int t = 0; t < 24; t++) goesNegative[t] = 1.0 - t * 0.1;
        var negEx = Assert.Throws<ArgumentException>(() => SimulationEngine.Run(WithPath(goesNegative, 0.6)));
        Assert.Contains("month 10", negEx.Message, StringComparison.Ordinal);

        // A GBM that starts non-positive is rejected the same way.
        Assert.Throws<ArgumentException>(() => SimulationEngine.Run(BaseInput(
            parameters:
            [
                Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(5),
                    cov: new CovariateLink("index", -0.3)),
            ],
            covariates: [new CovariateInput("index", Gbm: new GbmSpec(0.0, 0.01, 0.05))],
            months: 24, paths: 10)));

        // Beta 0 never reads the series, so a non-positive path is harmless and stays accepted.
        var ok = SimulationEngine.Run(WithPath(startsAtZero, 0.0));
        Assert.Equal(24, ok.HorizonMonths);
    }

    [Fact]
    public void RunawayFlowMultiplier_FailsFastNamingSegmentFlowAndMonth()
    {
        var input = BaseInput(
            segments: [Segment("seg-a", clients: 100), Segment("seg-b", clients: 100)],
            parameters: [Param("seg-b", FlowKind.ChurnHazard, DistributionSpec.Point(0.01))],
            events:
            [
                new EventInput("absurd", 7, 9, [new EventRateMultiplier("seg-b", FlowKind.ChurnHazard, 5000.0)]),
            ],
            months: 24, paths: 5);

        var ex = Assert.Throws<ArgumentException>(() => SimulationEngine.Run(input));
        Assert.Contains("seg-b", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ChurnHazard", ex.Message, StringComparison.Ordinal);
        Assert.Contains("month 7", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1000x limit", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunawayFlowMultiplier_AlsoCatchesAMisScaledCovariate()
    {
        // A covariate that grows 100x with beta 3 => 1e6x on the rate: caught, not silently run.
        var path = new double[24];
        for (int t = 0; t < 24; t++) path[t] = 1.0 + t * 5.0;

        var input = BaseInput(
            parameters:
            [
                Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(1),
                    cov: new CovariateLink("index", 3.0)),
            ],
            covariates: [new CovariateInput("index", MonthlyPath: path)],
            months: 24, paths: 5);

        var ex = Assert.Throws<ArgumentException>(() => SimulationEngine.Run(input));
        Assert.Contains("NewFirmArrival", ex.Message, StringComparison.Ordinal);
        Assert.Contains("covariate beta", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CombinedMultiplierJustUnderTheLimit_StillRuns()
    {
        var input = BaseInput(
            segments: [Segment(clients: 100, pool: 0)],
            parameters: [Param("seg-a", FlowKind.ChurnHazard, DistributionSpec.Point(0.001))],
            events:
            [
                new EventInput("big", 3, 5, [new EventRateMultiplier(null, FlowKind.ChurnHazard, 999.0)]),
            ],
            months: 12, paths: 20);

        var result = SimulationEngine.Run(input);
        Assert.Equal(12, result.HorizonMonths);
    }

    // ---------- events ----------

    [Fact]
    public void EventMultiplier_ChangesRatesOnlyInsideWindow()
    {
        var baseline = BaseInput(
            segments: [Segment(clients: 1000, pool: 0)],
            parameters: [Param("seg-a", FlowKind.ChurnHazard, DistributionSpec.Point(0.02))],
            months: 18, paths: 300, seed: 9);
        var shocked = baseline with
        {
            Events = [new EventInput("incident", 6, 11, [new EventRateMultiplier(null, FlowKind.ChurnHazard, 5.0)])],
        };

        var without = SimulationEngine.Run(baseline);
        var with_ = SimulationEngine.Run(shocked);

        // Before the window: identical draws -> identical trajectories.
        for (int t = 0; t < 6; t++)
        {
            Assert.Equal(without.Segments[0].Churned.Mean[t], with_.Segments[0].Churned.Mean[t]);
            Assert.Equal(without.Segments[0].Clients.Mean[t], with_.Segments[0].Clients.Mean[t]);
        }
        // Inside the window: clearly elevated churn.
        for (int t = 6; t <= 11; t++)
            Assert.True(with_.Segments[0].Churned.Mean[t] > without.Segments[0].Churned.Mean[t] * 2,
                $"month {t}: {with_.Segments[0].Churned.Mean[t]} vs {without.Segments[0].Churned.Mean[t]}");
    }

    [Fact]
    public void EventMultiplier_RespectsSegmentScope()
    {
        var segs = new[] { Segment("seg-a", clients: 1000, pool: 0), Segment("seg-b", clients: 1000, pool: 0) };
        var input = BaseInput(
            segments: segs,
            parameters:
            [
                Param("seg-a", FlowKind.ChurnHazard, DistributionSpec.Point(0.02)),
                Param("seg-b", FlowKind.ChurnHazard, DistributionSpec.Point(0.02)),
            ],
            events: [new EventInput("incident", 0, 11, [new EventRateMultiplier("seg-a", FlowKind.ChurnHazard, 5.0)])],
            months: 12, paths: 300, seed: 21);

        var result = SimulationEngine.Run(input);
        double churnA = 0, churnB = 0;
        for (int t = 0; t < 12; t++)
        {
            churnA += result.Segments[0].Churned.Mean[t];
            churnB += result.Segments[1].Churned.Mean[t];
        }
        Assert.True(churnA > churnB * 2, $"seg-a churn {churnA} should far exceed seg-b churn {churnB}");
    }

    // ---------- attractiveness ----------

    [Fact]
    public void AttractivenessMultiplier_RaisesWins()
    {
        EngineInput Input(double mult)
        {
            var attr = new List<AttractivenessEntry>();
            for (int t = 0; t < 24; t++) attr.Add(new AttractivenessEntry("seg-a", t, mult));
            return BaseInput(
                segments: [Segment(pool: 5000, clients: 0)],
                parameters:
                [
                    Param("seg-a", FlowKind.ConversionToMarket, DistributionSpec.Point(0.05)),
                    Param("seg-a", FlowKind.WinRate, DistributionSpec.Point(0.2)),
                ],
                attractiveness: attr,
                months: 24, paths: 300, seed: 13);
        }

        var low = SimulationEngine.Run(Input(1.0));
        var high = SimulationEngine.Run(Input(2.0));

        double winsLow = 0, winsHigh = 0;
        for (int t = 0; t < 24; t++)
        {
            winsLow += low.Segments[0].NewWins.Mean[t];
            winsHigh += high.Segments[0].NewWins.Mean[t];
        }
        Assert.True(winsHigh > winsLow * 1.5, $"expected {winsHigh} > 1.5 * {winsLow}");
    }

    // ---------- cross-sell ----------

    [Fact]
    public void CrossSell_AddsModuleArrAndBookings()
    {
        var modules = new[] { new ModulePenetration("mod-x", 0.2, 200) };
        var input = BaseInput(
            segments: [Segment(clients: 500, pool: 0, modules: modules)],
            parameters:
            [
                Param("seg-a", FlowKind.CrossSellRate, DistributionSpec.Point(0.10), moduleId: "mod-x"),
            ],
            months: 24, paths: 200, seed: 17);

        var result = SimulationEngine.Run(input);
        var seg = result.Segments[0];

        // ARR should grow over time from module adoption (base clients static, no churn).
        Assert.True(seg.RecognizedArr.Mean[23] > seg.RecognizedArr.Mean[0],
            $"{seg.RecognizedArr.Mean[23]} should exceed {seg.RecognizedArr.Mean[0]}");
        // Bookings come only from module adds here (no wins).
        double totalBookings = 0;
        for (int t = 0; t < 24; t++) totalBookings += seg.Bookings.Mean[t];
        Assert.True(totalBookings > 0);
        // Module holders can never exceed clients: ARR capped at full penetration run-rate.
        double maxArr = 12 * (500 * 1000 + 500 * 200);
        for (int t = 0; t < 24; t++) Assert.True(seg.RecognizedArr.P90[t] <= maxArr + 1e-9);
    }

    // ---------- conservation / sanity ----------

    [Fact]
    public void ExtremeRates_NeverProduceNegativeStocks()
    {
        var input = BaseInput(
            segments: [Segment(pool: 50, clients: 30)],
            parameters:
            [
                Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(100)),
                Param("seg-a", FlowKind.ConversionToMarket, DistributionSpec.Point(3.0)),
                Param("seg-a", FlowKind.WinRate, DistributionSpec.Point(10.0)),
                Param("seg-a", FlowKind.ChurnHazard, DistributionSpec.Point(5.0)),
            ],
            months: 24, paths: 200, seed: 5);

        var result = SimulationEngine.Run(input);
        var seg = result.Segments[0];
        for (int t = 0; t < 24; t++)
        {
            Assert.True(seg.Clients.P10[t] >= 0);
            Assert.True(seg.ProspectPool.P10[t] >= 0);
            Assert.True(seg.NewWins.P10[t] >= 0);
            Assert.True(seg.Churned.P10[t] >= 0);
        }
    }

    [Fact]
    public void Quantiles_AreOrderedForAllMetrics()
    {
        var input = BaseInput(parameters:
        [
            Param("seg-a", FlowKind.NewFirmArrival, new DistributionSpec(DistributionType.LogNormal, 1.5, 0.5)),
            Param("seg-a", FlowKind.ConversionToMarket, new DistributionSpec(DistributionType.Beta, 2, 30)),
            Param("seg-a", FlowKind.WinRate, new DistributionSpec(DistributionType.Beta, 3, 7)),
            Param("seg-a", FlowKind.ChurnHazard, new DistributionSpec(DistributionType.Beta, 1, 50)),
        ], months: 36, paths: 500);

        var result = SimulationEngine.Run(input);
        foreach (var seg in result.Segments.Concat([result.Total]))
        {
            foreach (var traj in new[]
                     {
                         seg.Clients, seg.ProspectPool, seg.NewWins, seg.Churned,
                         seg.Bookings, seg.RecognizedArr, seg.ContributionMargin,
                     })
            {
                for (int t = 0; t < 36; t++)
                {
                    Assert.True(traj.P10[t] <= traj.P50[t], $"p10 {traj.P10[t]} > p50 {traj.P50[t]} at {t}");
                    Assert.True(traj.P50[t] <= traj.P90[t], $"p50 {traj.P50[t]} > p90 {traj.P90[t]} at {t}");
                }
            }
        }
        Assert.True(result.Npv.P10 <= result.Npv.P50);
        Assert.True(result.Npv.P50 <= result.Npv.P90);
    }

    [Fact]
    public void Total_IsSumOfSegmentMeans()
    {
        var segs = new[] { Segment("seg-a"), Segment("seg-b", pool: 300, clients: 50) };
        var input = BaseInput(
            segments: segs,
            parameters:
            [
                Param("seg-a", FlowKind.ConversionToMarket, DistributionSpec.Point(0.05)),
                Param("seg-a", FlowKind.WinRate, DistributionSpec.Point(0.3)),
                Param("seg-b", FlowKind.ConversionToMarket, DistributionSpec.Point(0.02)),
                Param("seg-b", FlowKind.WinRate, DistributionSpec.Point(0.2)),
            ],
            months: 12, paths: 100);

        var result = SimulationEngine.Run(input);
        for (int t = 0; t < 12; t++)
        {
            double sum = result.Segments[0].Clients.Mean[t] + result.Segments[1].Clients.Mean[t];
            Assert.Equal(sum, result.Total.Clients.Mean[t], precision: 6);
        }
    }

    [Fact]
    public void Npv_PositiveMarginsYieldPositiveNpvWithTerminalValue()
    {
        var input = BaseInput(
            segments: [Segment(clients: 200, pool: 0, costs: new CostRates(50, 1000, 10, 500))],
            parameters: [],
            months: 12, paths: 10);

        var result = SimulationEngine.Run(input);
        // Static 200 clients * 1000 arpu - costs => steady positive margin; NPV strongly positive.
        Assert.True(result.Npv.Mean > 0);
        Assert.Equal(result.Npv.P10, result.Npv.P90); // fully deterministic input -> zero-width distribution
        Assert.Equal(10, result.NpvPerPath.Length);
    }

    // ---------- validation ----------

    [Fact]
    public void InvalidInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => SimulationEngine.Run(BaseInput(segments: [])));
        Assert.Throws<ArgumentException>(() => SimulationEngine.Run(BaseInput(months: 0)));
        Assert.Throws<ArgumentException>(() => SimulationEngine.Run(BaseInput(paths: 0)));
        // Covariate with both path and gbm specs
        Assert.Throws<ArgumentException>(() => SimulationEngine.Run(BaseInput(
            covariates: [new CovariateInput("bad", new double[24], new GbmSpec(1, 0, 0.1))])));
        // Parameter referencing unknown covariate
        Assert.Throws<ArgumentException>(() => SimulationEngine.Run(BaseInput(
            parameters: [Param("seg-a", FlowKind.NewFirmArrival, DistributionSpec.Point(1),
                cov: new CovariateLink("missing", 1.0))])));
        // ModuleId on a non-cross-sell flow
        Assert.Throws<ArgumentException>(() => SimulationEngine.Run(BaseInput(
            parameters: [Param("seg-a", FlowKind.WinRate, DistributionSpec.Point(0.2), moduleId: "mod-x")])));
    }

    // ---------- performance ----------

    [Fact]
    public void Performance_FiveSegments1000Paths36Months_UnderTwoSeconds()
    {
        var segments = new SegmentInput[5];
        var parameters = new List<ParameterSpec>();
        for (int i = 0; i < 5; i++)
        {
            string id = $"seg-{i}";
            segments[i] = Segment(id, pool: 1000, clients: 200,
                modules: [new ModulePenetration("mod-x", 0.3, 250), new ModulePenetration("mod-y", 0.1, 400)]);
            parameters.Add(Param(id, FlowKind.NewFirmArrival, new DistributionSpec(DistributionType.LogNormal, 1.5, 0.4),
                cov: new CovariateLink("index", 0.2)));
            parameters.Add(Param(id, FlowKind.ConversionToMarket, new DistributionSpec(DistributionType.Beta, 2, 30)));
            parameters.Add(Param(id, FlowKind.WinRate, new DistributionSpec(DistributionType.Beta, 3, 7)));
            parameters.Add(Param(id, FlowKind.ChurnHazard, new DistributionSpec(DistributionType.Beta, 1, 60)));
            parameters.Add(Param(id, FlowKind.CrossSellRate, new DistributionSpec(DistributionType.Beta, 1, 20), moduleId: "mod-x"));
            parameters.Add(Param(id, FlowKind.CrossSellRate, new DistributionSpec(DistributionType.Beta, 1, 40), moduleId: "mod-y"));
        }

        var input = new EngineInput(
            segments,
            parameters,
            [new CovariateInput("index", Gbm: new GbmSpec(1.0, 0.005, 0.04))],
            [new EventInput("shock", 12, 17, [new EventRateMultiplier(null, FlowKind.ChurnHazard, 2.0)])],
            [new AttractivenessEntry("seg-0", 18, 1.5)],
            HorizonMonths: 36,
            Paths: 1000,
            Seed: 99);

        // Warm-up (JIT), then measure.
        SimulationEngine.Run(input);
        var sw = Stopwatch.StartNew();
        var result = SimulationEngine.Run(input);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds}ms");
        Assert.Equal(1000, result.Paths);
        Assert.Equal(5, result.Segments.Count);
    }
}
