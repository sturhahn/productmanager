import { describe, expect, it } from "vitest";
import type { CoverageDto, FeatureDeltaDto, FeatureDto } from "./api/types";
import { buildCoverageMap } from "./coverage";
import { bestOurAssessedTier, featureMaskInfo, isDeltaMasked, tierRank } from "./masking";

const PRODUCT = "product-1";
const OTHER_OUR_PRODUCT = "product-2";
const COMPETITOR_PRODUCT = "competitor-product-1";
const PROCESS = "process-1";
const SEGMENT = "segment-1";

function coverage(overrides: Partial<CoverageDto>): CoverageDto {
  return {
    id: "cov-1",
    productId: PRODUCT,
    businessProcessId: PROCESS,
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

function delta(overrides: Partial<FeatureDeltaDto>): FeatureDeltaDto {
  return {
    kind: "Coverage",
    businessProcessId: PROCESS,
    fromTier: "Bronze",
    toTier: "Gold",
    approachId: null,
    segmentId: null,
    magnitude: null,
    rationale: null,
    ...overrides,
  };
}

describe("tierRank", () => {
  it("orders NotPlaying < Bronze < Silver < Gold", () => {
    expect(tierRank("NotPlaying")).toBeLessThan(tierRank("Bronze"));
    expect(tierRank("Bronze")).toBeLessThan(tierRank("Silver"));
    expect(tierRank("Silver")).toBeLessThan(tierRank("Gold"));
  });
});

describe("bestOurAssessedTier", () => {
  it("returns null when none of our products has an assessment", () => {
    const map = buildCoverageMap([]);
    expect(
      bestOurAssessedTier([PRODUCT], PROCESS, null, null, map),
    ).toBeNull();
  });

  it("takes the max across all of our products, not just one", () => {
    const map = buildCoverageMap([
      coverage({ productId: PRODUCT, tier: "Bronze" }),
      coverage({ id: "cov-2", productId: OTHER_OUR_PRODUCT, tier: "Gold" }),
    ]);
    expect(
      bestOurAssessedTier([PRODUCT, OTHER_OUR_PRODUCT], PROCESS, null, null, map),
    ).toBe("Gold");
  });

  it("ignores Unknown assessments when computing the max", () => {
    const map = buildCoverageMap([
      coverage({ productId: PRODUCT, tier: "Gold", isUnknown: true }),
      coverage({ id: "cov-2", productId: OTHER_OUR_PRODUCT, tier: "Bronze" }),
    ]);
    expect(
      bestOurAssessedTier([PRODUCT, OTHER_OUR_PRODUCT], PROCESS, null, null, map),
    ).toBe("Bronze");
  });

  it("never considers a competitor product even if passed a coverage map containing it", () => {
    const map = buildCoverageMap([
      coverage({ productId: COMPETITOR_PRODUCT, tier: "Gold" }),
    ]);
    expect(
      bestOurAssessedTier([PRODUCT], PROCESS, null, null, map),
    ).toBeNull();
  });
});

describe("isDeltaMasked", () => {
  it("masks a delta whose toTier is already met by the generic assessment", () => {
    const map = buildCoverageMap([coverage({ tier: "Gold" })]);
    const d = delta({ toTier: "Silver" });
    expect(isDeltaMasked(d, [PRODUCT], map)).toBe(true);
  });

  it("masks a delta whose toTier exactly equals the assessed tier", () => {
    const map = buildCoverageMap([coverage({ tier: "Silver" })]);
    const d = delta({ toTier: "Silver" });
    expect(isDeltaMasked(d, [PRODUCT], map)).toBe(true);
  });

  it("does not mask when the delta's toTier exceeds the assessed tier", () => {
    const map = buildCoverageMap([coverage({ tier: "Bronze" })]);
    const d = delta({ toTier: "Gold" });
    expect(isDeltaMasked(d, [PRODUCT], map)).toBe(false);
  });

  it("does not mask when there is no assessment at all", () => {
    const map = buildCoverageMap([]);
    const d = delta({ toTier: "Bronze" });
    expect(isDeltaMasked(d, [PRODUCT], map)).toBe(false);
  });

  it("treats an Unknown assessment as no assessment — never masks", () => {
    const map = buildCoverageMap([
      coverage({ tier: "Gold", isUnknown: true }),
    ]);
    const d = delta({ toTier: "Bronze" });
    expect(isDeltaMasked(d, [PRODUCT], map)).toBe(false);
  });

  it("a segment-scoped delta checks the segment-scoped assessment, not the generic one", () => {
    // Generic assessment is only Bronze, but this segment has been
    // specifically assessed at Gold — the segment-scoped delta is masked
    // even though a generic-only lookup would say it isn't.
    const generic = coverage({ tier: "Bronze" });
    const scoped = coverage({
      id: "cov-scoped",
      clientSegmentId: SEGMENT,
      tier: "Gold",
    });
    const map = buildCoverageMap([generic, scoped]);
    const d = delta({ toTier: "Silver", segmentId: SEGMENT });
    expect(isDeltaMasked(d, [PRODUCT], map)).toBe(true);
  });

  it("a segment-scoped delta falls back to the generic assessment when no scoped one exists", () => {
    const generic = coverage({ tier: "Gold" });
    const map = buildCoverageMap([generic]);
    const d = delta({ toTier: "Silver", segmentId: SEGMENT });
    expect(isDeltaMasked(d, [PRODUCT], map)).toBe(true);
  });

  it("does not mask non-Coverage delta kinds", () => {
    const map = buildCoverageMap([coverage({ tier: "Gold" })]);
    const d = delta({ kind: "SwitchingCost", toTier: null, businessProcessId: null });
    expect(isDeltaMasked(d, [PRODUCT], map)).toBe(false);
  });

  it("does not mask when there are no products of ours", () => {
    const map = buildCoverageMap([coverage({ tier: "Gold" })]);
    const d = delta({ toTier: "Bronze" });
    expect(isDeltaMasked(d, [], map)).toBe(false);
  });

  it("masks based on the best assessment across all our products, regardless of which product the feature belongs to", () => {
    // Only OTHER_OUR_PRODUCT has an assessment that dominates the delta; the
    // simulation still masks it because it maxes over every product we own,
    // not just the one the feature happens to be tagged with.
    const map = buildCoverageMap([
      coverage({ productId: OTHER_OUR_PRODUCT, tier: "Gold" }),
    ]);
    const d = delta({ toTier: "Silver" });
    expect(isDeltaMasked(d, [PRODUCT, OTHER_OUR_PRODUCT], map)).toBe(true);
  });

  it("never masks based on a competitor's coverage", () => {
    const map = buildCoverageMap([
      coverage({ productId: COMPETITOR_PRODUCT, tier: "Gold" }),
    ]);
    const d = delta({ toTier: "Bronze" });
    // COMPETITOR_PRODUCT is deliberately omitted from ourProductIds — a
    // caller would never include a competitor there, and its coverage must
    // not leak into the "ours" aggregation even if it appeared in the map.
    expect(isDeltaMasked(d, [PRODUCT], map)).toBe(false);
  });
});

describe("featureMaskInfo", () => {
  const baseFeature: Pick<FeatureDto, "deltas"> = {
    deltas: [],
  };

  it("reports allMasked only when every delta is masked and there is at least one", () => {
    const map = buildCoverageMap([coverage({ tier: "Gold" })]);
    const info = featureMaskInfo(
      { ...baseFeature, deltas: [delta({ toTier: "Silver" }), delta({ toTier: "Bronze" })] },
      [PRODUCT],
      map,
    );
    expect(info.maskable).toBe(2);
    expect(info.masked).toBe(2);
    expect(info.allMasked).toBe(true);
  });

  it("is not allMasked when only some deltas are masked", () => {
    const map = buildCoverageMap([coverage({ tier: "Silver" })]);
    const info = featureMaskInfo(
      { ...baseFeature, deltas: [delta({ toTier: "Bronze" }), delta({ toTier: "Gold" })] },
      [PRODUCT],
      map,
    );
    expect(info.masked).toBe(1);
    expect(info.allMasked).toBe(false);
  });

  it("is not allMasked when the feature has no deltas", () => {
    const map = buildCoverageMap([]);
    const info = featureMaskInfo({ ...baseFeature, deltas: [] }, [PRODUCT], map);
    expect(info.allMasked).toBe(false);
  });

  it("is not allMasked when a non-maskable delta kind is present, even if all Coverage deltas are masked", () => {
    const map = buildCoverageMap([coverage({ tier: "Gold" })]);
    const info = featureMaskInfo(
      {
        ...baseFeature,
        deltas: [
          delta({ toTier: "Bronze" }),
          delta({ kind: "SwitchingCost", toTier: null, businessProcessId: null }),
        ],
      },
      [PRODUCT],
      map,
    );
    expect(info.maskable).toBe(1);
    expect(info.masked).toBe(1);
    expect(info.allMasked).toBe(false);
  });

  it("masking is independent of which product the feature is tagged with — it aggregates across all our products", () => {
    const map = buildCoverageMap([
      coverage({ productId: OTHER_OUR_PRODUCT, tier: "Gold" }),
    ]);
    const info = featureMaskInfo(
      { ...baseFeature, deltas: [delta({ toTier: "Silver" })] },
      [PRODUCT, OTHER_OUR_PRODUCT],
      map,
    );
    expect(info.masked).toBe(1);
    expect(info.allMasked).toBe(true);
  });
});
