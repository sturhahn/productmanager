"use client";

import * as React from "react";
import {
  useMutation,
  useQueries,
  useQuery,
  useQueryClient,
  type UseMutationOptions,
} from "@tanstack/react-query";
import { toast } from "sonner";
import { activeWorkspaceStore, api, ApiError } from "@/lib/api/client";
import type { FirmDto, FirmProductRelationshipDto } from "@/lib/api/types";
import { activeMarketStore } from "@/lib/market-store";
import { useAuth } from "@/lib/auth";

export const queryKeys = {
  workspaces: ["workspaces"] as const,
  workspace: (id: string | null) => ["workspace", id ?? "default"] as const,
  teams: (ws: string) => ["teams", ws] as const,
  initiatives: (ws: string) => ["initiatives", ws] as const,
  projects: (ws: string) => ["projects", ws] as const,
  features: (ws: string) => ["features", ws] as const,
  roadmaps: (ws: string) => ["roadmaps", ws] as const,
  roadmap: (id: string) => ["roadmap", id] as const,
  feasibility: (id: string) => ["feasibility", id] as const,
  markets: (ws: string) => ["markets", ws] as const,
  segments: (marketId: string) => ["segments", marketId] as const,
  allSegments: (ws: string) => ["all-segments", ws] as const,
  competitors: (marketId: string) => ["competitors", marketId] as const,
  products: (marketId: string) => ["products", marketId] as const,
  allProducts: (ws: string) => ["all-products", ws] as const,
  processes: (marketId: string) => ["processes", marketId] as const,
  allBusinessProcesses: (ws: string) => ["all-processes", ws] as const,
  approaches: ["approaches"] as const,
  coverages: ["coverages"] as const,
  needs: (scope: string) => ["needs", scope] as const,
  marketEvents: (ws: string) => ["market-events", ws] as const,
  covariates: (ws: string) => ["covariates", ws] as const,
  assumptionSets: (ws: string) => ["assumption-sets", ws] as const,
  assumptionSet: (id: string) => ["assumption-set", id] as const,
  simulationRuns: (ws: string) => ["simulation-runs", ws] as const,
  simulationResult: (id: string) => ["simulation-result", id] as const,
  simulationInput: (id: string) => ["simulation-input", id] as const,
  simulationDiagnostics: (id: string) =>
    ["simulation-diagnostics", id] as const,
  channels: (marketId: string) => ["channels", marketId] as const,
  integrations: (productId: string) => ["integrations", productId] as const,
  firmsPage: (
    marketId: string,
    segment: string,
    status: string,
    page: number,
  ) => ["firms-page", marketId, segment, status, page] as const,
  firm: (id: string) => ["firm", id] as const,
  firmRelationships: (scope: string) => ["firm-relationships", scope] as const,
  opportunities: (ws: string) => ["opportunities", ws] as const,
  calibrationRuns: (ws: string) => ["calibration-runs", ws] as const,
  calibrationDiff: (id: string) => ["calibration-diff", id] as const,
};

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof Error) return error.message;
  return "Something went wrong";
}

const getServerActiveWorkspaceId = () => null;

/** The workspace id the client currently has active, read reactively from storage. */
export function useActiveWorkspaceId(): string | null {
  return React.useSyncExternalStore(
    activeWorkspaceStore.subscribe,
    activeWorkspaceStore.get,
    getServerActiveWorkspaceId,
  );
}

/** Switch the active workspace — every workspace-scoped query is keyed off this. */
export function setActiveWorkspace(id: string) {
  activeWorkspaceStore.set(id);
}

/** All workspaces the user can switch between, for the sidebar switcher. */
export function useWorkspaces() {
  const { authenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.workspaces,
    queryFn: () => api.workspaces.list(),
    enabled: authenticated,
    select: (page) => page.items,
  });
}

/**
 * The active workspace. Resolves the id persisted in localStorage; falls
 * back to (and persists) /workspaces/default when unset, or when the stored
 * id no longer exists (e.g. the workspace was deleted elsewhere).
 */
export function useWorkspace() {
  const { authenticated } = useAuth();
  const queryClient = useQueryClient();
  const activeId = useActiveWorkspaceId();

  return useQuery({
    queryKey: queryKeys.workspace(activeId),
    queryFn: async () => {
      if (activeId) {
        try {
          return await api.workspaces.get(activeId);
        } catch (error) {
          if (!(error instanceof ApiError) || error.status !== 404) throw error;
          // Stale id — the workspace no longer exists. Fall through to default.
        }
      }
      const workspace = await api.workspaces.default();
      queryClient.setQueryData(queryKeys.workspace(workspace.id), workspace);
      activeWorkspaceStore.set(workspace.id);
      return workspace;
    },
    enabled: authenticated,
    staleTime: Infinity,
  });
}

