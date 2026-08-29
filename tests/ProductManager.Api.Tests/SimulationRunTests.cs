using System.Net;
using System.Net.Http.Json;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Engine;
using DistributionType = ProductManager.Domain.Common.DistributionType;

namespace ProductManager.Api.Tests;

/// <summary>
/// Assumption sets, their branches, and end-to-end simulation runs: assumptions → run → trajectories.
/// </summary>
public sealed class SimulationRunTests : ApiTestBase
{
    private static readonly DateOnly RunStart = new(2026, 1, 1);

    [Fact]
    public async Task Assumption_set_branch_is_isolated_from_its_parent()
    {
        var workspace = await DefaultWorkspaceAsync();
        var segmentId = (await CreateMarketWithSegmentsAsync()).Segments[0];

        var baseSet = await PostAsync<AssumptionSetDto>("/api/assumption-sets",
            new CreateAssumptionSetRequest(workspace.Id, "Base case", "Everything asserted"));

        var withChurn = await PutAsync<AssumptionSetDto>($"/api/assumption-sets/{baseSet.Id}/parameters",
            new UpsertParameterRequest(
                new DistributionSpecDto(DistributionType.Point, 0.01),
                ParameterProvenance.Asserted, "Observed 1%/month",
                segmentId, SimulationFlowKind.ChurnHazard));

        var churn = Assert.Single(withChurn.Parameters);
        Assert.True(churn.IsEngineParameter);
        Assert.Equal(segmentId, churn.ClientSegmentId);
        Assert.Equal(SimulationFlowKind.ChurnHazard, churn.FlowKind);

        // Upsert on the engine's own key: same (segment, flow) replaces rather than duplicating.
        var reasserted = await PutAsync<AssumptionSetDto>($"/api/assumption-sets/{baseSet.Id}/parameters",
            new UpsertParameterRequest(
                new DistributionSpecDto(DistributionType.Beta, 2, 198),
                ParameterProvenance.Calibrated, "Fitted",
                segmentId, SimulationFlowKind.ChurnHazard));
        var single = Assert.Single(reasserted.Parameters);
        Assert.Equal(DistributionType.Beta, single.Distribution.Type);
        Assert.Equal(ParameterProvenance.Calibrated, single.Provenance);

        // "What if churn is 2x" is a branch, not an edit.
        var branch = await PostAsync<AssumptionSetDto>($"/api/assumption-sets/{baseSet.Id}/branch",
            new BranchAssumptionSetRequest("Churn doubles"));
        Assert.Equal(baseSet.Id, branch.ParentAssumptionSetId);
        Assert.Single(branch.Parameters);
        Assert.NotEqual(single.Id, branch.Parameters[0].Id);

        await PutAsync<AssumptionSetDto>($"/api/assumption-sets/{branch.Id}/parameters",
            new UpsertParameterRequest(
                new DistributionSpecDto(DistributionType.Point, 0.02),
                ParameterProvenance.Asserted, "Twice as bad",
                segmentId, SimulationFlowKind.ChurnHazard));

        var parentAfter = await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{baseSet.Id}");
        Assert.Equal(DistributionType.Beta, Assert.Single(parentAfter.Parameters).Distribution.Type);

        var branchAfter = await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{branch.Id}");
        Assert.Equal(0.02, Assert.Single(branchAfter.Parameters).Distribution.P1);

        var pruned = await DeleteAsync<AssumptionSetDto>(
            $"/api/assumption-sets/{branch.Id}/parameters/{branchAfter.Parameters[0].Id}");
        Assert.Empty(pruned.Parameters);
    }

