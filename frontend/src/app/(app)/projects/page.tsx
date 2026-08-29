"use client";

import * as React from "react";
import { PlusIcon, PencilIcon, Trash2Icon, XIcon } from "lucide-react";
import { api } from "@/lib/api/client";
import type {
  FeatureDto,
  InitiativeDto,
  ProjectDto,
  ProjectStatus,
  TeamDto,
} from "@/lib/api/types";
import { PROJECT_STATUSES } from "@/lib/api/types";
import { formatDate, formatPeriod } from "@/lib/timeline";
import {
  queryKeys,
  useFeatures,
  useInitiatives,
  useInvalidatingMutation,
  useProjects,
  useTeams,
  useWorkspace,
} from "@/lib/queries";
import { PageHeader, EmptyState } from "@/components/page-header";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

const STATUS_BADGE: Record<
  ProjectStatus,
  "outline" | "secondary" | "default" | "destructive"
> = {
  Proposed: "outline",
  Planned: "secondary",
  InProgress: "default",
  Done: "secondary",
  Cancelled: "destructive",
};

export default function ProjectsPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const projects = useProjects(workspaceId);
  const initiatives = useInitiatives(workspaceId);
  const teams = useTeams(workspaceId);
  const features = useFeatures(workspaceId);

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editingId, setEditingId] = React.useState<string | null>(null);
  const [deleting, setDeleting] = React.useState<ProjectDto | null>(null);

  const initiativeName = React.useMemo(() => {
    const map = new Map<string, string>();
    for (const i of initiatives.data ?? []) map.set(i.id, i.name);
    return map;
  }, [initiatives.data]);

  // Re-resolve the edited project from the cache so section mutations refresh it.
  const editing =
    (projects.data ?? []).find((p) => p.id === editingId) ?? null;

  const deleteProject = useInvalidatingMutation(
    (id: string) => api.projects.delete(id),
    () => [queryKeys.projects(workspaceId ?? "")],
    { successMessage: "Project deleted", onSuccess: () => setDeleting(null) },
  );

  return (
    <>
      <PageHeader
        title="Projects"
        description="Units of execution — dated, staffed, and sequenced"
      >
        <Button
          disabled={(initiatives.data ?? []).length === 0}
          onClick={() => {
            setEditingId(null);
            setEditorOpen(true);
          }}
        >
          <PlusIcon data-icon="inline-start" />
          New project
        </Button>
      </PageHeader>

      <div className="flex-1 overflow-auto p-6">
        {(initiatives.data ?? []).length === 0 && projects.data ? (
          <EmptyState
            title="Create an initiative first"
            hint="Every project belongs to an initiative — the strategic bet it serves."
          />
        ) : projects.data && projects.data.length === 0 ? (
          <EmptyState
            title="No projects yet"
            hint="A project has dates, effort demands per team, and dependencies. Plan it on the roadmap when it is ready."
          />
        ) : (
          <div className="overflow-hidden rounded-xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Initiative</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Start</TableHead>
                  <TableHead>End</TableHead>
                  <TableHead className="w-20">Deps</TableHead>
                  <TableHead className="w-24 text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(projects.data ?? []).map((project) => (
                  <TableRow key={project.id}>
                    <TableCell className="font-medium">{project.name}</TableCell>
                    <TableCell className="text-muted-foreground">
                      {initiativeName.get(project.initiativeId) ?? "—"}
                    </TableCell>
                    <TableCell>
                      <Badge variant={STATUS_BADGE[project.status]}>
                        {project.status}
                      </Badge>
                    </TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {formatDate(project.startDate)}
                    </TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {formatDate(project.endDate)}
                    </TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {project.dependsOnProjectIds.length}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="inline-flex gap-1">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Edit ${project.name}`}
                          onClick={() => {
                            setEditingId(project.id);
                            setEditorOpen(true);
                          }}
                        >
                          <PencilIcon />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Delete ${project.name}`}
                          onClick={() => setDeleting(project)}
                        >
                          <Trash2Icon className="text-destructive" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </div>

      {workspaceId && editorOpen && (
        <ProjectEditorDialog
          key={editing?.id ?? "new"}
          open={editorOpen}
          onOpenChange={(open) => {
            setEditorOpen(open);
            if (!open) setEditingId(null);
          }}
          workspaceId={workspaceId}
          project={editing}
          initiatives={initiatives.data ?? []}
          allProjects={projects.data ?? []}
          teams={teams.data ?? []}
          features={features.data ?? []}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "project"}?`}
        description="Dependencies, effort demands, and roadmap plans for this project are removed too."
        onConfirm={() => deleting && deleteProject.mutate(deleting.id)}
        busy={deleteProject.isPending}
      />
    </>
  );
}

function ProjectEditorDialog({
  open,
  onOpenChange,
  workspaceId,
  project,
  initiatives,
  allProjects,
  teams,
  features,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workspaceId: string;
  project: ProjectDto | null;
  initiatives: InitiativeDto[];
  allProjects: ProjectDto[];
  teams: TeamDto[];
  features: FeatureDto[];
}) {
  const [name, setName] = React.useState(project?.name ?? "");
  const [description, setDescription] = React.useState(
    project?.description ?? "",
  );
  const [initiativeId, setInitiativeId] = React.useState(
    project?.initiativeId ?? initiatives[0]?.id ?? "",
  );
  const [status, setStatus] = React.useState<ProjectStatus>(
    project?.status ?? "Proposed",
  );
  const [startDate, setStartDate] = React.useState(project?.startDate ?? "");
  const [endDate, setEndDate] = React.useState(project?.endDate ?? "");

  const invalidate = () => [queryKeys.projects(workspaceId)];

  const save = useInvalidatingMutation(
    () =>
      project
        ? api.projects.update(project.id, {
            name,
            description: description || null,
            status,
            startDate: startDate || null,
            endDate: endDate || null,
          })
        : api.projects.create({
            initiativeId,
            name,
            description: description || null,
            status,
            startDate: startDate || null,
            endDate: endDate || null,
          }),
    invalidate,
    {
      successMessage: project ? "Project saved" : "Project created",
      onSuccess: () => onOpenChange(false),
    },
  );

  const projectById = new Map(allProjects.map((p) => [p.id, p]));
  const teamById = new Map(teams.map((t) => [t.id, t]));
  const featureById = new Map(features.map((f) => [f.id, f]));

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{project ? "Edit project" : "New project"}</DialogTitle>
          <DialogDescription>
            {project
              ? "Details, dependencies, effort, and delivered features."
              : "Basics first — dependencies and effort once it exists."}
          </DialogDescription>
        </DialogHeader>

        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(undefined);
          }}
        >
          <div className="flex flex-col gap-2">
            <Label htmlFor="proj-name">Name</Label>
            <Input
              id="proj-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label>Initiative</Label>
              <Select
                items={initiatives.map((i) => ({ label: i.name, value: i.id }))}
                value={initiativeId}
                onValueChange={(v) => v && setInitiativeId(v as string)}
                disabled={project !== null}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {initiatives.map((i) => (
                    <SelectItem key={i.id} value={i.id}>
                      {i.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label>Status</Label>
              <Select
                items={PROJECT_STATUSES.map((s) => ({ label: s, value: s }))}
                value={status}
                onValueChange={(v) => v && setStatus(v as ProjectStatus)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {PROJECT_STATUSES.map((s) => (
                    <SelectItem key={s} value={s}>
                      {s}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label htmlFor="proj-start">Start date</Label>
              <Input
                id="proj-start"
                type="date"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="proj-end">End date</Label>
              <Input
                id="proj-end"
                type="date"
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
              />
            </div>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="proj-desc">Description</Label>
            <Textarea
              id="proj-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
            />
          </div>

          <DialogFooter className="mt-1">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button
              type="submit"
              disabled={save.isPending || !name.trim() || !initiativeId}
            >
              {save.isPending
                ? "Saving…"
                : project
                  ? "Save changes"
                  : "Create project"}
            </Button>
          </DialogFooter>
        </form>

        {project && (
          <>
            <Separator />
            <DependenciesSection
              project={project}
              projectById={projectById}
              allProjects={allProjects}
              workspaceId={workspaceId}
            />
            <Separator />
            <EffortSection
              project={project}
              teams={teams}
              teamById={teamById}
              workspaceId={workspaceId}
            />
            <Separator />
            <FeaturesSection
              project={project}
              features={features}
              featureById={featureById}
              workspaceId={workspaceId}
            />
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

function DependenciesSection({
  project,
  projectById,
  allProjects,
  workspaceId,
}: {
  project: ProjectDto;
  projectById: Map<string, ProjectDto>;
  allProjects: ProjectDto[];
  workspaceId: string;
}) {
  const invalidate = () => [queryKeys.projects(workspaceId)];
  const add = useInvalidatingMutation(
    (dependsOnProjectId: string) =>
      api.projects.addDependency(project.id, { dependsOnProjectId }),
    invalidate,
  );
  const remove = useInvalidatingMutation(
    (dependsOnProjectId: string) =>
      api.projects.removeDependency(project.id, dependsOnProjectId),
    invalidate,
  );

  const candidates = allProjects.filter(
    (p) => p.id !== project.id && !project.dependsOnProjectIds.includes(p.id),
  );

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-sm font-medium">Depends on</h3>
      <div className="flex flex-wrap gap-1.5">
        {project.dependsOnProjectIds.map((depId) => (
          <Badge key={depId} variant="secondary" className="gap-1 pr-1">
            {projectById.get(depId)?.name ?? "unknown project"}
            <button
              type="button"
              aria-label="Remove dependency"
              className="rounded-sm p-0.5 hover:bg-foreground/10"
              onClick={() => remove.mutate(depId)}
            >
              <XIcon className="size-3" />
            </button>
          </Badge>
        ))}
        {project.dependsOnProjectIds.length === 0 && (
          <span className="text-xs text-muted-foreground">
            No dependencies.
          </span>
        )}
      </div>
      {candidates.length > 0 && (
        <Select
          items={candidates.map((p) => ({ label: p.name, value: p.id }))}
          value={null}
          onValueChange={(v) => v && add.mutate(v as string)}
        >
          <SelectTrigger className="w-full">
            <SelectValue>{() => "Add a dependency…"}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            {candidates.map((p) => (
              <SelectItem key={p.id} value={p.id}>
                {p.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
    </section>
  );
}

function EffortSection({
  project,
  teams,
  teamById,
  workspaceId,
}: {
  project: ProjectDto;
  teams: TeamDto[];
  teamById: Map<string, TeamDto>;
  workspaceId: string;
}) {
  const [teamId, setTeamId] = React.useState<string | null>(null);
  const [period, setPeriod] = React.useState("");
  const [points, setPoints] = React.useState("");

  const setDemand = useInvalidatingMutation(
    (body: { teamId: string; year: number; month: number; points: number }) =>
      api.projects.setEffortDemand(project.id, body),
    () => [queryKeys.projects(workspaceId)],
    {
      onSuccess: () => {
        setPoints("");
      },
    },
  );

  function submit() {
    if (!teamId || !period) return;
    const [year, month] = period.split("-").map(Number);
    const value = Number(points);
    if (Number.isNaN(value) || value < 0) return;
    setDemand.mutate({ teamId, year, month, points: value });
  }

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-sm font-medium">Effort demands</h3>
      {project.effortDemands.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {project.effortDemands.map((d) => (
            <li
              key={`${d.teamId}-${d.year}-${d.month}`}
              className="flex items-center justify-between rounded-md bg-muted/60 px-2.5 py-1.5 text-sm"
            >
              <span>
                {teamById.get(d.teamId)?.name ?? "unknown team"}
                <span className="ml-2 font-mono text-xs text-muted-foreground">
                  {formatPeriod(d.year, d.month)}
                </span>
              </span>
              <span className="flex items-center gap-2">
                <span className="font-mono text-xs">{d.points} pts</span>
                <button
                  type="button"
                  aria-label="Clear demand"
                  className="rounded-sm p-0.5 text-muted-foreground hover:bg-foreground/10"
                  onClick={() =>
                    setDemand.mutate({
                      teamId: d.teamId,
                      year: d.year,
                      month: d.month,
                      points: 0,
                    })
                  }
                >
                  <XIcon className="size-3.5" />
                </button>
              </span>
            </li>
          ))}
        </ul>
      ) : (
        <span className="text-xs text-muted-foreground">
          No effort demanded yet. Demand is per team per month; the roadmap
          checks it against capacity.
        </span>
      )}
      {teams.length > 0 && (
        <div className="flex items-end gap-2">
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <Label className="text-xs">Team</Label>
            <Select
              items={teams.map((t) => ({ label: t.name, value: t.id }))}
              value={teamId}
              onValueChange={(v) => setTeamId(v as string | null)}
            >
              <SelectTrigger className="w-full" size="sm">
                <SelectValue>
                  {(v: string | null) =>
                    v ? (teamById.get(v)?.name ?? "Team") : "Team…"
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {teams.map((t) => (
                  <SelectItem key={t.id} value={t.id}>
                    {t.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1">
            <Label className="text-xs" htmlFor="effort-period">
              Month
            </Label>
            <Input
              id="effort-period"
              type="month"
              className="h-7 w-36 text-xs"
              value={period}
              onChange={(e) => setPeriod(e.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1">
            <Label className="text-xs" htmlFor="effort-points">
              Points
            </Label>
            <Input
              id="effort-points"
              inputMode="decimal"
              className="h-7 w-20 font-mono text-xs"
              value={points}
              onChange={(e) => setPoints(e.target.value)}
            />
          </div>
          <Button
            type="button"
            size="sm"
            variant="secondary"
            disabled={!teamId || !period || points === "" || setDemand.isPending}
            onClick={submit}
          >
            Set
          </Button>
        </div>
      )}
    </section>
  );
}

function FeaturesSection({
  project,
  features,
  featureById,
  workspaceId,
}: {
  project: ProjectDto;
  features: FeatureDto[];
  featureById: Map<string, FeatureDto>;
  workspaceId: string;
}) {
  const invalidate = () => [
    queryKeys.projects(workspaceId),
    queryKeys.features(workspaceId),
  ];
  const link = useInvalidatingMutation(
    (featureId: string) => api.projects.linkFeature(project.id, { featureId }),
    invalidate,
  );
  const unlink = useInvalidatingMutation(
    (featureId: string) => api.projects.unlinkFeature(project.id, featureId),
    invalidate,
  );

  const candidates = features.filter(
    (f) => !project.deliveredFeatureIds.includes(f.id),
  );

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-sm font-medium">Delivers features</h3>
      <div className="flex flex-wrap gap-1.5">
        {project.deliveredFeatureIds.map((fid) => (
          <Badge key={fid} variant="secondary" className="gap-1 pr-1">
            {featureById.get(fid)?.name ?? "unknown feature"}
            <button
              type="button"
              aria-label="Unlink feature"
              className="rounded-sm p-0.5 hover:bg-foreground/10"
              onClick={() => unlink.mutate(fid)}
            >
              <XIcon className="size-3" />
            </button>
          </Badge>
        ))}
        {project.deliveredFeatureIds.length === 0 && (
          <span className="text-xs text-muted-foreground">
            The seam is empty — link the features this project makes real.
          </span>
        )}
      </div>
      {candidates.length > 0 && (
        <Select
          items={candidates.map((f) => ({ label: f.name, value: f.id }))}
          value={null}
          onValueChange={(v) => v && link.mutate(v as string)}
        >
          <SelectTrigger className="w-full">
            <SelectValue>{() => "Link a feature…"}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            {candidates.map((f) => (
              <SelectItem key={f.id} value={f.id}>
                {f.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
    </section>
  );
}
