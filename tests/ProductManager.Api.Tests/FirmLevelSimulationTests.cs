using System.Net;
using System.Net.Http.Json;
using ProductManager.Api.Contracts;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Engine;

namespace ProductManager.Api.Tests;

/// <summary>
/// End-to-end firm-level runs: the same POST that drives the aggregate engine, with
/// <c>engineMode = FirmLevel</c>, mapped onto the choice model over the actual firm roster.
/// </summary>
public sealed class FirmLevelSimulationTests : Phase3TestBase
{
    private static readonly DateOnly RunStart = new(2026, 1, 1);

    [Fact]
    public async Task A_firm_level_run_executes_persists_and_reports_its_funnel()
    {
        var fixture = await CreateRunnableWorldAsync();

        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            fixture.RoadmapId, fixture.AssumptionSetId, HorizonMonths: 6, Paths: 4, Seed: 11,
            Name: "Firm level base", StartDate: RunStart, MarketId: fixture.World.MarketId,
            EngineMode: SimulationEngineMode.FirmLevel));

        Assert.True(run.IsExecuted);
        Assert.Equal(SimulationEngineMode.FirmLevel, run.EngineMode);
        Assert.Equal(FirmLevelInputBuilder.ModelVersion, run.ModelVersion);
        Assert.True(run.HasDiagnostics);

        var result = await GetAsync<EngineResult>($"/api/simulation-runs/{run.Id}/result");
        Assert.Equal(4, result.Paths);
        Assert.Equal(6, result.HorizonMonths);
        Assert.Equal(Assert.Single(result.Segments).SegmentId, fixture.World.SegmentId.ToString());
        Assert.Equal(6, result.Total.Clients.Mean.Length);

        // The segment starts with clients, so month 0 recognised ARR cannot be zero.
        Assert.True(result.Total.RecognizedArr.Mean[0] > 0);
        Assert.True(result.Npv.P10 <= result.Npv.P50 && result.Npv.P50 <= result.Npv.P90);

        var diagnostics = await GetAsync<FirmLevelDiagnostics>($"/api/simulation-runs/{run.Id}/diagnostics");
        var funnel = Assert.Single(diagnostics.FunnelBySegment);
        Assert.Equal(fixture.World.SegmentId.ToString(), funnel.SegmentId);
        Assert.True(funnel.Opened > 0, "The conversion rate should have opened opportunities.");
        Assert.Equal(funnel.Opened, funnel.Won + funnel.Lost + funnel.NoDecision + funnel.StillOpen, 6);
        Assert.InRange(funnel.WinRate, 0.0, 1.0);
        Assert.NotEmpty(diagnostics.FunnelByChannel);

        // The result document carries the diagnostics too, so one fetch answers "what happened".
        var raw = await RawAsync($"/api/simulation-runs/{run.Id}/result");
        Assert.Contains("\"diagnostics\"", raw);
    }

    [Fact]
    public async Task Two_firm_level_runs_of_the_same_inputs_and_seed_are_identical()
    {
        var fixture = await CreateRunnableWorldAsync();
        var request = new CreateSimulationRunRequest(fixture.RoadmapId, fixture.AssumptionSetId,
            HorizonMonths: 6, Paths: 4, Seed: 5, StartDate: RunStart, MarketId: fixture.World.MarketId,
            EngineMode: SimulationEngineMode.FirmLevel);

        var first = await PostAsync<SimulationRunDto>("/api/simulation-runs", request);
        var second = await PostAsync<SimulationRunDto>("/api/simulation-runs", request);
        Assert.NotEqual(first.Id, second.Id);

        Assert.Equal(
            await RawAsync($"/api/simulation-runs/{first.Id}/result"),
            await RawAsync($"/api/simulation-runs/{second.Id}/result"));
        Assert.Equal(
            await RawAsync($"/api/simulation-runs/{first.Id}/input"),
            await RawAsync($"/api/simulation-runs/{second.Id}/input"));

        // A different seed must actually change the draws, or "determinism" would be vacuous.
        var different = await PostAsync<SimulationRunDto>("/api/simulation-runs", request with { Seed = 6 });
        Assert.NotEqual(
            await RawAsync($"/api/simulation-runs/{first.Id}/result"),
            await RawAsync($"/api/simulation-runs/{different.Id}/result"));
    }

    [Fact]
    public async Task The_stored_firm_level_input_alone_reproduces_the_stored_trajectories()
    {
        var fixture = await CreateRunnableWorldAsync();

        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            fixture.RoadmapId, fixture.AssumptionSetId, HorizonMonths: 6, Paths: 4, Seed: 21,
            StartDate: RunStart, MarketId: fixture.World.MarketId, EngineMode: SimulationEngineMode.FirmLevel));

        var snapshot = SimulationJson.Deserialize<FirmLevelInput>(
            await RawAsync($"/api/simulation-runs/{run.Id}/input"));

        // No database, no request — just the stored document through the pure engine.
        var replayed = FirmLevelEngine.Run(snapshot);

        Assert.Equal(
            await RawAsync($"/api/simulation-runs/{run.Id}/diagnostics"),
            SimulationJson.Serialize(replayed.Diagnostics));
        Assert.Equal(
            SimulationJson.Serialize(replayed.Result with { ElapsedMs = 0 }),
            SimulationJson.Serialize(
                SimulationJson.Deserialize<EngineResult>(await RawAsync($"/api/simulation-runs/{run.Id}/result"))
                with { ElapsedMs = 0 }));
    }

    [Fact]
    public async Task The_firm_level_input_carries_the_roster_the_choice_model_needs()
    {
        var fixture = await CreateRunnableWorldAsync();

        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            fixture.RoadmapId, fixture.AssumptionSetId, HorizonMonths: 6, Paths: 2, Seed: 3,
            StartDate: RunStart, MarketId: fixture.World.MarketId, EngineMode: SimulationEngineMode.FirmLevel));

        var input = SimulationJson.Deserialize<FirmLevelInput>(await RawAsync($"/api/simulation-runs/{run.Id}/input"));

        Assert.Equal(2, input.Processes.Count);
        Assert.Equal(3, input.Products.Count);
        Assert.NotEmpty(input.Firms);
        Assert.Contains(input.Firms, f => f.IsSynthetic);
        Assert.Contains(input.Firms, f => f.Relationships.Count > 0);

        var segment = Assert.Single(input.Segments);
        Assert.Equal(2, segment.ProcessNeeds.Count);
        Assert.Equal(ProspectPool, segment.InitialProspectPool);
        Assert.Contains(segment.InitialClientTargets,
            t => t.ProductId == fixture.World.OurProductId.ToString() && t.Count == ClientCount);

        // The channel's reach x origination is what routes opportunities; the bias is what tilts them.
        var channel = Assert.Single(input.Channels);
        Assert.Equal(0.9, Assert.Single(channel.SegmentReach).Value);
        Assert.Equal(1.0, Assert.Single(channel.OriginationShare).Value);
        Assert.Equal(0.4, Assert.Single(channel.ProductBias).Bias);

        // Choice knobs come from the assumption set, by the documented key scheme.
        Assert.Equal(0.5, input.Choice.SwitchingCostBase);
        Assert.Equal(0.3, input.Choice.ExpansionPropensity);
        Assert.Equal(0.2, input.Choice.NoDecisionUtility);
        Assert.Equal(1.0, Assert.Single(input.Choice.SalesCycles).Mu);
        Assert.Equal(0.6, Assert.Single(input.Scoping).BroadScopeProbability);

        // Our product's coverage of settlement is gold; the rival's is silver.
        var ourProduct = input.Products.Single(p => p.Id == fixture.World.OurProductId.ToString());
        Assert.Equal(1.0, ourProduct.Coverage
            .Single(c => c.ProcessId == fixture.World.SettlementProcessId.ToString()).Score);
        Assert.Equal(0.6, Assert.Single(ourProduct.Integrations).Quality);

        // Pricing is per covered process, not per product: the engine multiplies this by the number
        // of in-scope processes a win covers, so the whole-product list price of 1000 across the two
        // processes it covers is 500 each. A full-scope win then earns the list price, not 2x it.
        Assert.Equal(500.0, Assert.Single(ourProduct.Pricing).ArpuPerProcessPerMonth);

        // The module covers one process, so its whole 400 rides on that one process.
        var module = input.Products.Single(p => p.Id == fixture.World.OurModuleId.ToString());
        Assert.Equal(400.0, Assert.Single(module.Pricing).ArpuPerProcessPerMonth);

        // Nothing on the roadmap: coverage is constant, so there are no steps to phase it.
        Assert.All(input.Products, p => Assert.True(p.CoverageSteps is null or { Count: 0 }));
    }

    [Fact]
    public async Task An_aggregate_run_is_still_the_default_and_has_no_diagnostics()
    {
        var fixture = await CreateRunnableWorldAsync();

        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            fixture.RoadmapId, fixture.AssumptionSetId, HorizonMonths: 6, Paths: 4, Seed: 11,
            StartDate: RunStart, MarketId: fixture.World.MarketId));

        Assert.Equal(SimulationEngineMode.Aggregate, run.EngineMode);
        Assert.Equal(SimulationInputBuilder.ModelVersion, run.ModelVersion);
        Assert.False(run.HasDiagnostics);

        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.GetAsync($"/api/simulation-runs/{run.Id}/diagnostics")).StatusCode);

        // The aggregate result document stays exactly an EngineResult.
        Assert.DoesNotContain("\"diagnostics\"", await RawAsync($"/api/simulation-runs/{run.Id}/result"));
    }

    [Fact]
    public async Task A_market_with_no_business_processes_cannot_run_the_choice_model()
    {
        var workspace = await DefaultWorkspaceAsync();
        var market = await PostAsync<MarketDto>("/api/markets", new CreateMarketRequest(workspace.Id, "Empty", null));
        var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
            new CreateClientSegmentRequest(market.Id, "Nobody", null));
        var assumptions = await PostAsync<AssumptionSetDto>("/api/assumption-sets",
            new CreateAssumptionSetRequest(workspace.Id, "Empty", null));
        var roadmapId = await CreateLiveRoadmapAsync(workspace.Id);

        var response = await Client.PostAsJsonAsync("/api/simulation-runs", new CreateSimulationRunRequest(
            roadmapId, assumptions.Id, HorizonMonths: 6, Paths: 2, MarketId: market.Id,
            EngineMode: SimulationEngineMode.FirmLevel), ApiFactory.Json);

        // The engine's own referential-integrity message, surfaced as a 400 with something actionable.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("business process", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(Guid.Empty, segment.Id);
    }

    // ---------- Fixture ----------

    private sealed record RunnableWorld(MarketWorld World, Guid RoadmapId, Guid AssumptionSetId);

    /// <summary>
    /// The Phase 3 world plus what a run needs: a channel, an integration, an observed client, the
    /// synthetic population filling the rest of the stocks, an assumption set and a live roadmap.
    /// </summary>
    private async Task<RunnableWorld> CreateRunnableWorldAsync()
    {
        var world = await CreateMarketAsync();

        var channel = await PostAsync<ChannelDto>("/api/channels",
            new CreateChannelRequest(world.MarketId, "Direct sales", null, true));
        await PutAsync<ChannelDto>($"/api/channels/{channel.Id}/segment-reaches",
            new SetChannelSegmentReachRequest(world.SegmentId, 0.9, 1.0));
        await PutAsync<ChannelDto>($"/api/channels/{channel.Id}/product-affinities",
            new SetChannelProductAffinityRequest(world.OurProductId, 0.4));

        await PostAsync<IntegrationDto>("/api/integrations",
            new CreateIntegrationRequest(world.OurProductId, world.RivalProductId, 0.6));

        var incumbent = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Rival-held firm", EpistemicStatus.Observed, world.SegmentId));
        await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(incumbent.Id, world.RivalProductId,
                [world.SettlementProcessId], 0.95, null, new DateOnly(2024, 1, 1)));

        await PostAsync<GenerateSyntheticFirmsResponse>("/api/firms/generate-synthetic",
            new GenerateSyntheticFirmsRequest(world.MarketId));

        var assumptions = await CreateAssumptionSetAsync(world);
        var roadmapId = await CreateLiveRoadmapAsync(world.WorkspaceId);
        return new RunnableWorld(world, roadmapId, assumptions.Id);
    }
}
