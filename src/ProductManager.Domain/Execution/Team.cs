using ProductManager.Domain.Common;

namespace ProductManager.Domain.Execution;

/// <summary>
/// Capacity budget unit. No per-person allocation, no skills model — capacity is a
/// feasibility constraint per period.
/// </summary>
public sealed class Team : Entity
{
    private readonly List<TeamCapacity> _capacities = [];

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>Coarse specialization tag (e.g. "backend", "quant"). Free text — data, not schema.</summary>
    public string? SpecializationTag { get; set; }

    public IReadOnlyCollection<TeamCapacity> Capacities => _capacities.AsReadOnly();

    private Team() { } // EF

    public Team(Guid workspaceId, string name, string? specializationTag = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Team name is required.", nameof(name));
        WorkspaceId = workspaceId;
        Name = name.Trim();
        SpecializationTag = specializationTag;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Team name is required.", nameof(name));
        Name = name.Trim();
    }

    /// <summary>Sets (upserting) the capacity for a period. Points are FTE-months or story points — a unit choice made in data.</summary>
    public void SetCapacity(Period period, decimal points)
    {
        if (points < 0) throw new ArgumentOutOfRangeException(nameof(points), "Capacity cannot be negative.");
        var existing = _capacities.FirstOrDefault(c => c.Period == period);
        if (existing is not null) _capacities.Remove(existing);
        _capacities.Add(new TeamCapacity(period, points));
    }

    public decimal CapacityFor(Period period) =>
        _capacities.FirstOrDefault(c => c.Period == period)?.Points ?? 0m;
}

/// <summary>Capacity of a team in one period. Owned by Team.</summary>
public sealed class TeamCapacity
{
    public Period Period { get; private set; } = null!;
    public decimal Points { get; private set; }

    private TeamCapacity() { } // EF

    public TeamCapacity(Period period, decimal points)
    {
        Period = period ?? throw new ArgumentNullException(nameof(period));
        if (points < 0) throw new ArgumentOutOfRangeException(nameof(points));
        Points = points;
    }
}
