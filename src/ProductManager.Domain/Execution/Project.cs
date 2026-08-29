using ProductManager.Domain.Common;

namespace ProductManager.Domain.Execution;

/// <summary>
/// Unit of execution. Belongs to an Initiative; *delivers* Features (the seam).
/// Carries dates, per-Team per-period effort demand, and dependencies on other Projects.
/// </summary>
public sealed class Project : Entity
{
    private readonly List<ProjectDependency> _dependencies = [];
    private readonly List<EffortDemand> _effortDemands = [];
    private readonly List<ProjectFeature> _deliveredFeatures = [];

    public Guid InitiativeId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; set; }
    public ProjectStatus Status { get; private set; } = ProjectStatus.Proposed;
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    /// <summary>The market/execution seam: features this project delivers.</summary>
    public IReadOnlyCollection<ProjectFeature> DeliveredFeatures => _deliveredFeatures.AsReadOnly();

    public IReadOnlyCollection<ProjectDependency> Dependencies => _dependencies.AsReadOnly();
    public IReadOnlyCollection<EffortDemand> EffortDemands => _effortDemands.AsReadOnly();

    private Project() { } // EF

    public Project(Guid initiativeId, string name, DateOnly? startDate = null, DateOnly? endDate = null)
    {
        if (initiativeId == Guid.Empty) throw new ArgumentException("InitiativeId is required.", nameof(initiativeId));
        Rename(name);
        InitiativeId = initiativeId;
        SetDates(startDate, endDate);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetDates(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
            throw new ArgumentException("Project end date cannot precede its start date.");
        StartDate = startDate;
        EndDate = endDate;
    }

    public void SetStatus(ProjectStatus status) => Status = status;

    public void AddDependency(Guid dependsOnProjectId)
    {
        if (dependsOnProjectId == Guid.Empty)
            throw new ArgumentException("Dependency project id is required.", nameof(dependsOnProjectId));
        if (dependsOnProjectId == Id)
            throw new InvalidOperationException("A project cannot depend on itself.");
        if (_dependencies.Any(d => d.DependsOnProjectId == dependsOnProjectId))
            return; // idempotent
        _dependencies.Add(new ProjectDependency(Id, dependsOnProjectId));
    }

    public void RemoveDependency(Guid dependsOnProjectId) =>
        _dependencies.RemoveAll(d => d.DependsOnProjectId == dependsOnProjectId);

    /// <summary>Sets (upserting) the estimated effort this project demands from a team in a period.</summary>
    public void SetEffortDemand(Guid teamId, Period period, decimal points)
    {
        if (teamId == Guid.Empty) throw new ArgumentException("TeamId is required.", nameof(teamId));
        if (points < 0) throw new ArgumentOutOfRangeException(nameof(points), "Effort demand cannot be negative.");
        var existing = _effortDemands.FirstOrDefault(d => d.TeamId == teamId && d.Period == period);
        if (existing is not null) _effortDemands.Remove(existing);
        if (points > 0)
            _effortDemands.Add(new EffortDemand(teamId, period, points));
    }

    public void LinkFeature(Guid featureId)
    {
        if (featureId == Guid.Empty) throw new ArgumentException("FeatureId is required.", nameof(featureId));
        if (_deliveredFeatures.All(f => f.FeatureId != featureId))
            _deliveredFeatures.Add(new ProjectFeature(Id, featureId));
    }

    public void UnlinkFeature(Guid featureId) => _deliveredFeatures.RemoveAll(f => f.FeatureId == featureId);
}

/// <summary>The one seam between execution and market sides: Project delivers Feature.</summary>
public sealed class ProjectFeature
{
    public Guid ProjectId { get; private set; }
    public Guid FeatureId { get; private set; }

    private ProjectFeature() { } // EF

    public ProjectFeature(Guid projectId, Guid featureId)
    {
        ProjectId = projectId;
        FeatureId = featureId;
    }
}

/// <summary>Directed dependency edge: the owning project cannot start before the dependency finishes.</summary>
public sealed class ProjectDependency
{
    public Guid ProjectId { get; private set; }
    public Guid DependsOnProjectId { get; private set; }

    private ProjectDependency() { } // EF

    public ProjectDependency(Guid projectId, Guid dependsOnProjectId)
    {
        ProjectId = projectId;
        DependsOnProjectId = dependsOnProjectId;
    }
}

/// <summary>Estimated effort demanded from one team in one period. Owned by Project.</summary>
public sealed class EffortDemand
{
    public Guid TeamId { get; private set; }
    public Period Period { get; private set; } = null!;
    public decimal Points { get; private set; }

    private EffortDemand() { } // EF

    public EffortDemand(Guid teamId, Period period, decimal points)
    {
        TeamId = teamId;
        Period = period ?? throw new ArgumentNullException(nameof(period));
        if (points < 0) throw new ArgumentOutOfRangeException(nameof(points));
        Points = points;
    }
}
