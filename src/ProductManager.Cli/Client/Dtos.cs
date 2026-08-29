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

/// <summary>The segment's observed starting state — the stocks and unit economics the simulation
/// engine starts a run from. Amounts are per month in the workspace's implicit single currency.</summary>
public sealed record SegmentStockStateDto(
    double InitialProspectPool,
    int InitialClientCount,
    double ArpuPerMonth,
    double ImplementationFee,
    double ServicingCostPerClient,
    double InfraFixedCostPerMonth,
    double InfraCostPerClient,
    double RdDragPerMonth);

/// <summary>Starting penetration and pricing of one cross-sellable module within the segment.</summary>
public sealed record SegmentModulePenetrationDto(Guid ModuleProductId, double Penetration, double ArpuPerMonth);

public sealed record ClientSegmentDto(
    Guid Id,
    Guid MarketId,
    string Name,
    string? Description,
    double SuiteAffinity,
    double ConsolidationPreference,
    SegmentStockStateDto StockState,
    IReadOnlyList<SegmentModulePenetrationDto> ModulePenetrations);

public sealed record CreateClientSegmentRequest(
    Guid MarketId,
    string Name,
    string? Description,
    double SuiteAffinity = 0.5,
    double ConsolidationPreference = 0.5);

/// <summary>Replaces the segment's whole starting state — it is one measurement, not a set of knobs.</summary>
public sealed record SetSegmentStockStateRequest(
    double InitialProspectPool = 0,
    int InitialClientCount = 0,
    double ArpuPerMonth = 0,
    double ImplementationFee = 0,
    double ServicingCostPerClient = 0,
    double InfraFixedCostPerMonth = 0,
    double InfraCostPerClient = 0,
    double RdDragPerMonth = 0);

public sealed record SetSegmentModulePenetrationRequest(Guid ModuleProductId, double Penetration, double ArpuPerMonth);

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

/// <summary>Records a coverage assessment, upserting on the (product, process, approach?, segment?)
/// key — posting the same tuple twice re-assesses rather than duplicating.</summary>
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

/// <summary>Re-assesses an existing coverage row found by id; the key dimensions are immutable.</summary>
public sealed record UpdateCoverageRequest(
    CoverageTier Tier,
    bool IsUnknown = false,
    double Confidence = 1.0,
    DateOnly? EffectiveDate = null,
    string? Rationale = null,
    string? ScaleLimits = null);

// ---------- Firm ----------

public sealed record FirmDto(
    Guid Id,
    Guid MarketId,
    Guid? ClientSegmentId,
    string Name,
    EpistemicStatus EpistemicStatus,
    string? AttributesJson,
    string? Notes);

public sealed record CreateFirmRequest(
    Guid MarketId,
    string Name,
    EpistemicStatus EpistemicStatus = EpistemicStatus.Observed,
    Guid? ClientSegmentId = null,
    string? AttributesJson = null,
    string? Notes = null);

public sealed record UpdateFirmRequest(
    string Name,
    EpistemicStatus EpistemicStatus = EpistemicStatus.Observed,
    Guid? ClientSegmentId = null,
    string? AttributesJson = null,
    string? Notes = null);

// ---------- Need ----------

public sealed record NeedDto(
    Guid Id,
    Guid? FirmId,
    Guid? ClientSegmentId,
    Guid BusinessProcessId,
    double ImportanceWeight,
    bool IsHard,
    MoneyDto? WorkaroundCostPerPeriod,
    string? RequiredDepthJson,
    string? Conditionality,
    string? Notes);

/// <summary>A demand edge. Exactly one of <c>FirmId</c> / <c>ClientSegmentId</c> must be given.</summary>
public sealed record CreateNeedRequest(
    Guid BusinessProcessId,
    double ImportanceWeight,
    Guid? FirmId = null,
    Guid? ClientSegmentId = null,
    bool IsHard = false,
    MoneyDto? WorkaroundCostPerPeriod = null,
    string? RequiredDepthJson = null,
    string? Conditionality = null,
    string? Notes = null);

public sealed record UpdateNeedRequest(
    double ImportanceWeight,
    bool IsHard = false,
    MoneyDto? WorkaroundCostPerPeriod = null,
    string? RequiredDepthJson = null,
    string? Conditionality = null,
    string? Notes = null);

// ---------- MarketEvent ----------

/// <summary>How an event scales one simulated flow while it is active. A null segment means all segments.</summary>
public sealed record EventRateMultiplierDto(Guid? ClientSegmentId, SimulationFlowKind FlowKind, double Multiplier);

public sealed record MarketEventDto(
    Guid Id,
    Guid MarketId,
    MarketEventType Type,
    string Name,
    DateOnly StartDate,
    DateOnly? EndDate,
    double? Magnitude,
    bool IsHypothetical,
    Guid? CompetitorId,
    string? Notes,
    IReadOnlyList<EventRateMultiplierDto> RateMultipliers);

