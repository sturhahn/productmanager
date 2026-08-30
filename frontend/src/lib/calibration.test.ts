import { describe, expect, it } from "vitest";
import {
  diffSummary,
  formatMeanSd,
  formatParamValue,
  formatRelativeShift,
  shapeDiffRows,
  shiftMagnitude,
  toDiffRow,
} from "./calibration";
import type {
  CalibrationDiffDto,
  CalibrationParameterDiffDto,
} from "./api/types";

function fitted(
  overrides: Partial<CalibrationParameterDiffDto>,
): CalibrationParameterDiffDto {
  return {
    key: "win-rate",
    clientSegmentId: "seg-1",
    flowKind: "WinRate",
    moduleProductId: null,
    productId: null,
    prior: { type: "Beta", p1: 2, p2: 8 },
    posterior: { type: "Beta", p1: 12, p2: 28 },
    priorMean: 0.2,
    posteriorMean: 0.3,
    priorStdDev: 0.12,
    posteriorStdDev: 0.07,
    shift: 0.1,
    sampleSize: 40,
    rationale: "fitted from 40 decided opportunities",
    ...overrides,
  };
}

function diff(
  fittedRows: CalibrationParameterDiffDto[],
  unfittedCount = 0,
): CalibrationDiffDto {
  return {
    calibrationRunId: "run-1",
    asOfDate: "2026-08-01",
    priorAssumptionSetId: "prior-1",
    fittedAssumptionSetId: "fitted-1",
    fitted: fittedRows,
    unfitted: Array.from({ length: unfittedCount }, (_, i) => ({
      key: `untouched-${i}`,
      clientSegmentId: null,
      flowKind: null,
      moduleProductId: null,
      productId: null,
      distribution: { type: "Point" as const, p1: 1, p2: 0 },
      reason: "no matching facts",
    })),
  };
}

describe("toDiffRow", () => {
  it("derives direction and relative shift from the means", () => {
    const row = toDiffRow(fitted({}));
    expect(row.direction).toBe("up");
    expect(row.relativeShift).toBeCloseTo(0.5, 10);
    expect(row.magnitude).toBe("large");
  });

  it("handles downward shifts against a negative prior magnitude correctly", () => {
    const row = toDiffRow(
      fitted({ priorMean: -2, posteriorMean: -2.1, shift: -0.1 }),
    );
    expect(row.direction).toBe("down");
    // relative to |prior|: -0.1 / 2 = -5%
    expect(row.relativeShift).toBeCloseTo(-0.05, 10);
  });

  it("marks a moved zero prior as large with no relative shift", () => {
    const row = toDiffRow(
      fitted({ priorMean: 0, posteriorMean: 0.4, shift: 0.4 }),
    );
    expect(row.relativeShift).toBeNull();
    expect(row.magnitude).toBe("large");
    expect(formatRelativeShift(row)).toBe("new");
  });

  it("marks an unmoved parameter as flat/none", () => {
    const row = toDiffRow(fitted({ posteriorMean: 0.2, shift: 0 }));
    expect(row.direction).toBe("flat");
    expect(row.magnitude).toBe("none");
    expect(formatRelativeShift(row)).toBe("0%");
  });
});

describe("shiftMagnitude", () => {
  it("classifies by relative-shift thresholds", () => {
    expect(shiftMagnitude(0.0001, 0.004)).toBe("none");
    expect(shiftMagnitude(0.01, 0.04)).toBe("small");
    expect(shiftMagnitude(0.02, 0.1)).toBe("medium");
    expect(shiftMagnitude(0.1, 0.5)).toBe("large");
    expect(shiftMagnitude(0.1, -0.5)).toBe("large");
  });
});

describe("shapeDiffRows", () => {
  it("sorts by relative shift, biggest movers first", () => {
    const rows = shapeDiffRows(
      diff([
        fitted({ key: "small", priorMean: 1, posteriorMean: 1.01, shift: 0.01 }),
        fitted({ key: "big", priorMean: 1, posteriorMean: 1.6, shift: 0.6 }),
        fitted({ key: "mid", priorMean: 1, posteriorMean: 0.9, shift: -0.1 }),
      ]),
    );
    expect(rows.map((r) => r.key)).toEqual(["big", "mid", "small"]);
  });
});

describe("diffSummary", () => {
  it("counts moved parameters and names the largest shift", () => {
    const s = diffSummary(
      diff(
        [
          fitted({ key: "still", posteriorMean: 0.2, shift: 0 }),
          fitted({ key: "moved-a", priorMean: 1, posteriorMean: 1.2, shift: 0.2 }),
          fitted({ key: "moved-b", priorMean: 1, posteriorMean: 1.05, shift: 0.05 }),
        ],
        3,
      ),
    );
    expect(s.fitted).toBe(3);
    expect(s.moved).toBe(2);
    expect(s.unfitted).toBe(3);
    expect(s.largest?.key).toBe("moved-a");
  });

  it("reports no largest mover when nothing moved", () => {
    const s = diffSummary(diff([fitted({ posteriorMean: 0.2, shift: 0 })]));
    expect(s.moved).toBe(0);
    expect(s.largest).toBeNull();
  });
});

describe("formatting", () => {
  it("formats parameter values at sensible precision across ranges", () => {
    expect(formatParamValue(0)).toBe("0");
    expect(formatParamValue(0.042)).toBe("0.042");
    expect(formatParamValue(1.5)).toBe("1.50");
    expect(formatParamValue(250)).toBe("250");
    expect(formatParamValue(12500)).toBe("12,500");
    expect(formatParamValue(0.0042)).toBe("0.0042");
  });

  it("formats mean ± sd, dropping the ± for point posteriors", () => {
    expect(formatMeanSd(0.2, 0.12)).toBe("0.200 ± 0.120");
    expect(formatMeanSd(3, 0)).toBe("3.00");
  });

  it("formats relative shifts with sign and percent", () => {
    expect(
      formatRelativeShift(
        toDiffRow(fitted({ priorMean: 1, posteriorMean: 1.123, shift: 0.123 })),
      ),
    ).toBe("+12.3%");
    expect(
      formatRelativeShift(
        toDiffRow(fitted({ priorMean: 1, posteriorMean: 0.959, shift: -0.041 })),
      ),
    ).toBe("−4.1%");
  });
});
