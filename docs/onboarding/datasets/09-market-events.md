# 09 - Market events

A market event is a typed shock - a production incident, a competitor's forced migration, a
pricing-model change, a regulation, a feature launch - that scales one or more simulated flows
while it is active. A real event records something that happened (`isHypothetical: false`); a
hypothetical one is a scenario arm you inject into a run ("with the incident / without"). The
engine reads an event's `rateMultipliers` and its date window; nothing else about it enters the
math. PRD: "Market side - dynamics", row MarketEvent. `pm` below means
`dotnet run --project src/ProductManager.Cli --`.

## Tier and prerequisites

- Tier C (aggregate simulation). Optional. A run with no events is valid; events are an
  overlay chosen per run.
- Must already be loaded: [01-workspace-and-teams](01-workspace-and-teams.md) and
  [02-market-structure](02-market-structure.md) (the market, its segments for
  `rateMultipliers[].segmentName`, and its competitors for `competitorName`). Events only
  change a run's result when [08-assumption-sets](08-assumption-sets.md) gives the affected
  flow a nonzero rate.
- Load time: seconds; one POST per event.

## Questions to ask the user

1. "What has happened in the last 1-2 years that visibly moved your funnel or churn?" (an
   outage, a competitor's end-of-life announcement, a price change, a regulation). These become
   real events. Default: none.
2. "For each, when did it start and end?" Dates as `yyyy-MM-dd`. An event that is still going
   has no `endDate` (the engine extends it to the end of the horizon). Default end for an
   outage: the day service was restored; for a migration window: the vendor's deadline.
3. "Which flows did it move, in which segments, and by how much?" Ask for a multiplier on the
   monthly rate: 1.0 is no effect, 1.5 is +50%, 0.7 is -30%. Default: apply to every segment
   (omit `segmentName`) if the user cannot split it.
4. "Which future scenarios do you want to compare?" These become hypothetical events (a
   competitor's rumored migration, your own next-year incident budget). Default: one
   competitor-linked hypothetical so the compare workflow has something to switch on.
5. "Is a competitor the subject?" If so, which one from 02 (`competitorName`). Default: none.
6. "Do you have an effect size in the source's own units (e.g. 20% of the installed base
   forced to migrate)?" Record it as `magnitude` with a `notes` explanation. It is
   documentation only.

**Consequential choices** here: an event's effect size and the flows and segments it moves (`eventEffects`), and applying an effect to every segment when the human cannot split it. These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only.

## Data requirements

Entity: `marketDynamics.marketEvents[]` (natural key `name`, scoped to the market).

| Field | Required | Type | Unit / format | Notes |
|---|---|---|---|---|
| `name` | yes | string | max 200 chars | Natural key within the market. |
| `type` | no | string | `ProductionIncident`, `ForcedMigration`, `PricingModelChange`, `RegulatoryChange`, `FeatureLaunch`, `Other` | Default `Other`. Exact spelling (`MarketEventType`). Classification only. |
| `startDate` | yes | string | `yyyy-MM-dd` | First active day. Must be present and non-empty. |
| `endDate` | no | string | `yyyy-MM-dd`, >= `startDate` | Last active day. Omitted = open-ended (active to the end of any horizon). |
| `isHypothetical` | no | boolean | | Default `false` (real). |
| `magnitude` | no | number | source units, free | Stored and shown; not read by the engine. |
| `competitorName` | no | string | exact competitor name from 02 | Who the event concerns. |
| `notes` | no | string | | Say what happened and where the multipliers come from. |
| `rateMultipliers` | no | array | | Default empty: an event with no multipliers changes nothing. |

Entity: `marketEvents[].rateMultipliers[]`.

| Field | Required | Type | Unit / format | Notes |
|---|---|---|---|---|
| `segmentName` | no | string | exact segment name | Omit for "every segment in the market". |
| `flowKind` | yes | string | `NewFirmArrival`, `ConversionToMarket`, `WinRate`, `ChurnHazard`, `CrossSellRate` | Exact spelling (`SimulationFlowKind`). |
| `multiplier` | yes | number | dimensionless, >= 0 | 1.0 = no effect. Negative or NaN is refused. |

## Units and conversions

How the engine applies an event (`SimulationInputBuilder.BuildEvent`,
`EngineInternals.BuildEventMultipliers`, `SimulationEngine.Run`):

- Month window. `startDate` and `endDate` are converted to whole-month indices relative to the
  run's start month: `MonthIndex = (year - startYear) x 12 + (month - startMonth)`. The day of
  the month is ignored; an event starting 2025-11-18 is active for all of November 2025. The
  window is inclusive on both ends, clipped to the horizon; an event entirely outside the
  horizon is dropped; a missing `endDate` runs to the last horizon month; a `startDate` before
  the run start is clipped to month 0 (the event is "already happening").
- Multiplication. For every active month, the segment's rate for that `flowKind` is multiplied
  by `multiplier`. Overlapping events multiply together (two x1.5 events give x2.25). A
  multiplier row without `segmentName` applies to every segment. `CrossSellRate` multipliers
  apply to every module in the segment.
- Composition. The event multiplier is one factor in `rate x covariate^beta x events x
  attractiveness` (attractiveness applies to `WinRate` only). Probabilities are clamped to
  [0, 1] afterwards, so a x2 on a 0.6 win rate saturates at 1.0. The combined multiplier may
  not exceed 1000x in any month; the engine aborts the run and names the segment, flow and
  month (`SimulationEngine.MaxFlowMultiplier`).
- `magnitude` is never read by the simulation (no reference in
  `src/ProductManager.Api/Simulation/` or `src/ProductManager.Engine/`). If an effect size
  should move a flow, express it as a multiplier.
- Selection per run. Events are not applied automatically. A run applies exactly the ids passed
  in `pm sim run --event-ids <realIds> --hypothetical-event-ids <hypotheticalIds>`
  (`SimulationRunsController.Create`); the two lists are merged and the flag is not
  re-checked against `isHypothetical`. The frontend's Simulate page offers only hypothetical
  events as checkboxes.

Conversion table:

| Source figure | Conversion | Target |
|---|---|---|
| "Churn doubled during the outage" | 2.0 | `ChurnHazard` multiplier |
| "Win rate went from 30% to 39% while their migration ran" | 0.39 / 0.30 = 1.3 | `WinRate` multiplier |
| "Inbound leads fell 25%" | 1 - 0.25 = 0.75 | `NewFirmArrival` (or `ConversionToMarket`) multiplier |
| "20% of their installed base must migrate" | keep as `magnitude: 0.2` and choose a multiplier for the flows it opens up | `magnitude` + `rateMultipliers` |
| Incident from 18 Nov to 9 Dec 2025 | `2025-11-18` / `2025-12-09` (both November and December months are active) | `startDate` / `endDate` |
| Announced, no end in sight | omit `endDate` | open-ended |

## Seed JSON shape

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "marketEvents": [
      {
        "name": "Peak-season carrier API outage",
        "type": "ProductionIncident",
        "startDate": "2025-11-18",
        "endDate": "2025-12-09",
        "isHypothetical": false,
        "notes": "Harborline Carrier Connect label generation degraded for three weeks of peak season; two clients gave notice. Churn multiplier from the Q4 2025 retention review.",
        "rateMultipliers": [
          { "flowKind": "ChurnHazard", "multiplier": 1.6 }
        ]
      },
      {
        "name": "Stackline usage-based pricing",
        "type": "PricingModelChange",
        "startDate": "2026-04-01",
        "isHypothetical": false,
        "competitorName": "Stackline Systems",
        "notes": "Stackline moved Stackline WMS to per-order pricing; regional 3PLs with volatile volumes started shortlisting us more often. Open-ended.",
        "rateMultipliers": [
          { "segmentName": "Regional 3PLs", "flowKind": "WinRate", "multiplier": 1.1 }
        ]
      },
      {
        "name": "Palletto Cloud v3 forced migration",
        "type": "ForcedMigration",
        "startDate": "2027-01-01",
        "endDate": "2027-06-30",
        "isHypothetical": true,
        "magnitude": 0.2,
        "competitorName": "Palletto",
        "notes": "Scenario: Palletto forces about 20% of its installed base onto Palletto Cloud v3 in H1 2027, opening a switching window in e-commerce fulfillment.",
        "rateMultipliers": [
          { "segmentName": "E-commerce Fulfillment", "flowKind": "WinRate", "multiplier": 1.35 },
          { "flowKind": "NewFirmArrival", "multiplier": 1.1 }
        ]
      }
    ]
  }
}
```

Variant - a hypothetical event about our own stability, the PRD's link between R&D drag and
incident-driven churn:

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "marketEvents": [
      {
        "name": "Harborline WMS 4.2 rollout incident",
        "type": "ProductionIncident",
        "startDate": "2026-11-01",
        "endDate": "2026-11-30",
        "isHypothetical": true,
        "notes": "Scenario: a bad release during peak doubles churn for a month across both segments.",
        "rateMultipliers": [
          { "flowKind": "ChurnHazard", "multiplier": 2.0 }
        ]
      }
    ]
  }
}
```

