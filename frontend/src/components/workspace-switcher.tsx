"use client";

import * as React from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  CheckIcon,
  ChevronsUpDownIcon,
  PencilIcon,
  PlusIcon,
} from "lucide-react";
import { api } from "@/lib/api/client";
import type { WorkspaceDto } from "@/lib/api/types";
import { cn } from "@/lib/utils";
import {
  errorMessage,
  queryKeys,
  setActiveWorkspace,
  useWorkspace,
  useWorkspaces,
} from "@/lib/queries";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

/** Sidebar dropdown for switching, creating, and renaming workspaces. */
export function WorkspaceSwitcher() {
  const workspace = useWorkspace();
  const workspaces = useWorkspaces();
  const activeId = workspace.data?.id;

  const [menuOpen, setMenuOpen] = React.useState(false);
  const [createOpen, setCreateOpen] = React.useState(false);
  const [renameTarget, setRenameTarget] = React.useState<WorkspaceDto | null>(
    null,
  );

  return (
    <>
      <DropdownMenu open={menuOpen} onOpenChange={setMenuOpen}>
        <DropdownMenuTrigger
          render={
            <button
              type="button"
              className="group/ws-switcher -mx-0.5 flex items-center gap-1 rounded-md px-0.5 py-0.5 text-left outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
            />
          }
        >
          <span className="truncate font-mono text-[11px] text-muted-foreground group-hover/ws-switcher:text-foreground group-aria-expanded/ws-switcher:text-foreground">
            {workspace.data?.name ?? " "}
          </span>
          <ChevronsUpDownIcon className="size-3 shrink-0 text-muted-foreground/50 group-hover/ws-switcher:text-muted-foreground" />
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start" className="w-56">
          {(workspaces.data ?? []).map((ws) => (
            <DropdownMenuItem
              key={ws.id}
              closeOnClick
              className="group/ws-item justify-between"
              onClick={() => setActiveWorkspace(ws.id)}
            >
              <span className="flex min-w-0 items-center gap-1.5">
                <CheckIcon
                  className={cn(
                    "size-3.5 shrink-0",
                    ws.id === activeId ? "opacity-100" : "opacity-0",
                  )}
                />
                <span className="truncate">{ws.name}</span>
              </span>
              <Button
                variant="ghost"
                size="icon-xs"
                className="shrink-0 text-muted-foreground opacity-0 group-hover/ws-item:opacity-100 group-focus/ws-item:opacity-100"
                onClick={(event) => {
                  event.stopPropagation();
                  setMenuOpen(false);
                  setRenameTarget(ws);
                }}
              >
                <PencilIcon />
                <span className="sr-only">Rename {ws.name}</span>
              </Button>
            </DropdownMenuItem>
          ))}
          <DropdownMenuSeparator />
          <DropdownMenuItem
            onClick={() => {
              setMenuOpen(false);
              setCreateOpen(true);
            }}
          >
            <PlusIcon className="size-3.5" />
            New workspace
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <CreateWorkspaceDialog open={createOpen} onOpenChange={setCreateOpen} />
      <RenameWorkspaceDialog
        workspace={renameTarget}
        onOpenChange={(open) => {
          if (!open) setRenameTarget(null);
        }}
      />
    </>
  );
}

function CreateWorkspaceDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const [name, setName] = React.useState("");

  const create = useMutation({
    mutationFn: () => api.workspaces.create({ name }),
    onSuccess: (created) => {
      toast.success("Workspace created");
      void queryClient.invalidateQueries({ queryKey: queryKeys.workspaces });
      setActiveWorkspace(created.id);
      onOpenChange(false);
    },
    onError: (error) => toast.error(errorMessage(error)),
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>New workspace</DialogTitle>
          <DialogDescription>
            A workspace holds its own teams, initiatives, projects, and
            roadmaps. You&apos;re switched to it once it&apos;s created.
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <div className="flex flex-col gap-2">
            <Label htmlFor="ws-name">Name</Label>
            <Input
              id="ws-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Acme Platform"
              required
              maxLength={200}
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={create.isPending || !name.trim()}>
              {create.isPending ? "Creating…" : "Create workspace"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function RenameWorkspaceDialog({
  workspace,
  onOpenChange,
}: {
  workspace: WorkspaceDto | null;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Dialog open={workspace !== null} onOpenChange={onOpenChange}>
      <DialogContent>
        {/* Keyed by workspace id so the draft name resets when the rename
            target changes, without a state-syncing effect. */}
        {workspace && (
          <RenameWorkspaceForm
            key={workspace.id}
            workspace={workspace}
            onOpenChange={onOpenChange}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function RenameWorkspaceForm({
  workspace,
  onOpenChange,
}: {
  workspace: WorkspaceDto;
  onOpenChange: (open: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const [name, setName] = React.useState(workspace.name);

  const rename = useMutation({
    mutationFn: () => api.workspaces.update(workspace.id, { name }),
    onSuccess: () => {
      toast.success("Workspace renamed");
      void queryClient.invalidateQueries({ queryKey: queryKeys.workspaces });
      void queryClient.invalidateQueries({
        queryKey: queryKeys.workspace(workspace.id),
      });
      onOpenChange(false);
    },
    onError: (error) => toast.error(errorMessage(error)),
  });

  return (
    <>
      <DialogHeader>
        <DialogTitle>Rename {workspace.name}</DialogTitle>
        <DialogDescription>
          Choose a new name for this workspace.
        </DialogDescription>
      </DialogHeader>
      <form
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault();
          rename.mutate();
        }}
      >
        <div className="flex flex-col gap-2">
          <Label htmlFor="ws-rename">Name</Label>
          <Input
            id="ws-rename"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            maxLength={200}
          />
        </div>
        <DialogFooter className="mt-2">
          <DialogClose render={<Button variant="outline" />}>
            Cancel
          </DialogClose>
          <Button type="submit" disabled={rename.isPending || !name.trim()}>
            {rename.isPending ? "Saving…" : "Save"}
          </Button>
        </DialogFooter>
      </form>
    </>
  );
}
