/**
 * Pure shaping logic for the simulation fan charts and KPI headlines.
 * No echarts import — these produce plain series descriptors that the
 * chart component feeds to echarts, so the math stays unit-testable.
 */

import type { EngineMetricTrajectory, EngineResult } from "./api/types";

// ---------------------------------------------------------------------
// Axis labels
// ---------------------------------------------------------------------

const MONTHS = [
  "Jan",
  "Feb",
  "Mar",
  "Apr",
  "May",
  "Jun",
  "Jul",
  "Aug",
  "Sep",
  "Oct",
  "Nov",
  "Dec",
];

/**
 * Month labels for the run horizon starting at `startDateIso` (yyyy-MM-dd).
 * Index i = month i of the simulation, e.g. "Jan '27".
 */
export function monthLabels(startDateIso: string, horizonMonths: number): string[] {
  const year = Number(startDateIso.slice(0, 4));
  const month = Number(startDateIso.slice(5, 7)) - 1; // 0-based
  const out: string[] = [];
  for (let i = 0; i < horizonMonths; i++) {
    const idx = year * 12 + month + i;
    const y = Math.floor(idx / 12);
    const m = idx % 12;
    out.push(`${MONTHS[m]} '${String(y % 100).padStart(2, "0")}`);
  }
  return out;
}

// ---------------------------------------------------------------------
// Fan band series (P10–P90 band + median line)
// ---------------------------------------------------------------------

export interface FanSeriesSpec {
  /** Unique per band on a chart; the three echarts series derive ids from it. */
  id: string;
  /** Legend/tooltip name for the median line. */
  name: string;
  /** Resolved CSS color (rgb/hex — not a var() reference). */
  color: string;
  p10: number[];
  p50: number[];
  p90: number[];
  /** Band fill opacity; default 0.16. */
  bandOpacity?: number;
}

export interface EChartsLineSeries {
  id: string;
  name?: string;
  type: "line";
  stack?: string;
  stackStrategy?: "all";
  data: number[];
  symbol: "none";
  silent?: boolean;
  z?: number;
  smooth?: boolean;
  lineStyle: { width: number; opacity?: number; color?: string };
  areaStyle?: { color: string; opacity: number };
  itemStyle?: { color: string };
  emphasis?: { disabled: boolean };
  tooltip?: { show: boolean };
}

/**
 * Builds the three echarts line series that draw one fan: an invisible base
 * at P10, a stacked band of height (P90 − P10), and the median line. The
 * stack trick keeps the band correct even when P10 is negative (margins).
 */
export function fanSeries(spec: FanSeriesSpec): EChartsLineSeries[] {
  const n = Math.min(spec.p10.length, spec.p50.length, spec.p90.length);
  const band: number[] = new Array(n);
  for (let i = 0; i < n; i++) band[i] = spec.p90[i] - spec.p10[i];

  return [
    {
      id: `${spec.id}-base`,
      type: "line",
      stack: spec.id,
      stackStrategy: "all",
      data: spec.p10.slice(0, n),
      symbol: "none",
      silent: true,
      lineStyle: { width: 0, opacity: 0 },
      emphasis: { disabled: true },
      tooltip: { show: false },
    },
    {
      id: `${spec.id}-band`,
      type: "line",
      stack: spec.id,
      stackStrategy: "all",
      data: band,
      symbol: "none",
      silent: true,
      lineStyle: { width: 0, opacity: 0 },
      areaStyle: { color: spec.color, opacity: spec.bandOpacity ?? 0.16 },
      emphasis: { disabled: true },
      tooltip: { show: false },
    },
    {
      id: `${spec.id}-median`,
      name: spec.name,
      type: "line",
      data: spec.p50.slice(0, n),
      symbol: "none",
      z: 3,
      smooth: false,
      lineStyle: { width: 2, color: spec.color },
      itemStyle: { color: spec.color },
    },
  ];
}

// ---------------------------------------------------------------------
// Headline metrics
// ---------------------------------------------------------------------

export function sum(values: number[]): number {
  let s = 0;
  for (const v of values) s += v;
  return s;
}

export function last(values: number[]): number {
  return values.length === 0 ? 0 : values[values.length - 1];
}

