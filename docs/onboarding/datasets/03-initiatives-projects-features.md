# 03 - Initiatives, projects, and features

This dataset is the execution side of the roadmap: initiatives (the strategic bets), the
projects that execute them (dates, status, dependencies, per-team monthly effort demand), and
the features those projects deliver. `Project delivers Feature` is the single seam between the
execution side and the market side; a feature's deltas are what a roadmap scenario changes in
the market simulation. Loading it enables the Initiatives, Projects and Features pages and is
required before any roadmap can be planned. It maps to the PRD sections "Domain Model >
Execution side (roadmap & delivery)" (rows Initiative and Project) and "Domain Model > The
seam" (row Feature) in `docs/PRDs/product_requirements.md`.

CLI shorthand used below: `pm` means `dotnet run --project src/ProductManager.Cli --`.

## Tier and prerequisites

- Tier: A (mandatory). A roadmap-only user needs initiatives, projects and features but may
  leave out every market-side reference (see below).
- Prerequisites: [01-workspace-and-teams](01-workspace-and-teams.md) must be loaded (effort
  demands reference teams by name). [02-market-structure](02-market-structure.md) is required
  only if you use `targetSegmentNames` on initiatives or `productName` / `deltas` on features.
  Those names resolve against the market already in the API; this file does not have to repeat
  the `market` block - see "Loading and idempotency" for the exact rule.
- Typical load time: seconds. One call per initiative, feature and project, plus one call per
  status update, delta set, dependency edge, effort-demand row and feature link (the Harborline
  example is about 40 calls).

## Questions to ask the user

1. Where is the current plan? Default sources: a roadmap spreadsheet or slide deck (initiatives
   and projects), Jira/Linear epics (projects), a product catalog or release notes (features).
   Ask for an export or a screen share; do not paraphrase from memory.
2. What are the initiatives (3-8 strategic bets), and for each: the outcome it is meant to
   produce, why the user believes it wins (`hypothesizedValue`), and whether it is meant to
   differentiate, reach parity with competitors, or retain existing clients (`strategicIntent`).
   Default: omit `strategicIntent` if the user is unsure; it is a label only, never read by the
   engine (`src/ProductManager.Domain/Execution/Initiative.cs` lines 20-24).
3. Which segments does each initiative target? Only answerable if
   [02-market-structure](02-market-structure.md) was loaded. Default: omit `targetSegmentNames`.
4. For each project: which initiative owns it, its status, start and end dates, and which
   projects it cannot start before. Default status: `Planned` for anything with dates,
   `Proposed` for ideas, `InProgress` for work already started, `Done` for shipped work you still
   want on the timeline.
5. What is each project's effort estimate, per team, and in which unit? Take the total estimate
   and the team split; the agent spreads it over the project's months (see "Units and
   conversions"). The unit must be the one recorded in 01.
6. Which capabilities does each project deliver? One or more feature names. Features are
   capabilities, not tickets: "Wave picking", not "WP-142 picking UI polish". Default: one
   feature per project named after the capability it adds.
7. For each feature: is it hypothetical, planned, or already shipped (with the ship date)? Which
   of our products does it belong to (`productName`, needs 02)? Which business process does it
   improve, from which coverage tier to which (`deltas`, needs 02)? Default for a roadmap-only
   user: `status` only, no `productName`, no `deltas`.

**Consequential choices** here: which epic or ticket delivers which capability and any merge or split of features (`identityMerge`, `capabilityScope`), and every feature delta, especially a jump such as Bronze to Gold (`coverageDelta`). These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only.

## Data requirements

### initiatives[]

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| name | yes | string | max 200 chars, trimmed | Natural key within the workspace, matched case-insensitively. |
| outcome | no | string | free text | The named outcome this bet is meant to produce. |
| hypothesizedValue | no | string | free text | Why we think this wins. |
| targetSegmentNames | no | array of strings | names of segments in the market | Default `[]`. Resolved against this file's `market` block if it has one, otherwise against the workspace's market read from the API (`SeedLoader.MarketResolver`, `SeedLoader.cs` lines 1010-1037). |
| strategicIntent | no | string | `Differentiation`, `Parity`, `Retention` | Exact member names of `StrategicIntent` (`src/ProductManager.Cli/Client/Enums.cs` lines 41-46). Omit for unset. |