## Loading and idempotency

Command: `pm seed --file <path>`.

Load order: workspace -> market -> marketDynamics -> teams -> ... Inside marketDynamics,
events load after `segmentStockStates`, `covariates` and `assumptionSets`, and before the tier D
entities (`SeedLoader.LoadMarketDynamicsAsync`).

Duplicate rule (`SeedLoader.LoadMarketEventAsync`, `src/ProductManager.Cli/Seed/SeedLoader.cs`):

- Matched by `name` among the market's existing events. Match = the whole row is skipped:
  type, dates, flag, magnitude, competitor, notes and `rateMultipliers` in the file are NOT
  applied to an existing event (create-only). To change one: `pm market-event update <id>
  --type ... --name ... --start ... --end ... --rate-multipliers "flowKind:multiplier,..."`
  (the multiplier set is replaced wholesale), or `pm market-event delete <id>` and re-seed.
  Tally: `marketEvent created N, matched M`.
- Not matched = one POST creating the event with all its multipliers.
- Name resolution happens first: an unknown `competitorName` or `rateMultipliers[].segmentName`
  aborts the load with a reference error. `type` and `flowKind` must parse as exact enum names.
- The API refuses `endDate` before `startDate` ("Event end cannot precede its start") and a
  negative `multiplier`.
