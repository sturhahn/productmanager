using ProductManager.Domain.Common;
using ProductManager.Domain.Execution;

namespace ProductManager.Domain.Tests;

public class RoadmapBranchingTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static Roadmap LiveRoadmapWithTwoProjects(out Guid p1, out Guid p2)
    {
        p1 = Guid.NewGuid();
        p2 = Guid.NewGuid();
        var live = new Roadmap(WorkspaceId, "Live", isLive: true);
        live.PlanProject(p1, D(1, 1), D(2, 28));
        live.PlanProject(p2, D(3, 1), D(4, 30));
        return live;
    }

    [Fact]
    public void Branch_DeepCopiesPlanLinks()
    {
        var live = LiveRoadmapWithTwoProjects(out var p1, out var p2);

        var branch = live.Branch("Scenario: accelerate");

        Assert.Equal(2, branch.Items.Count);
        Assert.Equal(new[] { p1, p2 },
            branch.Items.OrderBy(i => i.SortOrder).Select(i => i.ProjectId).ToArray());
        // Deep copy: distinct item identities, same plan content.
        Assert.Empty(branch.Items.Select(i => i.Id).Intersect(live.Items.Select(i => i.Id)));
        Assert.All(branch.Items, i => Assert.Equal(branch.Id, i.RoadmapId));
    }

    [Fact]
    public void Branch_IsNotLive_AndPointsAtItsParent()
    {
        var live = LiveRoadmapWithTwoProjects(out _, out _);

        var branch = live.Branch("Scenario");

        Assert.False(branch.IsLive);
        Assert.Equal(live.Id, branch.ParentRoadmapId);
        Assert.Equal(live.WorkspaceId, branch.WorkspaceId);
    }

    [Fact]
    public void EditingABranch_DoesNotTouchTheLiveRoadmap()
    {
        var live = LiveRoadmapWithTwoProjects(out var p1, out _);

        var branch = live.Branch("Scenario");
        branch.Items.First(i => i.ProjectId == p1).Reschedule(D(6, 1), D(7, 31));
        branch.PlanProject(Guid.NewGuid(), D(8, 1), D(9, 30));

        var liveItem = live.Items.First(i => i.ProjectId == p1);
        Assert.Equal(D(1, 1), liveItem.PlannedStart);
        Assert.Equal(2, live.Items.Count);
        Assert.Equal(3, branch.Items.Count);
    }

    [Fact]
    public void AdoptBranch_ReplacesLivePlanLinks_WithDeepCopies()
    {
        var live = LiveRoadmapWithTwoProjects(out var p1, out var p2);
        var branch = live.Branch("Scenario");
        branch.UnplanProject(p2);
        branch.Items.First(i => i.ProjectId == p1).Reschedule(D(5, 1), D(6, 30));
        var p3 = Guid.NewGuid();
        branch.PlanProject(p3, D(7, 1), D(8, 31));

        live.AdoptBranch(branch);

        Assert.Equal(2, live.Items.Count);
        var adopted = live.Items.First(i => i.ProjectId == p1);
        Assert.Equal(D(5, 1), adopted.PlannedStart);
        Assert.Contains(live.Items, i => i.ProjectId == p3);
        Assert.DoesNotContain(live.Items, i => i.ProjectId == p2);
        // Deep copy on merge too: live items belong to live, with fresh identities.
        Assert.All(live.Items, i => Assert.Equal(live.Id, i.RoadmapId));
        Assert.Empty(live.Items.Select(i => i.Id).Intersect(branch.Items.Select(i => i.Id)));
        Assert.True(live.IsLive);
    }

    [Fact]
    public void AdoptBranch_IntoANonLiveRoadmap_IsRejected()
    {
        var live = LiveRoadmapWithTwoProjects(out _, out _);
        var branch1 = live.Branch("S1");
        var branch2 = live.Branch("S2");

        Assert.Throws<DomainConflictException>(() => branch1.AdoptBranch(branch2));
    }

    [Fact]
    public void AdoptingTheLiveRoadmapItself_IsRejected()
    {
        var live = LiveRoadmapWithTwoProjects(out _, out _);

        Assert.Throws<DomainConflictException>(() => live.AdoptBranch(live));
    }

    [Fact]
    public void AdoptingABranchFromAnotherWorkspace_IsRejected()
    {
        var live = LiveRoadmapWithTwoProjects(out _, out _);
        var foreign = new Roadmap(Guid.NewGuid(), "Other workspace scenario");

        Assert.Throws<DomainConflictException>(() => live.AdoptBranch(foreign));
    }

    [Fact]
    public void PlanProject_UpsertsThePlanLinkForAProject()
    {
        var roadmap = new Roadmap(WorkspaceId, "Live");
        var p = Guid.NewGuid();
        roadmap.PlanProject(p, D(1, 1), D(2, 1));
        roadmap.PlanProject(p, D(3, 1), D(4, 1)); // reschedule via re-plan

        var item = Assert.Single(roadmap.Items);
        Assert.Equal(D(3, 1), item.PlannedStart);
    }

    [Fact]
    public void PlanProject_RejectsEndBeforeStart()
    {
        var roadmap = new Roadmap(WorkspaceId, "Live");
        Assert.Throws<ArgumentException>(() => roadmap.PlanProject(Guid.NewGuid(), D(5, 1), D(4, 1)));
    }
}
