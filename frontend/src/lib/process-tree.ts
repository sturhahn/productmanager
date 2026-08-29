import type { BusinessProcessDto } from "./api/types";

/**
 * Orders business processes depth-first so children render under their
 * parent, with the depth for indentation. Orphans (parent not in the list)
 * surface at the root rather than disappearing.
 */
export function flattenProcessTree(
  processes: BusinessProcessDto[],
): { process: BusinessProcessDto; depth: number }[] {
  const byParent = new Map<string | null, BusinessProcessDto[]>();
  for (const p of processes) {
    const key = p.parentProcessId;
    const list = byParent.get(key) ?? [];
    list.push(p);
    byParent.set(key, list);
  }
  const ids = new Set(processes.map((p) => p.id));
  const out: { process: BusinessProcessDto; depth: number }[] = [];
  const visit = (parentKey: string | null, depth: number) => {
    for (const p of byParent.get(parentKey) ?? []) {
      out.push({ process: p, depth });
      visit(p.id, depth + 1);
    }
  };
  visit(null, 0);
  for (const p of processes) {
    if (p.parentProcessId !== null && !ids.has(p.parentProcessId)) {
      out.push({ process: p, depth: 0 });
    }
  }
  return out;
}
