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
    IReadOnlyList<Guid> TargetNeedIds,
    StrategicIntent? StrategicIntent);

public sealed record CreateInitiativeRequest(
    Guid WorkspaceId,
    string Name,
    string? Outcome,
    string? HypothesizedValue,
    IReadOnlyList<Guid>? TargetSegmentIds,
    IReadOnlyList<Guid>? TargetNeedIds,
    StrategicIntent? StrategicIntent = null);

public sealed record UpdateInitiativeRequest(
    string Name,
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

/// <summary>Share of a segment's firms using one alternative approach to a process.</summary>
public sealed record SegmentApproachAdoptionDto(Guid ApproachId, double Share);

public sealed record SetSegmentApproachAdoptionRequest(Guid ApproachId, double Share);

public sealed record ClientSegmentDto(
    Guid Id,
    Guid MarketId,
    string Name,
    string? Description,
    double SuiteAffinity,
    double ConsolidationPreference,
    SegmentStockStateDto StockState,
    IReadOnlyList<SegmentModulePenetrationDto> ModulePenetrations,
    IReadOnlyList<SegmentApproachAdoptionDto> ApproachAdoptions);

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
    string? Notes = null,
    DateTimeOffset? RecordedAt = null,
    SourceRef? Source = null);

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
    bool IsEngineParameter,
    PriorEvidenceDto? PriorEvidence = null);

/// <summary>Which observations informed a prior (PRD8). Null on a parameter = unknown. Independent: no
/// dates. Observations: an effective-date period (ObservedFrom..ObservedThrough) or the ledger as recorded
/// through RecordedThrough (what a fit stamps, with SourceCalibrationRunId).</summary>
public sealed record PriorEvidenceDto(
    PriorEvidenceBasis Basis,
    DateOnly? ObservedFrom = null,
    DateOnly? ObservedThrough = null,
    DateOnly? RecordedThrough = null,
    string? Population = null,
    Guid? SourceCalibrationRunId = null);

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
    string? Key = null,
    PriorEvidenceDto? PriorEvidence = null,
    bool ClearPriorEvidence = false);

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
    DateTimeOffset CreatedAt,
    SimulationEngineMode EngineMode,
    bool HasDiagnostics);

/// <summary>Executes one simulation synchronously and stores it — immutable once executed.
/// <c>EngineMode</c> selects Aggregate (engine v0, the default fast preview) or FirmLevel (engine
/// v1: opportunities/consideration-sets/utilities over the actual firm roster).</summary>
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
    IReadOnlyList<Guid>? HypotheticalEventIds = null,
    SimulationEngineMode EngineMode = SimulationEngineMode.Aggregate);

// ---------- Phase 3: channels, integrations, firm relationships, opportunities, calibration ----------

/// <summary>One segment a channel touches: how much of it the channel can reach, and how much of its
/// opportunity flow the channel originates.</summary>
public sealed record ChannelSegmentReachDto(Guid ClientSegmentId, double Reach, double OriginationShare);

/// <summary>Signed utility bias a channel applies to a product it presents. 0 is neutral.</summary>
public sealed record ChannelProductAffinityDto(Guid ProductId, double Bias);

public sealed record ChannelDto(
    Guid Id,
    Guid MarketId,
    string Name,
    string? Description,
    bool OriginatesOpportunities,
    bool CarriesOnlyListedProducts,
    IReadOnlyList<ChannelSegmentReachDto> SegmentReaches,
    IReadOnlyList<ChannelProductAffinityDto> ProductAffinities);

/// <summary>A route to market; direct sales is just another channel. <c>CarriesOnlyListedProducts</c>
/// turns the affinity list into a whitelist.</summary>
public sealed record CreateChannelRequest(
    Guid MarketId,
    string Name,
    string? Description = null,
    bool OriginatesOpportunities = false,
    bool CarriesOnlyListedProducts = false);

public sealed record UpdateChannelRequest(
    string Name,
    string? Description = null,
    bool OriginatesOpportunities = false,
    bool CarriesOnlyListedProducts = false);

