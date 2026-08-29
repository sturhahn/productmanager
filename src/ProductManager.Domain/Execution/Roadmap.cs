using ProductManager.Domain.Common;

namespace ProductManager.Domain.Execution;

/// <summary>
/// Branchable, versioned plan. The live roadmap is a living document; a scenario is a branch
/// (deep copy of plan links); adopting a scenario is a merge back to live.
/// </summary>
public sealed class Roadmap : Entity
{
    private readonly List<RoadmapItem> _items = [];

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>Optional named version label (e.g. "2026-Q3 board deck").</summary>
    public string? VersionName { get; set; }

    /// <summary>The roadmap this one was branched from; null for a root roadmap.</summary>
    public Guid? ParentRoadmapId { get; private set; }

    /// <summary>Exactly one live roadmap per workspace is the intended state (enforced at the application layer).</summary>
    public bool IsLive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public IReadOnlyCollection<RoadmapItem> Items => _items.AsReadOnly();

    private Roadmap() { } // EF

    public Roadmap(Guid workspaceId, string name, bool isLive = false)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        Rename(name);
        WorkspaceId = workspaceId;
        IsLive = isLive;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Roadmap name is required.", nameof(name));
        Name = name.Trim();
    }

    /// <summary>Adds or replaces the plan entry for a project.</summary>
    public RoadmapItem PlanProject(Guid projectId, DateOnly plannedStart, DateOnly plannedEnd, int? sortOrder = null)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("ProjectId is required.", nameof(projectId));
        if (plannedEnd < plannedStart)
            throw new ArgumentException("Planned end cannot precede planned start.");
        _items.RemoveAll(i => i.ProjectId == projectId);
        var item = new RoadmapItem(Id, projectId, plannedStart, plannedEnd,
            sortOrder ?? (_items.Count == 0 ? 0 : _items.Max(i => i.SortOrder) + 1));
        _items.Add(item);
        return item;
    }

    public void UnplanProject(Guid projectId) => _items.RemoveAll(i => i.ProjectId == projectId);

    /// <summary>
    /// Branch this roadmap into a scenario: a new (non-live) roadmap whose plan links are a
    /// deep copy of this one's, with <see cref="ParentRoadmapId"/> pointing back here.
    /// </summary>
    public Roadmap Branch(string branchName)
    {
        var branch = new Roadmap(WorkspaceId, branchName, isLive: false)
        {
            ParentRoadmapId = Id
        };
        foreach (var item in _items.OrderBy(i => i.SortOrder))
            branch._items.Add(new RoadmapItem(branch.Id, item.ProjectId, item.PlannedStart, item.PlannedEnd, item.SortOrder));
        return branch;
    }

    /// <summary>
    /// Adopt a branch: replace this (live) roadmap's plan links with deep copies of the branch's.
    /// Only a live roadmap can adopt, and only from a branch in the same workspace.
    /// </summary>
    public void AdoptBranch(Roadmap branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (!IsLive)
            throw new InvalidOperationException("Only the live roadmap can adopt a branch; merge target must be live.");
        if (branch.IsLive)
            throw new InvalidOperationException("Cannot adopt the live roadmap into itself.");
        if (branch.WorkspaceId != WorkspaceId)
            throw new InvalidOperationException("Cannot adopt a branch from a different workspace.");
        _items.Clear();
        foreach (var item in branch._items.OrderBy(i => i.SortOrder))
            _items.Add(new RoadmapItem(Id, item.ProjectId, item.PlannedStart, item.PlannedEnd, item.SortOrder));
    }

    public void MakeLive() => IsLive = true;

    public void RetireFromLive() => IsLive = false;
}

/// <summary>A plan link: this roadmap schedules this project on these dates, in this display order.</summary>
public sealed class RoadmapItem : Entity
{
    public Guid RoadmapId { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateOnly PlannedStart { get; private set; }
    public DateOnly PlannedEnd { get; private set; }
    public int SortOrder { get; private set; }

    private RoadmapItem() { } // EF

    public RoadmapItem(Guid roadmapId, Guid projectId, DateOnly plannedStart, DateOnly plannedEnd, int sortOrder)
    {
        if (plannedEnd < plannedStart)
            throw new ArgumentException("Planned end cannot precede planned start.");
        RoadmapId = roadmapId;
        ProjectId = projectId;
        PlannedStart = plannedStart;
        PlannedEnd = plannedEnd;
        SortOrder = sortOrder;
    }

    public void Reschedule(DateOnly plannedStart, DateOnly plannedEnd)
    {
        if (plannedEnd < plannedStart)
            throw new ArgumentException("Planned end cannot precede planned start.");
        PlannedStart = plannedStart;
        PlannedEnd = plannedEnd;
    }

    public void Reorder(int sortOrder) => SortOrder = sortOrder;
}
