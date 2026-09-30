# 04 - Roadmap

This dataset creates the workspace's live roadmap and plans projects on it: each roadmap item
puts one project on the timeline with a planned start, a planned end and a display order. The
roadmap is what the timeline page draws, what the feasibility check evaluates (team-capacity
overallocation and dependency ordering), and what a simulation run walks to find out when each
feature's coverage deltas become real. It maps to the PRD section "Domain Model > Execution
side (roadmap & delivery)" (row Roadmap) and to Phase 1 of "Phasing" in
`docs/PRDs/product_requirements.md`.

CLI shorthand used below: `pm` means `dotnet run --project src/ProductManager.Cli --`.

## Tier and prerequisites

- Tier: A (mandatory). Without a live roadmap the timeline page is empty and no simulation can
  reference a plan.
- Prerequisites: [01-workspace-and-teams](01-workspace-and-teams.md) and
  [03-initiatives-projects-features](03-initiatives-projects-features.md) must be loaded; every
  `items[].projectName` must resolve to a project in the same file or already in the workspace.
- Typical load time: seconds. One call to create the roadmap, one per item, one to make it live.

## Questions to ask the user

1. What is the plan called and which version is this? Default name: "<Company> <year> roadmap"
   (Harborline: "Harborline 2026 roadmap"); default `versionName`: "v1". The version label is
   free text (`src/ProductManager.Domain/Execution/Roadmap.cs` line 16 gives "2026-Q3 board
   deck" as an example).
2. Which projects from 03 are actually on the plan? Default: every project whose status is
   `Planned`, `InProgress` or `Done` with dates; leave `Proposed` projects off unless the user
   wants them on the timeline as candidates. The Harborline example plans all three, including
   the `Proposed` one, so the feasibility check covers the whole portfolio.
3. For each planned project, what are the planned start and end dates? Default: the project's
   own `startDate` / `endDate` from 03. The two are separate fields; the roadmap dates are the
   ones that matter (see "Units and conversions").
4. In what order should projects appear on the timeline (`sortOrder`)? Default: by planned
   start, then by initiative. Numbering starts at 0.
5. Should this roadmap be the live one? Default: yes (`isLive: true`). Exactly one live roadmap
   per workspace is the intended state; scenarios are branches created later from the UI or
   `pm roadmap branch`, not at onboarding.
6. Are there dependencies the plan must respect? Already captured in 03 as
   `dependsOnProjectNames`; here, confirm that each dependent project's planned start is not
   before its dependency's planned end, or accept that the feasibility check will flag it.

**Consequential choices** here: which date a roadmap item follows when sources disagree, a sales commitment against the plan, a Done epic under an unreleased capability (`planningDate`), and whether proposed projects go on the timeline. These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only.

## Data requirements

### roadmap

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| name | yes | string | max 200 chars, trimmed | Natural key within the workspace, matched case-insensitively (`src/ProductManager.Cli/Seed/NameIndex.cs`). |
| versionName | no | string | max 200 chars, free text | Set only at creation; not updated when the roadmap already exists. |
| isLive | no | boolean | `true` / `false` | Default `true` (`src/ProductManager.Cli/Seed/SeedModels.cs` line 104). When true the loader calls make-live after planning the items. |
| items | no | array of roadmap items | see below | Default `[]`. Each item is upserted every run. |

### roadmap.items[]

| field | required | type | unit / format / allowed values | notes |
|---|---|---|---|---|
| projectName | yes | string | name of a project | Resolved against this file's `projects[]`, then against a paged workspace-wide project listing (`SeedLoader.ProjectResolver`, `SeedLoader.cs` lines 1045-1069), so a roadmap-only file resolves projects an earlier file created. |
| start | yes | string | `yyyy-MM-dd` | Planned start (`PlannedStart`). Required; the loader uses `ParseDate`, not the optional variant (`SeedLoader.cs` line 271). |
| end | yes | string | `yyyy-MM-dd` | Planned end (`PlannedEnd`). Must not precede `start` (`Roadmap.cs` lines 50-51). |
| sortOrder | no | integer | 0-based display order | When omitted the item is appended after the current maximum, or 0 on an empty roadmap (`Roadmap.cs` lines 53-54). |

## Units and conversions

- Dates are ISO `yyyy-MM-dd`. Both `start` and `end` are calendar days, inclusive.
- Roadmap item dates versus project dates. A project has its own `startDate` / `endDate`
  (03), and its roadmap item has `plannedStart` / `plannedEnd` (this file). They are stored
  separately and nothing keeps them in sync. Which one is used where:
  - Timeline bars: the roadmap item's `plannedStart` / `plannedEnd`
    (`frontend/src/components/roadmap/timeline.tsx` line 176).
  - Sequencing violations: the roadmap item's dates
    (`src/ProductManager.Domain/Services/RoadmapSequencingService.cs` line 77).
  - When a delivered feature's coverage delta becomes real in a simulation: the roadmap item's
    `plannedEnd` (`src/ProductManager.Api/Controllers/SimulationRunsController.cs` lines
    342-345 and 376).
  - The project's own dates are used only as the default when a user drags an unplanned project
    onto the roadmap in the UI (`frontend/src/app/(app)/roadmap/page.tsx` lines 144-145).
  For onboarding, copy the project dates into the roadmap item unless the user says the plan
  differs from the project record; the roadmap item is the authoritative schedule.
- Capacity feasibility does not read dates at all. It sums each planned project's
  `effortDemands` per (team, year, month) and compares the sum with the team's capacity for that
  month (`src/ProductManager.Domain/Services/CapacityFeasibilityService.cs` lines 41-60;
  `RoadmapsController.cs` lines 238-252 restricts the projects to those planned on this
  roadmap and the teams to the roadmap's workspace). So a project whose demand rows fall in
  months outside its planned window is not flagged for that; keep demand months inside the
  planned window yourself.
- `overallocationPoints = demandPoints - capacityPoints`, in the workspace's points convention
  from 01. `capacityUndefined: true` means the team has no capacity row for that month and 0 was
  assumed.
- Sequencing violation kinds (`RoadmapSequencingService.cs` lines 5-15, produced at lines
  59-84; `SequencingViolationKind` in `src/ProductManager.Cli/Client/Enums.cs` lines 48-53):
  - `StartsBeforeDependencyEnds`: the dependent item's `plannedStart` is strictly before the
    dependency item's `plannedEnd`. Starting on the same day the dependency ends is allowed.
  - `DependencyNotOnRoadmap`: the project depends on a project that has no item on this
    roadmap.
  - `DependencyCycle`: the dependency edges among planned projects form a cycle.
- `isFeasible` is true only when there are no capacity warnings and no sequencing violations
  (`RoadmapsController.cs` line 260).
- `isLive`: making a roadmap live retires every other live roadmap in the workspace
  (`RoadmapsController.cs` lines 88-100 and 265-272). The live roadmap cannot be deleted (lines
  80-81). `versionName` is a label; it does not create a snapshot. Branching (`pm roadmap
  branch <id> --name <name>`) creates a non-live deep copy with `parentRoadmapId` set
  (`Roadmap.cs` lines 61-74); `adopt` merges a branch back. Neither is needed at onboarding.

## Seed JSON shape

Excerpt of `docs/onboarding/example-seed.json`. The three project names are the ones defined in
[03-initiatives-projects-features](03-initiatives-projects-features.md).

```json
{
  "roadmap": {
    "name": "Harborline 2026 roadmap",
    "versionName": "v1",
    "isLive": true,
    "items": [
      { "projectName": "Carrier Connect v1", "start": "2026-09-01", "end": "2026-10-31", "sortOrder": 0 },
      { "projectName": "Cycle count workflows", "start": "2026-10-01", "end": "2026-11-30", "sortOrder": 1 },
      { "projectName": "Wave picking engine", "start": "2026-11-01", "end": "2026-12-31", "sortOrder": 2 }
    ]
  }
}
```

With the capacities from 01 and the effort demands from 03 this plan is feasible: Core
Platform's busiest month is 2026-11 with 16 + 8 = 24 points against 24 capacity, Web peaks at
14 of 16, and "Wave picking engine" starts 2026-11-01, the day after its dependency "Carrier
Connect v1" ends.

## Loading and idempotency

Command (from the repo root, API running, CLI signed in):

```
dotnet run --project src/ProductManager.Cli -- seed --file <path-to-your-seed>.json
```

Position in the load order (`SeedLoader.RunAsync`, `src/ProductManager.Cli/Seed/SeedLoader.cs`
lines 23-48): the roadmap is loaded last (line 45), after projects, and only if the `roadmap`
key is present.

A roadmap-only file loads on its own: `items[].projectName` is looked up first in the projects
this run created, then in a paged listing of every project in the workspace, fetched once and
only on the first miss (`SeedLoader.ProjectResolver`, lines 1045-1069). The `projects[]` key
does not have to be repeated here.

Rule (`SeedLoader.LoadRoadmapAsync`, lines 259-279):

1. Roadmaps are listed for the workspace and matched by normalized name. A match is reused
   (`roadmap matched 1`); a miss is created with `name` and `versionName` and `isLive: false`
   (`roadmap created 1`). `versionName` is not updated on a match.
2. Every item is sent to `POST /api/roadmaps/{id}/items` (`PlanProject`), which removes any
   existing item for that project and adds the new one (`Roadmap.cs` lines 47-57). Re-running
   with changed dates reschedules; re-running unchanged is a no-op in effect. Removing an item
   from the file does not unplan it - use `pm roadmap unplan <roadmapId> --project-id
   <projectId>`.
3. If `isLive` is true (the default), `POST /api/roadmaps/{id}/make-live` runs after the items
   and retires any other live roadmap in the workspace.

Tally line printed by the CLI (`src/ProductManager.Cli/Commands/SeedCommand.cs` lines 46-51):

```
  roadmap              created   1, matched   0
```

Items are not counted in the tally. A second run prints `roadmap created 0, matched 1`.

Failure modes: an `items[].projectName` that does not resolve is a preflight problem ("Seed file
references '<name>' ..."), and nothing in the file is written, the roadmap included. `end`
before `start` is rejected by the API ("Planned end cannot precede planned start.") and stops the
run at that item; earlier items stay planned, and rerunning the fixed file resumes.

## Verification

CLI (JSON output):

- `pm roadmap list --workspace-id <workspaceId>` - live roadmaps sort first
  (`RoadmapsController.cs` line 28). Expect one item with `isLive: true`, `versionName: "v1"`
  and `items` ordered by `sortOrder`, each item carrying `projectId`, `plannedStart`,
  `plannedEnd`, `sortOrder`. `--is-live true` filters to the live one.
- `pm roadmap get <roadmapId>` - the same roadmap with its items.
- `pm roadmap feasibility <roadmapId>` - `{ roadmapId, isFeasible, capacityWarnings,
  sequencingViolations }`. For the Harborline example: `isFeasible: true`, both arrays empty.
  To see the check work, raise "Wave picking engine"'s 2026-11 Core Platform demand to 20 and
  re-run the seed: one warning with `teamName: "Core Platform"`, `year: 2026`, `month: 11`,
  `capacityPoints: 24`, `demandPoints: 28`, `overallocationPoints: 4`. Move its `start` to
  `2026-10-15`: one violation of kind `StartsBeforeDependencyEnds`.

Frontend: `http://localhost:3000/roadmap` (`frontend/src/app/(app)/roadmap/page.tsx`). Correct
looks like: the live roadmap selected, three bars on the timeline at the planned dates in
sortOrder, an empty unplanned tray (every project is planned), and an empty warnings panel.
Drag-and-drop on this page rewrites `plannedStart` / `plannedEnd` through the same API the
loader used.

## Common mistakes

- Leaving `start` or `end` out or empty. Both are required strings; the loader fails the item
  with "'' is not a valid date for roadmap item start (expected yyyy-MM-dd)".
- Planning a dependent project to start before its dependency's planned end because the
  project-level dates in 03 were copied without checking. The check is strict "before"; same-day
  is fine.
- Planning only some of a dependency chain. A planned project whose dependency is not planned
  produces `DependencyNotOnRoadmap`; either plan the dependency or remove the edge in 03.
- Assuming the project's `startDate` / `endDate` drive the timeline or the simulation. They do
  not; the roadmap item's dates do. Keep them in sync yourself.
- Loading a second roadmap with `isLive: true` and expecting both to be live. The newest one
  wins and the previous live roadmap is retired; use `isLive: false` for a scenario, or branch
  from the live roadmap instead.
- Expecting effort demand months outside the planned window to be flagged. Capacity feasibility
  is per (team, month) across planned projects regardless of item dates; the sequencing check is
  the only one that reads dates.
