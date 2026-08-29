using ProductManager.Domain.Execution;
using ProductManager.Domain.Services;

namespace ProductManager.Domain.Tests;

public class RoadmapSequencingTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid InitiativeId = Guid.NewGuid();

    private static DateOnly D(int month, int day) => new(2026, month, day);

    [Fact]
    public void NoViolations_WhenDependentStartsAfterDependencyEnds()
    {
        var a = new Project(InitiativeId, "A");
        var b = new Project(InitiativeId, "B");
        b.AddDependency(a.Id);

        var roadmap = new Roadmap(WorkspaceId, "Live", isLive: true);
        roadmap.PlanProject(a.Id, D(1, 1), D(2, 28));
        roadmap.PlanProject(b.Id, D(3, 1), D(4, 30));

        Assert.Empty(RoadmapSequencingService.DetectViolations(roadmap, [a, b]));
    }

    [Fact]
    public void NoViolations_WhenDependentStartsExactlyAtDependencyEnd()
    {
        var a = new Project(InitiativeId, "A");
        var b = new Project(InitiativeId, "B");
        b.AddDependency(a.Id);

        var roadmap = new Roadmap(WorkspaceId, "Live");
        roadmap.PlanProject(a.Id, D(1, 1), D(2, 28));
        roadmap.PlanProject(b.Id, D(2, 28), D(4, 30));

        Assert.Empty(RoadmapSequencingService.DetectViolations(roadmap, [a, b]));
    }

    [Fact]
    public void Violation_WhenDependentStartsBeforeDependencyEnds()
    {
        var a = new Project(InitiativeId, "A");
        var b = new Project(InitiativeId, "B");
        b.AddDependency(a.Id);

        var roadmap = new Roadmap(WorkspaceId, "Live");
        roadmap.PlanProject(a.Id, D(1, 1), D(3, 31));
        roadmap.PlanProject(b.Id, D(2, 1), D(4, 30)); // overlaps its dependency

        var violation = Assert.Single(RoadmapSequencingService.DetectViolations(roadmap, [a, b]));
        Assert.Equal(SequencingViolationKind.StartsBeforeDependencyEnds, violation.Kind);
        Assert.Equal(b.Id, violation.ProjectId);
        Assert.Equal(a.Id, violation.DependsOnProjectId);
    }

    [Fact]
    public void Violation_WhenDependencyIsNotPlannedOnRoadmap()
    {
        var a = new Project(InitiativeId, "A");
        var b = new Project(InitiativeId, "B");
        b.AddDependency(a.Id);

        var roadmap = new Roadmap(WorkspaceId, "Live");
        roadmap.PlanProject(b.Id, D(1, 1), D(2, 1)); // A missing from the plan

        var violation = Assert.Single(RoadmapSequencingService.DetectViolations(roadmap, [a, b]));
        Assert.Equal(SequencingViolationKind.DependencyNotOnRoadmap, violation.Kind);
        Assert.Equal(b.Id, violation.ProjectId);
        Assert.Equal(a.Id, violation.DependsOnProjectId);
    }

    [Fact]
    public void CycleBetweenPlannedProjects_IsReportedOnEveryEdgeOfTheCycle()
    {
        var a = new Project(InitiativeId, "A");
        var b = new Project(InitiativeId, "B");
        var c = new Project(InitiativeId, "C");
        a.AddDependency(b.Id);
        b.AddDependency(c.Id);
        c.AddDependency(a.Id);

        var roadmap = new Roadmap(WorkspaceId, "Live");
        roadmap.PlanProject(a.Id, D(1, 1), D(1, 31));
        roadmap.PlanProject(b.Id, D(2, 1), D(2, 28));
        roadmap.PlanProject(c.Id, D(3, 1), D(3, 31));

        var violations = RoadmapSequencingService.DetectViolations(roadmap, [a, b, c]);

        Assert.Equal(3, violations.Count(v => v.Kind == SequencingViolationKind.DependencyCycle));
    }

    [Fact]
    public void ProjectsOutsideTheCycle_AreStillCheckedNormally()
    {
        var a = new Project(InitiativeId, "A");
        var b = new Project(InitiativeId, "B");
        a.AddDependency(b.Id);
        b.AddDependency(a.Id); // cycle A<->B

        var c = new Project(InitiativeId, "C");
        var d = new Project(InitiativeId, "D");
        d.AddDependency(c.Id); // healthy edge, but mis-ordered

        var roadmap = new Roadmap(WorkspaceId, "Live");
        roadmap.PlanProject(a.Id, D(1, 1), D(1, 31));
        roadmap.PlanProject(b.Id, D(2, 1), D(2, 28));
        roadmap.PlanProject(c.Id, D(3, 1), D(3, 31));
        roadmap.PlanProject(d.Id, D(1, 1), D(1, 15)); // starts before C ends

        var violations = RoadmapSequencingService.DetectViolations(roadmap, [a, b, c, d]);

        Assert.Equal(2, violations.Count(v => v.Kind == SequencingViolationKind.DependencyCycle));
        var ordering = Assert.Single(violations, v => v.Kind == SequencingViolationKind.StartsBeforeDependencyEnds);
        Assert.Equal(d.Id, ordering.ProjectId);
    }

    [Fact]
    public void DependenciesOfUnplannedProjects_AreIgnored()
    {
        var a = new Project(InitiativeId, "A");
        var b = new Project(InitiativeId, "B");
        b.AddDependency(a.Id); // B is not on the roadmap at all

        var roadmap = new Roadmap(WorkspaceId, "Live");
        roadmap.PlanProject(a.Id, D(1, 1), D(1, 31));

        Assert.Empty(RoadmapSequencingService.DetectViolations(roadmap, [a, b]));
    }

    [Fact]
    public void LongDependencyChain_InOrder_HasNoViolations()
    {
        var projects = new List<Project>();
        var roadmap = new Roadmap(WorkspaceId, "Live");
        Project? previous = null;
        for (var i = 0; i < 50; i++)
        {
            var p = new Project(InitiativeId, $"P{i}");
            if (previous is not null) p.AddDependency(previous.Id);
            var start = new DateOnly(2026, 1, 1).AddDays(i * 10);
            roadmap.PlanProject(p.Id, start, start.AddDays(9));
            projects.Add(p);
            previous = p;
        }

        Assert.Empty(RoadmapSequencingService.DetectViolations(roadmap, projects));
    }

    [Fact]
    public void SelfDependency_IsRejectedByTheDomain()
    {
        var a = new Project(InitiativeId, "A");
        Assert.Throws<InvalidOperationException>(() => a.AddDependency(a.Id));
    }
}
