"use client";

import * as React from "react";
import { PlusIcon, PencilIcon, Trash2Icon } from "lucide-react";
import { api } from "@/lib/api/client";
import type { InitiativeDto, StrategicIntent } from "@/lib/api/types";
import { STRATEGIC_INTENTS } from "@/lib/api/types";
import {
  queryKeys,
  useInitiatives,
  useInvalidatingMutation,
  useProjects,
  useWorkspace,
} from "@/lib/queries";
import { initiativeHue } from "@/lib/colors";
import { PageHeader, EmptyState } from "@/components/page-header";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
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

const STRATEGIC_INTENT_BADGE: Record<
  StrategicIntent,
  "default" | "secondary" | "outline"
> = {
  Differentiation: "default",
  Parity: "secondary",
  Retention: "outline",
};

export default function InitiativesPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const initiatives = useInitiatives(workspaceId);
  const projects = useProjects(workspaceId);

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<InitiativeDto | null>(null);
  const [deleting, setDeleting] = React.useState<InitiativeDto | null>(null);

  const projectCount = React.useMemo(() => {
    const counts = new Map<string, number>();
    for (const p of projects.data ?? []) {
      counts.set(p.initiativeId, (counts.get(p.initiativeId) ?? 0) + 1);
    }
    return counts;
  }, [projects.data]);

  const deleteInitiative = useInvalidatingMutation(
    (id: string) => api.initiatives.delete(id),
    () => [
      queryKeys.initiatives(workspaceId ?? ""),
      queryKeys.projects(workspaceId ?? ""),
    ],
    { successMessage: "Initiative deleted", onSuccess: () => setDeleting(null) },
  );

  return (
    <>
      <PageHeader
        title="Initiatives"
        description="Strategic bets — the why behind the projects"
      >
        <Button
          onClick={() => {
            setEditing(null);
            setEditorOpen(true);
          }}
        >
          <PlusIcon data-icon="inline-start" />
          New initiative
        </Button>
      </PageHeader>

      <div className="flex-1 overflow-auto p-6">
        {initiatives.data && initiatives.data.length === 0 ? (
          <EmptyState
            title="No initiatives yet"
            hint="An initiative names an outcome you are betting on. Projects hang off it, and the roadmap groups its lane by initiative."
          />
        ) : (
          <div className="overflow-hidden rounded-xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead className="w-32">Strategic intent</TableHead>
                  <TableHead>Outcome</TableHead>
                  <TableHead>Hypothesized value</TableHead>
                  <TableHead className="w-24">Projects</TableHead>
                  <TableHead className="w-24 text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(initiatives.data ?? []).map((initiative, index) => (
                  <TableRow key={initiative.id}>
                    <TableCell className="font-medium">
                      <span className="flex items-center gap-2">
                        <span
                          aria-hidden
                          className="size-2.5 shrink-0 rounded-[3px]"
                          style={{ background: initiativeHue(index) }}
                        />
                        {initiative.name}
                      </span>
                    </TableCell>
                    <TableCell>
                      {initiative.strategicIntent ? (
                        <Badge variant={STRATEGIC_INTENT_BADGE[initiative.strategicIntent]}>
                          {initiative.strategicIntent}
                        </Badge>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </TableCell>
                    <TableCell className="max-w-72 truncate text-muted-foreground">
                      {initiative.outcome ?? "—"}
                    </TableCell>
                    <TableCell className="max-w-72 truncate text-muted-foreground">
                      {initiative.hypothesizedValue ?? "—"}
                    </TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {projectCount.get(initiative.id) ?? 0}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="inline-flex gap-1">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Edit ${initiative.name}`}
                          onClick={() => {
                            setEditing(initiative);
                            setEditorOpen(true);
                          }}
                        >
                          <PencilIcon />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Delete ${initiative.name}`}
                          onClick={() => setDeleting(initiative)}
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

      {workspaceId && (
        <InitiativeEditorDialog
          key={editing?.id ?? "new"}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          workspaceId={workspaceId}
          initiative={editing}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "initiative"}?`}
        description="Deleting an initiative requires it to have no projects. Move or delete its projects first."
        onConfirm={() => deleting && deleteInitiative.mutate(deleting.id)}
        busy={deleteInitiative.isPending}
      />
    </>
  );
}

function InitiativeEditorDialog({
  open,
  onOpenChange,
  workspaceId,
  initiative,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workspaceId: string;
  initiative: InitiativeDto | null;
}) {
  const [name, setName] = React.useState(initiative?.name ?? "");
  const [outcome, setOutcome] = React.useState(initiative?.outcome ?? "");
  const [value, setValue] = React.useState(initiative?.hypothesizedValue ?? "");
  const [strategicIntent, setStrategicIntent] = React.useState<StrategicIntent | "none">(
    initiative?.strategicIntent ?? "none",
  );

  const save = useInvalidatingMutation(
    () =>
      initiative
        ? api.initiatives.update(initiative.id, {
            name,
            outcome: outcome || null,
            hypothesizedValue: value || null,
            targetSegmentIds: initiative.targetSegmentIds,
            targetNeedIds: initiative.targetNeedIds,
            strategicIntent: strategicIntent === "none" ? null : strategicIntent,
          })
        : api.initiatives.create({
            workspaceId,
            name,
            outcome: outcome || null,
            hypothesizedValue: value || null,
            strategicIntent: strategicIntent === "none" ? null : strategicIntent,
          }),
    () => [queryKeys.initiatives(workspaceId)],
    {
      successMessage: initiative ? "Initiative saved" : "Initiative created",
      onSuccess: () => onOpenChange(false),
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>
            {initiative ? "Edit initiative" : "New initiative"}
          </DialogTitle>
          <DialogDescription>
            Name the bet, the outcome it should produce, and why it is worth it.
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
            <Label htmlFor="init-name">Name</Label>
            <Input
              id="init-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="init-strategic-intent">Strategic intent</Label>
            <Select
              items={[
                { label: "None", value: "none" },
                ...STRATEGIC_INTENTS.map((intent) => ({ label: intent, value: intent })),
              ]}
              value={strategicIntent}
              onValueChange={(v) => v && setStrategicIntent(v as StrategicIntent | "none")}
            >
              <SelectTrigger id="init-strategic-intent" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">None</SelectItem>
                {STRATEGIC_INTENTS.map((intent) => (
                  <SelectItem key={intent} value={intent}>
                    {intent}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="init-outcome">Outcome</Label>
            <Textarea
              id="init-outcome"
              value={outcome}
              onChange={(e) => setOutcome(e.target.value)}
              rows={2}
              placeholder="What changes in the market if this works?"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="init-value">Hypothesized value</Label>
            <Textarea
              id="init-value"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              rows={2}
              placeholder="Why we believe the bet pays off"
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending
                ? "Saving…"
                : initiative
                  ? "Save changes"
                  : "Create initiative"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
