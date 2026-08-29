import { describe, expect, it } from "vitest";
import {
  addDays,
  barGeometry,
  computeRange,
  dateToX,
  daysBetween,
  daysInMonth,
  monthCells,
  packRows,
  quarterCells,
  resizeDates,
  shiftDates,
  xToDate,
} from "./timeline";

describe("day math", () => {
  it("computes whole-day differences across month and year boundaries", () => {
    expect(daysBetween("2026-01-01", "2026-01-31")).toBe(30);
    expect(daysBetween("2026-12-31", "2027-01-01")).toBe(1);
    expect(daysBetween("2026-03-01", "2026-02-28")).toBe(-1);
  });

  it("adds days through a leap year", () => {
    expect(addDays("2028-02-28", 1)).toBe("2028-02-29");
    expect(addDays("2028-02-29", 1)).toBe("2028-03-01");
    expect(daysInMonth(2028, 2)).toBe(29);
    expect(daysInMonth(2026, 2)).toBe(28);
  });
});

describe("pixel mapping", () => {
  it("round-trips date -> x -> date", () => {
    const rangeStart = "2026-01-01";
    const x = dateToX("2026-03-15", rangeStart);
    expect(xToDate(x, rangeStart)).toBe("2026-03-15");
  });

  it("snaps mid-pixel positions to the nearest day", () => {
    expect(xToDate(4 * 10 + 1, "2026-01-01", 4)).toBe("2026-01-11");
    expect(xToDate(4 * 10 + 3, "2026-01-01", 4)).toBe("2026-01-12");
  });

  it("makes bars end-inclusive", () => {
    const { left, width } = barGeometry("2026-01-01", "2026-01-01", "2026-01-01", 4);
    expect(left).toBe(0);
    expect(width).toBe(4); // one day wide, not zero
  });
});

describe("range and headers", () => {
  it("pads one month before and two after the items", () => {
    const range = computeRange(
      [{ start: "2026-03-10", end: "2026-06-20" }],
      "2026-04-01",
      6,
    );
    expect(range.start).toBe("2026-02-01");
    expect(range.endExclusive).toBe("2026-09-01");
  });

  it("enforces a minimum width when there are no items", () => {
    const range = computeRange([], "2026-08-29", 12);
    expect(range.start).toBe("2026-07-01");
    expect(range.endExclusive).toBe("2027-07-01");
  });

  it("builds month cells and groups them into quarters", () => {
    const months = monthCells("2026-11-01", "2027-03-01");
    expect(months.map((m) => m.label)).toEqual(["Nov", "Dec", "Jan", "Feb"]);
    const quarters = quarterCells(months);
    expect(quarters.map((q) => q.label)).toEqual(["Q4 2026", "Q1 2027"]);
    expect(quarters[0].monthCount).toBe(2);
    expect(quarters[1].days).toBe(31 + 28);
  });
});

describe("drag operations", () => {
  it("shifts both ends when moving a bar", () => {
    expect(shiftDates("2026-01-10", "2026-02-10", 7)).toEqual({
      start: "2026-01-17",
      end: "2026-02-17",
    });
  });

  it("clamps resize so a bar never inverts", () => {
    expect(resizeDates("2026-01-10", "2026-01-20", "end", -30)).toEqual({
      start: "2026-01-10",
      end: "2026-01-10",
    });
    expect(resizeDates("2026-01-10", "2026-01-20", "start", 30)).toEqual({
      start: "2026-01-20",
      end: "2026-01-20",
    });
  });
});

describe("row packing", () => {
  it("stacks overlapping items and reuses freed rows", () => {
    const packed = packRows([
      { id: "a", start: "2026-01-01", end: "2026-01-31" },
      { id: "b", start: "2026-01-15", end: "2026-02-15" },
      { id: "c", start: "2026-02-01", end: "2026-02-28" },
    ]);
    const byId = Object.fromEntries(packed.map((p) => [p.id, p.row]));
    expect(byId.a).toBe(0);
    expect(byId.b).toBe(1); // overlaps a
    expect(byId.c).toBe(0); // a has ended, row 0 is free
  });
});
