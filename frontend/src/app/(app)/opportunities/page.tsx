"use client";

import * as React from "react";
import {
  CheckIcon,
  GavelIcon,
  PencilIcon,
  PlusIcon,
  Trash2Icon,
  XIcon,
} from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api/client";
import type {
  BusinessProcessDto,
  ChannelDto,
  CompetitorDto,
  OpportunityDto,
  OpportunityStage,
  ProductDto,
} from "@/lib/api/types";
import {
  queryKeys,
  useActiveMarket,
  useAssumptionSets,
  useBusinessProcesses,
  useChannels,
  useCompetitors,
  useFirmsById,
  useInvalidatingMutation,
  useOpportunities,
  useProducts,
  useSegments,
  useWorkspace,
} from "@/lib/queries";
import {
  ageMonths,
  cycleStatus,
  expectedCycleMonths,
  filterByDecidedRange,
  formatMonths,
  groupOpportunities,
  type CycleStatus,
} from "@/lib/pipeline";
import { cn } from "@/lib/utils";
import { PageHeader, EmptyState } from "@/components/page-header";
import { MarketPicker } from "@/components/market-picker";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
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

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

function dateToApi(date: string): string {
  return `${date}T00:00:00Z`;
}

/** Firms worth opening opportunities against — the observed/inferred roster. */
function useRosterFirms(marketId: string | undefined) {
  return useQuery({
    queryKey: ["roster-firms", marketId ?? ""],
    queryFn: async () => {
      const [observed, inferred] = await Promise.all([
        api.firms.page({ marketId: marketId!, epistemicStatus: "Observed" }, 1, 200),
        api.firms.page({ marketId: marketId!, epistemicStatus: "Inferred" }, 1, 200),
      ]);
      return [...observed.items, ...inferred.items].sort((a, b) =>
        a.name.localeCompare(b.name),
      );
    },
    enabled: !!marketId,
  });
}

const STAGE_STYLES: Record<OpportunityStage, string> = {
  Open: "bg-secondary text-secondary-foreground",
  Qualified: "bg-primary/15 text-primary",
  Won: "bg-primary text-primary-foreground",
  Lost: "bg-destructive/15 text-destructive",
  NoDecision: "border border-dashed bg-transparent text-muted-foreground",
};

const CYCLE_STYLES: Record<CycleStatus, string> = {
  onTrack: "text-muted-foreground",
  stretched: "text-amber-600 dark:text-amber-400",
  overdue: "text-destructive",
};

