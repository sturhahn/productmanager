"use client";

import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationOptions,
} from "@tanstack/react-query";
import { toast } from "sonner";
import { api, ApiError } from "@/lib/api/client";
import { useAuth } from "@/lib/auth";

export const queryKeys = {
  workspace: ["workspace"] as const,
  teams: (ws: string) => ["teams", ws] as const,
  initiatives: (ws: string) => ["initiatives", ws] as const,
  projects: (ws: string) => ["projects", ws] as const,
  features: (ws: string) => ["features", ws] as const,
  roadmaps: (ws: string) => ["roadmaps", ws] as const,
  roadmap: (id: string) => ["roadmap", id] as const,
  feasibility: (id: string) => ["feasibility", id] as const,
};

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof Error) return error.message;
  return "Something went wrong";
}

export function useWorkspace() {
  const { authenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.workspace,
    queryFn: () => api.workspaces.default(),
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
