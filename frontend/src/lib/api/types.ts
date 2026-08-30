/**
 * Typed contract for the ProductManager API, hand-written from
 * openapi/productmanager.json (Phase 1 surface). Dates use ISO strings:
 * DateOnly -> "yyyy-MM-dd", DateTimeOffset -> ISO 8601.
 */

// ---------- Shared ----------

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  errors?: Record<string, string[]>;
}

// ---------- Auth ----------

export interface TokenRequest {
  password: string;
}

export interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
}

// ---------- Workspace ----------

export interface WorkspaceDto {
  id: string;
  name: string;
  createdAt: string;
}

export interface CreateWorkspaceRequest {
  name: string;
}

export interface UpdateWorkspaceRequest {
  name: string;
}

// ---------- Team ----------

export interface TeamCapacityDto {
  year: number;
  month: number;
  points: number;
}

export interface TeamDto {
  id: string;
  workspaceId: string;
  name: string;
  specializationTag: string | null;
  capacities: TeamCapacityDto[];
}

export interface CreateTeamRequest {
  workspaceId: string;
  name: string;
  specializationTag?: string | null;
}

export interface UpdateTeamRequest {
  name: string;
  specializationTag?: string | null;
}

export interface SetTeamCapacityRequest {
  year: number;
  month: number;
  points: number;
}

// ---------- Initiative ----------

export interface InitiativeDto {
  id: string;
  workspaceId: string;
  name: string;
  outcome: string | null;
  hypothesizedValue: string | null;
  targetSegmentIds: string[];
  targetNeedIds: string[];
}

export interface CreateInitiativeRequest {
  workspaceId: string;
  name: string;
  outcome?: string | null;
  hypothesizedValue?: string | null;
  targetSegmentIds?: string[] | null;
  targetNeedIds?: string[] | null;
}

export interface UpdateInitiativeRequest {
  name: string;
  outcome?: string | null;
  hypothesizedValue?: string | null;
  targetSegmentIds?: string[] | null;
  targetNeedIds?: string[] | null;
}

// ---------- Project ----------

export type ProjectStatus =
  | "Proposed"
  | "Planned"
  | "InProgress"
  | "Done"
  | "Cancelled";

export const PROJECT_STATUSES: ProjectStatus[] = [
  "Proposed",
  "Planned",
  "InProgress",
  "Done",
  "Cancelled",
];

export interface EffortDemandDto {
  teamId: string;
  year: number;
  month: number;
  points: number;
}

export interface ProjectDto {
  id: string;
  initiativeId: string;
  name: string;
  description: string | null;
  status: ProjectStatus;
  startDate: string | null;
  endDate: string | null;
  dependsOnProjectIds: string[];
  deliveredFeatureIds: string[];
  effortDemands: EffortDemandDto[];
}

export interface CreateProjectRequest {
  initiativeId: string;
  name: string;
  description?: string | null;
  status?: ProjectStatus | null;
  startDate?: string | null;
  endDate?: string | null;
}

export interface UpdateProjectRequest {
  name: string;
  description?: string | null;
  status?: ProjectStatus | null;
  startDate?: string | null;
  endDate?: string | null;
}

export interface AddProjectDependencyRequest {
  dependsOnProjectId: string;
}

export interface SetEffortDemandRequest {
  teamId: string;
  year: number;
  month: number;
  /** 0 clears the demand. */
  points: number;
}

export interface LinkFeatureRequest {
  featureId: string;
}

// ---------- Feature (the seam) ----------

export type FeatureStatus = "Hypothetical" | "Planned" | "Shipped";

export const FEATURE_STATUSES: FeatureStatus[] = [
  "Hypothetical",
  "Planned",
  "Shipped",
];

export type CoverageTier = "NotPlaying" | "Bronze" | "Silver" | "Gold";

export type FeatureDeltaKind = "Coverage" | "SwitchingCost" | "Other";

export interface FeatureDeltaDto {
  kind: FeatureDeltaKind;
  businessProcessId: string | null;
  fromTier: CoverageTier | null;
  toTier: CoverageTier | null;
  approachId: string | null;
  segmentId: string | null;
  magnitude: number | null;
  rationale: string | null;
}