export default function OpportunitiesPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const { markets, market } = useActiveMarket(workspaceId);
  const opportunities = useOpportunities(workspaceId);
  const channels = useChannels(market?.id);
  const products = useProducts(market?.id);
  const competitors = useCompetitors(market?.id);
  const processes = useBusinessProcesses(market?.id);
  const segments = useSegments(market?.id);
  const assumptionSets = useAssumptionSets(workspaceId);
  const rosterFirms = useRosterFirms(market?.id);

  const firmIds = React.useMemo(
    () => [...new Set((opportunities.data ?? []).map((o) => o.firmId))],
    [opportunities.data],
  );
  const firmsById = useFirmsById(firmIds);

  // The newest assumption set carrying sales-cycle priors supplies the
  // "expected cycle" each open deal is measured against.
  const cycleParams = React.useMemo(() => {
    const withCycles = (assumptionSets.data ?? []).filter((s) =>
      s.parameters.some((p) => p.key.startsWith("choice.sales-cycle-mu:")),
    );
    withCycles.sort(
      (a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt),
    );
    return withCycles[0]?.parameters ?? [];
  }, [assumptionSets.data]);

  // Scope to the active market via each opportunity's firm.
  const marketOpportunities = React.useMemo(() => {
    if (!market) return [];
    return (opportunities.data ?? []).filter(
      (o) => firmsById.get(o.firmId)?.marketId === market.id,
    );
  }, [opportunities.data, firmsById, market]);

  const groups = React.useMemo(
    () => groupOpportunities(marketOpportunities),
    [marketOpportunities],
  );

  const [decidedFrom, setDecidedFrom] = React.useState<string>("");
  const [decidedTo, setDecidedTo] = React.useState<string>("");
  const decidedRangeActive = decidedFrom !== "" || decidedTo !== "";
  const filteredDecided = React.useMemo(
    () =>
      filterByDecidedRange(
        groups.decided,
        decidedFrom || null,
        decidedTo || null,
      ),
    [groups.decided, decidedFrom, decidedTo],
  );

  const [createOpen, setCreateOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<OpportunityDto | null>(null);
  const [qualifying, setQualifying] = React.useState<OpportunityDto | null>(
    null,
  );
  const [deciding, setDeciding] = React.useState<OpportunityDto | null>(null);
  const [deleting, setDeleting] = React.useState<OpportunityDto | null>(null);

  const invalidate = () => [queryKeys.opportunities(workspaceId ?? "")];

  const deleteOpportunity = useInvalidatingMutation(
    (id: string) => api.opportunities.delete(id),
    invalidate,
    {
      successMessage: "Opportunity deleted",
      onSuccess: () => setDeleting(null),
    },
  );

  const now = new Date();
  const firmName = (id: string) => firmsById.get(id)?.name ?? "…";
  const channelName = (id: string | null) =>
    id === null
      ? "Direct"
      : (channels.data?.find((c) => c.id === id)?.name ?? "Unknown");
  const productName = (id: string) =>
    products.data?.find((p) => p.id === id)?.name ?? id.slice(0, 8);
  const processName = (id: string) =>
    processes.data?.find((p) => p.id === id)?.name ?? id.slice(0, 8);
  const segmentName = (id: string | null | undefined) =>
    segments.data?.find((s) => s.id === id)?.name ?? null;

  const renderCard = (o: OpportunityDto) => {
    const firm = firmsById.get(o.firmId);
    const age = ageMonths(o, now);
    const expected = firm?.clientSegmentId
      ? expectedCycleMonths(firm.clientSegmentId, cycleParams)
      : null;
    const status = cycleStatus(age, expected);
    const scopeShown = o.scope.slice(0, 4);
    return (
      <li
        key={o.id}
        className="group flex flex-col gap-2 rounded-xl border bg-card p-3"
      >
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="truncate text-sm font-medium">{firmName(o.firmId)}</p>
            <p className="text-[11px] text-muted-foreground">
              {segmentName(firm?.clientSegmentId) ?? "No segment"} ·{" "}
              {channelName(o.channelId)}
            </p>
          </div>
          <Badge className={cn("shrink-0 text-[10px]", STAGE_STYLES[o.stage])}>
            {o.stage === "NoDecision" ? "No decision" : o.stage}
          </Badge>
        </div>

        <div className="flex flex-wrap gap-1">
          {scopeShown.map((s) => (
            <Badge
              key={s.businessProcessId}
              variant="outline"
              className="max-w-40 truncate text-[10px] font-normal"
            >
              {processName(s.businessProcessId)}
            </Badge>
          ))}
          {o.scope.length > scopeShown.length && (
            <Badge variant="outline" className="text-[10px] font-normal">
              +{o.scope.length - scopeShown.length}
            </Badge>
          )}
          {o.isCrossSell && (
            <Badge variant="secondary" className="text-[10px]">
              Cross-sell
            </Badge>
          )}
        </div>

        <div className="flex items-center justify-between gap-2">
          <span
            className={cn(
              "font-mono text-[11px]",
              status ? CYCLE_STYLES[status] : "text-muted-foreground",
            )}
          >
            {formatMonths(age)}
            {expected !== null && ` of ~${formatMonths(expected)} cycle`}
            {status === "overdue" && " · overdue"}
            {status === "stretched" && " · stretched"}
          </span>
          {o.competitorIds.length > 0 && (
            <span className="text-[11px] text-muted-foreground">
              vs {o.competitorIds.length} competitor
              {o.competitorIds.length === 1 ? "" : "s"}
            </span>
          )}
        </div>

        {o.isDecided ? (
          o.winningProductId && (
            <p className="text-[11px] text-muted-foreground">
              Winner:{" "}
              <span className="font-medium text-foreground">
                {productName(o.winningProductId)}
              </span>
            </p>
          )
        ) : (
          <div className="flex gap-1 opacity-0 transition-opacity group-hover:opacity-100 group-focus-within:opacity-100">
            {o.stage === "Open" && (
              <Button
                variant="outline"
                size="sm"
                className="h-7 text-xs"
                onClick={() => setQualifying(o)}
              >
                <CheckIcon data-icon="inline-start" />
                Qualify
              </Button>
            )}
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs"
              onClick={() => setDeciding(o)}
            >
              <GavelIcon data-icon="inline-start" />
              Decide
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              className="h-7"
              aria-label="Edit opportunity"
              onClick={() => setEditing(o)}
            >
              <PencilIcon />
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              className="h-7"
              aria-label="Delete opportunity"
              onClick={() => setDeleting(o)}
            >
              <Trash2Icon className="text-destructive" />
            </Button>
          </div>
        )}
      </li>
    );
  };

  const columns: {
    title: string;
    hint: string;
    items: OpportunityDto[];
    totalCount?: number;
    extra?: React.ReactNode;
  }[] = [
    {
      title: "Open",
      hint: "Scope can still expand — land-and-expand happens here",
      items: groups.open,
    },
    {
      title: "Qualified",
      hint: "In active evaluation against the consideration set",
      items: groups.qualified,
    },
    {
      title: "Decided",
      hint: "Won, lost, or no decision — calibration reads these from the ledger",
      items: filteredDecided,
      totalCount: groups.decided.length,
      extra: (
        <div className="flex flex-col gap-1.5 rounded-lg border bg-muted/30 p-2">
          <div className="flex items-center gap-1.5">
            <Input
              type="date"
              aria-label="Decided from"
              value={decidedFrom}
              onChange={(e) => setDecidedFrom(e.target.value)}
              className="h-7 flex-1 text-xs"
            />
            <span className="text-xs text-muted-foreground">–</span>
            <Input
              type="date"
              aria-label="Decided to"
              value={decidedTo}
              onChange={(e) => setDecidedTo(e.target.value)}
              className="h-7 flex-1 text-xs"
            />
            {decidedRangeActive && (
              <Button
                variant="ghost"
                size="icon-sm"
                className="h-7 shrink-0"
                aria-label="Clear decided-date filter"
                onClick={() => {
                  setDecidedFrom("");
                  setDecidedTo("");
                }}
              >
                <XIcon />
              </Button>
            )}
          </div>
          {decidedRangeActive && (
            <p className="text-[11px] text-muted-foreground">
              {filteredDecided.length} of {groups.decided.length} shown
            </p>
          )}
        </div>
      ),
    },
  ];

  return (
    <>
      <PageHeader
        title="Pipeline"
        description="Opportunities — firms seeking coverage for a scoped set of business processes"
      >
        {workspaceId && (
          <MarketPicker
            workspaceId={workspaceId}
            markets={markets.data ?? []}
            market={market}
          />
        )}
        <Button
          disabled={!market}
          onClick={() => setCreateOpen(true)}
        >
          <PlusIcon data-icon="inline-start" />
          New opportunity
        </Button>
      </PageHeader>

      <div className="flex-1 overflow-auto p-6">
        {!market ? (
          <EmptyState
            title="No market selected"
            hint="Opportunities belong to firms, and firms live inside a market."
          />
        ) : marketOpportunities.length === 0 ? (
          <EmptyState
            title="No opportunities yet"
            hint="Each decided opportunity becomes a fact in the ledger — the raw material calibration fits win rates and sales cycles from."
          />
        ) : (
          <div className="grid gap-4 lg:grid-cols-3">
            {columns.map((col) => (
              <section key={col.title} className="flex min-w-0 flex-col gap-2">
                <div className="flex items-baseline gap-2">
                  <h2 className="font-heading text-sm font-semibold tracking-tight">
                    {col.title}
                  </h2>
                  <span className="font-mono text-xs text-muted-foreground">
                    {col.totalCount !== undefined
                      ? `${col.items.length} / ${col.totalCount}`
                      : col.items.length}
                  </span>
                </div>
                <p className="text-[11px] text-muted-foreground">{col.hint}</p>
                {col.extra}
                {col.items.length === 0 ? (
                  <div className="rounded-xl border border-dashed px-3 py-6 text-center text-xs text-muted-foreground">
                    {col.totalCount ? "No opportunities in this range" : "Nothing here"}
                  </div>
                ) : (
                  <ul className="flex flex-col gap-2">
                    {col.items.map(renderCard)}
                  </ul>
                )}
              </section>
            ))}
          </div>
        )}
      </div>

      {market && createOpen && (
        <CreateOpportunityDialog
          onOpenChange={(open) => !open && setCreateOpen(false)}
          workspaceId={workspaceId!}
          rosterFirms={rosterFirms.data ?? []}
          channels={channels.data ?? []}
          competitors={competitors.data ?? []}
          processes={processes.data ?? []}
          products={products.data ?? []}
        />
      )}

      {editing && workspaceId && (
        <EditOpportunityDialog
          opportunity={
            marketOpportunities.find((o) => o.id === editing.id) ?? editing
          }
          onOpenChange={(open) => !open && setEditing(null)}
          workspaceId={workspaceId}
          channels={channels.data ?? []}
          competitors={competitors.data ?? []}
          processes={processes.data ?? []}
          products={products.data ?? []}
        />
      )}

      {qualifying && workspaceId && (
        <QualifyDialog
          opportunity={qualifying}
          firmName={firmName(qualifying.firmId)}
          workspaceId={workspaceId}
          onOpenChange={(open) => !open && setQualifying(null)}
        />
      )}

      {deciding && workspaceId && (
        <DecideDialog
          opportunity={deciding}
          firmName={firmName(deciding.firmId)}
          workspaceId={workspaceId}
          products={products.data ?? []}
          onOpenChange={(open) => !open && setDeciding(null)}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title="Delete this opportunity?"
        description="Only undecided opportunities should be deleted — decided ones have already written their fact to the ledger."
        onConfirm={() => deleting && deleteOpportunity.mutate(deleting.id)}
        busy={deleteOpportunity.isPending}
      />
    </>
  );
}

// ---------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------

function CreateOpportunityDialog({
  onOpenChange,
  workspaceId,
  rosterFirms,
  channels,
  competitors,
  processes,
  products,
}: {
  onOpenChange: (open: boolean) => void;
  workspaceId: string;
  rosterFirms: { id: string; name: string }[];
  channels: ChannelDto[];
  competitors: CompetitorDto[];
  processes: BusinessProcessDto[];
  products: ProductDto[];
}) {
  const [firmId, setFirmId] = React.useState<string>("");
  const [channelId, setChannelId] = React.useState<string>("direct");
  const [openedDate, setOpenedDate] = React.useState(todayIso());
  const [scopeIds, setScopeIds] = React.useState<Set<string>>(new Set());
  const [competitorIds, setCompetitorIds] = React.useState<Set<string>>(
    new Set(),
  );
  const [moduleId, setModuleId] = React.useState<string>("none");
  const [notes, setNotes] = React.useState("");

  const modules = products.filter((p) => p.isOurs && p.isModule);

  const create = useInvalidatingMutation(
    () =>
      api.opportunities.create({
        firmId,
        openedAt: dateToApi(openedDate),
        channelId: channelId === "direct" ? null : channelId,
        crossSellModuleProductId: moduleId === "none" ? null : moduleId,
        scopeProcessIds: [...scopeIds],
        competitorIds: [...competitorIds],
        notes: notes || null,
      }),
    () => [queryKeys.opportunities(workspaceId)],
    {
      successMessage: "Opportunity opened",
      onSuccess: () => onOpenChange(false),
    },
  );

  const toggle = (set: Set<string>, id: string) => {
    const next = new Set(set);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    return next;
  };

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>New opportunity</DialogTitle>
          <DialogDescription>
            A cross-sell is not a special case — pick the module being sold at
            an existing client and scope it to the new processes.
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex max-h-[65vh] flex-col gap-4 overflow-y-auto pr-1"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate(undefined);
          }}
        >
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label>Firm</Label>
              <Select
                items={rosterFirms.map((f) => ({ label: f.name, value: f.id }))}
                value={firmId || null}
                onValueChange={(v) => v && setFirmId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Pick a firm" />
                </SelectTrigger>
                <SelectContent>
                  {rosterFirms.map((f) => (
                    <SelectItem key={f.id} value={f.id}>
                      {f.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="opp-opened">Opened</Label>
              <Input
                id="opp-opened"
                type="date"
                value={openedDate}
                onChange={(e) => setOpenedDate(e.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label>Channel</Label>
              <Select
                items={[
                  { label: "Direct", value: "direct" },
                  ...channels.map((c) => ({ label: c.name, value: c.id })),
                ]}
                value={channelId}
                onValueChange={(v) => v && setChannelId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="direct">Direct</SelectItem>
                  {channels.map((c) => (
                    <SelectItem key={c.id} value={c.id}>
                      {c.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label>Cross-sell module</Label>
              <Select
                items={[
                  { label: "Not a cross-sell", value: "none" },
                  ...modules.map((m) => ({ label: m.name, value: m.id })),
                ]}
                value={moduleId}
                onValueChange={(v) => v && setModuleId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">Not a cross-sell</SelectItem>
                  {modules.map((m) => (
                    <SelectItem key={m.id} value={m.id}>
                      {m.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="flex flex-col gap-2">
            <Label>Scope (business processes)</Label>
            <div className="flex max-h-40 flex-col gap-1.5 overflow-y-auto rounded-lg border p-2.5">
              {processes.length === 0 ? (
                <p className="text-xs text-muted-foreground">
                  No processes in this market yet.
                </p>
              ) : (
                processes.map((p) => (
                  <label key={p.id} className="flex items-center gap-2 text-sm">
                    <Checkbox
                      checked={scopeIds.has(p.id)}
                      onCheckedChange={() =>
                        setScopeIds((prev) => toggle(prev, p.id))
                      }
                    />
                    <span className="min-w-0 flex-1 truncate">{p.name}</span>
                  </label>
                ))
              )}
            </div>
          </div>

          {competitors.length > 0 && (
            <div className="flex flex-col gap-2">
              <Label>Competitors present</Label>
              <div className="flex flex-wrap gap-2 rounded-lg border p-2.5">
                {competitors.map((c) => (
                  <label
                    key={c.id}
                    className="flex items-center gap-1.5 text-sm"
                  >
                    <Checkbox
                      checked={competitorIds.has(c.id)}
                      onCheckedChange={() =>
                        setCompetitorIds((prev) => toggle(prev, c.id))
                      }
                    />
                    {c.name}
                  </label>
                ))}
              </div>
            </div>
          )}

          <div className="flex flex-col gap-2">
            <Label htmlFor="opp-notes">Notes</Label>
            <Textarea
              id="opp-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
            />
          </div>

          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button
              type="submit"
              disabled={create.isPending || !firmId || scopeIds.size === 0}
            >
              {create.isPending ? "Opening…" : "Open opportunity"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ---------------------------------------------------------------------
// Edit (details + live scope management)
// ---------------------------------------------------------------------

function EditOpportunityDialog({
  opportunity,
  onOpenChange,
  workspaceId,
  channels,
  competitors,
  processes,
  products,
}: {
  opportunity: OpportunityDto;
  onOpenChange: (open: boolean) => void;
  workspaceId: string;
  channels: ChannelDto[];
  competitors: CompetitorDto[];
  processes: BusinessProcessDto[];
  products: ProductDto[];
}) {
  const [channelId, setChannelId] = React.useState<string>(
    opportunity.channelId ?? "direct",
  );
  const [moduleId, setModuleId] = React.useState<string>(
    opportunity.crossSellModuleProductId ?? "none",
  );
  const [competitorIds, setCompetitorIds] = React.useState<Set<string>>(
    new Set(opportunity.competitorIds),
  );
  const [notes, setNotes] = React.useState(opportunity.notes ?? "");
  const [addProcessId, setAddProcessId] = React.useState<string>("");

  const invalidate = () => [queryKeys.opportunities(workspaceId)];

  const save = useInvalidatingMutation(
    () =>
      api.opportunities.update(opportunity.id, {
        channelId: channelId === "direct" ? null : channelId,
        crossSellModuleProductId: moduleId === "none" ? null : moduleId,
        competitorIds: [...competitorIds],
        notes: notes || null,
      }),
    invalidate,
    { successMessage: "Opportunity saved", onSuccess: () => onOpenChange(false) },
  );

  const addScope = useInvalidatingMutation(
    (businessProcessId: string) =>
      api.opportunities.addScope(opportunity.id, { businessProcessId }),
    invalidate,
    { successMessage: "Scope expanded", onSuccess: () => setAddProcessId("") },
  );

  const removeScope = useInvalidatingMutation(
    (businessProcessId: string) =>
      api.opportunities.removeScope(opportunity.id, businessProcessId),
    invalidate,
    { successMessage: "Removed from scope" },
  );

  const processName = (id: string) =>
    processes.find((p) => p.id === id)?.name ?? id.slice(0, 8);
  const inScope = new Set(opportunity.scope.map((s) => s.businessProcessId));
  const addable = processes.filter((p) => !inScope.has(p.id));
  const modules = products.filter((p) => p.isOurs && p.isModule);

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Edit opportunity</DialogTitle>
          <DialogDescription>
            Scope changes apply immediately — it can expand (or shrink)
            mid-cycle until the decision.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-2 rounded-lg border bg-muted/30 p-3">
          <Label className="text-xs text-muted-foreground">Scope</Label>
          <div className="flex flex-wrap gap-1.5">
            {opportunity.scope.map((s) => (
              <Badge
                key={s.businessProcessId}
                variant="outline"
                className="gap-1 font-normal"
              >
                {processName(s.businessProcessId)}
                <button
                  type="button"
                  aria-label={`Remove ${processName(s.businessProcessId)} from scope`}
                  className="text-muted-foreground hover:text-destructive"
                  onClick={() => removeScope.mutate(s.businessProcessId)}
                >
                  <XIcon className="size-3" />
                </button>
              </Badge>
            ))}
          </div>
          {addable.length > 0 && (
            <div className="flex items-center gap-2">
              <Select
                items={addable.map((p) => ({ label: p.name, value: p.id }))}
                value={addProcessId || null}
                onValueChange={(v) => v && setAddProcessId(v as string)}
              >
                <SelectTrigger size="sm" className="min-w-44">
                  <SelectValue placeholder="Expand scope…" />
                </SelectTrigger>
                <SelectContent>
                  {addable.map((p) => (
                    <SelectItem key={p.id} value={p.id}>
                      {p.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Button
                variant="outline"
                size="sm"
                disabled={!addProcessId || addScope.isPending}
                onClick={() => addScope.mutate(addProcessId)}
              >
                Add
              </Button>
            </div>
          )}
        </div>

        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(undefined);
          }}
        >
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label>Channel</Label>
              <Select
                items={[
                  { label: "Direct", value: "direct" },
                  ...channels.map((c) => ({ label: c.name, value: c.id })),
                ]}
                value={channelId}
                onValueChange={(v) => v && setChannelId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="direct">Direct</SelectItem>
                  {channels.map((c) => (
                    <SelectItem key={c.id} value={c.id}>
                      {c.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label>Cross-sell module</Label>
              <Select
                items={[
                  { label: "Not a cross-sell", value: "none" },
                  ...modules.map((m) => ({ label: m.name, value: m.id })),
                ]}
                value={moduleId}
                onValueChange={(v) => v && setModuleId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">Not a cross-sell</SelectItem>
                  {modules.map((m) => (
                    <SelectItem key={m.id} value={m.id}>
                      {m.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          {competitors.length > 0 && (
            <div className="flex flex-col gap-2">
              <Label>Competitors present</Label>
              <div className="flex flex-wrap gap-2 rounded-lg border p-2.5">
                {competitors.map((c) => (
                  <label
                    key={c.id}
                    className="flex items-center gap-1.5 text-sm"
                  >
                    <Checkbox
                      checked={competitorIds.has(c.id)}
                      onCheckedChange={() =>
                        setCompetitorIds((prev) => {
                          const next = new Set(prev);
                          if (next.has(c.id)) next.delete(c.id);
                          else next.add(c.id);
                          return next;
                        })
                      }
                    />
                    {c.name}
                  </label>
                ))}
              </div>
            </div>
          )}

          <div className="flex flex-col gap-2">
            <Label htmlFor="opp-edit-notes">Notes</Label>
            <Textarea
              id="opp-edit-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
            />
          </div>

          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending}>
              {save.isPending ? "Saving…" : "Save changes"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ---------------------------------------------------------------------
// Qualify
// ---------------------------------------------------------------------

function QualifyDialog({
  opportunity,
  firmName,
  workspaceId,
  onOpenChange,
}: {
  opportunity: OpportunityDto;
  firmName: string;
  workspaceId: string;
  onOpenChange: (open: boolean) => void;
}) {
  const [date, setDate] = React.useState(todayIso());

  const qualify = useInvalidatingMutation(
    () => api.opportunities.qualify(opportunity.id, { at: dateToApi(date) }),
    () => [queryKeys.opportunities(workspaceId)],
    {
      successMessage: "Opportunity qualified",
      onSuccess: () => onOpenChange(false),
    },
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Qualify {firmName}?</DialogTitle>
          <DialogDescription>
            Marks the opportunity as being actively evaluated — the timestamp
            feeds sales-cycle calibration.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="qualify-date">Qualified on</Label>
          <Input
            id="qualify-date"
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
          />
        </div>
        <DialogFooter>
          <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
          <Button
            disabled={qualify.isPending}
            onClick={() => qualify.mutate(undefined)}
          >
            {qualify.isPending ? "Qualifying…" : "Qualify"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ---------------------------------------------------------------------
// Decide
// ---------------------------------------------------------------------

function DecideDialog({
  opportunity,
  firmName,
  workspaceId,
  products,
  onOpenChange,
}: {
  opportunity: OpportunityDto;
  firmName: string;
  workspaceId: string;
  products: ProductDto[];
  onOpenChange: (open: boolean) => void;
}) {
  const [outcome, setOutcome] = React.useState<OpportunityStage>("Won");
  const [winnerId, setWinnerId] = React.useState<string>("");
  const [date, setDate] = React.useState(todayIso());

  const decide = useInvalidatingMutation(
    () =>
      api.opportunities.decide(opportunity.id, {
        outcome,
        winningProductId: outcome === "NoDecision" ? null : winnerId || null,
        at: dateToApi(date),
      }),
    () => [queryKeys.opportunities(workspaceId)],
    {
      successMessage: "Decision recorded — fact appended to the ledger",
      onSuccess: () => onOpenChange(false),
    },
  );

  const needsWinner = outcome === "Won";
  const winnerOptions =
    outcome === "Won"
      ? products
      : products.filter((p) => !p.isOurs);

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Decide {firmName}</DialogTitle>
          <DialogDescription>
            Deciding appends an immutable OpportunityDecided fact — calibration
            reads the ledger, not this row.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label>Outcome</Label>
              <Select
                items={[
                  { label: "Won", value: "Won" },
                  { label: "Lost", value: "Lost" },
                  { label: "No decision", value: "NoDecision" },
                ]}
                value={outcome}
                onValueChange={(v) => v && setOutcome(v as OpportunityStage)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Won">Won</SelectItem>
                  <SelectItem value="Lost">Lost</SelectItem>
                  <SelectItem value="NoDecision">No decision</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="decide-date">Decided on</Label>
              <Input
                id="decide-date"
                type="date"
                value={date}
                onChange={(e) => setDate(e.target.value)}
              />
            </div>
          </div>
          {outcome !== "NoDecision" && (
            <div className="flex flex-col gap-2">
              <Label>
                {outcome === "Won" ? "Winning product" : "Lost to"}
              </Label>
              <Select
                items={winnerOptions.map((p) => ({
                  label: p.name,
                  value: p.id,
                }))}
                value={winnerId || null}
                onValueChange={(v) => v && setWinnerId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue
                    placeholder={
                      outcome === "Won"
                        ? "Which of our products won?"
                        : "Which product won? (optional)"
                    }
                  />
                </SelectTrigger>
                <SelectContent>
                  {winnerOptions.map((p) => (
                    <SelectItem key={p.id} value={p.id}>
                      {p.name}
                      {p.isOurs ? " (ours)" : ""}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
        </div>
        <DialogFooter>
          <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
          <Button
            disabled={decide.isPending || (needsWinner && !winnerId)}
            onClick={() => decide.mutate(undefined)}
          >
            {decide.isPending ? "Recording…" : "Record decision"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
