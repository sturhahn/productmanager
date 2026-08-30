"use client";

import * as React from "react";
import {
  GitBranchIcon,
  LockIcon,
  PlayIcon,
  Trash2Icon,
} from "lucide-react";
import type { EChartsOption, SeriesOption } from "echarts";
import { api } from "@/lib/api/client";
import type {
  ClientSegmentDto,
  EngineFirmLevelDiagnostics,
  EngineMetricTrajectory,
  EngineResult,
  MarketEventDto,
  RoadmapDto,
  SimulationEngineMode,
  SimulationRunDto,
} from "@/lib/api/types";
import {
  queryKeys,
  useAllChannels,
  useAllSegments,
  useAssumptionSets,
  useInvalidatingMutation,
  useMarketEvents,
  useMarkets,
  useRoadmaps,
  useSimulationDiagnostics,
  useSimulationResult,
  useSimulationRuns,
  useWorkspace,
} from "@/lib/queries";
import {
  channelWinRates,
  formatMeanCount,
  formatRate,
  FUNNEL_OUTCOME_LABELS,
  funnelParts,
  funnelTotals,
  segmentWinRates,
} from "@/lib/funnel";
import {
  compareHeadlines,
  fanSeries,
  formatCompact,
  formatSignedCompact,
  headlineMetrics,
  METRIC_LABELS,
  metricOf,
  monthLabels,
  type TrajectoryMetricKey,
} from "@/lib/fanchart";
import {
  baseChartOption,
  EChart,
  useChartTokens,
  type ChartTokens,
} from "@/components/charts/echart";
import { cn } from "@/lib/utils";
import { PageHeader, EmptyState } from "@/components/page-header";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
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

const CHART_METRICS: TrajectoryMetricKey[] = [
  "recognizedArr",
  "bookings",
  "clients",
  "contributionMargin",
];

function runLabel(run: SimulationRunDto): string {
  return run.name || `Run ${run.id.slice(0, 8)}`;
}

function ModeBadge({ mode }: { mode: SimulationEngineMode }) {
  return mode === "FirmLevel" ? (
    <Badge className="bg-primary/15 text-[10px] text-primary">
      Firm-level · full detail
    </Badge>
  ) : (
    <Badge variant="secondary" className="text-[10px]">
      Aggregate · fast preview
    </Badge>
  );
}

function roadmapLabel(r: RoadmapDto): string {
  const version = r.versionName ? ` · ${r.versionName}` : "";
  const live = r.isLive ? " (live)" : "";
  return `${r.name}${version}${live}`;
}

