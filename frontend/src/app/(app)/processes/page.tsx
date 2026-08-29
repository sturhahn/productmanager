"use client";

import * as React from "react";
import {
  CornerDownRightIcon,
  PencilIcon,
  PlusIcon,
  RouteIcon,
  Trash2Icon,
} from "lucide-react";
import { api } from "@/lib/api/client";
import type { ApproachDto, BusinessProcessDto } from "@/lib/api/types";
import {
  queryKeys,
  useActiveMarket,
  useApproaches,
  useBusinessProcesses,
  useInvalidatingMutation,
  useWorkspace,
} from "@/lib/queries";
import { cn } from "@/lib/utils";
import { flattenProcessTree } from "@/lib/process-tree";
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

export default function ProcessesPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const { markets, market } = useActiveMarket(workspaceId);
  const processes = useBusinessProcesses(market?.id);
  const approaches = useApproaches();

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<BusinessProcessDto | null>(null);
  const [parentForNew, setParentForNew] = React.useState<string | null>(null);
  const [deleting, setDeleting] = React.useState<BusinessProcessDto | null>(
    null,
  );
  const [approachesFor, setApproachesFor] =
    React.useState<BusinessProcessDto | null>(null);

  const rows = React.useMemo(
    () => flattenProcessTree(processes.data ?? []),
    [processes.data],
  );

  const approachesOf = (processId: string) =>
    (approaches.data ?? []).filter((a) => a.businessProcessId === processId);

  const deleteProcess = useInvalidatingMutation(
    (id: string) => api.businessProcesses.delete(id),
    () => [queryKeys.processes(market?.id ?? ""), queryKeys.approaches],
    { successMessage: "Process deleted", onSuccess: () => setDeleting(null) },
  );

  return (
    <>
      <PageHeader
        title="Business processes"
        description="The shared vocabulary demand and supply both point at — hierarchical, per market"
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
            setParentForNew(null);
            setEditorOpen(true);
          }}
        >
          <PlusIcon data-icon="inline-start" />
          New process
        </Button>
      </PageHeader>

      <div className="flex-1 overflow-auto p-6">
        {!market ? (
          <EmptyState
            title="No market selected"
            hint="Create a market first — its process catalog is what the coverage grid is built on."
          />
        ) : rows.length === 0 ? (
          <EmptyState
            title="No processes yet"
            hint="Catalog what firms in this market must accomplish. Processes become the rows of the coverage grid."
          />
        ) : (
          <div className="overflow-hidden rounded-xl border bg-card">
            <ul className="divide-y">
              {rows.map(({ process, depth }) => {
                const procApproaches = approachesOf(process.id);
                return (
                  <li
                    key={process.id}
                    className="group flex items-center gap-3 px-4 py-2.5"
                    style={{ paddingLeft: `${1 + depth * 1.5}rem` }}
                  >
                    {depth > 0 && (
                      <CornerDownRightIcon className="size-3.5 shrink-0 text-muted-foreground/50" />
                    )}
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <span
                          className={cn(
                            "truncate text-sm",
                            depth === 0 ? "font-semibold" : "font-medium",
                          )}
                        >
                          {process.name}
                        </span>
                        {procApproaches.map((a) => (
                          <Badge
                            key={a.id}
                            variant="outline"
                            className="gap-1 text-[11px] text-muted-foreground"
                          >
                            <RouteIcon className="size-3" />
                            {a.name}
                          </Badge>
                        ))}
                      </div>
                      {process.description && (
                        <p className="truncate text-xs text-muted-foreground">
                          {process.description}
                        </p>
                      )}
                    </div>
                    <div className="inline-flex gap-1 opacity-0 transition-opacity group-hover:opacity-100 group-focus-within:opacity-100">
                      <Button
                        variant="ghost"
                        size="sm"
                        className="text-muted-foreground"
                        onClick={() => setApproachesFor(process)}
                      >
                        <RouteIcon data-icon="inline-start" />
                        Approaches
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={`Add sub-process under ${process.name}`}
                        onClick={() => {
                          setEditing(null);
                          setParentForNew(process.id);
                          setEditorOpen(true);
                        }}
                      >
                        <PlusIcon />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={`Edit ${process.name}`}
                        onClick={() => {
                          setEditing(process);
                          setParentForNew(null);
                          setEditorOpen(true);
                        }}
                      >
                        <PencilIcon />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={`Delete ${process.name}`}
                        onClick={() => setDeleting(process)}
                      >
                        <Trash2Icon className="text-destructive" />
                      </Button>
                    </div>
                  </li>
                );
              })}
            </ul>
          </div>
        )}
      </div>

      {market && (
        <ProcessEditorDialog
          key={`${editing?.id ?? "new"}-${parentForNew ?? "root"}`}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          marketId={market.id}
          process={editing}
          defaultParentId={parentForNew}
          allProcesses={processes.data ?? []}
        />
      )}

      {approachesFor && (
        <ApproachesDialog
          process={approachesFor}
          approaches={approachesOf(approachesFor.id)}
          onOpenChange={(open) => !open && setApproachesFor(null)}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "process"}?`}
        description="A process with sub-processes can't be deleted — remove or reparent them first. Coverage assessments pointing at it go with it."
        onConfirm={() => deleting && deleteProcess.mutate(deleting.id)}
        busy={deleteProcess.isPending}
      />
    </>
  );
}

function ProcessEditorDialog({
  open,
  onOpenChange,
  marketId,
  process,
  defaultParentId,
  allProcesses,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  marketId: string;
  process: BusinessProcessDto | null;
  defaultParentId: string | null;
  allProcesses: BusinessProcessDto[];
}) {
  const [name, setName] = React.useState(process?.name ?? "");
  const [description, setDescription] = React.useState(
    process?.description ?? "",
  );
  const [parentId, setParentId] = React.useState<string>(
    process?.parentProcessId ?? defaultParentId ?? "none",
  );

  const parentOptions = allProcesses.filter((p) => p.id !== process?.id);

  const save = useInvalidatingMutation(
    () => {
      const parentProcessId = parentId === "none" ? null : parentId;
      return process
        ? api.businessProcesses.update(process.id, {
            name,
            description: description || null,
            parentProcessId,
          })
        : api.businessProcesses.create({
            marketId,
            name,
            description: description || null,
            parentProcessId,
          });
    },
    () => [queryKeys.processes(marketId)],
    {
      successMessage: process ? "Process saved" : "Process created",
      onSuccess: () => onOpenChange(false),
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {process ? "Edit process" : "New business process"}
          </DialogTitle>
          <DialogDescription>
            What firms must accomplish — not how. Alternative methods are
            approaches under the process.
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
            <Label htmlFor="proc-name">Name</Label>
            <Input
              id="proc-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label>Parent process</Label>
            <Select
              items={[
                { label: "None (top level)", value: "none" },
                ...parentOptions.map((p) => ({ label: p.name, value: p.id })),
              ]}
              value={parentId}
              onValueChange={(v) => v && setParentId(v as string)}
            >
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">None (top level)</SelectItem>
                {parentOptions.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="proc-desc">Description</Label>
            <Textarea
              id="proc-desc"
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
                : process
                  ? "Save changes"
                  : "Create process"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function ApproachesDialog({
  process,
  approaches,
  onOpenChange,
}: {
  process: BusinessProcessDto;
  approaches: ApproachDto[];
  onOpenChange: (open: boolean) => void;
}) {
  const [newName, setNewName] = React.useState("");
  const [newDescription, setNewDescription] = React.useState("");

  const invalidate = () => [queryKeys.approaches];

  const create = useInvalidatingMutation(
    () =>
      api.approaches.create({
        businessProcessId: process.id,
        name: newName,
        description: newDescription || null,
      }),
    invalidate,
    {
      successMessage: "Approach added",
      onSuccess: () => {
        setNewName("");
        setNewDescription("");
      },
    },
  );

  const remove = useInvalidatingMutation(
    (id: string) => api.approaches.delete(id),
    invalidate,
    { successMessage: "Approach removed" },
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{process.name} — approaches</DialogTitle>
          <DialogDescription>
            Alternative methods for the same process (e.g. via partner, direct,
            on-chain). Most processes have one implicit approach — leave this
            empty unless the method genuinely differs.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          {approaches.length === 0 && (
            <p className="rounded-lg border border-dashed px-3 py-4 text-center text-sm text-muted-foreground">
              No explicit approaches — the process has a single implicit one.
            </p>
          )}
          {approaches.map((a) => (
            <div
              key={a.id}
              className="flex items-center gap-3 rounded-lg border px-3 py-2"
            >
              <RouteIcon className="size-4 shrink-0 text-muted-foreground" />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium">{a.name}</p>
                {a.description && (
                  <p className="truncate text-xs text-muted-foreground">
                    {a.description}
                  </p>
                )}
              </div>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={`Remove ${a.name}`}
                onClick={() => remove.mutate(a.id)}
              >
                <Trash2Icon className="text-destructive" />
              </Button>
            </div>
          ))}
        </div>

        <form
          className="flex flex-col gap-2 rounded-lg border bg-muted/30 p-3"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate(undefined);
          }}
        >
          <Label htmlFor="appr-name" className="text-xs text-muted-foreground">
            Add an approach
          </Label>
          <div className="flex gap-2">
            <Input
              id="appr-name"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="Name"
              maxLength={200}
            />
            <Button type="submit" disabled={create.isPending || !newName.trim()}>
              <PlusIcon data-icon="inline-start" />
              Add
            </Button>
          </div>
          <Input
            value={newDescription}
            onChange={(e) => setNewDescription(e.target.value)}
            placeholder="Description (optional)"
          />
        </form>

        <DialogFooter>
          <DialogClose render={<Button variant="outline" />}>Done</DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
