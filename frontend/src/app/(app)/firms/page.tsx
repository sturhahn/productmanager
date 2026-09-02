"use client";

import * as React from "react";
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  PencilIcon,
  PlusIcon,
  SparklesIcon,
  Trash2Icon,
} from "lucide-react";
import { toast } from "sonner";
import { api } from "@/lib/api/client";
import type {
  ClientSegmentDto,
  CompetitorDto,
  EpistemicStatus,
  FirmDto,
  FirmProductRelationshipDto,
  ProductDto,
} from "@/lib/api/types";
import { EPISTEMIC_STATUSES } from "@/lib/api/types";
import {
  queryKeys,
  useActiveMarket,
  useBusinessProcesses,
  useCompetitors,
  useFirmsPage,
  useInvalidatingMutation,
  useProducts,
  useRelationshipsForFirms,
  useSegments,
  useWorkspace,
} from "@/lib/queries";
import { cn } from "@/lib/utils";
import { PageHeader, EmptyState } from "@/components/page-header";
import { MarketPicker } from "@/components/market-picker";
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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

const PAGE_SIZE = 25;

export function EpistemicBadge({ status }: { status: EpistemicStatus }) {
  if (status === "Observed") {
    return <Badge variant="secondary">Observed</Badge>;
  }
  if (status === "Inferred") {
    return <Badge variant="outline">Inferred</Badge>;
  }
  return (
    <Badge
      variant="outline"
      className="border-dashed text-muted-foreground"
    >
      Synthetic
    </Badge>
  );
}

// Small badge distinguishing our own products from competitor products in a
// firm's relationship list — "Ours" for products with a null competitorId,
// the competitor's name (joined client-side from already-fetched competitor
// data) otherwise, falling back to a generic "Competitor" label.
function ProductOriginBadge({
  product,
  competitors,
}: {
  product: ProductDto | undefined;
  competitors: CompetitorDto[];
}) {
  if (!product || product.competitorId === null) {
    return (
      <Badge variant="secondary" className="font-normal">
        Ours
      </Badge>
    );
  }
  const competitorName = competitors.find(
    (c) => c.id === product.competitorId,
  )?.name;
  return (
    <Badge variant="outline" className="font-normal text-muted-foreground">
      {competitorName ?? "Competitor"}
    </Badge>
  );
}