    [Fact]
    public async Task An_engine_parameter_needs_both_a_segment_and_a_flow_kind()
    {
        var workspace = await DefaultWorkspaceAsync();
        var set = await PostAsync<AssumptionSetDto>("/api/assumption-sets",
            new CreateAssumptionSetRequest(workspace.Id, "Base case", null));

        var response = await Client.PutAsJsonAsync($"/api/assumption-sets/{set.Id}/parameters", new
        {
            distribution = new { type = "Point", p1 = 0.5, p2 = 0.0 },
            flowKind = "WinRate"
        }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // A free-form assumption the engine ignores is still allowed, keyed by string.
        var freeForm = await PutAsync<AssumptionSetDto>($"/api/assumption-sets/{set.Id}/parameters",
            new UpsertParameterRequest(new DistributionSpecDto(DistributionType.Normal, 0.3, 0.05),
                ParameterProvenance.Default, "Placeholder", Key: "utility.price-weight"));
        Assert.False(Assert.Single(freeForm.Parameters).IsEngineParameter);
    }

    [Fact]
    public async Task A_run_executes_persists_and_returns_a_sane_result()
    {
        var fixture = await CreateFullFixtureAsync();

        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            fixture.RoadmapId, fixture.AssumptionSetId, HorizonMonths: 12, Paths: 8, Seed: 42,
            Name: "Base case", StartDate: RunStart));

        Assert.True(run.IsExecuted);
        Assert.Equal(12, run.HorizonMonths);
        Assert.Equal(8, run.Paths);
        Assert.Equal(42, run.Seed);
        Assert.Equal("Base case", run.Name);
        Assert.Equal(RunStart, run.StartDate);
        Assert.Equal(SimulationInputBuilder.ModelVersion, run.ModelVersion);

        var listed = await GetAsync<PagedResponse<SimulationRunDto>>(
            $"/api/simulation-runs?workspaceId={fixture.WorkspaceId}");
        Assert.Equal(1, listed.TotalCount);
        Assert.Equal(run.Id, listed.Items[0].Id);

        var result = await GetAsync<EngineResult>($"/api/simulation-runs/{run.Id}/result");
        Assert.Equal(8, result.Paths);
        Assert.Equal(12, result.HorizonMonths);
        Assert.Equal(8, result.NpvPerPath.Length);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("total", result.Total.SegmentId);
        Assert.Equal(12, result.Total.Clients.Mean.Length);

        // The segments started with clients, so month 0 recognised ARR cannot be zero.
        Assert.True(result.Total.RecognizedArr.Mean[0] > 0);
        Assert.True(result.Total.Clients.Mean[0] > 0);
        Assert.True(result.Npv.P10 <= result.Npv.P50 && result.Npv.P50 <= result.Npv.P90);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/simulation-runs/{run.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/simulation-runs/{run.Id}")).StatusCode);
    }

    [Fact]
    public async Task Two_runs_of_the_same_inputs_and_seed_produce_identical_results()
    {
        var fixture = await CreateFullFixtureAsync();
        var request = new CreateSimulationRunRequest(fixture.RoadmapId, fixture.AssumptionSetId,
            HorizonMonths: 12, Paths: 8, Seed: 7, StartDate: RunStart);

        var first = await PostAsync<SimulationRunDto>("/api/simulation-runs", request);
        var second = await PostAsync<SimulationRunDto>("/api/simulation-runs", request);

        Assert.NotEqual(first.Id, second.Id);

        var firstResult = await RawAsync($"/api/simulation-runs/{first.Id}/result");
        var secondResult = await RawAsync($"/api/simulation-runs/{second.Id}/result");
        Assert.Equal(firstResult, secondResult);

        var firstInput = await RawAsync($"/api/simulation-runs/{first.Id}/input");
        var secondInput = await RawAsync($"/api/simulation-runs/{second.Id}/input");
        Assert.Equal(firstInput, secondInput);

        // A different seed must actually change the draws, or "determinism" would be vacuous.
        var different = await PostAsync<SimulationRunDto>("/api/simulation-runs", request with { Seed = 8 });
        Assert.NotEqual(firstResult, await RawAsync($"/api/simulation-runs/{different.Id}/result"));
    }

