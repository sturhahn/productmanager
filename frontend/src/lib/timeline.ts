/**
 * Pure date/geometry math for the roadmap timeline.
 * All dates are ISO "yyyy-MM-dd" strings (the API's DateOnly wire format);
 * internally we count whole days from an epoch, so there is no DST drift.
 */

export const DAY_PX = 4;

const MS_PER_DAY = 86_400_000;

/** Parse "yyyy-MM-dd" into a UTC timestamp (ms). */
export function parseIso(date: string): number {
  const [y, m, d] = date.split("-").map(Number);
  return Date.UTC(y, m - 1, d);
}

/** Format a UTC timestamp (ms) as "yyyy-MM-dd". */
export function toIso(ms: number): string {
  const d = new Date(ms);
  const y = d.getUTCFullYear().toString().padStart(4, "0");
  const m = (d.getUTCMonth() + 1).toString().padStart(2, "0");
  const day = d.getUTCDate().toString().padStart(2, "0");
  return `${y}-${m}-${day}`;
}

/** Whole days from `a` to `b` (positive when b is later). */
export function daysBetween(a: string, b: string): number {
  return Math.round((parseIso(b) - parseIso(a)) / MS_PER_DAY);
}

export function addDays(date: string, days: number): string {
  return toIso(parseIso(date) + days * MS_PER_DAY);
}

export function todayIso(): string {
  const now = new Date();
  return toIso(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()));
}

export interface MonthCell {
  year: number;
  month: number; // 1-12
  /** First day of the month, ISO. */
  start: string;
  days: number;
  label: string;
}

export interface QuarterCell {
  year: number;
  quarter: number; // 1-4
  label: string;
  /** Number of month cells this quarter spans within the visible range. */
  monthCount: number;
  days: number;
}

const MONTH_LABELS = [
  "Jan",
  "Feb",
  "Mar",
  "Apr",
  "May",
  "Jun",
  "Jul",
  "Aug",
  "Sep",
  "Oct",
  "Nov",
  "Dec",
];

export function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

export function startOfMonth(date: string): string {
  const [y, m] = date.split("-").map(Number);
  return `${y.toString().padStart(4, "0")}-${m.toString().padStart(2, "0")}-01`;
}

function addMonths(year: number, month: number, delta: number) {
  const idx = year * 12 + (month - 1) + delta;
  return { year: Math.floor(idx / 12), month: (idx % 12) + 1 };
}

/**
 * The visible window for a roadmap: from one month before the earliest item
 * (or today) to two months after the latest, at least `minMonths` wide.
 */
export function computeRange(
  itemDates: { start: string; end: string }[],
  today: string = todayIso(),
  minMonths = 12,
): { start: string; endExclusive: string } {
  let min = today;
  let max = today;
  for (const item of itemDates) {
    if (parseIso(item.start) < parseIso(min)) min = item.start;
    if (parseIso(item.end) > parseIso(max)) max = item.end;
  }
  const [minY, minM] = startOfMonth(min).split("-").map(Number);
  const from = addMonths(minY, minM, -1);
  const [maxY, maxM] = startOfMonth(max).split("-").map(Number);
  let to = addMonths(maxY, maxM, 3); // end month + 2 full months, exclusive
  const span = to.year * 12 + to.month - (from.year * 12 + from.month);
  if (span < minMonths) to = addMonths(from.year, from.month, minMonths);
  const iso = (p: { year: number; month: number }) =>
    `${p.year.toString().padStart(4, "0")}-${p.month.toString().padStart(2, "0")}-01`;
  return { start: iso(from), endExclusive: iso(to) };
}

export function monthCells(start: string, endExclusive: string): MonthCell[] {
  const cells: MonthCell[] = [];
  let [y, m] = start.split("-").map(Number);
  const endMs = parseIso(endExclusive);
  while (Date.UTC(y, m - 1, 1) < endMs) {
    cells.push({
      year: y,
      month: m,
      start: `${y.toString().padStart(4, "0")}-${m.toString().padStart(2, "0")}-01`,
      days: daysInMonth(y, m),
      label: MONTH_LABELS[m - 1],
    });
    ({ year: y, month: m } = addMonths(y, m, 1));
  }
  return cells;
}

export function quarterCells(months: MonthCell[]): QuarterCell[] {
  const quarters: QuarterCell[] = [];
  for (const cell of months) {
    const quarter = Math.floor((cell.month - 1) / 3) + 1;
    const last = quarters[quarters.length - 1];
    if (last && last.year === cell.year && last.quarter === quarter) {
      last.monthCount += 1;
      last.days += cell.days;
    } else {
      quarters.push({
        year: cell.year,
        quarter,
        label: `Q${quarter} ${cell.year}`,
        monthCount: 1,
        days: cell.days,
      });
    }
  }
  return quarters;
}

/** X pixel offset of a date within the range. */
export function dateToX(date: string, rangeStart: string, dayPx = DAY_PX): number {
  return daysBetween(rangeStart, date) * dayPx;
}

/** Snap an x offset back to the nearest day's date. */
export function xToDate(x: number, rangeStart: string, dayPx = DAY_PX): string {
  return addDays(rangeStart, Math.round(x / dayPx));
}

/** Bar geometry for an item; the end date is inclusive. */
export function barGeometry(
  start: string,
  end: string,
  rangeStart: string,
  dayPx = DAY_PX,
): { left: number; width: number } {
  const left = dateToX(start, rangeStart, dayPx);
  const width = (daysBetween(start, end) + 1) * dayPx;
  return { left, width };
}

/** Shift both dates of a bar by a whole number of days. */
export function shiftDates(
  start: string,
  end: string,
  deltaDays: number,
): { start: string; end: string } {
  return { start: addDays(start, deltaDays), end: addDays(end, deltaDays) };
}

/** Resize one edge, never letting the bar collapse below one day. */
export function resizeDates(
  start: string,
  end: string,
  edge: "start" | "end",
  deltaDays: number,
): { start: string; end: string } {
  if (edge === "start") {
    let next = addDays(start, deltaDays);
    if (parseIso(next) > parseIso(end)) next = end;
    return { start: next, end };
  }
  let next = addDays(end, deltaDays);
  if (parseIso(next) < parseIso(start)) next = start;
  return { start, end: next };
}

/**
 * Pack items into stacked rows within a lane: items that overlap in time get
 * different rows (greedy, by start date).
 */
export function packRows<T extends { start: string; end: string }>(
  items: T[],
): (T & { row: number })[] {
  const sorted = [...items].sort(
    (a, b) => parseIso(a.start) - parseIso(b.start) || parseIso(a.end) - parseIso(b.end),
  );
  const rowEnds: number[] = [];
  return sorted.map((item) => {
    const startMs = parseIso(item.start);
    let row = rowEnds.findIndex((end) => end < startMs);
    if (row === -1) {
      row = rowEnds.length;
      rowEnds.push(parseIso(item.end));
    } else {
      rowEnds[row] = parseIso(item.end);
    }
    return { ...item, row };
  });
}

/** "2026-08" style key for a capacity-warning period. */
export function periodKey(year: number, month: number): string {
  return `${year.toString().padStart(4, "0")}-${month.toString().padStart(2, "0")}`;
}

export function formatPeriod(year: number, month: number): string {
  return `${MONTH_LABELS[month - 1]} ${year}`;
}

export function formatDate(date: string | null | undefined): string {
  if (!date) return "—";
  const [y, m, d] = date.split("-").map(Number);
  return `${MONTH_LABELS[m - 1]} ${d}, ${y}`;
}
