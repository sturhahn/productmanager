"use client";

import * as React from "react";
import { GripVerticalIcon, TriangleAlertIcon, XIcon } from "lucide-react";
import { cn } from "@/lib/utils";
import type {
  InitiativeDto,
  ProjectDto,
  RoadmapDto,
  RoadmapFeasibilityDto,
  RoadmapItemDto,
} from "@/lib/api/types";
import { initiativeHue } from "@/lib/colors";
import {
  DAY_PX,
  barGeometry,
  computeRange,
  dateToX,
  daysBetween,
  formatDate,
  monthCells,
  packRows,
  periodKey,
  quarterCells,
  resizeDates,
  shiftDates,
  todayIso,
} from "@/lib/timeline";

const DEFAULT_LABEL_W = 256;
const MIN_LABEL_W = 140;
const MAX_LABEL_W = 420;
const LABEL_W_STORAGE_KEY = "roadmap.labelWidth";
const ROW_H = 36;
const BAR_H = 26;
const LANE_PAD = 6;
const HEADER_H = 52;

function clampLabelWidth(width: number): number {
  return Math.min(MAX_LABEL_W, Math.max(MIN_LABEL_W, width));
}

function loadLabelWidth(): number {
  try {
    const stored = window.localStorage.getItem(LABEL_W_STORAGE_KEY);
    if (!stored) return DEFAULT_LABEL_W;
    const parsed = Number(stored);
    if (!Number.isFinite(parsed)) return DEFAULT_LABEL_W;
    return clampLabelWidth(parsed);
  } catch {
    return DEFAULT_LABEL_W;
  }
}

function saveLabelWidth(width: number): void {
  try {
    window.localStorage.setItem(LABEL_W_STORAGE_KEY, String(width));
  } catch {
    // ignore — persistence is a nice-to-have
  }
}

/**
 * Per-browser persisted lane label width, read via useSyncExternalStore so
 * the server-rendered snapshot (always DEFAULT_LABEL_W) can differ from the
 * client's localStorage-backed value without a hydration mismatch — mirrors
 * the activeWorkspaceStore pattern in lib/api/client.ts.
 */
