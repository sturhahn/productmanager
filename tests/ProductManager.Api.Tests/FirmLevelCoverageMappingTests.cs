using ProductManager.Api.Contracts;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Engine;
using DistributionType = ProductManager.Domain.Common.DistributionType;

namespace ProductManager.Api.Tests;

/// <summary>
/// How the coverage grid and the roadmap reach the choice model: segment-scoped assessments stay
/// scoped to their segment, and a planned feature delta becomes a time-phased step at the month its
/// delivering project is planned to end. Both are checked through the real POST /api/simulation-runs
/// path, against the stored input snapshot the run is reproducible from.
/// </summary>
public sealed class FirmLevelCoverageMappingTests : ApiTestBase
{
    private static readonly DateOnly RunStart = new(2026, 1, 1);

    private sealed record TwoSegmentWorld(
        Guid WorkspaceId,
        Guid MarketId,
        Guid AlphaSegmentId,
        Guid BetaSegmentId,
        Guid SettlementProcessId,
        Guid ReportingProcessId,
        Guid OurProductId,
        Guid RivalProductId,
        Guid RoadmapId,
        Guid AssumptionSetId,
        Guid? ProjectId);

    // ---------- Segment-scoped coverage ----------

    [Fact]
    public async Task A_segment_scoped_assessment_stays_scoped_to_its_segment_in_the_built_input()
    {
        var world = await CreateWorldAsync(scopedGoldForAlpha: true);
        var input = await RunAndReadInputAsync(world, seed: 4);

        var ourCells = input.Products.Single(p => p.Id == world.OurProductId.ToString()).Coverage;

        // The generic assessment is untouched — it is NOT raised to the best cell anyone holds.
        var generic = ourCells.Single(c =>
            c.ProcessId == world.SettlementProcessId.ToString() && c.SegmentId is null);
        Assert.Equal(0.4, generic.Score);

        // Alpha's own row travels as a segment-scoped cell that shadows it for Alpha only.
        var scoped = ourCells.Single(c =>
            c.ProcessId == world.SettlementProcessId.ToString() && c.SegmentId == world.AlphaSegmentId.ToString());
        Assert.Equal(1.0, scoped.Score);

        Assert.DoesNotContain(ourCells, c => c.SegmentId == world.BetaSegmentId.ToString());
    }