public sealed record SetChannelSegmentReachRequest(Guid ClientSegmentId, double Reach, double OriginationShare = 0);

public sealed record SetChannelProductAffinityRequest(Guid ProductId, double Bias);

public sealed record IntegrationDto(Guid Id, Guid ProductAId, Guid ProductBId, double QualityScore, string? Notes);

/// <summary>A Product↔Product edge with a quality score in [0, 1]. The edge is symmetric: (A, B) and
/// (B, A) are the same integration, and posting the reverse pair is a conflict.</summary>
public sealed record CreateIntegrationRequest(Guid ProductAId, Guid ProductBId, double QualityScore, string? Notes = null);

public sealed record UpdateIntegrationRequest(double QualityScore, string? Notes = null);

/// <summary>"Being a client" as an edge: which product, which processes it actually serves at the
/// firm (the modules held), what fraction of list price is paid, and when it started and ended.</summary>
public sealed record FirmProductRelationshipDto(
    Guid Id,
    Guid FirmId,
    Guid ProductId,
    IReadOnlyList<Guid> ServedProcessIds,
    double PriceLevel,
    MoneyDto? PricePaidPerPeriod,
    DateOnly? AcquisitionDate,
    DateOnly? EndDate,
    bool IsActive,
    string? Notes);

public sealed record CreateFirmProductRelationshipRequest(
    Guid FirmId,
    Guid ProductId,
    IReadOnlyList<Guid>? ServedProcessIds = null,
    double PriceLevel = 1.0,
    MoneyDto? PricePaidPerPeriod = null,
    DateOnly? AcquisitionDate = null,
    string? Notes = null,
    DateOnly? EndDate = null,
    IReadOnlyList<SourceRef>? Sources = null);

/// <summary>Replaces the whole edge. Omitting <c>EndDate</c> reopens a relationship that had ended.</summary>
public sealed record UpdateFirmProductRelationshipRequest(
    IReadOnlyList<Guid>? ServedProcessIds = null,
    double PriceLevel = 1.0,
    MoneyDto? PricePaidPerPeriod = null,
    DateOnly? AcquisitionDate = null,
    DateOnly? EndDate = null,
    string? Notes = null);

/// <summary>One firm in a bulk import, with the relationships it already holds. Without <c>Source</c>
/// it upserts by (market, name) and overwrites; with it, it matches by source identity and is a partial
/// update (absent keeps, <c>ClearFields</c> clears) — see the API's FirmImportItem docs.</summary>
public sealed record FirmImportItem(
    string Name,
    Guid? ClientSegmentId = null,
    EpistemicStatus? EpistemicStatus = null,
    string? AttributesJson = null,
    string? Notes = null,
    IReadOnlyList<FirmImportRelationship>? Relationships = null,
    SourceRef? Source = null,
    IReadOnlyList<string>? ClearFields = null);

public sealed record FirmImportRelationship(
    Guid ProductId,
    IReadOnlyList<Guid>? ServedProcessIds = null,
    double? PriceLevel = null,
    MoneyDto? PricePaidPerPeriod = null,
    DateOnly? AcquisitionDate = null,
    DateOnly? EndDate = null,
    IReadOnlyList<SourceRef>? Sources = null,
    IReadOnlyList<string>? ClearFields = null);

/// <summary>Imports a roster of firms into one market. Matching is by natural key (market, name).</summary>
public sealed record BulkImportFirmsRequest(Guid MarketId, IReadOnlyList<FirmImportItem> Firms);

public sealed record BulkImportFirmsResponse(
    Guid MarketId,
    int Created,
    int Updated,
    int RelationshipsWritten,
    IReadOnlyList<FirmDto> Firms,
    int Unchanged = 0,
    int Exceptions = 0,
    IReadOnlyList<FirmImportOutcomeDto>? Outcomes = null);

/// <summary>Identity of one record in an external source system; compared exactly.</summary>
public sealed record SourceRef(string SourceSystem, string SourceInstance, string ObjectKind, string SourceRecordId);

