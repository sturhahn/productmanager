/**
 * Masked coverage deltas — the client-side mirror of the simulation's
 * attractiveness schedule (see SimulationInputBuilder.BuildAttractivenessSchedule),
 * which computes, per (businessProcess, segment):
 *
 *   ourTier = max over ALL our products of max(currently assessed tier,
 *                                               best planned delta's toTier)
 *
 * The simulation does not attribute a delta's impact to the delivering
 * feature's own product — it just raises the ceiling across every product we
 * own (world.OurProductIds). A delta is masked (has zero simulation impact
 * right now) when the tier it targets is already met or beaten by the best
 * non-Unknown assessment across all of our products at that
 * (businessProcess, approach?, segment?) key — resolved with the same
 * exact-then-generic fallback the grid uses.
 *
 * - Only `Coverage`-kind deltas can be masked; `SwitchingCost`/`Other`
 *   deltas don't carry a toTier and always have potential impact.
 * - No non-Unknown assessment for any of our products at the key => not
 *   masked (nothing to dominate it).
 * - An explicit "Unknown" assessment never counts toward the best tier — it
 *   asserts nothing about the tier, so it can't be presumed to already meet
 *   or beat the delta's target.
 * - Which specific product the feature happens to belong to is irrelevant:
 *   the simulation never keys off it.
 */

import type { CoverageDto, CoverageTier, FeatureDeltaDto, FeatureDto } from "./api/types";
import { effectiveCoverage } from "./coverage";

/** NotPlaying < Bronze < Silver < Gold. */
export const TIER_RANK: Record<CoverageTier, number> = {
  NotPlaying: 0,
  Bronze: 1,
  Silver: 2,
  Gold: 3,
};

export function tierRank(tier: CoverageTier): number {
  return TIER_RANK[tier];
}

/**
 * The best non-Unknown effective assessed tier across all of our products at
 * a (businessProcessId, approachId?, segmentId?) key — the same aggregation
 * the simulation performs before planned deltas are layered on. Returns
 * `null` when none of our products has a non-Unknown assessment there.
 */
export function bestOurAssessedTier(
  ourProductIds: readonly string[],
  businessProcessId: string,
  approachId: string | null,
  clientSegmentId: string | null,
  coverageMap: Map<string, CoverageDto>,
): CoverageTier | null {
  let best: CoverageTier | null = null;
  for (const productId of ourProductIds) {
    const assessed = effectiveCoverage(
      coverageMap,
      productId,
      businessProcessId,
      approachId,
      clientSegmentId,
    );
    if (assessed === null || assessed.isUnknown) continue;
    if (best === null || tierRank(assessed.tier) > tierRank(best)) {
      best = assessed.tier;
    }
  }
  return best;
}

/**
 * True when `delta` currently has zero simulation impact: the best
 * non-Unknown assessment across all of our products at the same
 * (process, approach?, segment?) key is already >= the delta's toTier.
 *
 * - Only `Coverage`-kind deltas can be masked; `SwitchingCost`/`Other`
 *   deltas don't carry a toTier and always have potential impact.
 * - No assessment across our products at the key => not masked (nothing to
 *   dominate it).
 * - An explicit "Unknown" assessment never masks — it never contributes to
 *   the best tier.
 */
export function isDeltaMasked(
  delta: FeatureDeltaDto,
  ourProductIds: readonly string[],
  coverageMap: Map<string, CoverageDto>,
): boolean {
  if (delta.kind !== "Coverage") return false;
  if (delta.toTier === null || delta.businessProcessId === null) return false;
  if (ourProductIds.length === 0) return false;

  const best = bestOurAssessedTier(
    ourProductIds,
    delta.businessProcessId,
    delta.approachId,
    delta.segmentId,
    coverageMap,
  );
  if (best === null) return false;

  return tierRank(delta.toTier) <= tierRank(best);
}

export interface FeatureMaskInfo {
  /** Deltas of kind "Coverage" — the only kind this rule can mask. */
  maskable: number;
  /** How many of those are currently masked. */
  masked: number;
  /**
   * True when the feature has at least one delta and every one of them is
   * masked — shipping (or delaying) this feature has zero simulation
   * impact right now.
   */
  allMasked: boolean;
}

/** Summarizes masking across all of a feature's deltas. */
export function featureMaskInfo(
  feature: Pick<FeatureDto, "deltas">,
  ourProductIds: readonly string[],
  coverageMap: Map<string, CoverageDto>,
): FeatureMaskInfo {
  const maskableDeltas = feature.deltas.filter((d) => d.kind === "Coverage");
  const maskedCount = maskableDeltas.filter((d) =>
    isDeltaMasked(d, ourProductIds, coverageMap),
  ).length;
  return {
    maskable: maskableDeltas.length,
    masked: maskedCount,
    allMasked:
      feature.deltas.length > 0 &&
      maskableDeltas.length === feature.deltas.length &&
      maskedCount === maskableDeltas.length,
  };
}
