"use client";

import * as React from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  GitBranchIcon,
  GitMergeIcon,
  PlusIcon,
  RadioIcon,
} from "lucide-react";
import { api } from "@/lib/api/client";
import type { RoadmapDto } from "@/lib/api/types";
import { addDays, todayIso } from "@/lib/timeline";
import {
  errorMessage,
  queryKeys,
  useFeasibility,
  useInitiatives,
  useProjects,
  useRoadmap,
  useRoadmaps,
  useTeams,
  useWorkspace,
} from "@/lib/queries";
import { PageHeader, EmptyState } from "@/components/page-header";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { RoadmapTimeline } from "@/components/roadmap/timeline";
import { WarningsPanel } from "@/components/roadmap/warnings-panel";
import { UnplannedTray } from "@/components/roadmap/unplanned-tray";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

export default function RoadmapPage() {
  const queryClient = useQueryClient();
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;

  const roadmaps = useRoadmaps(workspaceId);
  const projects = useProjects(workspaceId);
  const initiatives = useInitiatives(workspaceId);
  const teams = useTeams(workspaceId);

  // Selection is derived: an explicit valid choice wins, otherwise the live
  // roadmap, otherwise the first one.
  const [chosenId, setSelectedId] = React.useState<string | null>(null);
  const selectedId = React.useMemo(() => {
    const list = roadmaps.data ?? [];
    if (chosenId && list.some((r) => r.id === chosenId)) return chosenId;
    return (list.find((r) => r.isLive) ?? list[0])?.id ?? null;
  }, [chosenId, roadmaps.data]);

  const roadmap = useRoadmap(selectedId ?? undefined);
  const feasibility = useFeasibility(selectedId ?? undefined);

  const roadmapKey = queryKeys.roadmap(selectedId ?? "");
  const invalidateRoadmap = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: roadmapKey });
    void queryClient.invalidateQueries({
      queryKey: queryKeys.feasibility(selectedId ?? ""),
    });
  }, [queryClient, roadmapKey, selectedId]);

  // ----- optimistic reschedule (drag / resize a bar) -----
  const reschedule = useMutation({
    mutationFn: (vars: { projectId: string; start: string; end: string }) =>
      api.roadmaps.rescheduleItem(selectedId!, vars.projectId, {
        plannedStart: vars.start,
        plannedEnd: vars.end,
      }),
    onMutate: async (vars) => {
      await queryClient.cancelQueries({ queryKey: roadmapKey });
      const previous = queryClient.getQueryData<RoadmapDto>(roadmapKey);
      if (previous) {
        queryClient.setQueryData<RoadmapDto>(roadmapKey, {
          ...previous,
          items: previous.items.map((item) =>
            item.projectId === vars.projectId
              ? { ...item, plannedStart: vars.start, plannedEnd: vars.end }
              : item,
          ),
        });
      }
      return { previous };
    },
    onError: (error, _vars, context) => {
      if (context?.previous) {
        queryClient.setQueryData(roadmapKey, context.previous);
      }
      toast.error(errorMessage(error));
    },
    onSettled: invalidateRoadmap,
  });

  // ----- optimistic lane reorder -----
  const reorder = useMutation({
    mutationFn: (projectIds: string[]) =>
      api.roadmaps.reorder(selectedId!, { projectIds }),
    onMutate: async (projectIds) => {
      await queryClient.cancelQueries({ queryKey: roadmapKey });
      const previous = queryClient.getQueryData<RoadmapDto>(roadmapKey);
      if (previous) {
        const order = new Map(projectIds.map((id, index) => [id, index]));
        queryClient.setQueryData<RoadmapDto>(roadmapKey, {
          ...previous,
          items: previous.items.map((item) => ({
            ...item,
            sortOrder: order.get(item.projectId) ?? item.sortOrder,
          })),
        });
      }
      return { previous };
    },
    onError: (error, _vars, context) => {
      if (context?.previous) {
        queryClient.setQueryData(roadmapKey, context.previous);
      }
      toast.error(errorMessage(error));
    },
    onSettled: invalidateRoadmap,
  });

  const plan = useMutation({
    mutationFn: (projectId: string) => {
      const project = (projects.data ?? []).find((p) => p.id === projectId);
      const start = project?.startDate ?? todayIso();
      const end = project?.endDate ?? addDays(start, 29);
      return api.roadmaps.planItem(selectedId!, {
        projectId,
        plannedStart: start,
        plannedEnd: end,
      });
    },
    onSuccess: invalidateRoadmap,
    onError: (error) => toast.error(errorMessage(error)),
  });

  const unplan = useMutation({
    mutationFn: (projectId: string) =>
      api.roadmaps.unplanItem(selectedId!, projectId),
    onSuccess: invalidateRoadmap,
    onError: (error) => toast.error(errorMessage(error)),
  });

  // ----- version actions -----
  const [createOpen, setCreateOpen] = React.useState(false);
  const [branchOpen, setBranchOpen] = React.useState(false);
  const [confirmMakeLive, setConfirmMakeLive] = React.useState(false);
  const [confirmAdopt, setConfirmAdopt] = React.useState(false);

  const invalidateList = React.useCallback(() => {
    void queryClient.invalidateQueries({
      queryKey: queryKeys.roadmaps(workspaceId ?? ""),
    });
    invalidateRoadmap();
  }, [queryClient, workspaceId, invalidateRoadmap]);

  const makeLive = useMutation({
    mutationFn: () => api.roadmaps.makeLive(selectedId!),
    onSuccess: () => {
      toast.success("Roadmap is now live");
      setConfirmMakeLive(false);
      invalidateList();
    },
    onError: (error) => toast.error(errorMessage(error)),
  });

  const selected = roadmaps.data?.find((r) => r.id === selectedId) ?? null;
  const live = roadmaps.data?.find((r) => r.isLive) ?? null;

  const adopt = useMutation({
    mutationFn: () =>
      api.roadmaps.adopt(live!.id, { branchRoadmapId: selectedId! }),
    onSuccess: () => {
      toast.success(`Adopted into ${live?.name ?? "live roadmap"}`);
      setConfirmAdopt(false);
      setSelectedId(live?.id ?? null);
      void queryClient.invalidateQueries({ queryKey: ["roadmap"] });
      void queryClient.invalidateQueries({ queryKey: ["feasibility"] });
      invalidateList();
    },
    onError: (error) => toast.error(errorMessage(error)),
  });

  const plannedProjectIds = new Set(
    (roadmap.data?.items ?? []).map((i) => i.projectId),
  );
  const unplanned = (projects.data ?? []).filter(
    (p) => !plannedProjectIds.has(p.id) && p.status !== "Cancelled",
  );

  const canAdopt =
    selected !== null && !selected.isLive && live !== null && live.id !== selected.id;

  return (
    <>
      <PageHeader title="Roadmap" description="Sequence the bets; watch capacity">
        {roadmaps.data && roadmaps.data.length > 0 && (
          <>
            <Select
              items={(roadmaps.data ?? []).map((r) => ({
                label: r.isLive ? `${r.name} · live` : r.name,
                value: r.id,
              }))}
              value={selectedId}
              onValueChange={(v) => v && setSelectedId(v as string)}
            >
              <SelectTrigger className="min-w-44">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {(roadmaps.data ?? []).map((r) => (
                  <SelectItem key={r.id} value={r.id}>
                    {r.name}
                    {r.isLive ? " · live" : ""}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {selected?.isLive ? (
              <Badge className="gap-1">
                <RadioIcon className="size-3" /> Live
              </Badge>
            ) : (
              <Badge variant="outline">Scenario</Badge>
            )}
            <Button
              variant="outline"
              size="sm"
              disabled={!selectedId}
              onClick={() => setBranchOpen(true)}
            >
              <GitBranchIcon data-icon="inline-start" />
              Branch
            </Button>
            {canAdopt && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setConfirmAdopt(true)}
              >
                <GitMergeIcon data-icon="inline-start" />
                Adopt into live
              </Button>
            )}
            {selected && !selected.isLive && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setConfirmMakeLive(true)}
              >
                <RadioIcon data-icon="inline-start" />
                Make live
              </Button>
            )}
            <Button size="sm" onClick={() => setCreateOpen(true)}>
              <PlusIcon data-icon="inline-start" />
              New
            </Button>
          </>
        )}
      </PageHeader>

      {roadmaps.data && roadmaps.data.length === 0 ? (
        <div className="flex flex-1 items-center justify-center p-6">
          <EmptyState
            title="No roadmap yet"
            hint="Create the live roadmap, then drag projects onto the timeline. Branch it any time to explore a scenario."
          >
            <Button onClick={() => setCreateOpen(true)}>
              <PlusIcon data-icon="inline-start" />
              Create live roadmap
            </Button>
          </EmptyState>
        </div>
      ) : (
        <div className="flex min-h-0 flex-1">
          <div className="flex min-w-0 flex-1 flex-col">
            <RoadmapTimeline
              roadmap={roadmap.data ?? null}
              projects={projects.data ?? []}
              initiatives={initiatives.data ?? []}
              feasibility={feasibility.data ?? null}
              onReschedule={(projectId, start, end) =>
                reschedule.mutate({ projectId, start, end })
              }
              onReorder={(projectIds) => reorder.mutate(projectIds)}
              onUnplan={(projectId) => unplan.mutate(projectId)}
            />
            <UnplannedTray
              projects={unplanned}
              disabled={!selectedId || plan.isPending}
              onPlan={(projectId) => plan.mutate(projectId)}
            />
          </div>
          <WarningsPanel
            feasibility={feasibility.data ?? null}
            loading={feasibility.isLoading}
            teams={teams.data ?? []}
            projects={projects.data ?? []}
          />
        </div>
      )}

      {workspaceId && (
        <CreateRoadmapDialog
          open={createOpen}
          onOpenChange={setCreateOpen}
          workspaceId={workspaceId}
          firstRoadmap={(roadmaps.data ?? []).length === 0}
          onCreated={(r) => {
            setSelectedId(r.id);
            invalidateList();
          }}
        />
      )}

      {selectedId && (
        <BranchDialog
          open={branchOpen}
          onOpenChange={setBranchOpen}
          sourceId={selectedId}
          sourceName={selected?.name ?? ""}
          onBranched={(r) => {
            setSelectedId(r.id);
            invalidateList();
          }}
        />
      )}

      <ConfirmDialog
        open={confirmMakeLive}
        onOpenChange={setConfirmMakeLive}
        title={`Make ${selected?.name ?? "this roadmap"} live?`}
        description="The current live roadmap is retired to a named version. The live roadmap is the one your team executes."
        confirmLabel="Make live"
        onConfirm={() => makeLive.mutate()}
        busy={makeLive.isPending}
      />

      <ConfirmDialog
        open={confirmAdopt}
        onOpenChange={setConfirmAdopt}
        title={`Adopt ${selected?.name ?? "this scenario"}?`}
        description={`The live roadmap (${live?.name ?? ""}) takes over this scenario's plan — its items are replaced with copies of these.`}
        confirmLabel="Adopt"
        onConfirm={() => adopt.mutate()}
        busy={adopt.isPending}
      />
    </>
  );
}