/// <summary>One source record and the row it became (GET /api/source-bindings).</summary>
public sealed record SourceBindingDto(
    Guid Id,
    Guid WorkspaceId,
    string SourceSystem,
    string SourceInstance,
    string ObjectKind,
    string SourceRecordId,
    string TargetType,
    Guid TargetId,
    DateTimeOffset BoundAt,
    string? ImportedValuesJson);

/// <summary>A source-owned field not applied because it was edited locally since the last import.</summary>
public sealed record FieldConflictDto(string Field, string? StoredValue, string? IncomingValue, string? LastImportedValue);

public sealed record RelationshipImportOutcomeDto(
    int Index,
    Guid ProductId,
    Guid? RelationshipId,
    ImportOutcome Outcome,
    string? Reason,
    IReadOnlyList<FieldConflictDto> Conflicts);

public sealed record FirmImportOutcomeDto(
    int Index,
    string Name,
    SourceRef? Source,
    Guid? FirmId,
    ImportOutcome Outcome,
    string? Reason,
    IReadOnlyList<FieldConflictDto> Conflicts,
    IReadOnlyList<RelationshipImportOutcomeDto> Relationships);

/// <summary>Fills a market's segment stocks with deterministically generated synthetic firms.
/// Regeneration replaces the previous synthetic population rather than adding a second one.</summary>
public sealed record GenerateSyntheticFirmsRequest(Guid MarketId);

public sealed record SyntheticSegmentFillDto(
    Guid ClientSegmentId,
    int ProspectTarget,
    int ObservedProspects,
    int SyntheticProspects,
    int ClientTarget,
    int ObservedClients,
    int SyntheticClients);

public sealed record GenerateSyntheticFirmsResponse(
    Guid MarketId,
    int Removed,
    int Created,
    int RelationshipsCreated,
    IReadOnlyList<SyntheticSegmentFillDto> Segments);

public sealed record OpportunityScopeItemDto(Guid BusinessProcessId, DateTimeOffset AddedAt);

public sealed record OpportunityDto(
    Guid Id,
    Guid FirmId,
    Guid? ChannelId,
    OpportunityStage Stage,
    DateTimeOffset OpenedAt,
    DateTimeOffset? QualifiedAt,
    DateTimeOffset? DecidedAt,
    IReadOnlyList<Guid> CompetitorIds,
    Guid? WinningProductId,
    Guid? CrossSellModuleProductId,
    bool IsCrossSell,
    bool IsDecided,
    string? Notes,
    IReadOnlyList<OpportunityScopeItemDto> Scope);

/// <summary>Opens an opportunity: a firm seeking coverage for a scoped set of business processes. A
/// cross-sell is not a special case — <c>CrossSellModuleProductId</c> names the module being sold.</summary>
public sealed record CreateOpportunityRequest(
    Guid FirmId,
    DateTimeOffset? OpenedAt = null,
    Guid? ChannelId = null,
    Guid? CrossSellModuleProductId = null,
    IReadOnlyList<Guid>? ScopeProcessIds = null,
    IReadOnlyList<Guid>? CompetitorIds = null,
    string? Notes = null,
    SourceRef? Source = null);

public sealed record UpdateOpportunityRequest(
    Guid? ChannelId = null,
    Guid? CrossSellModuleProductId = null,
    IReadOnlyList<Guid>? CompetitorIds = null,
    string? Notes = null);

/// <summary>Adds one business process to the scope. Scope can expand mid-cycle until the decision.</summary>
public sealed record AddOpportunityScopeRequest(Guid BusinessProcessId, DateTimeOffset? AddedAt = null);

public sealed record QualifyOpportunityRequest(DateTimeOffset? At = null);

/// <summary>Records the decision. <c>Outcome</c> must be Won, Lost or NoDecision; a won deal must
/// name the winning product (ours or a competitor's).</summary>
public sealed record DecideOpportunityRequest(
    OpportunityStage Outcome,
    Guid? WinningProductId = null,
    DateTimeOffset? At = null,
    DateTimeOffset? RecordedAt = null);