export interface FeatureDto {
  id: string;
  workspaceId: string;
  productId: string | null;
  name: string;
  description: string | null;
  status: FeatureStatus;
  shippedDate: string | null;
  deltas: FeatureDeltaDto[];
}

export interface CreateFeatureRequest {
  workspaceId: string;
  name: string;
  description?: string | null;
  productId?: string | null;
}

export interface UpdateFeatureRequest {
  name: string;
  description?: string | null;
  productId?: string | null;
  status?: FeatureStatus | null;
  shippedDate?: string | null;
}

// ---------- Roadmap ----------

export interface RoadmapItemDto {
  id: string;
  roadmapId: string;
  projectId: string;
  plannedStart: string;
  plannedEnd: string;
  sortOrder: number;
}

export interface RoadmapDto {
  id: string;
  workspaceId: string;
  name: string;
  versionName: string | null;
  parentRoadmapId: string | null;
  isLive: boolean;
  createdAt: string;
  items: RoadmapItemDto[];
}

export interface CreateRoadmapRequest {
  workspaceId: string;
  name: string;
  versionName?: string | null;
  isLive?: boolean;
}

export interface UpdateRoadmapRequest {
  name: string;
  versionName?: string | null;
}

export interface PlanProjectRequest {
  projectId: string;
  plannedStart: string;
  plannedEnd: string;
  sortOrder?: number | null;
}

export interface RescheduleItemRequest {
  plannedStart: string;
  plannedEnd: string;
  sortOrder?: number | null;
}

export interface ReorderRoadmapRequest {
  projectIds: string[];
}

export interface BranchRoadmapRequest {
  name: string;
}

export interface AdoptBranchRequest {
  branchRoadmapId: string;
}

// ---------- Feasibility ----------

export interface CapacityWarningDto {
  teamId: string;
  teamName: string | null;
  year: number;
  month: number;
  capacityPoints: number;
  demandPoints: number;
  overallocationPoints: number;
  capacityUndefined: boolean;
}

export type SequencingViolationKind =
  | "StartsBeforeDependencyEnds"
  | "DependencyNotOnRoadmap"
  | "DependencyCycle";

export interface SequencingViolationDto {
  kind: SequencingViolationKind;
  projectId: string;
  dependsOnProjectId: string;
  description: string;
}

export interface RoadmapFeasibilityDto {
  roadmapId: string;
  isFeasible: boolean;
  capacityWarnings: CapacityWarningDto[];
  sequencingViolations: SequencingViolationDto[];
}

// =====================================================================
// Phase 2 — market model & simulation surface
// =====================================================================

// ---------- Money ----------

export interface MoneyDto {
  amount: number;
  currency: string;
}

// ---------- Market ----------

export interface MarketDto {
  id: string;
  workspaceId: string;
  name: string;
  description: string | null;
}

export interface CreateMarketRequest {
  workspaceId: string;
  name: string;
  description?: string | null;
}

export interface UpdateMarketRequest {
  name: string;
  description?: string | null;
}

// ---------- Client segment ----------

export interface SegmentStockStateDto {
  initialProspectPool: number;
  initialClientCount: number;
  arpuPerMonth: number;
  implementationFee: number;
  servicingCostPerClient: number;
  infraFixedCostPerMonth: number;
  infraCostPerClient: number;
  rdDragPerMonth: number;
}

export interface SegmentModulePenetrationDto {
  moduleProductId: string;
  penetration: number;
  arpuPerMonth: number;
}

export interface SegmentApproachAdoptionDto {
  approachId: string;
  share: number;
}

export type SetSegmentApproachAdoptionRequest = SegmentApproachAdoptionDto;

export interface ClientSegmentDto {
  id: string;
  marketId: string;
  name: string;
  description: string | null;
  suiteAffinity: number;
  consolidationPreference: number;
  stockState: SegmentStockStateDto;
  modulePenetrations: SegmentModulePenetrationDto[];
  approachAdoptions: SegmentApproachAdoptionDto[];
}

export interface CreateClientSegmentRequest {
  marketId: string;
  name: string;
  description?: string | null;
  suiteAffinity?: number;
  consolidationPreference?: number;
}

