using ProductManager.Domain.Common;
using ProductManager.Domain.Execution;

namespace ProductManager.Domain.Services;

/// <summary>A team is overallocated in a period: total demand exceeds capacity.</summary>
public sealed record CapacityWarning(
    Guid TeamId,
    Period Period,
    decimal CapacityPoints,
    decimal DemandPoints)
{
    public decimal OverallocationPoints => DemandPoints - CapacityPoints;

    /// <summary>True when the team has no capacity defined at all for the period (treated as zero).</summary>
    public bool CapacityUndefined { get; init; }
}

/// <summary>
/// Pure capacity feasibility check: per-team per-period effort demand across projects vs team
/// capacity. Capacity is a feasibility constraint — a plan whose demand exceeds capacity in a
/// period is flagged, not silently accepted.
/// </summary>
public static class CapacityFeasibilityService
{
    /// <summary>
    /// Computes overallocation warnings for a set of projects against a set of teams.
    /// A period with demand for a team but no defined capacity counts as capacity 0 and is
    /// flagged with <see cref="CapacityWarning.CapacityUndefined"/>.
    /// Demand for unknown teams (not in <paramref name="teams"/>) is also flagged as undefined capacity.
    /// </summary>
    public static IReadOnlyList<CapacityWarning> ComputeOverallocations(
        IEnumerable<Team> teams,
        IEnumerable<Project> projects)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(projects);

        var teamById = teams.ToDictionary(t => t.Id);

        var demandByTeamPeriod = projects
            .SelectMany(p => p.EffortDemands)
            .GroupBy(d => (d.TeamId, d.Period))
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Points));

        var warnings = new List<CapacityWarning>();
        foreach (var ((teamId, period), demand) in demandByTeamPeriod)
        {
            var hasTeam = teamById.TryGetValue(teamId, out var team);
            var hasCapacity = hasTeam && team!.Capacities.Any(c => c.Period == period);
            var capacity = hasCapacity ? team!.CapacityFor(period) : 0m;

            if (demand > capacity)
            {
                warnings.Add(new CapacityWarning(teamId, period, capacity, demand)
                {
                    CapacityUndefined = !hasCapacity
                });
            }
        }

        return warnings
            .OrderBy(w => w.Period)
            .ThenBy(w => w.TeamId)
            .ToList();
    }
}