export default function SimulatePage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const runs = useSimulationRuns(workspaceId);
  const roadmaps = useRoadmaps(workspaceId);
  const assumptionSets = useAssumptionSets(workspaceId);
  const segments = useAllSegments(workspaceId);
  const channels = useAllChannels(workspaceId);
  const tokens = useChartTokens();

  const [selectedRunId, setSelectedRunId] = React.useState<string | null>(null);
  const [mode, setMode] = React.useState<"results" | "compare">("results");
  const [compareA, setCompareA] = React.useState<string | null>(null);
  const [compareB, setCompareB] = React.useState<string | null>(null);
  const [deleting, setDeleting] = React.useState<SimulationRunDto | null>(null);

  const runList = runs.data ?? [];
  const selectedRun =
    runList.find((r) => r.id === selectedRunId) ?? runList[0] ?? null;

  const deleteRun = useInvalidatingMutation(
    (id: string) => api.simulationRuns.delete(id),
    () => [queryKeys.simulationRuns(workspaceId ?? "")],
    { successMessage: "Run deleted", onSuccess: () => setDeleting(null) },
  );

  return (
    <>
      <PageHeader
        title="Simulate"
        description="Monte Carlo over the market model — same seed, same inputs, same numbers, every time"
      >
        <div className="flex rounded-lg border p-0.5">
          <Button
            variant={mode === "results" ? "secondary" : "ghost"}
            size="sm"
            onClick={() => setMode("results")}
          >
            Results
          </Button>
          <Button
            variant={mode === "compare" ? "secondary" : "ghost"}
            size="sm"
            onClick={() => setMode("compare")}
          >
            Compare
          </Button>
        </div>
      </PageHeader>

      <div className="flex flex-1 overflow-hidden">
        <aside className="w-80 shrink-0 overflow-y-auto border-r p-4">
          {workspaceId && (
            <RunConfigPanel
              workspaceId={workspaceId}
              roadmaps={roadmaps.data ?? []}
              assumptionSets={assumptionSets.data ?? []}
              onCreated={(run) => {
                setSelectedRunId(run.id);
                setMode("results");
              }}
            />
          )}
        </aside>

        <div className="flex flex-1 flex-col overflow-y-auto">
          <div className="flex-1 p-6">
            {mode === "results" ? (
              selectedRun ? (
                <RunResults
                  key={selectedRun.id}
                  run={selectedRun}
                  segments={segments.data ?? []}
                  channelNames={
                    new Map((channels.data ?? []).map((c) => [c.id, c.name]))
                  }
                  tokens={tokens}
                />
              ) : (
                <EmptyState
                  title="No runs yet"
                  hint="Configure a run on the left — pick a roadmap branch and an assumption set — and hit Run simulation. Results appear here in seconds."
                />
              )
            ) : (
              <CompareView
                runs={runList}
                a={compareA}
                b={compareB}
                onPickA={setCompareA}
                onPickB={setCompareB}
                roadmaps={roadmaps.data ?? []}
                assumptionSetNames={
                  new Map(
                    (assumptionSets.data ?? []).map((s) => [s.id, s.name]),
                  )
                }
                tokens={tokens}
              />
            )}
          </div>

          <div className="border-t p-6">
            <h3 className="mb-3 font-heading text-sm font-semibold tracking-tight">
              Runs
            </h3>
            {runList.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                Every executed run is stored with a full input snapshot and is
                exactly reproducible.
              </p>
            ) : (
              <div className="overflow-hidden rounded-xl border bg-card">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Run</TableHead>
                      <TableHead>Engine</TableHead>
                      <TableHead>Created</TableHead>
                      <TableHead>Horizon</TableHead>
                      <TableHead>Paths</TableHead>
                      <TableHead>Seed</TableHead>
                      <TableHead>Elapsed</TableHead>
                      <TableHead />
                      <TableHead className="w-40 text-right">Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {runList.map((run) => (
                      <TableRow
                        key={run.id}
                        className={cn(
                          selectedRun?.id === run.id &&
                            mode === "results" &&
                            "bg-accent/40",
                        )}
                      >
                        <TableCell className="font-medium">
                          {runLabel(run)}
                        </TableCell>
                        <TableCell>
                          <ModeBadge mode={run.engineMode} />
                        </TableCell>
                        <TableCell className="font-mono text-xs text-muted-foreground">
                          {new Date(run.createdAt).toLocaleString()}
                        </TableCell>
                        <TableCell className="font-mono text-xs">
                          {run.horizonMonths}mo
                        </TableCell>
                        <TableCell className="font-mono text-xs">
                          {run.paths}
                        </TableCell>
                        <TableCell className="font-mono text-xs">
                          {run.seed}
                        </TableCell>
                        <TableCell className="font-mono text-xs text-muted-foreground">
                          {run.elapsedMs !== null ? `${run.elapsedMs}ms` : "—"}
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
                              onClick={() => {
                                setSelectedRunId(run.id);
                                setMode("results");
                              }}
                            >
                              View
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              className="text-muted-foreground"
                              onClick={() => {
                                if (compareA === null || compareB !== null) {
                                  setCompareA(run.id);
                                  setCompareB(null);
                                } else {
                                  setCompareB(run.id);
                                }
                                setMode("compare");
                              }}
                            >
                              {compareA === run.id
                                ? "A"
                                : compareB === run.id
                                  ? "B"
                                  : "Compare"}
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
        description="The stored trajectories and input snapshot are removed. The run cannot be edited — only deleted."
        onConfirm={() => deleting && deleteRun.mutate(deleting.id)}
        busy={deleteRun.isPending}
      />
    </>
  );
}

// ---------------------------------------------------------------------
// Config panel
// ---------------------------------------------------------------------

function RunConfigPanel({
  workspaceId,
  roadmaps,
  assumptionSets,
  onCreated,
}: {
  workspaceId: string;
  roadmaps: RoadmapDto[];
  assumptionSets: { id: string; name: string }[];
  onCreated: (run: SimulationRunDto) => void;
}) {
  const markets = useMarkets(workspaceId);
  const events = useMarketEvents(workspaceId);
  const hypotheticalEvents = (events.data ?? []).filter(
    (e) => e.isHypothetical,
  );

  const [name, setName] = React.useState("");
  const [engineMode, setEngineMode] =
    React.useState<SimulationEngineMode>("Aggregate");
  const [roadmapId, setRoadmapId] = React.useState<string>("");
  const [assumptionSetId, setAssumptionSetId] = React.useState<string>("");
  const [marketId, setMarketId] = React.useState<string>("all");
  const [horizon, setHorizon] = React.useState("60");
  const [paths, setPaths] = React.useState("1000");
  const [seed, setSeed] = React.useState("0");
  const [discount, setDiscount] = React.useState("10");
  const [terminal, setTerminal] = React.useState("2");
  const [eventIds, setEventIds] = React.useState<Set<string>>(new Set());

  const liveRoadmap = roadmaps.find((r) => r.isLive);
  const effectiveRoadmapId =
    roadmapId || liveRoadmap?.id || roadmaps[0]?.id || "";
  const effectiveSetId = assumptionSetId || assumptionSets[0]?.id || "";

  const create = useInvalidatingMutation(
    () =>
      api.simulationRuns.create({
        roadmapId: effectiveRoadmapId,
        assumptionSetId: effectiveSetId,
        horizonMonths: Math.max(1, Math.round(Number(horizon) || 60)),
        paths: Math.max(1, Math.round(Number(paths) || 1000)),
        seed: Math.round(Number(seed) || 0),
        discountRateAnnual: (Number(discount) || 0) / 100,
        terminalGrowthAnnual: (Number(terminal) || 0) / 100,
        name: name.trim() || null,
        marketId: marketId === "all" ? null : marketId,
        eventIds: [],
        hypotheticalEventIds: [...eventIds],
        engineMode,
      }),
    () => [queryKeys.simulationRuns(workspaceId)],
    {
      successMessage: "Run complete",
      onSuccess: (run) => onCreated(run),
    },
  );

  const toggleEvent = (event: MarketEventDto) =>
    setEventIds((prev) => {
      const next = new Set(prev);
      if (next.has(event.id)) next.delete(event.id);
      else next.add(event.id);
      return next;
    });

  const ready = effectiveRoadmapId !== "" && effectiveSetId !== "";

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h3 className="font-heading text-sm font-semibold tracking-tight">
          New run
        </h3>
        <p className="text-xs text-muted-foreground">
          Executes synchronously and lands in the runs list, immutable.
        </p>
      </div>

      <div className="flex flex-col gap-2">
        <Label className="text-xs">Engine</Label>
        <div className="flex flex-col gap-1 rounded-lg border p-1">
          {(
            [
              {
                mode: "Aggregate" as const,
                title: "Aggregate",
                hint: "fast preview — stock-and-flow per segment",
              },
              {
                mode: "FirmLevel" as const,
                title: "Firm-level",
                hint: "full detail — opportunities, utilities, switching",
              },
            ]
          ).map(({ mode, title, hint }) => (
            <button
              key={mode}
              type="button"
              aria-pressed={engineMode === mode}
              onClick={() => setEngineMode(mode)}
              className={cn(
                "flex flex-col rounded-md px-2.5 py-1.5 text-left transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring/50",
                engineMode === mode
                  ? "bg-accent text-accent-foreground"
                  : "text-muted-foreground hover:bg-accent/50",
              )}
            >
              <span className="text-sm font-medium">{title}</span>
              <span className="text-[11px] text-muted-foreground">{hint}</span>
            </button>
          ))}
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="run-name" className="text-xs">
          Name (optional)
        </Label>
        <Input
          id="run-name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="e.g. Baseline vs. Q3 slip"
        />
      </div>

      <div className="flex flex-col gap-2">
        <Label className="text-xs">Roadmap (branches included)</Label>
        <Select
          items={roadmaps.map((r) => ({ label: roadmapLabel(r), value: r.id }))}
          value={effectiveRoadmapId || null}
          onValueChange={(v) => v && setRoadmapId(v as string)}
        >
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {roadmaps.map((r) => (
              <SelectItem key={r.id} value={r.id}>
                <span className="flex items-center gap-1.5">
                  {r.parentRoadmapId !== null && (
                    <GitBranchIcon className="size-3.5 text-muted-foreground" />
                  )}
                  {roadmapLabel(r)}
                </span>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="flex flex-col gap-2">
        <Label className="text-xs">Assumption set</Label>
        <Select
          items={assumptionSets.map((s) => ({ label: s.name, value: s.id }))}
          value={effectiveSetId || null}
          onValueChange={(v) => v && setAssumptionSetId(v as string)}
        >
          <SelectTrigger className="w-full">
            <SelectValue />
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
        <Label className="text-xs">Market</Label>
        <Select
          items={[
            { label: "Whole workspace", value: "all" },
            ...(markets.data ?? []).map((m) => ({
              label: m.name,
              value: m.id,
            })),
          ]}
          value={marketId}
          onValueChange={(v) => v && setMarketId(v as string)}
        >
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Whole workspace</SelectItem>
            {(markets.data ?? []).map((m) => (
              <SelectItem key={m.id} value={m.id}>
                {m.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="grid grid-cols-3 gap-2">
        <div className="flex flex-col gap-1">
          <Label htmlFor="run-horizon" className="text-xs">
            Horizon (mo)
          </Label>
          <Input
            id="run-horizon"
            inputMode="numeric"
            className="font-mono"
            value={horizon}
            onChange={(e) => setHorizon(e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="run-paths" className="text-xs">
            Paths
          </Label>
          <Input
            id="run-paths"
            inputMode="numeric"
            className="font-mono"
            value={paths}
            onChange={(e) => setPaths(e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="run-seed" className="text-xs">
            Seed
          </Label>
          <Input
            id="run-seed"
            inputMode="numeric"
            className="font-mono"
            value={seed}
            onChange={(e) => setSeed(e.target.value)}
          />
        </div>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <div className="flex flex-col gap-1">
          <Label htmlFor="run-discount" className="text-xs">
            Discount % / yr
          </Label>
          <Input
            id="run-discount"
            inputMode="decimal"
            className="font-mono"
            value={discount}
            onChange={(e) => setDiscount(e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="run-terminal" className="text-xs">
            Terminal growth % / yr
          </Label>
          <Input
            id="run-terminal"
            inputMode="decimal"
            className="font-mono"
            value={terminal}
            onChange={(e) => setTerminal(e.target.value)}
          />
        </div>
      </div>

      {hypotheticalEvents.length > 0 && (
        <div className="flex flex-col gap-2">
          <Label className="text-xs">Hypothetical events to inject</Label>
          <div className="flex flex-col gap-1.5 rounded-lg border p-2.5">
            {hypotheticalEvents.map((event) => (
              <label
                key={event.id}
                className="flex items-center gap-2 text-sm"
              >
                <Checkbox
                  checked={eventIds.has(event.id)}
                  onCheckedChange={() => toggleEvent(event)}
                />
                <span className="min-w-0 flex-1 truncate">{event.name}</span>
                <span className="font-mono text-[10px] text-muted-foreground">
                  {event.startDate.slice(0, 7)}
                </span>
              </label>
            ))}
          </div>
        </div>
      )}

      <Button
        disabled={!ready || create.isPending}
        onClick={() => create.mutate(undefined)}
      >
        <PlayIcon data-icon="inline-start" />
        {create.isPending ? `Running ${paths} paths…` : "Run simulation"}
      </Button>
      {!ready && (
        <p className="text-xs text-muted-foreground">
          A run needs a roadmap and an assumption set.
        </p>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------
// Results
// ---------------------------------------------------------------------

function RunResults({
  run,
  segments,
  channelNames,
  tokens,
}: {
  run: SimulationRunDto;
  segments: ClientSegmentDto[];
  channelNames: Map<string, string>;
  tokens: ChartTokens;
}) {
  const result = useSimulationResult(run.id);
  const diagnostics = useSimulationDiagnostics(
    run.id,
    run.engineMode === "FirmLevel" && run.hasDiagnostics,
  );
  const [scope, setScope] = React.useState<string>("total");

  if (result.isPending) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-muted-foreground">
        Loading trajectories…
      </div>
    );
  }
  if (result.isError || !result.data) {
    return (
      <EmptyState
        title="No stored result"
        hint="This run has no stored trajectories — it may predate execution storage."
      />
    );
  }

  const data = result.data;
  const segmentName = (engineId: string) =>
    segments.find((s) => s.id === engineId)?.name ?? engineId.slice(0, 8);

  const scoped =
    scope === "total"
      ? data.total
      : (data.segments.find((s) => s.segmentId === scope) ?? data.total);
  const months = monthLabels(run.startDate, run.horizonMonths);
  const headline = headlineMetrics(data);

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="font-heading text-lg font-semibold tracking-tight">
              {runLabel(run)}
            </h2>
            <ModeBadge mode={run.engineMode} />
          </div>
          <p className="text-xs text-muted-foreground">
            {run.paths} paths · seed {run.seed} · {run.horizonMonths} months
            from {run.startDate} · {data.elapsedMs || run.elapsedMs || 0}ms
          </p>
        </div>
        <Select
          items={[
            { label: "Total (all segments)", value: "total" },
            ...data.segments
              .filter((s) => s.segmentId !== "total")
              .map((s) => ({
                label: segmentName(s.segmentId),
                value: s.segmentId,
              })),
          ]}
          value={scope}
          onValueChange={(v) => v && setScope(v as string)}
        >
          <SelectTrigger size="sm" className="min-w-44">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="total">Total (all segments)</SelectItem>
            {data.segments
              .filter((s) => s.segmentId !== "total")
              .map((s) => (
                <SelectItem key={s.segmentId} value={s.segmentId}>
                  {segmentName(s.segmentId)}
                </SelectItem>
              ))}
          </SelectContent>
        </Select>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard
          label="NPV (P50)"
          value={formatCompact(headline.npvP50)}
          sub={`P10 ${formatCompact(headline.npvP10)} — P90 ${formatCompact(headline.npvP90)}`}
          accent
        />
        <KpiCard
          label="Total bookings (P50)"
          value={formatCompact(headline.totalBookingsP50)}
          sub="summed over the horizon"
        />
        <KpiCard
          label="Ending clients (P50)"
          value={formatCompact(headline.endingClientsP50)}
          sub={`ARR ${formatCompact(headline.endingArrP50)} at month ${run.horizonMonths}`}
        />
        <KpiCard
          label="Terminal growth"
          value={`${(run.terminalGrowthAnnual * 100).toFixed(1)}%`}
          sub={`discounted at ${(run.discountRateAnnual * 100).toFixed(1)}%/yr`}
        />
      </div>

      {run.engineMode === "FirmLevel" && diagnostics.data && (
        <DiagnosticsPanel
          diagnostics={diagnostics.data}
          segmentName={segmentName}
          channelNames={channelNames}
        />
      )}

      <div className="grid gap-4 xl:grid-cols-2">
        {CHART_METRICS.map((metric) => (
          <FanChartCard
            key={`${metric}-${scope}-${tokens.themeKey}`}
            title={METRIC_LABELS[metric]}
            months={months}
            trajectory={metricOf(scoped, metric)}
            color={tokens.chart[0]}
            tokens={tokens}
          />
        ))}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------
// Firm-level diagnostics
// ---------------------------------------------------------------------

const OUTCOME_COLORS: Record<string, string> = {
  won: "var(--chart-4)",
  lost: "var(--chart-5)",
  noDecision: "var(--chart-3)",
  stillOpen: "var(--muted-foreground)",
};

function DiagnosticsPanel({
  diagnostics,
  segmentName,
  channelNames,
}: {
  diagnostics: EngineFirmLevelDiagnostics;
  segmentName: (id: string) => string;
  channelNames: Map<string, string>;
}) {
  const totals = funnelTotals(diagnostics.funnelBySegment);
  const parts = funnelParts(totals);
  const segRows = segmentWinRates(diagnostics.funnelBySegment);
  const chanRows = channelWinRates(diagnostics.funnelByChannel);
  const channelName = (id: string) =>
    id === "direct" ? "Direct" : (channelNames.get(id) ?? id.slice(0, 8));

  return (
    <section className="rounded-xl border bg-card p-4">
      <div className="mb-3 flex items-baseline justify-between">
        <h3 className="text-sm font-semibold">Opportunity funnel</h3>
        <span className="text-[10px] text-muted-foreground">
          mean across paths · opened = won + lost + no decision + still open
        </span>
      </div>

      <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard
          label="Win rate (decided)"
          value={formatRate(totals.winRate)}
          sub={`${formatMeanCount(totals.won)} of ${formatMeanCount(totals.won + totals.lost + totals.noDecision)} decided · gap on lost ${diagnostics.meanUtilityGapLostDeals.toFixed(2)}`}
          accent
        />
        <KpiCard
          label="Opportunities opened"
          value={formatMeanCount(totals.opened)}
          sub={`${formatMeanCount(totals.qualified)} qualified`}
        />
        <KpiCard
          label="Avg realized sales cycle"
          value={`${diagnostics.avgSalesCycleMonths.toFixed(1)} mo`}
          sub="open → decision, decided deals"
        />
        <KpiCard
          label="Scope expansions"
          value={formatMeanCount(diagnostics.expansionCount)}
          sub={`${formatMeanCount(diagnostics.syntheticFirmsCreated)} synthetic firms filled`}
        />
      </div>

      {totals.opened > 0 && (
        <div className="mb-4">
          <div
            className="flex h-4 w-full overflow-hidden rounded-full"
            role="img"
            aria-label="Opportunity outcomes"
          >
            {parts
              .filter((p) => p.fraction > 0)
              .map((p) => (
                <div
                  key={p.key}
                  style={{
                    width: `${p.fraction * 100}%`,
                    backgroundColor: OUTCOME_COLORS[p.key],
                    opacity: p.key === "stillOpen" ? 0.35 : 0.85,
                  }}
                  title={`${FUNNEL_OUTCOME_LABELS[p.key]}: ${formatMeanCount(p.value)}`}
                />
              ))}
          </div>
          <div className="mt-1.5 flex flex-wrap gap-x-4 gap-y-1">
            {parts.map((p) => (
              <span
                key={p.key}
                className="inline-flex items-center gap-1.5 text-[11px] text-muted-foreground"
              >
                <span
                  className="size-2 rounded-full"
                  style={{
                    backgroundColor: OUTCOME_COLORS[p.key],
                    opacity: p.key === "stillOpen" ? 0.35 : 0.85,
                  }}
                />
                {FUNNEL_OUTCOME_LABELS[p.key]}{" "}
                <span className="font-mono">{formatMeanCount(p.value)}</span>
              </span>
            ))}
          </div>
        </div>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <WinRateTable
          title="Win rate by segment"
          rows={segRows.map((r) => ({ ...r, name: segmentName(r.id) }))}
        />
        <WinRateTable
          title="Win rate by channel"
          rows={chanRows.map((r) => ({ ...r, name: channelName(r.id) }))}
        />
      </div>
    </section>
  );
}

function WinRateTable({
  title,
  rows,
}: {
  title: string;
  rows: { id: string; name: string; opened: number; won: number; winRate: number }[];
}) {
  return (
    <div className="overflow-hidden rounded-lg border">
      <p className="border-b bg-muted/40 px-3 py-1.5 text-xs font-semibold">
        {title}
      </p>
      {rows.length === 0 ? (
        <p className="px-3 py-3 text-xs text-muted-foreground">
          Nothing opened here.
        </p>
      ) : (
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-[11px] text-muted-foreground">
              <th className="px-3 py-1 font-medium">&nbsp;</th>
              <th className="px-3 py-1 text-right font-medium">Opened</th>
              <th className="px-3 py-1 text-right font-medium">Won</th>
              <th className="px-3 py-1 text-right font-medium">Win rate</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} className="border-t">
                <td className="max-w-40 truncate px-3 py-1.5">{r.name}</td>
                <td className="px-3 py-1.5 text-right font-mono text-xs">
                  {formatMeanCount(r.opened)}
                </td>
                <td className="px-3 py-1.5 text-right font-mono text-xs">
                  {formatMeanCount(r.won)}
                </td>
                <td className="px-3 py-1.5 text-right font-mono text-xs font-semibold">
                  {formatRate(r.winRate)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

function KpiCard({
  label,
  value,
  sub,
  accent = false,
}: {
  label: string;
  value: string;
  sub?: string;
  accent?: boolean;
}) {
  return (
    <div
      className={cn(
        "rounded-xl border bg-card p-4",
        accent && "border-primary/40 bg-primary/5",
      )}
    >
      <p className="text-xs text-muted-foreground">{label}</p>
      <p
        className={cn(
          "mt-1 font-mono text-2xl font-semibold tracking-tight",
          accent && "text-primary",
        )}
      >
        {value}
      </p>
      {sub && <p className="mt-0.5 text-[11px] text-muted-foreground">{sub}</p>}
    </div>
  );
}

function FanChartCard({
  title,
  months,
  trajectory,
  color,
  tokens,
  extra,
}: {
  title: string;
  months: string[];
  trajectory: EngineMetricTrajectory;
  color: string;
  tokens: ChartTokens;
  extra?: { name: string; color: string; trajectory: EngineMetricTrajectory };
}) {
  const option = React.useMemo<EChartsOption>(() => {
    const base = baseChartOption(tokens, months, formatCompact);
    const series = [
      ...fanSeries({
        id: "primary",
        name: extra ? "A" : title,
        color,
        p10: trajectory.p10,
        p50: trajectory.p50,
        p90: trajectory.p90,
      }),
      ...(extra
        ? fanSeries({
            id: "secondary",
            name: extra.name,
            color: extra.color,
            p10: extra.trajectory.p10,
            p50: extra.trajectory.p50,
            p90: extra.trajectory.p90,
            bandOpacity: 0.12,
          })
        : []),
    ];
    return { ...base, series: series as SeriesOption[] };
  }, [months, trajectory, color, tokens, title, extra]);

  return (
    <div className="rounded-xl border bg-card p-4">
      <div className="mb-1 flex items-center justify-between">
        <h3 className="text-sm font-semibold">{title}</h3>
        <span className="text-[10px] text-muted-foreground">
          band = P10–P90 · line = median
        </span>
      </div>
      <EChart option={option} className="h-56 w-full" ariaLabel={title} />
    </div>
  );
}

// ---------------------------------------------------------------------
// Compare
// ---------------------------------------------------------------------

function CompareView({
  runs,
  a,
  b,
  onPickA,
  onPickB,
  roadmaps,
  assumptionSetNames,
  tokens,
}: {
  runs: SimulationRunDto[];
  a: string | null;
  b: string | null;
  onPickA: (id: string) => void;
  onPickB: (id: string) => void;
  roadmaps: RoadmapDto[];
  assumptionSetNames: Map<string, string>;
  tokens: ChartTokens;
}) {
  const runA = runs.find((r) => r.id === a) ?? null;
  const runB = runs.find((r) => r.id === b) ?? null;
  const resultA = useSimulationResult(runA?.id);
  const resultB = useSimulationResult(runB?.id);

  const picker = (
    value: string | null,
    onPick: (id: string) => void,
    label: string,
    color: string,
  ) => (
    <div className="flex items-center gap-2">
      <span
        className="size-2.5 rounded-full"
        style={{ backgroundColor: color }}
      />
      <Label className="text-xs text-muted-foreground">{label}</Label>
      <Select
        items={runs.map((r) => ({ label: runLabel(r), value: r.id }))}
        value={value}
        onValueChange={(v) => v && onPick(v as string)}
      >
        <SelectTrigger size="sm" className="min-w-44">
          <SelectValue placeholder="Pick a run" />
        </SelectTrigger>
        <SelectContent>
          {runs.map((r) => (
            <SelectItem key={r.id} value={r.id}>
              {runLabel(r)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );

  const colorA = tokens.chart[0];
  const colorB = tokens.chart[4];

  const roadmapName = (id: string) => {
    const r = roadmaps.find((x) => x.id === id);
    return r ? roadmapLabel(r) : id.slice(0, 8);
  };
  const setName = (id: string) => assumptionSetNames.get(id) ?? id.slice(0, 8);

  const modeLabel = (m: SimulationRunDto["engineMode"]) =>
    m === "FirmLevel" ? "Firm-level" : "Aggregate";

  const inputRows: { label: string; a: string; b: string }[] =
    runA && runB
      ? [
          {
            label: "Engine",
            a: modeLabel(runA.engineMode),
            b: modeLabel(runB.engineMode),
          },
          { label: "Roadmap", a: roadmapName(runA.roadmapId), b: roadmapName(runB.roadmapId) },
          {
            label: "Assumption set",
            a: setName(runA.assumptionSetId),
            b: setName(runB.assumptionSetId),
          },
          { label: "Start date", a: runA.startDate, b: runB.startDate },
          {
            label: "Horizon",
            a: `${runA.horizonMonths}mo`,
            b: `${runB.horizonMonths}mo`,
          },
          { label: "Paths", a: String(runA.paths), b: String(runB.paths) },
          { label: "Seed", a: String(runA.seed), b: String(runB.seed) },
          {
            label: "Discount / yr",
            a: `${(runA.discountRateAnnual * 100).toFixed(1)}%`,
            b: `${(runB.discountRateAnnual * 100).toFixed(1)}%`,
          },
          {
            label: "Terminal growth / yr",
            a: `${(runA.terminalGrowthAnnual * 100).toFixed(1)}%`,
            b: `${(runB.terminalGrowthAnnual * 100).toFixed(1)}%`,
          },
        ]
      : [];

  const bothLoaded =
    runA !== null &&
    runB !== null &&
    resultA.data !== undefined &&
    resultB.data !== undefined;

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center gap-4">
        {picker(a, onPickA, "Run A", colorA)}
        {picker(b, onPickB, "Run B", colorB)}
      </div>

      {!runA || !runB ? (
        <EmptyState
          title="Pick two runs"
          hint="Choose A and B above (or use the Compare buttons in the runs list) to overlay their fan charts and diff their inputs."
        />
      ) : !bothLoaded ? (
        <div className="flex h-40 items-center justify-center text-sm text-muted-foreground">
          Loading both runs…
        </div>
      ) : (
        <CompareLoaded
          runA={runA}
          runB={runB}
          resultA={resultA.data!}
          resultB={resultB.data!}
          colorA={colorA}
          colorB={colorB}
          inputRows={inputRows}
          tokens={tokens}
        />
      )}
    </div>
  );
}

function CompareLoaded({
  runA,
  runB,
  resultA,
  resultB,
  colorA,
  colorB,
  inputRows,
  tokens,
}: {
  runA: SimulationRunDto;
  runB: SimulationRunDto;
  resultA: EngineResult;
  resultB: EngineResult;
  colorA: string;
  colorB: string;
  inputRows: { label: string; a: string; b: string }[];
  tokens: ChartTokens;
}) {
  // Shared axis: the longer horizon's labels.
  const months =
    runA.horizonMonths >= runB.horizonMonths
      ? monthLabels(runA.startDate, runA.horizonMonths)
      : monthLabels(runB.startDate, runB.horizonMonths);

  const deltas = compareHeadlines(resultA, resultB);
  const differingInputs = inputRows.filter((r) => r.a !== r.b);

  const mixedModes = runA.engineMode !== runB.engineMode;

  return (
    <>
      {mixedModes && (
        <div className="flex flex-wrap items-center gap-2 rounded-xl border border-dashed bg-muted/30 px-4 py-2.5 text-xs text-muted-foreground">
          <span className="font-medium text-foreground">
            Mixed engine modes:
          </span>
          <span className="inline-flex items-center gap-1.5">
            A <ModeBadge mode={runA.engineMode} />
          </span>
          <span className="inline-flex items-center gap-1.5">
            B <ModeBadge mode={runB.engineMode} />
          </span>
          <span>
            — both engines share the trajectory format, so the overlay still
            compares like for like; funnel diagnostics exist only on the
            firm-level run.
          </span>
        </div>
      )}

      <section className="rounded-xl border bg-card p-4">
        <h3 className="mb-2 text-sm font-semibold">Input references</h3>
        <p className="mb-3 text-xs text-muted-foreground">
          {differingInputs.length === 0
            ? "Identical inputs — any output difference would be a bug."
            : `${differingInputs.length} input${differingInputs.length === 1 ? "" : "s"} differ: ${differingInputs.map((r) => r.label).join(", ")}.`}
        </p>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground">
                <th className="py-1 pr-4 font-medium">Input</th>
                <th className="py-1 pr-4 font-medium">
                  <span className="inline-flex items-center gap-1.5">
                    <span
                      className="size-2 rounded-full"
                      style={{ backgroundColor: colorA }}
                    />
                    {runLabel(runA)}
                  </span>
                </th>
                <th className="py-1 font-medium">
                  <span className="inline-flex items-center gap-1.5">
                    <span
                      className="size-2 rounded-full"
                      style={{ backgroundColor: colorB }}
                    />
                    {runLabel(runB)}
                  </span>
                </th>
              </tr>
            </thead>
            <tbody>
              {inputRows.map((row) => {
                const differs = row.a !== row.b;
                return (
                  <tr
                    key={row.label}
                    className={cn("border-t", differs && "bg-primary/5")}
                  >
                    <td className="py-1.5 pr-4 text-muted-foreground">
                      {row.label}
                    </td>
                    <td
                      className={cn(
                        "py-1.5 pr-4 font-mono text-xs",
                        differs && "font-semibold",
                      )}
                    >
                      {row.a}
                    </td>
                    <td
                      className={cn(
                        "py-1.5 font-mono text-xs",
                        differs && "font-semibold",
                      )}
                    >
                      {row.b}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </section>

      <section className="rounded-xl border bg-card p-4">
        <h3 className="mb-3 text-sm font-semibold">Headline deltas (B − A)</h3>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground">
                <th className="py-1 pr-4 font-medium">Metric</th>
                <th className="py-1 pr-4 text-right font-medium">A</th>
                <th className="py-1 pr-4 text-right font-medium">B</th>
                <th className="py-1 pr-4 text-right font-medium">Δ</th>
                <th className="py-1 text-right font-medium">Δ%</th>
              </tr>
            </thead>
            <tbody>
              {deltas.map((row) => (
                <tr key={row.label} className="border-t">
                  <td className="py-1.5 pr-4">{row.label}</td>
                  <td className="py-1.5 pr-4 text-right font-mono text-xs">
                    {formatCompact(row.a)}
                  </td>
                  <td className="py-1.5 pr-4 text-right font-mono text-xs">
                    {formatCompact(row.b)}
                  </td>
                  <td
                    className={cn(
                      "py-1.5 pr-4 text-right font-mono text-xs font-semibold",
                      row.delta > 0 && "text-primary",
                      row.delta < 0 && "text-destructive",
                    )}
                  >
                    {formatSignedCompact(row.delta)}
                  </td>
                  <td className="py-1.5 text-right font-mono text-xs text-muted-foreground">
                    {row.deltaPct === null
                      ? "—"
                      : `${(row.deltaPct * 100).toFixed(1)}%`}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <div className="grid gap-4 xl:grid-cols-2">
        {CHART_METRICS.map((metric) => (
          <FanChartCard
            key={`${metric}-${tokens.themeKey}`}
            title={METRIC_LABELS[metric]}
            months={months}
            trajectory={metricOf(resultA.total, metric)}
            color={colorA}
            tokens={tokens}
            extra={{
              name: "B",
              color: colorB,
              trajectory: metricOf(resultB.total, metric),
            }}
          />
        ))}
      </div>
    </>
  );
}
