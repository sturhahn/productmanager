"use client";

import * as React from "react";
import { PencilIcon, PlusIcon, Trash2Icon } from "lucide-react";
import { api } from "@/lib/api/client";
import type { CompetitorDto } from "@/lib/api/types";
import {
  queryKeys,
  useActiveMarket,
  useCompetitors,
  useInvalidatingMutation,
  useProducts,
  useWorkspace,
} from "@/lib/queries";
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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export default function CompetitorsPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const { markets, market } = useActiveMarket(workspaceId);
  const competitors = useCompetitors(market?.id);
  const products = useProducts(market?.id);

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<CompetitorDto | null>(null);
  const [deleting, setDeleting] = React.useState<CompetitorDto | null>(null);

  const deleteCompetitor = useInvalidatingMutation(
    (id: string) => api.competitors.delete(id),
    () => [queryKeys.competitors(market?.id ?? "")],
    { successMessage: "Competitor deleted", onSuccess: () => setDeleting(null) },
  );

  const productCount = (competitorId: string) =>
    (products.data ?? []).filter((p) => p.competitorId === competitorId).length;

  return (
    <>
      <PageHeader
        title="Competitors"
        description="Rival organizations and what they're selling into this market"
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
          New competitor
        </Button>
      </PageHeader>

      <div className="flex-1 overflow-auto p-6">
        {!market ? (
          <EmptyState
            title="No market selected"
            hint="Create a market first — competitors live inside a market."
          />
        ) : competitors.data && competitors.data.length === 0 ? (
          <EmptyState
            title="No competitors yet"
            hint="Add the rivals whose products you'll assess on the coverage grid."
          />
        ) : (
          <div className="overflow-hidden rounded-xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Products</TableHead>
                  <TableHead>Strategy notes</TableHead>
                  <TableHead className="w-24 text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(competitors.data ?? []).map((c) => (
                  <TableRow key={c.id}>
                    <TableCell className="font-medium">{c.name}</TableCell>
                    <TableCell>
                      <Badge variant="secondary">{productCount(c.id)}</Badge>
                    </TableCell>
                    <TableCell className="max-w-md truncate text-sm text-muted-foreground">
                      {c.strategyNotes || "—"}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="inline-flex gap-1">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Edit ${c.name}`}
                          onClick={() => {
                            setEditing(c);
                            setEditorOpen(true);
                          }}
                        >
                          <PencilIcon />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Delete ${c.name}`}
                          onClick={() => setDeleting(c)}
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

      {market && (
        <CompetitorEditorDialog
          key={editing?.id ?? "new"}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          marketId={market.id}
          competitor={editing}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "competitor"}?`}
        description="A competitor that still owns products can't be deleted — reassign or delete its products first."
        onConfirm={() => deleting && deleteCompetitor.mutate(deleting.id)}
        busy={deleteCompetitor.isPending}
      />
    </>
  );
}

function CompetitorEditorDialog({
  open,
  onOpenChange,
  marketId,
  competitor,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  marketId: string;
  competitor: CompetitorDto | null;
}) {
  const [name, setName] = React.useState(competitor?.name ?? "");
  const [notes, setNotes] = React.useState(competitor?.strategyNotes ?? "");

  const save = useInvalidatingMutation(
    () =>
      competitor
        ? api.competitors.update(competitor.id, {
            name,
            strategyNotes: notes || null,
          })
        : api.competitors.create({
            marketId,
            name,
            strategyNotes: notes || null,
          }),
    () => [queryKeys.competitors(marketId)],
    {
      successMessage: competitor ? "Competitor saved" : "Competitor created",
      onSuccess: () => onOpenChange(false),
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {competitor ? "Edit competitor" : "New competitor"}
          </DialogTitle>
          <DialogDescription>
            Their products get the same coverage machinery as ours — just with
            sparser, lower-confidence assessments.
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
            <Label htmlFor="comp-name">Name</Label>
            <Input
              id="comp-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="comp-notes">Strategy notes</Label>
            <Textarea
              id="comp-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={4}
              placeholder="Their playbook, pricing posture, likely moves…"
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending
                ? "Saving…"
                : competitor
                  ? "Save changes"
                  : "Create competitor"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
