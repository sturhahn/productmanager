/**
 * Pure shaping logic for the calibration diff view — the transparency
 * centerpiece: what the data moved, by how much, and on what evidence.
 */

import type {
  CalibrationDiffDto,
  CalibrationParameterDiffDto,
} from "./api/types";

// ---------------------------------------------------------------------
// Rows
// ---------------------------------------------------------------------

export type ShiftDirection = "up" | "down" | "flat";

export type ShiftMagnitude = "none" | "small" | "medium" | "large";

export interface DiffRow {
  key: string;
  clientSegmentId: string | null;
  flowKind: string | null;
  moduleProductId: string | null;
  productId: string | null;
  priorMean: number;
  priorStdDev: number;
  posteriorMean: number;
  posteriorStdDev: number;
  /** posterior mean − prior mean, as reported by the run. */
  shift: number;
  /** shift / |prior mean|; null when the prior mean is 0. */
  relativeShift: number | null;
  direction: ShiftDirection;
  magnitude: ShiftMagnitude;
  sampleSize: number;
  rationale: string;
}

/** Relative-shift thresholds for the magnitude coloring. */
const SMALL = 0.005;
const MEDIUM = 0.05;
const LARGE = 0.2;

export function shiftMagnitude(
  shift: number,
  relativeShift: number | null,
): ShiftMagnitude {
  if (shift === 0) return "none";
  if (relativeShift === null) return "large"; // moved off a zero prior
  const abs = Math.abs(relativeShift);
  if (abs < SMALL) return "none";
  if (abs < MEDIUM) return "small";
  if (abs < LARGE) return "medium";
  return "large";
}

export function toDiffRow(d: CalibrationParameterDiffDto): DiffRow {
  const relativeShift =
    d.priorMean === 0 ? null : d.shift / Math.abs(d.priorMean);
  return {
    key: d.key,
    clientSegmentId: d.clientSegmentId,
    flowKind: d.flowKind ?? null,
    moduleProductId: d.moduleProductId,
    productId: d.productId,
    priorMean: d.priorMean,
    priorStdDev: d.priorStdDev,
    posteriorMean: d.posteriorMean,
    posteriorStdDev: d.posteriorStdDev,
    shift: d.shift,
    relativeShift,
    direction: d.shift > 0 ? "up" : d.shift < 0 ? "down" : "flat",
    magnitude: shiftMagnitude(d.shift, relativeShift),
    sampleSize: d.sampleSize,
    rationale: d.rationale,
  };
}

/** Sort key: relative shift when defined, absolute shift otherwise. */
function sortWeight(row: DiffRow): number {
  return row.relativeShift === null
    ? Math.abs(row.shift)
    : Math.abs(row.relativeShift);
}

/** Fitted parameters, biggest movers first. */
export function shapeDiffRows(diff: CalibrationDiffDto): DiffRow[] {
  return diff.fitted.map(toDiffRow).sort((a, b) => sortWeight(b) - sortWeight(a));
}

// ---------------------------------------------------------------------
// Header summary
// ---------------------------------------------------------------------

export interface DiffSummary {
  /** Parameters whose posterior mean actually moved. */
  moved: number;
  /** Fitted parameters in the diff. */
  fitted: number;
  /** Priors the evidence could not move. */
  unfitted: number;
  /** The biggest mover (by relative shift), or null when nothing moved. */
  largest: DiffRow | null;
}

export function diffSummary(diff: CalibrationDiffDto): DiffSummary {
  const rows = shapeDiffRows(diff);
  const movedRows = rows.filter((r) => r.shift !== 0);
  return {
    moved: movedRows.length,
    fitted: rows.length,
    unfitted: diff.unfitted.length,
    largest: movedRows[0] ?? null,
  };
}

// ---------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------

/** 3 significant digits without scientific notation for sane ranges. */
export function formatParamValue(value: number): string {
  if (!Number.isFinite(value)) return "—";
  if (value === 0) return "0";
  const abs = Math.abs(value);
  if (abs >= 1000) {
    return value.toLocaleString("en-US", { maximumFractionDigits: 0 });
  }
  if (abs >= 100) return value.toFixed(0);
  if (abs >= 1) return value.toFixed(2);
  if (abs >= 0.01) return value.toFixed(3);
  return value.toPrecision(2);
}

/** "0.042 ± 0.010"; the ± is dropped for zero-width (point) posteriors. */
export function formatMeanSd(mean: number, sd: number): string {
  const m = formatParamValue(mean);
  return sd === 0 ? m : `${m} ± ${formatParamValue(sd)}`;
}

/** "+12.3%" / "−4.1%" / "new" (off a zero prior) / "0%". */
export function formatRelativeShift(row: DiffRow): string {
  if (row.shift === 0) return "0%";
  if (row.relativeShift === null) return "new";
  const pct = Math.abs(row.relativeShift * 100);
  const digits = pct >= 100 ? 0 : 1;
  const sign = row.relativeShift > 0 ? "+" : "−";
  return `${sign}${pct.toFixed(digits)}%`;
}