export interface UpdateClientSegmentRequest {
  name: string;
  description?: string | null;
  suiteAffinity?: number;
  consolidationPreference?: number;
}

export type SetSegmentStockStateRequest = SegmentStockStateDto;

export type SetSegmentModulePenetrationRequest = SegmentModulePenetrationDto;

// ---------- Competitor ----------

export interface CompetitorDto {
  id: string;
  marketId: string;
  name: string;
  strategyNotes: string | null;
}

export interface CreateCompetitorRequest {
  marketId: string;
  name: string;
  strategyNotes?: string | null;
}

export interface UpdateCompetitorRequest {
  name: string;
  strategyNotes?: string | null;
}

// ---------- Product ----------

export interface ProductPriceDto {
  clientSegmentId: string | null;
  recurringPricePerPeriod: MoneyDto;
  implementationFee: MoneyDto;
  usageBasedPlaceholder: string | null;
}

export interface ProductDto {
  id: string;
  marketId: string;
  competitorId: string | null;
  parentProductId: string | null;
  name: string;
  description: string | null;
  isOurs: boolean;
  isModule: boolean;
  prices: ProductPriceDto[];
}

export interface CreateProductRequest {
  marketId: string;
  name: string;
  description?: string | null;
  competitorId?: string | null;
  parentProductId?: string | null;
}

export interface UpdateProductRequest {
  name: string;
  description?: string | null;
  parentProductId?: string | null;
}

export interface SetProductPriceRequest {
  recurringPricePerPeriod: MoneyDto;
  implementationFee: MoneyDto;
  clientSegmentId?: string | null;
  usageBasedPlaceholder?: string | null;
}

// ---------- Business process & approach ----------

export interface BusinessProcessDto {
  id: string;
  marketId: string;
  parentProcessId: string | null;
  name: string;
  description: string | null;
}

export interface CreateBusinessProcessRequest {
  marketId: string;
  name: string;
  description?: string | null;
  parentProcessId?: string | null;
}

export interface UpdateBusinessProcessRequest {
  name: string;
  description?: string | null;
  parentProcessId?: string | null;
}

export interface ApproachDto {
  id: string;
  businessProcessId: string;
  name: string;
  description: string | null;
}

export interface CreateApproachRequest {
  businessProcessId: string;
  name: string;
  description?: string | null;
}

export interface UpdateApproachRequest {
  name: string;
  description?: string | null;
}

// ---------- Coverage ----------

export const COVERAGE_TIERS: CoverageTier[] = [
  "Gold",
  "Silver",
  "Bronze",
  "NotPlaying",
];

export interface CoverageDto {
  id: string;
  productId: string;
  businessProcessId: string;
  approachId: string | null;
  clientSegmentId: string | null;
  tier: CoverageTier;
  isUnknown: boolean;
  confidence: number;
  numericScore: number | null;
  effectiveDate: string | null;
  rationale: string | null;
  scaleLimits: string | null;
}

export interface CreateCoverageRequest {
  productId: string;
  businessProcessId: string;
  tier: CoverageTier;
  isUnknown?: boolean;
  confidence?: number;
  approachId?: string | null;
  clientSegmentId?: string | null;
  effectiveDate?: string | null;
  rationale?: string | null;
}

export interface UpdateCoverageRequest {
  tier: CoverageTier;
  isUnknown?: boolean;
  confidence?: number;
  effectiveDate?: string | null;
  rationale?: string | null;
  scaleLimits?: string | null;
}

// ---------- Need ----------

export interface NeedDto {
  id: string;
  firmId: string | null;
  clientSegmentId: string | null;
  businessProcessId: string;
  importanceWeight: number;
  isHard: boolean;
  workaroundCostPerPeriod: MoneyDto;
  requiredDepthJson: string | null;
  conditionality: string | null;
  notes: string | null;
}

export interface CreateNeedRequest {
  businessProcessId: string;
  importanceWeight: number;
  firmId?: string | null;
  clientSegmentId?: string | null;
  isHard?: boolean;
  workaroundCostPerPeriod?: MoneyDto;
  requiredDepthJson?: string | null;
  conditionality?: string | null;
  notes?: string | null;
}