export function useTeams(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.teams(workspaceId ?? ""),
    queryFn: () => api.teams.list(workspaceId!),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useInitiatives(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.initiatives(workspaceId ?? ""),
    queryFn: () => api.initiatives.list(workspaceId!),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useProjects(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.projects(workspaceId ?? ""),
    queryFn: () => api.projects.list(workspaceId!),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useFeatures(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.features(workspaceId ?? ""),
    queryFn: () => api.features.list(workspaceId!),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useRoadmaps(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.roadmaps(workspaceId ?? ""),
    queryFn: () => api.roadmaps.list(workspaceId!),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useRoadmap(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.roadmap(id ?? ""),
    queryFn: () => api.roadmaps.get(id!),
    enabled: !!id,
  });
}

export function useFeasibility(roadmapId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.feasibility(roadmapId ?? ""),
    queryFn: () => api.roadmaps.feasibility(roadmapId!),
    enabled: !!roadmapId,
  });
}

/**
 * A mutation that invalidates the given query keys on success and toasts the
 * API's problem detail on failure.
 */
export function useInvalidatingMutation<TData, TVariables>(
  mutationFn: (variables: TVariables) => Promise<TData>,
  invalidate: () => readonly (readonly unknown[])[],
  options?: Pick<
    UseMutationOptions<TData, unknown, TVariables>,
    "onSuccess"
  > & { successMessage?: string },
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: (data, variables, onMutateResult, context) => {
      for (const key of invalidate()) {
        void queryClient.invalidateQueries({ queryKey: key });
      }
      if (options?.successMessage) toast.success(options.successMessage);
      options?.onSuccess?.(data, variables, onMutateResult, context);
    },
    onError: (error: unknown) => {
      toast.error(errorMessage(error));
    },
  });
}

// =====================================================================
// Phase 2 — market model & simulation hooks
// =====================================================================

export function useMarkets(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.markets(workspaceId ?? ""),
    queryFn: () => api.markets.list(workspaceId!),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

const getServerActiveMarketId = () => null;

/**
 * The market the Market screens are scoped to: the persisted choice when it
 * still exists, otherwise the workspace's first market.
 */
export function useActiveMarket(workspaceId: string | undefined) {
  const markets = useMarkets(workspaceId);
  const storedId = React.useSyncExternalStore(
    activeMarketStore.subscribe,
    () => (workspaceId ? activeMarketStore.get(workspaceId) : null),
    getServerActiveMarketId,
  );
  const market =
    markets.data?.find((m) => m.id === storedId) ?? markets.data?.[0] ?? null;
  return { markets, market };
}

export function setActiveMarket(workspaceId: string, marketId: string) {
  activeMarketStore.set(workspaceId, marketId);
}

export function useSegments(marketId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.segments(marketId ?? ""),
    queryFn: () => api.clientSegments.list(marketId!),
    enabled: !!marketId,
    select: (page) => page.items,
  });
}

/** Every segment across every market of the workspace (simulation results span markets). */
export function useAllSegments(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.allSegments(workspaceId ?? ""),
    queryFn: async () => {
      const markets = await api.markets.list(workspaceId!);
      const pages = await Promise.all(
        markets.items.map((m) => api.clientSegments.list(m.id)),
      );
      return pages.flatMap((p) => p.items);
    },
    enabled: !!workspaceId,
  });
}

export function useCompetitors(marketId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.competitors(marketId ?? ""),
    queryFn: () => api.competitors.list(marketId!),
    enabled: !!marketId,
    select: (page) => page.items,
  });
}

export function useProducts(marketId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.products(marketId ?? ""),
    queryFn: () => api.products.list(marketId!),
    enabled: !!marketId,
    select: (page) => page.items,
  });
}

export function useBusinessProcesses(marketId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.processes(marketId ?? ""),
    queryFn: () => api.businessProcesses.list(marketId!),
    enabled: !!marketId,
    select: (page) => page.items,
  });
}

/** Every product across every market of the workspace — used where a screen
 * (e.g. Features) needs to resolve "our" products without being scoped to a
 * single active market. */
export function useAllProducts(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.allProducts(workspaceId ?? ""),
    queryFn: async () => {
      const markets = await api.markets.list(workspaceId!);
      const pages = await Promise.all(
        markets.items.map((m) => api.products.list(m.id)),
      );
      return pages.flatMap((p) => p.items);
    },
    enabled: !!workspaceId,
  });
}

/** Every business process across every market of the workspace — used where
 * a screen (e.g. Features) needs to label a process by id without being
 * scoped to a single active market. */
export function useAllBusinessProcesses(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.allBusinessProcesses(workspaceId ?? ""),
    queryFn: async () => {
      const markets = await api.markets.list(workspaceId!);
      const pages = await Promise.all(
        markets.items.map((m) => api.businessProcesses.list(m.id)),
      );
      return pages.flatMap((p) => p.items);
    },
    enabled: !!workspaceId,
  });
}

export function useApproaches() {
  const { authenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.approaches,
    queryFn: () => api.approaches.list(),
    enabled: authenticated,
    select: (page) => page.items,
  });
}