export default function FirmsPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const { markets, market } = useActiveMarket(workspaceId);
  const segments = useSegments(market?.id);
  const products = useProducts(market?.id);
  const competitors = useCompetitors(market?.id);
  const processes = useBusinessProcesses(market?.id);

  const [segmentFilter, setSegmentFilter] = React.useState<string>("all");
  const [statusFilter, setStatusFilter] = React.useState<string>("all");
  const [page, setPage] = React.useState(1);

  const firms = useFirmsPage(
    market?.id,
    {
      clientSegmentId: segmentFilter === "all" ? undefined : segmentFilter,
      epistemicStatus: statusFilter === "all" ? undefined : statusFilter,
    },
    page,
    PAGE_SIZE,
  );

  const pageFirmIds = React.useMemo(
    () => (firms.data?.items ?? []).map((f) => f.id),
    [firms.data],
  );
  const relationships = useRelationshipsForFirms(pageFirmIds);

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<FirmDto | null>(null);
  const [deleting, setDeleting] = React.useState<FirmDto | null>(null);
  const [generateOpen, setGenerateOpen] = React.useState(false);

  const invalidateFirms = () => [["firms-page"], ["firm-relationships"]];

  const deleteFirm = useInvalidatingMutation(
    (id: string) => api.firms.delete(id),
    invalidateFirms,
    { successMessage: "Firm deleted", onSuccess: () => setDeleting(null) },
  );

  const generate = useInvalidatingMutation(
    () => api.firms.generateSynthetic(market!.id),
    invalidateFirms,
    {
      onSuccess: (result) => {
        setGenerateOpen(false);
        toast.success(
          `Synthetic fill complete: ${result.created.toLocaleString()} firms created` +
            ` (${result.relationshipsCreated.toLocaleString()} relationships), ` +
            `${result.removed.toLocaleString()} replaced`,
        );
      },
    },
  );

  const segmentName = (id: string | null) =>
    id === null
      ? "—"
      : (segments.data?.find((s) => s.id === id)?.name ?? "Unknown");
  const productName = (id: string) =>
    products.data?.find((p) => p.id === id)?.name ?? id.slice(0, 8);

  const totalPages = firms.data?.totalPages ?? 1;
  const totalCount = firms.data?.totalCount ?? 0;

  const changeFilter = (apply: () => void) => {
    apply();
    setPage(1);
  };

  return (
    <>
      <PageHeader
        title="Firms"
        description="Every market participant — observed from the roster, inferred from evidence, or synthetic to fill the segment stocks"
      >
        {workspaceId && (
          <MarketPicker
            workspaceId={workspaceId}
            markets={markets.data ?? []}
            market={market}
          />
        )}
        <Button
          variant="outline"
          disabled={!market || generate.isPending}
          onClick={() => setGenerateOpen(true)}
        >
          <SparklesIcon data-icon="inline-start" />
          {generate.isPending ? "Generating…" : "Generate synthetic"}
        </Button>
        <Button
          disabled={!market}
          onClick={() => {
            setEditing(null);
            setEditorOpen(true);
          }}
        >
          <PlusIcon data-icon="inline-start" />
          New firm
        </Button>
      </PageHeader>

      <div className="flex-1 overflow-auto p-6">
        {!market ? (
          <EmptyState
            title="No market selected"
            hint="Create a market first — firms live inside a market."
          />
        ) : (
          <div className="flex flex-col gap-3">
            <div className="flex flex-wrap items-center gap-2">
              <Select
                items={[
                  { label: "All segments", value: "all" },
                  ...(segments.data ?? []).map((s) => ({
                    label: s.name,
                    value: s.id,
                  })),
                ]}
                value={segmentFilter}
                onValueChange={(v) =>
                  v && changeFilter(() => setSegmentFilter(v as string))
                }
              >
                <SelectTrigger size="sm" className="min-w-40">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All segments</SelectItem>
                  {(segments.data ?? []).map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Select
                items={[
                  { label: "All statuses", value: "all" },
                  ...EPISTEMIC_STATUSES.map((s) => ({ label: s, value: s })),
                ]}
                value={statusFilter}
                onValueChange={(v) =>
                  v && changeFilter(() => setStatusFilter(v as string))
                }
              >
                <SelectTrigger size="sm" className="min-w-36">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All statuses</SelectItem>
                  {EPISTEMIC_STATUSES.map((s) => (
                    <SelectItem key={s} value={s}>
                      {s}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <span className="ml-auto text-xs text-muted-foreground">
                {totalCount.toLocaleString()} firm{totalCount === 1 ? "" : "s"}
              </span>
            </div>

            {firms.data && firms.data.items.length === 0 ? (
              <EmptyState
                title="No firms match"
                hint="Add observed firms from your roster, or generate synthetic ones to fill each segment's stocks."
              />
            ) : (
              <div className="overflow-hidden rounded-xl border bg-card">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Name</TableHead>
                      <TableHead>Segment</TableHead>
                      <TableHead>Status</TableHead>
                      <TableHead>Incumbent products</TableHead>
                      <TableHead className="w-24 text-right">Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody
                    className={cn(firms.isPlaceholderData && "opacity-60")}
                  >
                    {(firms.data?.items ?? []).map((firm) => {
                      const rels = (relationships.get(firm.id) ?? []).filter(
                        (r) => r.isActive,
                      );
                      return (
                        <TableRow key={firm.id}>
                          <TableCell className="font-medium">
                            {firm.name}
                          </TableCell>
                          <TableCell className="text-sm text-muted-foreground">
                            {segmentName(firm.clientSegmentId)}
                          </TableCell>
                          <TableCell>
                            <EpistemicBadge status={firm.epistemicStatus} />
                          </TableCell>
                          <TableCell>
                            {rels.length === 0 ? (
                              <span className="text-xs text-muted-foreground">
                                prospect
                              </span>
                            ) : (
                              <div className="flex flex-wrap gap-1">
                                {rels.map((r) => (
                                  <IncumbentChip
                                    key={r.id}
                                    relationship={r}
                                    productName={productName(r.productId)}
                                    product={products.data?.find(
                                      (p) => p.id === r.productId,
                                    )}
                                    competitors={competitors.data ?? []}
                                    processCount={
                                      processes.data?.length ?? 0
                                    }
                                  />
                                ))}
                              </div>
                            )}
                          </TableCell>
                          <TableCell className="text-right">
                            <div className="inline-flex gap-1">
                              <Button
                                variant="ghost"
                                size="icon-sm"
                                aria-label={`Edit ${firm.name}`}
                                onClick={() => {
                                  setEditing(firm);
                                  setEditorOpen(true);
                                }}
                              >
                                <PencilIcon />
                              </Button>
                              <Button
                                variant="ghost"
                                size="icon-sm"
                                aria-label={`Delete ${firm.name}`}
                                onClick={() => setDeleting(firm)}
                              >
                                <Trash2Icon className="text-destructive" />
                              </Button>
                            </div>
                          </TableCell>
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>
              </div>
            )}

            {totalPages > 1 && (
              <div className="flex items-center justify-end gap-2">
                <span className="text-xs text-muted-foreground">
                  Page {page} of {totalPages}
                </span>
                <Button
                  variant="outline"
                  size="icon-sm"
                  aria-label="Previous page"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                >
                  <ChevronLeftIcon />
                </Button>
                <Button
                  variant="outline"
                  size="icon-sm"
                  aria-label="Next page"
                  disabled={page >= totalPages}
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                >
                  <ChevronRightIcon />
                </Button>
              </div>
            )}
          </div>
        )}
      </div>

      {market && (
        <FirmEditorDialog
          key={editing?.id ?? "new"}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          marketId={market.id}
          firm={editing}
          segments={segments.data ?? []}
          products={products.data ?? []}
          competitors={competitors.data ?? []}
          relationships={
            editing ? (relationships.get(editing.id) ?? []) : []
          }
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "firm"}?`}
        description="Its relationships and opportunities go with it. Observed firms are calibration ground truth — prefer marking notes over deleting."
        onConfirm={() => deleting && deleteFirm.mutate(deleting.id)}
        busy={deleteFirm.isPending}
      />

      <ConfirmDialog
        open={generateOpen}
        onOpenChange={setGenerateOpen}
        title="Generate synthetic firms?"
        description="Fills each segment's prospect pool and per-product client targets with deterministically generated synthetic firms wherever the observed roster falls short of the segment stocks. Regenerating replaces the previous synthetic population — observed and inferred firms are never touched."
        confirmLabel="Generate"
        onConfirm={() => generate.mutate(undefined)}
        busy={generate.isPending}
      />
    </>
  );
}

function IncumbentChip({
  relationship,
  productName,
  product,
  competitors,
  processCount,
}: {
  relationship: FirmProductRelationshipDto;
  productName: string;
  product: ProductDto | undefined;
  competitors: CompetitorDto[];
  processCount: number;
}) {
  const served = relationship.servedProcessIds.length;
  const holdings =
    served === 0
      ? "full coverage"
      : `${served}/${Math.max(processCount, served)} processes`;
  return (
    <Badge variant="outline" className="gap-1 font-normal">
      <span className="font-medium">{productName}</span>
      <ProductOriginBadge product={product} competitors={competitors} />
      <span className="text-[10px] text-muted-foreground">{holdings}</span>
    </Badge>
  );
}

// ---------------------------------------------------------------------
// Editor dialog (firm + its relationships)
// ---------------------------------------------------------------------

function FirmEditorDialog({
  open,
  onOpenChange,
  marketId,
  firm,
  segments,
  products,
  competitors,
  relationships,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  marketId: string;
  firm: FirmDto | null;
  segments: ClientSegmentDto[];
  products: ProductDto[];
  competitors: CompetitorDto[];
  relationships: FirmProductRelationshipDto[];
}) {
  const [name, setName] = React.useState(firm?.name ?? "");
  const [segmentId, setSegmentId] = React.useState<string>(
    firm?.clientSegmentId ?? "none",
  );
  const [status, setStatus] = React.useState<EpistemicStatus>(
    firm?.epistemicStatus ?? "Observed",
  );
  const [notes, setNotes] = React.useState(firm?.notes ?? "");

  const save = useInvalidatingMutation(
    () => {
      const clientSegmentId = segmentId === "none" ? null : segmentId;
      return firm
        ? api.firms.update(firm.id, {
            name,
            epistemicStatus: status,
            clientSegmentId,
            attributesJson: firm.attributesJson,
            notes: notes || null,
          })
        : api.firms.create({
            marketId,
            name,
            epistemicStatus: status,
            clientSegmentId,
            notes: notes || null,
          });
    },
    () => [["firms-page"]],
    {
      successMessage: firm ? "Firm saved" : "Firm created",
      onSuccess: () => onOpenChange(false),
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{firm ? "Edit firm" : "New firm"}</DialogTitle>
          <DialogDescription>
            Observed firms are ground truth for calibration; inferred ones
            carry latent attributes the engine samples from segment
            distributions.
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
            <Label htmlFor="firm-name">Name</Label>
            <Input
              id="firm-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-2">
              <Label>Segment</Label>
              <Select
                items={[
                  { label: "Unassigned", value: "none" },
                  ...segments.map((s) => ({ label: s.name, value: s.id })),
                ]}
                value={segmentId}
                onValueChange={(v) => v && setSegmentId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">Unassigned</SelectItem>
                  {segments.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-2">
              <Label>Epistemic status</Label>
              <Select
                items={EPISTEMIC_STATUSES.map((s) => ({ label: s, value: s }))}
                value={status}
                onValueChange={(v) => v && setStatus(v as EpistemicStatus)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {EPISTEMIC_STATUSES.map((s) => (
                    <SelectItem key={s} value={s}>
                      {s}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="firm-notes">Notes</Label>
            <Textarea
              id="firm-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
            />
          </div>

          {firm && (
            <RelationshipsSection
              firm={firm}
              products={products}
              competitors={competitors}
              relationships={relationships}
            />
          )}

          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending
                ? "Saving…"
                : firm
                  ? "Save changes"
                  : "Create firm"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function RelationshipsSection({
  firm,
  products,
  competitors,
  relationships,
}: {
  firm: FirmDto;
  products: ProductDto[];
  competitors: CompetitorDto[];
  relationships: FirmProductRelationshipDto[];
}) {
  const [productId, setProductId] = React.useState<string>("");
  const [priceLevel, setPriceLevel] = React.useState("1.0");

  const invalidate = () => [
    queryKeys.firmRelationships(`firm:${firm.id}`),
    ["firms-page"],
  ];

  const addRelationship = useInvalidatingMutation(
    () =>
      api.firmRelationships.create({
        firmId: firm.id,
        productId,
        priceLevel: Number(priceLevel) || 1,
      }),
    invalidate,
    { successMessage: "Relationship added", onSuccess: () => setProductId("") },
  );

  const endRelationship = useInvalidatingMutation(
    (id: string) => api.firmRelationships.delete(id),
    invalidate,
    { successMessage: "Relationship removed" },
  );

  const productName = (id: string) =>
    products.find((p) => p.id === id)?.name ?? id.slice(0, 8);
  const held = new Set(
    relationships.filter((r) => r.isActive).map((r) => r.productId),
  );
  const addable = products.filter((p) => !held.has(p.id));

  return (
    <div className="flex flex-col gap-2 rounded-lg border bg-muted/30 p-3">
      <Label className="text-xs text-muted-foreground">
        Product relationships (incumbency the choice engine switches against)
      </Label>
      {relationships.filter((r) => r.isActive).length === 0 ? (
        <p className="text-xs text-muted-foreground">
          No active relationships — this firm is a prospect.
        </p>
      ) : (
        <ul className="flex flex-col gap-1">
          {relationships
            .filter((r) => r.isActive)
            .map((r) => (
              <li
                key={r.id}
                className="flex items-center gap-2 rounded-md border bg-card px-2 py-1.5 text-sm"
              >
                <span className="min-w-0 flex-1 truncate font-medium">
                  {productName(r.productId)}
                </span>
                <ProductOriginBadge
                  product={products.find((p) => p.id === r.productId)}
                  competitors={competitors}
                />
                <span className="font-mono text-[11px] text-muted-foreground">
                  {r.servedProcessIds.length === 0
                    ? "full coverage"
                    : `${r.servedProcessIds.length} processes`}
                  {" · "}
                  {Math.round(r.priceLevel * 100)}% of list
                </span>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Remove ${productName(r.productId)}`}
                  onClick={() => endRelationship.mutate(r.id)}
                >
                  <Trash2Icon className="text-destructive" />
                </Button>
              </li>
            ))}
        </ul>
      )}
      <div className="flex items-end gap-2">
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <Select
            items={addable.map((p) => ({ label: p.name, value: p.id }))}
            value={productId || null}
            onValueChange={(v) => v && setProductId(v as string)}
          >
            <SelectTrigger size="sm" className="w-full">
              <SelectValue placeholder="Add a product…" />
            </SelectTrigger>
            <SelectContent>
              {addable.map((p) => (
                <SelectItem key={p.id} value={p.id}>
                  {p.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex w-24 flex-col gap-1">
          <Label htmlFor="rel-price" className="text-[10px]">
            Price level
          </Label>
          <Input
            id="rel-price"
            inputMode="decimal"
            className="h-8 font-mono text-xs"
            value={priceLevel}
            onChange={(e) => setPriceLevel(e.target.value)}
          />
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!productId || addRelationship.isPending}
          onClick={() => addRelationship.mutate(undefined)}
        >
          Add
        </Button>
      </div>
    </div>
  );
}
