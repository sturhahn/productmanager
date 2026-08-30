"use client";

import * as React from "react";
import {
  GitBranchIcon,
  InfoIcon,
  LinkIcon,
  PencilIcon,
  PlusIcon,
  Trash2Icon,
} from "lucide-react";
import { api } from "@/lib/api/client";
import type {
  AssumptionSetDto,
  ClientSegmentDto,
  CovariateDto,
  DistributionType,
  ParameterDto,
  ParameterProvenance,
  ProductDto,
  SimulationFlowKind,
} from "@/lib/api/types";
import {
  DISTRIBUTION_TYPES,
  PARAMETER_PROVENANCES,
  SIMULATION_FLOW_KINDS,
} from "@/lib/api/types";
import {
  queryKeys,
  useAllSegments,
  useAssumptionSets,
  useCovariates,
  useInvalidatingMutation,
  useMarkets,
  useWorkspace,
} from "@/lib/queries";
import { useQuery } from "@tanstack/react-query";
import { cn } from "@/lib/utils";
import { PageHeader, EmptyState } from "@/components/page-header";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
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
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";

const FLOW_LABELS: Record<SimulationFlowKind, string> = {
  NewFirmArrival: "New-firm arrival",
  ConversionToMarket: "Conversion to market",
  WinRate: "Win rate",
  ChurnHazard: "Churn hazard",
  CrossSellRate: "Cross-sell rate",
};

const DISTRIBUTION_PARAM_LABELS: Record<DistributionType, [string, string | null]> = {
  Point: ["value", null],
  Normal: ["mean", "std dev"],
  Lognormal: ["mu (log)", "sigma (log)"],
  Beta: ["alpha", "beta"],
};

const PROVENANCE_STYLES: Record<ParameterProvenance, string> = {
  Asserted: "bg-secondary text-secondary-foreground",
  Calibrated: "bg-primary/15 text-primary",
  Default: "border border-dashed bg-transparent text-muted-foreground",
};

function flattenSetTree(
  sets: AssumptionSetDto[],
): { set: AssumptionSetDto; depth: number }[] {
  const byParent = new Map<string | null, AssumptionSetDto[]>();
  for (const s of sets) {
    const list = byParent.get(s.parentAssumptionSetId) ?? [];
    list.push(s);
    byParent.set(s.parentAssumptionSetId, list);
  }
  const ids = new Set(sets.map((s) => s.id));
  const out: { set: AssumptionSetDto; depth: number }[] = [];
  const visit = (parent: string | null, depth: number) => {
    for (const s of byParent.get(parent) ?? []) {
      out.push({ set: s, depth });
      visit(s.id, depth + 1);
    }
  };
  visit(null, 0);
  for (const s of sets) {
    if (s.parentAssumptionSetId !== null && !ids.has(s.parentAssumptionSetId)) {
      out.push({ set: s, depth: 0 });
    }
  }
  return out;
}

/** Every product (across markets) that can carry a cross-sell parameter. */
function useAllModules(workspaceId: string | undefined) {
  const markets = useMarkets(workspaceId);
  const marketIds = (markets.data ?? []).map((m) => m.id).join(",");
  return useQuery({
    queryKey: ["all-modules", workspaceId ?? "", marketIds],
    queryFn: async () => {
      const pages = await Promise.all(
        (markets.data ?? []).map((m) => api.products.list(m.id)),
      );
      return pages.flatMap((p) => p.items).filter((p) => p.isModule && p.isOurs);
    },
    enabled: !!workspaceId && markets.data !== undefined,
  });
}

