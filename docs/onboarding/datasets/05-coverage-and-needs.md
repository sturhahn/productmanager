# 05 - Coverage and needs

Fills in the supply and demand sides of the market defined in
[02-market-structure](02-market-structure.md). Coverage is the product x business-process grid
of gold / silver / bronze / not-playing assessments (ours and competitors'); needs are the
segment x business-process importance weights and constraints that say what buyers actually
care about. Together they drive the static coverage grid page, feature-delta masking, the
aggregate engine's coverage-vs-rival attractiveness multiplier, and the firm-level choice
model's utility. PRD section: "Market side - demand & supply of capability" (Need and Coverage
rows) and "The choice model (how a firm decides)" in `docs/PRDs/product_requirements.md`.

Commands below use `pm` as shorthand for `dotnet run --project src/ProductManager.Cli --`.

## Tier and prerequisites

- Tier B. Coverage is mandatory for the coverage grid and for any simulation; needs are
  mandatory for the firm-level engine (tier D) and unused by the aggregate engine (tier C).
  Without needs the firm-level engine has no demand side: every product's coverage term is
  multiplied by a zero weight (doc comment on `SeedNeed`,
  `src/ProductManager.Cli/Seed/SeedModels.cs`).
- Prerequisites: [01-workspace-and-teams](01-workspace-and-teams.md) and
  [02-market-structure](02-market-structure.md) - every row references a product, process,
  segment, or approach by name. Both can be in the same file (the loader creates the market
  skeleton before coverages and needs) or already loaded.
- Typical load: one HTTP list + one create per row; a 6-process, 4-product grid with 20 needs
  takes well under a minute.

## Questions to ask the user

1. Which processes does each of our products cover, and how well? Walk the process catalog from
   02 product by product. Use the tier definitions from the PRD:
   - `Gold` - market-leading; the reason customers pick you for this process.
   - `Silver` - competitive; wins or loses on other factors.
   - `Bronze` - check-the-box; present but below competitive.
   - `NotPlaying` - no offering for this process.
   Source: the product marketing "capabilities" grid, the last few RFP responses, win/loss
   notes. [Default for our products: no default - every cell must be an explicit answer.]
2. Does coverage differ by segment or by approach? Only then add a row with `segmentName` or
   `approachName`. [Default: one generic row per product x process.]
3. Same questions for each competitor product, sparser. For each cell ask "do we actually know?"
   If not, record `isUnknown: true` - never guess Bronze. Source: analyst reports, lost-deal
   debriefs, competitor pricing pages, demo recordings.
4. How sure are you? `confidence` in [0,1] per row [default 1.0 for our own products, 0.5-0.8
   for competitor rows you have evidence for]. As of what date did this assessment hold?
   `effectiveDate` [default: the date of the source document].
5. Per segment, rank the processes by how much they matter in a purchase decision. Which are
   deal-breakers (a product without it is not evaluated)? Which are worked around when missing,
   and what does the workaround cost per month (staff, spreadsheets, a point-solution vendor)?
   Source: win/loss interviews, RFP weighting sections, the sales team's discovery script.
