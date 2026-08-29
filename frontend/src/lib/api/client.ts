/**
 * The typed API client — the single module every screen talks through.
 * Base URL comes from NEXT_PUBLIC_API_URL (default: the API's dev port).
 */
import type {
  AddProjectDependencyRequest,
  AdoptBranchRequest,
  ApproachDto,
  AssumptionSetDto,
  BranchAssumptionSetRequest,
  BranchRoadmapRequest,
  BusinessProcessDto,
  ClientSegmentDto,
  CompetitorDto,
  CovariateDto,
  CovariateHistoryDto,
  CoverageDto,
  CreateApproachRequest,
  CreateAssumptionSetRequest,
  CreateBusinessProcessRequest,
  CreateClientSegmentRequest,
  CreateCompetitorRequest,
  CreateCovariateRequest,
  CreateCoverageRequest,
  CreateFactEventRequest,
  CreateFeatureRequest,
  CreateFirmRequest,
  CreateInitiativeRequest,
  CreateMarketEventRequest,
  CreateMarketRequest,
  CreateNeedRequest,
  CreateProductRequest,
  CreateProjectRequest,
  CreateRoadmapRequest,
  CreateSimulationRunRequest,
  CreateTeamRequest,
  CreateWorkspaceRequest,
  EngineInputSnapshot,
  EngineResult,
  FactEventDto,
  FactEventKind,
  FeatureDto,
  FirmDto,
  InitiativeDto,
  LinkFeatureRequest,
  MarketDto,
  MarketEventDto,
  NeedDto,
  PagedResponse,
  PlanProjectRequest,
  ProblemDetails,
  ProductDto,
  ProjectDto,
  ReorderRoadmapRequest,
  RescheduleItemRequest,
  RoadmapDto,
  RoadmapFeasibilityDto,
  RoadmapItemDto,
  SetEffortDemandRequest,
  SetProductPriceRequest,
  SetSegmentModulePenetrationRequest,
  SetSegmentStockStateRequest,
  SetTeamCapacityRequest,
  SimulationRunDto,
  TeamDto,
  TokenResponse,
  UpdateApproachRequest,
  UpdateAssumptionSetRequest,
  UpdateBusinessProcessRequest,
  UpdateClientSegmentRequest,
  UpdateCompetitorRequest,
  UpdateCovariateRequest,
  UpdateCoverageRequest,
  UpdateFeatureRequest,
  UpdateFirmRequest,
  UpdateInitiativeRequest,
  UpdateMarketEventRequest,
  UpdateMarketRequest,
  UpdateNeedRequest,
  UpdateProductRequest,
  UpdateProjectRequest,
  UpdateRoadmapRequest,
  UpdateTeamRequest,
  UpdateWorkspaceRequest,
  UpsertParameterRequest,
  WorkspaceDto,
} from "./types";

export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5072";

const TOKEN_KEY = "productmanager.token";
const TOKEN_EVENT = "productmanager:token-changed";

function notifyTokenChanged() {
  if (typeof window !== "undefined") {
    window.dispatchEvent(new Event(TOKEN_EVENT));
  }
}