export interface UpdateNeedRequest {
  importanceWeight: number;
  isHard?: boolean;
  workaroundCostPerPeriod?: MoneyDto;
  requiredDepthJson?: string | null;
  conditionality?: string | null;
  notes?: string | null;
}

// ---------- Firm ----------

export type EpistemicStatus = "Observed" | "Inferred" | "Synthetic";

export const EPISTEMIC_STATUSES: EpistemicStatus[] = [
  "Observed",
  "Inferred",
  "Synthetic",
];

export interface FirmDto {
  id: string;
  marketId: string;
  clientSegmentId: string | null;
  name: string;
  epistemicStatus: EpistemicStatus;
  attributesJson: string | null;
  notes: string | null;
}

export interface CreateFirmRequest {
  marketId: string;
  name: string;
  epistemicStatus?: EpistemicStatus;
  clientSegmentId?: string | null;
  attributesJson?: string | null;
  notes?: string | null;
}

export interface UpdateFirmRequest {
  name: string;
  epistemicStatus?: EpistemicStatus;
  clientSegmentId?: string | null;
  attributesJson?: string | null;
  notes?: string | null;
}

// ---------- Market events ----------

export type MarketEventType =
  | "ProductionIncident"
  | "ForcedMigration"
  | "PricingModelChange"
  | "RegulatoryChange"
  | "FeatureLaunch"
  | "Other";

export const MARKET_EVENT_TYPES: MarketEventType[] = [
  "ProductionIncident",
  "ForcedMigration",
  "PricingModelChange",
  "RegulatoryChange",
  "FeatureLaunch",
  "Other",
];

export type SimulationFlowKind =
  | "NewFirmArrival"
  | "ConversionToMarket"
  | "WinRate"
  | "ChurnHazard"
  | "CrossSellRate";

export const SIMULATION_FLOW_KINDS: SimulationFlowKind[] = [
  "NewFirmArrival",
  "ConversionToMarket",
  "WinRate",
  "ChurnHazard",
  "CrossSellRate",
];

export interface EventRateMultiplierDto {
  clientSegmentId: string | null;
  flowKind: SimulationFlowKind;
  multiplier: number;
}

export interface MarketEventDto {
  id: string;
  marketId: string;
  type: MarketEventType;
  name: string;
  startDate: string;
  endDate: string | null;
  magnitude: number | null;
  isHypothetical: boolean;
  competitorId: string | null;
  notes: string | null;
  rateMultipliers: EventRateMultiplierDto[];
}

export interface CreateMarketEventRequest {
  marketId: string;
  type: MarketEventType;
  name: string;
  startDate: string;
  endDate?: string | null;
  isHypothetical?: boolean;
  magnitude?: number | null;
  competitorId?: string | null;
  notes?: string | null;
  rateMultipliers?: EventRateMultiplierDto[] | null;
}

export interface UpdateMarketEventRequest {
  type: MarketEventType;
  name: string;
  startDate: string;
  endDate?: string | null;
  isHypothetical?: boolean;
  magnitude?: number | null;
  competitorId?: string | null;
  notes?: string | null;
  rateMultipliers?: EventRateMultiplierDto[] | null;
}

// ---------- Covariates ----------

export interface CovariateDto {
  id: string;
  workspaceId: string;
  name: string;
  description: string | null;
  unit: string | null;
  gbmStart: number | null;
  gbmDrift: number | null;
  gbmVol: number | null;
  hasGbmSpec: boolean;
}

export interface CreateCovariateRequest {
  workspaceId: string;
  name: string;
  description?: string | null;
  unit?: string | null;
  gbmStart?: number | null;
  gbmDrift?: number | null;
  gbmVol?: number | null;
}

export interface UpdateCovariateRequest {
  name: string;
  description?: string | null;
  unit?: string | null;
  gbmStart?: number | null;
  gbmDrift?: number | null;
  gbmVol?: number | null;
}

export interface CovariateObservationDto {
  factEventId: string;
  effectiveDate: string;
  recordedDate: string;
  value: number;
  notes: string | null;
}

export interface CovariateHistoryDto {
  covariateId: string;
  name: string;
  unit: string | null;
  observations: CovariateObservationDto[];
}

// ---------- Facts ledger ----------

