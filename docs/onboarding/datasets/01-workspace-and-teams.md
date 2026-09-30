# 01 - Workspace and teams

This dataset names the workspace (the root aggregate everything else hangs off) and defines the
teams whose monthly capacity constrains the roadmap. Teams are capacity budget units only: no
people, no skills model. Loading it enables the Teams page and, once projects carry effort
demand, the roadmap feasibility check (capacity warnings). It maps to the PRD section "Domain
Model > Execution side (roadmap & delivery)" (rows Workspace and Team) in
`docs/PRDs/product_requirements.md`.

CLI shorthand used below: `pm` means `dotnet run --project src/ProductManager.Cli --`.

## Tier and prerequisites

- Tier: A (mandatory for any use of the product, including roadmap-only).
- Prerequisites: none. This is the first dataset to load. The API must be running and the CLI
  signed in (`pm auth login --password <password> --base-url <url>`).
- Typical load time: a few seconds. The loader makes one API call per team plus one call per
  capacity row (3 teams x 4 months = about 15 calls).

## Questions to ask the user

1. What should the workspace be called? Default: the company or product-line name (Harborline
   uses "Harborline"). One workspace only - see "Loading and idempotency" for why.
2. Which teams own delivery work? Ask for the list of engineering/delivery teams that projects
   will draw effort from. Default: the teams as they appear in the user's planning tool (Jira
   boards, an org chart, a capacity spreadsheet). Merge sub-teams that are always planned
   together; split a team only if projects will demand effort from its parts separately.
3. What is each team's specialization? A short free-text tag such as "backend", "integrations",
   "frontend", "data". Default: leave it out if the user has no obvious label; it is
   descriptive only (`src/ProductManager.Domain/Execution/Team.cs` line 16: "Free text - data,
   not schema").
4. In which unit does the team estimate work? Options: FTE-weeks, FTE-months, working days, or
   story points with a known monthly velocity. Default: FTE-weeks (see "Units and conversions").
   Record the chosen convention in the human's answers; every effort demand in
   [03-initiatives-projects-features](03-initiatives-projects-features.md) must use the same
   unit.
5. What is the planning horizon - which months get a capacity row? Default: the current month
   through the end of the roadmap's last planned project (Harborline: 2026-09 to 2026-12). Any
   month a project demands effort in but no capacity is entered for is treated as capacity 0 and
   flagged (`src/ProductManager.Domain/Services/CapacityFeasibilityService.cs` lines 49-58).
6. Where does capacity data come from? Usually headcount per team (HR system, org chart) plus a
   focus/availability factor (holidays, support rotation, on-call). Default factor: 0.8 if the
   user has no number.
7. Does capacity vary by month? Default: a flat number per team, then adjust for known dips
   (December holidays, a planned hiring date).

**Consequential choices** here: merging or splitting teams, the capacity and effort unit (an unapproved conversion between units), and any capacity figure or focus factor the human did not state. These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only.

## Data requirements

### workspace

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| name | yes (if the `workspace` key is present) | string | max 200 chars | Natural key. If the key is omitted or the name is blank, the loader uses the API's default workspace unchanged. |

### teams[]

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| name | yes | string | max 200 chars, trimmed | Natural key within the workspace. Matched case-insensitively (`src/ProductManager.Cli/Seed/NameIndex.cs`, `Normalize` = trim + lower-case). |
| specializationTag | no | string | max 100 chars, free text | Set only at creation. Not updated on re-run when the team already exists. |
| capacities | no | array of capacity rows | see below | Default `[]`. Each row is upserted every run. |

### teams[].capacities[]

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| year | yes | integer | 2000-2200 | `src/ProductManager.Api/Contracts/ExecutionContracts.cs` line 51. |
| month | yes | integer | 1-12 | Calendar month. (year, month) is the period key; one row per team per period. |
| points | yes | decimal | 0 to 1,000,000, in the workspace's points convention | `ExecutionContracts.cs` line 53. A row with 0 is stored as a 0-capacity period, not deleted (`Team.cs` lines 39-45). |

## Units and conversions

What the code says about the unit: "Points are FTE-months or story points - a unit choice made
in data" (`src/ProductManager.Domain/Execution/Team.cs` line 38; repeated on
`SetTeamCapacityRequest` in `src/ProductManager.Api/Contracts/ExecutionContracts.cs` line 49
and on `pm team set-capacity --points` in `src/ProductManager.Cli/Commands/TeamCommands.cs`
line 87). The system never converts points; it only compares them.