// ---------- Calibration ----------

public sealed record CalibrationRunDto(
    Guid Id,
    Guid WorkspaceId,
    string? Name,
    DateOnly FactsAsOfDate,
    Guid PriorAssumptionSetId,
    Guid? FittedAssumptionSetId,
    bool IsExecuted,
    DateTimeOffset CreatedAt,
    string? Notes,
    DateOnly? ObservationsRecordedAfter = null);

/// <summary>Fits the prior assumption set to the facts ledger as it stood on <c>AsOfDate</c>: only
/// facts recorded on or before that date are read. The run is immutable and produces a new
/// assumption set (parent = the prior, provenance Calibrated) rather than editing the prior.</summary>
/// <para>ObservationsRecordedAfter narrows the fit to facts recorded after that date (a sequential update of
/// a prior fitted up to it). A fit whose prior already contains its observations is refused with a 409 of
/// type <see cref="ApiRequestException.PriorEvidenceReuseType"/>.</para>
public sealed record CreateCalibrationRunRequest(
    Guid AssumptionSetId,
    DateOnly? AsOfDate = null,
    string? Name = null,
    string? Notes = null,
    DateOnly? ObservationsRecordedAfter = null);

/// <summary>Prior vs posterior for one parameter, with the evidence that moved it.</summary>
public sealed record CalibrationParameterDiffDto(
    string Key,
    Guid? ClientSegmentId,
    SimulationFlowKind? FlowKind,
    Guid? ModuleProductId,
    Guid? ProductId,
    DistributionSpecDto? Prior,
    DistributionSpecDto? Posterior,
    double PriorMean,
    double PosteriorMean,
    double PriorStdDev,
    double PosteriorStdDev,
    double Shift,
    int SampleSize,
    string Rationale);

/// <summary>A prior the evidence could not move, passed through with the reason why.</summary>
public sealed record CalibrationUnfittedDto(
    string Key,
    Guid? ClientSegmentId,
    SimulationFlowKind? FlowKind,
    Guid? ModuleProductId,
    Guid? ProductId,
    DistributionSpecDto? Distribution,
    string Reason);

public sealed record CalibrationDiffDto(
    Guid CalibrationRunId,
    DateOnly AsOfDate,
    Guid PriorAssumptionSetId,
    Guid? FittedAssumptionSetId,
    IReadOnlyList<CalibrationParameterDiffDto> Fitted,
    IReadOnlyList<CalibrationUnfittedDto> Unfitted);

/// <summary>A calibration run plus the diff it produced — what POST returns.</summary>
public sealed record CalibrationRunResultDto(CalibrationRunDto Run, CalibrationDiffDto Diff);

// ---------- Calibration readiness (PRD8) — mirrors Contracts/CalibrationReadinessContracts.cs ----------

public sealed record ReadinessIssueDto(ReadinessIssueCode Code, string Message);

public sealed record ReadinessParameterDto(
    string Key,
    SimulationFlowKind? FlowKind,
    Guid? ClientSegmentId,
    Guid? ModuleProductId,
    Guid? ProductId,
    CalibrationReadinessStatus Status,
    string Units,
    string Denominator,
    DateOnly? ObservedFrom,
    DateOnly? ObservedThrough,
    int Observations,
    int? Successes,
    int? Failures,
    int? Excluded,
    double? Exposure,
    DistributionSpecDto? Prior,
    ParameterProvenance? PriorProvenance,
    PriorEvidenceDto? PriorEvidence,
    DistributionSpecDto? Posterior,
    double? PosteriorMean,
    string? UnfittedReason,
    IReadOnlyList<ReadinessIssueDto> Issues);

public sealed record ReadinessAssertedDto(
    string Key,
    string Quantity,
    CalibrationReadinessStatus Status,
    ParameterProvenance? Provenance,
    DistributionSpecDto? Distribution,
    string? Rationale,
    int Count);