### projects[]

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| name | yes | string | max 200 chars, trimmed | Natural key. Projects are listed per initiative but the loader keeps one name index for the whole file, so keep names unique across the workspace. |
| initiativeName | yes | string | name of an initiative | Must be in this file's `initiatives[]` or already exist in the workspace. |
| description | no | string | free text | |
| status | no | string | `Proposed`, `Planned`, `InProgress`, `Done`, `Cancelled` | `ProjectStatus` (`Enums.cs` lines 7-14). Default when omitted: `Proposed` (`src/ProductManager.Domain/Execution/Project.cs` line 18). |
| startDate | no | string | `yyyy-MM-dd` | The project's own dates; descriptive. The roadmap item has separate planned dates - see [04-roadmap](04-roadmap.md). |
| endDate | no | string | `yyyy-MM-dd` | Must not precede `startDate` (`Project.cs` lines 45-51). |
| dependsOnProjectNames | no | array of strings | names of projects | Default `[]`. "Cannot start before that project finishes" (`Project.cs` line 105). Self-dependency is rejected. |
| effortDemands | no | array of effort rows | see below | Default `[]`. |
| deliversFeatureNames | no | array of strings | names of features | Default `[]`. The seam. Must be in this file's `features[]` or already exist in the workspace. |

### projects[].effortDemands[]

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| teamName | yes | string | name of a team from 01 | Resolved against teams already in the workspace (loaded earlier in the same run or a previous run). |
| year | yes | integer | 2000-2200 | |
| month | yes | integer | 1-12 | One row per (team, year, month) per project. |
| points | yes | decimal | 0 to 1,000,000, same unit as team capacity | 0 clears the demand for that team/period (`Project.cs` lines 70-78; `ExecutionContracts.cs` line 117). |

### features[]

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| name | yes | string | max 200 chars, trimmed | Natural key within the workspace, matched case-insensitively. |
| description | no | string | free text | |
| status | no | string | `Hypothetical`, `Planned`, `Shipped` | `FeatureStatus` (`Enums.cs` lines 16-21). Default when omitted: `Hypothetical` (`src/ProductManager.Domain/Seam/Feature.cs` line 22). |
| shippedDate | no (yes when status is `Shipped`) | string | `yyyy-MM-dd` | The API rejects `Shipped` without a date (`src/ProductManager.Api/Controllers/FeaturesController.cs` lines 73-77). |
| productName | no | string | name of one of our products in the market | Resolved via `MarketResolver` - this file's `market` block, or the workspace's market from the API. Nullable "while still exploratory" (`Feature.cs` line 17). |
| deltas | no | array of delta rows | see below | Default `[]`. When non-empty, replaces the feature's whole delta set on every run. |

### features[].deltas[]

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| kind | no | string | `Coverage`, `SwitchingCost`, `Other` | `FeatureDeltaKind` (`Enums.cs` lines 32-37). Default `Coverage`. |
| businessProcessName | yes for `Coverage`; optional otherwise | string | name of a process in the market | Resolved via `MarketResolver` - this file's `market` block, or the workspace's market from the API. |
| fromTier | no | string | `NotPlaying`, `Bronze`, `Silver`, `Gold` | `CoverageTier` (`Enums.cs` lines 24-30). If given, `toTier` must be strictly higher (`Feature.cs` lines 60-61). |
| toTier | yes for `Coverage`; ignored otherwise | string | `NotPlaying`, `Bronze`, `Silver`, `Gold` | The tier the feature raises coverage to. |
| approachName | no (`Coverage` only) | string | name of an approach on the same `businessProcessName` | Scopes the delta to one approach's cell. Requires `businessProcessName`; the approach index covers every process in the market, so an approach an earlier file created resolves (`SeedLoader.ListRemainingApproachesAsync`, `SeedLoader.cs` lines 366-381). Dropped for non-coverage kinds (`Feature.cs` lines 69-78). |
| segmentName | no | string | name of a segment in the market | Scopes the delta to one segment; omit for all segments. |
| magnitude | yes for `SwitchingCost` and `Other`; ignored for `Coverage` | number | "units defined by kind, in data" (`Feature.cs` line 96) | The API rejects a non-coverage delta without it (`FeaturesController.cs` lines 141-142). |
| rationale | no | string | free text | Why this delta exists; shown in the UI. |

## Units and conversions

- Dates are ISO `yyyy-MM-dd` (`ParseHelpers.ParseDate`,
  `src/ProductManager.Cli/Commands/ParseHelpers.cs` lines 12-17). "Q4 2026" is not a date;
  ask for a day or use the first/last day of the quarter.
- Effort demand is points per team per calendar month, in the convention recorded in 01. The
  feasibility check sums demand across all projects planned on a roadmap per (team, year,
  month) and compares it to that team's capacity for the same month
  (`src/ProductManager.Domain/Services/CapacityFeasibilityService.cs` lines 41-60).