The comparison it makes (`CapacityFeasibilityService.cs` lines 41-60): for every (team, year,
month), the sum of effort-demand points across the projects planned on a roadmap is compared to
that team's capacity points for the same (year, month). Demand above capacity produces a
warning with `overallocationPoints = demand - capacity`. So the only hard rule is: capacity
points and effort-demand points must be in the same unit, per calendar month.

Recommended convention: 1 point = 1 FTE-week of team effort. It is fine-grained enough for
small projects, converts cleanly from headcount, and does not depend on a team's story-point
scale. Whatever you pick, write it down in the onboarding answers; there is no field for it in
the data.

Conversions to points per month (round to one decimal):

| source figure | to FTE-weeks (recommended) | to FTE-months | to working days |
|---|---|---|---|
| N engineers on the team, focus factor f | N x 4.33 x f | N x f | N x 21 x f |
| X FTE-months | X x 4.33 | X | X x 21 |
| D working days | D / 5 | D / 21 | D |
| S story points, monthly velocity V per team | use story points directly: capacity = V, demand in the same story points | same | same |

Worked example (Harborline, focus factor 0.8, FTE-weeks): Core Platform has 7 engineers ->
7 x 4.33 x 0.8 = 24.2 -> 24 points/month. Integrations has 3.5 FTE -> 12. Web has 4.6 FTE ->
16.

Other unit facts:

- Periods are calendar months. There is no weekly or quarterly capacity; spread a quarterly
  budget evenly across its three months.
- A month with no capacity row counts as 0 when demand exists (`CapacityFeasibilityService.cs`
  line 51) and the warning carries `capacityUndefined: true`. Enter a row for every month in the
  planning horizon, even if it repeats.
- Decimals are allowed (`points` is a decimal).

## Seed JSON shape

```json
{
  "workspace": {
    "name": "Harborline"
  },
  "teams": [
    {
      "name": "Core Platform",
      "specializationTag": "backend",
      "capacities": [
        { "year": 2026, "month": 9, "points": 24 },
        { "year": 2026, "month": 10, "points": 24 },
        { "year": 2026, "month": 11, "points": 24 },
        { "year": 2026, "month": 12, "points": 24 }
      ]
    },
    {
      "name": "Integrations",
      "specializationTag": "integrations",
      "capacities": [
        { "year": 2026, "month": 9, "points": 12 },
        { "year": 2026, "month": 10, "points": 12 },
        { "year": 2026, "month": 11, "points": 12 },
        { "year": 2026, "month": 12, "points": 12 }
      ]
    },
    {
      "name": "Web",
      "specializationTag": "frontend",
      "capacities": [
        { "year": 2026, "month": 9, "points": 16 },
        { "year": 2026, "month": 10, "points": 16 },
        { "year": 2026, "month": 11, "points": 16 },
        { "year": 2026, "month": 12, "points": 16 }
      ]
    }
  ]
}
```

## Loading and idempotency

Command (from the repo root, API running, CLI signed in):

```
dotnet run --project src/ProductManager.Cli -- seed --file <path-to-your-seed>.json
```

Position in the load order (`SeedLoader.RunAsync`, `src/ProductManager.Cli/Seed/SeedLoader.cs`
lines 23-48): workspace first, then market, marketDynamics, teams, initiatives, features,
projects, roadmap. Workspace and teams therefore run before anything that references a team.