export function useCoverages() {
  const { authenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.coverages,
    queryFn: () => api.coverages.list({}),
    enabled: authenticated,
    select: (page) => page.items,
  });
}

export function useSegmentNeeds(clientSegmentId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.needs(clientSegmentId ?? ""),
    queryFn: () => api.needs.list({ clientSegmentId: clientSegmentId! }),
    enabled: !!clientSegmentId,
    select: (page) => page.items,
  });
}

export function useMarketEvents(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.marketEvents(workspaceId ?? ""),
    queryFn: () => api.marketEvents.list({}),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useCovariates(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.covariates(workspaceId ?? ""),
    queryFn: () => api.covariates.list(workspaceId!),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useAssumptionSets(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.assumptionSets(workspaceId ?? ""),
    queryFn: () => api.assumptionSets.list(workspaceId!),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useSimulationRuns(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.simulationRuns(workspaceId ?? ""),
    queryFn: () => api.simulationRuns.list({ workspaceId: workspaceId! }),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useSimulationResult(runId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.simulationResult(runId ?? ""),
    queryFn: () => api.simulationRuns.result(runId!),
    enabled: !!runId,
    staleTime: Infinity, // runs are immutable
  });
}

// =====================================================================
// Phase 3 — firm-level choice engine hooks
// =====================================================================

export function useSimulationDiagnostics(
  runId: string | undefined,
  enabled: boolean,
) {
  return useQuery({
    queryKey: queryKeys.simulationDiagnostics(runId ?? ""),
    queryFn: () => api.simulationRuns.diagnostics(runId!),
    enabled: !!runId && enabled,
    staleTime: Infinity, // runs are immutable
  });
}

/** Every channel across every market — simulation results span markets. */
export function useAllChannels(workspaceId: string | undefined) {
  return useQuery({
    queryKey: ["all-channels", workspaceId ?? ""],
    queryFn: async () => {
      const markets = await api.markets.list(workspaceId!);
      const pages = await Promise.all(
        markets.items.map((m) => api.channels.list(m.id)),
      );
      return pages.flatMap((p) => p.items);
    },
    enabled: !!workspaceId,
  });
}

export function useChannels(marketId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.channels(marketId ?? ""),
    queryFn: () => api.channels.list(marketId!),
    enabled: !!marketId,
    select: (page) => page.items,
  });
}

export function useIntegrations(productId?: string) {
  const { authenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.integrations(productId ?? "all"),
    queryFn: () => api.integrations.list(productId),
    enabled: authenticated,
    select: (page) => page.items,
  });
}

/** One server-side page of the firm roster with optional filters. */
export function useFirmsPage(
  marketId: string | undefined,
  filters: { clientSegmentId?: string; epistemicStatus?: string },
  page: number,
  pageSize: number,
) {
  return useQuery({
    queryKey: queryKeys.firmsPage(
      marketId ?? "",
      filters.clientSegmentId ?? "all",
      filters.epistemicStatus ?? "all",
      page,
    ),
    queryFn: () =>
      api.firms.page({ marketId: marketId!, ...filters }, page, pageSize),
    enabled: !!marketId,
    placeholderData: (previous) => previous,
  });
}

/** Resolve a set of firms by id (for opportunity rows). Cached per firm. */
export function useFirmsById(firmIds: string[]): Map<string, FirmDto> {
  return useQueries({
    queries: firmIds.map((id) => ({
      queryKey: queryKeys.firm(id),
      queryFn: () => api.firms.get(id),
      staleTime: 60_000,
    })),
    combine: (results) => {
      const map = new Map<string, FirmDto>();
      for (const r of results) {
        if (r.data) map.set(r.data.id, r.data);
      }
      return map;
    },
  });
}

/** Active relationships for one page of firms — one small query per firm. */
export function useRelationshipsForFirms(
  firmIds: string[],
): Map<string, FirmProductRelationshipDto[]> {
  return useQueries({
    queries: firmIds.map((id) => ({
      queryKey: queryKeys.firmRelationships(`firm:${id}`),
      queryFn: () => api.firmRelationships.list({ firmId: id }),
      staleTime: 30_000,
    })),
    combine: (results) => {
      const map = new Map<string, FirmProductRelationshipDto[]>();
      results.forEach((r, i) => {
        if (r.data) map.set(firmIds[i], r.data.items);
      });
      return map;
    },
  });
}

export function useOpportunities(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.opportunities(workspaceId ?? ""),
    queryFn: () => api.opportunities.list({}),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useCalibrationRuns(workspaceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.calibrationRuns(workspaceId ?? ""),
    queryFn: () => api.calibrationRuns.list(workspaceId!),
    enabled: !!workspaceId,
    select: (page) => page.items,
  });
}

export function useCalibrationDiff(runId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.calibrationDiff(runId ?? ""),
    queryFn: () => api.calibrationRuns.diff(runId!),
    enabled: !!runId,
    staleTime: Infinity, // calibration runs are immutable
  });
}
