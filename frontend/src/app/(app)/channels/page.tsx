"use client";

import * as React from "react";
import {
  CheckIcon,
  PencilIcon,
  PlusIcon,
  Trash2Icon,
  XIcon,
} from "lucide-react";
import { api } from "@/lib/api/client";
import type {
  ChannelDto,
  ClientSegmentDto,
  ProductDto,
} from "@/lib/api/types";
import {
  queryKeys,
  useActiveMarket,
  useChannels,
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

export default function ChannelsPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const { markets, market } = useActiveMarket(workspaceId);
  const channels = useChannels(market?.id);
  const segments = useSegments(market?.id);
  const products = useProducts(market?.id);

  const [chosenId, setChosenId] = React.useState<string | null>(null);
  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<ChannelDto | null>(null);
  const [deleting, setDeleting] = React.useState<ChannelDto | null>(null);

  const list = channels.data ?? [];
  const selected = list.find((c) => c.id === chosenId) ?? list[0] ?? null;

  const deleteChannel = useInvalidatingMutation(
    (id: string) => api.channels.delete(id),
    () => [queryKeys.channels(market?.id ?? "")],
    { successMessage: "Channel deleted", onSuccess: () => setDeleting(null) },
  );

  return (
    <>
      <PageHeader
        title="Channels"
        description="Routes to market — reach into segments, opportunity origination, and the biases each channel carries"
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
          New channel
        </Button>
      </PageHeader>

      <div className="flex flex-1 overflow-hidden">
        {!market ? (
          <div className="flex-1 p-6">
            <EmptyState
              title="No market selected"
              hint="Create a market first — channels reach into its segments."
            />
          </div>
        ) : list.length === 0 ? (
          <div className="flex-1 p-6">
            <EmptyState
              title="No channels yet"
              hint="Direct sales is just another channel — add it, then the consultants and gatekeepers with their reach and biases."
            />
          </div>
        ) : (
          <>
            <aside className="w-64 shrink-0 overflow-y-auto border-r p-3">
              <ul className="flex flex-col gap-1">
                {list.map((c) => (
                  <li key={c.id}>
                    <button
                      type="button"
                      onClick={() => setChosenId(c.id)}
                      className={cn(
                        "flex w-full flex-col gap-0.5 rounded-lg px-3 py-2 text-left text-sm transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring/50",
                        selected?.id === c.id
                          ? "bg-accent font-medium text-accent-foreground"
                          : "text-muted-foreground hover:bg-accent/60",
                      )}
                    >
                      <span className="truncate">{c.name}</span>
                      <span className="font-mono text-[11px] text-muted-foreground/80">
                        {c.segmentReaches.length} segment
                        {c.segmentReaches.length === 1 ? "" : "s"} ·{" "}
                        {c.productAffinities.length} bias
                        {c.productAffinities.length === 1 ? "" : "es"}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </aside>

            <div className="flex-1 overflow-y-auto p-6">
              {selected && (
                <ChannelDetail
                  key={selected.id}
                  channel={selected}
                  marketId={market.id}
                  segments={segments.data ?? []}
                  products={products.data ?? []}
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
        <ChannelEditorDialog
          key={editing?.id ?? "new"}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          marketId={market.id}
          channel={editing}
          onCreated={(id) => setChosenId(id)}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "channel"}?`}
        description="Opportunities that came through this channel keep their history; future firm-level runs simply stop originating through it."
        onConfirm={() => deleting && deleteChannel.mutate(deleting.id)}
        busy={deleteChannel.isPending}
      />
    </>
  );
}

// ---------------------------------------------------------------------
// Detail
// ---------------------------------------------------------------------

function ChannelDetail({
  channel,
  marketId,
  segments,
  products,
  onEdit,
  onDelete,
}: {
  channel: ChannelDto;
  marketId: string;
  segments: ClientSegmentDto[];
  products: ProductDto[];
  onEdit: () => void;
  onDelete: () => void;
}) {
  return (
    <div className="flex max-w-3xl flex-col gap-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="font-heading text-lg font-semibold tracking-tight">
              {channel.name}
            </h2>
            {channel.originatesOpportunities && (
              <Badge variant="secondary">Originates opportunities</Badge>
            )}
            {channel.carriesOnlyListedProducts && (
              <Badge variant="outline">Carries listed products only</Badge>
            )}
          </div>
          {channel.description && (
            <p className="mt-0.5 text-sm text-muted-foreground">
              {channel.description}
            </p>
          )}
        </div>
        <div className="flex gap-1">
          <Button variant="ghost" size="icon-sm" aria-label="Edit channel" onClick={onEdit}>
            <PencilIcon />
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="Delete channel"
            onClick={onDelete}
          >
            <Trash2Icon className="text-destructive" />
          </Button>
        </div>
      </div>

      <section className="rounded-xl border bg-card">
        <div className="border-b px-4 py-2.5">
          <h3 className="text-sm font-semibold">Segment reach & origination</h3>
          <p className="text-xs text-muted-foreground">
            The engine draws an opportunity&apos;s channel with weight reach ×
            origination share — a consultant may see a whole segment yet
            originate nothing.
          </p>
        </div>
        <div className="divide-y">
          {segments.length === 0 ? (
            <p className="px-4 py-4 text-sm text-muted-foreground">
              No segments in this market yet.
            </p>
          ) : (
            segments.map((s) => (
              <SegmentReachRow
                key={s.id}
                channel={channel}
                marketId={marketId}
                segment={s}
              />
            ))
          )}
        </div>
      </section>

      <section className="rounded-xl border bg-card">
        <div className="border-b px-4 py-2.5">
          <h3 className="text-sm font-semibold">Product bias</h3>
          <p className="text-xs text-muted-foreground">
            Signed utility nudge the channel applies to a product it presents
            (consultants tilted toward an incumbent, for example). 0 is
            neutral.
            {channel.carriesOnlyListedProducts &&
              " This list is also the whitelist of products the channel carries."}
          </p>
        </div>
        <ProductBiasEditor
          channel={channel}
          marketId={marketId}
          products={products}
        />
      </section>
    </div>
  );
}

function SegmentReachRow({
  channel,
  marketId,
  segment,
}: {
  channel: ChannelDto;
  marketId: string;
  segment: ClientSegmentDto;
}) {
  const existing = channel.segmentReaches.find(
    (r) => r.clientSegmentId === segment.id,
  );
  const [reach, setReach] = React.useState(
    existing ? String(existing.reach) : "",
  );
  const [origination, setOrigination] = React.useState(
    existing ? String(existing.originationShare) : "",
  );

  const dirty =
    reach !== (existing ? String(existing.reach) : "") ||
    origination !== (existing ? String(existing.originationShare) : "");

  const invalidate = () => [queryKeys.channels(marketId)];

  const save = useInvalidatingMutation(
    () =>
      api.channels.setSegmentReach(channel.id, {
        clientSegmentId: segment.id,
        reach: Number(reach) || 0,
        originationShare: Number(origination) || 0,
      }),
    invalidate,
    { successMessage: "Reach saved" },
  );

  const remove = useInvalidatingMutation(
    () => api.channels.removeSegmentReach(channel.id, segment.id),
    invalidate,
    {
      successMessage: "Reach cleared",
      onSuccess: () => {
        setReach("");
        setOrigination("");
      },
    },
  );

  return (
    <div className="flex items-center gap-3 px-4 py-2">
      <span className="min-w-0 flex-1 truncate text-sm font-medium">
        {segment.name}
      </span>
      <div className="flex items-center gap-1.5">
        <Label
          htmlFor={`reach-${channel.id}-${segment.id}`}
          className="text-[11px] text-muted-foreground"
        >
          Reach
        </Label>
        <Input
          id={`reach-${channel.id}-${segment.id}`}
          inputMode="decimal"
          placeholder="0–1"
          className="h-8 w-20 font-mono text-xs"
          value={reach}
          onChange={(e) => setReach(e.target.value)}
        />
      </div>
      <div className="flex items-center gap-1.5">
        <Label
          htmlFor={`orig-${channel.id}-${segment.id}`}
          className="text-[11px] text-muted-foreground"
        >
          Origination
        </Label>
        <Input
          id={`orig-${channel.id}-${segment.id}`}
          inputMode="decimal"
          placeholder="0–1"
          className="h-8 w-20 font-mono text-xs"
          value={origination}
          onChange={(e) => setOrigination(e.target.value)}
        />
      </div>
      <div className="flex w-16 justify-end gap-1">
        {dirty && reach.trim() !== "" && (
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Save reach for ${segment.name}`}
            disabled={save.isPending}
            onClick={() => save.mutate(undefined)}
          >
            <CheckIcon className="text-primary" />
          </Button>
        )}
        {existing && (
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Clear reach for ${segment.name}`}
            disabled={remove.isPending}
            onClick={() => remove.mutate(undefined)}
          >
            <XIcon className="text-muted-foreground" />
          </Button>
        )}
      </div>
    </div>
  );
}

function ProductBiasEditor({
  channel,
  marketId,
  products,
}: {
  channel: ChannelDto;
  marketId: string;
  products: ProductDto[];
}) {
  const [addingId, setAddingId] = React.useState<string>("");

  const invalidate = () => [queryKeys.channels(marketId)];

  const setAffinity = useInvalidatingMutation(
    (vars: { productId: string; bias: number }) =>
      api.channels.setProductAffinity(channel.id, vars),
    invalidate,
    { successMessage: "Bias saved" },
  );

  const removeAffinity = useInvalidatingMutation(
    (productId: string) =>
      api.channels.removeProductAffinity(channel.id, productId),
    invalidate,
    { successMessage: "Bias removed" },
  );

  const productName = (id: string) =>
    products.find((p) => p.id === id)?.name ?? id.slice(0, 8);
  const listed = new Set(channel.productAffinities.map((a) => a.productId));
  const addable = products.filter((p) => !listed.has(p.id));

  return (
    <div className="flex flex-col gap-2 p-4">
      {channel.productAffinities.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No product biases — the channel presents every product neutrally.
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {channel.productAffinities.map((a) => (
            <BiasRow
              key={a.productId}
              name={productName(a.productId)}
              bias={a.bias}
              busy={setAffinity.isPending}
              onSave={(bias) =>
                setAffinity.mutate({ productId: a.productId, bias })
              }
              onRemove={() => removeAffinity.mutate(a.productId)}
            />
          ))}
        </ul>
      )}
      {addable.length > 0 && (
        <div className="flex items-center gap-2 pt-1">
          <Select
            items={addable.map((p) => ({ label: p.name, value: p.id }))}
            value={addingId || null}
            onValueChange={(v) => v && setAddingId(v as string)}
          >
            <SelectTrigger size="sm" className="min-w-48">
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
          <Button
            variant="outline"
            size="sm"
            disabled={!addingId || setAffinity.isPending}
            onClick={() => {
              setAffinity.mutate({ productId: addingId, bias: 0 });
              setAddingId("");
            }}
          >
            <PlusIcon data-icon="inline-start" />
            Add
          </Button>
        </div>
      )}
    </div>
  );
}

function BiasRow({
  name,
  bias,
  busy,
  onSave,
  onRemove,
}: {
  name: string;
  bias: number;
  busy: boolean;
  onSave: (bias: number) => void;
  onRemove: () => void;
}) {
  const [value, setValue] = React.useState(bias);
  const dirty = value !== bias;

  return (
    <li className="flex items-center gap-3 rounded-lg border px-3 py-2">
      <span className="min-w-0 w-40 truncate text-sm font-medium">{name}</span>
      <input
        type="range"
        min={-2}
        max={2}
        step={0.05}
        value={value}
        onChange={(e) => setValue(Number(e.target.value))}
        aria-label={`Bias for ${name}`}
        className="h-1.5 flex-1 cursor-pointer accent-primary"
      />
      <span
        className={cn(
          "w-14 text-right font-mono text-xs",
          value > 0 && "text-primary",
          value < 0 && "text-destructive",
        )}
      >
        {value > 0 ? "+" : ""}
        {value.toFixed(2)}
      </span>
      <div className="flex w-16 justify-end gap-1">
        {dirty && (
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Save bias for ${name}`}
            disabled={busy}
            onClick={() => onSave(value)}
          >
            <CheckIcon className="text-primary" />
          </Button>
        )}
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label={`Remove bias for ${name}`}
          onClick={onRemove}
        >
          <Trash2Icon className="text-destructive" />
        </Button>
      </div>
    </li>
  );
}

// ---------------------------------------------------------------------
// Editor dialog
// ---------------------------------------------------------------------

function ChannelEditorDialog({
  open,
  onOpenChange,
  marketId,
  channel,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  marketId: string;
  channel: ChannelDto | null;
  onCreated: (id: string) => void;
}) {
  const [name, setName] = React.useState(channel?.name ?? "");
  const [description, setDescription] = React.useState(
    channel?.description ?? "",
  );
  const [originates, setOriginates] = React.useState(
    channel?.originatesOpportunities ?? true,
  );
  const [whitelist, setWhitelist] = React.useState(
    channel?.carriesOnlyListedProducts ?? false,
  );

  const save = useInvalidatingMutation(
    () =>
      channel
        ? api.channels.update(channel.id, {
            name,
            description: description || null,
            originatesOpportunities: originates,
            carriesOnlyListedProducts: whitelist,
          })
        : api.channels.create({
            marketId,
            name,
            description: description || null,
            originatesOpportunities: originates,
            carriesOnlyListedProducts: whitelist,
          }),
    () => [queryKeys.channels(marketId)],
    {
      successMessage: channel ? "Channel saved" : "Channel created",
      onSuccess: (saved) => {
        onOpenChange(false);
        if (!channel) onCreated(saved.id);
      },
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{channel ? "Edit channel" : "New channel"}</DialogTitle>
          <DialogDescription>
            Reach, origination and per-product bias are edited on the channel
            itself once it exists.
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
            <Label htmlFor="chan-name">Name</Label>
            <Input
              id="chan-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
              placeholder="e.g. Direct sales, Prime broker referrals"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="chan-desc">Description</Label>
            <Textarea
              id="chan-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
            />
          </div>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={originates}
              onCheckedChange={(v) => setOriginates(v === true)}
            />
            Originates opportunities (weight = reach × origination share)
          </label>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={whitelist}
              onCheckedChange={(v) => setWhitelist(v === true)}
            />
            Carries only listed products — others drop out of consideration
          </label>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending
                ? "Saving…"
                : channel
                  ? "Save changes"
                  : "Create channel"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