Workspace rule (`SeedLoader.LoadWorkspaceAsync`, lines 52-77). The API bootstraps exactly one
workspace on first run and the frontend always shows the default one, which is the oldest by
`createdAt` (`src/ProductManager.Api/Controllers/WorkspacesController.cs` lines 17-24). The
loader never creates a second workspace:

- `workspace` key absent or `name` blank: the default workspace is used as-is; tally
  `workspace ... matched 1`.
- A workspace with that name already exists (case-insensitive): it is used; `matched 1`.
- No workspace with that name: the loader fetches the default workspace and renames it to
  `name`; tally `workspace created 1` (it counts the rename as the create).

Consequence: `pm workspace create` exists but is not what onboarding should use - a second
workspace would never be shown by the frontend. Put one `workspace` block in the seed file.

Team rule (`SeedLoader.LoadTeamsAsync`, lines 81-97):

- Teams are listed for the workspace and matched by normalized name. A match is reused
  (`team matched`), a miss is created with `name` and `specializationTag` (`team created`).
- On a match, `specializationTag` is not updated. Use `pm team update <id> --name <name> --tag
  <tag>` to change it.
- Every `capacities[]` row is sent to `PUT /api/teams/{id}/capacities`, which replaces the row
  for that (year, month) (`Team.SetCapacity`, `Team.cs` lines 39-45). Re-running a file with
  changed points overwrites; removing a row from the file does not delete the stored period.
  Capacity rows are not counted in the tally.

Tally printed by the CLI (`src/ProductManager.Cli/Commands/SeedCommand.cs` lines 46-51), for
the example above on a fresh instance:

```
Loaded <path>:
  team                 created   3, matched   0
  workspace            created   1, matched   0
```

On a second run of the same file: `team created 0, matched 3` and `workspace created 0,
matched 1`, and the capacity rows are re-applied with identical values.

Failure modes at this stage: an unreachable API or expired token fails before anything is
written; a `points` value outside 0-1,000,000 or a month outside 1-12 is rejected by the API
with a 400 after the team itself was created (re-run after fixing the file - the team will be
matched).

## Verification

CLI (JSON output):

- `pm workspace get-default` - `name` should be the seed's workspace name.
- `pm workspace list` - exactly one item is the expected state.
- `pm team list --workspace-id <workspaceId>` - one item per team; each item's `capacities`
  array lists `{year, month, points}` rows in period order (`ExecutionMapping.ToDto`,
  `ExecutionContracts.cs` lines 132-137). Check that every month of the planning horizon is
  present for every team.
- `pm team get <teamId>` - a single team with the same shape.

Frontend: `http://localhost:3000/teams` (`frontend/src/app/(app)/teams/page.tsx`). Correct
looks like: the three teams listed, each with its tag, and the capacity editor showing the
points you entered per month. After [04-roadmap](04-roadmap.md) is loaded,
`http://localhost:3000/roadmap` shows a warnings panel; with the Harborline numbers it should
be empty (the example demands never exceed capacity).

## Common mistakes

- Mixing units: capacity in FTE-months (small numbers) and effort demand in story points
  (large numbers) makes every month look overallocated, or the reverse hides real conflicts.
  One convention per workspace, recorded in the answers.
- Entering capacity for the year only, not per month. Every (year, month) with demand needs a
  row; missing months are treated as capacity 0 and flagged `capacityUndefined`.
- Creating a second workspace with `pm workspace create` and loading into it. The frontend
  shows the oldest workspace only; the loader's rename behaviour exists to avoid this.
- Renaming a team between runs. The natural key is the name, so "Core Platform" and "Platform
  Core" are two teams. Rename with `pm team update` instead, then fix the seed file.
- Expecting the loader to update `specializationTag` on an existing team - it does not; only
  new teams take the tag from the file.
- Using `month: "09"` (a string) or `year: 26`. Both are integers; the year must be four
  digits between 2000 and 2200.
