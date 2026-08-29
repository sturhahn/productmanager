import { describe, expect, it } from "vitest";
import type { EngineMetricTrajectory, EngineResult } from "./api/types";
import {
  compareHeadlines,
  deltaRow,
  fanSeries,
  formatCompact,
  formatSignedCompact,
  headlineMetrics,
  last,
  monthLabels,
  sum,
} from "./fanchart";

function traj(p50: number[], spread = 0): EngineMetricTrajectory {
  return {
    mean: p50,
    p10: p50.map((v) => v - spread),
    p50,
    p90: p50.map((v) => v + spread),
  };
}

function result(overrides?: Partial<EngineResult>): EngineResult {
  const zero = traj([0, 0, 0]);
  const total = {
    segmentId: "total",
    clients: traj([10, 12, 15]),
    prospectPool: zero,
    newWins: zero,
    churned: zero,
    bookings: traj([100, 200, 300]),
    recognizedArr: traj([1200, 1400, 1800]),
    contributionMargin: traj([-50, 10, 90]),
  };
  return {
    segments: [total],
    total,
    npv: { mean: 5100, p10: 4000, p50: 5000, p90: 6500 },
    npvPerPath: [],
    paths: 100,
    seed: 0,
    horizonMonths: 3,
    elapsedMs: 0,
    ...overrides,
  };
}

describe("monthLabels", () => {
  it("starts at the run's start month and crosses year boundaries", () => {
    const labels = monthLabels("2026-11-01", 4);
    expect(labels).toEqual(["Nov '26", "Dec '26", "Jan '27", "Feb '27"]);
  });

  it("produces exactly horizonMonths labels", () => {
    expect(monthLabels("2026-01-01", 60)).toHaveLength(60);
  });
});

describe("fanSeries", () => {
  const spec = {
    id: "arr",
    name: "ARR",
    color: "rgb(10, 20, 30)",
    p10: [-5, 0, 10],
    p50: [0, 10, 20],
    p90: [5, 30, 40],
  };

  it("builds base at p10, band of height p90 - p10, and a median line", () => {
    const [base, band, median] = fanSeries(spec);
    expect(base.data).toEqual([-5, 0, 10]);
    expect(band.data).toEqual([10, 30, 30]); // works with negative p10
    expect(median.data).toEqual([0, 10, 20]);
  });

  it("stacks base and band on the same stack with stackStrategy all", () => {
    const [base, band, median] = fanSeries(spec);
    expect(base.stack).toBe("arr");
    expect(band.stack).toBe("arr");
    expect(base.stackStrategy).toBe("all");
    expect(median.stack).toBeUndefined();
  });

  it("keeps the band and base invisible and silent, colors the median", () => {
    const [base, band, median] = fanSeries(spec);
    expect(base.lineStyle.opacity).toBe(0);
    expect(band.areaStyle).toEqual({ color: "rgb(10, 20, 30)", opacity: 0.16 });
    expect(band.silent).toBe(true);
    expect(median.lineStyle.color).toBe("rgb(10, 20, 30)");
    expect(median.name).toBe("ARR");
  });

  it("truncates to the shortest percentile array", () => {
    const series = fanSeries({ ...spec, p90: [5, 30] });
    expect(series[0].data).toHaveLength(2);
    expect(series[2].data).toHaveLength(2);
  });
});

describe("headline metrics", () => {
  it("sums median bookings and takes final-month medians", () => {
    const h = headlineMetrics(result());
    expect(h.totalBookingsP50).toBe(600);
    expect(h.endingClientsP50).toBe(15);
    expect(h.endingArrP50).toBe(1800);
    expect(h.endingMarginP50).toBe(90);
    expect(h.npvP50).toBe(5000);
    expect(h.npvP10).toBe(4000);
    expect(h.npvP90).toBe(6500);
  });

  it("sum and last handle empty arrays", () => {
    expect(sum([])).toBe(0);
    expect(last([])).toBe(0);
  });
});

describe("compare deltas", () => {
  it("computes absolute and relative deltas", () => {
    const row = deltaRow("NPV", 1000, 1250);
    expect(row.delta).toBe(250);
    expect(row.deltaPct).toBeCloseTo(0.25);
  });

  it("guards relative delta against a zero baseline", () => {
    expect(deltaRow("x", 0, 10).deltaPct).toBeNull();
  });

  it("uses the magnitude of a negative baseline so the sign follows the change", () => {
    const row = deltaRow("margin", -100, -50);
    expect(row.delta).toBe(50);
    expect(row.deltaPct).toBeCloseTo(0.5);
  });

  it("lines up headline rows for two runs", () => {
    const a = result();
    const b = result({
      npv: { mean: 6000, p10: 4500, p50: 6000, p90: 8000 },
    });
    const rows = compareHeadlines(a, b);
    expect(rows[0]).toMatchObject({ label: "NPV (P50)", a: 5000, b: 6000, delta: 1000 });
    expect(rows.map((r) => r.label)).toContain("Total bookings (P50)");
  });
});

describe("formatting", () => {
  it("keeps small numbers plain and compacts large ones", () => {
    expect(formatCompact(12)).toBe("12");
    expect(formatCompact(999)).toBe("999");
    expect(formatCompact(1200)).toBe("1.2K");
    expect(formatCompact(2500000)).toBe("2.5M");
  });

  it("signs deltas with a proper minus", () => {
    expect(formatSignedCompact(1500)).toBe("+1.5K");
    expect(formatSignedCompact(-1500)).toBe("−1.5K");
    expect(formatSignedCompact(0)).toBe("0");
  });
});
