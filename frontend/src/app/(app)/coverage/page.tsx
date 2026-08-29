"use client";

import * as React from "react";
import { Trash2Icon } from "lucide-react";
import { api } from "@/lib/api/client";
import type {
  ApproachDto,
  BusinessProcessDto,
  ClientSegmentDto,
  CoverageDto,
  CoverageTier,
  ProductDto,
} from "@/lib/api/types";
import { COVERAGE_TIERS } from "@/lib/api/types";
import {
  queryKeys,
  useActiveMarket,
  useApproaches,
  useBusinessProcesses,
  useCompetitors,
  useCoverages,
  useFeatures,
  useInvalidatingMutation,
  useProducts,
  useSegments,
  useWorkspace,
} from "@/lib/queries";
import { cn } from "@/lib/utils";
import { flattenProcessTree } from "@/lib/process-tree";
import { buildCoverageMap, coverageKey, resolveCoverage } from "@/lib/coverage";
import { bestOurAssessedTier, isDeltaMasked } from "@/lib/masking";
import { PageHeader, EmptyState } from "@/components/page-header";
import { MarketPicker } from "@/components/market-picker";
import { TierChip, TIER_LABELS } from "@/components/tier-chip";
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
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";

interface CellTarget {
  product: ProductDto;
  process: BusinessProcessDto;
  approachId: string | null;
  segmentId: string | null;
  /** The row matching the exact filter key, if assessed. */
  exact: CoverageDto | null;
  /** The generic (segment-null, approach-null) fallback shown dimmed. */
  inherited: CoverageDto | null;
}