export const tokenStore = {
  get(): string | null {
    if (typeof window === "undefined") return null;
    try {
      return window.localStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
  },
  set(token: string) {
    try {
      window.localStorage.setItem(TOKEN_KEY, token);
    } catch {
      // storage unavailable — session-only auth
    }
    notifyTokenChanged();
  },
  clear() {
    try {
      window.localStorage.removeItem(TOKEN_KEY);
    } catch {
      // ignore
    }
    notifyTokenChanged();
  },
  /** Subscribe to token changes (this tab and others). */
  subscribe(callback: () => void): () => void {
    window.addEventListener(TOKEN_EVENT, callback);
    window.addEventListener("storage", callback);
    return () => {
      window.removeEventListener(TOKEN_EVENT, callback);
      window.removeEventListener("storage", callback);
    };
  },
};

const ACTIVE_WORKSPACE_KEY = "productmanager.activeWorkspaceId";
const ACTIVE_WORKSPACE_EVENT = "productmanager:workspace-changed";

function notifyActiveWorkspaceChanged() {
  if (typeof window !== "undefined") {
    window.dispatchEvent(new Event(ACTIVE_WORKSPACE_EVENT));
  }
}

/**
 * The client's chosen workspace, persisted so it survives a reload. Falls
 * back to the oldest ("default") workspace when unset, or when the stored id
 * no longer resolves — see useWorkspace in lib/queries.ts.
 */
export const activeWorkspaceStore = {
  get(): string | null {
    if (typeof window === "undefined") return null;
    try {
      return window.localStorage.getItem(ACTIVE_WORKSPACE_KEY);
    } catch {
      return null;
    }
  },
  set(id: string) {
    try {
      window.localStorage.setItem(ACTIVE_WORKSPACE_KEY, id);
    } catch {
      // storage unavailable — the session falls back to /workspaces/default
    }
    notifyActiveWorkspaceChanged();
  },
  clear() {
    try {
      window.localStorage.removeItem(ACTIVE_WORKSPACE_KEY);
    } catch {
      // ignore
    }
    notifyActiveWorkspaceChanged();
  },
  /** Subscribe to active-workspace changes (this tab and others). */
  subscribe(callback: () => void): () => void {
    window.addEventListener(ACTIVE_WORKSPACE_EVENT, callback);
    window.addEventListener("storage", callback);
    return () => {
      window.removeEventListener(ACTIVE_WORKSPACE_EVENT, callback);
      window.removeEventListener("storage", callback);
    };
  },
};

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;

  constructor(status: number, message: string, problem: ProblemDetails | null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }
}

type Query = Record<string, string | number | boolean | undefined>;

function buildUrl(path: string, query?: Query): string {
  const url = new URL(`/api${path}`, API_BASE_URL);
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined) url.searchParams.set(key, String(value));
    }
  }
  return url.toString();
}

