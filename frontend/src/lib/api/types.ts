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