export default function AssumptionsPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const sets = useAssumptionSets(workspaceId);
  const segments = useAllSegments(workspaceId);
  const covariates = useCovariates(workspaceId);
  const modules = useAllModules(workspaceId);

  // Deep link from Calibration ("open the fitted set"): /assumptions?set=<id>
  const [chosenId, setChosenId] = React.useState<string | null>(() =>
    typeof window === "undefined"
      ? null
      : new URLSearchParams(window.location.search).get("set"),
  );

  const rows = React.useMemo(() => flattenSetTree(sets.data ?? []), [sets.data]);
  const selected =
    sets.data?.find((s) => s.id === chosenId) ?? rows[0]?.set ?? null;

  const [setEditorOpen, setSetEditorOpen] = React.useState(false);
  const [editingSet, setEditingSet] = React.useState<AssumptionSetDto | null>(
    null,
  );
  const [branching, setBranching] = React.useState<AssumptionSetDto | null>(
    null,
  );
  const [deleting, setDeleting] = React.useState<AssumptionSetDto | null>(null);
  const [paramDialogOpen, setParamDialogOpen] = React.useState(false);

  const invalidate = () => [queryKeys.assumptionSets(workspaceId ?? "")];

  const deleteSet = useInvalidatingMutation(
    (id: string) => api.assumptionSets.delete(id),
    invalidate,
    {
      successMessage: "Assumption set deleted",
      onSuccess: () => setDeleting(null),
    },
  );

  return (
    <>
      <PageHeader
        title="Assumptions"
        description="Versioned parameter collections — every number has a distribution, a provenance, and a rationale"
      >
        <Button
          disabled={!workspaceId}
          onClick={() => {
            setEditingSet(null);
            setSetEditorOpen(true);
          }}
        >
          <PlusIcon data-icon="inline-start" />
          New set
        </Button>
      </PageHeader>

      <div className="flex flex-1 overflow-hidden">
        {rows.length === 0 ? (
          <div className="flex-1 p-6">
            <EmptyState
              title="No assumption sets yet"
              hint='Create a baseline set, add flow-rate parameters per segment, then branch it for what-ifs ("churn 2x") without losing the original.'
            />
          </div>
        ) : (
          <>
            <aside className="w-64 shrink-0 overflow-y-auto border-r p-3">
              <ul className="flex flex-col gap-1">
                {rows.map(({ set, depth }) => (
                  <li key={set.id} style={{ paddingLeft: `${depth * 0.75}rem` }}>
                    <button
                      type="button"
                      onClick={() => setChosenId(set.id)}
                      className={cn(
                        "flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring/50",
                        selected?.id === set.id
                          ? "bg-accent font-medium text-accent-foreground"
                          : "text-muted-foreground hover:bg-accent/60",
                      )}
                    >
                      {depth > 0 && (
                        <GitBranchIcon className="size-3.5 shrink-0 text-muted-foreground/60" />
                      )}
                      <span className="min-w-0 flex-1">
                        <span className="block truncate">{set.name}</span>
                        <span className="block font-mono text-[11px] text-muted-foreground/80">
                          {set.parameters.length} parameter
                          {set.parameters.length === 1 ? "" : "s"}
                        </span>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </aside>

            <div className="flex-1 overflow-y-auto p-6">
              {selected && workspaceId && (
                <AssumptionSetDetail
                  key={selected.id}
                  set={selected}
                  workspaceId={workspaceId}
                  segments={segments.data ?? []}
                  covariates={covariates.data ?? []}
                  modules={modules.data ?? []}
                  onEdit={() => {
                    setEditingSet(selected);
                    setSetEditorOpen(true);
                  }}
                  onBranch={() => setBranching(selected)}
                  onDelete={() => setDeleting(selected)}
                  onAddParameter={() => setParamDialogOpen(true)}
                />
              )}
            </div>
          </>
        )}
      </div>

      {workspaceId && (
        <SetEditorDialog
          key={editingSet?.id ?? "new"}
          open={setEditorOpen}
          onOpenChange={setSetEditorOpen}
          workspaceId={workspaceId}
          set={editingSet}
          onCreated={(id) => setChosenId(id)}
        />
      )}

      {branching && workspaceId && (
        <BranchDialog
          set={branching}
          workspaceId={workspaceId}
          onOpenChange={(open) => !open && setBranching(null)}
          onCreated={(id) => setChosenId(id)}
        />
      )}

      {paramDialogOpen && selected && workspaceId && (
        <AddParameterDialog
          set={selected}
          workspaceId={workspaceId}
          segments={segments.data ?? []}
          covariates={covariates.data ?? []}
          modules={modules.data ?? []}
          onOpenChange={(open) => !open && setParamDialogOpen(false)}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "assumption set"}?`}
        description="A set referenced by simulation runs or child branches can't be deleted."
        onConfirm={() => deleting && deleteSet.mutate(deleting.id)}
        busy={deleteSet.isPending}
      />
    </>
  );
}

// ---------------------------------------------------------------------
// Detail
// ---------------------------------------------------------------------

function AssumptionSetDetail({
  set,
  workspaceId,
  segments,
  covariates,
  modules,
  onEdit,
  onBranch,
  onDelete,
  onAddParameter,
}: {
  set: AssumptionSetDto;
  workspaceId: string;
  segments: ClientSegmentDto[];
  covariates: CovariateDto[];
  modules: ProductDto[];
  onEdit: () => void;
  onBranch: () => void;
  onDelete: () => void;
  onAddParameter: () => void;
}) {
  const segmentName = (id: string | null) =>
    id === null
      ? "All segments"
      : (segments.find((s) => s.id === id)?.name ?? "Unknown segment");

  const engineParams = set.parameters.filter((p) => p.isEngineParameter);
  const freeParams = set.parameters.filter((p) => !p.isEngineParameter);

  const groups = new Map<string, ParameterDto[]>();
  for (const p of engineParams) {
    const key = p.clientSegmentId ?? "";
    const list = groups.get(key) ?? [];
    list.push(p);
    groups.set(key, list);
  }
  const orderedGroups = [...groups.entries()].sort(([a], [b]) =>
    segmentName(a || null).localeCompare(segmentName(b || null)),
  );
  for (const [, list] of orderedGroups) {
    list.sort(
      (a, b) =>
        SIMULATION_FLOW_KINDS.indexOf(a.flowKind ?? "NewFirmArrival") -
        SIMULATION_FLOW_KINDS.indexOf(b.flowKind ?? "NewFirmArrival"),
    );
  }

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-5">
      <section className="flex items-start justify-between gap-4">
        <div>
          <h2 className="font-heading text-lg font-semibold tracking-tight">
            {set.name}
          </h2>
          <p className="mt-0.5 text-sm text-muted-foreground">
            {set.notes || "No notes."}
          </p>
        </div>
        <div className="flex shrink-0 gap-1">
          <Button variant="outline" size="sm" onClick={onBranch}>
            <GitBranchIcon data-icon="inline-start" />
            Branch
          </Button>
          <Button variant="outline" size="sm" onClick={onAddParameter}>
            <PlusIcon data-icon="inline-start" />
            Add parameter
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="Edit set"
            onClick={onEdit}
          >
            <PencilIcon />
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="Delete set"
            onClick={onDelete}
          >
            <Trash2Icon className="text-destructive" />
          </Button>
        </div>
      </section>

      {engineParams.length === 0 && freeParams.length === 0 ? (
        <EmptyState
          title="No parameters yet"
          hint="Add flow-rate parameters per segment: arrival, conversion, win rate, churn, cross-sell. Anything missing defaults to a point value of 0 in the engine."
        />
      ) : (
        <>
          {orderedGroups.map(([segKey, params]) => (
            <section key={segKey || "all"} className="rounded-xl border bg-card">
              <div className="border-b bg-muted/40 px-4 py-2">
                <h3 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                  {segmentName(segKey || null)}
                </h3>
              </div>
              <div className="divide-y">
                {params.map((param) => (
                  <ParameterRow
                    key={param.id}
                    set={set}
                    param={param}
                    workspaceId={workspaceId}
                    covariates={covariates}
                    modules={modules}
                  />
                ))}
              </div>
            </section>
          ))}

          {freeParams.length > 0 && (
            <section className="rounded-xl border bg-card">
              <div className="border-b bg-muted/40 px-4 py-2">
                <h3 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                  Free-form assumptions (ignored by the engine)
                </h3>
              </div>
              <div className="divide-y">
                {freeParams.map((param) => (
                  <ParameterRow
                    key={param.id}
                    set={set}
                    param={param}
                    workspaceId={workspaceId}
                    covariates={covariates}
                    modules={modules}
                  />
                ))}
              </div>
            </section>
          )}
        </>
      )}
    </div>
  );
}

function ParameterRow({
  set,
  param,
  workspaceId,
  covariates,
  modules,
}: {
  set: AssumptionSetDto;
  param: ParameterDto;
  workspaceId: string;
  covariates: CovariateDto[];
  modules: ProductDto[];
}) {
  const [type, setType] = React.useState<DistributionType>(
    param.distribution.type,
  );
  const [p1, setP1] = React.useState(String(param.distribution.p1));
  const [p2, setP2] = React.useState(String(param.distribution.p2));

  const covariate = param.covariateId
    ? covariates.find((c) => c.id === param.covariateId)
    : null;
  const moduleName = param.moduleProductId
    ? (modules.find((m) => m.id === param.moduleProductId)?.name ?? "module")
    : null;

  const invalidate = () => [queryKeys.assumptionSets(workspaceId)];

  const save = useInvalidatingMutation(
    (next: { type: DistributionType; p1: number; p2: number }) =>
      api.assumptionSets.upsertParameter(set.id, {
        distribution: { type: next.type, p1: next.p1, p2: next.p2 },
        provenance: param.provenance,
        rationale: param.rationale,
        clientSegmentId: param.clientSegmentId,
        flowKind: param.isEngineParameter ? param.flowKind : null,
        moduleProductId: param.moduleProductId,
        covariateId: param.covariateId,
        covariateBeta: param.covariateBeta,
        key: param.isEngineParameter ? null : param.key,
      }),
    invalidate,
  );

  const remove = useInvalidatingMutation(
    () => api.assumptionSets.removeParameter(set.id, param.id),
    invalidate,
    { successMessage: "Parameter removed" },
  );

  const commit = (nextType: DistributionType, np1: string, np2: string) => {
    const parsed1 = Number(np1);
    const parsed2 = nextType === "Point" ? 0 : Number(np2);
    if (Number.isNaN(parsed1) || Number.isNaN(parsed2)) return;
    const unchanged =
      nextType === param.distribution.type &&
      parsed1 === param.distribution.p1 &&
      parsed2 === param.distribution.p2;
    if (unchanged) return;
    save.mutate({ type: nextType, p1: parsed1, p2: parsed2 });
  };

  const [label1, label2] = DISTRIBUTION_PARAM_LABELS[type];

  return (
    <div className="group flex items-center gap-3 px-4 py-2">
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="truncate text-sm font-medium">
            {param.isEngineParameter && param.flowKind
              ? FLOW_LABELS[param.flowKind]
              : param.key}
          </span>
          {moduleName && (
            <Badge variant="outline" className="text-[10px]">
              {moduleName}
            </Badge>
          )}
          <span
            className={cn(
              "rounded-md px-1.5 py-0.5 text-[10px] font-medium",
              PROVENANCE_STYLES[param.provenance],
            )}
          >
            {param.provenance}
          </span>
          {covariate && (
            <Tooltip>
              <TooltipTrigger
                render={
                  <Badge variant="outline" className="gap-1 text-[10px]">
                    <LinkIcon className="size-3" />
                    {covariate.name}
                  </Badge>
                }
              />
              <TooltipContent>
                rate × ({covariate.name} ÷ start)^{param.covariateBeta ?? 0}
              </TooltipContent>
            </Tooltip>
          )}
          {param.rationale && (
            <Tooltip>
              <TooltipTrigger
                render={
                  <InfoIcon className="size-3.5 shrink-0 text-muted-foreground/70" />
                }
              />
              <TooltipContent className="max-w-64">
                {param.rationale}
              </TooltipContent>
            </Tooltip>
          )}
        </div>
      </div>

      <div className="flex items-center gap-1.5">
        <Select
          items={DISTRIBUTION_TYPES.map((t) => ({ label: t, value: t }))}
          value={type}
          onValueChange={(v) => {
            if (!v) return;
            const nextType = v as DistributionType;
            setType(nextType);
            commit(nextType, p1, p2);
          }}
        >
          <SelectTrigger size="sm" className="w-28 font-mono text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {DISTRIBUTION_TYPES.map((t) => (
              <SelectItem key={t} value={t}>
                {t}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex flex-col">
          <Input
            aria-label={label1}
            inputMode="decimal"
            className="h-7 w-20 font-mono text-xs"
            value={p1}
            onChange={(e) => setP1(e.target.value)}
            onBlur={() => commit(type, p1, p2)}
            onKeyDown={(e) => {
              if (e.key === "Enter") commit(type, p1, p2);
            }}
          />
          <span className="pl-1 text-[9px] text-muted-foreground/70">
            {label1}
          </span>
        </div>
        {label2 !== null && (
          <div className="flex flex-col">
            <Input
              aria-label={label2}
              inputMode="decimal"
              className="h-7 w-20 font-mono text-xs"
              value={p2}
              onChange={(e) => setP2(e.target.value)}
              onBlur={() => commit(type, p1, p2)}
              onKeyDown={(e) => {
                if (e.key === "Enter") commit(type, p1, p2);
              }}
            />
            <span className="pl-1 text-[9px] text-muted-foreground/70">
              {label2}
            </span>
          </div>
        )}
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label="Remove parameter"
          className="opacity-0 transition-opacity group-hover:opacity-100"
          onClick={() => remove.mutate(undefined)}
        >
          <Trash2Icon className="text-destructive" />
        </Button>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------
// Dialogs
// ---------------------------------------------------------------------

function SetEditorDialog({
  open,
  onOpenChange,
  workspaceId,
  set,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workspaceId: string;
  set: AssumptionSetDto | null;
  onCreated: (id: string) => void;
}) {
  const [name, setName] = React.useState(set?.name ?? "");
  const [notes, setNotes] = React.useState(set?.notes ?? "");

  const save = useInvalidatingMutation(
    () =>
      set
        ? api.assumptionSets.update(set.id, { name, notes: notes || null })
        : api.assumptionSets.create({
            workspaceId,
            name,
            notes: notes || null,
          }),
    () => [queryKeys.assumptionSets(workspaceId)],
    {
      successMessage: set ? "Set saved" : "Set created",
      onSuccess: (saved) => {
        if (!set) onCreated(saved.id);
        onOpenChange(false);
      },
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {set ? "Edit assumption set" : "New assumption set"}
          </DialogTitle>
          <DialogDescription>
            A named collection of parameters. Branch it to explore what-ifs
            without touching the baseline.
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
            <Label htmlFor="set-name">Name</Label>
            <Input
              id="set-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="set-notes">Notes</Label>
            <Textarea
              id="set-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={3}
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending ? "Saving…" : set ? "Save changes" : "Create set"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function BranchDialog({
  set,
  workspaceId,
  onOpenChange,
  onCreated,
}: {
  set: AssumptionSetDto;
  workspaceId: string;
  onOpenChange: (open: boolean) => void;
  onCreated: (id: string) => void;
}) {
  const [name, setName] = React.useState(`${set.name} (branch)`);

  const branch = useInvalidatingMutation(
    () => api.assumptionSets.branch(set.id, { name }),
    () => [queryKeys.assumptionSets(workspaceId)],
    {
      successMessage: "Branched",
      onSuccess: (created) => {
        onCreated(created.id);
        onOpenChange(false);
      },
    },
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Branch {set.name}</DialogTitle>
          <DialogDescription>
            Copies every parameter into a new set that remembers its parent.
            Tweak one number — &quot;what if churn is 2×&quot; — and compare
            runs.
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            branch.mutate(undefined);
          }}
        >
          <div className="flex flex-col gap-2">
            <Label htmlFor="branch-name">Branch name</Label>
            <Input
              id="branch-name"
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
            <Button type="submit" disabled={branch.isPending || !name.trim()}>
              {branch.isPending ? "Branching…" : "Create branch"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function AddParameterDialog({
  set,
  workspaceId,
  segments,
  covariates,
  modules,
  onOpenChange,
}: {
  set: AssumptionSetDto;
  workspaceId: string;
  segments: ClientSegmentDto[];
  covariates: CovariateDto[];
  modules: ProductDto[];
  onOpenChange: (open: boolean) => void;
}) {
  const [segmentId, setSegmentId] = React.useState<string>(
    segments[0]?.id ?? "",
  );
  const [flowKind, setFlowKind] =
    React.useState<SimulationFlowKind>("NewFirmArrival");
  const [moduleId, setModuleId] = React.useState<string>(modules[0]?.id ?? "");
  const [type, setType] = React.useState<DistributionType>("Point");
  const [p1, setP1] = React.useState("0");
  const [p2, setP2] = React.useState("0");
  const [provenance, setProvenance] =
    React.useState<ParameterProvenance>("Asserted");
  const [rationale, setRationale] = React.useState("");
  const [covariateId, setCovariateId] = React.useState<string>("none");
  const [beta, setBeta] = React.useState("0");

  const save = useInvalidatingMutation(
    () =>
      api.assumptionSets.upsertParameter(set.id, {
        distribution: {
          type,
          p1: Number(p1) || 0,
          p2: type === "Point" ? 0 : Number(p2) || 0,
        },
        provenance,
        rationale: rationale || null,
        clientSegmentId: segmentId,
        flowKind,
        moduleProductId: flowKind === "CrossSellRate" ? moduleId : null,
        covariateId: covariateId === "none" ? null : covariateId,
        covariateBeta: covariateId === "none" ? null : Number(beta) || 0,
      }),
    () => [queryKeys.assumptionSets(workspaceId)],
    { successMessage: "Parameter saved", onSuccess: () => onOpenChange(false) },
  );

  const [label1, label2] = DISTRIBUTION_PARAM_LABELS[type];

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Add parameter to {set.name}</DialogTitle>
          <DialogDescription>
            Writing the same segment + flow (+ module) again replaces the
            existing parameter — the triple is the engine&apos;s key.
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(undefined);
          }}
        >
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label>Segment</Label>
              <Select
                items={segments.map((s) => ({ label: s.name, value: s.id }))}
                value={segmentId}
                onValueChange={(v) => v && setSegmentId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {segments.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label>Flow</Label>
              <Select
                items={SIMULATION_FLOW_KINDS.map((f) => ({
                  label: FLOW_LABELS[f],
                  value: f,
                }))}
                value={flowKind}
                onValueChange={(v) => v && setFlowKind(v as SimulationFlowKind)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {SIMULATION_FLOW_KINDS.map((f) => (
                    <SelectItem key={f} value={f}>
                      {FLOW_LABELS[f]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          {flowKind === "CrossSellRate" && (
            <div className="flex flex-col gap-2">
              <Label>Module</Label>
              <Select
                items={modules.map((m) => ({ label: m.name, value: m.id }))}
                value={moduleId}
                onValueChange={(v) => v && setModuleId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {modules.map((m) => (
                    <SelectItem key={m.id} value={m.id}>
                      {m.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {modules.length === 0 && (
                <p className="text-xs text-destructive">
                  No modules exist — cross-sell needs a module product.
                </p>
              )}
            </div>
          )}

          <div className="grid grid-cols-3 gap-3">
            <div className="flex flex-col gap-2">
              <Label>Distribution</Label>
              <Select
                items={DISTRIBUTION_TYPES.map((t) => ({ label: t, value: t }))}
                value={type}
                onValueChange={(v) => v && setType(v as DistributionType)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {DISTRIBUTION_TYPES.map((t) => (
                    <SelectItem key={t} value={t}>
                      {t}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="param-p1">{label1}</Label>
              <Input
                id="param-p1"
                inputMode="decimal"
                className="font-mono"
                value={p1}
                onChange={(e) => setP1(e.target.value)}
              />
            </div>
            {label2 !== null && (
              <div className="flex flex-col gap-2">
                <Label htmlFor="param-p2">{label2}</Label>
                <Input
                  id="param-p2"
                  inputMode="decimal"
                  className="font-mono"
                  value={p2}
                  onChange={(e) => setP2(e.target.value)}
                />
              </div>
            )}
          </div>

          <div className="grid grid-cols-3 gap-3">
            <div className="flex flex-col gap-2">
              <Label>Provenance</Label>
              <Select
                items={PARAMETER_PROVENANCES.map((p) => ({
                  label: p,
                  value: p,
                }))}
                value={provenance}
                onValueChange={(v) =>
                  v && setProvenance(v as ParameterProvenance)
                }
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {PARAMETER_PROVENANCES.map((p) => (
                    <SelectItem key={p} value={p}>
                      {p}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label>Covariate link</Label>
              <Select
                items={[
                  { label: "None", value: "none" },
                  ...covariates.map((c) => ({ label: c.name, value: c.id })),
                ]}
                value={covariateId}
                onValueChange={(v) => v && setCovariateId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">None</SelectItem>
                  {covariates.map((c) => (
                    <SelectItem key={c.id} value={c.id}>
                      {c.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            {covariateId !== "none" && (
              <div className="flex flex-col gap-2">
                <Label htmlFor="param-beta">Beta</Label>
                <Input
                  id="param-beta"
                  inputMode="decimal"
                  className="font-mono"
                  value={beta}
                  onChange={(e) => setBeta(e.target.value)}
                />
              </div>
            )}
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="param-rationale">Rationale</Label>
            <Textarea
              id="param-rationale"
              value={rationale}
              onChange={(e) => setRationale(e.target.value)}
              rows={2}
              placeholder="Where this number comes from"
            />
          </div>

          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button
              type="submit"
              disabled={
                save.isPending ||
                !segmentId ||
                (flowKind === "CrossSellRate" && !moduleId)
              }
            >
              {save.isPending ? "Saving…" : "Save parameter"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
