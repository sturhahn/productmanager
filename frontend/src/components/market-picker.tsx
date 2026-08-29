"use client";

import { GlobeIcon } from "lucide-react";
import type { MarketDto } from "@/lib/api/types";
import { setActiveMarket } from "@/lib/queries";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

/**
 * The market context selector shared by every Market screen. The choice is
 * persisted per workspace, so Segments, Products, Coverage… all stay on the
 * same market as you move between them.
 */
export function MarketPicker({
  workspaceId,
  markets,
  market,
}: {
  workspaceId: string;
  markets: MarketDto[];
  market: MarketDto | null;
}) {
  if (markets.length === 0) return null;
  return (
    <Select
      items={markets.map((m) => ({ label: m.name, value: m.id }))}
      value={market?.id ?? null}
      onValueChange={(v) => v && setActiveMarket(workspaceId, v as string)}
    >
      <SelectTrigger className="min-w-44" aria-label="Market">
        <GlobeIcon className="size-3.5 text-muted-foreground" />
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {markets.map((m) => (
          <SelectItem key={m.id} value={m.id}>
            {m.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
