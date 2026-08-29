using System.ComponentModel.DataAnnotations;
using ProductManager.Domain.Execution;
using ProductManager.Domain.Services;

namespace ProductManager.Api.Contracts;

public sealed record RoadmapItemDto(
    Guid Id,
    Guid RoadmapId,
    Guid ProjectId,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    int SortOrder);

public sealed record RoadmapDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? VersionName,
    Guid? ParentRoadmapId,
    bool IsLive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RoadmapItemDto> Items);

public sealed record CreateRoadmapRequest(
    [Required] Guid WorkspaceId,
    [Required, MaxLength(200)] string Name,
    [MaxLength(200)] string? VersionName,
    bool IsLive = false);

public sealed record UpdateRoadmapRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(200)] string? VersionName);

/// <summary>Plans (or re-plans) a project on the roadmap.</summary>
public sealed record PlanProjectRequest(
    [Required] Guid ProjectId,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    int? SortOrder);

/// <summary>Moves a planned project on the timeline (the drag-and-drop reschedule).</summary>
public sealed record RescheduleItemRequest(
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    int? SortOrder);

/// <summary>Reorders the roadmap: sort order becomes the position in this list (the drag-and-drop reorder).</summary>
public sealed record ReorderRoadmapRequest([Required, MinLength(1)] IReadOnlyList<Guid> ProjectIds);

public sealed record BranchRoadmapRequest([Required, MaxLength(200)] string Name);

/// <summary>Merges a scenario branch into the live roadmap.</summary>
public sealed record AdoptBranchRequest([Required] Guid BranchRoadmapId);

// ---------- Feasibility ----------

public sealed record CapacityWarningDto(
    Guid TeamId,
    string? TeamName,
    int Year,
    int Month,
    decimal CapacityPoints,
    decimal DemandPoints,
    decimal OverallocationPoints,
    bool CapacityUndefined);

public sealed record SequencingViolationDto(
    SequencingViolationKind Kind,
    Guid ProjectId,
    Guid DependsOnProjectId,
    string Description);

/// <summary>Feasibility of a roadmap: capacity overallocations and dependency-ordering problems.</summary>
public sealed record RoadmapFeasibilityDto(
    Guid RoadmapId,
    bool IsFeasible,
    IReadOnlyList<CapacityWarningDto> CapacityWarnings,
    IReadOnlyList<SequencingViolationDto> SequencingViolations);

public static class RoadmapMapping
{
    public static RoadmapItemDto ToDto(this RoadmapItem i) =>
        new(i.Id, i.RoadmapId, i.ProjectId, i.PlannedStart, i.PlannedEnd, i.SortOrder);

    public static RoadmapDto ToDto(this Roadmap r) => new(
        r.Id, r.WorkspaceId, r.Name, r.VersionName, r.ParentRoadmapId, r.IsLive, r.CreatedAt,
        r.Items.OrderBy(i => i.SortOrder).Select(ToDto).ToList());

    public static CapacityWarningDto ToDto(this CapacityWarning w, string? teamName) => new(
        w.TeamId, teamName, w.Period.Year, w.Period.Month,
        w.CapacityPoints, w.DemandPoints, w.OverallocationPoints, w.CapacityUndefined);

    public static SequencingViolationDto ToDto(this SequencingViolation v) =>
        new(v.Kind, v.ProjectId, v.DependsOnProjectId, v.Description);
}
