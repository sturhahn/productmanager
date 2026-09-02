"use client";

import * as React from "react";
import Link from "next/link";
import {
  ArrowDownRightIcon,
  ArrowRightIcon,
  ArrowUpRightIcon,
  ExternalLinkIcon,
  FlaskConicalIcon,
  LockIcon,
  Trash2Icon,
} from "lucide-react";
import { api } from "@/lib/api/client";
import type {
  AssumptionSetDto,
  CalibrationRunDto,
  SimulationFlowKind,
} from "@/lib/api/types";
import {
  queryKeys,
  useAllProducts,
  useAllSegments,
  useAssumptionSets,
  useCalibrationDiff,
  useCalibrationRuns,
  useInvalidatingMutation,
  useWorkspace,
} from "@/lib/queries";
import {
  diffSummary,
  formatMeanSd,
  formatRelativeShift,
  shapeDiffRows,
  type DiffRow,
  type ShiftMagnitude,
} from "@/lib/calibration";
import { cn } from "@/lib/utils";
import { PageHeader, EmptyState } from "@/components/page-header";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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

const FLOW_LABELS: Record<SimulationFlowKind, string> = {
  NewFirmArrival: "New-firm arrival",
  ConversionToMarket: "Conversion to market",
  WinRate: "Win rate",
  ChurnHazard: "Churn hazard",
  CrossSellRate: "Cross-sell rate",
};