- Renaming an event in the file creates a second event; the old one stays.

## Verification

- `pm market-event list` - every event with its window, flag, competitor and multipliers.
  Filter with `--is-hypothetical true` (scenario events) or `--is-hypothetical false` (real
  ones), or `--competitor-id <id>`.
- `pm market-event get <eventId>` - one event in full.
- Frontend: there is no dedicated events page. `/simulate`
  (`frontend/src/app/(app)/simulate/page.tsx`) lists hypothetical events under "Hypothetical
  events to inject" when configuring a run.
- Effect check: run twice with the same `--seed` and compare -
  `pm sim run --roadmap-id <r> --assumption-set-id <a> --seed 1 --name "base"` then the same
  with `--hypothetical-event-ids <eventId> --name "with migration"`, and
  `pm sim compare <runA> <runB>`. `pm sim input <runId>` shows the event as `events[]` with
  0-based `startMonth`/`endMonth` and its multipliers.
- Correct looks like: real events dated in the past, hypothetical ones typically in the
  future, every multiplier close to 1 (0.5-2.0 is the plausible range), and the compare table
  moving in the direction the multipliers imply.

## Common mistakes

- Expecting `magnitude` to do something: it is stored and displayed only. Put the effect in
  `rateMultipliers`.
- Writing a percent change as the multiplier: `"multiplier": 35` is a 35x shock (and likely
  trips the 1000x guard when stacked). "+35%" is `1.35`.
- Expecting real events to apply automatically: a run applies only the ids passed in
  `--event-ids` / `--hypothetical-event-ids`.
- Re-running the seed to change an event: the name matches and the row is skipped. Use
  `pm market-event update` or delete and re-seed.
- `flowKind` or `type` misspelled (`"Churn"`, `"forced_migration"`): the loader fails to
  parse the enum before any API call.
- A `segmentName` from another market, or a `competitorName` not in 02: reference error.
- Open-ended real event from long ago: with no `endDate` it stays active for the whole
  horizon of every run that includes it. Add the `endDate` when it ended.