6. Any process whose importance is conditional on a firm attribute ("only firms with
   international clients")? Record the condition as free text in `conditionality`.

**Consequential choices** here: every coverage tier and delta (`coverageTier`, `coverageDelta`) and every need weight, hardness or workaround cost (`need`). Rubric: [review section 5](../review/README.md#5-needs-and-coverage-rubric) - a tier needs dated, representative evidence; a competitor cell with no accepted evidence stays `isUnknown: true` (unknown stays unknown, never a guessed Bronze, and extraction confidence never turns it into a tier); a customer request is evidence of a need, not of a coverage tier or of any change in win probability. Repeated sales notes are one observation. These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only.

## Data requirements

Fields are on `SeedCoverage` and `SeedNeed` in `src/ProductManager.Cli/Seed/SeedModels.cs`.
Both arrays sit under `market` next to the 02 keys.

### market.coverages[]

| field | required | type | unit / format | notes |
|---|---|---|---|---|
| `productName` | yes | string | | Resolves to a product in this market (ours or a competitor's). |
| `businessProcessName` | yes | string | | Resolves to a process in this market. |
| `segmentName` | no | string | | Segment-specific cell. Omit for the generic cell that applies to every segment. |
| `approachName` | no | string | | Must be an approach of `businessProcessName`. Omit for the default cell that applies to every approach. |
| `tier` | no | enum string | `NotPlaying`, `Bronze`, `Silver`, `Gold` | Default `NotPlaying`. Exact spelling (`src/ProductManager.Cli/Client/Enums.cs`, `CoverageTier`). |
| `isUnknown` | no | bool | | Default false. True = explicit "we don't know"; the API then stores tier `NotPlaying`, confidence 0.0, numeric score null and ignores `tier` / `confidence` (`Coverage.Unknown`, `src/ProductManager.Domain/Market/DemandSupply.cs`). |
| `confidence` | no | number | fraction in [0,1] | Default 1.0. API and domain reject values outside [0,1]. |
| `rationale` | no | string | | Why this tier; cite the evidence. |
| `effectiveDate` | no | date | `yyyy-MM-dd` | When the assessment held. Not used by the engines; shown in the UI. |

Natural key: (product, process, approach or none, segment or none). One row per key.
`scaleLimits` (a JSON side-channel with `hasCapacity`) is not seedable; set it afterwards with
`pm coverage update <id> --scale-limits '{"hasCapacity":false}'` if a product cannot onboard
new clients for a process.

### market.needs[]

| field | required | type | unit / format | notes |
|---|---|---|---|---|
| `segmentName` | yes | string | | Seeded needs are segment-level only; firm-level needs are created via `pm need create --firm-id`. |
| `businessProcessName` | yes | string | | |
| `importanceWeight` | yes | number | >= 0, recommended scale [0,1] | Relative importance; see "Units and conversions". API rejects negatives. |
| `isHard` | no | bool | | Default false. Hard = disqualifies a candidate below `requiredCoverage`; soft = charges `workaroundCostPerPeriod`. |
| `requiredCoverage` | no | number | fraction in [0,1] | Effective-coverage floor. Omit for the engine default: 0.4 for a hard need, 0 for a soft one. Loader rejects values outside [0,1]. |
| `workaroundCostPerPeriod` | no | number | money per month | Cost of working around an unmet soft need. Stored as Money with currency "USD" (hard-coded in `SeedLoader.LoadNeedsAsync`). |
| `conditionality` | no | string | | Free text; stored and displayed, not read by the engine. |
| `notes` | no | string | | |

Natural key: (segment, process). One row per key.

## Units and conversions

- Tier numeric mapping (the same table in three places: `Coverage.NumericScore` in
  `src/ProductManager.Domain/Market/DemandSupply.cs`, `NumericTier` in
  `src/ProductManager.Api/Simulation/FirmLevelInputBuilder.cs` and in
  `src/ProductManager.Api/Simulation/SimulationInputBuilder.cs`):

  | tier | score |
  |---|---|
  | `Gold` | 1.0 |
  | `Silver` | 0.7 |
  | `Bronze` | 0.4 |
  | `NotPlaying` | 0.0 |
  | `isUnknown: true` | null - excluded from both engines (`.Where(c => !c.IsUnknown)` in both input builders) |

  Unknown is therefore not Bronze and not NotPlaying: an unknown competitor cell neither helps
  nor hurts the competitor in a simulation, whereas a guessed Bronze would drag its score down.
- Effective coverage for a firm = sum over the segment's approach mix of share x resolved cell
  score, so a Gold cell on an approach 10% of the segment uses contributes 0.1
  (`src/ProductManager.Engine/FirmLevelEngine.cs`, coverage epoch build). With no approaches
  the generic cell counts in full. A segment-specific cell shadows the generic cell for that
  segment only; precedence is (approach, segment) > (approach, generic) > (default approach,
  segment) > (default approach, generic) (`CoverageCell` remarks,
  `src/ProductManager.Engine/FirmLevelContracts.cs`).
- Aggregate engine: coverage enters only as a win-rate multiplier
  `clamp(1 + 0.5 x (ourCoverageIndex - bestRivalIndex), 0.5, 1.5)`, where each index is the
  mean tier score over relevant processes (`SimulationInputBuilder.BuildAttractivenessSchedule`).
  Needs are not read by the aggregate engine.
- `confidence` is stored and shown on the grid; neither engine reads it today (no reference in
  `src/ProductManager.Api/Simulation/` or `src/ProductManager.Engine/`). Record it anyway - it
  is how a later reviewer knows which cells to re-check.
- `importanceWeight` is used raw, not normalized per segment: the firm-level utility is
  `sum over in-scope processes of importance x effectiveCoverage`, with each firm's weight
  sampled from a point distribution at the need's value (or a `utility.importance-weight`
  parameter from [08-assumption-sets](08-assumption-sets.md) that overrides it), clamped at
  >= 0 (`FirmLevelInputBuilder.BuildSegment`; `FirmLevelEngine.SampleLatents` and the utility
  loop). Because the other utility terms are O(1) - the consolidation bonus is at most
  `consolidationPreference`, the price term is `priceSensitivity x monthlyPrice / 1000` with
  defaults 1.0 and 1000 (`src/ProductManager.Api/Simulation/ChoiceParameterKeys.cs`) - keep
  weights in [0,1] with the most important process at 1.0. A process with no need row has
  weight 0 and contributes nothing to any candidate's utility.
- Rank-to-weight conversion: ask the user to rank the n processes that matter to a segment,
  then `importanceWeight = (n - rank + 1) / n`, rounded to one decimal. Five ranked processes
  give 1.0, 0.8, 0.6, 0.4, 0.2. Ties share a rank. Processes the user does not rank get no
  need row. If the user has an RFP weighting (points per section), divide each section's
  points by the largest section's points instead.
- `requiredCoverage` is compared against effective coverage (above). 0.4 means "at least
  Bronze under the default approach"; 0.7 means "at least Silver". Hard need default 0.4
  (`FirmLevelInputBuilder.HardNeedDefaultRequiredCoverage`). The loader writes it into the
  need's `requiredDepthJson` as `{"requiredCoverage": x}`, which is where the builder reads it.
- Hard vs soft, exactly as the engine applies them (`FirmLevelEngine.cs`, coverage epoch build
  and utility loop):
  - hard, effective coverage < `requiredCoverage`: the candidate is dropped from the
    consideration set for any opportunity whose scope includes the process.
  - soft, `requiredCoverage` > 0 and effective coverage below it: `workaroundCostPerPeriod` is
    added to the candidate's penalty.
  - soft, no `requiredCoverage`, effective coverage 0: the workaround cost is still charged
    (not covering the process at all always incurs it).
  - soft, no `requiredCoverage`, any positive coverage: no charge.
- `workaroundCostPerPeriod` is a monthly amount (doc comment on `SeedNeed`;
  `Need.WorkaroundCostPerPeriod` is Money per period, periods are calendar months). Yearly cost
  / 12. The engine subtracts `choice.workaround-cost-weight x cost` from utility with the
  weight defaulting to 1.0 and no division by `priceScale` (`FirmLevelEngine.cs` utility loop;
  `ChoiceParameterKeys.DefaultWorkaroundCostWeight`). A raw currency amount (2500) therefore
  swamps a coverage utility of O(1): set `choice.workaround-cost-weight` to about
  `1 / priceScale` (0.001 with the default price scale) in your assumption set
  ([08-assumption-sets](08-assumption-sets.md)) so workaround cost and price sit on the same
  scale, or record the cost in the same [0,1] units as importance and leave the weight at 1.
  State which convention you chose in `notes`.
- Dates are ISO `yyyy-MM-dd`. `effectiveDate` may be any past date; it is not a fact-ledger
  `recordedDate` and is not validated against today.

## Seed JSON shape

```json
{
  "market": {
    "name": "Mid-market 3PL warehouse software",
    "coverages": [
      {
        "productName": "Harborline WMS",
        "businessProcessName": "Inventory Control",
        "tier": "Gold",
        "confidence": 0.9,
        "rationale": "Lot, serial, and location accuracy referenced in every recent win.",
        "effectiveDate": "2026-06-30"
      },
      {
        "productName": "Harborline WMS",
        "businessProcessName": "Order Picking",
        "approachName": "RF Scanner Guided",
        "tier": "Silver",
        "confidence": 0.8,
        "rationale": "Scanner-directed picking ships today; wave picking is on the roadmap.",
        "effectiveDate": "2026-06-30"
      },
      {
        "productName": "Harborline WMS",
        "businessProcessName": "Shipping & Carrier Integration",
        "tier": "Bronze",
        "confidence": 0.9,
        "rationale": "Two carriers hard-wired, no rate shopping until Carrier Connect v1.",
        "effectiveDate": "2026-06-30"
      },
      {
        "productName": "Palletto Cloud",
        "businessProcessName": "Shipping & Carrier Integration",
        "segmentName": "E-commerce Fulfillment",
        "tier": "Gold",
        "confidence": 0.6,
        "rationale": "Lost two e-commerce deals on carrier connectivity in Q2 2026.",
        "effectiveDate": "2026-05-15"
      }
    ],
    "needs": [
      {
        "segmentName": "E-commerce Fulfillment",
        "businessProcessName": "Shipping & Carrier Integration",
        "importanceWeight": 1.0,
        "isHard": true,
        "requiredCoverage": 0.4,
        "notes": "No parcel integration, no evaluation. Bronze is enough to be considered."
      },
      {
        "segmentName": "E-commerce Fulfillment",
        "businessProcessName": "Order Picking",
        "importanceWeight": 0.8
      },
      {
        "segmentName": "Regional 3PLs",
        "businessProcessName": "Billing",
        "importanceWeight": 0.9,
        "workaroundCostPerPeriod": 2500,
        "notes": "Monthly USD. Without activity billing a 3PL runs a spreadsheet and a part-time clerk. Pair with choice.workaround-cost-weight = 0.001."
      },
      {
        "segmentName": "Regional 3PLs",
        "businessProcessName": "Inventory Control",
        "importanceWeight": 1.0
      }
    ]
  }
}
```

An explicit unknown for a competitor cell (the tier and confidence you would otherwise supply
are ignored, so omit them):

```json
{
  "market": {
    "name": "Mid-market 3PL warehouse software",
    "coverages": [
      {
        "productName": "Stackline WMS",
        "businessProcessName": "Cycle Counting",
        "isUnknown": true,
        "rationale": "Never seen in a demo; no analyst coverage. Do not assume Bronze."
      }
    ]
  }
}
```

## Loading and idempotency

Command (from the repo root, API running, `pm auth login` done):

```
dotnet run --project src/ProductManager.Cli -- seed --file <your-seed>.json
```

Position in `SeedLoader.RunAsync`: workspace -> **market** (segments, processes, approaches,
competitors, products, then **coverages**, then **needs**) -> marketDynamics -> teams ->
initiatives -> features -> projects -> roadmap.

A coverage-only or needs-only file loads on its own, as the example above does. The `market`
block carrying only `name` matches the existing market by name and creates nothing, and every
index the rows resolve against (products, processes, segments, approaches) is built from the
API's listing for that market rather than from this file. The approach index covers every
process in the market, so an `approachName` resolves even though this file lists no
`approaches` (`SeedLoader.LoadApproachesAsync` and `ListRemainingApproachesAsync`,
`src/ProductManager.Cli/Seed/SeedLoader.cs` lines 334-381).

Duplicate rules are hand-rolled because neither entity has a name
(`src/ProductManager.Cli/Seed/SeedLoader.cs`, `LoadCoveragesAsync` / `LoadNeedsAsync`):

| entity | matched by | on match | on miss |
|---|---|---|---|
| coverage | `GET /api/coverages?productId&businessProcessId&clientSegmentId`, then any row whose `approachId` equals the seed row's (null = null). A seed row without `segmentName` sends no segment filter, so it also matches an existing segment-scoped row for the same (product, process, approach) | tally `matched`; the existing tier, confidence, rationale, and date are left as they were | `POST /api/coverages` |
| need | `GET /api/needs?clientSegmentId&businessProcessId`; any existing row | tally `matched`; nothing updated | `POST /api/needs` with `requiredDepthJson` built from `requiredCoverage` and the workaround cost as `{amount, "USD"}` |

Consequences:

- Re-running the same file changes nothing: `coverage created 0, matched N` and
  `need created 0, matched M`.
- Changing a tier in the file and re-running does not re-assess. Use
  `pm coverage upsert --product-id ... --business-process-id ... --tier Silver` (the API `POST`
  upserts on the full key) or `pm coverage update <id> --tier Silver`, or edit the cell on the
  Coverage page. Likewise `pm need update <id> --importance 0.7`.
- Two seed rows with the same key in one file: the second is matched against the first and
  skipped, so the first wins.
- A `productName`, `businessProcessName`, `segmentName`, or `approachName` that does not resolve
  (or an approach that belongs to a different process) aborts the run with a broken-reference
  error before any later entity loads.
- `requiredCoverage` outside [0,1] aborts with an argument error before the need is posted.

## Verification

- `pm coverage list --product-id <guid>` (also `--business-process-id`, `--segment-id`);
  `pm coverage get <id>` shows `tier`, `isUnknown`, `confidence`, `numericScore`,
  `effectiveDate`, `rationale`. Correct: `numericScore` is 1.0 / 0.7 / 0.4 / 0.0 for assessed
  rows and null with `isUnknown: true` for unknowns.
- `pm need list --segment-id <guid>` (also `--business-process-id`, `--firm-id`);
  `pm need get <id>` shows `importanceWeight`, `isHard`, `workaroundCostPerPeriod`
  (`{amount, currency}`), `requiredDepthJson`, `conditionality`, `notes`. Correct: one row per
  (segment, process) you intended, `requiredDepthJson` equal to `{"requiredCoverage":0.4}`
  where you set a floor, null otherwise.
- Ids for the filters: segments via `pm segment list --market-id <guid>`; products and
  processes via `GET /api/products?marketId=` and `GET /api/business-processes?marketId=`
  (no `pm` verbs exist for those - see [02-market-structure](02-market-structure.md)).
- Web app: `/coverage` is the product x process grid with a segment and approach filter;
  segment-specific cells show in full, inherited generic cells dimmed, unknowns as an
  "Unknown" chip. Every product should have a chip in every process column (NotPlaying counts;
  a blank cell means you have not assessed it). Needs have no dedicated page; use the CLI.

## Common mistakes

- Guessing `Bronze` for a competitor process you have no evidence on: it lowers the
  competitor's simulated attractiveness. Use `isUnknown: true`.
- Spaced or misspelled tiers (`"Not Playing"`, `"None"`): the loader's enum parse fails with
  "is not a valid coverage tier (expected one of: ...)". Lower-case (`"gold"`) happens to parse
  because `ParseHelpers.ParseEnum` passes `ignoreCase: true`, but it is off-contract - write the
  exact members `NotPlaying`, `Bronze`, `Silver`, `Gold`.
- `confidence: 90` instead of `0.9`: the API rejects it (Range 0..1).
- `approachName` on a process that has no approaches, or an approach defined for a different
  process: broken-reference error. The approach must be declared for that same
  `businessProcessName` in 02.
- Leaving out `needs` for a segment and then running the firm-level engine: no candidate has
  any utility from coverage in that segment, so outcomes are driven by price, bias, and noise
  only.
- Importance weights on a 1-100 scale: they are not normalized, so a 100 makes the price and
  consolidation terms irrelevant. Use [0,1].
- Editing a tier in the seed file expecting a re-run to update it: matched rows are never
  re-assessed. Use `pm coverage upsert` / `update` or the grid.
- A soft need with `workaroundCostPerPeriod` in raw currency and the default
  `choice.workaround-cost-weight` of 1.0: the penalty is thousands of utility points. Set the
  weight or the units as described above.
