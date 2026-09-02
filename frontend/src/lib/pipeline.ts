/**
 * Pure logic for the Pipeline screen: grouping opportunities into funnel
 * stages and comparing an opportunity's age against the expected sales
 * cycle carried by the assumption set's choice parameters.
 */

import type {
  DistributionSpecDto,
  OpportunityDto,
  OpportunityStage,
  ParameterDto,
} from "./api/types";

// ---------------------------------------------------------------------
// Funnel grouping
// ---------------------------------------------------------------------

export type PipelineGroup = "open" | "qualified" | "decided";

export const PIPELINE_GROUPS: PipelineGroup[] = [
  "open",
  "qualified",
  "decided",
];

export function pipelineGroup(stage: OpportunityStage): PipelineGroup {
  switch (stage) {
    case "Open":
      return "open";
    case "Qualified":
      return "qualified";
    default:
      return "decided";
  }
}

/** Newest first inside each group (undecided by openedAt, decided by decidedAt). */
export function groupOpportunities(
  opportunities: OpportunityDto[],
): Record<PipelineGroup, OpportunityDto[]> {
  const groups: Record<PipelineGroup, OpportunityDto[]> = {
    open: [],
    qualified: [],
    decided: [],
  };
  for (const o of opportunities) groups[pipelineGroup(o.stage)].push(o);
  const time = (o: OpportunityDto) =>
    Date.parse(o.decidedAt ?? o.openedAt) || 0;
  for (const g of PIPELINE_GROUPS) {
    groups[g].sort((a, b) => time(b) - time(a));
  }
  return groups;
}

/**
 * Filters (decided) opportunities to those whose `decidedAt` falls within
 * `[from, to]` inclusive. `from`/`to` are `YYYY-MM-DD` date-input strings, or
 * null for an open-ended bound. When both are null every item passes
 * through unfiltered. Once at least one bound is set, items with no
 * `decidedAt` (or an unparseable one) are excluded — there's nothing to
 * compare against.
 */
export function filterByDecidedRange<T extends { decidedAt: string | null }>(
  items: T[],
  from: string | null,
  to: string | null,
): T[] {
  if (from === null && to === null) return items;
  const fromMs = from !== null ? Date.parse(`${from}T00:00:00Z`) : null;
  const toMs = to !== null ? Date.parse(`${to}T23:59:59.999Z`) : null;
  return items.filter((item) => {
    if (!item.decidedAt) return false;
    const decided = Date.parse(item.decidedAt);
    if (!Number.isFinite(decided)) return false;
    if (fromMs !== null && decided < fromMs) return false;
    if (toMs !== null && decided > toMs) return false;
    return true;
  });
}

// ---------------------------------------------------------------------
// Age vs expected sales cycle
// ---------------------------------------------------------------------

const DAYS_PER_MONTH = 365.25 / 12;

/**
 * Fractional months between opening and either the decision or `now`.
 * Clamped at 0 for opened-in-the-future rows.
 */
export function ageMonths(opportunity: OpportunityDto, now: Date): number {
  const opened = Date.parse(opportunity.openedAt);
  const end = opportunity.decidedAt
    ? Date.parse(opportunity.decidedAt)
    : now.getTime();
  if (!Number.isFinite(opened) || !Number.isFinite(end)) return 0;
  return Math.max(0, (end - opened) / (86_400_000 * DAYS_PER_MONTH));
}

/** Mean of a distribution spec, matching the engine's conventions. */
export function distributionMean(spec: DistributionSpecDto): number {
  switch (spec.type) {
    case "Point":
      return spec.p1;
    case "Normal":
      return spec.p1;
    case "Lognormal":
      return Math.exp(spec.p1 + (spec.p2 * spec.p2) / 2);
    case "Beta": {
      const total = spec.p1 + spec.p2;
      return total === 0 ? 0 : spec.p1 / total;
    }
  }
}

const CYCLE_MU_PREFIX = "choice.sales-cycle-mu:";
const CYCLE_SIGMA_PREFIX = "choice.sales-cycle-sigma:";

/**
 * Expected sales-cycle length (months) for a segment from the assumption
 * set's `choice.sales-cycle-mu|sigma:{segment}` parameters — the mean of the
 * lognormal cycle, exp(mu + sigma²/2). Product-specific keys
 * (`...:{segment}:{product}`) are ignored: the pipeline row doesn't know the
 * winner yet. Returns null when the set carries no cycle for the segment
 * (the engine then decides instantly, so there is nothing to compare to).
 */
export function expectedCycleMonths(
  segmentId: string,
  parameters: ParameterDto[],
): number | null {
  let mu: number | null = null;
  let sigma = 0;
  for (const p of parameters) {
    if (p.key === `${CYCLE_MU_PREFIX}${segmentId}`) {
      mu = distributionMean(p.distribution);
    } else if (p.key === `${CYCLE_SIGMA_PREFIX}${segmentId}`) {
      sigma = distributionMean(p.distribution);
    }
  }
  if (mu === null) return null;
  return Math.exp(mu + (sigma * sigma) / 2);
}

export type CycleStatus = "onTrack" | "stretched" | "overdue";

/**
 * How an opportunity's age compares to the expected cycle: within it is on
 * track, up to 1.5x is stretched, beyond that overdue. Null when there is
 * no expectation to compare against.
 */
export function cycleStatus(
  age: number,
  expected: number | null,
): CycleStatus | null {
  if (expected === null || expected <= 0) return null;
  if (age <= expected) return "onTrack";
  if (age <= expected * 1.5) return "stretched";
  return "overdue";
}

/** "0.6 mo" / "14 mo" — compact age label for pipeline rows. */
export function formatMonths(months: number): string {
  if (!Number.isFinite(months)) return "—";
  const rounded = months >= 10 ? Math.round(months) : Number(months.toFixed(1));
  return `${rounded} mo`;
}
