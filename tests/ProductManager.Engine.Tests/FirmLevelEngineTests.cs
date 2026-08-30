using System.Diagnostics;
using System.Text.Json;
using ProductManager.Engine;

namespace ProductManager.Engine.Tests;

public class FirmLevelEngineTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = false };

    // ---------- builders ----------

    private static IReadOnlyList<ProcessInput> DefaultProcesses() =>
    [
        new("p1", "c1"), new("p2", "c1"), new("p3", "c2"), new("p4", "c2"),
    ];

    private static IReadOnlyList<ProcessNeed> DefaultNeeds() =>
    [
        new("p1", DistributionSpec.Point(1)),
        new("p2", DistributionSpec.Point(1)),
        new("p3", DistributionSpec.Point(1)),
        new("p4", DistributionSpec.Point(1)),
    ];

    private static FirmLevelSegmentInput Seg(
        string id = "seg-a",
        double pool = 200,
        IReadOnlyList<InitialClientTarget>? targets = null,
        IReadOnlyList<ProcessNeed>? needs = null,
        DistributionSpec? priceSens = null,
        double consol = 0.0)
        => new(id, pool, targets ?? [], [], needs ?? DefaultNeeds(),
            priceSens ?? DistributionSpec.Point(1), consol, new CostRates(50, 1000, 10, 500));

    private static FirmLevelProductInput Product(
        string id,
        bool ours,
        double score = 0.7,
        double arpu = 100,
        double implFee = 1000,
        IReadOnlyList<CoverageCell>? coverage = null,
        IReadOnlyList<IntegrationEdge>? integrations = null,
        IReadOnlyList<BrandBias>? brand = null,
        string segId = "seg-a")
        => new(id, ours,
            coverage ??
            [
                new("p1", null, score), new("p2", null, score),
                new("p3", null, score), new("p4", null, score),
            ],
            [new ProductPrice(segId, arpu, implFee)],
            integrations ?? [],
            brand ?? []);

    private static FirmInput Prospect(string id, string seg = "seg-a") => new(id, seg, false, []);

    private static FirmInput Client(
        string id, string prod, string seg = "seg-a", double priceLevel = 1.0,
        IReadOnlyList<string>? procs = null)
        => new(id, seg, false,
            [new IncumbentRelationship(prod, procs ?? ["p1", "p2", "p3", "p4"], priceLevel)]);

    private static ChoiceParameters DefaultChoice(
        double switching = 2.0,
        double workaround = 1.0,
        double expansion = 0.0,
        IReadOnlyList<SalesCycleSpec>? cycles = null,
        double noDecision = -1.0,
        double integrationWeight = 1.0)
        => new(switching, workaround, expansion, cycles ?? [], noDecision, integrationWeight);

    private static FirmLevelInput Base(
        IReadOnlyList<FirmLevelSegmentInput>? segments = null,
        IReadOnlyList<ProcessInput>? processes = null,
        IReadOnlyList<FirmInput>? firms = null,
        IReadOnlyList<FirmLevelProductInput>? products = null,
        IReadOnlyList<ChannelInput>? channels = null,
        ChoiceParameters? choice = null,
        IReadOnlyList<OpportunityScoping>? scoping = null,
        IReadOnlyList<ParameterSpec>? parameters = null,
        IReadOnlyList<CovariateInput>? covariates = null,
        IReadOnlyList<EventInput>? events = null,
        int months = 24,
        int paths = 300,
        int seed = 42)
        => new(
            segments ?? [Seg()],
            processes ?? DefaultProcesses(),
            firms ?? [],
            products ?? [Product("us", ours: true, score: 0.8), Product("comp", ours: false, score: 0.6)],
            channels ?? [],
            choice ?? DefaultChoice(),
            scoping ?? [],
            parameters ?? [new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1))],
            covariates ?? [],
            events ?? [],
            months, paths, seed);

    private static SegmentFunnel Funnel(FirmLevelResult r, int seg = 0) => r.Diagnostics.FunnelBySegment[seg];

    // ---------- determinism ----------

    [Fact]
    public void SameSeed_ProducesByteIdenticalResults()
    {
        var input = Base(
            firms: Enumerable.Range(0, 40).Select(i => Client($"f-{i}", "comp")).ToArray(),
            segments: [Seg(pool: 120, priceSens: new DistributionSpec(DistributionType.Normal, 1.0, 0.3))],
            channels:
            [
                new ChannelInput("direct-sales", [new("seg-a", 1.0)], [new("seg-a", 0.7)], []),
                new ChannelInput("consultants", [new("seg-a", 0.8)], [new("seg-a", 0.3)],
                    [new ChannelProductBias("comp", 0.5)]),
            ],
            choice: DefaultChoice(cycles: [new SalesCycleSpec("seg-a", null, Math.Log(2), 0.5)], expansion: 1.0),
            scoping: [new OpportunityScoping("seg-a", 0.4, DistributionSpec.Point(1))],
            parameters:
            [
                new ParameterSpec("seg-a", FlowKind.NewFirmArrival, null, new DistributionSpec(DistributionType.LogNormal, 1.0, 0.3)),
                new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, new DistributionSpec(DistributionType.Beta, 2, 20),
                    new CovariateLink("index", 0.5)),
                new ParameterSpec("seg-a", FlowKind.ChurnHazard, null, new DistributionSpec(DistributionType.Beta, 1, 30)),
                new ParameterSpec("seg-a", FlowKind.CrossSellRate, null, DistributionSpec.Point(0.05)),
            ],
            covariates: [new CovariateInput("index", Gbm: new GbmSpec(1.0, 0.01, 0.05))],
            events: [new EventInput("shock", 6, 9, [new EventRateMultiplier(null, FlowKind.ChurnHazard, 3.0)])],
            months: 18, paths: 50);

        var a = FirmLevelEngine.Run(input);
        var b = FirmLevelEngine.Run(input);

        string jsonA = JsonSerializer.Serialize(a with { Result = a.Result with { ElapsedMs = 0 } }, JsonOptions);
        string jsonB = JsonSerializer.Serialize(b with { Result = b.Result with { ElapsedMs = 0 } }, JsonOptions);
        Assert.Equal(jsonA, jsonB);
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentResults()
    {
        var a = FirmLevelEngine.Run(Base(seed: 1));
        var b = FirmLevelEngine.Run(Base(seed: 2));
        Assert.NotEqual(a.Result.Npv.Mean, b.Result.Npv.Mean);
    }

    [Fact]
    public void IncreasingPathCount_LeavesEarlierPathsUnchanged()
    {
        var small = FirmLevelEngine.Run(Base(paths: 40));
        var large = FirmLevelEngine.Run(Base(paths: 90));
        for (int p = 0; p < 40; p++)
            Assert.Equal(small.Result.NpvPerPath[p], large.Result.NpvPerPath[p]);
    }

    // ---------- funnel conservation ----------

    [Fact]
    public void Funnel_OpenedEqualsWonPlusLostPlusNoDecisionPlusStillOpen()
    {
        var input = Base(
            firms: Enumerable.Range(0, 60).Select(i => Client($"f-{i}", "comp")).ToArray(),
            segments: [Seg(pool: 150)],
            choice: DefaultChoice(cycles: [new SalesCycleSpec("seg-a", null, Math.Log(4), 0.6)]),
            parameters:
            [
                new ParameterSpec("seg-a", FlowKind.NewFirmArrival, null, DistributionSpec.Point(3)),
                new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.08)),
                new ParameterSpec("seg-a", FlowKind.ChurnHazard, null, DistributionSpec.Point(0.05)),
                new ParameterSpec("seg-a", FlowKind.CrossSellRate, null, DistributionSpec.Point(0.05)),
            ],
            months: 12, paths: 200);

        var funnel = Funnel(FirmLevelEngine.Run(input));
        Assert.True(funnel.Opened > 0);
        Assert.True(funnel.StillOpen > 0, "expected some opportunities still open at horizon end");
        Assert.Equal(funnel.Opened, funnel.Won + funnel.Lost + funnel.NoDecision + funnel.StillOpen, precision: 6);
        Assert.True(funnel.Qualified <= funnel.Opened);
    }

    // ---------- logit sanity ----------

    [Fact]
    public void BetterCoverage_YieldsHigherWinShare()
    {
        double WonShare(double ourScore)
        {
            var input = Base(products:
            [
                Product("us", ours: true, score: ourScore),
                Product("comp", ours: false, score: 0.7),
            ]);
            var f = Funnel(FirmLevelEngine.Run(input));
            return f.Won / Math.Max(1e-9, f.Won + f.Lost);
        }

        double low = WonShare(0.4);
        double high = WonShare(0.9);
        Assert.True(high > low + 0.05, $"expected win share {high} to clearly exceed {low}");
    }

    [Fact]
    public void PriceSensitivity_MonotonicallyReducesExpensiveProductWins()
    {
        double Won(double sensitivity)
        {
            var input = Base(
                segments: [Seg(priceSens: DistributionSpec.Point(sensitivity))],
                products:
                [
                    Product("us", ours: true, score: 0.7, arpu: 400),   // expensive
                    Product("comp", ours: false, score: 0.7, arpu: 100), // cheap
                ]);
            return Funnel(FirmLevelEngine.Run(input)).Won;
        }

        double atZero = Won(0.0);
        double atMid = Won(3.0);
        double atHigh = Won(8.0);
        Assert.True(atMid < atZero, $"expected {atMid} < {atZero}");
        Assert.True(atHigh < atMid, $"expected {atHigh} < {atMid}");
    }

    // ---------- switching costs ----------

    [Fact]
    public void SwitchingCost_RaisesIncumbentRetention()
    {
        double OurWins(double switchingCost)
        {
            var input = Base(
                segments: [Seg(pool: 0)],
                firms: Enumerable.Range(0, 150).Select(i => Client($"f-{i}", "comp")).ToArray(),
                products:
                [
                    Product("us", ours: true, score: 0.9),
                    Product("comp", ours: false, score: 0.5),
                ],
                choice: DefaultChoice(switching: switchingCost),
                parameters: [new ParameterSpec("seg-a", FlowKind.ChurnHazard, null, DistributionSpec.Point(0.1))]);
            return Funnel(FirmLevelEngine.Run(input)).Won;
        }

        double free = OurWins(0.0);
        double locked = OurWins(10.0);
        Assert.True(free > 0, "with no switching cost we should win some re-evaluations");
        Assert.True(locked < free * 0.5,
            $"high switching cost should at least halve our re-evaluation wins: {locked} vs {free}");
    }

    // ---------- integrations ----------

    [Fact]
    public void IntegrationQuality_RaisesBestOfBreedWinShareForNarrowScopes()
    {
        double OurWins(double quality)
        {
            // Every firm runs "erp" on cluster c1; cross-sell probes open narrow scopes over the
            // uncovered cluster c2. Our specialist covers only c2; the rival suite covers both.
            var erpCoverage = new CoverageCell[] { new("p1", null, 0.9), new("p2", null, 0.9) };
            var usCoverage = new CoverageCell[] { new("p3", null, 0.7), new("p4", null, 0.7) };
            var input = Base(
                segments: [Seg(pool: 0)],
                firms: Enumerable.Range(0, 200)
                    .Select(i => Client($"f-{i}", "erp", procs: ["p1", "p2"])).ToArray(),
                products:
                [
                    Product("us", ours: true, coverage: usCoverage,
                        integrations: quality > 0 ? [new IntegrationEdge("erp", quality)] : []),
                    Product("suite", ours: false, score: 0.6),
                    Product("erp", ours: false, coverage: erpCoverage),
                ],
                parameters: [new ParameterSpec("seg-a", FlowKind.CrossSellRate, null, DistributionSpec.Point(0.1))]);
            return Funnel(FirmLevelEngine.Run(input)).Won;
        }

        double without = OurWins(0.0);
        double with_ = OurWins(2.0);
        Assert.True(with_ > without * 1.3,
            $"integration quality should clearly raise our cross-sell wins: {with_} vs {without}");
    }

    // ---------- hard vs soft constraints ----------

    [Fact]
    public void HardConstraint_DisqualifiesProduct_ItNeverWins()
    {
        IReadOnlyList<ProcessNeed> needs =
        [
            new("p1", DistributionSpec.Point(1), IsHard: true, RequiredCoverage: 0.8),
            new("p2", DistributionSpec.Point(1)),
            new("p3", DistributionSpec.Point(1)),
            new("p4", DistributionSpec.Point(1)),
        ];
        // Our product fails the hard requirement on p1 despite a huge brand bonus.
        var input = Base(
            segments: [Seg(needs: needs)],
            products:
            [
                Product("us", ours: true,
                    coverage: [new("p1", null, 0.5), new("p2", null, 0.9), new("p3", null, 0.9), new("p4", null, 0.9)],
                    brand: [new BrandBias("seg-a", 4.0)]),
                Product("comp", ours: false, score: 0.9),
            ]);

        var funnel = Funnel(FirmLevelEngine.Run(input));
        Assert.True(funnel.Opened > 0);
        Assert.Equal(0.0, funnel.Won);       // disqualified product never wins
        Assert.Equal(0.0, funnel.Qualified); // and never even qualifies (broad scopes always hit p1)
    }

    [Fact]
    public void SoftConstraint_AddsWorkaroundCostInsteadOfDisqualifying()
    {
        double Won(IReadOnlyList<ProcessNeed> needs)
        {
            var input = Base(
                segments: [Seg(needs: needs)],
                products:
                [
                    Product("us", ours: true,
                        coverage: [new("p1", null, 0.5), new("p2", null, 0.9), new("p3", null, 0.9), new("p4", null, 0.9)],
                        brand: [new BrandBias("seg-a", 2.0)]),
                    Product("comp", ours: false, score: 0.9),
                ]);
            return Funnel(FirmLevelEngine.Run(input)).Won;
        }

        IReadOnlyList<ProcessNeed> unconstrained = DefaultNeeds();
        IReadOnlyList<ProcessNeed> soft =
        [
            new("p1", DistributionSpec.Point(1), IsHard: false, RequiredCoverage: 0.8, WorkaroundCost: 3.0),
            new("p2", DistributionSpec.Point(1)),
            new("p3", DistributionSpec.Point(1)),
            new("p4", DistributionSpec.Point(1)),
        ];

        double baseline = Won(unconstrained);
        double penalized = Won(soft);
        Assert.True(penalized > 0, "a soft constraint must not disqualify: we should still win sometimes");
        Assert.True(penalized < baseline * 0.7,
            $"the workaround cost should clearly depress our wins: {penalized} vs {baseline}");
    }

    // ---------- channels ----------

    [Fact]
    public void ChannelBias_ShiftsShare_AndChannelFunnelIsReported()
    {
        FirmLevelResult Run(double compBias)
        {
            var input = Base(channels:
            [
                new ChannelInput("consultants", [new("seg-a", 1.0)], [new("seg-a", 1.0)],
                    compBias == 0 ? [] : [new ChannelProductBias("comp", compBias)]),
            ]);
            return FirmLevelEngine.Run(input);
        }

        var unbiased = Run(0.0);
        var biased = Run(2.5);

        Assert.True(Funnel(biased).Won < Funnel(unbiased).Won * 0.7,
            $"a channel biased toward the competitor should depress our wins: {Funnel(biased).Won} vs {Funnel(unbiased).Won}");

        var chan = biased.Diagnostics.FunnelByChannel.Single(c => c.ChannelId == "consultants");
        Assert.True(chan.Opened > 0);
        Assert.Equal(Funnel(biased).Opened, chan.Opened, precision: 6); // single channel originates everything
    }

    // ---------- sales cycle ----------

    [Fact]
    public void SalesCycleDelay_ShiftsBookingsLaterThanInstantDecisions()
    {
        double EarlyBookings(IReadOnlyList<SalesCycleSpec> cycles, out FirmLevelResult result)
        {
            var input = Base(choice: DefaultChoice(cycles: cycles), months: 24, paths: 200);
            result = FirmLevelEngine.Run(input);
            double sum = 0;
            for (int t = 0; t < 4; t++) sum += result.Result.Segments[0].Bookings.Mean[t];
            return sum;
        }

        double instant = EarlyBookings([], out var instantResult);
        double delayed = EarlyBookings([new SalesCycleSpec("seg-a", null, Math.Log(6), 0.1)], out var delayedResult);

        Assert.True(instant > 0, "instant decisions should book revenue in the first months");
        Assert.True(delayed < instant * 0.2,
            $"a ~6 month sales cycle should push bookings out of the first 4 months: {delayed} vs {instant}");
        Assert.Equal(0.0, instantResult.Diagnostics.AvgSalesCycleMonths);
        Assert.InRange(delayedResult.Diagnostics.AvgSalesCycleMonths, 4.0, 8.0);
    }

    // ---------- expansion ----------

    [Fact]
    public void ScopeExpansion_IncreasesBookedArr_WhenCoverageAdvantageOnAdjacentCluster()
    {
        FirmLevelResult Run(double propensity)
        {
            var input = Base(
                products:
                [
                    Product("us", ours: true, score: 0.9),
                    Product("comp", ours: false, score: 0.3),
                ],
                choice: DefaultChoice(expansion: propensity),
                scoping: [new OpportunityScoping("seg-a", 0.0, DistributionSpec.Point(1))], // narrow: 1 cluster
                months: 24, paths: 200);
            return FirmLevelEngine.Run(input);
        }

        var without = Run(0.0);
        var with_ = Run(2.0); // advantage 0.6 x 2 => expansion probability clamps to 1

        Assert.Equal(0.0, without.Diagnostics.ExpansionCount);
        Assert.True(with_.Diagnostics.ExpansionCount > 0);
        double arrWithout = without.Result.Segments[0].RecognizedArr.Mean[23];
        double arrWith = with_.Result.Segments[0].RecognizedArr.Mean[23];
        Assert.True(arrWith > arrWithout * 1.3,
            $"expansion should clearly raise recognized ARR: {arrWith} vs {arrWithout}");
    }

    // ---------- synthetic fill ----------

    [Fact]
    public void SyntheticFirms_FillSegmentStocksTheRosterDoesNotCover()
    {
        var input = Base(
            segments:
            [
                Seg(pool: 50, targets: [new InitialClientTarget("comp", 10), new InitialClientTarget("us", 4)]),
            ],
            firms:
            [
                Prospect("obs-1"), Prospect("obs-2"), Prospect("obs-3"), Prospect("obs-4"), Prospect("obs-5"),
                Client("cl-1", "comp"), Client("cl-2", "comp"), Client("cl-3", "comp"),
            ],
            parameters: [], // all flows off: stocks must be static
            months: 6, paths: 20);

        var result = FirmLevelEngine.Run(input);

        // 45 prospects + 7 comp clients + 4 our clients synthesized.
        Assert.Equal(45 + 7 + 4, result.Diagnostics.SyntheticFirmsCreated);
        var seg = result.Result.Segments[0];
        for (int t = 0; t < 6; t++)
        {
            Assert.Equal(50, seg.ProspectPool.Mean[t]);
            Assert.Equal(4, seg.Clients.Mean[t]); // our clients only
            // 4 clients x 4 covered processes x 100 arpu x 12.
            Assert.Equal(4 * 4 * 100 * 12, seg.RecognizedArr.Mean[t]);
            Assert.Equal(0, seg.NewWins.Mean[t]);
            Assert.Equal(0, seg.Churned.Mean[t]);
        }
    }

    // ---------- output shape ----------

    [Fact]
    public void TrajectoryQuantiles_AreOrdered_AndTotalSumsSegments()
    {
        var input = Base(
            segments: [Seg(pool: 150, priceSens: new DistributionSpec(DistributionType.Normal, 1.0, 0.5))],
            firms: Enumerable.Range(0, 30).Select(i => Client($"f-{i}", "comp")).ToArray(),
            parameters:
            [
                new ParameterSpec("seg-a", FlowKind.NewFirmArrival, null, new DistributionSpec(DistributionType.LogNormal, 1.0, 0.4)),
                new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, new DistributionSpec(DistributionType.Beta, 2, 20)),
                new ParameterSpec("seg-a", FlowKind.ChurnHazard, null, new DistributionSpec(DistributionType.Beta, 1, 40)),
                new ParameterSpec("seg-a", FlowKind.CrossSellRate, null, DistributionSpec.Point(0.03)),
            ],
            months: 18, paths: 300);

        var result = FirmLevelEngine.Run(input).Result;
        foreach (var seg in result.Segments.Concat([result.Total]))
        {
            foreach (var traj in new[]
                     {
                         seg.Clients, seg.ProspectPool, seg.NewWins, seg.Churned,
                         seg.Bookings, seg.RecognizedArr, seg.ContributionMargin,
                     })
            {
                for (int t = 0; t < 18; t++)
                {
                    Assert.True(traj.P10[t] <= traj.P50[t], $"p10 {traj.P10[t]} > p50 {traj.P50[t]} at {t}");
                    Assert.True(traj.P50[t] <= traj.P90[t], $"p50 {traj.P50[t]} > p90 {traj.P90[t]} at {t}");
                }
            }
        }
        Assert.True(result.Npv.P10 <= result.Npv.P50);
        Assert.True(result.Npv.P50 <= result.Npv.P90);
        for (int t = 0; t < 18; t++)
            Assert.Equal(result.Segments[0].Clients.Mean[t], result.Total.Clients.Mean[t], precision: 9);
    }

    // ---------- guard rails / validation ----------

    [Fact]
    public void ReferentialIntegrity_IsValidatedWithActionableErrors()
    {
        // Firm in unknown segment.
        var ex = Assert.Throws<ArgumentException>(() => FirmLevelEngine.Run(
            Base(firms: [Prospect("f-1", "no-such-seg")])));
        Assert.Contains("f-1", ex.Message);
        Assert.Contains("no-such-seg", ex.Message);

        // Relationship with unknown product.
        ex = Assert.Throws<ArgumentException>(() => FirmLevelEngine.Run(
            Base(firms: [Client("f-1", "ghost-product")])));
        Assert.Contains("ghost-product", ex.Message);

        // Coverage for unknown process.
        ex = Assert.Throws<ArgumentException>(() => FirmLevelEngine.Run(Base(products:
        [
            Product("us", ours: true, coverage: [new("nope", null, 0.5)]),
            Product("comp", ours: false),
        ])));
        Assert.Contains("nope", ex.Message);

        // Channel bias for unknown product.
        ex = Assert.Throws<ArgumentException>(() => FirmLevelEngine.Run(Base(channels:
        [
            new ChannelInput("ch", [new("seg-a", 1.0)], [new("seg-a", 1.0)],
                [new ChannelProductBias("ghost", 1.0)]),
        ])));
        Assert.Contains("ghost", ex.Message);

        // Duplicate firm ids.
        ex = Assert.Throws<ArgumentException>(() => FirmLevelEngine.Run(
            Base(firms: [Prospect("dup"), Prospect("dup")])));
        Assert.Contains("dup", ex.Message);

        // Too many processes for the 64-bit scope masks.
        var manyProcs = Enumerable.Range(0, 65).Select(i => new ProcessInput($"p{i}")).ToArray();
        ex = Assert.Throws<ArgumentException>(() => FirmLevelEngine.Run(Base(
            processes: manyProcs,
            products: [Product("us", true, coverage: [new("p0", null, 0.5)]), Product("comp", false, coverage: [new("p0", null, 0.5)])],
            segments: [Seg(needs: [new ProcessNeed("p0", DistributionSpec.Point(1))])])));
        Assert.Contains("64", ex.Message);

        // Unknown covariate link.
        ex = Assert.Throws<ArgumentException>(() => FirmLevelEngine.Run(Base(parameters:
        [
            new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1),
                new CovariateLink("missing", 0.5)),
        ])));
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void RunawayFlowMultiplier_FailsFastLikeV0()
    {
        var input = Base(
            events: [new EventInput("absurd", 2, 4, [new EventRateMultiplier(null, FlowKind.ConversionToMarket, 5000.0)])],
            months: 12, paths: 5);
        var ex = Assert.Throws<ArgumentException>(() => FirmLevelEngine.Run(input));
        Assert.Contains("seg-a", ex.Message);
        Assert.Contains("ConversionToMarket", ex.Message);
        Assert.Contains("1000x limit", ex.Message);
    }

    // ---------- implicit direct channel ----------

    private static double ChannelOpened(FirmLevelResult r, string channelId)
        => r.Diagnostics.FunnelByChannel.Single(c => c.ChannelId == channelId).Opened;

    [Fact]
    public void ChannelOrigination_ResidualOriginatesDirect_InProportion()
    {
        // reach x originationShare: 0.7 x 0.5 = 0.35 and 0.4 x 0.3 = 0.12. The weights must NOT
        // be renormalized across the listed channels: the residual 0.53 originates direct.
        var input = Base(
            channels:
            [
                new ChannelInput("ch-big", [new("seg-a", 0.7)], [new("seg-a", 0.5)], []),
                new ChannelInput("ch-small", [new("seg-a", 0.4)], [new("seg-a", 0.3)], []),
            ],
            months: 24, paths: 300);

        var r = FirmLevelEngine.Run(input);
        double total = Funnel(r).Opened;
        Assert.True(total > 50, $"need a meaningful sample of opportunities, got {total}");

        double big = ChannelOpened(r, "ch-big");
        double small = ChannelOpened(r, "ch-small");
        double direct = ChannelOpened(r, "direct");
        Assert.Equal(total, big + small + direct, precision: 6);
        Assert.InRange(big / total, 0.32, 0.38);
        Assert.InRange(small / total, 0.09, 0.15);
        Assert.InRange(direct / total, 0.50, 0.56);
    }

    [Fact]
    public void ChannelOrigination_WeightsAboveOne_NormalizeDownProportionally_NoDirect()
    {
        // Weights 0.8 and 0.6 sum to 1.4 => normalized to 4/7 and 3/7, zero direct.
        var input = Base(
            channels:
            [
                new ChannelInput("ch-a", [new("seg-a", 0.8)], [new("seg-a", 1.0)], []),
                new ChannelInput("ch-b", [new("seg-a", 1.0)], [new("seg-a", 0.6)], []),
            ],
            months: 24, paths: 300);

        var r = FirmLevelEngine.Run(input);
        double total = Funnel(r).Opened;
        double a = ChannelOpened(r, "ch-a");
        double b = ChannelOpened(r, "ch-b");
        Assert.Equal(0.0, ChannelOpened(r, "direct"));
        Assert.Equal(total, a + b, precision: 6);
        Assert.InRange(a / total, 4.0 / 7.0 - 0.03, 4.0 / 7.0 + 0.03);
        Assert.InRange(b / total, 3.0 / 7.0 - 0.03, 3.0 / 7.0 + 0.03);
    }

    [Fact]
    public void ChannelOrigination_WeightsSummingToOne_LeaveZeroDirect()
    {
        var input = Base(
            channels:
            [
                new ChannelInput("ch-a", [new("seg-a", 1.0)], [new("seg-a", 0.5)], []),
                new ChannelInput("ch-b", [new("seg-a", 1.0)], [new("seg-a", 0.5)], []),
            ],
            months: 24, paths: 300);

        var r = FirmLevelEngine.Run(input);
        Assert.Equal(0.0, ChannelOpened(r, "direct"));
        Assert.Equal(Funnel(r).Opened, ChannelOpened(r, "ch-a") + ChannelOpened(r, "ch-b"), precision: 6);
    }

    // ---------- segment-scoped coverage ----------

    private static FirmLevelProductInput TwoSegProduct(
        string id, bool ours, IReadOnlyList<CoverageCell> coverage,
        IReadOnlyList<CoverageStep>? steps = null)
        => new(id, ours, coverage,
            [new ProductPrice("seg-a", 100, 1000), new ProductPrice("seg-b", 100, 1000)],
            [], [], steps);

    private static IReadOnlyList<CoverageCell> GenericCells(double score) =>
        [new("p1", null, score), new("p2", null, score), new("p3", null, score), new("p4", null, score)];

    private static FirmLevelInput TwoSegInput(
        IReadOnlyList<CoverageCell> ourCells, int seed = 42)
        => Base(
            segments: [Seg("seg-a"), Seg("seg-b")],
            products: [TwoSegProduct("us", true, ourCells), TwoSegProduct("comp", false, GenericCells(0.6))],
            parameters:
            [
                new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1)),
                new ParameterSpec("seg-b", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1)),
            ],
            months: 24, paths: 300, seed: seed);

    [Fact]
    public void SegmentScopedCoverage_RaisesOnlyItsSegmentsWinRate()
    {
        // Generic 0.4 everywhere; a Gold (0.95) row scoped to seg-a must raise seg-a's win rate
        // only — seg-b keeps resolving the generic 0.4 cells.
        var scopedGold = new List<CoverageCell>(GenericCells(0.4))
        {
            new("p1", null, 0.95, SegmentId: "seg-a"),
            new("p2", null, 0.95, SegmentId: "seg-a"),
            new("p3", null, 0.95, SegmentId: "seg-a"),
            new("p4", null, 0.95, SegmentId: "seg-a"),
        };

        var scoped = FirmLevelEngine.Run(TwoSegInput(scopedGold));
        var baseline = FirmLevelEngine.Run(TwoSegInput(GenericCells(0.4)));

        double wrA = scoped.Diagnostics.FunnelBySegment[0].WinRate;
        double wrB = scoped.Diagnostics.FunnelBySegment[1].WinRate;
        double wrAbase = baseline.Diagnostics.FunnelBySegment[0].WinRate;
        double wrBbase = baseline.Diagnostics.FunnelBySegment[1].WinRate;

        Assert.True(wrA > wrB + 0.10,
            $"the scoped Gold row must lift seg-a clearly above seg-b: {wrA} vs {wrB}");
        Assert.True(wrA > wrAbase + 0.10,
            $"the scoped Gold row must lift seg-a above its unscoped baseline: {wrA} vs {wrAbase}");
        Assert.True(Math.Abs(wrB - wrBbase) < 0.04,
            $"a seg-a-scoped cell must NOT move seg-b's win rate: {wrB} vs baseline {wrBbase}");
    }

    [Fact]
    public void SegmentScopedCoverage_WithoutGenericFallback_MeansNoCoverageInOtherSegments()
    {
        // Our product's coverage exists ONLY as seg-a-scoped cells: in seg-b there is no generic
        // cell to fall back to, so we must never qualify (let alone win) there.
        IReadOnlyList<CoverageCell> onlySegA =
        [
            new("p1", null, 0.9, SegmentId: "seg-a"),
            new("p2", null, 0.9, SegmentId: "seg-a"),
            new("p3", null, 0.9, SegmentId: "seg-a"),
            new("p4", null, 0.9, SegmentId: "seg-a"),
        ];

        var r = FirmLevelEngine.Run(TwoSegInput(onlySegA));
        var segA = r.Diagnostics.FunnelBySegment[0];
        var segB = r.Diagnostics.FunnelBySegment[1];

        Assert.True(segA.Won > 0, "we should win in the segment our scoped cells target");
        Assert.True(segB.Opened > 0);
        Assert.Equal(0.0, segB.Qualified);
        Assert.Equal(0.0, segB.Won);
    }

    // ---------- time-phased coverage (CoverageSteps) ----------

    [Fact]
    public void CoverageStep_IsEvaluatedAtOpportunityOpenMonth_IncludingHardConstraints()
    {
        // Every process carries a hard 0.8 requirement. We sit at 0.5 until a step to 0.9 at
        // month 12, so we are hard-disqualified for every opportunity OPENED before month 12.
        // The ~3-month sales cycle means decisions for post-step opens land from ~month 14 on;
        // if coverage were (wrongly) evaluated at the decision month, opens from months 9-11
        // would already produce wins in months 12-13.
        IReadOnlyList<ProcessNeed> hardNeeds =
        [
            new("p1", DistributionSpec.Point(1), IsHard: true, RequiredCoverage: 0.8),
            new("p2", DistributionSpec.Point(1), IsHard: true, RequiredCoverage: 0.8),
            new("p3", DistributionSpec.Point(1), IsHard: true, RequiredCoverage: 0.8),
            new("p4", DistributionSpec.Point(1), IsHard: true, RequiredCoverage: 0.8),
        ];
        IReadOnlyList<CoverageStep> steps =
        [
            new("p1", null, null, 12, 0.9), new("p2", null, null, 12, 0.9),
            new("p3", null, null, 12, 0.9), new("p4", null, null, 12, 0.9),
        ];
        var input = Base(
            segments: [Seg(pool: 300, needs: hardNeeds)],
            products:
            [
                Product("us", ours: true, score: 0.5) with { CoverageSteps = steps },
                Product("comp", ours: false, score: 0.9),
            ],
            choice: DefaultChoice(cycles: [new SalesCycleSpec("seg-a", null, Math.Log(3), 0.05)]),
            months: 24, paths: 200);

        var r = FirmLevelEngine.Run(input);
        var wins = r.Result.Segments[0].NewWins.Mean;
        for (int t = 0; t <= 13; t++)
            Assert.True(wins[t] == 0.0,
                $"no opportunity opened before the month-12 step may produce a win, got {wins[t]} at month {t}");
        double lateWins = 0;
        for (int t = 14; t < 24; t++) lateWins += wins[t];
        Assert.True(lateWins > 0, "opportunities opened at or after the step must produce wins");
    }

    [Fact]
    public void CoverageStep_DelayingStepMonth_LowersEarlyWins_ThenConverges()
    {
        double[] WinsByMonth(int stepMonth)
        {
            IReadOnlyList<CoverageStep> steps =
            [
                new("p1", null, null, stepMonth, 0.9), new("p2", null, null, stepMonth, 0.9),
                new("p3", null, null, stepMonth, 0.9), new("p4", null, null, stepMonth, 0.9),
            ];
            var input = Base(
                segments: [Seg(pool: 400)],
                products:
                [
                    Product("us", ours: true, score: 0.35) with { CoverageSteps = steps },
                    Product("comp", ours: false, score: 0.6),
                ],
                parameters: [new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.08))],
                months: 36, paths: 300);
            return FirmLevelEngine.Run(input).Result.Segments[0].NewWins.Mean;
        }

        var early = WinsByMonth(6);
        var late = WinsByMonth(18);

        // Before either step the two runs are byte-identical (same seed, same coverage in force).
        for (int t = 0; t < 6; t++) Assert.Equal(late[t], early[t]);

        // Delaying the step costs wins in the in-between window...
        double earlyWindow = 0, lateWindow = 0;
        for (int t = 6; t < 18; t++) { earlyWindow += early[t]; lateWindow += late[t]; }
        Assert.True(earlyWindow > lateWindow * 1.3,
            $"the earlier step must win clearly more in months 6-17: {earlyWindow} vs {lateWindow}");

        // ...and converges once both have stepped: monthly win rates in the tail match closely.
        double earlyTail = 0, lateTail = 0;
        for (int t = 30; t < 36; t++) { earlyTail += early[t]; lateTail += late[t]; }
        Assert.True(lateTail > 0, "the delayed step must still be winning by the tail months");
        Assert.True(Math.Abs(earlyTail - lateTail) < 0.30 * Math.Max(earlyTail, lateTail),
            $"tail-month wins must converge after both runs have stepped: {earlyTail} vs {lateTail}");
    }

    [Fact]
    public void CoverageStep_PreservesDeterminism()
    {
        var input = Base(
            firms: Enumerable.Range(0, 30).Select(i => Client($"f-{i}", "comp")).ToArray(),
            products:
            [
                Product("us", ours: true, score: 0.5) with
                {
                    CoverageSteps = [new CoverageStep("p1", null, null, 4, 0.9), new CoverageStep("p3", null, null, 9, 0.8)],
                },
                Product("comp", ours: false, score: 0.6),
            ],
            choice: DefaultChoice(cycles: [new SalesCycleSpec("seg-a", null, Math.Log(2), 0.5)], expansion: 1.0),
            parameters:
            [
                new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.08)),
                new ParameterSpec("seg-a", FlowKind.ChurnHazard, null, DistributionSpec.Point(0.05)),
                new ParameterSpec("seg-a", FlowKind.CrossSellRate, null, DistributionSpec.Point(0.05)),
            ],
            months: 18, paths: 50);

        var a = FirmLevelEngine.Run(input);
        var b = FirmLevelEngine.Run(input);
        string jsonA = JsonSerializer.Serialize(a with { Result = a.Result with { ElapsedMs = 0 } }, JsonOptions);
        string jsonB = JsonSerializer.Serialize(b with { Result = b.Result with { ElapsedMs = 0 } }, JsonOptions);
        Assert.Equal(jsonA, jsonB);
    }

    // ---------- scoped base cells vs generic steps: max(base, step) resolution ----------

    private static string Serialized(FirmLevelResult r) =>
        JsonSerializer.Serialize(r with { Result = r.Result with { ElapsedMs = 0 } }, JsonOptions);

    [Fact]
    public void ScopedBaseCell_EqualToGeneric_WithGenericSteps_IsBitIdenticalToNoScopedCell()
    {
        // The verifier's control experiment as a regression test: a segment-scoped base cell with
        // the SAME score as the generic cell it shadows is a pure no-op — including the generic
        // cell's roadmap steps, which must keep applying in the scoped segment. Before the
        // max(base, step) fix the scoped cell cancelled the month-3/month-4 lifts in seg-a.
        IReadOnlyList<CoverageStep> genericSteps =
        [
            new("p1", null, null, 3, 0.7), new("p2", null, null, 3, 0.7),
            new("p3", null, null, 4, 0.7), new("p4", null, null, 4, 0.7),
        ];
        var scopedSameTier = new List<CoverageCell>(GenericCells(0.4))
        {
            new("p1", null, 0.4, SegmentId: "seg-a"),
            new("p2", null, 0.4, SegmentId: "seg-a"),
            new("p3", null, 0.4, SegmentId: "seg-a"),
            new("p4", null, 0.4, SegmentId: "seg-a"),
        };

        FirmLevelInput Input(IReadOnlyList<CoverageCell> ourCells) => Base(
            segments: [Seg("seg-a"), Seg("seg-b")],
            products:
            [
                TwoSegProduct("us", true, ourCells, genericSteps),
                TwoSegProduct("comp", false, GenericCells(0.6)),
            ],
            parameters:
            [
                new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1)),
                new ParameterSpec("seg-b", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1)),
            ],
            months: 24, paths: 300);

        var withScoped = FirmLevelEngine.Run(Input(scopedSameTier));
        var withoutScoped = FirmLevelEngine.Run(Input(GenericCells(0.4)));

        Assert.Equal(Serialized(withoutScoped), Serialized(withScoped));
    }

    [Fact]
    public void ScopedBaseCell_BelowGeneric_GenericStepStillLiftsTheScopedSegment()
    {
        // seg-a carries its own (worse) assessment; a generic step lifts every segment from
        // month 6. Max semantics: from month 6 seg-a resolves max(0.25 scoped base, 0.9 step).
        var scopedLower = new List<CoverageCell>(GenericCells(0.35))
        {
            new("p1", null, 0.25, SegmentId: "seg-a"),
            new("p2", null, 0.25, SegmentId: "seg-a"),
            new("p3", null, 0.25, SegmentId: "seg-a"),
            new("p4", null, 0.25, SegmentId: "seg-a"),
        };
        IReadOnlyList<CoverageStep> genericSteps =
        [
            new("p1", null, null, 6, 0.9), new("p2", null, null, 6, 0.9),
            new("p3", null, null, 6, 0.9), new("p4", null, null, 6, 0.9),
        ];

        FirmLevelInput Input(IReadOnlyList<CoverageStep>? steps) => Base(
            segments: [Seg("seg-a"), Seg("seg-b")],
            products:
            [
                TwoSegProduct("us", true, scopedLower, steps),
                TwoSegProduct("comp", false, GenericCells(0.6)),
            ],
            parameters:
            [
                new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1)),
                new ParameterSpec("seg-b", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1)),
            ],
            months: 24, paths: 300);

        var withStep = FirmLevelEngine.Run(Input(genericSteps));
        var noStep = FirmLevelEngine.Run(Input(null));

        // Before the step month nothing differs in seg-a...
        var stepWins = withStep.Result.Segments[0].NewWins.Mean;
        var noStepWins = noStep.Result.Segments[0].NewWins.Mean;
        for (int t = 0; t < 6; t++) Assert.Equal(noStepWins[t], stepWins[t]);

        // ...and after it the generic step must clearly lift seg-a despite its scoped base cell.
        double lifted = 0, flat = 0;
        for (int t = 8; t < 24; t++) { lifted += stepWins[t]; flat += noStepWins[t]; }
        Assert.True(lifted > flat * 1.5,
            $"the generic step must lift the segment carrying its own scoped base cell: {lifted} vs {flat}");
    }

    [Fact]
    public void ScopedStep_BeatsGenericStep_InItsSegment()
    {
        // Both a generic step (to 0.5) and a seg-a-scoped step (to 0.95) land at month 3. seg-a
        // must resolve the scoped step; seg-b the generic one — so seg-a pulls clearly ahead.
        IReadOnlyList<CoverageStep> steps =
        [
            new("p1", null, null, 3, 0.5), new("p2", null, null, 3, 0.5),
            new("p3", null, null, 3, 0.5), new("p4", null, null, 3, 0.5),
            new("p1", null, "seg-a", 3, 0.95), new("p2", null, "seg-a", 3, 0.95),
            new("p3", null, "seg-a", 3, 0.95), new("p4", null, "seg-a", 3, 0.95),
        ];
        IReadOnlyList<CoverageStep> genericOnly = [.. steps.Take(4)];

        FirmLevelInput Input(IReadOnlyList<CoverageStep> s) => Base(
            segments: [Seg("seg-a"), Seg("seg-b")],
            products:
            [
                TwoSegProduct("us", true, GenericCells(0.4), s),
                TwoSegProduct("comp", false, GenericCells(0.6)),
            ],
            parameters:
            [
                new ParameterSpec("seg-a", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1)),
                new ParameterSpec("seg-b", FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.1)),
            ],
            months: 24, paths: 300);

        var r = FirmLevelEngine.Run(Input(steps));
        var control = FirmLevelEngine.Run(Input(genericOnly));

        double wrA = r.Diagnostics.FunnelBySegment[0].WinRate;
        double wrB = r.Diagnostics.FunnelBySegment[1].WinRate;
        double wrAControl = control.Diagnostics.FunnelBySegment[0].WinRate;
        double wrBControl = control.Diagnostics.FunnelBySegment[1].WinRate;

        Assert.True(wrA > wrB + 0.10,
            $"the scoped step must lift seg-a clearly above seg-b: {wrA} vs {wrB}");
        Assert.True(wrA > wrAControl + 0.10,
            $"the scoped step must lift seg-a above the generic-step-only control: {wrA} vs {wrAControl}");
        Assert.True(Math.Abs(wrB - wrBControl) < 0.04,
            $"a seg-a-scoped step must NOT move seg-b: {wrB} vs control {wrBControl}");
    }

    [Fact]
    public void Step_BelowBase_NeverLowersCoverage()
    {
        // Steps are deltas/improvements: a step below the resolved base loses the max and the run
        // is bit-identical to one with no steps at all (the extra epoch boundary resolves to the
        // same effective coverage every month).
        IReadOnlyList<CoverageStep> lowerSteps =
        [
            new("p1", null, null, 6, 0.3), new("p2", null, null, 6, 0.3),
            new("p3", null, null, 6, 0.3), new("p4", null, null, 6, 0.3),
        ];

        FirmLevelInput Input(IReadOnlyList<CoverageStep>? steps) => Base(
            products:
            [
                Product("us", ours: true, score: 0.8) with { CoverageSteps = steps },
                Product("comp", ours: false, score: 0.6),
            ],
            months: 24, paths: 300);

        var withLowerStep = FirmLevelEngine.Run(Input(lowerSteps));
        var noSteps = FirmLevelEngine.Run(Input(null));

        Assert.Equal(Serialized(noSteps), Serialized(withLowerStep));
    }

    [Fact]
    public void HardConstraint_UsesEffectiveScore_AcrossStepBoundary_DespiteScopedBaseCell()
    {
        // Hard 0.8 requirement everywhere. Our base coverage is a seg-a-scoped 0.5 shadowing a
        // generic 0.5; the only route past the constraint is the generic step to 0.9 at month 12.
        // Qualification must use max(base, step): hard-disqualified for every opportunity opened
        // before month 12, winning afterwards — under the shadowing bug the scoped cell cancelled
        // the generic step and we never qualified at all.
        IReadOnlyList<ProcessNeed> hardNeeds =
        [
            new("p1", DistributionSpec.Point(1), IsHard: true, RequiredCoverage: 0.8),
            new("p2", DistributionSpec.Point(1), IsHard: true, RequiredCoverage: 0.8),
            new("p3", DistributionSpec.Point(1), IsHard: true, RequiredCoverage: 0.8),
            new("p4", DistributionSpec.Point(1), IsHard: true, RequiredCoverage: 0.8),
        ];
        var scopedShadow = new List<CoverageCell>(GenericCells(0.5))
        {
            new("p1", null, 0.5, SegmentId: "seg-a"),
            new("p2", null, 0.5, SegmentId: "seg-a"),
            new("p3", null, 0.5, SegmentId: "seg-a"),
            new("p4", null, 0.5, SegmentId: "seg-a"),
        };
        IReadOnlyList<CoverageStep> genericSteps =
        [
            new("p1", null, null, 12, 0.9), new("p2", null, null, 12, 0.9),
            new("p3", null, null, 12, 0.9), new("p4", null, null, 12, 0.9),
        ];
        var input = Base(
            segments: [Seg(pool: 300, needs: hardNeeds)],
            products:
            [
                Product("us", ours: true, coverage: scopedShadow) with { CoverageSteps = genericSteps },
                Product("comp", ours: false, score: 0.9),
            ],
            choice: DefaultChoice(cycles: [new SalesCycleSpec("seg-a", null, Math.Log(3), 0.05)]),
            months: 24, paths: 200);

        var r = FirmLevelEngine.Run(input);
        var wins = r.Result.Segments[0].NewWins.Mean;
        for (int t = 0; t <= 13; t++)
            Assert.True(wins[t] == 0.0,
                $"no opportunity opened before the month-12 step may produce a win, got {wins[t]} at month {t}");
        double lateWins = 0;
        for (int t = 14; t < 24; t++) lateWins += wins[t];
        Assert.True(lateWins > 0,
            "the generic step must clear the hard constraint in the segment with its own scoped base cell");
    }

    // ---------- performance ----------

    [Fact]
    public void Performance_5000Firms500Paths36Months_RunsInSeconds()
    {
        var processes = Enumerable.Range(1, 8)
            .Select(i => new ProcessInput($"p{i}", i <= 4 ? "core" : "adjacent")).ToArray();
        var needs = processes
            .Select(p => new ProcessNeed(p.Id, new DistributionSpec(DistributionType.Normal, 1.0, 0.2)))
            .ToArray();
        FirmLevelProductInput Prod(string id, bool ours, double score, IReadOnlyList<CoverageStep>? steps = null) => new(
            id, ours,
            processes.Select(p => new CoverageCell(p.Id, null, score)).ToArray(),
            [new ProductPrice("seg-a", 100, 2000), new ProductPrice("seg-b", 120, 2500)],
            [], [], steps);

        FirmLevelSegmentInput SegX(string id) => new(
            id, 1500, [new InitialClientTarget("comp", 1000)], [], needs,
            new DistributionSpec(DistributionType.Normal, 1.5, 0.4), 0.3, new CostRates(50, 1000, 10, 500));

        var parameters = new List<ParameterSpec>();
        foreach (string sid in new[] { "seg-a", "seg-b" })
        {
            parameters.Add(new ParameterSpec(sid, FlowKind.NewFirmArrival, null, DistributionSpec.Point(5)));
            parameters.Add(new ParameterSpec(sid, FlowKind.ConversionToMarket, null, DistributionSpec.Point(0.01)));
            parameters.Add(new ParameterSpec(sid, FlowKind.ChurnHazard, null, DistributionSpec.Point(0.02)));
            parameters.Add(new ParameterSpec(sid, FlowKind.CrossSellRate, null, DistributionSpec.Point(0.02)));
        }

        var input = new FirmLevelInput(
            [SegX("seg-a"), SegX("seg-b")],
            processes,
            [],
            [
                // Time-phased coverage on our product keeps the multi-epoch path in the hot loop.
                Prod("us", true, 0.8,
                [
                    new CoverageStep("p5", null, null, 6, 0.9), new CoverageStep("p6", null, null, 12, 0.9),
                    new CoverageStep("p7", null, null, 18, 0.9), new CoverageStep("p8", null, null, 24, 0.9),
                ]),
                Prod("comp", false, 0.6),
                Prod("niche", false, 0.4),
            ],
            [
                new ChannelInput("direct-sales", [new("seg-a", 1.0), new("seg-b", 1.0)],
                    [new("seg-a", 0.6), new("seg-b", 0.6)], []),
                new ChannelInput("consultants", [new("seg-a", 0.8), new("seg-b", 0.8)],
                    [new("seg-a", 0.4), new("seg-b", 0.4)], [new ChannelProductBias("comp", 0.5)]),
            ],
            new ChoiceParameters(2.0, 1.0, 0.5,
                [new SalesCycleSpec("seg-a", null, Math.Log(3), 0.5), new SalesCycleSpec("seg-b", null, Math.Log(3), 0.5)],
                -1.0),
            [
                new OpportunityScoping("seg-a", 0.3, DistributionSpec.Point(1)),
                new OpportunityScoping("seg-b", 0.3, DistributionSpec.Point(1)),
            ],
            parameters,
            [], [],
            HorizonMonths: 36,
            Paths: 500,
            Seed: 99);

        // Warm-up (JIT), then measure. 2 x (1500 + 1000) = 5000 base firms, all synthetic fill.
        FirmLevelEngine.Run(input with { Paths = 10 });
        var sw = Stopwatch.StartNew();
        var result = FirmLevelEngine.Run(input);
        sw.Stop();

        Assert.Equal(5000.0, result.Diagnostics.SyntheticFirmsCreated,
            5000.0 * 0.1); // fill + a few hundred arrivals over 36 months
        Assert.True(sw.ElapsedMilliseconds < 15000,
            $"5000 firms x 500 paths x 36 months took {sw.ElapsedMilliseconds}ms (target: seconds)");
        Assert.Equal(500, result.Result.Paths);
        Assert.Equal(2, result.Result.Segments.Count);
    }
}
