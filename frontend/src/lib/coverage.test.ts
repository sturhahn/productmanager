import { describe, expect, it } from "vitest";
import type { CoverageDto } from "./api/types";
import { buildCoverageMap, coverageKey, effectiveCoverage, resolveCoverage } from "./coverage";

const P = "product-1";
const B = "process-1";

function coverage(overrides: Partial<CoverageDto>): CoverageDto {
  return {
    id: "cov-1",
    productId: P,
    businessProcessId: B,
    approachId: null,
    clientSegmentId: null,
    tier: "Silver",
    isUnknown: false,
    confidence: 1,
    numericScore: null,
    effectiveDate: null,
    rationale: null,
    scaleLimits: null,
    ...overrides,
  };
}

describe("coverageKey", () => {
  it("treats null approach/segment as distinct from any id", () => {
    expect(coverageKey(P, B, null, null)).not.toBe(coverageKey(P, B, "a", null));
    expect(coverageKey(P, B, null, null)).not.toBe(coverageKey(P, B, null, "s"));
  });
});

describe("resolveCoverage", () => {
  it("returns the exact match when the key is generic (no approach/segment fallback needed)", () => {
    const generic = coverage({ id: "generic" });
    const map = buildCoverageMap([generic]);
    const resolved = resolveCoverage(map, P, B, null, null);
    expect(resolved.exact).toBe(generic);
    expect(resolved.inherited).toBeNull();
  });

  it("falls back to the generic assessment when a segment-scoped one is absent", () => {
    const generic = coverage({ id: "generic", tier: "Gold" });
    const map = buildCoverageMap([generic]);
    const resolved = resolveCoverage(map, P, B, null, "segment-1");
    expect(resolved.exact).toBeNull();
    expect(resolved.inherited).toBe(generic);
  });

  it("prefers the exact segment-scoped assessment over the generic one", () => {
    const generic = coverage({ id: "generic", tier: "Gold" });
    const scoped = coverage({
      id: "scoped",
      clientSegmentId: "segment-1",
      tier: "Bronze",
    });
    const map = buildCoverageMap([generic, scoped]);
    const resolved = resolveCoverage(map, P, B, null, "segment-1");
    expect(resolved.exact).toBe(scoped);
    expect(resolved.inherited).toBe(generic);
  });

  it("does not fall back when neither an exact nor a generic assessment exists", () => {
    const map = buildCoverageMap([]);
    const resolved = resolveCoverage(map, P, B, "approach-1", "segment-1");
    expect(resolved.exact).toBeNull();
    expect(resolved.inherited).toBeNull();
  });
});

describe("effectiveCoverage", () => {
  it("is the exact assessment when present", () => {
    const generic = coverage({ id: "generic", tier: "Gold" });
    const scoped = coverage({
      id: "scoped",
      approachId: "approach-1",
      tier: "Bronze",
    });
    const map = buildCoverageMap([generic, scoped]);
    expect(effectiveCoverage(map, P, B, "approach-1", null)).toBe(scoped);
  });

  it("is the inherited generic assessment when no exact one exists", () => {
    const generic = coverage({ id: "generic", tier: "Silver" });
    const map = buildCoverageMap([generic]);
    expect(effectiveCoverage(map, P, B, "approach-1", null)).toBe(generic);
  });

  it("is null when nothing at all has been assessed", () => {
    const map = buildCoverageMap([]);
    expect(effectiveCoverage(map, P, B, null, null)).toBeNull();
  });
});
