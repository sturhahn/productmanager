"use client";

import * as React from "react";
import {
  CircleCheckIcon,
  TriangleAlertIcon,
  OctagonAlertIcon,
} from "lucide-react";
import type {
  ProjectDto,
  RoadmapFeasibilityDto,
  TeamDto,
} from "@/lib/api/types";
import { formatPeriod } from "@/lib/timeline";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";

export function WarningsPanel({
  feasibility,
  loading,
  teams,
  projects,
}: {
  feasibility: RoadmapFeasibilityDto | null;
  loading: boolean;
  teams: TeamDto[];
  projects: ProjectDto[];
}) {
  const teamName = React.useMemo(() => {
    const map = new Map(teams.map((t) => [t.id, t.name]));
    return (id: string, fallback: string | null) =>
      fallback ?? map.get(id) ?? "Unknown team";
  }, [teams]);

  const projectName = React.useMemo(() => {
    const map = new Map(projects.map((p) => [p.id, p.name]));
    return (id: string) => map.get(id) ?? "Unknown project";
  }, [projects]);

  const warnings = feasibility?.capacityWarnings ?? [];
  const violations = feasibility?.sequencingViolations ?? [];

  return (
    <aside className="flex w-72 shrink-0 flex-col border-l bg-card/50">
      <div className="flex items-center justify-between border-b px-4 py-3">
        <h2 className="font-heading text-sm font-semibold">Feasibility</h2>
        {loading || !feasibility ? (
          <Badge variant="outline">checking…</Badge>
        ) : feasibility.isFeasible ? (
          <Badge className="gap-1 border-transparent bg-chart-4/15 text-chart-4">
            <CircleCheckIcon className="size-3" />
            Feasible
          </Badge>
        ) : (
          <Badge className="gap-1 border-transparent bg-warning/15 text-warning-foreground dark:text-warning">
            <TriangleAlertIcon className="size-3" />
            {warnings.length + violations.length} issue
            {warnings.length + violations.length === 1 ? "" : "s"}
          </Badge>
        )}
      </div>

      <div className="flex-1 overflow-y-auto p-4">
        {feasibility && feasibility.isFeasible && (
          <p className="text-sm text-muted-foreground">
            Every planned month fits team capacity and all dependencies finish
            before their dependents start.
          </p>
        )}

        {warnings.length > 0 && (
          <section>
            <h3 className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">
              Capacity
            </h3>
            <ul className="flex flex-col gap-2">
              {warnings.map((w, index) => (
                <li
                  key={`${w.teamId}-${w.year}-${w.month}-${index}`}
                  className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-sm"
                >
                  <div className="flex items-center gap-1.5 font-medium">
                    <TriangleAlertIcon className="size-3.5 shrink-0 text-warning" />
                    {teamName(w.teamId, w.teamName)}
                    <span className="ml-auto font-mono text-[11px] text-muted-foreground">
                      {formatPeriod(w.year, w.month)}
                    </span>
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {w.capacityUndefined ? (
                      <>
                        {w.demandPoints} pts demanded, capacity undefined — set
                        it on the Teams page.
                      </>
                    ) : (
                      <>
                        {w.demandPoints} pts demanded vs {w.capacityPoints}{" "}
                        capacity —{" "}
                        <span className="font-medium text-warning-foreground dark:text-warning">
                          over by {w.overallocationPoints}
                        </span>
                      </>
                    )}
                  </p>
                </li>
              ))}
            </ul>
          </section>
        )}

        {warnings.length > 0 && violations.length > 0 && (
          <Separator className="my-4" />
        )}

        {violations.length > 0 && (
          <section>
            <h3 className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">
              Sequencing
            </h3>
            <ul className="flex flex-col gap-2">
              {violations.map((v, index) => (
                <li
                  key={`${v.projectId}-${v.dependsOnProjectId}-${index}`}
                  className="rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm"
                >
                  <div className="flex items-center gap-1.5 font-medium">
                    <OctagonAlertIcon className="size-3.5 shrink-0 text-destructive" />
                    {projectName(v.projectId)}
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {v.description}
                  </p>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </aside>
  );
}