export type FactEventKind =
  | "ClientWon"
  | "ClientLost"
  | "Churn"
  | "CompetitorShippedFeature"
  | "PricingChanged"
  | "IncidentOccurred"
  | "CovariateObserved"
  | "OpportunityDecided"
  | "SegmentArrivalObserved"
  | "Other";

export interface FactEventDto {
  id: string;
  workspaceId: string;
  kind: FactEventKind;
  subjectType: string | null;
  subjectId: string | null;
  effectiveDate: string;
  recordedDate: string;
  payloadJson: string | null;
  notes: string | null;
}

export interface CreateFactEventRequest {
  workspaceId: string;
  kind: FactEventKind;
  effectiveDate: string;
  subjectType?: string | null;
  subjectId?: string | null;
  payloadJson?: string | null;
  notes?: string | null;
}

// ---------- Assumption sets & parameters ----------

export type DistributionType = "Point" | "Normal" | "Lognormal" | "Beta";

export const DISTRIBUTION_TYPES: DistributionType[] = [
  "Point",
  "Normal",
  "Lognormal",
  "Beta",
];

export type ParameterProvenance = "Asserted" | "Calibrated" | "Default";

export const PARAMETER_PROVENANCES: ParameterProvenance[] = [
  "Asserted",
  "Calibrated",
  "Default",
];

export interface DistributionSpecDto {
  type: DistributionType;
  p1: number;
  p2: number;
}

export interface ParameterDto {
  id: string;
  assumptionSetId: string;
  key: string;
  distribution: DistributionSpecDto;
  provenance: ParameterProvenance;
  rationale: string | null;
  clientSegmentId: string | null;
  flowKind: SimulationFlowKind | null;
  moduleProductId: string | null;
  covariateId: string | null;
  covariateBeta: number | null;
  isEngineParameter: boolean;
}

export interface AssumptionSetDto {
  id: string;
  workspaceId: string;
  name: string;
  parentAssumptionSetId: string | null;
  createdAt: string;
  notes: string | null;
  parameters: ParameterDto[];
}

export interface CreateAssumptionSetRequest {
  workspaceId: string;
  name: string;
  notes?: string | null;
}

export interface UpdateAssumptionSetRequest {
  name: string;
  notes?: string | null;
}

export interface BranchAssumptionSetRequest {
  name: string;
}

export interface UpsertParameterRequest {
  distribution: DistributionSpecDto;
  provenance?: ParameterProvenance;
  rationale?: string | null;
  clientSegmentId?: string | null;
  flowKind?: SimulationFlowKind | null;
  moduleProductId?: string | null;
  covariateId?: string | null;
  covariateBeta?: number | null;
  key?: string | null;
}

// ---------- Simulation runs ----------

export type SimulationEngineMode = "Aggregate" | "FirmLevel";

export interface SimulationRunDto {
  id: string;
  workspaceId: string;
  roadmapId: string;
  assumptionSetId: string;
  name: string | null;
  startDate: string;
  horizonMonths: number;
  paths: number;
  seed: number;
  discountRateAnnual: number;
  terminalGrowthAnnual: number;
  modelVersion: string | null;
  isExecuted: boolean;
  elapsedMs: number | null;
  createdAt: string;
  engineMode: SimulationEngineMode;
  hasDiagnostics: boolean;
}

export interface CreateSimulationRunRequest {
  roadmapId: string;
  assumptionSetId: string;
  horizonMonths: number;
  paths: number;
  seed: number;
  discountRateAnnual: number;
  terminalGrowthAnnual: number;
  name?: string | null;
  startDate?: string | null;
  marketId?: string | null;
  eventIds?: string[] | null;
  hypotheticalEventIds?: string[] | null;
  engineMode?: SimulationEngineMode;
}

// ---------- Engine result (read-only trajectories) ----------

export interface EngineMetricTrajectory {
  mean: number[];
  p10: number[];
  p50: number[];
  p90: number[];
}

export interface EngineSegmentTrajectories {
  segmentId: string;
  clients: EngineMetricTrajectory;
  prospectPool: EngineMetricTrajectory;
  newWins: EngineMetricTrajectory;
  churned: EngineMetricTrajectory;
  bookings: EngineMetricTrajectory;
  recognizedArr: EngineMetricTrajectory;
  contributionMargin: EngineMetricTrajectory;
}

