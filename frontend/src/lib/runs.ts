/**
 * Pure display logic for simulation runs. No React — unit-testable and
 * shared across the Simulate page's runs table, Compare pickers, and
 * results header.
 */

/** The minimal shape runLabel needs — a subset of SimulationRunDto. */
export interface RunLabelInput {
  name: string | null;
  id: string;
  createdAt: string;
}

/**
 * A named run displays its name. An unnamed run falls back to an absolute
 * compact local timestamp (e.g. "Aug 31, 14:02") — never a truncated GUID,
 * which reads as meaningless noise once a workspace has more than a
 * handful of runs.
 */
export function runLabel(run: RunLabelInput): string {
  if (run.name) return run.name;
  const d = new Date(run.createdAt);
  const datePart = d.toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
  });
  const timePart = d.toLocaleTimeString(undefined, {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
  return `${datePart}, ${timePart}`;
}
