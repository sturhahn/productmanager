/**
 * Effective-coverage resolution — the single place that decides which
 * assessment applies at a (product, business process, approach, segment)
 * key. Shared by the Coverage grid (what to show in a cell) and the
 * masking computation (what a feature's delta is competing against).
 */

import type { CoverageDto } from "./api/types";

/** Composite key for a coverage assessment's 4-part identity. */
export function coverageKey(
  productId: string,
  businessProcessId: string,
  approachId: string | null,
  clientSegmentId: string | null,
): string {
  return `${productId}|${businessProcessId}|${approachId ?? ""}|${clientSegmentId ?? ""}`;
}

/** Indexes coverage rows by their full 4-part key for O(1) lookup. */
export function buildCoverageMap(
  coverages: CoverageDto[],
): Map<string, CoverageDto> {
  const map = new Map<string, CoverageDto>();
  for (const c of coverages) {
    map.set(
      coverageKey(c.productId, c.businessProcessId, c.approachId, c.clientSegmentId),
      c,
    );
  }
  return map;
}

export interface ResolvedCoverage {
  /** The row matching the exact (product, process, approach, segment) key, if assessed. */
  exact: CoverageDto | null;
  /**
   * The generic (approach=null, segment=null) fallback the key inherits
   * from. Only ever populated when the requested key itself scopes to a
   * specific approach and/or segment — a generic request has no further
   * fallback.
   */
  inherited: CoverageDto | null;
}

/**
 * Resolves the coverage assessment(s) that apply at a (product, process,
 * approach, segment) key: an exact assessment at that key, and — when the
 * key is approach/segment-specific — the generic assessment it falls back
 * to when no exact one exists. This is the same exact-then-generic rule
 * the simulation engine uses to find the "currently assessed" tier.
 */
export function resolveCoverage(
  map: Map<string, CoverageDto>,
  productId: string,
  businessProcessId: string,
  approachId: string | null,
  clientSegmentId: string | null,
): ResolvedCoverage {
  const exact =
    map.get(coverageKey(productId, businessProcessId, approachId, clientSegmentId)) ??
    null;
  const inherited =
    approachId === null && clientSegmentId === null
      ? null
      : (map.get(coverageKey(productId, businessProcessId, null, null)) ?? null);
  return { exact, inherited };
}

/**
 * The coverage assessment that actually applies at a key: the exact
 * assessment if one exists, otherwise the generic fallback.
 */
export function effectiveCoverage(
  map: Map<string, CoverageDto>,
  productId: string,
  businessProcessId: string,
  approachId: string | null,
  clientSegmentId: string | null,
): CoverageDto | null {
  const { exact, inherited } = resolveCoverage(
    map,
    productId,
    businessProcessId,
    approachId,
    clientSegmentId,
  );
  return exact ?? inherited;
}
