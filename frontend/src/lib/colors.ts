/**
 * Categorical hues for initiatives: stable per position in the workspace's
 * initiative list, cycling through the chart tokens (theme-aware CSS vars).
 */

const HUES = [
  "var(--chart-1)",
  "var(--chart-2)",
  "var(--chart-3)",
  "var(--chart-4)",
  "var(--chart-5)",
] as const;

export function initiativeHue(index: number): string {
  return HUES[((index % HUES.length) + HUES.length) % HUES.length];
}
