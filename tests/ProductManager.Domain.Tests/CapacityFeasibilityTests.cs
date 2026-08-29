using ProductManager.Domain.Common;
using ProductManager.Domain.Execution;
using ProductManager.Domain.Services;

namespace ProductManager.Domain.Tests;

public class CapacityFeasibilityTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid InitiativeId = Guid.NewGuid();
    private static readonly Period Jan = new(2026, 1);
    private static readonly Period Feb = new(2026, 2);

    private static Team TeamWith(params (Period Period, decimal Points)[] capacities)
    {
        var team = new Team(WorkspaceId, "Backend");
        foreach (var (period, points) in capacities)
            team.SetCapacity(period, points);
        return team;
    }

    [Fact]
    public void NoWarnings_WhenDemandIsWithinCapacity()
    {
        var team = TeamWith((Jan, 10m));
        var project = new Project(InitiativeId, "P1");
        project.SetEffortDemand(team.Id, Jan, 8m);

        var warnings = CapacityFeasibilityService.ComputeOverallocations([team], [project]);

        Assert.Empty(warnings);
    }

    [Fact]
    public void NoWarnings_WhenDemandExactlyEqualsCapacity()
    {
        var team = TeamWith((Jan, 10m));
        var project = new Project(InitiativeId, "P1");
        project.SetEffortDemand(team.Id, Jan, 10m);

        var warnings = CapacityFeasibilityService.ComputeOverallocations([team], [project]);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Warns_WhenSingleProjectExceedsCapacity()
    {
        var team = TeamWith((Jan, 10m));
        var project = new Project(InitiativeId, "P1");
        project.SetEffortDemand(team.Id, Jan, 12m);

        var warnings = CapacityFeasibilityService.ComputeOverallocations([team], [project]);

        var warning = Assert.Single(warnings);
        Assert.Equal(team.Id, warning.TeamId);
        Assert.Equal(Jan, warning.Period);
        Assert.Equal(10m, warning.CapacityPoints);
        Assert.Equal(12m, warning.DemandPoints);
        Assert.Equal(2m, warning.OverallocationPoints);
        Assert.False(warning.CapacityUndefined);
    }

    [Fact]
    public void AggregatesDemand_AcrossProjects_PerTeamPerPeriod()
    {
        var team = TeamWith((Jan, 10m));
        var p1 = new Project(InitiativeId, "P1");
        p1.SetEffortDemand(team.Id, Jan, 6m);
        var p2 = new Project(InitiativeId, "P2");
        p2.SetEffortDemand(team.Id, Jan, 6m);

        var warnings = CapacityFeasibilityService.ComputeOverallocations([team], [p1, p2]);

        var warning = Assert.Single(warnings);
        Assert.Equal(12m, warning.DemandPoints);
        Assert.Equal(2m, warning.OverallocationPoints);
    }

    [Fact]
    public void WarnsOnlyForOverallocatedPeriods_NotAllPeriods()
    {
        var team = TeamWith((Jan, 10m), (Feb, 10m));
        var project = new Project(InitiativeId, "P1");
        project.SetEffortDemand(team.Id, Jan, 15m);
        project.SetEffortDemand(team.Id, Feb, 5m);

        var warnings = CapacityFeasibilityService.ComputeOverallocations([team], [project]);

        var warning = Assert.Single(warnings);
        Assert.Equal(Jan, warning.Period);
    }

    [Fact]
    public void DemandInPeriodWithNoDefinedCapacity_IsFlaggedAsUndefinedCapacity()
    {
        var team = TeamWith((Jan, 10m)); // no Feb capacity
        var project = new Project(InitiativeId, "P1");
        project.SetEffortDemand(team.Id, Feb, 1m);

        var warnings = CapacityFeasibilityService.ComputeOverallocations([team], [project]);

        var warning = Assert.Single(warnings);
        Assert.Equal(Feb, warning.Period);
        Assert.Equal(0m, warning.CapacityPoints);
        Assert.True(warning.CapacityUndefined);
    }

    [Fact]
    public void DemandForUnknownTeam_IsFlaggedAsUndefinedCapacity()
    {
        var team = TeamWith((Jan, 10m));
        var ghostTeamId = Guid.NewGuid();
        var project = new Project(InitiativeId, "P1");
        project.SetEffortDemand(ghostTeamId, Jan, 3m);

        var warnings = CapacityFeasibilityService.ComputeOverallocations([team], [project]);

        var warning = Assert.Single(warnings);
        Assert.Equal(ghostTeamId, warning.TeamId);
        Assert.True(warning.CapacityUndefined);
    }

    [Fact]
    public void SeparateTeams_AreEvaluatedIndependently()
    {
        var backend = TeamWith((Jan, 10m));
        var frontend = new Team(WorkspaceId, "Frontend");
        frontend.SetCapacity(Jan, 4m);

        var project = new Project(InitiativeId, "P1");
        project.SetEffortDemand(backend.Id, Jan, 9m); // within
        project.SetEffortDemand(frontend.Id, Jan, 5m); // over

        var warnings = CapacityFeasibilityService.ComputeOverallocations([backend, frontend], [project]);

        var warning = Assert.Single(warnings);
        Assert.Equal(frontend.Id, warning.TeamId);
        Assert.Equal(1m, warning.OverallocationPoints);
    }

    [Fact]
    public void UpsertingEffortDemand_ReplacesRatherThanAccumulates()
    {
        var team = TeamWith((Jan, 10m));
        var project = new Project(InitiativeId, "P1");
        project.SetEffortDemand(team.Id, Jan, 20m);
        project.SetEffortDemand(team.Id, Jan, 5m); // corrected estimate

        var warnings = CapacityFeasibilityService.ComputeOverallocations([team], [project]);

        Assert.Empty(warnings);
    }

    [Fact]
    public void ZeroDemand_ProducesNoWarnings_EvenWithZeroCapacity()
    {
        var team = TeamWith((Jan, 0m));
        var project = new Project(InitiativeId, "P1");
        project.SetEffortDemand(team.Id, Jan, 0m); // removes the demand row

        var warnings = CapacityFeasibilityService.ComputeOverallocations([team], [project]);

        Assert.Empty(warnings);
    }

    [Fact]
    public void NegativeDemand_IsRejectedByTheDomain()
    {
        var project = new Project(InitiativeId, "P1");
        Assert.Throws<ArgumentOutOfRangeException>(() => project.SetEffortDemand(Guid.NewGuid(), Jan, -1m));
    }

    [Fact]
    public void NegativeCapacity_IsRejectedByTheDomain()
    {
        var team = new Team(WorkspaceId, "Backend");
        Assert.Throws<ArgumentOutOfRangeException>(() => team.SetCapacity(Jan, -1m));
    }
}