- Spreading a total estimate: if a project needs E points from team T and runs from month m1 to
  month m2 inclusive, enter E / (number of months) per month, rounded to one decimal, and put
  the rounding remainder on the last month. Example: 30 FTE-weeks from Core Platform over
  2026-11 and 2026-12 -> 15 and 15. If the user says "mostly at the start", front-load it; the
  check is per month, so the shape matters.
- Partial months: a project starting on the 20th still has a demand row for that whole calendar
  month. Scale the row down (for example one third of a normal month) rather than skipping it.
- Coverage tiers are ordered `NotPlaying` < `Bronze` < `Silver` < `Gold` (enum order,
  `Enums.cs` lines 24-30). A `Coverage` delta means: after this feature ships, our coverage of
  `businessProcessName` (optionally for one `approachName` and/or `segmentName`) is at least
  `toTier`. `fromTier` is documentation of where we are today; the engine uses only `toTier`
  (`src/ProductManager.Api/Controllers/SimulationRunsController.cs` lines 385-391).
- When a delta takes effect in a simulation: on the planned end date of the roadmap item of the
  project that delivers it, not on the project's `endDate` and not on the feature's
  `shippedDate` (`SimulationRunsController.cs` lines 342-345 and 376). Roadmap dates live in
  [04-roadmap](04-roadmap.md).
- `SwitchingCost` and `Other` deltas carry a free `magnitude` whose unit is whatever the data
  author defines. The simulation currently reads only `Coverage` deltas
  (`SimulationRunsController.cs` line 387 skips other kinds); record the others for the
  strategy view and future use, and state their unit in `rationale`.
- `strategicIntent` is a portfolio label; it has no numeric effect anywhere.

## Seed JSON shape

Excerpt of `docs/onboarding/example-seed.json`, listed in load order (initiatives, features,
projects). It uses `targetSegmentNames`, `productName` and `deltas`, so the market from
[02-market-structure](02-market-structure.md) must already be loaded (or be in the same file).

```json
{
  "initiatives": [
    {
      "name": "Win e-commerce fulfillment",
      "outcome": "Become the default WMS for e-commerce fulfillment operators running 2-10 sites.",
      "hypothesizedValue": "Wave picking and cycle counting close the two gaps named most often in lost e-commerce deals.",
      "targetSegmentNames": [ "E-commerce Fulfillment" ],
      "strategicIntent": "Differentiation"
    },
    {
      "name": "Carrier connectivity parity",
      "outcome": "Stop losing deals on carrier integration checklists.",
      "hypothesizedValue": "Every serious competitor rate-shops across carriers; matching them protects renewals rather than winning new logos.",
      "strategicIntent": "Parity"
    }
  ],
  "features": [
    {
      "name": "Multi-carrier rate shopping",
      "description": "Compares live rates across connected carriers at ship-confirm and picks the cheapest service that meets the promised date.",
      "status": "Planned",
      "productName": "Harborline Carrier Connect",
      "deltas": [
        {
          "kind": "Coverage",
          "businessProcessName": "Shipping & Carrier Integration",
          "fromTier": "Bronze",
          "toTier": "Gold",
          "rationale": "Closes the rate-shopping gap versus Stackline WMS and Palletto Cloud."
        }
      ]
    },
    {
      "name": "Wave picking",
      "description": "Groups orders into waves released to RF scanners by zone and carrier cut-off.",
      "status": "Planned",
      "productName": "Harborline WMS",
      "deltas": [
        {
          "kind": "Coverage",
          "businessProcessName": "Order Picking",
          "approachName": "RF Scanner Guided",
          "segmentName": "E-commerce Fulfillment",
          "fromTier": "Silver",
          "toTier": "Gold",
          "rationale": "High-volume fulfillment sites pick in waves; single-order RF picking caps us at Silver there."
        }
      ]
    },
    {
      "name": "Cycle counting",
      "description": "Scheduled and ad-hoc cycle counts with variance review, replacing annual wall-to-wall counts.",
      "status": "Hypothetical",
      "productName": "Harborline WMS",
      "deltas": [
        {
          "kind": "Coverage",
          "businessProcessName": "Cycle Counting",
          "fromTier": "NotPlaying",
          "toTier": "Silver"
        }
      ]
    }
  ],
  "projects": [
    {
      "name": "Carrier Connect v1",
      "initiativeName": "Carrier connectivity parity",
      "description": "First release of the Carrier Connect module: three parcel carriers, rate shopping, label printing.",
      "status": "InProgress",
      "startDate": "2026-09-01",
      "endDate": "2026-10-31",
      "effortDemands": [
        { "teamName": "Integrations", "year": 2026, "month": 9, "points": 10 },
        { "teamName": "Integrations", "year": 2026, "month": 10, "points": 10 },
        { "teamName": "Core Platform", "year": 2026, "month": 9, "points": 6 }
      ],
      "deliversFeatureNames": [ "Multi-carrier rate shopping" ]
    },
    {
      "name": "Wave picking engine",
      "initiativeName": "Win e-commerce fulfillment",
      "description": "Wave planning service plus the RF scanner flows that consume it; needs carrier cut-offs from Carrier Connect.",
      "status": "Planned",
      "startDate": "2026-11-01",
      "endDate": "2026-12-31",
      "dependsOnProjectNames": [ "Carrier Connect v1" ],
      "effortDemands": [
        { "teamName": "Core Platform", "year": 2026, "month": 11, "points": 16 },
        { "teamName": "Core Platform", "year": 2026, "month": 12, "points": 16 },
        { "teamName": "Web", "year": 2026, "month": 11, "points": 8 },
        { "teamName": "Web", "year": 2026, "month": 12, "points": 8 }
      ],
      "deliversFeatureNames": [ "Wave picking" ]
    },
    {
      "name": "Cycle count workflows",
      "initiativeName": "Win e-commerce fulfillment",
      "description": "Count scheduling, RF count screens, and variance approval.",
      "status": "Proposed",
      "startDate": "2026-10-01",
      "endDate": "2026-11-30",
      "effortDemands": [
        { "teamName": "Core Platform", "year": 2026, "month": 10, "points": 8 },
        { "teamName": "Core Platform", "year": 2026, "month": 11, "points": 8 },
        { "teamName": "Web", "year": 2026, "month": 10, "points": 6 },
        { "teamName": "Web", "year": 2026, "month": 11, "points": 6 }
      ],
      "deliversFeatureNames": [ "Cycle counting" ]
    }
  ]
}
```