export interface EngineDistributionSummary {
  mean: number;
  p10: number;
  p50: number;
  p90: number;
}

export interface EngineResult {
  segments: EngineSegmentTrajectories[];
  total: EngineSegmentTrajectories;
  npv: EngineDistributionSummary;
  npvPerPath: number[];
  paths: number;
  seed: number;
  horizonMonths: number;
  elapsedMs: number;
}

// =====================================================================
// Phase 3 — firm-level choice engine surface
// =====================================================================

// ---------- Channels ----------

export interface ChannelSegmentReachDto {
  clientSegmentId: string;
  /** Fraction of the segment the channel can reach, [0, 1]. */
  reach: number;
  /** Fraction of the segment's opportunity flow the channel originates. */
  originationShare: number;
}

export interface ChannelProductAffinityDto {
  productId: string;
  /** Signed utility bias applied to the product; 0 is neutral. */
  bias: number;
}

export interface ChannelDto {
  id: string;
  marketId: string;
  name: string;
  description: string | null;
  originatesOpportunities: boolean;
  /** Turns the affinity list into a whitelist of carried products. */
  carriesOnlyListedProducts: boolean;
  segmentReaches: ChannelSegmentReachDto[];
  productAffinities: ChannelProductAffinityDto[];
}

export interface CreateChannelRequest {
  marketId: string;
  name: string;
  description?: string | null;
  originatesOpportunities?: boolean;
  carriesOnlyListedProducts?: boolean;
}

export interface UpdateChannelRequest {
  name: string;
  description?: string | null;
  originatesOpportunities?: boolean;
  carriesOnlyListedProducts?: boolean;
}

export type SetChannelSegmentReachRequest = ChannelSegmentReachDto;

export type SetChannelProductAffinityRequest = ChannelProductAffinityDto;

// ---------- Integrations ----------

export interface IntegrationDto {
  id: string;
  productAId: string;
  productBId: string;
  /** Quality in [0, 1]; symmetric edge. */
  qualityScore: number;
  notes: string | null;
}

export interface CreateIntegrationRequest {
  productAId: string;
  productBId: string;
  qualityScore: number;
  notes?: string | null;
}

export interface UpdateIntegrationRequest {
  qualityScore: number;
  notes?: string | null;
}

// ---------- Firm↔Product relationships ----------

export interface FirmProductRelationshipDto {
  id: string;
  firmId: string;
  productId: string;
  /** Empty = "whatever the product covers" — not yet decomposed into modules. */
  servedProcessIds: string[];
  /** Multiplier on list price: 1.0 = list, 0.8 = a 20% discount. */
  priceLevel: number;
  pricePaidPerPeriod: MoneyDto;
  acquisitionDate: string | null;
  endDate: string | null;
  isActive: boolean;
  notes: string | null;
}

export interface CreateFirmProductRelationshipRequest {
  firmId: string;
  productId: string;
  servedProcessIds?: string[] | null;
  priceLevel?: number;
  pricePaidPerPeriod?: MoneyDto;
  acquisitionDate?: string | null;
  notes?: string | null;
}

export interface UpdateFirmProductRelationshipRequest {
  servedProcessIds?: string[] | null;
  priceLevel?: number;
  pricePaidPerPeriod?: MoneyDto;
  acquisitionDate?: string | null;
  endDate?: string | null;
  notes?: string | null;
}

// ---------- Synthetic firm generation ----------

export interface SyntheticSegmentFillDto {
  clientSegmentId: string;
  prospectTarget: number;
  observedProspects: number;
  syntheticProspects: number;
  clientTarget: number;
  observedClients: number;
  syntheticClients: number;
}

export interface GenerateSyntheticFirmsResponse {
  marketId: string;
  removed: number;
  created: number;
  relationshipsCreated: number;
  segments: SyntheticSegmentFillDto[];
}

// ---------- Opportunities ----------

export type OpportunityStage =
  | "Open"
  | "Qualified"
  | "Won"
  | "Lost"
  | "NoDecision";