export interface HeadlineMetrics {
  npvP10: number;
  npvP50: number;
  npvP90: number;
  npvMean: number;
  /** Sum of median monthly bookings over the horizon. */
  totalBookingsP50: number;
  /** Median client count in the final month. */
  endingClientsP50: number;
  /** Median recognized ARR in the final month. */
  endingArrP50: number;
  /** Median contribution margin in the final month. */
  endingMarginP50: number;
}

export function headlineMetrics(result: EngineResult): HeadlineMetrics {
  return {
    npvP10: result.npv.p10,
    npvP50: result.npv.p50,
    npvP90: result.npv.p90,
    npvMean: result.npv.mean,
    totalBookingsP50: sum(result.total.bookings.p50),
    endingClientsP50: last(result.total.clients.p50),
    endingArrP50: last(result.total.recognizedArr.p50),
    endingMarginP50: last(result.total.contributionMargin.p50),
  };
}

// ---------------------------------------------------------------------
// Compare deltas
// ---------------------------------------------------------------------

export interface MetricDeltaRow {
  label: string;
  a: number;
  b: number;
  delta: number;
  /** Relative change b vs a; null when a is 0. */
  deltaPct: number | null;
}

export function deltaRow(label: string, a: number, b: number): MetricDeltaRow {
  return {
    label,
    a,
    b,
    delta: b - a,
    deltaPct: a === 0 ? null : (b - a) / Math.abs(a),
  };
}

export function compareHeadlines(
  a: EngineResult,
  b: EngineResult,
): MetricDeltaRow[] {
  const ha = headlineMetrics(a);
  const hb = headlineMetrics(b);
  return [
    deltaRow("NPV (P50)", ha.npvP50, hb.npvP50),
    deltaRow("NPV (P10)", ha.npvP10, hb.npvP10),
    deltaRow("NPV (P90)", ha.npvP90, hb.npvP90),
    deltaRow("Total bookings (P50)", ha.totalBookingsP50, hb.totalBookingsP50),
    deltaRow("Ending ARR (P50)", ha.endingArrP50, hb.endingArrP50),
    deltaRow("Ending clients (P50)", ha.endingClientsP50, hb.endingClientsP50),
    deltaRow(
      "Ending margin / mo (P50)",
      ha.endingMarginP50,
      hb.endingMarginP50,
    ),
  ];
}

// ---------------------------------------------------------------------
// Metric selection helpers
// ---------------------------------------------------------------------

export type TrajectoryMetricKey =
  | "recognizedArr"
  | "bookings"
  | "clients"
  | "contributionMargin"
  | "newWins"
  | "churned"
  | "prospectPool";

export const METRIC_LABELS: Record<TrajectoryMetricKey, string> = {
  recognizedArr: "Recognized ARR",
  bookings: "Bookings",
  clients: "Clients",
  contributionMargin: "Contribution margin",
  newWins: "New wins",
  churned: "Churned",
  prospectPool: "Prospect pool",
};

export function metricOf(
  trajectories: {
    clients: EngineMetricTrajectory;
    prospectPool: EngineMetricTrajectory;
    newWins: EngineMetricTrajectory;
    churned: EngineMetricTrajectory;
    bookings: EngineMetricTrajectory;
    recognizedArr: EngineMetricTrajectory;
    contributionMargin: EngineMetricTrajectory;
  },
  key: TrajectoryMetricKey,
): EngineMetricTrajectory {
  return trajectories[key];
}

// ---------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------

const compact = new Intl.NumberFormat("en-US", {
  notation: "compact",
  maximumFractionDigits: 1,
});

const plain = new Intl.NumberFormat("en-US", {
  maximumFractionDigits: 1,
});

/** "1.2M", "480K", "12" — used on KPI cards and chart axes. */
export function formatCompact(value: number): string {
  if (!Number.isFinite(value)) return "—";
  if (Math.abs(value) < 1000) return plain.format(value);
  return compact.format(value);
}

/** Signed variant for delta cells: "+1.2M" / "−480K". */
export function formatSignedCompact(value: number): string {
  if (!Number.isFinite(value)) return "—";
  const abs = formatCompact(Math.abs(value));
  if (value > 0) return `+${abs}`;
  if (value < 0) return `−${abs}`;
  return abs;
}