export default function CoveragePage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const { markets, market } = useActiveMarket(workspaceId);
  const products = useProducts(market?.id);
  const processes = useBusinessProcesses(market?.id);
  const segments = useSegments(market?.id);
  const competitors = useCompetitors(market?.id);
  const approaches = useApproaches();
  const coverages = useCoverages();
  const features = useFeatures(workspaceId);

  const [segmentFilter, setSegmentFilter] = React.useState<string>("generic");
  const [approachFilter, setApproachFilter] = React.useState<string>("default");
  const [editing, setEditing] = React.useState<CellTarget | null>(null);

  const marketProducts = React.useMemo(
    () => products.data ?? [],
    [products.data],
  );
  const ourProductIds = React.useMemo(
    () => marketProducts.filter((p) => p.isOurs).map((p) => p.id),
    [marketProducts],
  );
  const processRows = React.useMemo(
    () => flattenProcessTree(processes.data ?? []),
    [processes.data],
  );

  // Columns: our products (parents then modules), then per competitor.
  const columns = React.useMemo(() => {
    const order = (list: ProductDto[]) => {
      const parents = list.filter((p) => p.parentProductId === null);
      const out: ProductDto[] = [];
      for (const parent of parents) {
        out.push(parent);
        out.push(...list.filter((p) => p.parentProductId === parent.id));
      }
      const seen = new Set(out.map((p) => p.id));
      out.push(...list.filter((p) => !seen.has(p.id)));
      return out;
    };
    const ours = order(marketProducts.filter((p) => p.isOurs));
    const theirs = (competitors.data ?? []).flatMap((c) =>
      order(marketProducts.filter((p) => p.competitorId === c.id)),
    );
    return [...ours, ...theirs];
  }, [marketProducts, competitors.data]);

  const competitorName = (id: string | null) =>
    (competitors.data ?? []).find((c) => c.id === id)?.name ?? "Competitor";

  // Approaches attached to this market's processes only.
  const marketApproaches = React.useMemo(() => {
    const processIds = new Set((processes.data ?? []).map((p) => p.id));
    return (approaches.data ?? []).filter((a) =>
      processIds.has(a.businessProcessId),
    );
  }, [approaches.data, processes.data]);

  // Coverage lookup for this market's products, on the full 4-part key.
  const coverageMap = React.useMemo(() => {
    const productIds = new Set(marketProducts.map((p) => p.id));
    return buildCoverageMap(
      (coverages.data ?? []).filter((c) => productIds.has(c.productId)),
    );
  }, [coverages.data, marketProducts]);

  const activeSegmentId = segmentFilter === "generic" ? null : segmentFilter;
  const activeApproachId = approachFilter === "default" ? null : approachFilter;

  const cellTarget = (
    product: ProductDto,
    process: BusinessProcessDto,
  ): CellTarget => {
    const { exact, inherited } = resolveCoverage(
      coverageMap,
      product.id,
      process.id,
      activeApproachId,
      activeSegmentId,
    );
    return {
      product,
      process,
      approachId: activeApproachId,
      segmentId: activeSegmentId,
      exact,
      inherited,
    };
  };

  // Pending (not-yet-Shipped) feature deltas that currently have zero
  // simulation impact, keyed by the exact (product, process, approach,
  // segment) cell they target — surfaced as a small dot on the grid.
  const maskedDeltaCells = React.useMemo(() => {
    const map = new Map<string, CoverageTier>();
    for (const feature of features.data ?? []) {
      if (feature.status === "Shipped" || feature.productId === null) continue;
      for (const delta of feature.deltas) {
        if (delta.kind !== "Coverage" || delta.businessProcessId === null) {
          continue;
        }
        // Masking mirrors the simulation: it maxes over every product we
        // own, not just the one this feature happens to be tagged with.
        // The dot is still drawn on the feature's own product cell — that's
        // just where a reader would look for it.
        if (!isDeltaMasked(delta, ourProductIds, coverageMap)) continue;
        const tier = bestOurAssessedTier(
          ourProductIds,
          delta.businessProcessId,
          delta.approachId,
          delta.segmentId,
          coverageMap,
        );
        if (!tier) continue;
        map.set(
          coverageKey(
            feature.productId,
            delta.businessProcessId,
            delta.approachId,
            delta.segmentId,
          ),
          tier,
        );
      }
    }
    return map;
  }, [features.data, coverageMap, ourProductIds]);

  const oursCount = columns.filter((p) => p.isOurs).length;

  return (
    <>
      <PageHeader
        title="Coverage"
        description="Gold / silver / bronze by business process — us against every rival, at a glance"
      >
        {workspaceId && (
          <MarketPicker
            workspaceId={workspaceId}
            markets={markets.data ?? []}
            market={market}
          />
        )}
      </PageHeader>

      <div className="flex flex-1 flex-col overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 border-b px-6 py-3">
          <div className="flex items-center gap-2">
            <Label className="text-xs text-muted-foreground">Segment</Label>
            <Select
              items={[
                { label: "Generic (all segments)", value: "generic" },
                ...(segments.data ?? []).map((s) => ({
                  label: s.name,
                  value: s.id,
                })),
              ]}
              value={segmentFilter}
              onValueChange={(v) => v && setSegmentFilter(v as string)}
            >
              <SelectTrigger size="sm" className="min-w-40">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="generic">Generic (all segments)</SelectItem>
                {(segments.data ?? []).map((s) => (
                  <SelectItem key={s.id} value={s.id}>
                    {s.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex items-center gap-2">
            <Label className="text-xs text-muted-foreground">Approach</Label>
            <Select
              items={[
                { label: "Default approach", value: "default" },
                ...marketApproaches.map((a) => ({
                  label: a.name,
                  value: a.id,
                })),
              ]}
              value={approachFilter}
              onValueChange={(v) => v && setApproachFilter(v as string)}
            >
              <SelectTrigger size="sm" className="min-w-40">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="default">Default approach</SelectItem>
                {marketApproaches.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="ml-auto flex items-center gap-2">
            <TierChip tier="Gold" size="sm" />
            <TierChip tier="Silver" size="sm" />
            <TierChip tier="Bronze" size="sm" />
            <TierChip tier="NotPlaying" size="sm" />
            <TierChip tier={null} unknown size="sm" />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-6">
          {!market ? (
            <EmptyState
              title="No market selected"
              hint="Create a market with processes and products first."
            />
          ) : processRows.length === 0 || columns.length === 0 ? (
            <EmptyState
              title="Nothing to assess yet"
              hint="The grid needs business processes (rows) and products (columns). Add them under Processes and Products."
            />
          ) : (
            <div className="overflow-auto rounded-xl border bg-card">
              <table className="w-full border-separate border-spacing-0 text-sm">
                <thead>
                  <tr>
                    <th className="sticky left-0 z-20 min-w-52 border-b bg-card px-4 py-2 text-left align-bottom font-medium text-muted-foreground">
                      Business process
                    </th>
                    {columns.map((product, i) => (
                      <th
                        key={product.id}
                        className={cn(
                          "min-w-28 border-b px-2 py-2 text-left align-bottom",
                          product.isOurs && "bg-primary/8",
                          i === oursCount - 1 &&
                            oursCount < columns.length &&
                            "border-r",
                        )}
                      >
                        <div className="flex flex-col gap-0.5">
                          <span className="text-[10px] font-medium tracking-wide text-muted-foreground uppercase">
                            {product.isOurs
                              ? "Ours"
                              : competitorName(product.competitorId)}
                          </span>
                          <span
                            className={cn(
                              "truncate text-xs font-semibold",
                              product.isOurs && "text-primary",
                            )}
                            title={product.name}
                          >
                            {product.name}
                            {product.isModule && (
                              <span className="ml-1 font-normal text-muted-foreground">
                                ·mod
                              </span>
                            )}
                          </span>
                        </div>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {processRows.map(({ process, depth }) => (
                    <tr key={process.id} className="group/row">
                      <td
                        className={cn(
                          "sticky left-0 z-10 border-b bg-card px-4 py-1.5",
                          depth === 0 ? "font-semibold" : "text-muted-foreground",
                        )}
                        style={{ paddingLeft: `${1 + depth * 1.25}rem` }}
                        title={process.description ?? undefined}
                      >
                        <span className="block max-w-56 truncate">
                          {process.name}
                        </span>
                      </td>
                      {columns.map((product, i) => {
                        const target = cellTarget(product, process);
                        const shown = target.exact ?? target.inherited;
                        const maskedTier = maskedDeltaCells.get(
                          coverageKey(
                            product.id,
                            process.id,
                            activeApproachId,
                            activeSegmentId,
                          ),
                        );
                        return (
                          <td
                            key={product.id}
                            className={cn(
                              "border-b p-1",
                              product.isOurs && "bg-primary/4",
                              i === oursCount - 1 &&
                                oursCount < columns.length &&
                                "border-r",
                            )}
                          >
                            <button
                              type="button"
                              onClick={() => setEditing(target)}
                              aria-label={`Assess ${product.name} on ${process.name}`}
                              className="relative flex h-8 w-full items-center justify-center rounded-md transition-colors outline-none hover:bg-accent/70 focus-visible:ring-2 focus-visible:ring-ring/50"
                            >
                              {shown ? (
                                <TierChip
                                  tier={shown.tier}
                                  unknown={shown.isUnknown}
                                  confidence={shown.confidence}
                                  inherited={target.exact === null}
                                  size="sm"
                                />
                              ) : (
                                <TierChip tier={null} size="sm" />
                              )}
                              {maskedTier && (
                                <Tooltip>
                                  <TooltipTrigger
                                    render={
                                      <span
                                        className="absolute top-0.5 right-0.5 size-1.5 rounded-full bg-muted-foreground/50"
                                        aria-label="Pending delta here has no simulation impact"
                                      />
                                    }
                                  />
                                  <TooltipContent className="max-w-64">
                                    Already assessed at {TIER_LABELS[maskedTier]}{" "}
                                    for {process.name} — a pending feature delta
                                    here currently has no simulation impact.
                                  </TooltipContent>
                                </Tooltip>
                              )}
                            </button>
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      {editing && (
        <CoverageCellDialog
          target={editing}
          segments={segments.data ?? []}
          approaches={marketApproaches}
          onOpenChange={(open) => !open && setEditing(null)}
        />
      )}
    </>
  );
}

function CoverageCellDialog({
  target,
  segments,
  approaches,
  onOpenChange,
}: {
  target: CellTarget;
  segments: ClientSegmentDto[];
  approaches: ApproachDto[];
  onOpenChange: (open: boolean) => void;
}) {
  const existing = target.exact;
  const [tier, setTier] = React.useState<CoverageTier>(
    existing?.tier ?? "Bronze",
  );
  const [isUnknown, setIsUnknown] = React.useState(existing?.isUnknown ?? false);
  const [confidencePct, setConfidencePct] = React.useState(
    String(Math.round((existing?.confidence ?? 1) * 100)),
  );
  const [rationale, setRationale] = React.useState(existing?.rationale ?? "");
  const [scaleLimits, setScaleLimits] = React.useState(
    existing?.scaleLimits ?? "",
  );

  const segmentName =
    target.segmentId === null
      ? "Generic (all segments)"
      : (segments.find((s) => s.id === target.segmentId)?.name ?? "Segment");
  const approachName =
    target.approachId === null
      ? "Default approach"
      : (approaches.find((a) => a.id === target.approachId)?.name ?? "Approach");

  const invalidate = () => [queryKeys.coverages];

  const save = useInvalidatingMutation(
    async () => {
      const confidence = Math.min(
        1,
        Math.max(0, (Number(confidencePct) || 0) / 100),
      );
      const common = {
        tier,
        isUnknown,
        confidence,
        rationale: rationale || null,
      };
      if (existing) {
        return api.coverages.update(existing.id, {
          ...common,
          scaleLimits: scaleLimits || null,
        });
      }
      const created = await api.coverages.upsert({
        productId: target.product.id,
        businessProcessId: target.process.id,
        approachId: target.approachId,
        clientSegmentId: target.segmentId,
        ...common,
      });
      if (scaleLimits.trim()) {
        return api.coverages.update(created.id, {
          ...common,
          scaleLimits: scaleLimits || null,
        });
      }
      return created;
    },
    invalidate,
    {
      successMessage: "Assessment saved",
      onSuccess: () => onOpenChange(false),
    },
  );

  const remove = useInvalidatingMutation(
    () => api.coverages.delete(existing!.id),
    invalidate,
    {
      successMessage: "Assessment removed",
      onSuccess: () => onOpenChange(false),
    },
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {target.product.name} × {target.process.name}
          </DialogTitle>
          <DialogDescription>
            {segmentName} · {approachName}
            {target.exact === null && target.inherited !== null && (
              <>
                {" "}
                — currently inheriting{" "}
                <strong>
                  {target.inherited.isUnknown
                    ? "Unknown"
                    : TIER_LABELS[target.inherited.tier]}
                </strong>{" "}
                from the generic assessment; saving writes a specific one.
              </>
            )}
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(undefined);
          }}
        >
          <label className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={isUnknown}
              onCheckedChange={(checked) => setIsUnknown(checked === true)}
            />
            We don&apos;t know (distinct from Bronze and from Not playing)
          </label>

          {!isUnknown && (
            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-2">
                <Label>Tier</Label>
                <Select
                  items={COVERAGE_TIERS.map((t) => ({
                    label: TIER_LABELS[t],
                    value: t,
                  }))}
                  value={tier}
                  onValueChange={(v) => v && setTier(v as CoverageTier)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {COVERAGE_TIERS.map((t) => (
                      <SelectItem key={t} value={t}>
                        <TierChip tier={t} size="sm" />
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="cov-confidence">Confidence %</Label>
                <Input
                  id="cov-confidence"
                  inputMode="decimal"
                  value={confidencePct}
                  onChange={(e) => setConfidencePct(e.target.value)}
                />
              </div>
            </div>
          )}

          <div className="flex flex-col gap-2">
            <Label htmlFor="cov-rationale">Rationale</Label>
            <Textarea
              id="cov-rationale"
              value={rationale}
              onChange={(e) => setRationale(e.target.value)}
              rows={2}
              placeholder="Why this tier — demos seen, docs read, client reports…"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="cov-scale">Capacity / scale note</Label>
            <Input
              id="cov-scale"
              value={scaleLimits}
              onChange={(e) => setScaleLimits(e.target.value)}
              placeholder="e.g. degrades past 10k portfolios"
            />
          </div>

          <DialogFooter className="mt-2">
            {existing && (
              <Button
                type="button"
                variant="ghost"
                className="mr-auto text-destructive"
                onClick={() => remove.mutate(undefined)}
                disabled={remove.isPending}
              >
                <Trash2Icon data-icon="inline-start" />
                Remove
              </Button>
            )}
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending}>
              {save.isPending ? "Saving…" : "Save assessment"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
