"use client";

import * as React from "react";
import { CheckIcon, PencilIcon, PlusIcon, Trash2Icon } from "lucide-react";
import { api } from "@/lib/api/client";
import type { MarketDto } from "@/lib/api/types";
import {
  queryKeys,
  setActiveMarket,
  useActiveMarket,
  useInvalidatingMutation,
  useWorkspace,
} from "@/lib/queries";
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

export default function MarketsPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const { markets, market: activeMarket } = useActiveMarket(workspaceId);

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<MarketDto | null>(null);
  const [deleting, setDeleting] = React.useState<MarketDto | null>(null);

  const deleteMarket = useInvalidatingMutation(
    (id: string) => api.markets.delete(id),
    () => [queryKeys.markets(workspaceId ?? "")],
    { successMessage: "Market deleted", onSuccess: () => setDeleting(null) },
  );

  return (
    <>
      <PageHeader
        title="Markets"
        description="Competitive arenas — each holds its own segments, products, and process catalog"
      >
        <Button
          onClick={() => {
            setEditing(null);
            setEditorOpen(true);
          }}
        >
          <PlusIcon data-icon="inline-start" />
          New market
        </Button>
      </PageHeader>

      <div className="flex-1 overflow-auto p-6">
        {markets.data && markets.data.length === 0 ? (
          <EmptyState
            title="No markets yet"
            hint="A market is a competitive arena: define one, then add its client segments, business processes, products, and competitors."
          />
        ) : (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {(markets.data ?? []).map((m) => {
              const isActive = m.id === activeMarket?.id;
              return (
                <div
                  key={m.id}
                  role="button"
                  tabIndex={0}
                  onClick={() => workspaceId && setActiveMarket(workspaceId, m.id)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && workspaceId) {
                      setActiveMarket(workspaceId, m.id);
                    }
                  }}
                  className={cn(
                    "group flex cursor-pointer flex-col gap-2 rounded-xl border bg-card p-4 text-left transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring/50",
                    isActive
                      ? "border-primary/50 ring-1 ring-primary/30"
                      : "hover:border-foreground/20",
                  )}
                >
                  <div className="flex items-start justify-between gap-2">
                    <span className="font-heading text-sm font-semibold tracking-tight">
                      {m.name}
                    </span>
                    {isActive && (
                      <Badge variant="secondary" className="gap-1">
                        <CheckIcon className="size-3" />
                        Active
                      </Badge>
                    )}
                  </div>
                  <p className="min-h-8 text-sm text-muted-foreground">
                    {m.description || "No description yet."}
                  </p>
                  <div className="flex items-center justify-between pt-1">
                    <span className="text-xs text-muted-foreground/70">
                      {isActive
                        ? "Market screens are scoped to this market"
                        : "Click to make active"}
                    </span>
                    <span
                      className="inline-flex gap-1 opacity-0 transition-opacity group-hover:opacity-100 group-focus-within:opacity-100"
                      onClick={(e) => e.stopPropagation()}
                    >
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={`Edit ${m.name}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          setEditing(m);
                          setEditorOpen(true);
                        }}
                      >
                        <PencilIcon />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={`Delete ${m.name}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          setDeleting(m);
                        }}
                      >
                        <Trash2Icon className="text-destructive" />
                      </Button>
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {workspaceId && (
        <MarketEditorDialog
          key={editing?.id ?? "new"}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          workspaceId={workspaceId}
          market={editing}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "market"}?`}
        description="Everything scoped to it — segments, processes, products, competitors, events — goes with it."
        onConfirm={() => deleting && deleteMarket.mutate(deleting.id)}
        busy={deleteMarket.isPending}
      />
    </>
  );
}

function MarketEditorDialog({
  open,
  onOpenChange,
  workspaceId,
  market,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workspaceId: string;
  market: MarketDto | null;
}) {
  const [name, setName] = React.useState(market?.name ?? "");
  const [description, setDescription] = React.useState(
    market?.description ?? "",
  );

  const save = useInvalidatingMutation(
    () =>
      market
        ? api.markets.update(market.id, {
            name,
            description: description || null,
          })
        : api.markets.create({
            workspaceId,
            name,
            description: description || null,
          }),
    () => [queryKeys.markets(workspaceId)],
    {
      successMessage: market ? "Market saved" : "Market created",
      onSuccess: (created) => {
        if (!market) setActiveMarket(workspaceId, created.id);
        onOpenChange(false);
      },
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{market ? "Edit market" : "New market"}</DialogTitle>
          <DialogDescription>
            Industry specifics enter as data — segments, processes, approaches —
            not as schema.
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
            <Label htmlFor="market-name">Name</Label>
            <Input
              id="market-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="market-desc">Description</Label>
            <Textarea
              id="market-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              placeholder="What arena is being fought over?"
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending
                ? "Saving…"
                : market
                  ? "Save changes"
                  : "Create market"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
