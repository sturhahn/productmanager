using System.Net;
using System.Net.Http.Json;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Domain.Common;

namespace ProductManager.Api.Tests;

public sealed class CrudRoundTripTests : ApiTestBase
{
    [Fact]
    public async Task Initiative_round_trips_through_create_read_update_delete()
    {
        var workspace = await DefaultWorkspaceAsync();
        var segmentId = Guid.NewGuid();

        var created = await PostAsync<InitiativeDto>("/api/initiatives", new CreateInitiativeRequest(
            workspace.Id, "Settlement modernization", "Cut fails by half", "Ops teams feel the pain daily",
            [segmentId], null));

        Assert.Equal("Settlement modernization", created.Name);
        Assert.Equal([segmentId], created.TargetSegmentIds);

        var fetched = await GetAsync<InitiativeDto>($"/api/initiatives/{created.Id}");
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Cut fails by half", fetched.Outcome);
        Assert.Equal([segmentId], fetched.TargetSegmentIds);

        var updated = await PutAsync<InitiativeDto>($"/api/initiatives/{created.Id}",
            new UpdateInitiativeRequest("Settlement modernization v2", "Cut fails by 70%", null, [], null));
        Assert.Equal("Settlement modernization v2", updated.Name);
        Assert.Empty(updated.TargetSegmentIds);

        var list = await GetAsync<PagedResponse<InitiativeDto>>($"/api/initiatives?workspaceId={workspace.Id}");
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(1, list.Page);

        var deleted = await Client.DeleteAsync($"/api/initiatives/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await Client.GetAsync($"/api/initiatives/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Initiative_round_trips_strategic_intent()
    {
        var workspace = await DefaultWorkspaceAsync();

        var created = await PostAsync<InitiativeDto>("/api/initiatives", new CreateInitiativeRequest(
            workspace.Id, "FIX table stakes", "Close the protocol gap", "Blocking two venues",
            null, null, StrategicIntent.Parity));

        Assert.Equal(StrategicIntent.Parity, created.StrategicIntent);

        var fetched = await GetAsync<InitiativeDto>($"/api/initiatives/{created.Id}");
        Assert.Equal(StrategicIntent.Parity, fetched.StrategicIntent);

        var cleared = await PutAsync<InitiativeDto>($"/api/initiatives/{created.Id}",
            new UpdateInitiativeRequest("FIX table stakes", "Close the protocol gap", "Blocking two venues", null, null, null));
        Assert.Null(cleared.StrategicIntent);

        var retagged = await PutAsync<InitiativeDto>($"/api/initiatives/{created.Id}",
            new UpdateInitiativeRequest("FIX table stakes", "Close the protocol gap", "Blocking two venues", null, null,
                StrategicIntent.Differentiation));
        Assert.Equal(StrategicIntent.Differentiation, retagged.StrategicIntent);
    }

    [Fact]
    public async Task Initiative_requires_a_name()
    {
        var workspace = await DefaultWorkspaceAsync();

        var response = await Client.PostAsJsonAsync("/api/initiatives",
            new { workspaceId = workspace.Id, name = "" }, ApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Initiative_on_an_unknown_workspace_is_not_found()
    {
        var response = await Client.PostAsJsonAsync("/api/initiatives",
            new CreateInitiativeRequest(Guid.NewGuid(), "Orphan", null, null, null, null), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Project_round_trips_with_dates_status_and_dependencies()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");

        var first = await CreateProjectAsync(initiative.Id, "Ledger rewrite",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        var second = await CreateProjectAsync(initiative.Id, "Reporting");

        Assert.Equal(ProjectStatus.Proposed, first.Status);
        Assert.Equal(new DateOnly(2026, 3, 31), first.EndDate);

        var updated = await PutAsync<ProjectDto>($"/api/projects/{second.Id}", new UpdateProjectRequest(
            "Reporting v2", "Now with exports", ProjectStatus.InProgress,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)));
        Assert.Equal(ProjectStatus.InProgress, updated.Status);
        Assert.Equal("Reporting v2", updated.Name);

        var withDependency = await PostAsync<ProjectDto>($"/api/projects/{second.Id}/dependencies",
            new AddProjectDependencyRequest(first.Id));
        Assert.Equal([first.Id], withDependency.DependsOnProjectIds);

        var removed = await Client.DeleteAsync($"/api/projects/{second.Id}/dependencies/{first.Id}");
        removed.EnsureSuccessStatusCode();
        var afterRemoval = await GetAsync<ProjectDto>($"/api/projects/{second.Id}");
        Assert.Empty(afterRemoval.DependsOnProjectIds);

        var byInitiative = await GetAsync<PagedResponse<ProjectDto>>($"/api/projects?initiativeId={initiative.Id}");
        Assert.Equal(2, byInitiative.TotalCount);

        var byWorkspace = await GetAsync<PagedResponse<ProjectDto>>($"/api/projects?workspaceId={workspace.Id}");
        Assert.Equal(2, byWorkspace.TotalCount);
    }

    [Fact]
    public async Task A_project_cannot_end_before_it_starts()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");

        var response = await Client.PostAsJsonAsync("/api/projects", new CreateProjectRequest(
            initiative.Id, "Backwards", null, null, new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1)),
            ApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>(ApiFactory.Json);
        Assert.Contains("end date", problem!.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_project_cannot_depend_on_itself()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var project = await CreateProjectAsync(initiative.Id, "Solo");

        var response = await Client.PostAsJsonAsync($"/api/projects/{project.Id}/dependencies",
            new AddProjectDependencyRequest(project.Id), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Team_round_trips_and_accepts_capacity_per_period()
    {
        var workspace = await DefaultWorkspaceAsync();

        var team = await PostAsync<TeamDto>("/api/teams", new CreateTeamRequest(workspace.Id, "Core", "backend"));
        Assert.Equal("backend", team.SpecializationTag);
        Assert.Empty(team.Capacities);

        var withCapacity = await PutAsync<TeamDto>($"/api/teams/{team.Id}/capacities",
            new SetTeamCapacityRequest(2026, 1, 20m));
        Assert.Equal(20m, Assert.Single(withCapacity.Capacities).Points);

        // Upsert: the same period is replaced, not duplicated.
        var reset = await PutAsync<TeamDto>($"/api/teams/{team.Id}/capacities",
            new SetTeamCapacityRequest(2026, 1, 25m));
        Assert.Equal(25m, Assert.Single(reset.Capacities).Points);

        var second = await PutAsync<TeamDto>($"/api/teams/{team.Id}/capacities",
            new SetTeamCapacityRequest(2026, 2, 15m));
        Assert.Equal(2, second.Capacities.Count);

        var refetched = await GetAsync<TeamDto>($"/api/teams/{team.Id}");
        Assert.Equal(2, refetched.Capacities.Count);
        Assert.Equal([25m, 15m], refetched.Capacities.Select(c => c.Points));

        var renamed = await PutAsync<TeamDto>($"/api/teams/{team.Id}", new UpdateTeamRequest("Core Platform", "quant"));
        Assert.Equal("Core Platform", renamed.Name);
        Assert.Equal(2, renamed.Capacities.Count);

        var deleted = await Client.DeleteAsync($"/api/teams/{team.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Negative_capacity_is_rejected()
    {
        var workspace = await DefaultWorkspaceAsync();
        var team = await CreateTeamAsync(workspace.Id, "Core");

        var response = await Client.PutAsJsonAsync($"/api/teams/{team.Id}/capacities",
            new SetTeamCapacityRequest(2026, 1, -5m), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Feature_round_trips_with_deltas_and_links_to_a_project()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Coverage push");
        var project = await CreateProjectAsync(initiative.Id, "Build the importer");

        var feature = await PostAsync<FeatureDto>("/api/features",
            new CreateFeatureRequest(workspace.Id, "Bulk data importer", "Reduces switching cost", null));
        Assert.Equal(FeatureStatus.Hypothetical, feature.Status);

        var processId = Guid.NewGuid();
        var withCoverageDelta = await PostAsync<FeatureDto>($"/api/features/{feature.Id}/deltas",
            new AddFeatureDeltaRequest(FeatureDeltaKind.Coverage, processId, CoverageTier.Bronze,
                CoverageTier.Gold, null, null, null, "Closes the gap"));
        var delta = Assert.Single(withCoverageDelta.Deltas);
        Assert.Equal(CoverageTier.Gold, delta.ToTier);

        var withBoth = await PostAsync<FeatureDto>($"/api/features/{feature.Id}/deltas",
            new AddFeatureDeltaRequest(FeatureDeltaKind.SwitchingCost, null, null, null, null, null, -0.4, null));
        Assert.Equal(2, withBoth.Deltas.Count);

        // A coverage delta must raise the tier.
        var invalid = await Client.PostAsJsonAsync($"/api/features/{feature.Id}/deltas",
            new AddFeatureDeltaRequest(FeatureDeltaKind.Coverage, processId, CoverageTier.Gold,
                CoverageTier.Bronze, null, null, null, null), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var replaced = await PutAsync<FeatureDto>($"/api/features/{feature.Id}/deltas",
            new[]
            {
                new AddFeatureDeltaRequest(FeatureDeltaKind.Coverage, processId, null, CoverageTier.Silver,
                    null, null, null, null)
            });
        Assert.Single(replaced.Deltas);
        Assert.Equal(CoverageTier.Silver, replaced.Deltas[0].ToTier);

        var shipped = await PutAsync<FeatureDto>($"/api/features/{feature.Id}", new UpdateFeatureRequest(
            "Bulk data importer", null, null, FeatureStatus.Shipped, new DateOnly(2026, 5, 1)));
        Assert.Equal(FeatureStatus.Shipped, shipped.Status);
        Assert.Equal(new DateOnly(2026, 5, 1), shipped.ShippedDate);

        var linked = await PostAsync<ProjectDto>($"/api/projects/{project.Id}/features",
            new LinkFeatureRequest(feature.Id));
        Assert.Equal([feature.Id], linked.DeliveredFeatureIds);

        var unlinked = await Client.DeleteAsync($"/api/projects/{project.Id}/features/{feature.Id}");
        unlinked.EnsureSuccessStatusCode();
        Assert.Empty((await GetAsync<ProjectDto>($"/api/projects/{project.Id}")).DeliveredFeatureIds);

        var byWorkspace = await GetAsync<PagedResponse<FeatureDto>>($"/api/features?workspaceId={workspace.Id}");
        Assert.Equal(1, byWorkspace.TotalCount);
    }

    [Fact]
    public async Task List_endpoints_paginate()
    {
        var workspace = await DefaultWorkspaceAsync();
        for (var i = 0; i < 5; i++)
            await CreateInitiativeAsync(workspace.Id, $"Initiative {i}");

        var firstPage = await GetAsync<PagedResponse<InitiativeDto>>(
            $"/api/initiatives?workspaceId={workspace.Id}&page=1&pageSize=2");

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);
        Assert.Equal(2, firstPage.Items.Count);

        var lastPage = await GetAsync<PagedResponse<InitiativeDto>>(
            $"/api/initiatives?workspaceId={workspace.Id}&page=3&pageSize=2");
        Assert.Single(lastPage.Items);

        var invalid = await Client.GetAsync($"/api/initiatives?workspaceId={workspace.Id}&pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Market_side_entities_can_be_seeded_and_read_back()
    {
        var workspace = await DefaultWorkspaceAsync();

        var market = await PostAsync<MarketDto>("/api/markets",
            new CreateMarketRequest(workspace.Id, "Post-trade", "Clearing and settlement"));

        var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
            new CreateClientSegmentRequest(market.Id, "Mid-size asset managers", null, 0.8, 0.6));
        Assert.Equal(0.8, segment.SuiteAffinity);

        var process = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.Id, "Settlement", null, null));
        var subProcess = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.Id, "Fails management", null, process.Id));
        Assert.Equal(process.Id, subProcess.ParentProcessId);

        var approach = await PostAsync<ApproachDto>("/api/approaches",
            new CreateApproachRequest(process.Id, "On-chain", null));

        var product = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Core platform", null, null, null));
        Assert.True(product.IsOurs);

        var priced = await PutAsync<ProductDto>($"/api/products/{product.Id}/prices", new SetProductPriceRequest(
            new MoneyDto(12000m, "USD"), new MoneyDto(50000m, "USD"), segment.Id, null));
        Assert.Equal(12000m, Assert.Single(priced.Prices).RecurringPricePerPeriod.Amount);

        var coverage = await PostAsync<CoverageDto>("/api/coverages", new CreateCoverageRequest(
            product.Id, process.Id, CoverageTier.Silver, false, 0.8, approach.Id, segment.Id,
            new DateOnly(2026, 1, 1), "Competitive but not leading"));
        Assert.Equal(CoverageTier.Silver, coverage.Tier);
        Assert.Equal(0.7, coverage.NumericScore);

        var unknown = await PostAsync<CoverageDto>("/api/coverages", new CreateCoverageRequest(
            product.Id, subProcess.Id, CoverageTier.NotPlaying, true));
        Assert.True(unknown.IsUnknown);
        Assert.Null(unknown.NumericScore);

        var coverages = await GetAsync<PagedResponse<CoverageDto>>($"/api/coverages?productId={product.Id}");
        Assert.Equal(2, coverages.TotalCount);

        var processes = await GetAsync<PagedResponse<BusinessProcessDto>>($"/api/business-processes?marketId={market.Id}");
        Assert.Equal(2, processes.TotalCount);
    }

    private sealed record ProblemDetailsBody(string? Title, string? Detail, int? Status);
}
