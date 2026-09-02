import { describe, expect, it } from "vitest";
import {
  ageMonths,
  cycleStatus,
  distributionMean,
  expectedCycleMonths,
  filterByDecidedRange,
  formatMonths,
  groupOpportunities,
  pipelineGroup,
} from "./pipeline";
import type { OpportunityDto, ParameterDto } from "./api/types";

function opp(overrides: Partial<OpportunityDto>): OpportunityDto {
  return {
    id: "o1",
    firmId: "f1",
    channelId: null,
    stage: "Open",
    openedAt: "2026-01-01T00:00:00Z",
    qualifiedAt: null,
    decidedAt: null,
    competitorIds: [],
    winningProductId: null,
    crossSellModuleProductId: null,
    isCrossSell: false,
    isDecided: false,
    notes: null,
    scope: [],
    ...overrides,
  };
}

function param(key: string, p1: number, p2 = 0): ParameterDto {
  return {
    id: key,
    assumptionSetId: "as1",
    key,
    distribution: { type: "Point", p1, p2 },
    provenance: "Asserted",
    rationale: null,
    clientSegmentId: null,
    flowKind: null,
    moduleProductId: null,
    covariateId: null,
    covariateBeta: null,
    isEngineParameter: false,
  };
}

describe("pipelineGroup", () => {
  it("maps every decided stage to the decided group", () => {
    expect(pipelineGroup("Open")).toBe("open");
    expect(pipelineGroup("Qualified")).toBe("qualified");
    expect(pipelineGroup("Won")).toBe("decided");
    expect(pipelineGroup("Lost")).toBe("decided");
    expect(pipelineGroup("NoDecision")).toBe("decided");
  });
});

describe("groupOpportunities", () => {
  it("buckets by stage and sorts newest first within each group", () => {
    const groups = groupOpportunities([
      opp({ id: "a", stage: "Open", openedAt: "2026-01-01T00:00:00Z" }),
      opp({ id: "b", stage: "Open", openedAt: "2026-03-01T00:00:00Z" }),
      opp({ id: "c", stage: "Qualified", openedAt: "2026-02-01T00:00:00Z" }),
      opp({
        id: "d",
        stage: "Won",
        isDecided: true,
        openedAt: "2025-11-01T00:00:00Z",
        decidedAt: "2026-02-15T00:00:00Z",
      }),
      opp({
        id: "e",
        stage: "Lost",
        isDecided: true,
        openedAt: "2025-12-01T00:00:00Z",
        decidedAt: "2026-04-01T00:00:00Z",
      }),
    ]);
    expect(groups.open.map((o) => o.id)).toEqual(["b", "a"]);
    expect(groups.qualified.map((o) => o.id)).toEqual(["c"]);
    // Decided sorts by decision date, not opening date.
    expect(groups.decided.map((o) => o.id)).toEqual(["e", "d"]);
  });
});

describe("ageMonths", () => {
  const now = new Date("2026-07-01T00:00:00Z");

  it("measures open opportunities against now", () => {
    const age = ageMonths(opp({ openedAt: "2026-01-01T00:00:00Z" }), now);
    // 181 days / 30.4375 ≈ 5.95 months
    expect(age).toBeCloseTo(181 / 30.4375, 5);
  });

  it("freezes decided opportunities at their decision date", () => {
    const age = ageMonths(
      opp({
        openedAt: "2026-01-01T00:00:00Z",
        decidedAt: "2026-02-01T00:00:00Z",
        stage: "Won",
        isDecided: true,
      }),
      now,
    );
    expect(age).toBeCloseTo(31 / 30.4375, 5);
  });

  it("clamps future openings at zero", () => {
    expect(ageMonths(opp({ openedAt: "2026-12-01T00:00:00Z" }), now)).toBe(0);
  });
});