Roadmap-only variant (no market loaded): the same features with no `productName` and no
`deltas`. This block loads with only 01 present.

```json
{
  "features": [
    { "name": "Multi-carrier rate shopping", "status": "Planned" },
    { "name": "Wave picking", "status": "Planned" },
    { "name": "Cycle counting", "status": "Hypothetical" }
  ]
}
```

## Loading and idempotency

Command (from the repo root, API running, CLI signed in):

```
dotnet run --project src/ProductManager.Cli -- seed --file <path-to-your-seed>.json
```

Position in the load order (`SeedLoader.RunAsync`, `src/ProductManager.Cli/Seed/SeedLoader.cs`
lines 23-48): workspace, market, marketDynamics, teams, then initiatives (line 40), features
(line 41), projects (line 42), roadmap. Within one file, features are always loaded before
projects, so `deliversFeatureNames` can name features defined lower down in the same file.
Projects themselves are created in a first pass and their dependencies, effort demands and
feature links are applied in a second pass (lines 241-254), so the order of entries inside
`projects[]` does not matter for `dependsOnProjectNames`.

Market references resolve against the API, not only against this file. `targetSegmentNames`,
`productName`, `businessProcessName`, `approachName` and `segmentName` go through
`SeedLoader.MarketResolver` (lines 1010-1037), which is only consulted when the file actually
names a market entity:

- With a `market` block, they resolve against the indexes that block produced. A block carrying
  only `name` matches the existing market by name, creates nothing, and still yields complete
  indexes.
- With no `market` block, the loader lists the workspace's markets on first need. Exactly one
  market gives full indexes; zero markets keeps the old "no market defined in the seed file"
  error (`RequireMarket`, lines 966-967); two or more raise a `SeedReferenceException` asking
  for a `"market": { "name": "..." }` stub naming the one to resolve against.
- The approach index covers every process in the market, not only processes this file lists
  approaches for, so an `approachName` may name an approach an earlier file created
  (`ListRemainingApproachesAsync`, lines 366-381).

Duplicate rules per entity (all names are matched after trim + lower-case, `NameIndex.cs`):

- Initiative (`LoadInitiativesAsync`, lines 101-128): listed for the workspace; a name match is
  reused and nothing on it is updated (outcome, hypothesizedValue, segments, strategicIntent
  are set only at creation). Tally `initiative created/matched`.
- Feature (`LoadFeaturesAsync`, lines 137-175): listed for the workspace; a match is reused. On
  every run, if `status` is present the feature is updated (`PUT /api/features/{id}`, which also
  rewrites name/description/productId from the file); `Planned` marks it planned, `Shipped`
  sets the ship date, `Hypothetical` leaves the stored status unchanged
  (`FeaturesController.cs` lines 68-79). If `deltas` is non-empty the whole delta set is
  replaced (`PUT /api/features/{id}/deltas`, lines 112-128), so re-runs never duplicate deltas;
  an empty `deltas` leaves existing deltas alone. Tally `feature created/matched`.