function CreateRoadmapDialog({
  open,
  onOpenChange,
  workspaceId,
  firstRoadmap,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workspaceId: string;
  firstRoadmap: boolean;
  onCreated: (roadmap: RoadmapDto) => void;
}) {
  const [name, setName] = React.useState(firstRoadmap ? "Live roadmap" : "");

  const create = useMutation({
    mutationFn: () =>
      api.roadmaps.create({ workspaceId, name, isLive: firstRoadmap }),
    onSuccess: (r) => {
      toast.success("Roadmap created");
      onOpenChange(false);
      onCreated(r);
    },
    onError: (error) => toast.error(errorMessage(error)),
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>New roadmap</DialogTitle>
          <DialogDescription>
            {firstRoadmap
              ? "This becomes the live roadmap."
              : "Starts empty — branch instead to copy the current plan."}
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <div className="flex flex-col gap-2">
            <Label htmlFor="rm-name">Name</Label>
            <Input
              id="rm-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={create.isPending || !name.trim()}>
              {create.isPending ? "Creating…" : "Create roadmap"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function BranchDialog({
  open,
  onOpenChange,
  sourceId,
  sourceName,
  onBranched,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sourceId: string;
  sourceName: string;
  onBranched: (roadmap: RoadmapDto) => void;
}) {
  const [name, setName] = React.useState("");

  const branch = useMutation({
    mutationFn: () => api.roadmaps.branch(sourceId, { name }),
    onSuccess: (r) => {
      toast.success(`Branched into ${r.name}`);
      onOpenChange(false);
      setName("");
      onBranched(r);
    },
    onError: (error) => toast.error(errorMessage(error)),
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Branch {sourceName}</DialogTitle>
          <DialogDescription>
            A scenario is a branch: same plan, freely editable, adoptable back
            into live when it wins.
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            branch.mutate();
          }}
        >
          <div className="flex flex-col gap-2">
            <Label htmlFor="branch-name">Scenario name</Label>
            <Input
              id="branch-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Aggressive Q2, Platform-first"
              required
              maxLength={200}
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={branch.isPending || !name.trim()}>
              {branch.isPending ? "Branching…" : "Create branch"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