export const OPPORTUNITY_STAGES: OpportunityStage[] = [
  "Open",
  "Qualified",
  "Won",
  "Lost",
  "NoDecision",
];

export interface OpportunityScopeItemDto {
  businessProcessId: string;
  addedAt: string;
}

export interface OpportunityDto {
  id: string;
  firmId: string;
  channelId: string | null;
  stage: OpportunityStage;
  openedAt: string;
  qualifiedAt: string | null;
  decidedAt: string | null;
  competitorIds: string[];
  winningProductId: string | null;
  crossSellModuleProductId: string | null;
  isCrossSell: boolean;
  isDecided: boolean;
  notes: string | null;
  scope: OpportunityScopeItemDto[];
}

export interface CreateOpportunityRequest {
  firmId: string;
  openedAt?: string | null;
  channelId?: string | null;
  crossSellModuleProductId?: string | null;
  scopeProcessIds?: string[] | null;
  competitorIds?: string[] | null;
  notes?: string | null;
}

export interface UpdateOpportunityRequest {
  channelId?: string | null;
  crossSellModuleProductId?: string | null;
  competitorIds?: string[] | null;
  notes?: string | null;
}

export interface QualifyOpportunityRequest {
  at?: string | null;
}

export interface DecideOpportunityRequest {
  /** Must be Won, Lost or NoDecision; Won requires a winning product. */
  outcome: OpportunityStage;
  winningProductId?: string | null;
  at?: string | null;
}

export interface AddOpportunityScopeRequest {
  businessProcessId: string;
  addedAt?: string | null;
}

// ---------- Firm-level engine diagnostics ----------

export interface EngineSegmentFunnel {
  segmentId: string;
  opened: number;
  qualified: number;
  won: number;
  lost: number;
  noDecision: number;
  stillOpen: number;
  winRate: number;
}

export interface EngineChannelFunnel {
  channelId: string;
  opened: number;
  won: number;
  winRate: number;
}

export interface EngineFirmLevelDiagnostics {
  funnelBySegment: EngineSegmentFunnel[];
  funnelByChannel: EngineChannelFunnel[];
  avgSalesCycleMonths: number;
  expansionCount: number;
  meanUtilityGapLostDeals: number;
  syntheticFirmsCreated: number;
}

// ---------- Calibration ----------

export interface CalibrationRunDto {
  id: string;
  workspaceId: string;
  name: string | null;
  factsAsOfDate: string;
  priorAssumptionSetId: string;
  fittedAssumptionSetId: string | null;
  isExecuted: boolean;
  createdAt: string;
  notes: string | null;
}

export interface CalibrationParameterDiffDto {
  key: string;
  clientSegmentId: string | null;
  flowKind: SimulationFlowKind | null;
  moduleProductId: string | null;
  productId: string | null;
  prior: DistributionSpecDto;
  posterior: DistributionSpecDto;
  priorMean: number;
  posteriorMean: number;
  priorStdDev: number;
  posteriorStdDev: number;
  /** posterior mean − prior mean. */
  shift: number;
  sampleSize: number;
  rationale: string;
}

export interface CalibrationUnfittedDto {
  key: string;
  clientSegmentId: string | null;
  flowKind: SimulationFlowKind | null;
  moduleProductId: string | null;
  productId: string | null;
  distribution: DistributionSpecDto;
  reason: string;
}

export interface CalibrationDiffDto {
  calibrationRunId: string;
  asOfDate: string;
  priorAssumptionSetId: string;
  fittedAssumptionSetId: string | null;
  fitted: CalibrationParameterDiffDto[];
  unfitted: CalibrationUnfittedDto[];
}

export interface CalibrationRunResultDto {
  run: CalibrationRunDto;
  diff: CalibrationDiffDto;
}

export interface CreateCalibrationRunRequest {
  assumptionSetId: string;
  asOfDate?: string | null;
  name?: string | null;
  notes?: string | null;
}

/** The stored engine input snapshot — read back only for diffing/inspection. */
export interface EngineInputSnapshot {
  segments: { id: string }[];
  horizonMonths: number;
  paths: number;
  seed: number;
  discountRateAnnual: number;
  terminalGrowthAnnual: number;
  [key: string]: unknown;
}
