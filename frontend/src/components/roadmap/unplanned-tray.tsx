"use client";

import * as React from "react";
import { CalendarPlusIcon } from "lucide-react";
import type { ProjectDto } from "@/lib/api/types";
import { Button } from "@/components/ui/button";

export function UnplannedTray({
  projects,
  disabled,
  onPlan,
}: {
  projects: ProjectDto[];
  disabled: boolean;
  onPlan: (projectId: string) => void;
}) {
  if (projects.length === 0) return null;

  return (
    <div className="shrink-0 border-t bg-card/60 px-4 py-2.5">
      <p className="mb-1.5 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
        Not on this roadmap
      </p>
      <div className="flex flex-wrap gap-1.5">
        {projects.map((project) => (
          <Button
            key={project.id}
            variant="outline"
            size="xs"
            disabled={disabled}
            onClick={() => onPlan(project.id)}
            title={`Plan ${project.name} on the roadmap`}
          >
            <CalendarPlusIcon data-icon="inline-start" />
            {project.name}
          </Button>
        ))}
      </div>
    </div>
  );
}