public sealed record CreateMarketEventRequest(
    Guid MarketId,
    MarketEventType Type,
    string Name,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    bool IsHypothetical = false,
    double? Magnitude = null,
    Guid? CompetitorId = null,
    string? Notes = null,
    IReadOnlyList<EventRateMultiplierDto>? RateMultipliers = null);

public sealed record UpdateMarketEventRequest(
    MarketEventType Type,
    string Name,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    bool IsHypothetical = false,
    double? Magnitude = null,
    Guid? CompetitorId = null,
    string? Notes = null,
    IReadOnlyList<EventRateMultiplierDto>? RateMultipliers = null);

// ---------- Covariate ----------

public sealed record CovariateDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    string? Unit,
    double? GbmStart,
    double? GbmDrift,
    double? GbmVol,
    bool HasGbmSpec);

/// <summary>The three <c>Gbm*</c> numbers describe the future path (geometric Brownian motion) and
/// must be given together or not at all; observed history arrives separately as facts.</summary>
public sealed record CreateCovariateRequest(
    Guid WorkspaceId,
    string Name,
    string? Description = null,
    string? Unit = null,
    double? GbmStart = null,
    double? GbmDrift = null,
    double? GbmVol = null);

public sealed record UpdateCovariateRequest(
    string Name,
    string? Description = null,
    string? Unit = null,
    double? GbmStart = null,
    double? GbmDrift = null,
    double? GbmVol = null);

/// <summary>One observed value of a covariate, read back out of the bi-temporal facts ledger.</summary>
public sealed record CovariateObservationDto(Guid FactEventId, DateOnly EffectiveDate, DateTimeOffset RecordedDate, double Value, string? Notes);

public sealed record CovariateHistoryDto(Guid CovariateId, string Name, string? Unit, IReadOnlyList<CovariateObservationDto> Observations);

// ---------- Facts ledger ----------

/// <summary>A bi-temporal market observation. Append-only: there is no update and no delete.</summary>
public sealed record FactEventDto(
    Guid Id,
    Guid WorkspaceId,
    FactEventKind Kind,
    DateOnly EffectiveDate,
    DateTimeOffset RecordedDate,
    string? SubjectType,
    Guid? SubjectId,
    string? PayloadJson,
    string? Notes);

/// <summary>Appends one observation. For a covariate reading use <c>Kind = CovariateObserved</c>,
/// <c>SubjectType = "Covariate"</c>, <c>SubjectId</c> = the covariate, and a payload of
/// <c>{"value": 1234.5}</c> — that is what the covariate history endpoint reads back.</summary>
public sealed record CreateFactEventRequest(
    Guid WorkspaceId,
    FactEventKind Kind,
    DateOnly EffectiveDate,
    string? SubjectType = null,
    Guid? SubjectId = null,
    string? PayloadJson = null,
    string? Notes = null);

// ---------- Distribution / AssumptionSet / Parameter ----------

/// <summary>A parameter distribution: a family plus two numbers. Point: P1 = the value, P2 must be
/// 0. Normal: P1 = mean, P2 = std dev. Lognormal: P1 = mu, P2 = sigma (log scale). Beta: P1 = alpha, P2 = beta.</summary>
public sealed record DistributionSpecDto(DistributionType Type, double P1, double P2 = 0);

public sealed record ParameterDto(
    Guid Id,
    Guid AssumptionSetId,
    string Key,
    DistributionSpecDto Distribution,
    ParameterProvenance Provenance,
    string? Rationale,
    Guid? ClientSegmentId,
    SimulationFlowKind? FlowKind,
    Guid? ModuleProductId,
    Guid? CovariateId,
    double? CovariateBeta,
    bool IsEngineParameter);

public sealed record AssumptionSetDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    Guid? ParentAssumptionSetId,
    DateTimeOffset CreatedAt,
    string? Notes,
    IReadOnlyList<ParameterDto> Parameters);

public sealed record CreateAssumptionSetRequest(Guid WorkspaceId, string Name, string? Notes = null);

public sealed record UpdateAssumptionSetRequest(string Name, string? Notes = null);

public sealed record BranchAssumptionSetRequest(string Name);

/// <summary>Sets (upserting) one parameter. Give <c>ClientSegmentId</c> + <c>FlowKind</c>
/// (+ <c>ModuleProductId</c> for CrossSellRate) for a parameter the engine reads — that triple is
/// the engine's own key, so writing it twice replaces rather than duplicates. Give <c>Key</c>
/// instead for a free-form assumption the engine ignores.</summary>
public sealed record UpsertParameterRequest(
    DistributionSpecDto Distribution,
    ParameterProvenance Provenance = ParameterProvenance.Asserted,
    string? Rationale = null,
    Guid? ClientSegmentId = null,
    SimulationFlowKind? FlowKind = null,
    Guid? ModuleProductId = null,
    Guid? CovariateId = null,
    double? CovariateBeta = null,
    string? Key = null);

