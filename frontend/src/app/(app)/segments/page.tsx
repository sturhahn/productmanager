"use client";

import * as React from "react";
import {
  PencilIcon,
  PlusIcon,
  Trash2Icon,
} from "lucide-react";
import { api } from "@/lib/api/client";
import type {
  BusinessProcessDto,
  ClientSegmentDto,
  NeedDto,
  ProductDto,
  SegmentModulePenetrationDto,
} from "@/lib/api/types";
import {
  queryKeys,
  useActiveMarket,
  useBusinessProcesses,
  useInvalidatingMutation,
  useProducts,
  useSegmentNeeds,
  useSegments,
  useWorkspace,
} from "@/lib/queries";
import { cn } from "@/lib/utils";
import { formatCompact } from "@/lib/fanchart";
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

export default function SegmentsPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const { markets, market } = useActiveMarket(workspaceId);
  const segments = useSegments(market?.id);

  const [chosenId, setChosenId] = React.useState<string | null>(null);
  const selected =
    segments.data?.find((s) => s.id === chosenId) ?? segments.data?.[0] ?? null;

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<ClientSegmentDto | null>(null);
  const [deleting, setDeleting] = React.useState<ClientSegmentDto | null>(null);

  const deleteSegment = useInvalidatingMutation(
    (id: string) => api.clientSegments.delete(id),
    () => [queryKeys.segments(market?.id ?? "")],
    { successMessage: "Segment deleted", onSuccess: () => setDeleting(null) },
  );

  return (
    <>
      <PageHeader
        title="Client segments"
        description="Groups of firms with a shared profile — the engine's unit of simulation"
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
          onClick={() => {
            setEditing(null);
            setEditorOpen(true);
          }}
        >
          <PlusIcon data-icon="inline-start" />
          New segment
        </Button>
      </PageHeader>

      <div className="flex flex-1 overflow-hidden">
        {!market ? (
          <div className="flex-1 p-6">
            <EmptyState
              title="No market selected"
              hint="Create a market first — segments live inside a market."
            />
          </div>
        ) : segments.data && segments.data.length === 0 ? (
          <div className="flex-1 p-6">
            <EmptyState
              title="No segments yet"
              hint="Define the groups of firms that behave alike: their needs, economics, and starting stocks."
            />
          </div>
        ) : (
          <>
            <aside className="w-60 shrink-0 overflow-y-auto border-r p-3">
              <ul className="flex flex-col gap-1">
                {(segments.data ?? []).map((s) => (
                  <li key={s.id}>
                    <button
                      type="button"
                      onClick={() => setChosenId(s.id)}
                      className={cn(
                        "w-full rounded-lg px-3 py-2 text-left text-sm transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring/50",
                        selected?.id === s.id
                          ? "bg-accent font-medium text-accent-foreground"
                          : "text-muted-foreground hover:bg-accent/60",
                      )}
                    >
                      <span className="block truncate">{s.name}</span>
                      <span className="block truncate font-mono text-[11px] text-muted-foreground/80">
                        {s.stockState.initialClientCount} clients ·{" "}
                        {formatCompact(s.stockState.initialProspectPool)} pool
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </aside>
            <div className="flex-1 overflow-y-auto p-6">
              {selected && market && (
                <SegmentDetail
                  key={selected.id}
                  segment={selected}
                  marketId={market.id}
                  onEdit={() => {
                    setEditing(selected);
                    setEditorOpen(true);
                  }}
                  onDelete={() => setDeleting(selected)}
                />
              )}
            </div>
          </>
        )}
      </div>

      {market && (
        <SegmentEditorDialog
          key={editing?.id ?? "new"}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          marketId={market.id}
          segment={editing}
          onCreated={(id) => setChosenId(id)}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "segment"}?`}
        description="Its stock state, penetrations, and needs go with it. Simulation parameters keyed to it become inert."
        onConfirm={() => deleting && deleteSegment.mutate(deleting.id)}
        busy={deleteSegment.isPending}
      />
    </>
  );
}

// ---------------------------------------------------------------------
// Detail
// ---------------------------------------------------------------------

function SegmentDetail({
  segment,
  marketId,
  onEdit,
  onDelete,
}: {
  segment: ClientSegmentDto;
  marketId: string;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const products = useProducts(marketId);
  const processes = useBusinessProcesses(marketId);
  const needs = useSegmentNeeds(segment.id);

  const [stockOpen, setStockOpen] = React.useState(false);
  const [penetrationEditing, setPenetrationEditing] =
    React.useState<SegmentModulePenetrationDto | null>(null);
  const [penetrationOpen, setPenetrationOpen] = React.useState(false);
  const [needEditing, setNeedEditing] = React.useState<NeedDto | null>(null);
  const [needOpen, setNeedOpen] = React.useState(false);

  const ourModules = (products.data ?? []).filter(
    (p) => p.isOurs && p.isModule,
  );
  const moduleName = (id: string) =>
    (products.data ?? []).find((p) => p.id === id)?.name ?? "Unknown module";
  const processName = (id: string) =>
    (processes.data ?? []).find((p) => p.id === id)?.name ?? "Unknown process";

  const removePenetration = useInvalidatingMutation(
    (moduleProductId: string) =>
      api.clientSegments.removeModulePenetration(segment.id, moduleProductId),
    () => [queryKeys.segments(marketId)],
    { successMessage: "Module penetration removed" },
  );

  const removeNeed = useInvalidatingMutation(
    (id: string) => api.needs.delete(id),
    () => [queryKeys.needs(segment.id)],
    { successMessage: "Need removed" },
  );

  const stock = segment.stockState;
  const stockTiles: { label: string; value: string }[] = [
    { label: "Prospect pool", value: formatCompact(stock.initialProspectPool) },
    { label: "Clients", value: String(stock.initialClientCount) },
    { label: "ARPU / mo", value: formatCompact(stock.arpuPerMonth) },
    {
      label: "Implementation fee",
      value: formatCompact(stock.implementationFee),
    },
    {
      label: "Servicing / client / mo",
      value: formatCompact(stock.servicingCostPerClient),
    },
    {
      label: "Infra fixed / mo",
      value: formatCompact(stock.infraFixedCostPerMonth),
    },
    {
      label: "Infra / client / mo",
      value: formatCompact(stock.infraCostPerClient),
    },
    { label: "R&D drag / mo", value: formatCompact(stock.rdDragPerMonth) },
  ];

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-6">
      <section className="flex items-start justify-between gap-4">
        <div>
          <h2 className="font-heading text-lg font-semibold tracking-tight">
            {segment.name}
          </h2>
          <p className="mt-0.5 text-sm text-muted-foreground">
            {segment.description || "No description yet."}
          </p>
          <div className="mt-2 flex gap-2">
            <Badge variant="secondary" className="font-mono text-[11px]">
              suite affinity {segment.suiteAffinity.toFixed(2)}
            </Badge>
            <Badge variant="secondary" className="font-mono text-[11px]">
              consolidation {segment.consolidationPreference.toFixed(2)}
            </Badge>
          </div>
        </div>
        <div className="flex shrink-0 gap-1">
          <Button variant="outline" size="sm" onClick={onEdit}>
            <PencilIcon data-icon="inline-start" />
            Edit profile
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Delete ${segment.name}`}
            onClick={onDelete}
          >
            <Trash2Icon className="text-destructive" />
          </Button>
        </div>
      </section>

      <section className="rounded-xl border bg-card p-4">
        <div className="mb-3 flex items-center justify-between">
          <div>
            <h3 className="text-sm font-semibold">Stock state</h3>
            <p className="text-xs text-muted-foreground">
              Where a simulation starts: pools, clients, and unit economics.
            </p>
          </div>
          <Button variant="outline" size="sm" onClick={() => setStockOpen(true)}>
            <PencilIcon data-icon="inline-start" />
            Edit
          </Button>
        </div>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {stockTiles.map((tile) => (
            <div key={tile.label} className="rounded-lg bg-muted/40 px-3 py-2">
              <p className="text-[11px] text-muted-foreground">{tile.label}</p>
              <p className="font-mono text-sm font-semibold">{tile.value}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="rounded-xl border bg-card p-4">
        <div className="mb-3 flex items-center justify-between">
          <div>
            <h3 className="text-sm font-semibold">Module penetration</h3>
            <p className="text-xs text-muted-foreground">
              Starting share of this segment&apos;s clients already on each
              cross-sellable module.
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            disabled={ourModules.length === 0}
            onClick={() => {
              setPenetrationEditing(null);
              setPenetrationOpen(true);
            }}
          >
            <PlusIcon data-icon="inline-start" />
            Add module
          </Button>
        </div>
        {segment.modulePenetrations.length === 0 ? (
          <p className="rounded-lg border border-dashed px-3 py-4 text-center text-sm text-muted-foreground">
            {ourModules.length === 0
              ? "No modules exist yet — add modules under our products first."
              : "No module penetration recorded."}
          </p>
        ) : (
          <ul className="divide-y">
            {segment.modulePenetrations.map((mp) => (
              <li
                key={mp.moduleProductId}
                className="group flex items-center gap-3 py-2"
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">
                    {moduleName(mp.moduleProductId)}
                  </p>
                  <p className="font-mono text-xs text-muted-foreground">
                    {(mp.penetration * 100).toFixed(0)}% penetration ·{" "}
                    {formatCompact(mp.arpuPerMonth)}/mo ARPU
                  </p>
                </div>
                <div className="inline-flex gap-1 opacity-0 transition-opacity group-hover:opacity-100">
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label="Edit penetration"
                    onClick={() => {
                      setPenetrationEditing(mp);
                      setPenetrationOpen(true);
                    }}
                  >
                    <PencilIcon />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label="Remove penetration"
                    onClick={() => removePenetration.mutate(mp.moduleProductId)}
                  >
                    <Trash2Icon className="text-destructive" />
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="rounded-xl border bg-card p-4">
        <div className="mb-3 flex items-center justify-between">
          <div>
            <h3 className="text-sm font-semibold">Needs</h3>
            <p className="text-xs text-muted-foreground">
              What this segment must accomplish, weighted by importance.
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            disabled={(processes.data ?? []).length === 0}
            onClick={() => {
              setNeedEditing(null);
              setNeedOpen(true);
            }}
          >
            <PlusIcon data-icon="inline-start" />
            Add need
          </Button>
        </div>
        {(needs.data ?? []).length === 0 ? (
          <p className="rounded-lg border border-dashed px-3 py-4 text-center text-sm text-muted-foreground">
            No needs recorded for this segment yet.
          </p>
        ) : (
          <ul className="divide-y">
            {(needs.data ?? []).map((need) => (
              <li key={need.id} className="group flex items-center gap-3 py-2">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <p className="truncate text-sm font-medium">
                      {processName(need.businessProcessId)}
                    </p>
                    <Badge
                      variant={need.isHard ? "default" : "outline"}
                      className="text-[10px]"
                    >
                      {need.isHard ? "Hard" : "Soft"}
                    </Badge>
                  </div>
                  <p className="truncate font-mono text-xs text-muted-foreground">
                    weight {need.importanceWeight.toFixed(2)}
                    {need.notes ? ` · ${need.notes}` : ""}
                  </p>
                </div>
                <div className="inline-flex gap-1 opacity-0 transition-opacity group-hover:opacity-100">
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label="Edit need"
                    onClick={() => {
                      setNeedEditing(need);
                      setNeedOpen(true);
                    }}
                  >
                    <PencilIcon />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label="Remove need"
                    onClick={() => removeNeed.mutate(need.id)}
                  >
                    <Trash2Icon className="text-destructive" />
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      {stockOpen && (
        <StockStateDialog
          segment={segment}
          marketId={marketId}
          onOpenChange={(open) => !open && setStockOpen(false)}
        />
      )}

      {penetrationOpen && (
        <PenetrationDialog
          key={penetrationEditing?.moduleProductId ?? "new"}
          segment={segment}
          marketId={marketId}
          modules={ourModules}
          existing={penetrationEditing}
          onOpenChange={(open) => !open && setPenetrationOpen(false)}
        />
      )}

      {needOpen && (
        <NeedDialog
          key={needEditing?.id ?? "new"}
          segment={segment}
          processes={processes.data ?? []}
          need={needEditing}
          onOpenChange={(open) => !open && setNeedOpen(false)}
        />
      )}
    </div>
  );
}

// ---------------------------------------------------------------------
// Dialogs
// ---------------------------------------------------------------------

function SegmentEditorDialog({
  open,
  onOpenChange,
  marketId,
  segment,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  marketId: string;
  segment: ClientSegmentDto | null;
  onCreated: (id: string) => void;
}) {
  const [name, setName] = React.useState(segment?.name ?? "");
  const [description, setDescription] = React.useState(
    segment?.description ?? "",
  );
  const [suiteAffinity, setSuiteAffinity] = React.useState(
    String(segment?.suiteAffinity ?? 0.5),
  );
  const [consolidation, setConsolidation] = React.useState(
    String(segment?.consolidationPreference ?? 0.5),
  );

  const save = useInvalidatingMutation(
    () =>
      segment
        ? api.clientSegments.update(segment.id, {
            name,
            description: description || null,
            suiteAffinity: Number(suiteAffinity) || 0,
            consolidationPreference: Number(consolidation) || 0,
          })
        : api.clientSegments.create({
            marketId,
            name,
            description: description || null,
            suiteAffinity: Number(suiteAffinity) || 0,
            consolidationPreference: Number(consolidation) || 0,
          }),
    () => [queryKeys.segments(marketId)],
    {
      successMessage: segment ? "Segment saved" : "Segment created",
      onSuccess: (saved) => {
        if (!segment) onCreated(saved.id);
        onOpenChange(false);
      },
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{segment ? "Edit segment" : "New segment"}</DialogTitle>
          <DialogDescription>
            Affinity and consolidation are 0–1 tendencies: suite vs
            best-of-breed scoping, and appetite for consolidating vendors.
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
            <Label htmlFor="seg-name">Name</Label>
            <Input
              id="seg-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="seg-desc">Description</Label>
            <Textarea
              id="seg-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label htmlFor="seg-suite">Suite affinity (0–1)</Label>
              <Input
                id="seg-suite"
                inputMode="decimal"
                value={suiteAffinity}
                onChange={(e) => setSuiteAffinity(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="seg-consol">Consolidation (0–1)</Label>
              <Input
                id="seg-consol"
                inputMode="decimal"
                value={consolidation}
                onChange={(e) => setConsolidation(e.target.value)}
              />
            </div>
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending
                ? "Saving…"
                : segment
                  ? "Save changes"
                  : "Create segment"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

const STOCK_FIELDS: {
  key: keyof ClientSegmentDto["stockState"];
  label: string;
  integer?: boolean;
}[] = [
  { key: "initialProspectPool", label: "Prospect pool (firms)" },
  { key: "initialClientCount", label: "Client count", integer: true },
  { key: "arpuPerMonth", label: "ARPU per month" },
  { key: "implementationFee", label: "Implementation fee" },
  { key: "servicingCostPerClient", label: "Servicing cost / client / mo" },
  { key: "infraFixedCostPerMonth", label: "Infra fixed cost / mo" },
  { key: "infraCostPerClient", label: "Infra cost / client / mo" },
  { key: "rdDragPerMonth", label: "R&D drag / mo" },
];

function StockStateDialog({
  segment,
  marketId,
  onOpenChange,
}: {
  segment: ClientSegmentDto;
  marketId: string;
  onOpenChange: (open: boolean) => void;
}) {
  const [values, setValues] = React.useState<Record<string, string>>(() => {
    const init: Record<string, string> = {};
    for (const f of STOCK_FIELDS) {
      init[f.key] = String(segment.stockState[f.key]);
    }
    return init;
  });

  const save = useInvalidatingMutation(
    () =>
      api.clientSegments.setStockState(segment.id, {
        initialProspectPool: Number(values.initialProspectPool) || 0,
        initialClientCount: Math.round(Number(values.initialClientCount) || 0),
        arpuPerMonth: Number(values.arpuPerMonth) || 0,
        implementationFee: Number(values.implementationFee) || 0,
        servicingCostPerClient: Number(values.servicingCostPerClient) || 0,
        infraFixedCostPerMonth: Number(values.infraFixedCostPerMonth) || 0,
        infraCostPerClient: Number(values.infraCostPerClient) || 0,
        rdDragPerMonth: Number(values.rdDragPerMonth) || 0,
      }),
    () => [queryKeys.segments(marketId)],
    { successMessage: "Stock state saved", onSuccess: () => onOpenChange(false) },
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{segment.name} — stock state</DialogTitle>
          <DialogDescription>
            One measurement of where the segment stands today. Amounts are per
            month in the workspace&apos;s single implicit currency.
          </DialogDescription>
        </DialogHeader>
        <form
          className="grid grid-cols-2 gap-x-4 gap-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(undefined);
          }}
        >
          {STOCK_FIELDS.map((f) => (
            <div key={f.key} className="flex flex-col gap-1">
              <Label htmlFor={`stock-${f.key}`} className="text-xs">
                {f.label}
              </Label>
              <Input
                id={`stock-${f.key}`}
                inputMode="decimal"
                className="font-mono"
                value={values[f.key]}
                onChange={(e) =>
                  setValues((v) => ({ ...v, [f.key]: e.target.value }))
                }
              />
            </div>
          ))}
          <DialogFooter className="col-span-2 mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending}>
              {save.isPending ? "Saving…" : "Save stock state"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function PenetrationDialog({
  segment,
  marketId,
  modules,
  existing,
  onOpenChange,
}: {
  segment: ClientSegmentDto;
  marketId: string;
  modules: ProductDto[];
  existing: SegmentModulePenetrationDto | null;
  onOpenChange: (open: boolean) => void;
}) {
  const [moduleId, setModuleId] = React.useState<string>(
    existing?.moduleProductId ?? modules[0]?.id ?? "",
  );
  const [penetrationPct, setPenetrationPct] = React.useState(
    String(((existing?.penetration ?? 0) * 100).toFixed(0)),
  );
  const [arpu, setArpu] = React.useState(String(existing?.arpuPerMonth ?? 0));

  const save = useInvalidatingMutation(
    () =>
      api.clientSegments.setModulePenetration(segment.id, {
        moduleProductId: moduleId,
        penetration: Math.min(
          1,
          Math.max(0, (Number(penetrationPct) || 0) / 100),
        ),
        arpuPerMonth: Number(arpu) || 0,
      }),
    () => [queryKeys.segments(marketId)],
    { successMessage: "Penetration saved", onSuccess: () => onOpenChange(false) },
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {existing ? "Edit module penetration" : "Add module penetration"}
          </DialogTitle>
          <DialogDescription>
            The share of {segment.name} clients already holding the module and
            what they pay for it monthly.
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
            <Label>Module</Label>
            <Select
              items={modules.map((m) => ({ label: m.name, value: m.id }))}
              value={moduleId}
              onValueChange={(v) => v && setModuleId(v as string)}
              disabled={existing !== null}
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
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label htmlFor="pen-pct">Penetration %</Label>
              <Input
                id="pen-pct"
                inputMode="decimal"
                value={penetrationPct}
                onChange={(e) => setPenetrationPct(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="pen-arpu">Module ARPU / mo</Label>
              <Input
                id="pen-arpu"
                inputMode="decimal"
                value={arpu}
                onChange={(e) => setArpu(e.target.value)}
              />
            </div>
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !moduleId}>
              {save.isPending ? "Saving…" : "Save"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function NeedDialog({
  segment,
  processes,
  need,
  onOpenChange,
}: {
  segment: ClientSegmentDto;
  processes: BusinessProcessDto[];
  need: NeedDto | null;
  onOpenChange: (open: boolean) => void;
}) {
  const [processId, setProcessId] = React.useState<string>(
    need?.businessProcessId ?? processes[0]?.id ?? "",
  );
  const [weight, setWeight] = React.useState(
    String(need?.importanceWeight ?? 1),
  );
  const [isHard, setIsHard] = React.useState(need?.isHard ?? false);
  const [notes, setNotes] = React.useState(need?.notes ?? "");

  const save = useInvalidatingMutation(
    () =>
      need
        ? api.needs.update(need.id, {
            importanceWeight: Number(weight) || 0,
            isHard,
            notes: notes || null,
          })
        : api.needs.create({
            clientSegmentId: segment.id,
            businessProcessId: processId,
            importanceWeight: Number(weight) || 0,
            isHard,
            notes: notes || null,
          }),
    () => [queryKeys.needs(segment.id)],
    {
      successMessage: need ? "Need saved" : "Need added",
      onSuccess: () => onOpenChange(false),
    },
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{need ? "Edit need" : "Add need"}</DialogTitle>
          <DialogDescription>
            A demand edge from {segment.name} to a business process. Hard needs
            disqualify products that don&apos;t cover them; soft needs cost a
            workaround.
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
            <Label>Business process</Label>
            <Select
              items={processes.map((p) => ({ label: p.name, value: p.id }))}
              value={processId}
              onValueChange={(v) => v && setProcessId(v as string)}
              disabled={need !== null}
            >
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {processes.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid grid-cols-2 items-end gap-3">
            <div className="flex flex-col gap-2">
              <Label htmlFor="need-weight">Importance weight</Label>
              <Input
                id="need-weight"
                inputMode="decimal"
                value={weight}
                onChange={(e) => setWeight(e.target.value)}
              />
            </div>
            <label className="flex h-8 items-center gap-2 text-sm">
              <Checkbox
                checked={isHard}
                onCheckedChange={(checked) => setIsHard(checked === true)}
              />
              Hard requirement
            </label>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="need-notes">Notes</Label>
            <Textarea
              id="need-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
              placeholder="Evidence, conditionality, workaround…"
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !processId}>
              {save.isPending ? "Saving…" : need ? "Save changes" : "Add need"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
