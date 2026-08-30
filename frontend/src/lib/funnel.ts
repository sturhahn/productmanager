/**
 * Pure shaping logic for the firm-level engine's opportunity-funnel
 * diagnostics. No React, no echarts — unit-testable math that the Simulate
 * screen renders as stacked bars and win-rate tables.
 */

import type {
  EngineChannelFunnel,
  EngineFirmLevelDiagnostics,
  EngineSegmentFunnel,
} from "./api/types";

// ---------------------------------------------------------------------
// Totals across segments
// ---------------------------------------------------------------------

export interface FunnelTotals {
  opened: number;
  qualified: number;
  won: number;
  lost: number;
  noDecision: number;
  stillOpen: number;
  /** won / decided (won + lost + noDecision); 0 when nothing decided. */
  winRate: number;
}

export function funnelTotals(segments: EngineSegmentFunnel[]): FunnelTotals {
  const t = {
    opened: 0,
    qualified: 0,
    won: 0,
    lost: 0,
    noDecision: 0,
    stillOpen: 0,
  };
  for (const s of segments) {
    t.opened += s.opened;
    t.qualified += s.qualified;
    t.won += s.won;
    t.lost += s.lost;
    t.noDecision += s.noDecision;
    t.stillOpen += s.stillOpen;
  }
  const decided = t.won + t.lost + t.noDecision;
  return { ...t, winRate: decided === 0 ? 0 : t.won / decided };
}

// ---------------------------------------------------------------------
// Outcome bar segments
// ---------------------------------------------------------------------

export type FunnelOutcomeKey = "won" | "lost" | "noDecision" | "stillOpen";

export const FUNNEL_OUTCOME_LABELS: Record<FunnelOutcomeKey, string> = {
  won: "Won",
  lost: "Lost",
  noDecision: "No decision",
  stillOpen: "Still open",
};

export interface FunnelPart {
  key: FunnelOutcomeKey;
  value: number;
  /** Fraction of opened; 0 when nothing opened. Parts sum to 1 (funnel conservation). */
  fraction: number;
}

/**
 * Splits an opened count into its four exhaustive outcomes. The engine
 * guarantees opened == won + lost + noDecision + stillOpen per path, so the
 * fractions of a non-empty funnel sum to 1.
 */
export function funnelParts(f: {
  opened: number;
  won: number;
  lost: number;
  noDecision: number;
  stillOpen: number;
}): FunnelPart[] {
  const keys: FunnelOutcomeKey[] = ["won", "lost", "noDecision", "stillOpen"];
  return keys.map((key) => ({
    key,
    value: f[key],
    fraction: f.opened === 0 ? 0 : f[key] / f.opened,
  }));
}

// ---------------------------------------------------------------------
// Win-rate tables
// ---------------------------------------------------------------------

export interface WinRateRow {
  id: string;
  opened: number;
  won: number;
  winRate: number;
}

/** Segment rows sorted by opened volume, largest first. */
export function segmentWinRates(
  segments: EngineSegmentFunnel[],
): WinRateRow[] {
  return [...segments]
    .sort((a, b) => b.opened - a.opened)
    .map((s) => ({
      id: s.segmentId,
      opened: s.opened,
      won: s.won,
      winRate: s.winRate,
    }));
}

/** Channel rows sorted by opened volume, largest first ("direct" included). */
export function channelWinRates(
  channels: EngineChannelFunnel[],
): WinRateRow[] {
  return [...channels]
    .sort((a, b) => b.opened - a.opened)
    .map((c) => ({
      id: c.channelId,
      opened: c.opened,
      won: c.won,
      winRate: c.winRate,
    }));
}

// ---------------------------------------------------------------------
// Headline stats
// ---------------------------------------------------------------------

export interface DiagnosticsHeadline {
  totals: FunnelTotals;
  avgSalesCycleMonths: number;
  expansionCount: number;
  meanUtilityGapLostDeals: number;
  syntheticFirmsCreated: number;
}

export function diagnosticsHeadline(
  d: EngineFirmLevelDiagnostics,
): DiagnosticsHeadline {
  return {
    totals: funnelTotals(d.funnelBySegment),
    avgSalesCycleMonths: d.avgSalesCycleMonths,
    expansionCount: d.expansionCount,
    meanUtilityGapLostDeals: d.meanUtilityGapLostDeals,
    syntheticFirmsCreated: d.syntheticFirmsCreated,
  };
}

/** "42.3%" for tables; one decimal keeps mean-across-paths honest. */
export function formatRate(rate: number): string {
  if (!Number.isFinite(rate)) return "—";
  return `${(rate * 100).toFixed(1)}%`;
}

/** Mean counts across MC paths — one decimal, no false precision. */
export function formatMeanCount(value: number): string {
  if (!Number.isFinite(value)) return "—";
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}