// ---------- SimulationRun ----------

public sealed record SimulationRunDto(
    Guid Id,
    Guid WorkspaceId,
    Guid RoadmapId,
    Guid AssumptionSetId,
    string? Name,
    DateOnly StartDate,
    int HorizonMonths,
    int Paths,
    int Seed,
    double DiscountRateAnnual,
    double TerminalGrowthAnnual,
    string? ModelVersion,
    bool IsExecuted,
    long? ElapsedMs,
    DateTimeOffset CreatedAt);

/// <summary>Executes one simulation synchronously and stores it — immutable once executed.</summary>
public sealed record CreateSimulationRunRequest(
    Guid RoadmapId,
    Guid AssumptionSetId,
    int HorizonMonths = 60,
    int Paths = 1000,
    int Seed = 0,
    double DiscountRateAnnual = 0.10,
    double TerminalGrowthAnnual = 0.02,
    string? Name = null,
    DateOnly? StartDate = null,
    Guid? MarketId = null,
    IReadOnlyList<Guid>? EventIds = null,
    IReadOnlyList<Guid>? HypotheticalEventIds = null);

// ---------- Engine (mirrors ProductManager.Engine — see openapi's Engine-prefixed schemas) ----------

public sealed record EngineCostRates(double ServicingPerClient, double InfraFixed, double InfraPerClient, double RdDragPerMonth);

public sealed record EngineModulePenetration(string ModuleId, double Penetration, double ArpuPerMonth);

public sealed record EngineSegmentInput(
    string Id,
    double InitialProspectPool,
    int InitialClientCount,
    double ArpuPerMonth,
    double ImplementationFee,
    IReadOnlyList<EngineModulePenetration> PerModulePenetration,
    EngineCostRates CostRates);

public sealed record EngineDistributionSpec(EngineDistributionType Type, double P1, double P2 = 0);

/// <summary>Beta is an elasticity: the engine applies rate * (x_t / x_0)^Beta.</summary>
public sealed record EngineCovariateLink(string CovariateId, double Beta);

/// <summary>Note: FlowKind reuses the CLI's <see cref="SimulationFlowKind"/> enum — the engine's own
/// "EngineFlowKind" schema has an identical member set, so one CLI enum type serves both wire shapes.</summary>
public sealed record EngineParameterSpec(
    string SegmentId,
    SimulationFlowKind FlowKind,
    string? ModuleId,
    EngineDistributionSpec Distribution,
    EngineCovariateLink? Covariate);

public sealed record EngineGbmSpec(double Start, double Drift, double Vol);

public sealed record EngineCovariateInput(string Id, double[]? MonthlyPath, EngineGbmSpec? Gbm);

public sealed record EngineEventRateMultiplier(string? SegmentId, SimulationFlowKind FlowKind, double Multiplier);

public sealed record EngineEventInput(string Id, int StartMonth, int EndMonth, IReadOnlyList<EngineEventRateMultiplier> RateMultipliers);

public sealed record EngineAttractivenessEntry(string SegmentId, int Month, double Multiplier);

public sealed record EngineInput(
    IReadOnlyList<EngineSegmentInput> Segments,
    IReadOnlyList<EngineParameterSpec> Parameters,
    IReadOnlyList<EngineCovariateInput> Covariates,
    IReadOnlyList<EngineEventInput> Events,
    IReadOnlyList<EngineAttractivenessEntry> AttractivenessSchedule,
    int HorizonMonths,
    int Paths,
    int Seed,
    double DiscountRateAnnual,
    double TerminalGrowthAnnual);

public sealed record EngineDistributionSummary(double Mean, double P10, double P50, double P90);

public sealed record EngineMetricTrajectory(double[] Mean, double[] P10, double[] P50, double[] P90);

public sealed record EngineSegmentTrajectories(
    string SegmentId,
    EngineMetricTrajectory Clients,
    EngineMetricTrajectory ProspectPool,
    EngineMetricTrajectory NewWins,
    EngineMetricTrajectory Churned,
    EngineMetricTrajectory Bookings,
    EngineMetricTrajectory RecognizedArr,
    EngineMetricTrajectory ContributionMargin);

public sealed record EngineResult(
    IReadOnlyList<EngineSegmentTrajectories> Segments,
    EngineSegmentTrajectories Total,
    EngineDistributionSummary Npv,
    double[] NpvPerPath,
    int Paths,
    int Seed,
    int HorizonMonths,
    long ElapsedMs);
