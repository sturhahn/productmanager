using System.ComponentModel.DataAnnotations;
using ProductManager.Domain.Common;
using ProductManager.Domain.Execution;

namespace ProductManager.Api.Contracts;

// ---------- Workspace ----------

public sealed record WorkspaceDto(Guid Id, string Name, DateTimeOffset CreatedAt);

public sealed record CreateWorkspaceRequest([Required, MaxLength(200)] string Name);

public sealed record UpdateWorkspaceRequest([Required, MaxLength(200)] string Name);

// ---------- Period ----------

/// <summary>A planning period (calendar month) — the unit of capacity and effort demand.</summary>
public sealed record PeriodDto(
    [Range(2000, 2200)] int Year,
    [Range(1, 12)] int Month)
{
    public Period ToDomain() => new(Year, Month);

    public static PeriodDto From(Period period) => new(period.Year, period.Month);

    public override string ToString() => $"{Year:0000}-{Month:00}";
}

// ---------- Team ----------

public sealed record TeamCapacityDto(int Year, int Month, decimal Points);

public sealed record TeamDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? SpecializationTag,
    IReadOnlyList<TeamCapacityDto> Capacities);

public sealed record CreateTeamRequest(
    [Required] Guid WorkspaceId,
    [Required, MaxLength(200)] string Name,
    [MaxLength(100)] string? SpecializationTag);

public sealed record UpdateTeamRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(100)] string? SpecializationTag);

/// <summary>Upserts a team's capacity for one period. Points are FTE-months or story points — a unit choice made in data.</summary>
public sealed record SetTeamCapacityRequest(
    [Range(2000, 2200)] int Year,
    [Range(1, 12)] int Month,
    [Range(0, 1000000)] decimal Points);

// ---------- Initiative ----------

public sealed record InitiativeDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Outcome,
    string? HypothesizedValue,
    IReadOnlyList<Guid> TargetSegmentIds,
    IReadOnlyList<Guid> TargetNeedIds,
    StrategicIntent? StrategicIntent);

public sealed record CreateInitiativeRequest(
    [Required] Guid WorkspaceId,
    [Required, MaxLength(200)] string Name,
    string? Outcome,
    string? HypothesizedValue,
    IReadOnlyList<Guid>? TargetSegmentIds,
    IReadOnlyList<Guid>? TargetNeedIds,
    StrategicIntent? StrategicIntent = null);

public sealed record UpdateInitiativeRequest(
    [Required, MaxLength(200)] string Name,
    string? Outcome,
    string? HypothesizedValue,
    IReadOnlyList<Guid>? TargetSegmentIds,
    IReadOnlyList<Guid>? TargetNeedIds,
    StrategicIntent? StrategicIntent = null);

// ---------- Project ----------

public sealed record EffortDemandDto(Guid TeamId, int Year, int Month, decimal Points);

public sealed record ProjectDto(
    Guid Id,
    Guid InitiativeId,
    string Name,
    string? Description,
    ProjectStatus Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<Guid> DependsOnProjectIds,
    IReadOnlyList<Guid> DeliveredFeatureIds,
    IReadOnlyList<EffortDemandDto> EffortDemands);

public sealed record CreateProjectRequest(
    [Required] Guid InitiativeId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    ProjectStatus? Status,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record UpdateProjectRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    ProjectStatus? Status,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record AddProjectDependencyRequest([Required] Guid DependsOnProjectId);

/// <summary>Upserts the effort this project demands from one team in one period. Points of 0 clears the demand.</summary>
public sealed record SetEffortDemandRequest(
    [Required] Guid TeamId,
    [Range(2000, 2200)] int Year,
    [Range(1, 12)] int Month,
    [Range(0, 1000000)] decimal Points);

public sealed record LinkFeatureRequest([Required] Guid FeatureId);

// ---------- Mapping ----------

public static class ExecutionMapping
{
    public static WorkspaceDto ToDto(this Workspace w) => new(w.Id, w.Name, w.CreatedAt);

    public static TeamDto ToDto(this Team t) => new(
        t.Id, t.WorkspaceId, t.Name, t.SpecializationTag,
        t.Capacities
            .OrderBy(c => c.Period.Year).ThenBy(c => c.Period.Month)
            .Select(c => new TeamCapacityDto(c.Period.Year, c.Period.Month, c.Points))
            .ToList());

    public static InitiativeDto ToDto(this Initiative i) => new(
        i.Id, i.WorkspaceId, i.Name, i.Outcome, i.HypothesizedValue,
        i.TargetSegmentIds.ToList(), i.TargetNeedIds.ToList(), i.StrategicIntent);

    public static ProjectDto ToDto(this Project p) => new(
        p.Id, p.InitiativeId, p.Name, p.Description, p.Status, p.StartDate, p.EndDate,
        p.Dependencies.Select(d => d.DependsOnProjectId).ToList(),
        p.DeliveredFeatures.Select(f => f.FeatureId).ToList(),
        p.EffortDemands
            .OrderBy(d => d.Period.Year).ThenBy(d => d.Period.Month)
            .Select(d => new EffortDemandDto(d.TeamId, d.Period.Year, d.Period.Month, d.Points))
            .ToList());
}