    [Fact]
    public async Task The_stored_input_snapshot_alone_reproduces_the_stored_result()
    {
        var fixture = await CreateFullFixtureAsync();

        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            fixture.RoadmapId, fixture.AssumptionSetId, HorizonMonths: 12, Paths: 8, Seed: 99,
            StartDate: RunStart));

        var snapshot = SimulationJson.Deserialize<EngineInput>(await RawAsync($"/api/simulation-runs/{run.Id}/input"));

        // No database, no request — just the stored document through the pure engine.
        var replayed = SimulationEngine.Run(snapshot) with { ElapsedMs = 0 };

        Assert.Equal(
            await RawAsync($"/api/simulation-runs/{run.Id}/result"),
            SimulationJson.Serialize(replayed));
    }

    [Fact]
    public async Task A_planned_feature_raises_attractiveness_from_the_month_its_project_lands()
    {
        var fixture = await CreateFullFixtureAsync();

        var run = await PostAsync<SimulationRunDto>("/api/simulation-runs", new CreateSimulationRunRequest(
            fixture.RoadmapId, fixture.AssumptionSetId, HorizonMonths: 12, Paths: 4, Seed: 1,
            StartDate: RunStart));

        var input = SimulationJson.Deserialize<EngineInput>(await RawAsync($"/api/simulation-runs/{run.Id}/input"));

        var segmentId = fixture.SegmentIds[0].ToString();
        var forSegment = input.AttractivenessSchedule
            .Where(a => a.SegmentId == segmentId)
            .ToDictionary(a => a.Month, a => a.Multiplier);

        // The project lands at the end of month 5, so months 0-4 are the pre-feature world.
        Assert.True(forSegment.TryGetValue(5, out double afterShip));
        double beforeShip = forSegment.TryGetValue(4, out double before) ? before : 1.0;

        Assert.True(afterShip > beforeShip,
            $"Expected the shipped feature to raise attractiveness ({beforeShip} -> {afterShip}).");
        Assert.InRange(afterShip, SimulationInputBuilder.MinAttractiveness, SimulationInputBuilder.MaxAttractiveness);

        // Coverage never regresses, so the multiplier is monotone non-decreasing over the horizon.
        double previous = 0;
        for (int month = 0; month < 12; month++)
        {
            double current = forSegment.GetValueOrDefault(month, 1.0);
            Assert.True(current >= previous - 1e-12, $"Attractiveness fell at month {month}.");
            previous = current;
        }
    }

    [Fact]
    public async Task An_event_overlay_must_name_events_that_exist()
    {
        var fixture = await CreateFullFixtureAsync();

        var response = await Client.PostAsJsonAsync("/api/simulation-runs", new
        {
            roadmapId = fixture.RoadmapId,
            assumptionSetId = fixture.AssumptionSetId,
            horizonMonths = 6,
            paths = 2,
            hypotheticalEventIds = new[] { Guid.NewGuid() }
        }, ApiFactory.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_workspace_with_no_segments_cannot_be_simulated()
    {
        var workspace = await DefaultWorkspaceAsync();
        var roadmap = await CreateRoadmapAsync(workspace.Id, "Live plan", isLive: true);
        var assumptions = await PostAsync<AssumptionSetDto>("/api/assumption-sets",
            new CreateAssumptionSetRequest(workspace.Id, "Base case", null));

        var response = await Client.PostAsJsonAsync("/api/simulation-runs",
            new CreateSimulationRunRequest(roadmap.Id, assumptions.Id, HorizonMonths: 6, Paths: 2),
            ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Fixtures ----------

    private async Task<string> RawAsync(string url)
    {
        var response = await Client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode} from {url}");
        return await response.Content.ReadAsStringAsync();
    }

    private sealed record MarketFixture(Guid WorkspaceId, Guid MarketId, IReadOnlyList<Guid> Segments);

    private sealed record FullFixture(
        Guid WorkspaceId,
        Guid MarketId,
        IReadOnlyList<Guid> SegmentIds,
        Guid RoadmapId,
        Guid AssumptionSetId);

    private async Task<MarketFixture> CreateMarketWithSegmentsAsync()
    {
        var workspace = await DefaultWorkspaceAsync();
        var market = await PostAsync<MarketDto>("/api/markets",
            new CreateMarketRequest(workspace.Id, "Post-trade", null));

        var segments = new List<Guid>();
        foreach (var (name, clients, pool, arpu) in new[]
                 {
                     ("Mid-size asset managers", 20, 300.0, 8000.0),
                     ("Hedge funds", 8, 120.0, 12000.0)
                 })
        {
            var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
                new CreateClientSegmentRequest(market.Id, name, null));
            await PutAsync<ClientSegmentDto>($"/api/client-segments/{segment.Id}/stock-state",
                new SetSegmentStockStateRequest(pool, clients, arpu, arpu * 5, 1200, 40000, 300, 15000));
            segments.Add(segment.Id);
        }

        return new MarketFixture(workspace.Id, market.Id, segments);
    }

    /// <summary>
    /// A small but complete world: two measured segments, a coverage grid with a rival, an
    /// assumption set with every flow parameterised, and a live roadmap whose one project delivers
    /// a feature that lifts coverage to gold at the end of month 5.
    /// </summary>
    private async Task<FullFixture> CreateFullFixtureAsync()
    {
        var market = await CreateMarketWithSegmentsAsync();

        var process = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.MarketId, "Settlement", null, null));

        var ourProduct = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.MarketId, "Core platform", null, null, null));
        var competitor = await PostAsync<CompetitorDto>("/api/competitors",
            new CreateCompetitorRequest(market.MarketId, "Rival Co", null));
        var theirProduct = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.MarketId, "Rival platform", null, competitor.Id, null));

        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(ourProduct.Id, process.Id, CoverageTier.Bronze));
        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(theirProduct.Id, process.Id, CoverageTier.Silver));

        // The seam: a feature that takes settlement to gold, delivered by a planned project.
        var feature = await PostAsync<FeatureDto>("/api/features",
            new CreateFeatureRequest(market.WorkspaceId, "Settlement rewrite", null, ourProduct.Id));
        await PostAsync<FeatureDto>($"/api/features/{feature.Id}/deltas",
            new AddFeatureDeltaRequest(FeatureDeltaKind.Coverage, process.Id, CoverageTier.Bronze,
                CoverageTier.Gold, null, null, null, "Closes the gap"));

        var initiative = await CreateInitiativeAsync(market.WorkspaceId, "Settlement modernization");
        var project = await CreateProjectAsync(initiative.Id, "Rewrite settlement",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await PostAsync<ProjectDto>($"/api/projects/{project.Id}/features",
            new LinkFeatureRequest(feature.Id));

        var roadmap = await CreateRoadmapAsync(market.WorkspaceId, "Live plan", isLive: true);
        await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
            new PlanProjectRequest(project.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), null));

        var assumptions = await PostAsync<AssumptionSetDto>("/api/assumption-sets",
            new CreateAssumptionSetRequest(market.WorkspaceId, "Base case", null));

        foreach (var segmentId in market.Segments)
        {
            await UpsertParameterAsync(assumptions.Id, segmentId, SimulationFlowKind.NewFirmArrival,
                new DistributionSpecDto(DistributionType.Point, 3));
            await UpsertParameterAsync(assumptions.Id, segmentId, SimulationFlowKind.ConversionToMarket,
                new DistributionSpecDto(DistributionType.Point, 0.05));
            await UpsertParameterAsync(assumptions.Id, segmentId, SimulationFlowKind.WinRate,
                new DistributionSpecDto(DistributionType.Beta, 3, 7));
            await UpsertParameterAsync(assumptions.Id, segmentId, SimulationFlowKind.ChurnHazard,
                new DistributionSpecDto(DistributionType.Point, 0.01));
        }

        return new FullFixture(market.WorkspaceId, market.MarketId, market.Segments, roadmap.Id, assumptions.Id);
    }

    private Task<AssumptionSetDto> UpsertParameterAsync(Guid assumptionSetId, Guid segmentId,
        SimulationFlowKind flowKind, DistributionSpecDto distribution) =>
        PutAsync<AssumptionSetDto>($"/api/assumption-sets/{assumptionSetId}/parameters",
            new UpsertParameterRequest(distribution, ParameterProvenance.Asserted, null, segmentId, flowKind));
}
