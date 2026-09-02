import { describe, expect, it } from "vitest";
import { runLabel } from "./runs";

describe("runLabel", () => {
  it("uses the run's name when set", () => {
    expect(
      runLabel({
        name: "Baseline vs. Q3 slip",
        id: "11111111-2222-3333-4444-555555555555",
        createdAt: "2026-08-31T14:02:00Z",
      }),
    ).toBe("Baseline vs. Q3 slip");
  });

  it("falls back to an absolute compact local timestamp for unnamed runs", () => {
    const label = runLabel({
      name: null,
      id: "11111111-2222-3333-4444-555555555555",
      createdAt: "2026-08-31T14:02:00Z",
    });
    // Never a truncated GUID.
    expect(label).not.toContain("11111111");
    // Formatted from createdAt: "Aug 31, HH:MM" in local time.
    expect(label).toMatch(/^Aug 31, \d{2}:\d{2}$/);
  });

  it("treats an empty-string name the same as unnamed", () => {
    const label = runLabel({
      name: "",
      id: "11111111-2222-3333-4444-555555555555",
      createdAt: "2026-08-31T14:02:00Z",
    });
    expect(label).toMatch(/^Aug 31, \d{2}:\d{2}$/);
  });
});