    [Fact]
    public async Task Gold_assessed_for_one_segment_only_lifts_that_segments_win_rate()
    {
        // Two segments identical in every respect — same needs, same stocks, same price, same
        // channel — so any difference in the funnel is the segment-scoped coverage row and nothing
        // else. Alpha is assessed Gold on settlement; Beta keeps the generic Bronze.
        var world = await CreateWorldAsync(scopedGoldForAlpha: true);

        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            world.RoadmapId, world.AssumptionSetId, HorizonMonths: 12, Paths: 8, Seed: 17,
            StartDate: RunStart, MarketId: world.MarketId, EngineMode: SimulationEngineMode.FirmLevel));

        var diagnostics = await GetAsync<FirmLevelDiagnostics>($"/api/simulation-runs/{run.Id}/diagnostics");
        var alpha = diagnostics.FunnelBySegment.Single(f => f.SegmentId == world.AlphaSegmentId.ToString());
        var beta = diagnostics.FunnelBySegment.Single(f => f.SegmentId == world.BetaSegmentId.ToString());

        Assert.True(alpha.Opened > 0 && beta.Opened > 0, "Both segments need opportunities to compare.");
        double gap = alpha.WinRate - beta.WinRate;
        Assert.True(gap > 0.05,
            $"Alpha (gold) won {alpha.WinRate:P1} of its deals, Beta (bronze) {beta.WinRate:P1} — a " +
            "segment-scoped gold assessment must lift the segment it was assessed for, visibly.");

        // The control: with no scoped row anywhere the two identical segments differ only by Monte
        // Carlo noise, which must be far smaller than the effect above — otherwise the "gap" proves
        // nothing about coverage.
        var flat = await CreateWorldAsync(scopedGoldForAlpha: false);
        var flatRun = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            flat.RoadmapId, flat.AssumptionSetId, HorizonMonths: 12, Paths: 8, Seed: 17,
            StartDate: RunStart, MarketId: flat.MarketId, EngineMode: SimulationEngineMode.FirmLevel));

        var flatDiagnostics = await GetAsync<FirmLevelDiagnostics>($"/api/simulation-runs/{flatRun.Id}/diagnostics");
        var flatAlpha = flatDiagnostics.FunnelBySegment.Single(f => f.SegmentId == flat.AlphaSegmentId.ToString());
        var flatBeta = flatDiagnostics.FunnelBySegment.Single(f => f.SegmentId == flat.BetaSegmentId.ToString());
        double noise = Math.Abs(flatAlpha.WinRate - flatBeta.WinRate);
        Assert.True(noise < gap / 2.0,
            $"Two identical segments differ by {noise:P1} of win rate, against a coverage effect of " +
            $"{gap:P1} — the effect is not distinguishable from noise at this sample size.");
    }

    // ---------- Roadmap steps ----------

    [Fact]
    public async Task A_planned_delta_becomes_a_step_at_its_projects_planned_end_month()
    {
        var world = await CreateWorldAsync(scopedGoldForAlpha: false, withRoadmapDelta: true);
        var input = await RunAndReadInputAsync(world, seed: 9);
        var ourProduct = input.Products.Single(p => p.Id == world.OurProductId.ToString());

        // The base cell is still the month-0 assessment: the roadmap does not rewrite history.
        Assert.Equal(0.4, ourProduct.Coverage
            .Single(c => c.ProcessId == world.ReportingProcessId.ToString() && c.SegmentId is null).Score);

        // The project is planned to end 2026-04-30, i.e. month 3 of a run starting 2026-01-01.
        var step = Assert.Single(ourProduct.CoverageSteps!);
        Assert.Equal(world.ReportingProcessId.ToString(), step.ProcessId);
        Assert.Null(step.ApproachId);
        Assert.Null(step.SegmentId);
        Assert.Equal(3, step.Month);
        Assert.Equal(1.0, step.Score);
    }

    [Fact]
    public async Task Delaying_the_delivering_project_moves_the_step_and_costs_early_wins()
    {
        var world = await CreateWorldAsync(scopedGoldForAlpha: false, withRoadmapDelta: true);

        var early = await RunAsync(world, seed: 23);
        var earlyInput = await ReadInputAsync(early);
        Assert.Equal(3, Assert.Single(
            earlyInput.Products.Single(p => p.Id == world.OurProductId.ToString()).CoverageSteps!).Month);

        // Slip the delivering project by six months. Nothing else about the world changes.
        await PutAsync<RoadmapDto>($"/api/roadmaps/{world.RoadmapId}/items/{world.ProjectId}",
            new RescheduleItemRequest(new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 31), null));

        var late = await RunAsync(world, seed: 23);
        var lateInput = await ReadInputAsync(late);
        Assert.Equal(9, Assert.Single(
            lateInput.Products.Single(p => p.Id == world.OurProductId.ToString()).CoverageSteps!).Month);

        // The delay is a real cost, not a re-labelling: the same seed wins fewer deals.
        var earlyDiagnostics = await GetAsync<FirmLevelDiagnostics>($"/api/simulation-runs/{early}/diagnostics");
        var lateDiagnostics = await GetAsync<FirmLevelDiagnostics>($"/api/simulation-runs/{late}/diagnostics");
        double earlyWon = earlyDiagnostics.FunnelBySegment.Sum(f => f.Won);
        double lateWon = lateDiagnostics.FunnelBySegment.Sum(f => f.Won);
        Assert.True(earlyWon > lateWon,
            $"Shipping in month 3 won {earlyWon:0.##} deals, shipping in month 9 won {lateWon:0.##}; " +
            "a six-month slip must cost wins.");
    }

    [Fact]
    public async Task A_generic_roadmap_step_still_lifts_a_segment_with_its_own_scoped_assessment()
    {
        // Alpha carries its own Bronze assessment on reporting — the process the generic roadmap
        // delta takes to Gold at month 3. The scoped base cell shadows the generic BASE score, but
        // under max(base, step) resolution it must NOT cancel the generic step: the roadmap lift
        // still applies to Alpha.
        var world = await CreateWorldAsync(
            scopedGoldForAlpha: false, withRoadmapDelta: true, scopedReportingBronzeForAlpha: true);
        var input = await RunAndReadInputAsync(world, seed: 17);
        var ourProduct = input.Products.Single(p => p.Id == world.OurProductId.ToString());

        // The builder keeps both: Alpha's scoped base cell and the still-generic step.
        Assert.Contains(ourProduct.Coverage, c =>
            c.ProcessId == world.ReportingProcessId.ToString()
            && c.SegmentId == world.AlphaSegmentId.ToString() && c.Score == 0.4);
        var step = Assert.Single(ourProduct.CoverageSteps!);
        Assert.Equal(world.ReportingProcessId.ToString(), step.ProcessId);
        Assert.Null(step.SegmentId);
        Assert.Equal(3, step.Month);
        Assert.Equal(1.0, step.Score);

        // Behavior: with the delta, the two otherwise-identical segments track each other — Alpha
        // gets the same month-3 gold lift Beta does, scoped assessment or not.
        // Paths: 300 (not the usual 8) — the engine is deterministic per (seed, paths), and at 8
        // paths the win-rate gap below is thin enough for Monte Carlo sampling noise to cross the
        // threshold. 300 paths at seed 17 converges the win rates (verified stable from 50 through
        // 1000 paths: gap1 stays ~0.001-0.03 against a 0.04 bound, gap2 stays ~0.07 against a 0.05
        // bound) while still running in well under a second.
        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            world.RoadmapId, world.AssumptionSetId, HorizonMonths: 12, Paths: 300, Seed: 17,
            StartDate: RunStart, MarketId: world.MarketId, EngineMode: SimulationEngineMode.FirmLevel));
        var diagnostics = await GetAsync<FirmLevelDiagnostics>($"/api/simulation-runs/{run.Id}/diagnostics");
        var alpha = diagnostics.FunnelBySegment.Single(f => f.SegmentId == world.AlphaSegmentId.ToString());
        var beta = diagnostics.FunnelBySegment.Single(f => f.SegmentId == world.BetaSegmentId.ToString());
        Assert.True(alpha.Opened > 0 && beta.Opened > 0, "Both segments need opportunities to compare.");
        Assert.True(Math.Abs(alpha.WinRate - beta.WinRate) < 0.04,
            $"Alpha ({alpha.WinRate:P1}) must get the generic roadmap lift Beta gets " +
            $"({beta.WinRate:P1}) — its scoped assessment must not cancel the step.");

        // The pair: the same world without the roadmap delta. The delta must be what lifts Alpha.
        var control = await CreateWorldAsync(
            scopedGoldForAlpha: false, withRoadmapDelta: false, scopedReportingBronzeForAlpha: true);
        var controlRun = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            control.RoadmapId, control.AssumptionSetId, HorizonMonths: 12, Paths: 300, Seed: 17,
            StartDate: RunStart, MarketId: control.MarketId, EngineMode: SimulationEngineMode.FirmLevel));
        var controlDiagnostics =
            await GetAsync<FirmLevelDiagnostics>($"/api/simulation-runs/{controlRun.Id}/diagnostics");
        var controlAlpha = controlDiagnostics.FunnelBySegment
            .Single(f => f.SegmentId == control.AlphaSegmentId.ToString());
        Assert.True(alpha.WinRate > controlAlpha.WinRate + 0.05,
            $"The month-3 gold step must lift Alpha's win rate ({alpha.WinRate:P1}) clearly above " +
            $"the no-roadmap control ({controlAlpha.WinRate:P1}) despite Alpha's scoped assessment.");
    }

    [Fact]
    public async Task A_delta_landing_after_the_horizon_produces_no_step_at_all()
    {
        var world = await CreateWorldAsync(scopedGoldForAlpha: false, withRoadmapDelta: true);
        await PutAsync<RoadmapDto>($"/api/roadmaps/{world.RoadmapId}/items/{world.ProjectId}",
            new RescheduleItemRequest(new DateOnly(2028, 1, 1), new DateOnly(2028, 6, 30), null));

        var input = await RunAndReadInputAsync(world, seed: 31);
        var ourProduct = input.Products.Single(p => p.Id == world.OurProductId.ToString());
        Assert.True(ourProduct.CoverageSteps is null or { Count: 0 });
    }

    // ---------- Pricing units ----------

    [Fact]
    public async Task Pricing_is_the_list_price_spread_over_the_processes_the_product_covers()
    {
        var world = await CreateWorldAsync(scopedGoldForAlpha: false, withRoadmapDelta: true);
        var input = await RunAndReadInputAsync(world, seed: 5);

        // Our product covers two processes, so the 1200/month list price is 600 per process: the
        // engine charges popcount(covered in scope) x this, which is 1200 for a full-scope win.
        var ourProduct = input.Products.Single(p => p.Id == world.OurProductId.ToString());
        Assert.All(ourProduct.Pricing, p => Assert.Equal(600.0, p.ArpuPerProcessPerMonth));

        // The rival plays on one process only, so its whole list price rides on that one.
        var rival = input.Products.Single(p => p.Id == world.RivalProductId.ToString());
        Assert.All(rival.Pricing, p => Assert.Equal(900.0, p.ArpuPerProcessPerMonth));
    }

    [Fact]
    public async Task Coverage_assessed_for_one_segment_only_widens_the_price_spread_in_that_segment_only()
    {
        // A third process our product only plays in Alpha: Alpha buys three processes for the same
        // 1200, Beta still buys two. Counting Alpha's extra cell against Beta's price would make the
        // product look cheaper per process in a segment where it does not cover that process at all.
        var world = await CreateWorldAsync(scopedGoldForAlpha: false, alphaOnlyThirdProcess: true);
        var input = await RunAndReadInputAsync(world, seed: 7);

        var pricing = input.Products.Single(p => p.Id == world.OurProductId.ToString()).Pricing;
        Assert.Equal(400.0, pricing.Single(p => p.SegmentId == world.AlphaSegmentId.ToString())
            .ArpuPerProcessPerMonth);
        Assert.Equal(600.0, pricing.Single(p => p.SegmentId == world.BetaSegmentId.ToString())
            .ArpuPerProcessPerMonth);
    }

    // ---------- Fixture ----------

    private async Task<FirmLevelInput> RunAndReadInputAsync(TwoSegmentWorld world, int seed) =>
        await ReadInputAsync(await RunAsync(world, seed));

    private async Task<Guid> RunAsync(TwoSegmentWorld world, int seed)
    {
        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            world.RoadmapId, world.AssumptionSetId, HorizonMonths: 12, Paths: 6, Seed: seed,
            StartDate: RunStart, MarketId: world.MarketId, EngineMode: SimulationEngineMode.FirmLevel));
        return run.Id;
    }

    private async Task<FirmLevelInput> ReadInputAsync(Guid runId)
    {
        var response = await Client.GetAsync($"/api/simulation-runs/{runId}/input");
        response.EnsureSuccessStatusCode();
        return SimulationJson.Deserialize<FirmLevelInput>(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Two segments that differ in nothing but (optionally) one segment-scoped coverage row, plus a
    /// rival to lose to and — optionally — a roadmap that takes reporting to gold mid-horizon.
    /// </summary>
    private async Task<TwoSegmentWorld> CreateWorldAsync(
        bool scopedGoldForAlpha, bool withRoadmapDelta = false, bool alphaOnlyThirdProcess = false,
        bool scopedReportingBronzeForAlpha = false)
    {
        // Names are unique per workspace and a test may build two worlds side by side.
        string tag = Guid.NewGuid().ToString("N")[..8];
        var workspace = await DefaultWorkspaceAsync();
        var market = await PostAsync<MarketDto>("/api/markets",
            new CreateMarketRequest(workspace.Id, $"Post-trade {tag}", null));

        var settlement = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.Id, "Settlement", null, null));
        var reporting = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.Id, "Reporting", null, null));

        var ourProduct = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Core platform", null, null, null));
        var competitor = await PostAsync<CompetitorDto>("/api/competitors",
            new CreateCompetitorRequest(market.Id, "Rival Co", null));
        var rivalProduct = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Rival platform", null, competitor.Id, null));

        // Whole-product list prices, segment-independent.
        await PutAsync<ProductDto>($"/api/products/{ourProduct.Id}/prices",
            new SetProductPriceRequest(new MoneyDto(1200m, "USD"), new MoneyDto(2000m, "USD"), null, null));
        await PutAsync<ProductDto>($"/api/products/{rivalProduct.Id}/prices",
            new SetProductPriceRequest(new MoneyDto(900m, "USD"), new MoneyDto(2000m, "USD"), null, null));

        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(ourProduct.Id, settlement.Id, CoverageTier.Bronze));
        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(ourProduct.Id, reporting.Id, CoverageTier.Bronze));
        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(rivalProduct.Id, settlement.Id, CoverageTier.Silver));

        var segments = new List<Guid>();
        foreach (string name in new[] { $"Alpha {tag}", $"Beta {tag}" })
        {
            var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
                new CreateClientSegmentRequest(market.Id, name, null, 1.0, 0.0));
            await PutAsync<ClientSegmentDto>($"/api/client-segments/{segment.Id}/stock-state",
                new SetSegmentStockStateRequest(200, 0, 1200, 2000, 100, 1000, 50, 500));

            await PostAsync<NeedDto>("/api/needs", new CreateNeedRequest(settlement.Id, 1.0, null, segment.Id));
            await PostAsync<NeedDto>("/api/needs", new CreateNeedRequest(reporting.Id, 1.0, null, segment.Id));
            segments.Add(segment.Id);
        }

        var alpha = segments[0];
        var beta = segments[1];

        if (scopedGoldForAlpha)
            await PostAsync<CoverageDto>("/api/coverages", new CreateCoverageRequest(
                ourProduct.Id, settlement.Id, CoverageTier.Gold, false, 1.0, null, alpha));

        // Alpha's own assessment of reporting, at the same Bronze tier as the generic row it
        // shadows — the setup where a scoped base cell used to cancel the generic roadmap step.
        if (scopedReportingBronzeForAlpha)
            await PostAsync<CoverageDto>("/api/coverages", new CreateCoverageRequest(
                ourProduct.Id, reporting.Id, CoverageTier.Bronze, false, 1.0, null, alpha));

        if (alphaOnlyThirdProcess)
        {
            var custody = await PostAsync<BusinessProcessDto>("/api/business-processes",
                new CreateBusinessProcessRequest(market.Id, "Custody", null, null));
            await PostAsync<CoverageDto>("/api/coverages", new CreateCoverageRequest(
                ourProduct.Id, custody.Id, CoverageTier.Silver, false, 1.0, null, alpha));
        }

        var channel = await PostAsync<ChannelDto>("/api/channels",
            new CreateChannelRequest(market.Id, "Direct sales", null, true));
        foreach (var segmentId in segments)
            await PutAsync<ChannelDto>($"/api/channels/{channel.Id}/segment-reaches",
                new SetChannelSegmentReachRequest(segmentId, 1.0, 1.0));

        await PostAsync<GenerateSyntheticFirmsResponse>("/api/firms/generate-synthetic",
            new GenerateSyntheticFirmsRequest(market.Id));

        var assumptions = await PostAsync<AssumptionSetDto>("/api/assumption-sets",
            new CreateAssumptionSetRequest(workspace.Id, $"Base case {tag}", null));
        foreach (var segmentId in segments)
        {
            await UpsertEngineParameterAsync(assumptions.Id, segmentId,
                SimulationFlowKind.ConversionToMarket, 0.25);
            await UpsertEngineParameterAsync(assumptions.Id, segmentId, SimulationFlowKind.ChurnHazard, 0.0);
            await UpsertEngineParameterAsync(assumptions.Id, segmentId, SimulationFlowKind.NewFirmArrival, 0.0);
        }

        await UpsertKnobAsync(assumptions.Id, ChoiceParameterKeys.PriceScale, 5000);
        await UpsertKnobAsync(assumptions.Id, ChoiceParameterKeys.NoDecisionUtility, 0.0);

        var roadmap = await CreateRoadmapAsync(workspace.Id, $"Live plan {tag}", isLive: true);
        Guid? projectId = null;

        if (withRoadmapDelta)
        {
            var feature = await PostAsync<FeatureDto>("/api/features",
                new CreateFeatureRequest(workspace.Id, $"Reporting rewrite {tag}", null, ourProduct.Id));
            await PostAsync<FeatureDto>($"/api/features/{feature.Id}/deltas",
                new AddFeatureDeltaRequest(FeatureDeltaKind.Coverage, reporting.Id, CoverageTier.Bronze,
                    CoverageTier.Gold, null, null, null, "Closes the reporting gap"));

            var initiative = await CreateInitiativeAsync(workspace.Id, $"Reporting overhaul {tag}");
            var project = await CreateProjectAsync(initiative.Id, $"Rewrite reporting {tag}",
                new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30));
            await PostAsync<ProjectDto>($"/api/projects/{project.Id}/features", new LinkFeatureRequest(feature.Id));
            await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
                new PlanProjectRequest(project.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30), null));
            projectId = project.Id;
        }

        return new TwoSegmentWorld(workspace.Id, market.Id, alpha, beta, settlement.Id, reporting.Id,
            ourProduct.Id, rivalProduct.Id, roadmap.Id, assumptions.Id, projectId);
    }

    private Task<AssumptionSetDto> UpsertEngineParameterAsync(
        Guid assumptionSetId, Guid segmentId, SimulationFlowKind flowKind, double value) =>
        PutAsync<AssumptionSetDto>($"/api/assumption-sets/{assumptionSetId}/parameters",
            new UpsertParameterRequest(new DistributionSpecDto(DistributionType.Point, value),
                ParameterProvenance.Asserted, null, segmentId, flowKind));

    private Task<AssumptionSetDto> UpsertKnobAsync(Guid assumptionSetId, string key, double value) =>
        PutAsync<AssumptionSetDto>($"/api/assumption-sets/{assumptionSetId}/parameters",
            new UpsertParameterRequest(new DistributionSpecDto(DistributionType.Point, value),
                ParameterProvenance.Asserted, null, Key: key));
}
