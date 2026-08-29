"use client";

import type { CoverageTier } from "@/lib/api/types";
import { cn } from "@/lib/utils";

export const TIER_LABELS: Record<CoverageTier, string> = {
  Gold: "Gold",
  Silver: "Silver",
  Bronze: "Bronze",
  NotPlaying: "Not playing",
};

const TIER_CLASSES: Record<CoverageTier, string> = {
  Gold: "bg-tier-gold text-tier-gold-foreground",
  Silver: "bg-tier-silver text-tier-silver-foreground",
  Bronze: "bg-tier-bronze text-tier-bronze-foreground",
  NotPlaying:
    "border border-dashed border-border bg-transparent text-muted-foreground/70",
};

/**
 * A coverage tier chip. `unknown` renders the explicit "we don't know" state
 * (distinct from NotPlaying and from Bronze). `inherited` dims a value that
 * falls back from a generic assessment under a segment/approach filter.
 */
export function TierChip({
  tier,
  unknown = false,
  confidence,
  inherited = false,
  size = "md",
  className,
}: {
  tier: CoverageTier | null;
  unknown?: boolean;
  confidence?: number;
  inherited?: boolean;
  size?: "sm" | "md";
  className?: string;
}) {
  const base = cn(
    "inline-flex items-center gap-1 rounded-md font-medium whitespace-nowrap select-none",
    size === "sm" ? "px-1.5 py-0.5 text-[11px]" : "px-2 py-0.5 text-xs",
    inherited && "opacity-55",
    className,
  );

  if (unknown) {
    return (
      <span
        className={cn(
          base,
          "border border-dashed border-border bg-muted/40 text-muted-foreground",
        )}
      >
        Unknown
      </span>
    );
  }
  if (tier === null) {
    return (
      <span className={cn(base, "text-muted-foreground/50")} aria-label="Unassessed">
        —
      </span>
    );
  }
  return (
    <span className={cn(base, TIER_CLASSES[tier])}>
      {TIER_LABELS[tier]}
      {confidence !== undefined && confidence < 1 && (
        <span className="font-mono text-[10px] opacity-75">
          {Math.round(confidence * 100)}%
        </span>
      )}
    </span>
  );
}
