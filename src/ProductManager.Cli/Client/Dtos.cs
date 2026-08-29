namespace ProductManager.Cli.Client;

// Client-side mirrors of src/ProductManager.Api/Contracts/*.cs. Property names are PascalCase
// here and mapped to the API's camelCase wire format by the JsonSerializerOptions in
// ProductManagerApiClient (CamelCase naming policy + case-insensitive reads).

// ---------- Paging ----------

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages { get; init; }
}

// ---------- Auth ----------

public sealed record TokenRequest(string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

// ---------- Workspace ----------

public sealed record WorkspaceDto(Guid Id, string Name, DateTimeOffset CreatedAt);

public sealed record CreateWorkspaceRequest(string Name);

public sealed record UpdateWorkspaceRequest(string Name);

// ---------- Team ----------

public sealed record TeamCapacityDto(int Year, int Month, decimal Points);

public sealed record TeamDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? SpecializationTag,
    IReadOnlyList<TeamCapacityDto> Capacities);

public sealed record CreateTeamRequest(Guid WorkspaceId, string Name, string? SpecializationTag);

public sealed record UpdateTeamRequest(string Name, string? SpecializationTag);

public sealed record SetTeamCapacityRequest(int Year, int Month, decimal Points);

// ---------- Initiative ----------

public sealed record InitiativeDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Outcome,
    string? HypothesizedValue,
    IReadOnlyList<Guid> TargetSegmentIds,
    IReadOnlyList<Guid> TargetNeedIds);

public sealed record CreateInitiativeRequest(
    Guid WorkspaceId,
    string Name,
    string? Outcome,
    string? HypothesizedValue,
    IReadOnlyList<Guid>? TargetSegmentIds,
    IReadOnlyList<Guid>? TargetNeedIds);

public sealed record UpdateInitiativeRequest(
    string Name,
    string? Outcome,
    string? HypothesizedValue,
    IReadOnlyList<Guid>? TargetSegmentIds,
    IReadOnlyList<Guid>? TargetNeedIds);

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
    Guid InitiativeId,
    string Name,
    string? Description,
    ProjectStatus? Status,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record UpdateProjectRequest(
    string Name,
    string? Description,
    ProjectStatus? Status,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record AddProjectDependencyRequest(Guid DependsOnProjectId);

public sealed record SetEffortDemandRequest(Guid TeamId, int Year, int Month, decimal Points);

public sealed record LinkFeatureRequest(Guid FeatureId);

// ---------- Feature (the seam) ----------

public sealed record FeatureDeltaDto(
    FeatureDeltaKind Kind,
    Guid? BusinessProcessId,
    CoverageTier? FromTier,
    CoverageTier? ToTier,
    Guid? ApproachId,
    Guid? SegmentId,
    double? Magnitude,
    string? Rationale);

public sealed record FeatureDto(
    Guid Id,
    Guid WorkspaceId,
    Guid? ProductId,
    string Name,
    string? Description,
    FeatureStatus Status,
    DateOnly? ShippedDate,
    IReadOnlyList<FeatureDeltaDto> Deltas);

public sealed record CreateFeatureRequest(Guid WorkspaceId, string Name, string? Description, Guid? ProductId);

public sealed record UpdateFeatureRequest(
    string Name,
    string? Description,
    Guid? ProductId,
    FeatureStatus? Status,
    DateOnly? ShippedDate);

public sealed record AddFeatureDeltaRequest(
    FeatureDeltaKind Kind,
    Guid? BusinessProcessId,
    CoverageTier? FromTier,
    CoverageTier? ToTier,
    Guid? ApproachId,
    Guid? SegmentId,
    double? Magnitude,
    string? Rationale);

// ---------- Roadmap ----------

public sealed record RoadmapItemDto(Guid Id, Guid RoadmapId, Guid ProjectId, DateOnly PlannedStart, DateOnly PlannedEnd, int SortOrder);

public sealed record RoadmapDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? VersionName,
    Guid? ParentRoadmapId,
    bool IsLive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RoadmapItemDto> Items);