async function request<T>(
  method: string,
  path: string,
  options: { query?: Query; body?: unknown; auth?: boolean } = {},
): Promise<T> {
  const headers: Record<string, string> = {};
  if (options.body !== undefined) headers["Content-Type"] = "application/json";
  if (options.auth !== false) {
    const token = tokenStore.get();
    if (token) headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(buildUrl(path, options.query), {
    method,
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
  });

  if (response.status === 401 && options.auth !== false) {
    tokenStore.clear();
    throw new ApiError(401, "Your session has expired. Sign in again.", null);
  }

  if (!response.ok) {
    let problem: ProblemDetails | null = null;
    try {
      problem = (await response.json()) as ProblemDetails;
    } catch {
      // non-JSON error body
    }
    const validation = problem?.errors
      ? Object.values(problem.errors).flat().join(" ")
      : null;
    const message =
      problem?.detail ??
      validation ??
      problem?.title ??
      `Request failed (${response.status})`;
    throw new ApiError(response.status, message, problem);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

const paging = { page: 1, pageSize: 200 };

export const api = {
  auth: {
    token: (password: string) =>
      request<TokenResponse>("POST", "/auth/token", {
        body: { password },
        auth: false,
      }),
    me: () => request<{ subject: string }>("GET", "/auth/me"),
  },

  workspaces: {
    list: () =>
      request<PagedResponse<WorkspaceDto>>("GET", "/workspaces", {
        query: paging,
      }),
    default: () => request<WorkspaceDto>("GET", "/workspaces/default"),
    get: (id: string) => request<WorkspaceDto>("GET", `/workspaces/${id}`),
    create: (body: CreateWorkspaceRequest) =>
      request<WorkspaceDto>("POST", "/workspaces", { body }),
    update: (id: string, body: UpdateWorkspaceRequest) =>
      request<WorkspaceDto>("PUT", `/workspaces/${id}`, { body }),
  },

  teams: {
    list: (workspaceId: string) =>
      request<PagedResponse<TeamDto>>("GET", "/teams", {
        query: { workspaceId, ...paging },
      }),
    create: (body: CreateTeamRequest) =>
      request<TeamDto>("POST", "/teams", { body }),
    update: (id: string, body: UpdateTeamRequest) =>
      request<TeamDto>("PUT", `/teams/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/teams/${id}`),
    setCapacity: (id: string, body: SetTeamCapacityRequest) =>
      request<TeamDto>("PUT", `/teams/${id}/capacities`, { body }),
  },

  initiatives: {
    list: (workspaceId: string) =>
      request<PagedResponse<InitiativeDto>>("GET", "/initiatives", {
        query: { workspaceId, ...paging },
      }),
    create: (body: CreateInitiativeRequest) =>
      request<InitiativeDto>("POST", "/initiatives", { body }),
    update: (id: string, body: UpdateInitiativeRequest) =>
      request<InitiativeDto>("PUT", `/initiatives/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/initiatives/${id}`),
  },

  projects: {
    list: (workspaceId: string) =>
      request<PagedResponse<ProjectDto>>("GET", "/projects", {
        query: { workspaceId, ...paging },
      }),
    create: (body: CreateProjectRequest) =>
      request<ProjectDto>("POST", "/projects", { body }),
    update: (id: string, body: UpdateProjectRequest) =>
      request<ProjectDto>("PUT", `/projects/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/projects/${id}`),
    addDependency: (id: string, body: AddProjectDependencyRequest) =>
      request<ProjectDto>("POST", `/projects/${id}/dependencies`, { body }),
    removeDependency: (id: string, dependsOnProjectId: string) =>
      request<void>(
        "DELETE",
        `/projects/${id}/dependencies/${dependsOnProjectId}`,
      ),
    setEffortDemand: (id: string, body: SetEffortDemandRequest) =>
      request<ProjectDto>("PUT", `/projects/${id}/effort-demands`, { body }),
    linkFeature: (id: string, body: LinkFeatureRequest) =>
      request<ProjectDto>("POST", `/projects/${id}/features`, { body }),
    unlinkFeature: (id: string, featureId: string) =>
      request<void>("DELETE", `/projects/${id}/features/${featureId}`),
  },

  features: {
    list: (workspaceId: string) =>
      request<PagedResponse<FeatureDto>>("GET", "/features", {
        query: { workspaceId, ...paging },
      }),
    create: (body: CreateFeatureRequest) =>
      request<FeatureDto>("POST", "/features", { body }),
    update: (id: string, body: UpdateFeatureRequest) =>
      request<FeatureDto>("PUT", `/features/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/features/${id}`),
  },

  roadmaps: {
    list: (workspaceId: string, isLive?: boolean) =>
      request<PagedResponse<RoadmapDto>>("GET", "/roadmaps", {
        query: { workspaceId, isLive, ...paging },
      }),
    get: (id: string) => request<RoadmapDto>("GET", `/roadmaps/${id}`),
    create: (body: CreateRoadmapRequest) =>
      request<RoadmapDto>("POST", "/roadmaps", { body }),
    update: (id: string, body: UpdateRoadmapRequest) =>
      request<RoadmapDto>("PUT", `/roadmaps/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/roadmaps/${id}`),
    makeLive: (id: string) =>
      request<RoadmapDto>("POST", `/roadmaps/${id}/make-live`),
    planItem: (id: string, body: PlanProjectRequest) =>
      request<RoadmapItemDto>("POST", `/roadmaps/${id}/items`, { body }),
    rescheduleItem: (
      id: string,
      projectId: string,
      body: RescheduleItemRequest,
    ) =>
      request<RoadmapItemDto>("PUT", `/roadmaps/${id}/items/${projectId}`, {
        body,
      }),
    unplanItem: (id: string, projectId: string) =>
      request<void>("DELETE", `/roadmaps/${id}/items/${projectId}`),
    reorder: (id: string, body: ReorderRoadmapRequest) =>
      request<RoadmapDto>("PUT", `/roadmaps/${id}/order`, { body }),
    branch: (id: string, body: BranchRoadmapRequest) =>
      request<RoadmapDto>("POST", `/roadmaps/${id}/branch`, { body }),
    adopt: (id: string, body: AdoptBranchRequest) =>
      request<RoadmapDto>("POST", `/roadmaps/${id}/adopt`, { body }),
    feasibility: (id: string) =>
      request<RoadmapFeasibilityDto>("GET", `/roadmaps/${id}/feasibility`),
  },

  // ------------------------------------------------------------------
  // Phase 2 — market model & simulation
  // ------------------------------------------------------------------

  markets: {
    list: (workspaceId: string) =>
      request<PagedResponse<MarketDto>>("GET", "/markets", {
        query: { workspaceId, ...paging },
      }),
    get: (id: string) => request<MarketDto>("GET", `/markets/${id}`),
    create: (body: CreateMarketRequest) =>
      request<MarketDto>("POST", "/markets", { body }),
    update: (id: string, body: UpdateMarketRequest) =>
      request<MarketDto>("PUT", `/markets/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/markets/${id}`),
  },

  clientSegments: {
    list: (marketId: string) =>
      request<PagedResponse<ClientSegmentDto>>("GET", "/client-segments", {
        query: { marketId, ...paging },
      }),
    get: (id: string) =>
      request<ClientSegmentDto>("GET", `/client-segments/${id}`),
    create: (body: CreateClientSegmentRequest) =>
      request<ClientSegmentDto>("POST", "/client-segments", { body }),
    update: (id: string, body: UpdateClientSegmentRequest) =>
      request<ClientSegmentDto>("PUT", `/client-segments/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/client-segments/${id}`),
    setStockState: (id: string, body: SetSegmentStockStateRequest) =>
      request<ClientSegmentDto>("PUT", `/client-segments/${id}/stock-state`, {
        body,
      }),
    setModulePenetration: (
      id: string,
      body: SetSegmentModulePenetrationRequest,
    ) =>
      request<ClientSegmentDto>(
        "PUT",
        `/client-segments/${id}/module-penetrations`,
        { body },
      ),
    removeModulePenetration: (id: string, moduleProductId: string) =>
      request<void>(
        "DELETE",
        `/client-segments/${id}/module-penetrations/${moduleProductId}`,
      ),
  },

  competitors: {
    list: (marketId: string) =>
      request<PagedResponse<CompetitorDto>>("GET", "/competitors", {
        query: { marketId, ...paging },
      }),
    create: (body: CreateCompetitorRequest) =>
      request<CompetitorDto>("POST", "/competitors", { body }),
    update: (id: string, body: UpdateCompetitorRequest) =>
      request<CompetitorDto>("PUT", `/competitors/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/competitors/${id}`),
  },

  products: {
    list: (marketId: string) =>
      request<PagedResponse<ProductDto>>("GET", "/products", {
        query: { marketId, ...paging },
      }),
    create: (body: CreateProductRequest) =>
      request<ProductDto>("POST", "/products", { body }),
    update: (id: string, body: UpdateProductRequest) =>
      request<ProductDto>("PUT", `/products/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/products/${id}`),
    setPrice: (id: string, body: SetProductPriceRequest) =>
      request<ProductDto>("PUT", `/products/${id}/prices`, { body }),
    removePrice: (id: string, clientSegmentId?: string) =>
      request<void>("DELETE", `/products/${id}/prices`, {
        query: { clientSegmentId },
      }),
  },

  businessProcesses: {
    list: (marketId: string) =>
      request<PagedResponse<BusinessProcessDto>>("GET", "/business-processes", {
        query: { marketId, ...paging },
      }),
    create: (body: CreateBusinessProcessRequest) =>
      request<BusinessProcessDto>("POST", "/business-processes", { body }),
    update: (id: string, body: UpdateBusinessProcessRequest) =>
      request<BusinessProcessDto>("PUT", `/business-processes/${id}`, { body }),
    delete: (id: string) =>
      request<void>("DELETE", `/business-processes/${id}`),
  },

  approaches: {
    list: (businessProcessId?: string) =>
      request<PagedResponse<ApproachDto>>("GET", "/approaches", {
        query: { businessProcessId, ...paging },
      }),
    create: (body: CreateApproachRequest) =>
      request<ApproachDto>("POST", "/approaches", { body }),
    update: (id: string, body: UpdateApproachRequest) =>
      request<ApproachDto>("PUT", `/approaches/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/approaches/${id}`),
  },

  coverages: {
    list: (filters: {
      productId?: string;
      businessProcessId?: string;
      clientSegmentId?: string;
    }) =>
      request<PagedResponse<CoverageDto>>("GET", "/coverages", {
        query: { ...filters, ...paging },
      }),
    upsert: (body: CreateCoverageRequest) =>
      request<CoverageDto>("POST", "/coverages", { body }),
    update: (id: string, body: UpdateCoverageRequest) =>
      request<CoverageDto>("PUT", `/coverages/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/coverages/${id}`),
  },

  needs: {
    list: (filters: {
      firmId?: string;
      clientSegmentId?: string;
      businessProcessId?: string;
    }) =>
      request<PagedResponse<NeedDto>>("GET", "/needs", {
        query: { ...filters, ...paging },
      }),
    create: (body: CreateNeedRequest) =>
      request<NeedDto>("POST", "/needs", { body }),
    update: (id: string, body: UpdateNeedRequest) =>
      request<NeedDto>("PUT", `/needs/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/needs/${id}`),
  },

  firms: {
    list: (filters: {
      marketId?: string;
      clientSegmentId?: string;
      epistemicStatus?: string;
    }) =>
      request<PagedResponse<FirmDto>>("GET", "/firms", {
        query: { ...filters, ...paging },
      }),
    create: (body: CreateFirmRequest) =>
      request<FirmDto>("POST", "/firms", { body }),
    update: (id: string, body: UpdateFirmRequest) =>
      request<FirmDto>("PUT", `/firms/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/firms/${id}`),
  },

  marketEvents: {
    list: (filters: {
      marketId?: string;
      isHypothetical?: boolean;
      competitorId?: string;
    }) =>
      request<PagedResponse<MarketEventDto>>("GET", "/market-events", {
        query: { ...filters, ...paging },
      }),
    create: (body: CreateMarketEventRequest) =>
      request<MarketEventDto>("POST", "/market-events", { body }),
    update: (id: string, body: UpdateMarketEventRequest) =>
      request<MarketEventDto>("PUT", `/market-events/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/market-events/${id}`),
  },

  covariates: {
    list: (workspaceId: string) =>
      request<PagedResponse<CovariateDto>>("GET", "/covariates", {
        query: { workspaceId, ...paging },
      }),
    create: (body: CreateCovariateRequest) =>
      request<CovariateDto>("POST", "/covariates", { body }),
    update: (id: string, body: UpdateCovariateRequest) =>
      request<CovariateDto>("PUT", `/covariates/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/covariates/${id}`),
    history: (
      id: string,
      range?: {
        effectiveFrom?: string;
        effectiveTo?: string;
        knownAsOf?: string;
      },
    ) =>
      request<CovariateHistoryDto>("GET", `/covariates/${id}/history`, {
        query: range,
      }),
  },

  facts: {
    list: (filters: {
      workspaceId: string;
      kind?: FactEventKind;
      subjectType?: string;
      subjectId?: string;
      effectiveFrom?: string;
      effectiveTo?: string;
      knownAsOf?: string;
    }) =>
      request<PagedResponse<FactEventDto>>("GET", "/facts", {
        query: { ...filters, ...paging },
      }),
    create: (body: CreateFactEventRequest) =>
      request<FactEventDto>("POST", "/facts", { body }),
  },

  assumptionSets: {
    list: (workspaceId: string) =>
      request<PagedResponse<AssumptionSetDto>>("GET", "/assumption-sets", {
        query: { workspaceId, ...paging },
      }),
    get: (id: string) =>
      request<AssumptionSetDto>("GET", `/assumption-sets/${id}`),
    create: (body: CreateAssumptionSetRequest) =>
      request<AssumptionSetDto>("POST", "/assumption-sets", { body }),
    update: (id: string, body: UpdateAssumptionSetRequest) =>
      request<AssumptionSetDto>("PUT", `/assumption-sets/${id}`, { body }),
    delete: (id: string) => request<void>("DELETE", `/assumption-sets/${id}`),
    branch: (id: string, body: BranchAssumptionSetRequest) =>
      request<AssumptionSetDto>("POST", `/assumption-sets/${id}/branch`, {
        body,
      }),
    upsertParameter: (id: string, body: UpsertParameterRequest) =>
      request<AssumptionSetDto>("PUT", `/assumption-sets/${id}/parameters`, {
        body,
      }),
    removeParameter: (id: string, parameterId: string) =>
      request<void>(
        "DELETE",
        `/assumption-sets/${id}/parameters/${parameterId}`,
      ),
  },

  simulationRuns: {
    list: (filters: {
      workspaceId?: string;
      roadmapId?: string;
      assumptionSetId?: string;
    }) =>
      request<PagedResponse<SimulationRunDto>>("GET", "/simulation-runs", {
        query: { ...filters, ...paging },
      }),
    get: (id: string) =>
      request<SimulationRunDto>("GET", `/simulation-runs/${id}`),
    create: (body: CreateSimulationRunRequest) =>
      request<SimulationRunDto>("POST", "/simulation-runs", { body }),
    delete: (id: string) => request<void>("DELETE", `/simulation-runs/${id}`),
    result: (id: string) =>
      request<EngineResult>("GET", `/simulation-runs/${id}/result`),
    input: (id: string) =>
      request<EngineInputSnapshot>("GET", `/simulation-runs/${id}/input`),
  },
};
