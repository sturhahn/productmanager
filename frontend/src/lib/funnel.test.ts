import { describe, expect, it } from "vitest";
import {
  channelWinRates,
  diagnosticsHeadline,
  formatMeanCount,
  formatRate,
  funnelParts,
  funnelTotals,
  segmentWinRates,
} from "./funnel";
import type {
  EngineFirmLevelDiagnostics,
  EngineSegmentFunnel,
} from "./api/types";

const segA: EngineSegmentFunnel = {
  segmentId: "seg-a",
  opened: 100,
  qualified: 80,
  won: 30,
  lost: 40,
  noDecision: 10,
  stillOpen: 20,
  winRate: 0.375,
};

const segB: EngineSegmentFunnel = {
  segmentId: "seg-b",
  opened: 50,
  qualified: 45,
  won: 20,
  lost: 5,
  noDecision: 5,
  stillOpen: 20,
  winRate: 20 / 30,
};

describe("funnelTotals", () => {
  it("sums outcomes across segments and recomputes the win rate over decided", () => {
    const t = funnelTotals([segA, segB]);
    expect(t.opened).toBe(150);
    expect(t.qualified).toBe(125);
    expect(t.won).toBe(50);
    expect(t.lost).toBe(45);
    expect(t.noDecision).toBe(15);
    expect(t.stillOpen).toBe(40);
    // 50 won of 110 decided — not the average of the per-segment rates.
    expect(t.winRate).toBeCloseTo(50 / 110, 10);
  });

  it("returns a zero win rate when nothing has been decided", () => {
    const t = funnelTotals([
      { ...segA, won: 0, lost: 0, noDecision: 0, stillOpen: 100 },
    ]);
    expect(t.winRate).toBe(0);
  });

  it("handles an empty segment list", () => {
    const t = funnelTotals([]);
    expect(t.opened).toBe(0);
    expect(t.winRate).toBe(0);
  });
});

describe("funnelParts", () => {
  it("splits opened into four exhaustive outcome fractions summing to 1", () => {
    const parts = funnelParts(segA);
    expect(parts.map((p) => p.key)).toEqual([
      "won",
      "lost",
      "noDecision",
      "stillOpen",
    ]);
    expect(parts.map((p) => p.value)).toEqual([30, 40, 10, 20]);
    const total = parts.reduce((s, p) => s + p.fraction, 0);
    expect(total).toBeCloseTo(1, 10);
    expect(parts[0].fraction).toBeCloseTo(0.3, 10);
  });

  it("yields zero fractions for an empty funnel instead of NaN", () => {
    const parts = funnelParts({
      opened: 0,
      won: 0,
      lost: 0,
      noDecision: 0,
      stillOpen: 0,
    });
    for (const p of parts) expect(p.fraction).toBe(0);
  });

  it("preserves fractional mean-across-paths counts", () => {
    const parts = funnelParts({
      opened: 10.5,
      won: 2.5,
      lost: 4,
      noDecision: 1,
      stillOpen: 3,
    });
    expect(parts[0].fraction).toBeCloseTo(2.5 / 10.5, 10);
  });
});

describe("win-rate tables", () => {
  it("sorts segment rows by opened volume, largest first", () => {
    const rows = segmentWinRates([segB, segA]);
    expect(rows.map((r) => r.id)).toEqual(["seg-a", "seg-b"]);
    expect(rows[0].winRate).toBe(0.375);
  });

  it("sorts channel rows including the implicit direct channel", () => {
    const rows = channelWinRates([
      { channelId: "direct", opened: 5, won: 2, winRate: 0.4 },
      { channelId: "chan-1", opened: 25, won: 10, winRate: 0.4 },
    ]);
    expect(rows.map((r) => r.id)).toEqual(["chan-1", "direct"]);
  });
});

describe("diagnosticsHeadline", () => {
  it("carries scalar diagnostics through with the summed funnel", () => {
    const d: EngineFirmLevelDiagnostics = {
      funnelBySegment: [segA, segB],
      funnelByChannel: [],
      avgSalesCycleMonths: 4.2,
      expansionCount: 7.5,
      meanUtilityGapLostDeals: 1.3,
      syntheticFirmsCreated: 2121,
    };
    const h = diagnosticsHeadline(d);
    expect(h.totals.opened).toBe(150);
    expect(h.avgSalesCycleMonths).toBe(4.2);
    expect(h.expansionCount).toBe(7.5);
    expect(h.syntheticFirmsCreated).toBe(2121);
  });
});

describe("formatting", () => {
  it("formats rates as one-decimal percentages", () => {
    expect(formatRate(0.375)).toBe("37.5%");
    expect(formatRate(0)).toBe("0.0%");
    expect(formatRate(Number.NaN)).toBe("—");
  });

  it("formats mean counts without false precision", () => {
    expect(formatMeanCount(7)).toBe("7");
    expect(formatMeanCount(7.526)).toBe("~7.5");
    expect(formatMeanCount(Number.NaN)).toBe("—");
  });

  it("prefixes non-integer means with a tilde to mark them as MC-path averages, not literal counts", () => {
    expect(formatMeanCount(0.1)).toBe("~0.1");
    expect(formatMeanCount(2.3)).toBe("~2.3");
    expect(formatMeanCount(0)).toBe("0");
    expect(formatMeanCount(3)).toBe("3");
    expect(formatMeanCount(-1.25)).toBe("~-1.3");
  });
});
