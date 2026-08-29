"use client";

import * as React from "react";
import { PlusIcon, CalendarCogIcon, PencilIcon, Trash2Icon } from "lucide-react";
import { api } from "@/lib/api/client";
import type { TeamDto } from "@/lib/api/types";
import { formatPeriod, periodKey } from "@/lib/timeline";
import {
  queryKeys,
  useInvalidatingMutation,
  useTeams,
  useWorkspace,
} from "@/lib/queries";
import { PageHeader, EmptyState } from "@/components/page-header";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

/** Rolling 12 months starting from the current month. */
function rollingPeriods(): { year: number; month: number }[] {
  const now = new Date();
  const out: { year: number; month: number }[] = [];
  for (let i = 0; i < 12; i++) {
    const idx = now.getFullYear() * 12 + now.getMonth() + i;
    out.push({ year: Math.floor(idx / 12), month: (idx % 12) + 1 });
  }
  return out;
}

export default function TeamsPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const teams = useTeams(workspaceId);

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<TeamDto | null>(null);
  const [capacityTeam, setCapacityTeam] = React.useState<TeamDto | null>(null);
  const [deleting, setDeleting] = React.useState<TeamDto | null>(null);

  const invalidate = () => [queryKeys.teams(workspaceId ?? "")];

  const deleteTeam = useInvalidatingMutation(
    (id: string) => api.teams.delete(id),
    invalidate,
    { successMessage: "Team deleted", onSuccess: () => setDeleting(null) },
  );

  return (
    <>
      <PageHeader
        title="Teams"
        description="Capacity budgets that constrain the roadmap"
      >
        <Button
          onClick={() => {
            setEditing(null);
            setEditorOpen(true);
          }}
        >
          <PlusIcon data-icon="inline-start" />
          New team
        </Button>
      </PageHeader>

      <div className="flex-1 overflow-auto p-6">
        {teams.data && teams.data.length === 0 ? (
          <EmptyState
            title="No teams yet"
            hint="Create a team, then set its capacity per month. The roadmap flags any period where planned effort exceeds it."
          />
        ) : (
          <div className="overflow-hidden rounded-xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Specialization</TableHead>
                  <TableHead>Capacity set for</TableHead>
                  <TableHead className="w-32 text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(teams.data ?? []).map((team) => (
                  <TableRow key={team.id}>
                    <TableCell className="font-medium">{team.name}</TableCell>
                    <TableCell>
                      {team.specializationTag ? (
                        <Badge variant="secondary">{team.specializationTag}</Badge>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {team.capacities.length === 0
                        ? "no periods"
                        : `${team.capacities.length} period${team.capacities.length === 1 ? "" : "s"}`}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="inline-flex gap-1">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Edit capacity for ${team.name}`}
                          onClick={() => setCapacityTeam(team)}
                        >
                          <CalendarCogIcon />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Edit ${team.name}`}
                          onClick={() => {
                            setEditing(team);
                            setEditorOpen(true);
                          }}
                        >
                          <PencilIcon />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Delete ${team.name}`}
                          onClick={() => setDeleting(team)}
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
        <TeamEditorDialog
          key={editing?.id ?? "new"}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          workspaceId={workspaceId}
          team={editing}
        />
      )}

      {capacityTeam && (
        <CapacityDialog
          team={capacityTeam}
          workspaceId={workspaceId ?? ""}
          onOpenChange={(open) => !open && setCapacityTeam(null)}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "team"}?`}
        description="Its capacity entries go with it. Projects that demanded effort from it will show unknown-team warnings on the roadmap."
        onConfirm={() => deleting && deleteTeam.mutate(deleting.id)}
        busy={deleteTeam.isPending}
      />
    </>
  );
}

function TeamEditorDialog({
  open,
  onOpenChange,
  workspaceId,
  team,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workspaceId: string;
  team: TeamDto | null;
}) {
  const [name, setName] = React.useState(team?.name ?? "");
  const [tag, setTag] = React.useState(team?.specializationTag ?? "");

  const save = useInvalidatingMutation(
    () =>
      team
        ? api.teams.update(team.id, {
            name,
            specializationTag: tag || null,
          })
        : api.teams.create({
            workspaceId,
            name,
            specializationTag: tag || null,
          }),
    () => [queryKeys.teams(workspaceId)],
    {
      successMessage: team ? "Team saved" : "Team created",
      onSuccess: () => onOpenChange(false),
    },
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{team ? "Edit team" : "New team"}</DialogTitle>
          <DialogDescription>
            A team is a monthly capacity budget, not a list of people.
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
            <Label htmlFor="team-name">Name</Label>
            <Input
              id="team-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="team-tag">Specialization tag</Label>
            <Input
              id="team-tag"
              value={tag}
              onChange={(e) => setTag(e.target.value)}
              placeholder="e.g. backend, data, design"
              maxLength={100}
            />
          </div>
          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending ? "Saving…" : team ? "Save changes" : "Create team"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function CapacityDialog({
  team,
  workspaceId,
  onOpenChange,
}: {
  team: TeamDto;
  workspaceId: string;
  onOpenChange: (open: boolean) => void;
}) {
  const periods = React.useMemo(() => rollingPeriods(), []);
  const existing = React.useMemo(() => {
    const map = new Map<string, number>();
    for (const c of team.capacities) map.set(periodKey(c.year, c.month), c.points);
    return map;
  }, [team]);

  const [values, setValues] = React.useState<Record<string, string>>(() => {
    const init: Record<string, string> = {};
    for (const p of periods) {
      const found = existing.get(periodKey(p.year, p.month));
      init[periodKey(p.year, p.month)] = found !== undefined ? String(found) : "";
    }
    return init;
  });

  const save = useInvalidatingMutation(
    async () => {
      for (const p of periods) {
        const key = periodKey(p.year, p.month);
        const raw = values[key].trim();
        const prev = existing.get(key);
        if (raw === "" && prev === undefined) continue;
        const points = raw === "" ? 0 : Number(raw);
        if (Number.isNaN(points) || points < 0) {
          throw new Error(`Invalid capacity for ${formatPeriod(p.year, p.month)}`);
        }
        if (prev !== undefined && prev === points) continue;
        await api.teams.setCapacity(team.id, { year: p.year, month: p.month, points });
      }
    },
    () => [queryKeys.teams(workspaceId)],
    { successMessage: "Capacity saved", onSuccess: () => onOpenChange(false) },
  );

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{team.name} — capacity</DialogTitle>
          <DialogDescription>
            Points per month (FTE-months or story points — your unit, applied
            consistently). Blank means undefined; the roadmap flags demand
            against undefined capacity.
          </DialogDescription>
        </DialogHeader>
        <div className="grid grid-cols-2 gap-x-4 gap-y-2 sm:grid-cols-3">
          {periods.map((p) => {
            const key = periodKey(p.year, p.month);
            return (
              <div key={key} className="flex flex-col gap-1">
                <Label
                  htmlFor={`cap-${key}`}
                  className="font-mono text-[11px] text-muted-foreground"
                >
                  {formatPeriod(p.year, p.month)}
                </Label>
                <Input
                  id={`cap-${key}`}
                  inputMode="decimal"
                  className="h-7 font-mono text-xs"
                  value={values[key]}
                  onChange={(e) =>
                    setValues((v) => ({ ...v, [key]: e.target.value }))
                  }
                  placeholder="—"
                />
              </div>
            );
          })}
        </div>
        <DialogFooter>
          <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
          <Button onClick={() => save.mutate(undefined)} disabled={save.isPending}>
            {save.isPending ? "Saving…" : "Save capacity"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
