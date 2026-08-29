using System.Net;
using System.Net.Http.Json;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;

namespace ProductManager.Api.Tests;

public sealed class RoadmapTests : ApiTestBase
{
    [Fact]
    public async Task Projects_can_be_planned_rescheduled_and_unplanned()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var project = await CreateProjectAsync(initiative.Id, "Ledger rewrite");
        var roadmap = await CreateRoadmapAsync(workspace.Id, "2026 plan", isLive: true);

        var planned = await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
            new PlanProjectRequest(project.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null));
        var item = Assert.Single(planned.Items);
        Assert.Equal(project.Id, item.ProjectId);
        Assert.Equal(0, item.SortOrder);

        var moved = await PutAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items/{project.Id}",
            new RescheduleItemRequest(new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30), null));
        Assert.Equal(new DateOnly(2026, 2, 1), moved.Items[0].PlannedStart);
        Assert.Equal(new DateOnly(2026, 4, 30), moved.Items[0].PlannedEnd);

        var backwards = await Client.PutAsJsonAsync($"/api/roadmaps/{roadmap.Id}/items/{project.Id}",
            new RescheduleItemRequest(new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1), null), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);

        var unplanned = await Client.DeleteAsync($"/api/roadmaps/{roadmap.Id}/items/{project.Id}");
        unplanned.EnsureSuccessStatusCode();
        Assert.Empty((await GetAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}")).Items);
    }

    [Fact]
    public async Task Drag_and_drop_reorder_rewrites_the_sort_order()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var roadmap = await CreateRoadmapAsync(workspace.Id, "2026 plan");

        var a = await CreateProjectAsync(initiative.Id, "A");
        var b = await CreateProjectAsync(initiative.Id, "B");
        var c = await CreateProjectAsync(initiative.Id, "C");

        foreach (var project in new[] { a, b, c })
            await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
                new PlanProjectRequest(project.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), null));

        var initial = await GetAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}");
        Assert.Equal([a.Id, b.Id, c.Id], initial.Items.Select(i => i.ProjectId));

        var reordered = await PutAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/order",
            new ReorderRoadmapRequest([c.Id, a.Id, b.Id]));

        Assert.Equal([c.Id, a.Id, b.Id], reordered.Items.Select(i => i.ProjectId));
        Assert.Equal([0, 1, 2], reordered.Items.Select(i => i.SortOrder));

        // The new order survives a round trip.
        var refetched = await GetAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}");
        Assert.Equal([c.Id, a.Id, b.Id], refetched.Items.Select(i => i.ProjectId));
    }

    [Fact]
    public async Task Reorder_rejects_a_partial_or_unknown_order()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var roadmap = await CreateRoadmapAsync(workspace.Id, "2026 plan");
        var a = await CreateProjectAsync(initiative.Id, "A");
        var b = await CreateProjectAsync(initiative.Id, "B");

        foreach (var project in new[] { a, b })
            await PostAsync<RoadmapDto>($"/api/roadmaps/{roadmap.Id}/items",
                new PlanProjectRequest(project.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), null));

        var partial = await Client.PutAsJsonAsync($"/api/roadmaps/{roadmap.Id}/order",
            new ReorderRoadmapRequest([a.Id]), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);

        var unknown = await Client.PutAsJsonAsync($"/api/roadmaps/{roadmap.Id}/order",
            new ReorderRoadmapRequest([a.Id, b.Id, Guid.NewGuid()]), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task A_branch_is_an_isolated_deep_copy_and_adopting_it_merges_back()
    {
        var workspace = await DefaultWorkspaceAsync();
        var initiative = await CreateInitiativeAsync(workspace.Id, "Platform");
        var kept = await CreateProjectAsync(initiative.Id, "Kept");
        var added = await CreateProjectAsync(initiative.Id, "Added in the scenario");

        var live = await CreateRoadmapAsync(workspace.Id, "Live plan", isLive: true);
        await PostAsync<RoadmapDto>($"/api/roadmaps/{live.Id}/items",
            new PlanProjectRequest(kept.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), null));

        var branch = await PostAsync<RoadmapDto>($"/api/roadmaps/{live.Id}/branch",
            new BranchRoadmapRequest("What if we pull Added forward"));

        Assert.False(branch.IsLive);
        Assert.Equal(live.Id, branch.ParentRoadmapId);
        Assert.Equal([kept.Id], branch.Items.Select(i => i.ProjectId));
        Assert.NotEqual(live.Id, branch.Id);

        // Editing the branch leaves the live roadmap untouched.
        await PostAsync<RoadmapDto>($"/api/roadmaps/{branch.Id}/items",
            new PlanProjectRequest(added.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 1), null));
        await PutAsync<RoadmapDto>($"/api/roadmaps/{branch.Id}/items/{kept.Id}",
            new RescheduleItemRequest(new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 1), null));

        var liveBeforeAdoption = await GetAsync<RoadmapDto>($"/api/roadmaps/{live.Id}");
        Assert.Equal([kept.Id], liveBeforeAdoption.Items.Select(i => i.ProjectId));
        Assert.Equal(new DateOnly(2026, 1, 1), liveBeforeAdoption.Items[0].PlannedStart);

        var adopted = await PostAsync<RoadmapDto>($"/api/roadmaps/{live.Id}/adopt",
            new AdoptBranchRequest(branch.Id));

        Assert.True(adopted.IsLive);
        Assert.Equal(2, adopted.Items.Count);
        Assert.Equal([kept.Id, added.Id], adopted.Items.Select(i => i.ProjectId));
        Assert.Equal(new DateOnly(2026, 2, 1), adopted.Items.First(i => i.ProjectId == kept.Id).PlannedStart);

        // The adopted items are copies: the branch keeps its own item identities.
        var branchAfter = await GetAsync<RoadmapDto>($"/api/roadmaps/{branch.Id}");
        Assert.Equal(2, branchAfter.Items.Count);
        Assert.Empty(branchAfter.Items.Select(i => i.Id).Intersect(adopted.Items.Select(i => i.Id)));

        var persisted = await GetAsync<RoadmapDto>($"/api/roadmaps/{live.Id}");
        Assert.Equal(2, persisted.Items.Count);
    }

    [Fact]
    public async Task Only_the_live_roadmap_can_adopt()
    {
        var workspace = await DefaultWorkspaceAsync();
        var draft = await CreateRoadmapAsync(workspace.Id, "Draft");
        var branch = await PostAsync<RoadmapDto>($"/api/roadmaps/{draft.Id}/branch",
            new BranchRoadmapRequest("Scenario"));

        var response = await Client.PostAsJsonAsync($"/api/roadmaps/{draft.Id}/adopt",
            new AdoptBranchRequest(branch.Id), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Making_a_roadmap_live_retires_the_previous_one()
    {
        var workspace = await DefaultWorkspaceAsync();
        var first = await CreateRoadmapAsync(workspace.Id, "First", isLive: true);
        var second = await CreateRoadmapAsync(workspace.Id, "Second");

        var promoted = await PostAsync<RoadmapDto>($"/api/roadmaps/{second.Id}/make-live", new { });

        Assert.True(promoted.IsLive);
        Assert.False((await GetAsync<RoadmapDto>($"/api/roadmaps/{first.Id}")).IsLive);

        var liveOnly = await GetAsync<PagedResponse<RoadmapDto>>(
            $"/api/roadmaps?workspaceId={workspace.Id}&isLive=true");
        Assert.Equal(1, liveOnly.TotalCount);
        Assert.Equal(second.Id, liveOnly.Items[0].Id);
    }

    [Fact]
    public async Task The_live_roadmap_cannot_be_deleted()
    {
        var workspace = await DefaultWorkspaceAsync();
        var live = await CreateRoadmapAsync(workspace.Id, "Live", isLive: true);

        var response = await Client.DeleteAsync($"/api/roadmaps/{live.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