- Project (`LoadProjectsAsync`, lines 210-255): existing projects are listed once per
  initiative referenced in the file; a match is reused and its status/dates/description are not
  updated. A `dependsOnProjectNames` entry that misses that index falls back to a paged
  workspace-wide project listing (`SeedLoader.ProjectResolver`, lines 1045-1069), so a
  dependency may live under an initiative this file never mentions. Dependency edges and
  feature links are idempotent adds (`Project.cs` lines 61-62 and 83-84); effort demands are
  upserts per (team, year, month) that overwrite and where 0 deletes. Tally `project
  created/matched`. Dependencies, demands and links are not counted.

Tally for the primary example on a fresh instance (`SeedCommand.cs` lines 46-51), execution-side
lines only:

```
  feature              created   3, matched   0
  initiative           created   2, matched   0
  project              created   3, matched   0
```

A second run prints `created 0, matched N` for each and re-applies statuses, deltas, edges,
demands and links with identical results.

Reference failures are preflight problems: the file loads nothing and `pm seed` lists every one as
"Seed file references '<name>', which was not found among the entities loaded so far. Check
ordering and spelling." Fix them all and run again.

## Verification

CLI (JSON output; ids are GUIDs from the list output):

- `pm initiative list --workspace-id <workspaceId>` - two items; check `strategicIntent` and
  that `targetSegmentIds` has one entry for "Win e-commerce fulfillment".
- `pm project list --workspace-id <workspaceId>` or `pm project list --initiative-id
  <initiativeId>` - each item shows `status`, `startDate`, `endDate`, `dependsOnProjectIds`,
  `deliveredFeatureIds` and `effortDemands` rows `{teamId, year, month, points}`
  (`ExecutionMapping.ToDto`, `ExecutionContracts.cs` lines 143-150). "Wave picking engine"
  must have one `dependsOnProjectIds` entry and four effort rows.
- `pm project get <projectId>` - one project, same shape.
- `pm feature list --workspace-id <workspaceId>` (optionally `--product-id <productId>`) - each
  item shows `status`, `shippedDate`, `productId` and `deltas` with `kind`, `fromTier`,
  `toTier`, `businessProcessId`, `approachId`, `segmentId`, `magnitude`, `rationale`.
- `pm feature get <featureId>`.

Frontend (`frontend/src/app/(app)/...`):

- `http://localhost:3000/initiatives` - the two initiatives with their intent badges.
- `http://localhost:3000/projects` - three projects with status badges and a dependency count
  column (1 for "Wave picking engine").
- `http://localhost:3000/features` - three features with status badges and their deltas. A
  coverage delta is shown as "masked" when our best assessed coverage at that process /
  approach / segment already meets or beats `toTier` (`frontend/src/lib/masking.ts`); with the
  Harborline coverage from [05-coverage-and-needs](05-coverage-and-needs.md) none should be
  masked.

## Common mistakes

- Referencing a market name (`productName`, `businessProcessName`, `segmentName`,
  `targetSegmentNames`) before any market exists. With no market in the workspace and no
  `market` block in the file, the load fails with "no market defined in the seed file"; load
  [02-market-structure](02-market-structure.md) first or drop the reference.
- Omitting the `market` block in a workspace that holds more than one market. The loader cannot
  tell which market the names belong to and fails asking for a `"market": { "name": "..." }`
  stub. Adding that stub matches the existing market and creates nothing.
- `approachName` without `businessProcessName`. The approach key is process + approach, so a
  bare approach name never resolves.
- A `Coverage` delta whose `toTier` is not above `fromTier` (for example Silver -> Silver). The
  domain rejects it: "A coverage delta must raise the tier". Omit `fromTier` if unsure.
- `status: "Shipped"` without `shippedDate` - the API returns 400 "A shipped feature requires a
  shippedDate."
- Wrong enum spelling: `"In Progress"`, `"in-progress"`, `"planned"` (lower-case works in the
  loader but fails the documented contract; use the exact names `InProgress`, `Planned`, ...).
  `"Retire"` is not a `StrategicIntent`; `Retention` is.
- Effort demand in a different unit from team capacity, or summed for the whole project on one
  month. Spread it per calendar month in the unit recorded in 01.
- Two projects with the same name under different initiatives. The loader keeps one name index
  for the file, so the second is silently matched to the first.
- Expecting a re-run to update an existing initiative's or project's fields. Only features are
  updated on match (status and deltas); fix the others with `pm initiative update` / `pm
  project update`.