public sealed record ReadinessModuleHoldersDto(Guid ModuleProductId, string? Name, int Holders);

public sealed record ReadinessDecisionCountsDto(
    int Won, int Lost, int NoDecision, int CrossSellWon, int CrossSellLost, int CrossSellNoDecision);

public sealed record ReadinessSegmentPopulationDto(
    Guid ClientSegmentId,
    string? SegmentName,
    int ObservedFirms,
    int InferredFirms,
    int SyntheticFirms,
    int ActiveCustomers,
    IReadOnlyList<ReadinessModuleHoldersDto> ModuleHolders,
    ReadinessDecisionCountsDto Decisions,
    int ChurnEvents,
    int ProductlessChurnEvents,
    int Relationships,
    int RelationshipsWithoutAcquisitionDate,
    int EndedRelationships,
    int MultiProductFirms,
    double ClientMonths);

public sealed record ReadinessExclusionDto(
    FactEventKind Kind, int RecordedAfterAsOf, int RecordedOnOrBeforeWindowStart, int Unattributed, int AddedAfterRun);

public sealed record ReadinessArrivalSeriesDto(
    Guid? ClientSegmentId,
    SimulationFlowKind FlowKind,
    int ObservedMonths,
    int ZeroMonths,
    DateOnly? FirstMonth,
    DateOnly? LastMonth,
    double Exposure,
    IReadOnlyList<DateOnly> MissingMonths,
    IReadOnlyList<DateOnly> DuplicateMonths);

public sealed record ReadinessHoldoutComparisonDto(
    string Key,
    SimulationFlowKind? FlowKind,
    Guid? ClientSegmentId,
    Guid? ModuleProductId,
    Guid? ProductId,
    HoldoutComparisonStatus Status,
    string? Reason,
    int SampleSize,
    int? ObservedEvents,
    double? Exposure,
    double? ObservedRate,
    double? PosteriorMean,
    double? IntervalLower,
    double? IntervalUpper,
    bool? ObservedInsideInterval);

public sealed record ReadinessHoldoutDto(
    DateOnly HeldOutFrom,
    DateOnly HeldOutThrough,
    double IntervalMass,
    string Note,
    IReadOnlyList<ReadinessHoldoutComparisonDto> Comparisons);

/// <summary>GET api/calibration-runs/{id}/readiness — computed on read from the stored run; never refits.</summary>
public sealed record CalibrationReadinessDto(
    Guid CalibrationRunId,
    DateOnly AsOfDate,
    DateOnly? ObservationsRecordedAfter,
    Guid PriorAssumptionSetId,
    Guid? FittedAssumptionSetId,
    CalibrationReadinessOverall Overall,
    IReadOnlyList<string> OverallReasons,
    IReadOnlyList<ReadinessParameterDto> Parameters,
    IReadOnlyList<ReadinessAssertedDto> Asserted,
    IReadOnlyList<ReadinessSegmentPopulationDto> Populations,
    IReadOnlyList<ReadinessExclusionDto> Exclusions,
    IReadOnlyList<ReadinessArrivalSeriesDto> ArrivalObservations,
    IReadOnlyList<string> Notes,
    ReadinessHoldoutDto? Holdout);

// ---------- Firm-level diagnostics (mirrors ProductManager.Engine.FirmLevelContracts — see openapi's Engine-prefixed schemas) ----------

public sealed record EngineSegmentFunnel(
    string SegmentId, double Opened, double Qualified, double Won, double Lost, double NoDecision, double StillOpen, double WinRate);

public sealed record EngineChannelFunnel(string ChannelId, double Opened, double Won, double WinRate);

public sealed record EngineFirmLevelDiagnostics(
    IReadOnlyList<EngineSegmentFunnel> FunnelBySegment,
    IReadOnlyList<EngineChannelFunnel> FunnelByChannel,
    double AvgSalesCycleMonths,
    double ExpansionCount,
    double MeanUtilityGapLostDeals,
    double SyntheticFirmsCreated);

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