public sealed record CreateRoadmapRequest(Guid WorkspaceId, string Name, string? VersionName, bool IsLive = false);

public sealed record UpdateRoadmapRequest(string Name, string? VersionName);

public sealed record PlanProjectRequest(Guid ProjectId, DateOnly PlannedStart, DateOnly PlannedEnd, int? SortOrder);

public sealed record RescheduleItemRequest(DateOnly PlannedStart, DateOnly PlannedEnd, int? SortOrder);

public sealed record ReorderRoadmapRequest(IReadOnlyList<Guid> ProjectIds);

public sealed record BranchRoadmapRequest(string Name);

public sealed record AdoptBranchRequest(Guid BranchRoadmapId);

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

public sealed record RoadmapFeasibilityDto(
    Guid RoadmapId,
    bool IsFeasible,
    IReadOnlyList<CapacityWarningDto> CapacityWarnings,
    IReadOnlyList<SequencingViolationDto> SequencingViolations);

// ---------- Market ----------

public sealed record MarketDto(Guid Id, Guid WorkspaceId, string Name, string? Description);

public sealed record CreateMarketRequest(Guid WorkspaceId, string Name, string? Description);

public sealed record ClientSegmentDto(
    Guid Id,
    Guid MarketId,
    string Name,
    string? Description,
    double SuiteAffinity,
    double ConsolidationPreference);

public sealed record CreateClientSegmentRequest(
    Guid MarketId,
    string Name,
    string? Description,
    double SuiteAffinity = 0.5,
    double ConsolidationPreference = 0.5);

public sealed record BusinessProcessDto(Guid Id, Guid MarketId, Guid? ParentProcessId, string Name, string? Description);

public sealed record CreateBusinessProcessRequest(Guid MarketId, string Name, string? Description, Guid? ParentProcessId);

public sealed record ApproachDto(Guid Id, Guid BusinessProcessId, string Name, string? Description);

public sealed record CreateApproachRequest(Guid BusinessProcessId, string Name, string? Description);

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record ProductPriceDto(Guid? ClientSegmentId, MoneyDto RecurringPricePerPeriod, MoneyDto ImplementationFee, string? UsageBasedPlaceholder);

public sealed record ProductDto(
    Guid Id,
    Guid MarketId,
    Guid? CompetitorId,
    Guid? ParentProductId,
    string Name,
    string? Description,
    bool IsOurs,
    bool IsModule,
    IReadOnlyList<ProductPriceDto> Prices);

public sealed record CreateProductRequest(Guid MarketId, string Name, string? Description, Guid? CompetitorId, Guid? ParentProductId);

public sealed record SetProductPriceRequest(MoneyDto RecurringPricePerPeriod, MoneyDto ImplementationFee, Guid? ClientSegmentId, string? UsageBasedPlaceholder);

public sealed record CompetitorDto(Guid Id, Guid MarketId, string Name, string? StrategyNotes);

public sealed record CreateCompetitorRequest(Guid MarketId, string Name, string? StrategyNotes);

public sealed record CoverageDto(
    Guid Id,
    Guid ProductId,
    Guid BusinessProcessId,
    Guid? ApproachId,
    Guid? ClientSegmentId,
    CoverageTier Tier,
    bool IsUnknown,
    double Confidence,
    double? NumericScore,
    DateOnly? EffectiveDate,
    string? Rationale,
    string? ScaleLimits);

public sealed record CreateCoverageRequest(
    Guid ProductId,
    Guid BusinessProcessId,
    CoverageTier Tier,
    bool IsUnknown = false,
    double Confidence = 1.0,
    Guid? ApproachId = null,
    Guid? ClientSegmentId = null,
    DateOnly? EffectiveDate = null,
    string? Rationale = null);
