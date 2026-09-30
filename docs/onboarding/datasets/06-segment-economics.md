# 06 - Segment economics

Segment stock states give each client segment the starting stocks (prospect pool, client count),
the pricing (base ARPU, implementation fee) and the cost rates the aggregate Monte Carlo engine
starts a run from, plus the starting penetration and pricing of each cross-sellable module. They
are the "observed starting state" the PRD's "Financial model (deliberately simple)" section runs
money through: bookings, recognized ARR and contribution margin are all computed from these
numbers. `pm` below means `dotnet run --project src/ProductManager.Cli --`.

## Tier and prerequisites

- Tier C (aggregate simulation). Optional for a roadmap-only instance; required before any
  `pm sim run`, because a segment nobody has measured carries an all-zero stock state
  (`SegmentStockState.Zero()`, `src/ProductManager.Domain/Market/SegmentStockState.cs`) and
  simulates zero clients and zero prospects.
- Must already be loaded: [01-workspace-and-teams](01-workspace-and-teams.md) and
  [02-market-structure](02-market-structure.md). Every `segmentName` must be a segment of the
  market named in `marketName`, and every `moduleProductName` must be a product in that market
  (the module Product with a `parentProductName`, see 02).
- Load time: seconds. One PUT per segment plus one PUT per module penetration row.
- The firm-level engine (tier D) reads the same stock states for its cost and revenue lines, so
  this file is a prerequisite for [11-firms-and-relationships](11-firms-and-relationships.md) too.

## Questions to ask the user

1. "Which finance report has ARR and client count per segment?" (billing system export,
   board deck, revenue spreadsheet). Default if there is no per-segment split: use company
   totals divided by segment client counts, and say so in the seed notes.
2. "Is the base product priced separately from add-on modules?" If module revenue is inside
   the ARR total, subtract it before computing base ARPU (see conversions) - the engine adds
   module MRR on top of base ARPU.
3. "What is the typical one-time implementation fee per new client, per segment?" Default: the
   list `implementationFeeAmount` from the product prices in 02, or 0.
4. "What does it cost to serve one client for one month?" (support, customer success, hosting
   that scales with clients). Default: annual cost of servicing headcount / clients / 12.
5. "What is the fixed hosting/infrastructure spend per month attributable to this segment?"
   Default: split total fixed infra across segments by client count.
6. "How much R&D spend per month goes to support, bugs and incidents rather than roadmap
   work?" This is `rdDragPerMonth`, a currency amount per month, not a fraction. Default: 0 if
   unknown, and note it.
7. "How many firms in this segment are addressable but not yet clients?" (TAM firm count from
   analyst reports, a prospect list, or the CRM). Default: total firms in segment minus clients.
8. "For each module: what share of the segment's clients already hold it, and what does it
   cost per month?" Default penetration 0 and module list price if there is no data.
9. "What currency?" All amounts in one file must be in the market's single currency; the
   engine never converts (`SegmentStockState.cs` class remarks).

**Consequential choices** here: how ARPU is derived from revenue, every cost line (`operatingCosts`) and the prospect pool (`marketPopulation`) when no accessible system holds them, and module penetration defaults. These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only.

## Data requirements

Entity: `marketDynamics.segmentStockStates[]` (one per segment; natural key `segmentName`).

| Field | Required | Type | Unit / format | Notes |
|---|---|---|---|---|
| `segmentName` | yes | string | exact segment name | Must exist in the market named by `marketDynamics.marketName`. |
| `initialProspectPool` | yes | number | firms (may be fractional) | Firms in the segment that are not clients yet. >= 0. |
| `initialClientCount` | yes | integer | firms | Clients at month 0. >= 0. Integer, not a decimal. |
| `arpuPerMonth` | yes | number | currency per client per month | Base product only; modules price separately. >= 0. |
| `implementationFee` | yes | number | currency, one-time per new client | Recognized in the month of the win. >= 0. |
| `servicingCostPerClient` | yes | number | currency per client per month | >= 0. |
| `infraFixedCostPerMonth` | yes | number | currency per month | Independent of client count. >= 0. |
| `infraCostPerClient` | yes | number | currency per client per month | >= 0. |
| `rdDragPerMonth` | yes | number | currency per month | Fixed cost line: support, bugs, incident work. >= 0. |
| `modulePenetrations` | no | array | | Default empty. |