function runLabel(run: CalibrationRunDto): string {
  return run.name || `Calibration ${run.id.slice(0, 8)}`;
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

export default function CalibrationPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const runs = useCalibrationRuns(workspaceId);
  const assumptionSets = useAssumptionSets(workspaceId);
  const segments = useAllSegments(workspaceId);
  const products = useAllProducts(workspaceId);

  const [selectedRunId, setSelectedRunId] = React.useState<string | null>(null);
  const [deleting, setDeleting] = React.useState<CalibrationRunDto | null>(
    null,
  );

  const runList = runs.data ?? [];
  const selectedRun =
    runList.find((r) => r.id === selectedRunId) ?? runList[0] ?? null;

  const deleteRun = useInvalidatingMutation(
    (id: string) => api.calibrationRuns.delete(id),
    () => [queryKeys.calibrationRuns(workspaceId ?? "")],
    { successMessage: "Calibration run deleted", onSuccess: () => setDeleting(null) },
  );

  const setName = (id: string | null) =>
    id === null
      ? "—"
      : (assumptionSets.data?.find((s) => s.id === id)?.name ?? id.slice(0, 8));

  return (
    <>
      <PageHeader
        title="Calibration"
        description="Fit the priors to the facts ledger as of a date — and see exactly what the data moved"
      />

      <div className="flex flex-1 overflow-hidden">
        <aside className="w-80 shrink-0 overflow-y-auto border-r p-4">
          {workspaceId && (
            <CalibrationConfigPanel
              workspaceId={workspaceId}
              assumptionSets={assumptionSets.data ?? []}
              onCreated={(runId) => setSelectedRunId(runId)}
            />
          )}
        </aside>

        <div className="flex flex-1 flex-col overflow-y-auto">
          <div className="flex-1 p-6">
            {selectedRun ? (
              <CalibrationResult
                key={selectedRun.id}
                run={selectedRun}
                priorName={setName(selectedRun.priorAssumptionSetId)}
                fittedName={setName(selectedRun.fittedAssumptionSetId)}
                segments={
                  new Map((segments.data ?? []).map((s) => [s.id, s.name]))
                }
                products={
                  new Map((products.data ?? []).map((p) => [p.id, p.name]))
                }
              />
            ) : (
              <EmptyState
                title="No calibration runs yet"
                hint="Pick a prior assumption set and an as-of date on the left. Only facts recorded by that date are read, so the fit honestly reconstructs what was known then."
              />
            )}
          </div>

          <div className="border-t p-6">
            <h3 className="mb-3 font-heading text-sm font-semibold tracking-tight">
              Calibration runs
            </h3>
            {runList.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                Every run is immutable and produces a new branch of its prior —
                the fitted set appears in the Assumptions lineage.
              </p>
            ) : (
              <div className="overflow-hidden rounded-xl border bg-card">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Run</TableHead>
                      <TableHead>As of</TableHead>
                      <TableHead>Prior</TableHead>
                      <TableHead>Fitted set</TableHead>
                      <TableHead>Created</TableHead>
                      <TableHead />
                      <TableHead className="w-28 text-right">Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {runList.map((run) => (
                      <TableRow
                        key={run.id}
                        className={cn(
                          selectedRun?.id === run.id && "bg-accent/40",
                        )}
                      >
                        <TableCell className="font-medium">
                          {runLabel(run)}
                        </TableCell>
                        <TableCell className="font-mono text-xs">
                          {run.factsAsOfDate}
                        </TableCell>
                        <TableCell className="text-sm text-muted-foreground">
                          {setName(run.priorAssumptionSetId)}
                        </TableCell>
                        <TableCell className="text-sm">
                          {setName(run.fittedAssumptionSetId)}
                        </TableCell>
                        <TableCell className="font-mono text-xs text-muted-foreground">
                          {new Date(run.createdAt).toLocaleString()}
                        </TableCell>
                        <TableCell>
                          {run.isExecuted && (
                            <Badge
                              variant="outline"
                              className="gap-1 text-[10px] text-muted-foreground"
                            >
                              <LockIcon className="size-3" />
                              Immutable
                            </Badge>
                          )}
                        </TableCell>
                        <TableCell className="text-right">
                          <div className="inline-flex gap-1">
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setSelectedRunId(run.id)}
                            >
                              View
                            </Button>
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              aria-label={`Delete ${runLabel(run)}`}
                              onClick={() => setDeleting(run)}
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
        </div>
      </div>

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting ? runLabel(deleting) : "run"}?`}
        description="The run record and its stored diff are removed. The fitted assumption set it produced stays in the lineage."
        onConfirm={() => deleting && deleteRun.mutate(deleting.id)}
        busy={deleteRun.isPending}
      />
    </>
  );
}

// ---------------------------------------------------------------------
// Config panel
// ---------------------------------------------------------------------

function CalibrationConfigPanel({
  workspaceId,
  assumptionSets,
  onCreated,
}: {
  workspaceId: string;
  assumptionSets: AssumptionSetDto[];
  onCreated: (runId: string) => void;
}) {
  const [assumptionSetId, setAssumptionSetId] = React.useState<string>("");
  const [asOfDate, setAsOfDate] = React.useState(todayIso());
  const [name, setName] = React.useState("");

  const effectiveSetId = assumptionSetId || assumptionSets[0]?.id || "";

  const create = useInvalidatingMutation(
    () =>
      api.calibrationRuns.create({
        assumptionSetId: effectiveSetId,
        asOfDate,
        name: name.trim() || null,
      }),
    () => [
      queryKeys.calibrationRuns(workspaceId),
      // A fitted set was branched off the prior — refresh the lineage.
      queryKeys.assumptionSets(workspaceId),
    ],
    {
      successMessage: "Calibration complete",
      onSuccess: (result) => onCreated(result.run.id),
    },
  );

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h3 className="font-heading text-sm font-semibold tracking-tight">
          Run calibration
        </h3>
        <p className="text-xs text-muted-foreground">
          Prior + facts ledger as of the date → a fitted branch of the prior,
          provenance Calibrated.
        </p>
      </div>

      <div className="flex flex-col gap-2">
        <Label className="text-xs">Prior assumption set</Label>
        <Select
          items={assumptionSets.map((s) => ({ label: s.name, value: s.id }))}
          value={effectiveSetId || null}
          onValueChange={(v) => v && setAssumptionSetId(v as string)}
        >
          <SelectTrigger className="w-full">
            <SelectValue placeholder="Pick a set" />
          </SelectTrigger>
          <SelectContent>
            {assumptionSets.map((s) => (
              <SelectItem key={s.id} value={s.id}>
                {s.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="cal-asof" className="text-xs">
          Facts as of
        </Label>
        <Input
          id="cal-asof"
          type="date"
          value={asOfDate}
          onChange={(e) => setAsOfDate(e.target.value)}
        />
        <p className="text-[11px] text-muted-foreground">
          Bi-temporal: only facts <em>recorded</em> by this date are read, so
          the fit reconstructs what was actually known.
        </p>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="cal-name" className="text-xs">
          Name (optional)
        </Label>
        <Input
          id="cal-name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="e.g. Post-Q2 refit"
        />
      </div>

      <Button
        disabled={!effectiveSetId || !asOfDate || create.isPending}
        onClick={() => create.mutate(undefined)}
      >
        <FlaskConicalIcon data-icon="inline-start" />
        {create.isPending ? "Fitting…" : "Run calibration"}
      </Button>
    </div>
  );
}

// ---------------------------------------------------------------------
// Result — the diff table
// ---------------------------------------------------------------------

const MAGNITUDE_WEIGHT: Record<ShiftMagnitude, string> = {
  none: "text-muted-foreground",
  small: "text-foreground",
  medium: "font-medium",
  large: "font-semibold",
};

function ShiftBadge({ row }: { row: DiffRow }) {
  const text = formatRelativeShift(row);
  if (row.direction === "flat") {
    return (
      <span className="inline-flex items-center gap-1 font-mono text-xs text-muted-foreground">
        <ArrowRightIcon className="size-3" />
        {text}
      </span>
    );
  }
  const up = row.direction === "up";
  const Icon = up ? ArrowUpRightIcon : ArrowDownRightIcon;
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 font-mono text-xs",
        MAGNITUDE_WEIGHT[row.magnitude],
        up ? "text-primary" : "text-destructive",
        row.magnitude === "large" && (up ? "bg-primary/10" : "bg-destructive/10"),
        row.magnitude === "medium" && (up ? "bg-primary/5" : "bg-destructive/5"),
      )}
    >
      <Icon className="size-3" />
      {text}
    </span>
  );
}

function CalibrationResult({
  run,
  priorName,
  fittedName,
  segments,
  products,
}: {
  run: CalibrationRunDto;
  priorName: string;
  fittedName: string;
  segments: Map<string, string>;
  products: Map<string, string>;
}) {
  const diff = useCalibrationDiff(run.id);
  const [showUnfitted, setShowUnfitted] = React.useState(false);

  if (diff.isPending) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-muted-foreground">
        Loading diff…
      </div>
    );
  }
  if (diff.isError || !diff.data) {
    return (
      <EmptyState
        title="No stored diff"
        hint="This calibration run has no stored diff to show."
      />
    );
  }

  const rows = shapeDiffRows(diff.data);
  const summary = diffSummary(diff.data);

  const scopeLabel = (row: {
    clientSegmentId: string | null;
    flowKind: string | null;
    moduleProductId: string | null;
    productId: string | null;
  }): string[] => {
    const parts: string[] = [];
    if (row.flowKind && row.flowKind in FLOW_LABELS) {
      parts.push(FLOW_LABELS[row.flowKind as SimulationFlowKind]);
    }
    if (row.clientSegmentId) {
      parts.push(segments.get(row.clientSegmentId) ?? "Unknown segment");
    }
    if (row.moduleProductId) {
      parts.push(products.get(row.moduleProductId) ?? "Unknown module");
    }
    if (row.productId) {
      parts.push(products.get(row.productId) ?? "Unknown product");
    }
    return parts;
  };

  const largestLabel = summary.largest
    ? [summary.largest.key, ...scopeLabel(summary.largest)].join(" · ")
    : null;

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-heading text-lg font-semibold tracking-tight">
            {runLabel(run)}
          </h2>
          <p className="text-xs text-muted-foreground">
            {priorName} → {fittedName} · facts as of {run.factsAsOfDate}
          </p>
        </div>
        {run.fittedAssumptionSetId && (
          <Button
            variant="outline"
            size="sm"
            nativeButton={false}
            render={
              <Link href={`/assumptions?set=${run.fittedAssumptionSetId}`} />
            }
          >
            <ExternalLinkIcon data-icon="inline-start" />
            Open fitted set in Assumptions
          </Button>
        )}
      </div>

      <div
        className={cn(
          "rounded-xl border p-4",
          summary.moved > 0
            ? "border-primary/40 bg-primary/5"
            : "bg-card",
        )}
      >
        <p className="text-sm">
          {summary.moved === 0 ? (
            <>
              The data moved{" "}
              <span className="font-semibold">no parameters</span> — the ledger
              holds nothing the priors didn&apos;t already say.
            </>
          ) : (
            <>
              The data moved{" "}
              <span className="font-semibold">
                {summary.moved} of {summary.fitted} fitted parameter
                {summary.fitted === 1 ? "" : "s"}
              </span>
              {summary.largest && (
                <>
                  {" — "}largest shift{" "}
                  <span className="font-mono font-semibold">
                    {formatRelativeShift(summary.largest)}
                  </span>{" "}
                  on <span className="font-medium">{largestLabel}</span>
                </>
              )}
              .
            </>
          )}
          {summary.unfitted > 0 && (
            <span className="text-muted-foreground">
              {" "}
              {summary.unfitted} prior{summary.unfitted === 1 ? "" : "s"}{" "}
              passed through untouched.
            </span>
          )}
        </p>
      </div>

      {rows.length > 0 && (
        <div className="overflow-hidden rounded-xl border bg-card">
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Parameter</TableHead>
                  <TableHead className="text-right">Prior</TableHead>
                  <TableHead className="w-10" />
                  <TableHead className="text-right">Posterior</TableHead>
                  <TableHead className="text-right">Shift</TableHead>
                  <TableHead className="text-right">n</TableHead>
                  <TableHead>Evidence</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row, i) => {
                  const scope = scopeLabel(row);
                  return (
                    <TableRow key={`${row.key}-${i}`}>
                      <TableCell>
                        <div className="flex flex-col gap-0.5">
                          <span className="font-mono text-xs">{row.key}</span>
                          {scope.length > 0 && (
                            <span className="text-[11px] text-muted-foreground">
                              {scope.join(" · ")}
                            </span>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="text-right font-mono text-xs text-muted-foreground">
                        {formatMeanSd(row.priorMean, row.priorStdDev)}
                      </TableCell>
                      <TableCell className="text-center text-muted-foreground/60">
                        →
                      </TableCell>
                      <TableCell
                        className={cn(
                          "text-right font-mono text-xs",
                          MAGNITUDE_WEIGHT[row.magnitude],
                        )}
                      >
                        {formatMeanSd(row.posteriorMean, row.posteriorStdDev)}
                      </TableCell>
                      <TableCell className="text-right">
                        <ShiftBadge row={row} />
                      </TableCell>
                      <TableCell className="text-right font-mono text-xs text-muted-foreground">
                        {row.sampleSize}
                      </TableCell>
                      <TableCell className="max-w-72 truncate text-xs text-muted-foreground">
                        {row.rationale}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
        </div>
      )}

      {diff.data.unfitted.length > 0 && (
        <section>
          <button
            type="button"
            className="text-xs font-medium text-muted-foreground underline-offset-2 hover:underline"
            onClick={() => setShowUnfitted((v) => !v)}
          >
            {showUnfitted ? "Hide" : "Show"} {diff.data.unfitted.length}{" "}
            untouched prior{diff.data.unfitted.length === 1 ? "" : "s"}
          </button>
          {showUnfitted && (
            <ul className="mt-2 flex flex-col gap-1 rounded-xl border bg-card p-3">
              {diff.data.unfitted.map((u, i) => {
                const scope = scopeLabel(u);
                return (
                  <li
                    key={`${u.key}-${i}`}
                    className="flex items-baseline gap-2 text-xs"
                  >
                    <span className="font-mono">{u.key}</span>
                    {scope.length > 0 && (
                      <span className="text-muted-foreground">
                        {scope.join(" · ")}
                      </span>
                    )}
                    <span className="ml-auto text-muted-foreground">
                      {u.reason}
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
        </section>
      )}
    </div>
  );
}
