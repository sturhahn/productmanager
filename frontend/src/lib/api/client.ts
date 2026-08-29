/**
 * The typed API client — the single module every screen talks through.
 * Base URL comes from NEXT_PUBLIC_API_URL (default: the API's dev port).
 */
import type {
  AddProjectDependencyRequest,
  AdoptBranchRequest,
  BranchRoadmapRequest,
  CreateFeatureRequest,
  CreateInitiativeRequest,
  CreateProjectRequest,
  CreateRoadmapRequest,
  CreateTeamRequest,
  FeatureDto,
  InitiativeDto,
  LinkFeatureRequest,
  PagedResponse,
  PlanProjectRequest,
  ProblemDetails,
  ProjectDto,
  ReorderRoadmapRequest,
  RescheduleItemRequest,
  RoadmapDto,
  RoadmapFeasibilityDto,
  RoadmapItemDto,
  SetEffortDemandRequest,
  SetTeamCapacityRequest,
  TeamDto,
  TokenResponse,
  UpdateFeatureRequest,
  UpdateInitiativeRequest,
  UpdateProjectRequest,
  UpdateRoadmapRequest,
  UpdateTeamRequest,
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
    default: () => request<WorkspaceDto>("GET", "/workspaces/default"),
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
};