All eight numbers are one measurement: omitting a field deserializes it as 0, and the PUT
replaces the whole stock state (`SeedLoader.ApplySegmentStockStateAsync`).

Entity: `segmentStockStates[].modulePenetrations[]` (natural key `moduleProductName` within
the segment).

| Field | Required | Type | Unit / format | Notes |
|---|---|---|---|---|
| `moduleProductName` | yes | string | exact product name | The module Product (has a `parentProductName` in 02). |
| `penetration` | yes | number | fraction in [0, 1] | Share of the segment's clients holding the module at month 0. Refused outside [0, 1]. |
| `arpuPerMonth` | yes | number | currency per holding client per month | >= 0. |

## Units and conversions

Everything is per month, in the market's single currency, as verified in
`src/ProductManager.Engine/SimulationEngine.cs` (money per tick) and
`src/ProductManager.Domain/Market/SegmentStockState.cs`. What the engine does with each number:

- `bookings = wins x (arpuPerMonth x 12 + implementationFee) + sum over modules(adds x moduleArpu x 12)`
- `recognized ARR = 12 x (clients x arpuPerMonth + sum over modules(moduleClients x moduleArpu))`
- `margin = (clients x arpuPerMonth + module MRR + wins x implementationFee) - (clients x (servicingCostPerClient + infraCostPerClient) + infraFixedCostPerMonth + rdDragPerMonth)`
- Initial module holders = `round(initialClientCount x penetration)`, capped at the client count.

Conversion table:

| Source figure | Conversion | Target |
|---|---|---|
| Segment ARR (base product) and client count | ARR / clients / 12 | `arpuPerMonth` |
| Average annual contract value (ACV) | ACV / 12 | `arpuPerMonth` |
| ARR including module revenue | (ARR - module ARR) / clients / 12 | `arpuPerMonth`; module ARR goes to `modulePenetrations[].arpuPerMonth` |
| Module ARR and module holders | module ARR / holders / 12 | `modulePenetrations[].arpuPerMonth` |
| Module holders and clients | holders / clients | `penetration` (fraction, not percent) |
| Annual cost of support and success staff for the segment | cost / clients / 12 | `servicingCostPerClient` |
| Annual hosting bill, fixed part | bill / 12 | `infraFixedCostPerMonth` |
| Annual hosting bill, variable part | bill / clients / 12 | `infraCostPerClient` |
| Annual R&D budget x share spent on support/bugs/incidents for this segment | budget x share / 12 | `rdDragPerMonth` (currency, never a fraction) |
| TAM firm count | TAM firms - current clients | `initialProspectPool` |
| One-time onboarding/implementation invoice | as is | `implementationFee` |
| Percent penetration (e.g. "30%") | / 100 | `penetration` = 0.3 |

Discount rate and terminal growth are NOT stored here; they are per-run inputs
(`pm sim run --discount-rate --terminal-growth`).