describe("distributionMean", () => {
  it("matches the engine's conventions per distribution type", () => {
    expect(distributionMean({ type: "Point", p1: 3, p2: 0 })).toBe(3);
    expect(distributionMean({ type: "Normal", p1: 2.5, p2: 1 })).toBe(2.5);
    expect(
      distributionMean({ type: "Lognormal", p1: Math.log(4), p2: 0 }),
    ).toBeCloseTo(4, 10);
    expect(distributionMean({ type: "Beta", p1: 2, p2: 6 })).toBeCloseTo(
      0.25,
      10,
    );
    expect(distributionMean({ type: "Beta", p1: 0, p2: 0 })).toBe(0);
  });
});

describe("expectedCycleMonths", () => {
  it("computes the lognormal mean exp(mu + sigma^2/2) from segment keys", () => {
    const params = [
      param("choice.sales-cycle-mu:seg-1", Math.log(6)),
      param("choice.sales-cycle-sigma:seg-1", 0.5),
    ];
    expect(expectedCycleMonths("seg-1", params)).toBeCloseTo(
      6 * Math.exp(0.125),
      10,
    );
  });

  it("defaults sigma to 0 when only mu is present", () => {
    const params = [param("choice.sales-cycle-mu:seg-1", Math.log(3))];
    expect(expectedCycleMonths("seg-1", params)).toBeCloseTo(3, 10);
  });

  it("ignores product-specific keys and other segments", () => {
    const params = [
      param("choice.sales-cycle-mu:seg-1:prod-9", Math.log(99)),
      param("choice.sales-cycle-mu:seg-2", Math.log(4)),
    ];
    expect(expectedCycleMonths("seg-1", params)).toBeNull();
  });

  it("returns null with no cycle parameters (instant decisions)", () => {
    expect(expectedCycleMonths("seg-1", [])).toBeNull();
  });
});

describe("cycleStatus", () => {
  it("classifies against the expected cycle with a 1.5x grace band", () => {
    expect(cycleStatus(3, 4)).toBe("onTrack");
    expect(cycleStatus(4, 4)).toBe("onTrack");
    expect(cycleStatus(5, 4)).toBe("stretched");
    expect(cycleStatus(6, 4)).toBe("stretched");
    expect(cycleStatus(6.01, 4)).toBe("overdue");
  });

  it("returns null with nothing to compare against", () => {
    expect(cycleStatus(3, null)).toBeNull();
    expect(cycleStatus(3, 0)).toBeNull();
  });
});

describe("filterByDecidedRange", () => {
  const won = (id: string, decidedAt: string | null) =>
    opp({ id, stage: "Won", isDecided: !!decidedAt, decidedAt });

  const items = [
    won("a", "2026-01-10T00:00:00Z"),
    won("b", "2026-02-15T00:00:00Z"),
    won("c", "2026-03-20T00:00:00Z"),
    won("d", null),
  ];

  it("passes everything through when both bounds are null", () => {
    expect(filterByDecidedRange(items, null, null).map((o) => o.id)).toEqual([
      "a",
      "b",
      "c",
      "d",
    ]);
  });

  it("applies an inclusive lower bound, excluding items with no decided date", () => {
    const result = filterByDecidedRange(items, "2026-02-15", null);
    expect(result.map((o) => o.id)).toEqual(["b", "c"]);
  });

  it("applies an inclusive upper bound, excluding items with no decided date", () => {
    const result = filterByDecidedRange(items, null, "2026-02-15");
    expect(result.map((o) => o.id)).toEqual(["a", "b"]);
  });

  it("applies both bounds together", () => {
    const result = filterByDecidedRange(items, "2026-01-15", "2026-03-01");
    expect(result.map((o) => o.id)).toEqual(["b"]);
  });

  it("excludes items with an unparseable decided date once a bound is set", () => {
    const withBad = [...items, opp({ id: "e", stage: "Lost", isDecided: true, decidedAt: "not-a-date" })];
    expect(filterByDecidedRange(withBad, "2026-01-01", null).map((o) => o.id)).toEqual([
      "a",
      "b",
      "c",
    ]);
  });
});

describe("formatMonths", () => {
  it("keeps one decimal under ten months, rounds above", () => {
    expect(formatMonths(0.62)).toBe("0.6 mo");
    expect(formatMonths(14.4)).toBe("14 mo");
    expect(formatMonths(Number.NaN)).toBe("—");
  });
});
