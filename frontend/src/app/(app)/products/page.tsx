"use client";

import * as React from "react";
import {
  CheckIcon,
  CornerDownRightIcon,
  DollarSignIcon,
  Link2Icon,
  PencilIcon,
  PlusIcon,
  Trash2Icon,
} from "lucide-react";
import { api } from "@/lib/api/client";
import type {
  ClientSegmentDto,
  CompetitorDto,
  IntegrationDto,
  ProductDto,
} from "@/lib/api/types";
import {
  queryKeys,
  useActiveMarket,
  useCompetitors,
  useIntegrations,
  useInvalidatingMutation,
  useProducts,
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

/** Parents first, each followed by its modules. */
function orderWithModules(products: ProductDto[]): {
  product: ProductDto;
  isChild: boolean;
}[] {
  const parents = products.filter((p) => p.parentProductId === null);
  const out: { product: ProductDto; isChild: boolean }[] = [];
  for (const parent of parents) {
    out.push({ product: parent, isChild: false });
    for (const child of products.filter(
      (p) => p.parentProductId === parent.id,
    )) {
      out.push({ product: child, isChild: true });
    }
  }
  // Modules whose parent got filtered out of this group still render.
  const seen = new Set(out.map((r) => r.product.id));
  for (const p of products) {
    if (!seen.has(p.id)) out.push({ product: p, isChild: true });
  }
  return out;
}

export default function ProductsPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const { markets, market } = useActiveMarket(workspaceId);
  const products = useProducts(market?.id);
  const competitors = useCompetitors(market?.id);
  const segments = useSegments(market?.id);

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<ProductDto | null>(null);
  const [newDefaults, setNewDefaults] = React.useState<{
    competitorId: string | null;
    parentProductId: string | null;
  }>({ competitorId: null, parentProductId: null });
  const [pricingFor, setPricingFor] = React.useState<ProductDto | null>(null);
  const [integrationsFor, setIntegrationsFor] =
    React.useState<ProductDto | null>(null);
  const [deleting, setDeleting] = React.useState<ProductDto | null>(null);

  const deleteProduct = useInvalidatingMutation(
    (id: string) => api.products.delete(id),
    () => [queryKeys.products(market?.id ?? "")],
    { successMessage: "Product deleted", onSuccess: () => setDeleting(null) },
  );

  const all = products.data ?? [];
  const ours = orderWithModules(all.filter((p) => p.isOurs));
  const byCompetitor = (competitors.data ?? []).map((c) => ({
    competitor: c,
    rows: orderWithModules(all.filter((p) => p.competitorId === c.id)),
  }));

  const openNew = (defaults: {
    competitorId: string | null;
    parentProductId: string | null;
  }) => {
    setEditing(null);
    setNewDefaults(defaults);
    setEditorOpen(true);
  };

  const renderRows = (rows: { product: ProductDto; isChild: boolean }[]) => (
    <ul className="divide-y">
      {rows.map(({ product, isChild }) => (
        <li
          key={product.id}
          className="group flex items-center gap-3 px-4 py-2.5"
          style={{ paddingLeft: isChild ? "2.5rem" : "1rem" }}
        >
          {isChild && (
            <CornerDownRightIcon className="size-3.5 shrink-0 text-muted-foreground/50" />
          )}
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <span className="truncate text-sm font-medium">
                {product.name}
              </span>
              {product.isModule && (
                <Badge variant="outline" className="text-[11px]">
                  Module
                </Badge>
              )}
              {product.prices.length > 0 && (
                <Badge variant="secondary" className="text-[11px]">
                  {product.prices.length} price
                  {product.prices.length === 1 ? "" : "s"}
                </Badge>
              )}
            </div>
            {product.description && (
              <p className="truncate text-xs text-muted-foreground">
                {product.description}
              </p>
            )}
          </div>
          <div className="inline-flex gap-1 opacity-0 transition-opacity group-hover:opacity-100 group-focus-within:opacity-100">
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={`Pricing for ${product.name}`}
              onClick={() => setPricingFor(product)}
            >
              <DollarSignIcon />
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={`Integrations for ${product.name}`}
              onClick={() => setIntegrationsFor(product)}
            >
              <Link2Icon />
            </Button>
            {!product.isModule && (
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={`Add module under ${product.name}`}
                onClick={() =>
                  openNew({
                    competitorId: product.competitorId,
                    parentProductId: product.id,
                  })
                }
              >
                <PlusIcon />
              </Button>
            )}
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={`Edit ${product.name}`}
              onClick={() => {
                setEditing(product);
                setEditorOpen(true);
              }}
            >
              <PencilIcon />
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={`Delete ${product.name}`}
              onClick={() => setDeleting(product)}
            >
              <Trash2Icon className="text-destructive" />
            </Button>
          </div>
        </li>
      ))}
    </ul>
  );

  return (
    <>
      <PageHeader
        title="Products"
        description="Sellable offerings, ours and theirs — a module is a product with a parent"
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
          onClick={() => openNew({ competitorId: null, parentProductId: null })}
        >
          <PlusIcon data-icon="inline-start" />
          New product
        </Button>
      </PageHeader>

      <div className="flex-1 space-y-6 overflow-auto p-6">
        {!market ? (
          <EmptyState
            title="No market selected"
            hint="Create a market first — products compete inside a market."
          />
        ) : (
          <>
            <section>
              <div className="mb-2 flex items-center gap-2">
                <h2 className="font-heading text-sm font-semibold tracking-tight">
                  Our products
                </h2>
                <Badge className="text-[11px]">Ours</Badge>
              </div>
              <div className="overflow-hidden rounded-xl border-2 border-primary/25 bg-card">
                {ours.length === 0 ? (
                  <p className="px-4 py-6 text-center text-sm text-muted-foreground">
                    No products of ours yet — create one without a competitor.
                  </p>
                ) : (
                  renderRows(ours)
                )}
              </div>
            </section>

            <section>
              <h2 className="mb-2 font-heading text-sm font-semibold tracking-tight">
                Competitor products
              </h2>
              {byCompetitor.length === 0 ? (
                <EmptyState
                  title="No competitors yet"
                  hint="Add competitors first, then attach their products for the coverage grid."
                />
              ) : (
                <div className="space-y-4">
                  {byCompetitor.map(({ competitor, rows }) => (
                    <div
                      key={competitor.id}
                      className="overflow-hidden rounded-xl border bg-card"
                    >
                      <div className="flex items-center justify-between border-b bg-muted/40 px-4 py-2">
                        <span className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                          {competitor.name}
                        </span>
                        <Button
                          variant="ghost"
                          size="sm"
                          className="text-muted-foreground"
                          onClick={() =>
                            openNew({
                              competitorId: competitor.id,
                              parentProductId: null,
                            })
                          }
                        >
                          <PlusIcon data-icon="inline-start" />
                          Add product
                        </Button>
                      </div>
                      {rows.length === 0 ? (
                        <p className="px-4 py-4 text-center text-sm text-muted-foreground">
                          No products recorded.
                        </p>
                      ) : (
                        renderRows(rows)
                      )}
                    </div>
                  ))}
                </div>
              )}
            </section>
          </>
        )}
      </div>

      {market && (
        <ProductEditorDialog
          key={editing?.id ?? `new-${newDefaults.competitorId}-${newDefaults.parentProductId}`}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          marketId={market.id}
          product={editing}
          defaults={newDefaults}
          competitors={competitors.data ?? []}
          allProducts={all}
        />
      )}

      {pricingFor && market && (
        <PricingDialog
          product={all.find((p) => p.id === pricingFor.id) ?? pricingFor}
          marketId={market.id}
          segments={segments.data ?? []}
          onOpenChange={(open) => !open && setPricingFor(null)}
        />
      )}

      {integrationsFor && market && (
        <IntegrationsDialog
          product={integrationsFor}
          allProducts={all}
          competitors={competitors.data ?? []}
          onOpenChange={(open) => !open && setIntegrationsFor(null)}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "product"}?`}
        description="A product that still has modules can't be deleted. Coverage assessments pointing at it go with it."
        onConfirm={() => deleting && deleteProduct.mutate(deleting.id)}
        busy={deleteProduct.isPending}
      />
    </>
  );
}

function ProductEditorDialog({
  open,
  onOpenChange,
  marketId,
  product,
  defaults,
  competitors,
  allProducts,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  marketId: string;
  product: ProductDto | null;
  defaults: { competitorId: string | null; parentProductId: string | null };
  competitors: CompetitorDto[];
  allProducts: ProductDto[];
}) {
  const [name, setName] = React.useState(product?.name ?? "");
  const [description, setDescription] = React.useState(
    product?.description ?? "",
  );
  const [competitorId, setCompetitorId] = React.useState<string>(
    product?.competitorId ?? defaults.competitorId ?? "ours",
  );
  const [parentId, setParentId] = React.useState<string>(
    product?.parentProductId ?? defaults.parentProductId ?? "none",
  );

  // A module's parent must belong to the same owner and can't be the product itself.
  const ownerCompetitorId = competitorId === "ours" ? null : competitorId;
  const parentOptions = allProducts.filter(
    (p) =>
      p.id !== product?.id &&
      p.parentProductId === null &&
      p.competitorId === ownerCompetitorId,
  );

  const save = useInvalidatingMutation(
    () => {
      const parentProductId = parentId === "none" ? null : parentId;
      return product
        ? api.products.update(product.id, {
            name,
            description: description || null,
            parentProductId,
          })
        : api.products.create({
            marketId,
            name,
            description: description || null,
            competitorId: ownerCompetitorId,
            parentProductId,
          });
    },
    () => [queryKeys.products(marketId)],
    {
      successMessage: product ? "Product saved" : "Product created",
      onSuccess: () => onOpenChange(false),
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{product ? "Edit product" : "New product"}</DialogTitle>
          <DialogDescription>
            Give it a parent product to make it a module — priced and
            cross-sold individually, same machinery.
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
            <Label htmlFor="prod-name">Name</Label>
            <Input
              id="prod-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          {!product && (
            <div className="flex flex-col gap-2">
              <Label>Owner</Label>
              <Select
                items={[
                  { label: "Ours", value: "ours" },
                  ...competitors.map((c) => ({ label: c.name, value: c.id })),
                ]}
                value={competitorId}
                onValueChange={(v) => {
                  if (!v) return;
                  setCompetitorId(v as string);
                  setParentId("none");
                }}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="ours">Ours</SelectItem>
                  {competitors.map((c) => (
                    <SelectItem key={c.id} value={c.id}>
                      {c.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
          <div className="flex flex-col gap-2">
            <Label>Parent product</Label>
            <Select
              items={[
                { label: "None (standalone product)", value: "none" },
                ...parentOptions.map((p) => ({ label: p.name, value: p.id })),
              ]}
              value={parentId}
              onValueChange={(v) => v && setParentId(v as string)}
            >
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">None (standalone product)</SelectItem>
                {parentOptions.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="prod-desc">Description</Label>
            <Textarea
              id="prod-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending
                ? "Saving…"
                : product
                  ? "Save changes"
                  : "Create product"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function PricingDialog({
  product,
  marketId,
  segments,
  onOpenChange,
}: {
  product: ProductDto;
  marketId: string;
  segments: ClientSegmentDto[];
  onOpenChange: (open: boolean) => void;
}) {
  const [segmentId, setSegmentId] = React.useState<string>("all");
  const [recurring, setRecurring] = React.useState("");
  const [implementation, setImplementation] = React.useState("");
  const [currency, setCurrency] = React.useState(
    product.prices[0]?.recurringPricePerPeriod.currency ?? "USD",
  );

  const segmentName = (id: string | null) =>
    id === null
      ? "All segments"
      : (segments.find((s) => s.id === id)?.name ?? "Unknown segment");

  const invalidate = () => [queryKeys.products(marketId)];

  const setPrice = useInvalidatingMutation(
    () =>
      api.products.setPrice(product.id, {
        clientSegmentId: segmentId === "all" ? null : segmentId,
        recurringPricePerPeriod: {
          amount: Number(recurring) || 0,
          currency,
        },
        implementationFee: {
          amount: Number(implementation) || 0,
          currency,
        },
      }),
    invalidate,
    {
      successMessage: "Price saved",
      onSuccess: () => {
        setRecurring("");
        setImplementation("");
      },
    },
  );

  const removePrice = useInvalidatingMutation(
    (clientSegmentId: string | null) =>
      api.products.removePrice(product.id, clientSegmentId ?? undefined),
    invalidate,
    { successMessage: "Price removed" },
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{product.name} — pricing</DialogTitle>
          <DialogDescription>
            Recurring price per month plus a one-time implementation fee,
            optionally differentiated per segment.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          {product.prices.length === 0 ? (
            <p className="rounded-lg border border-dashed px-3 py-4 text-center text-sm text-muted-foreground">
              No prices set yet.
            </p>
          ) : (
            product.prices.map((price) => (
              <div
                key={price.clientSegmentId ?? "all"}
                className="flex items-center gap-3 rounded-lg border px-3 py-2"
              >
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-medium">
                    {segmentName(price.clientSegmentId)}
                  </p>
                  <p className="font-mono text-xs text-muted-foreground">
                    {price.recurringPricePerPeriod.amount.toLocaleString()}{" "}
                    {price.recurringPricePerPeriod.currency}/mo ·{" "}
                    {price.implementationFee.amount.toLocaleString()}{" "}
                    {price.implementationFee.currency} impl
                  </p>
                </div>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Remove price for ${segmentName(price.clientSegmentId)}`}
                  onClick={() => removePrice.mutate(price.clientSegmentId)}
                >
                  <Trash2Icon className="text-destructive" />
                </Button>
              </div>
            ))
          )}
        </div>

        <form
          className="flex flex-col gap-3 rounded-lg border bg-muted/30 p-3"
          onSubmit={(e) => {
            e.preventDefault();
            setPrice.mutate(undefined);
          }}
        >
          <Label className="text-xs text-muted-foreground">
            Set / replace a price
          </Label>
          <Select
            items={[
              { label: "All segments", value: "all" },
              ...segments.map((s) => ({ label: s.name, value: s.id })),
            ]}
            value={segmentId}
            onValueChange={(v) => v && setSegmentId(v as string)}
          >
            <SelectTrigger className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All segments</SelectItem>
              {segments.map((s) => (
                <SelectItem key={s.id} value={s.id}>
                  {s.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <div className="grid grid-cols-3 gap-2">
            <div className="col-span-1 flex flex-col gap-1">
              <Label htmlFor="price-recurring" className="text-xs">
                Recurring / mo
              </Label>
              <Input
                id="price-recurring"
                inputMode="decimal"
                value={recurring}
                onChange={(e) => setRecurring(e.target.value)}
                placeholder="0"
                required
              />
            </div>
            <div className="col-span-1 flex flex-col gap-1">
              <Label htmlFor="price-impl" className="text-xs">
                Implementation
              </Label>
              <Input
                id="price-impl"
                inputMode="decimal"
                value={implementation}
                onChange={(e) => setImplementation(e.target.value)}
                placeholder="0"
              />
            </div>
            <div className="col-span-1 flex flex-col gap-1">
              <Label htmlFor="price-currency" className="text-xs">
                Currency
              </Label>
              <Input
                id="price-currency"
                value={currency}
                onChange={(e) => setCurrency(e.target.value.toUpperCase())}
                maxLength={3}
              />
            </div>
          </div>
          <Button
            type="submit"
            disabled={setPrice.isPending || recurring.trim() === ""}
          >
            {setPrice.isPending ? "Saving…" : "Save price"}
          </Button>
        </form>

        <DialogFooter>
          <DialogClose render={<Button variant="outline" />}>Done</DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ---------------------------------------------------------------------
// Integrations — Product↔Product quality edges (competitors included)
// ---------------------------------------------------------------------

function IntegrationsDialog({
  product,
  allProducts,
  competitors,
  onOpenChange,
}: {
  product: ProductDto;
  allProducts: ProductDto[];
  competitors: CompetitorDto[];
  onOpenChange: (open: boolean) => void;
}) {
  const integrations = useIntegrations(product.id);
  const [addingId, setAddingId] = React.useState<string>("");

  const invalidate = () => [
    queryKeys.integrations(product.id),
    queryKeys.integrations("all"),
  ];

  const create = useInvalidatingMutation(
    (otherProductId: string) =>
      api.integrations.create({
        productAId: product.id,
        productBId: otherProductId,
        qualityScore: 0.5,
      }),
    invalidate,
    { successMessage: "Integration added", onSuccess: () => setAddingId("") },
  );

  const update = useInvalidatingMutation(
    (vars: { id: string; qualityScore: number; notes: string | null }) =>
      api.integrations.update(vars.id, {
        qualityScore: vars.qualityScore,
        notes: vars.notes,
      }),
    invalidate,
    { successMessage: "Integration saved" },
  );

  const remove = useInvalidatingMutation(
    (id: string) => api.integrations.delete(id),
    invalidate,
    { successMessage: "Integration removed" },
  );

  const ownerLabel = (p: ProductDto) =>
    p.isOurs
      ? "ours"
      : (competitors.find((c) => c.id === p.competitorId)?.name ?? "competitor");

  const productById = new Map(allProducts.map((p) => [p.id, p]));
  const otherId = (edge: IntegrationDto) =>
    edge.productAId === product.id ? edge.productBId : edge.productAId;

  const edges = integrations.data ?? [];
  const linked = new Set(edges.map(otherId));
  const addable = allProducts.filter(
    (p) => p.id !== product.id && !linked.has(p.id),
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{product.name} — integrations</DialogTitle>
          <DialogDescription>
            Symmetric quality edges (0–1). In the firm-level engine they raise
            a best-of-breed candidate&apos;s utility against a firm&apos;s
            existing stack — competitor products included.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          {integrations.isPending ? (
            <p className="py-3 text-center text-sm text-muted-foreground">
              Loading…
            </p>
          ) : edges.length === 0 ? (
            <p className="rounded-lg border border-dashed px-3 py-4 text-center text-sm text-muted-foreground">
              No integrations recorded for this product.
            </p>
          ) : (
            <ul className="flex flex-col gap-2">
              {edges.map((edge) => {
                const other = productById.get(otherId(edge));
                return (
                  <IntegrationRow
                    key={edge.id}
                    edge={edge}
                    name={other?.name ?? otherId(edge).slice(0, 8)}
                    owner={other ? ownerLabel(other) : ""}
                    busy={update.isPending}
                    onSave={(qualityScore) =>
                      update.mutate({
                        id: edge.id,
                        qualityScore,
                        notes: edge.notes,
                      })
                    }
                    onRemove={() => remove.mutate(edge.id)}
                  />
                );
              })}
            </ul>
          )}
        </div>

        {addable.length > 0 && (
          <div className="flex items-center gap-2">
            <Select
              items={addable.map((p) => ({
                label: `${p.name} (${ownerLabel(p)})`,
                value: p.id,
              }))}
              value={addingId || null}
              onValueChange={(v) => v && setAddingId(v as string)}
            >
              <SelectTrigger size="sm" className="min-w-0 flex-1">
                <SelectValue placeholder="Link another product…" />
              </SelectTrigger>
              <SelectContent>
                {addable.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name} ({ownerLabel(p)})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Button
              variant="outline"
              size="sm"
              disabled={!addingId || create.isPending}
              onClick={() => create.mutate(addingId)}
            >
              <PlusIcon data-icon="inline-start" />
              Add
            </Button>
          </div>
        )}

        <DialogFooter>
          <DialogClose render={<Button variant="outline" />}>Done</DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function IntegrationRow({
  edge,
  name,
  owner,
  busy,
  onSave,
  onRemove,
}: {
  edge: IntegrationDto;
  name: string;
  owner: string;
  busy: boolean;
  onSave: (qualityScore: number) => void;
  onRemove: () => void;
}) {
  const [value, setValue] = React.useState(edge.qualityScore);
  const dirty = value !== edge.qualityScore;

  return (
    <li className="flex items-center gap-3 rounded-lg border px-3 py-2">
      <div className="w-36 min-w-0">
        <p className="truncate text-sm font-medium">{name}</p>
        {owner && (
          <p className="text-[10px] text-muted-foreground">{owner}</p>
        )}
      </div>
      <input
        type="range"
        min={0}
        max={1}
        step={0.05}
        value={value}
        onChange={(e) => setValue(Number(e.target.value))}
        aria-label={`Integration quality with ${name}`}
        className="h-1.5 flex-1 cursor-pointer accent-primary"
      />
      <span
        className={cn(
          "w-10 text-right font-mono text-xs",
          value >= 0.7 && "text-primary",
        )}
      >
        {value.toFixed(2)}
      </span>
      <div className="flex w-16 justify-end gap-1">
        {dirty && (
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Save integration quality with ${name}`}
            disabled={busy}
            onClick={() => onSave(value)}
          >
            <CheckIcon className="text-primary" />
          </Button>
        )}
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label={`Remove integration with ${name}`}
          onClick={onRemove}
        >
          <Trash2Icon className="text-destructive" />
        </Button>
      </div>
    </li>
  );
}