## Seed JSON shape

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "segmentStockStates": [
      {
        "segmentName": "Regional 3PLs",
        "initialProspectPool": 420,
        "initialClientCount": 38,
        "arpuPerMonth": 2400,
        "implementationFee": 12000,
        "servicingCostPerClient": 350,
        "infraFixedCostPerMonth": 6000,
        "infraCostPerClient": 45,
        "rdDragPerMonth": 9000,
        "modulePenetrations": [
          {
            "moduleProductName": "Harborline Carrier Connect",
            "penetration": 0.3,
            "arpuPerMonth": 600
          }
        ]
      },
      {
        "segmentName": "E-commerce Fulfillment",
        "initialProspectPool": 650,
        "initialClientCount": 21,
        "arpuPerMonth": 3900,
        "implementationFee": 18000,
        "servicingCostPerClient": 520,
        "infraFixedCostPerMonth": 4500,
        "infraCostPerClient": 60,
        "rdDragPerMonth": 7000,
        "modulePenetrations": [
          {
            "moduleProductName": "Harborline Carrier Connect",
            "penetration": 0.15,
            "arpuPerMonth": 750
          }
        ]
      }
    ]
  }
}
```

Reading the Harborline numbers: Regional 3PLs pay USD 2,400/month for Harborline WMS (ACV
28,800), 30% of the 38 clients also hold Carrier Connect at USD 600/month, and the segment
carries USD 9,000/month of R&D drag.

## Loading and idempotency

Command: `pm seed --file <path>` (the seed file may contain only the `marketDynamics` key, as
above, once 01 and 02 are loaded).

Load order (`SeedLoader.RunAsync`): workspace -> market -> marketDynamics -> teams ->
initiatives -> features -> projects -> roadmap. Inside marketDynamics, `segmentStockStates` are
applied first, before covariates, assumption sets and events, so a later parameter can rely on
the segment already having stocks.

Duplicate rule (`SeedLoader.ApplySegmentStockStateAsync`, `src/ProductManager.Cli/Seed/SeedLoader.cs`):

- Stock state: there is no match-or-create decision. Every run issues a PUT that replaces the
  segment's whole stock state with the eight numbers in the file. Re-running the same file is a
  no-op; running a changed file overwrites. Tally line: `segmentStock applied N`.
- Module penetration: upsert by module product within the segment. A module listed in the file
  is set to the file's values; a module NOT listed in the file is left untouched (remove it with
  `pm segment remove-module-penetration <segmentId> --module-id <productId>`). Tally line:
  `modulePenetration applied N`.
- An unknown `segmentName` or `moduleProductName` aborts the load with a reference error before
  anything later in the file is applied.

The CLI prints `Loaded <path>:` followed by one line per entity kind, e.g.
`segmentStock          applied   2` and `modulePenetration    applied   2`.

## Verification

- `pm segment list` - each segment's `stockState` and `modulePenetrations` are in the JSON.
- `pm segment get <segmentId>` - the full stock state and module penetration rows for one
  segment.
- Frontend: `/segments` (`frontend/src/app/(app)/segments/page.tsx`) shows a tile row per
  segment (Prospect pool, Clients, ARPU / mo, implementation fee, servicing cost, fixed infra)
  and the module penetration table.
- Correct looks like: client counts match the finance report, `arpuPerMonth x 12 x
  initialClientCount` is close to the segment's base ARR, every penetration is between 0 and 1,
  and no segment that will be simulated still shows 0 clients and 0 pool.
- To edit one segment by hand: `pm segment set-stocks <segmentId> --prospect-pool 420
  --client-count 38 --arpu 2400 --implementation-fee 12000 --servicing-cost 350 --infra-fixed
  6000 --infra-per-client 45 --rd-drag 9000` and `pm segment set-module-penetration <segmentId>
  --module-id <productId> --penetration 0.3 --arpu 600`.

## Common mistakes

- Percent instead of fraction: `"penetration": 30` is refused ("Penetration must be in
  [0, 1]"); write `0.3`.
- Annual ARPU: `arpuPerMonth` is per month. Putting ACV 28800 there inflates ARR twelvefold.
- Double-counting module revenue: base `arpuPerMonth` must exclude module revenue, because the
  engine adds `moduleClients x moduleArpu` on top.
- `rdDragPerMonth` as a fraction ("0.2 of R&D") - it is a currency amount per month added to
  fixed cost. A value like 0.2 silently makes R&D drag negligible.
- Referencing a module that is not yet a product in the market, or a segment that is not in
  this market: the load stops with a reference error. Load 02 first.
- Leaving out fields: a missing field is 0, and the PUT replaces the whole state, so a partial
  object zeroes whatever you omitted.
- Mixed currencies across segments: the engine adds them as if they were the same unit.