const labelWidthStore = (() => {
  let value =
    typeof window === "undefined" ? DEFAULT_LABEL_W : loadLabelWidth();
  const listeners = new Set<() => void>();
  return {
    get: () => value,
    getServerSnapshot: () => DEFAULT_LABEL_W,
    set(width: number) {
      value = clampLabelWidth(width);
      saveLabelWidth(value);
      for (const listener of listeners) listener();
    },
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
})();

interface PackedBar {
  item: RoadmapItemDto;
  project: ProjectDto | undefined;
  start: string;
  end: string;
  row: number;
}

interface Lane {
  key: string;
  initiative: InitiativeDto | null;
  hueIndex: number;
  bars: PackedBar[];
  height: number;
  minSort: number;
}

type Drag =
  | {
      kind: "bar";
      projectId: string;
      mode: "move" | "start" | "end";
      originX: number;
      origStart: string;
      origEnd: string;
      start: string;
      end: string;
    }
  | {
      kind: "lane";
      laneKey: string;
      order: string[];
    }
  | {
      kind: "label";
      originX: number;
      origWidth: number;
    };

export function RoadmapTimeline({
  roadmap,
  projects,
  initiatives,
  feasibility,
  onReschedule,
  onReorder,
  onUnplan,
}: {
  roadmap: RoadmapDto | null;
  projects: ProjectDto[];
  initiatives: InitiativeDto[];
  feasibility: RoadmapFeasibilityDto | null;
  onReschedule: (projectId: string, start: string, end: string) => void;
  onReorder: (projectIds: string[]) => void;
  onUnplan: (projectId: string) => void;
}) {
  const [drag, setDrag] = React.useState<Drag | null>(null);
  const labelWidth = React.useSyncExternalStore(
    labelWidthStore.subscribe,
    labelWidthStore.get,
    labelWidthStore.getServerSnapshot,
  );
  const lanesRef = React.useRef<HTMLDivElement | null>(null);
  const today = todayIso();

  const LABEL_W = labelWidth;

  const projectById = React.useMemo(
    () => new Map(projects.map((p) => [p.id, p])),
    [projects],
  );
  const hueIndexByInitiative = React.useMemo(
    () => new Map(initiatives.map((i, index) => [i.id, index])),
    [initiatives],
  );
  const initiativeById = React.useMemo(
    () => new Map(initiatives.map((i) => [i.id, i])),
    [initiatives],
  );

  const items = React.useMemo(() => roadmap?.items ?? [], [roadmap]);

  // Apply the in-flight bar drag on top of query data.
  const effectiveDates = React.useCallback(
    (item: RoadmapItemDto): { start: string; end: string } => {
      if (drag?.kind === "bar" && drag.projectId === item.projectId) {
        return { start: drag.start, end: drag.end };
      }
      return { start: item.plannedStart, end: item.plannedEnd };
    },
    [drag],
  );

  const baseLanes = React.useMemo<Lane[]>(() => {
    const groups = new Map<string, RoadmapItemDto[]>();
    for (const item of items) {
      const project = projectById.get(item.projectId);
      const laneKey = project?.initiativeId ?? "unassigned";
      const list = groups.get(laneKey) ?? [];
      list.push(item);
      groups.set(laneKey, list);
    }
    const lanes: Lane[] = [];
    for (const [laneKey, laneItems] of groups) {
      const packed = packRows(
        laneItems.map((item) => {
          const { start, end } = effectiveDates(item);
          return {
            item,
            project: projectById.get(item.projectId),
            start,
            end,
          };
        }),
      );
      const rows = Math.max(...packed.map((p) => p.row)) + 1;
      lanes.push({
        key: laneKey,
        initiative: initiativeById.get(laneKey) ?? null,
        hueIndex: hueIndexByInitiative.get(laneKey) ?? 0,
        bars: packed,
        height: rows * ROW_H + LANE_PAD * 2,
        minSort: Math.min(...laneItems.map((i) => i.sortOrder)),
      });
    }
    lanes.sort((a, b) => a.minSort - b.minSort);
    return lanes;
  }, [items, projectById, initiativeById, hueIndexByInitiative, effectiveDates]);

  // During a lane drag the preview order wins.
  const lanes = React.useMemo(() => {
    if (drag?.kind !== "lane") return baseLanes;
    const byKey = new Map(baseLanes.map((l) => [l.key, l]));
    return drag.order
      .map((key) => byKey.get(key))
      .filter((l): l is Lane => l !== undefined);
  }, [baseLanes, drag]);

  const range = React.useMemo(
    () =>
      computeRange(
        items.map((i) => ({ start: i.plannedStart, end: i.plannedEnd })),
        today,
      ),
    [items, today],
  );
  const months = React.useMemo(
    () => monthCells(range.start, range.endExclusive),
    [range],
  );
  const quarters = React.useMemo(() => quarterCells(months), [months]);
  const totalWidth = daysBetween(range.start, range.endExclusive) * DAY_PX;
  const lanesHeight = lanes.reduce((sum, lane) => sum + lane.height, 0);

  const warningMonths = React.useMemo(() => {
    const map = new Map<string, number>();
    for (const w of feasibility?.capacityWarnings ?? []) {
      const key = periodKey(w.year, w.month);
      map.set(key, (map.get(key) ?? 0) + 1);
    }
    return map;
  }, [feasibility]);

  const violationsByProject = React.useMemo(() => {
    const map = new Map<string, string[]>();
    for (const v of feasibility?.sequencingViolations ?? []) {
      const list = map.get(v.projectId) ?? [];
      list.push(v.description);
      map.set(v.projectId, list);
    }
    return map;
  }, [feasibility]);

  // ----- bar drag -----
  function startBarDrag(
    event: React.PointerEvent,
    projectId: string,
    mode: "move" | "start" | "end",
  ) {
    if (event.button !== 0) return;
    const item = items.find((i) => i.projectId === projectId);
    if (!item) return;
    event.preventDefault();
    event.stopPropagation();
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    setDrag({
      kind: "bar",
      projectId,
      mode,
      originX: event.clientX,
      origStart: item.plannedStart,
      origEnd: item.plannedEnd,
      start: item.plannedStart,
      end: item.plannedEnd,
    });
  }

  function moveBarDrag(event: React.PointerEvent) {
    setDrag((current) => {
      if (current?.kind !== "bar") return current;
      const deltaDays = Math.round((event.clientX - current.originX) / DAY_PX);
      const next =
        current.mode === "move"
          ? shiftDates(current.origStart, current.origEnd, deltaDays)
          : resizeDates(
              current.origStart,
              current.origEnd,
              current.mode,
              deltaDays,
            );
      if (next.start === current.start && next.end === current.end) {
        return current;
      }
      return { ...current, ...next };
    });
  }

  function endBarDrag() {
    setDrag((current) => {
      if (current?.kind !== "bar") return current;
      if (
        current.start !== current.origStart ||
        current.end !== current.origEnd
      ) {
        onReschedule(current.projectId, current.start, current.end);
      }
      return null;
    });
  }

  // ----- lane drag -----
  function startLaneDrag(event: React.PointerEvent, laneKey: string) {
    if (event.button !== 0) return;
    event.preventDefault();
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    setDrag({ kind: "lane", laneKey, order: baseLanes.map((l) => l.key) });
  }

  function moveLaneDrag(event: React.PointerEvent) {
    const container = lanesRef.current;
    setDrag((current) => {
      if (current?.kind !== "lane" || !container) return current;
      const byKey = new Map(baseLanes.map((l) => [l.key, l]));
      const y = event.clientY - container.getBoundingClientRect().top;
      // Find the slot whose vertical midpoint the pointer has passed.
      let offset = 0;
      let targetIndex = current.order.length - 1;
      for (let index = 0; index < current.order.length; index++) {
        const lane = byKey.get(current.order[index]);
        const height = lane?.height ?? ROW_H;
        if (y < offset + height / 2 + (index === 0 ? 0 : 0)) {
          targetIndex = index;
          break;
        }
        offset += height;
      }
      if (y >= offset && targetIndex === current.order.length - 1) {
        targetIndex = current.order.length - 1;
      }
      const fromIndex = current.order.indexOf(current.laneKey);
      if (fromIndex === -1 || fromIndex === targetIndex) return current;
      const order = [...current.order];
      order.splice(fromIndex, 1);
      order.splice(targetIndex, 0, current.laneKey);
      return { ...current, order };
    });
  }

  function endLaneDrag() {
    setDrag((current) => {
      if (current?.kind !== "lane") return current;
      const byKey = new Map(baseLanes.map((l) => [l.key, l]));
      const originalOrder = baseLanes.map((l) => l.key);
      if (current.order.join("|") !== originalOrder.join("|")) {
        const projectIds = current.order.flatMap((key) => {
          const lane = byKey.get(key);
          if (!lane) return [];
          return [...lane.bars]
            .sort((a, b) => a.item.sortOrder - b.item.sortOrder)
            .map((b) => b.item.projectId);
        });
        onReorder(projectIds);
      }
      return null;
    });
  }

  // ----- label column resize -----
  function startLabelDrag(event: React.PointerEvent) {
    if (event.button !== 0) return;
    event.preventDefault();
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    setDrag({ kind: "label", originX: event.clientX, origWidth: labelWidth });
  }

  function moveLabelDrag(event: React.PointerEvent) {
    if (drag?.kind !== "label") return;
    const next = clampLabelWidth(drag.origWidth + (event.clientX - drag.originX));
    labelWidthStore.set(next);
  }

  function endLabelDrag() {
    if (drag?.kind !== "label") return;
    setDrag(null);
  }

  if (!roadmap) {
    return (
      <div className="flex flex-1 items-center justify-center text-sm text-muted-foreground">
        Loading roadmap…
      </div>
    );
  }

  return (
    <div className="relative min-h-0 flex-1 overflow-auto overscroll-contain">
      <div
        style={{ width: LABEL_W + totalWidth, minHeight: "100%" }}
        className="relative"
      >
        {/* ---- header: quarters + months ---- */}
        <div
          className="sticky top-0 z-30 flex border-b bg-background/95 backdrop-blur-sm"
          style={{ height: HEADER_H }}
        >
          <div
            className="sticky left-0 z-40 shrink-0 border-r bg-background"
            style={{ width: LABEL_W }}
          />
          <div className="relative" style={{ width: totalWidth }}>
            <div className="flex h-6 items-stretch">
              {quarters.map((q) => (
                <div
                  key={q.label}
                  style={{ width: q.days * DAY_PX }}
                  className="flex items-center border-r px-2 font-heading text-[11px] font-medium tracking-wide text-muted-foreground"
                >
                  {q.label}
                </div>
              ))}
            </div>
            <div className="flex items-stretch" style={{ height: HEADER_H - 24 }}>
              {months.map((m) => {
                const warned = warningMonths.has(periodKey(m.year, m.month));
                return (
                  <div
                    key={m.start}
                    style={{ width: m.days * DAY_PX }}
                    className={cn(
                      "flex items-center gap-1 border-r border-border/60 px-2 font-mono text-[11px]",
                      warned ? "text-warning-foreground dark:text-warning" : "text-muted-foreground",
                    )}
                  >
                    {m.label}
                    {warned && <TriangleAlertIcon className="size-3 text-warning" />}
                  </div>
                );
              })}
            </div>
          </div>
        </div>

        {/* label column resize handle — full-height overlay whose inner strip
            stays pinned to the column edge (sticky left) while scrolling
            vertically with normal flow (no sticky top) */}
        <div className="pointer-events-none absolute inset-y-0 left-0 right-0 z-50">
          <div
            role="separator"
            aria-orientation="vertical"
            aria-label="Resize lane label column"
            className="pointer-events-auto sticky h-full w-1.5 -translate-x-1/2 cursor-col-resize touch-none hover:bg-primary/30 active:bg-primary/50"
            style={{ left: LABEL_W }}
            onPointerDown={startLabelDrag}
            onPointerMove={moveLabelDrag}
            onPointerUp={endLabelDrag}
            onPointerCancel={endLabelDrag}
          />
        </div>

        {/* ---- lanes ---- */}
        <div ref={lanesRef} className="relative" style={{ minHeight: lanesHeight }}>
          {/* month gridlines + capacity bands, behind everything */}
          <div
            aria-hidden
            className="absolute inset-y-0 z-0"
            style={{ left: LABEL_W, width: totalWidth }}
          >
            {months.map((m) => {
              const x = dateToX(m.start, range.start);
              const warned = warningMonths.get(periodKey(m.year, m.month));
              return (
                <React.Fragment key={m.start}>
                  <div
                    className="absolute inset-y-0 border-l border-border/50"
                    style={{ left: x }}
                  />
                  {warned !== undefined && (
                    <div
                      className="absolute inset-y-0 border-x border-warning/35 bg-warning/12"
                      style={{ left: x, width: m.days * DAY_PX }}
                      title={`${warned} capacity warning${warned === 1 ? "" : "s"} in ${m.label} ${m.year}`}
                    />
                  )}
                </React.Fragment>
              );
            })}
            {/* today */}
            {dateToX(today, range.start) >= 0 &&
              dateToX(today, range.start) <= totalWidth && (
                <div
                  className="absolute inset-y-0 z-10 w-px bg-primary/70"
                  style={{ left: dateToX(today, range.start) }}
                >
                  <div className="absolute -top-0 -left-[3px] size-[7px] rounded-full bg-primary" />
                </div>
              )}
          </div>

          {lanes.length === 0 && (
            <div className="sticky left-0 flex h-40 w-full items-center justify-center px-6">
              <p className="max-w-md text-center text-sm text-muted-foreground">
                Nothing planned yet. Add projects from the tray below — then
                drag bars to reschedule and lanes to resequence.
              </p>
            </div>
          )}

          {lanes.map((lane) => {
            const dragging = drag?.kind === "lane" && drag.laneKey === lane.key;
            return (
              <div
                key={lane.key}
                className={cn(
                  "relative z-10 flex border-b border-border/60 transition-shadow",
                  dragging && "z-20 shadow-lg",
                )}
                style={{ height: lane.height }}
              >
                {/* lane label — sticky on horizontal scroll */}
                <div
                  className={cn(
                    "sticky left-0 z-20 flex shrink-0 items-center gap-2 border-r bg-background px-3",
                    dragging && "bg-accent",
                  )}
                  style={{ width: LABEL_W }}
                  title={lane.initiative?.name ?? "No initiative"}
                >
                  <button
                    type="button"
                    aria-label={`Reorder ${lane.initiative?.name ?? "unassigned"} lane`}
                    className="-ml-1 cursor-grab touch-none rounded p-1 text-muted-foreground/60 hover:bg-muted hover:text-foreground active:cursor-grabbing"
                    onPointerDown={(e) => startLaneDrag(e, lane.key)}
                    onPointerMove={moveLaneDrag}
                    onPointerUp={endLaneDrag}
                    onPointerCancel={endLaneDrag}
                  >
                    <GripVerticalIcon className="size-4" />
                  </button>
                  <span
                    aria-hidden
                    className="size-2.5 shrink-0 rounded-[3px]"
                    style={{ background: initiativeHue(lane.hueIndex) }}
                  />
                  <span className="truncate text-sm font-medium">
                    {lane.initiative?.name ?? "No initiative"}
                  </span>
                </div>

                {/* bars area */}
                <div className="relative" style={{ width: totalWidth }}>
                  {lane.bars.map((bar) => {
                    const geometry = barGeometry(bar.start, bar.end, range.start);
                    const violations =
                      violationsByProject.get(bar.item.projectId) ?? [];
                    const isDraggingBar =
                      drag?.kind === "bar" &&
                      drag.projectId === bar.item.projectId;
                    const hue = initiativeHue(lane.hueIndex);
                    return (
                      <div
                        key={bar.item.projectId}
                        role="button"
                        tabIndex={0}
                        title={`${bar.project?.name ?? "Project"} · ${formatDate(bar.start)} → ${formatDate(bar.end)}${violations.length > 0 ? `\n⚠ ${violations.join("\n⚠ ")}` : ""}`}
                        className={cn(
                          "group absolute flex cursor-grab touch-none items-center overflow-hidden rounded-md text-xs font-medium text-white shadow-sm transition-[box-shadow] select-none",
                          isDraggingBar &&
                            "cursor-grabbing shadow-md ring-2 ring-ring/60",
                          violations.length > 0 &&
                            "ring-2 ring-destructive/80",
                        )}
                        style={{
                          left: geometry.left,
                          width: Math.max(geometry.width, DAY_PX * 2),
                          top: LANE_PAD + bar.row * ROW_H + (ROW_H - BAR_H) / 2,
                          height: BAR_H,
                          background: `color-mix(in oklch, ${hue}, transparent 8%)`,
                        }}
                        onPointerDown={(e) =>
                          startBarDrag(e, bar.item.projectId, "move")
                        }
                        onPointerMove={moveBarDrag}
                        onPointerUp={endBarDrag}
                        onPointerCancel={endBarDrag}
                      >
                        <span className="pointer-events-none truncate px-2 drop-shadow-sm">
                          {violations.length > 0 && (
                            <TriangleAlertIcon className="mr-1 inline size-3 -translate-y-px" />
                          )}
                          {bar.project?.name ?? "Unknown project"}
                        </span>
                        {/* unplan */}
                        <button
                          type="button"
                          aria-label={`Remove ${bar.project?.name ?? "project"} from roadmap`}
                          className="absolute top-1/2 right-1 hidden -translate-y-1/2 rounded-sm bg-black/25 p-0.5 hover:bg-black/45 group-hover:block"
                          onPointerDown={(e) => e.stopPropagation()}
                          onClick={(e) => {
                            e.stopPropagation();
                            onUnplan(bar.item.projectId);
                          }}
                        >
                          <XIcon className="size-3" />
                        </button>
                        {/* resize handles */}
                        <div
                          className="absolute inset-y-0 left-0 w-2 cursor-ew-resize"
                          onPointerDown={(e) =>
                            startBarDrag(e, bar.item.projectId, "start")
                          }
                          onPointerMove={moveBarDrag}
                          onPointerUp={endBarDrag}
                          onPointerCancel={endBarDrag}
                        />
                        <div
                          className="absolute inset-y-0 right-0 w-2 cursor-ew-resize"
                          onPointerDown={(e) =>
                            startBarDrag(e, bar.item.projectId, "end")
                          }
                          onPointerMove={moveBarDrag}
                          onPointerUp={endBarDrag}
                          onPointerCancel={endBarDrag}
                        />
                      </div>
                    );
                  })}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
