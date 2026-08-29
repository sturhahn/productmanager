using ProductManager.Api.Contracts;
using ProductManager.Domain.Services;

namespace ProductManager.Api.Tests;

public sealed class FeasibilityTests : ApiTestBase
{
    [Fact]
    public async Task An_overallocated_team_produces_a_capacity_warning()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var team = await CreateTeamAsync(workspace.Id, "Core");
        await PutAsync<TeamDto>($"/api/teams/{team.Id}/capacities", new SetTeamCapacityRequest(2026, 1, 10m));

        // Two projects each demanding 6 points from the same team in the same period: 12 > 10.
        var first = await CreateProjectAsync(initiative.Id, "First");
        var second = await CreateProjectAsync(initiative.Id, "Second");
        await PutAsync<ProjectDto>($"/api/projects/{first.Id}/effort-demands",
            new SetEffortDemandRequest(team.Id, 2026, 1, 6m));
        await PutAsync<ProjectDto>($"/api/projects/{second.Id}/effort-demands",
            new SetEffortDemandRequest(team.Id, 2026, 1, 6m));

        var roadmap = await CreateRoadmapAsync(workspace.Id, "2026 plan", isLive: true);
        foreach (var project in new[] { first, second })
            await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
                new PlanProjectRequest(project.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null));

        var feasibility = await GetAsync<RoadmapFeasibilityDto>($"/api/roadmaps/{roadmap.Id}/feasibility");

        Assert.False(feasibility.IsFeasible);
        var warning = Assert.Single(feasibility.CapacityWarnings);
        Assert.Equal(team.Id, warning.TeamId);
        Assert.Equal("Core", warning.TeamName);
        Assert.Equal(2026, warning.Year);
        Assert.Equal(1, warning.Month);
        Assert.Equal(10m, warning.CapacityPoints);
        Assert.Equal(12m, warning.DemandPoints);
        Assert.Equal(2m, warning.OverallocationPoints);
        Assert.False(warning.CapacityUndefined);
        Assert.Empty(feasibility.SequencingViolations);
    }

    [Fact]
    public async Task Demand_within_capacity_is_feasible()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var team = await CreateTeamAsync(workspace.Id, "Core");
        await PutAsync<TeamDto>($"/api/teams/{team.Id}/capacities", new SetTeamCapacityRequest(2026, 1, 10m));

        var project = await CreateProjectAsync(initiative.Id, "Fits");
        await PutAsync<ProjectDto>($"/api/projects/{project.Id}/effort-demands",
            new SetEffortDemandRequest(team.Id, 2026, 1, 10m));

        var roadmap = await CreateRoadmapAsync(workspace.Id, "2026 plan", isLive: true);
        await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
            new PlanProjectRequest(project.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null));

        var feasibility = await GetAsync<RoadmapFeasibilityDto>($"/api/roadmaps/{roadmap.Id}/feasibility");

        Assert.True(feasibility.IsFeasible);
        Assert.Empty(feasibility.CapacityWarnings);
    }

    [Fact]
    public async Task Demand_in_a_period_with_no_declared_capacity_is_flagged_as_undefined()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var team = await CreateTeamAsync(workspace.Id, "Core");

        var project = await CreateProjectAsync(initiative.Id, "Unbudgeted");
        await PutAsync<ProjectDto>($"/api/projects/{project.Id}/effort-demands",
            new SetEffortDemandRequest(team.Id, 2026, 7, 4m));

        var roadmap = await CreateRoadmapAsync(workspace.Id, "2026 plan", isLive: true);
        await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
            new PlanProjectRequest(project.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), null));

        var feasibility = await GetAsync<RoadmapFeasibilityDto>($"/api/roadmaps/{roadmap.Id}/feasibility");

        var warning = Assert.Single(feasibility.CapacityWarnings);
        Assert.True(warning.CapacityUndefined);
        Assert.Equal(0m, warning.CapacityPoints);
    }

    [Fact]
    public async Task A_project_planned_before_its_dependency_ends_is_a_sequencing_violation()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var upstream = await CreateProjectAsync(initiative.Id, "Upstream");
        var downstream = await CreateProjectAsync(initiative.Id, "Downstream");
        await PostAsync<ProjectDto>($"/api/projects/{downstream.Id}/dependencies",
            new AddProjectDependencyRequest(upstream.Id));

        var roadmap = await CreateRoadmapAsync(workspace.Id, "2026 plan", isLive: true);
        await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
            new PlanProjectRequest(upstream.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), null));
        await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
            new PlanProjectRequest(downstream.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 31), null));

        var feasibility = await GetAsync<RoadmapFeasibilityDto>($"/api/roadmaps/{roadmap.Id}/feasibility");

        Assert.False(feasibility.IsFeasible);
        var violation = Assert.Single(feasibility.SequencingViolations);
        Assert.Equal(SequencingViolationKind.StartsBeforeDependencyEnds, violation.Kind);
        Assert.Equal(downstream.Id, violation.ProjectId);
        Assert.Equal(upstream.Id, violation.DependsOnProjectId);
    }

    [Fact]
    public async Task Effort_demand_of_zero_clears_the_warning()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var team = await CreateTeamAsync(workspace.Id, "Core");
        await PutAsync<TeamDto>($"/api/teams/{team.Id}/capacities", new SetTeamCapacityRequest(2026, 1, 1m));

        var project = await CreateProjectAsync(initiative.Id, "Heavy");
        await PutAsync<ProjectDto>($"/api/projects/{project.Id}/effort-demands",
            new SetEffortDemandRequest(team.Id, 2026, 1, 9m));

        var roadmap = await CreateRoadmapAsync(workspace.Id, "2026 plan", isLive: true);
        await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
            new PlanProjectRequest(project.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null));

        Assert.Single((await GetAsync<RoadmapFeasibilityDto>(
            $"/api/roadmaps/{roadmap.Id}/feasibility")).CapacityWarnings);

        var cleared = await PutAsync<ProjectDto>($"/api/projects/{project.Id}/effort-demands",
            new SetEffortDemandRequest(team.Id, 2026, 1, 0m));
        Assert.Empty(cleared.EffortDemands);

        Assert.True((await GetAsync<RoadmapFeasibilityDto>($"/api/roadmaps/{roadmap.Id}/feasibility")).IsFeasible);
    }
}
