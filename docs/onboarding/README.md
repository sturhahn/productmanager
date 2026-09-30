# Agentic onboarding guide (orchestrator level)

You are the orchestrator agent. Your job is to take a fresh checkout of ProductManager and a human
who has data somewhere (spreadsheets, a CRM, Jira, a finance system) and end with a running
instance loaded with that human's data.

Read this file end to end before you start. Do not read the dataset files under `datasets/` - each
one is read by the subagent you spawn for it. (Discovery, section 6, and portfolio selection, section 8,
are the exceptions: they read a requested dataset's tier, data-requirements and units sections only to
map evidence and shape the mapping.)

Shorthand used below: `pm` means `dotnet run --project src/ProductManager.Cli --`. There is no
installed `pm` binary. Every command is run from the repository root.

Related reading: [`../../CLAUDE.md`](../../CLAUDE.md) (repo conventions),
[`../../seed/CLAUDE.md`](../../seed/CLAUDE.md) (seed loading rules),
[`../PRDs/product_requirements.md`](../PRDs/product_requirements.md) (the authoritative product
spec - the *why* behind every dataset).

---

## 1. How to use this guide

### The orchestrator / subagent split

| Role | Reads | Does |
|---|---|---|
| Orchestrator (you) | this file, plus [`discovery/README.md`](discovery/README.md) when sources are connected and [`portfolio/README.md`](portfolio/README.md) when Aha! or Jira are connected | environment checks, stack up, source discovery, consolidated interview, portfolio selection, spawns subagents, assembles and loads, verifies |
| Dataset subagent | one file under `datasets/` | asks its dataset's questions, converts the human's source data, emits a seed JSON fragment, loads it, reports |

This is progressive disclosure on purpose: the 12 dataset files together are far too long for one
context, and most onboardings need only three or four of them.

### Every dataset file has the same skeleton

Each file under `datasets/` uses these H2 headings, in this order: `## Tier and prerequisites`,
`## Questions to ask the user`, `## Data requirements`, `## Units and conversions`,
`## Seed JSON shape`, `## Loading and idempotency`, `## Verification`, `## Common mistakes`. Rely
on that shape: tell each subagent to work the sections in order.

### Subagent brief template

Copy this block, fill the bracketed slots, and use it as the subagent's prompt. One subagent per
dataset file.

```
Repo: /absolute/path/to/ProductManager  (work from the repo root; all paths absolute)

Read docs/onboarding/datasets/<NN-name>.md in full before writing anything. Follow its
eight sections in order. Do not read the other dataset files.

The human has already answered this dataset's questions:
<paste the human's answers to the "Questions to ask the user" section, verbatim>

Answers already agreed in the consolidated interview (section 7) - reuse them, do not re-ask:
<paste the agreed answers that affect this dataset, e.g. "Q1: segments come from Account.Segment__c";
"none" when there was no discovery>

Source manifest: <absolute path to source-manifest.json, or "none - no connected sources">
If given, read this dataset's `datasetReadiness` entry and the objects it cites; use the source IDs
and `rawResponseFiles` instead of re-querying. Only ask the human about what the manifest and the
agreed answers leave open. Discovery is read-only: do not re-run it or widen its scope.

Portfolio mapping: <absolute path to portfolio-mapping.json, or "none - no Aha!/Jira sources">
If given, take every initiative, project, feature, team and business process name, every inclusion
decision and every effort or capacity unit from it. Never coin your own name, merge or split records,
or include a record the mapping holds out or excludes; a name you think is wrong goes in your report
for the orchestrator. Prepare the fragment only from the mapping's selected records.

Source data location:
<path to spreadsheet / export / API, plus any credentials handling the human specified>

Already loaded into this instance (so by-name references to these will resolve):
<list the datasets already loaded, e.g. "01, 02, 03">

Deliverables:
1. A seed JSON fragment written to <path>/<NN>-<slug>.json containing only the top-level
   SeedFile keys this dataset owns.
2. Load it:  dotnet run --project src/ProductManager.Cli -- seed --file <path>/<NN>-<slug>.json
3. Run the verification commands from the file's "## Verification" section.
4. Report back: the tally line the CLI printed, the verification output, every judgement
   call you made converting the source data (units, defaults, gaps you filled), and
   anything you could not source and left out.

Rules: the API is the only write path - never touch the database directly. Do not invent
fields; the schema is src/ProductManager.Cli/Seed/SeedModels.cs. Do not edit the dataset
file or any other repo file. Do not commit.
```

---

## 2. Prerequisites and verification checks

Run every check before you touch data. Report failures to the human rather than trying to install
things yourself.

| Requirement | Minimum version | Check command | Expected output |
|---|---|---|---|
| .NET SDK | 9.0.x installed | `dotnet --list-sdks` | at least one line starting `9.0.` (`dotnet --version` should print `9.0.*`) |
| Node.js | 20.9.0 or newer | `node --version` | `v20.9.0` or higher |
| npm | ships with Node | `npm --version` | any version |
| Docker Engine | any current | `docker --version` | a version string |
| Docker Compose | v2 (the `docker compose` subcommand, not `docker-compose`) | `docker compose version` | `Docker Compose version v2...` or newer |
| git | any current | `git --version` | a version string |

Notes on the .NET check: every project in this repo targets `net9.0` and every
`Microsoft.EntityFrameworkCore` / `Microsoft.AspNetCore.*` package is pinned to `9.0.10`. A
machine with only a 10.x SDK installed will not build unless the .NET 9 targeting pack is also
present, and there is no `global.json` pinning the SDK. If `dotnet --list-sdks` shows no `9.0.`
line, stop and ask the human to install the .NET 9 SDK.

Note on the Node check: `frontend/package.json` has no `engines` field and there is no `.nvmrc`.
The requirement comes from the pinned `next@16.3.3`, whose own package metadata declares
`engines.node >= 20.9.0`. Confirm after `npm ci` with:

```
node -e "console.log(require('./frontend/node_modules/next/package.json').engines)"
```

Once the versions pass, restore and build:

| Step | Command | Expected |
|---|---|---|
| EF tooling | `dotnet tool restore` | `Tool 'dotnet-ef' (version '9.0.10') was restored.` |
| Backend build | `dotnet build` | `Build succeeded`, 0 errors |
| Frontend deps | `cd frontend && npm ci` | completes without peer-dependency errors |

`dotnet test` is optional here but useful as a smoke test. Be aware it regenerates
`openapi/productmanager.json` as a side effect (see [`../../CLAUDE.md`](../../CLAUDE.md)); if that
file changes, leave the change for the human to review rather than committing it.

---

## 3. Running the stack

### 3.1 Database

```
docker compose up -d --wait
```

Postgres 17, published on host port **5433** (see `docker-compose.yml`). `--wait` blocks on the
container healthcheck, so when it returns the database really is accepting connections. The
connection string the API uses in Development is in
`src/ProductManager.Api/appsettings.Development.json`.

### 3.2 API

```
dotnet run --project src/ProductManager.Api
```

Serves `http://localhost:5072` (profile `http`, `src/ProductManager.Api/Properties/launchSettings.json`).

**Migrations run automatically at startup.** `DatabaseInitializer` is registered as a hosted
service in `src/ProductManager.Api/Program.cs:129`, and
`src/ProductManager.Api/Infrastructure/DatabaseInitializer.cs:29-30` calls
`db.Database.MigrateAsync(...)` when the provider is Npgsql (it calls `EnsureCreatedAsync` only for
the SQLite test provider). You do **not** need to run `dotnet ef database update` by hand. This can
be disabled with the config key `Database:InitializeOnStartup` (default `true`,
`DatabaseInitializer.cs:21`), so if someone has set that to `false` you must apply migrations
manually instead.

One more thing that startup does: `DatabaseInitializer.EnsureDefaultWorkspaceAsync` creates a
single workspace named `Default Workspace` on first run
(`DatabaseInitializer.cs:47-57`). Seed loading does **not** create a second workspace - see
`SeedLoader.LoadWorkspaceAsync` in `src/ProductManager.Cli/Seed/SeedLoader.cs`, which adopts the
bootstrap workspace and *renames* it to the seed file's `workspace.name`, because Phase 1 is
single-workspace and the frontend always resolves `GET /api/workspaces/default`.

Checks:

| Check | Command | Expected |
|---|---|---|
| API is up | `curl -fsS http://localhost:5072/swagger/v1/swagger.json \| head -c 200` | JSON starting `{"openapi":...` |
| API + DB reachable | `curl -fsS http://localhost:5072/api/health` | `{"status":"ok","databaseReachable":true}` |
| Swagger UI (Development only) | open `http://localhost:5072/swagger` | the ProductManager API v1 explorer |

`/api/health` is anonymous (`src/ProductManager.Api/Controllers/HealthController.cs`), so it works
before you hold a token. Note that a broken database does **not** stop the API from starting:
startup failures are swallowed by default (`Database:ContinueOnInitializationFailure`), so Swagger
can be up while the database is not. Always check `/api/health` too, not just Swagger.

### 3.3 Schema is current

```
dotnet ef migrations list --project src/ProductManager.Api
```

Every migration should be listed with no `(Pending)` suffix. As of this writing the expected list
is:

```
20260829162844_InitialSchema
20260829183300_Phase2
20260830021035_Phase3
20260901023821_AddInitiativeStrategicIntent
```

Migrations live in `src/ProductManager.Api/Persistence/Migrations/`. The command needs
`dotnet tool restore` to have run and the database to be up (it queries
`__EFMigrationsHistory`). If anything shows `(Pending)`, the API has not started against this
database yet - start it, or run `dotnet ef database update --project src/ProductManager.Api`.

### 3.4 CLI authentication

```
dotnet run --project src/ProductManager.Cli -- auth login --password productmanager-dev
dotnet run --project src/ProductManager.Cli -- auth whoami
```

`auth login` (`src/ProductManager.Cli/Commands/AuthCommands.cs`) has one required option,
`--password`; it exchanges the password for a bearer token and stores it in
`~/.productmanager/config.json`. `auth whoami` prints `baseUrl`, `signedIn` and `expiresAt`.
Expect `"signedIn": true`.

The password `productmanager-dev` lives in `src/ProductManager.Api/appsettings.Development.json`.
It is a development-only value and must be overridden before any deployment. If the API is not on
`http://localhost:5072`, pass the root option `--base-url` (defined in
`src/ProductManager.Cli/Commands/CliInfrastructure.cs`) on every command.

Tokens last 720 minutes (12 hours) - `Auth:TokenLifetimeMinutes` in the same settings file. A long
onboarding may outlive one; just re-run `auth login`.

### 3.5 Frontend

```
cd frontend
cp .env.example .env.local
npm run dev
```

Serves `http://localhost:3000` and talks to the API through `NEXT_PUBLIC_API_URL`
(`frontend/.env.example` defaults it to `http://localhost:5072`). Sign in with the same dev
password.

---

## 4. Optional: load the demo dataset first

`seed/` ships a three-layer fintech demo: `seed/phase1-seed.json` (execution and roadmap plus a
base market), then `seed/phase2-seed.json`, then `seed/phase3-seed.json`. They must be loaded in
that order - phase2 and phase3 resolve references by name against what phase1 created (see
[`../../seed/CLAUDE.md`](../../seed/CLAUDE.md)).

```
dotnet run --project src/ProductManager.Cli -- seed --file seed/phase1-seed.json
dotnet run --project src/ProductManager.Cli -- seed --file seed/phase2-seed.json
dotnet run --project src/ProductManager.Cli -- seed --file seed/phase3-seed.json
```

This is useful to show the human a fully populated product before asking them for their own data.

**But this instance has exactly one workspace.** The demo and the human's real data would land in
the same workspace, and the real seed would rename the workspace while leaving every fintech
entity in place. So pick one:

- **Recommended: demo first, then wipe.** Load the demo, walk the human through the UI, then run
  `docker compose down -v` followed by `docker compose up -d --wait` and restart the API before
  loading real data. `down -v` removes the `productmanager-pgdata` volume, which **permanently
  deletes every row in the database** - the demo data and anything else in it. Only do this when
  the human has confirmed there is nothing they want to keep.
- **Or: skip the demo.** Load only [`example-seed.json`](example-seed.json) as a smoke test of the
  loader. It is small, coherent, and loads in a single pass, but it still creates real entities in
  the workspace, so the same "wipe before real data" rule applies.

---

## 5. The task list

Twelve datasets, in load order. Tiers: **A** mandatory, **B** static market view, **C** aggregate
Monte Carlo simulation, **D** firm-level engine plus calibration.

| # | File | Loads | Tier | Prerequisites | Typical questions |
|---|---|---|---|---|---|
| 01 | [datasets/01-workspace-and-teams.md](datasets/01-workspace-and-teams.md) | workspace name, teams, per-month capacities | A | none | team list, capacity unit, which months |
| 02 | [datasets/02-market-structure.md](datasets/02-market-structure.md) | market, segments, business processes, approaches, competitors, products and modules, prices | B (mandatory if you want any market side) | 01 | who you sell to, what you sell, who you compete with, list prices |
| 03 | [datasets/03-initiatives-projects-features.md](datasets/03-initiatives-projects-features.md) | initiatives, projects (dependencies, effort demands, delivered features), features and their deltas | A | 01; 02 if features carry coverage deltas | the current plan, per-team effort, what each project ships |
| 04 | [datasets/04-roadmap.md](datasets/04-roadmap.md) | the roadmap and its planned items | A | 03 | planning horizon, start and end per project |
| 05 | [datasets/05-coverage-and-needs.md](datasets/05-coverage-and-needs.md) | coverage assessments, needs | B | 02 | how well each product covers each process, what buyers must accomplish |
| 06 | [datasets/06-segment-economics.md](datasets/06-segment-economics.md) | segment stock states, module penetrations | C | 02 | client counts, ARPU, churn, module attach rates |
| 07 | [datasets/07-covariates.md](datasets/07-covariates.md) | covariates and their observations | C | 01 | which external series drive demand, where the history lives |
| 08 | [datasets/08-assumption-sets.md](datasets/08-assumption-sets.md) | assumption sets and their parameters | C | 02, 06; 07 for covariate links | rates and their uncertainty, where each number came from |
| 09 | [datasets/09-market-events.md](datasets/09-market-events.md) | market events and rate multipliers | C | 02 | known shocks, competitor migrations, regulatory dates |
| 10 | [datasets/10-channels-and-integrations.md](datasets/10-channels-and-integrations.md) | channels (segment reach, product affinity), integrations | D | 02 | routes to market, reach per segment, product integrations |
| 11 | [datasets/11-firms-and-relationships.md](datasets/11-firms-and-relationships.md) | firms and firm-product relationships | D | 02; 10 if firms arrive through channels | the client and prospect roster, who uses what |
| 12 | [datasets/12-historical-facts.md](datasets/12-historical-facts.md) | opportunities, churn events, arrival counts | D | 11; 10 for channel attribution | CRM history, churn log, lead volumes, bi-temporal recording dates |

### What each tier unlocks

Tier A alone is a complete roadmap tool. Every later tier is additive; a human can stop after any
of them and the instance stays coherent.

| Tier | Frontend pages that become useful |
|---|---|
| A | `/roadmap`, `/initiatives`, `/projects`, `/features`, `/teams` |
| B | `/markets`, `/segments`, `/products`, `/competitors`, `/processes`, `/coverage` |
| C | `/assumptions`, `/simulate` (aggregate runs) |
| D | `/firms`, `/channels`, `/opportunities` (labeled "Pipeline"), `/calibration`, plus firm-level runs on `/simulate` |

Routes verified against `frontend/src/app/(app)/` and the sidebar in
`frontend/src/components/app-sidebar.tsx`.

Say this to the human plainly: **A is mandatory, everything else is a choice.** Do not start
building tier C parameter sets for someone who only wanted a roadmap.

---

## 6. Source discovery (before the interview and any dataset work)

**When:** the human has connected sources you can read (Aha!, Jira, Salesforce, via connectors or
APIs you already have). **Skip it** when the only sources are spreadsheets or exports the human hands
you; go straight to section 7, which then covers those files.

**Why:** questions about data that is already reachable waste the human's time, and unbounded
extraction imports irrelevant records.

Procedure - the detail and every field is in [`discovery/README.md`](discovery/README.md):

1. Agree scope boundaries first: outcome and tier, planning horizon, commercial observation period,
   and the source containers (Aha! workspace, Jira projects, Salesforce org and account/opportunity
   filters). An unspecified boundary is a question, never permission to crawl.
2. Inspect definitions, statuses, relationships and bounded samples; then extract only the agreed
   population, following pagination to its end.
3. Record missing access (`accessLimitations`) separately from absent data (`absentData`).
4. Propose authority by concept (Salesforce commercial, Aha! product intent, Jira execution) and ask
   for exceptions.
5. Map datasets 01-12 to evidence with one of four classifications: ready, requires a mapping
   decision, requires another source, intentionally deferred. Keep observed values apart from
   proposed assumptions.
6. Write the manifest ([`discovery/source-manifest.example.json`](discovery/source-manifest.example.json))
   and a short assessment ([`discovery/source-assessment.example.md`](discovery/source-assessment.example.md))
   outside the repo, and carry both into section 7.

Hard rules:

- Discovery is read-only and creates nothing: no ProductManager entities, no writes to any source
  system, no database access. Loading stays API-only and later, exactly as sections 9 and 10 describe.
- It precedes all per-dataset preparation and any real-data load. Environment checks, authentication,
  load order and verification (sections 2, 3, 9, 10) are unchanged.
- Store no credentials, tokens or connection strings in any artifact.
- Finance costs and market-wide totals stay gaps when no accessible system holds them; a Jira Done
  status is not customer availability; a sales commitment is not a delivery date.

---

## 7. Interviewing the human

With connected sources, run discovery (section 6) first. The interview is then **one consolidated
question list**, ranked by consequence (how many datasets and tiers change with the answer), taken
from the manifest's `openQuestions` and ordered highest first. Give every question candidate answers
found in the sources, so the human picks or corrects instead of supplying data. Ask each question
once and record the answers; they are pasted into every subagent brief (the "Answers already agreed"
slot) so no dataset agent re-asks them.

If the evidence cannot support the tier the human asked for, say so and offer the narrower tier from
`tierReadiness.offer` (for example: "A now, B once you supply a coverage grid"). Commercial gaps never
block tier A. Let the human choose.

Without connected sources the same list applies with no candidates: ask the questions below and get a
concrete file for each. Ask the tier-level questions yourself before spawning anything; the
per-dataset questions belong to the subagents - do not pre-empt them.

1. **Which tiers do you want?** (With discovery: already stated as the requested tier; confirm.) Recommended default: start at A, add B if you want the market and
   coverage view, and only go to C or D if you actually want simulation or calibration.
2. **What is this workspace called?** Default: the company or product-line name. This renames the
   bootstrap workspace, it does not create a second one.
3. **What market are you modelling, and in what currency?** Default: one market, USD. Only needed
   from tier B onward.
4. **What unit is team capacity in, and over what period?** The schema stores decimal `points` per
   `(year, month)` per team. Pick one convention (story points per month, engineer-months, ideal
   days) and use it for both team capacity and project effort demand, or feasibility numbers are
   meaningless. Default: story points per month.
5. **How many teams, initiatives and projects, roughly?** Default assumption: fewer than 10 teams
   and fewer than 50 projects. If any dataset runs to thousands of rows, tell the subagent to
   script the conversion rather than hand-write JSON.
6. **What is the planning horizon?** (With discovery: agreed in scope; do not re-ask.) Default: the next 12 to 18 months, aligned to whole months.
7. **Where does each dataset live?** (With discovery: the manifest already answers this for connected sources; ask only for what the sources cannot hold.) Get a concrete path or export for each tier you agreed on -
   spreadsheet, CSV export, CRM report, Jira query, finance system extract. A subagent with no
   source cannot invent one.
8. **Who decides when the data is ambiguous?** Default: the subagent picks a documented default and
   flags it in its report; you surface every flag to the human at the end.

Write the answers down. You will paste the relevant slice of them into each subagent brief. With Aha! or Jira connected, section 8 comes next.

---

## 8. Portfolio selection (Aha! or Jira connected, before any dataset subagent)

**When:** Aha! or Jira sources were discovered (section 6) and tier A is requested, after the interview
(section 7) and before you spawn a subagent for datasets 01, 03 or 04. **Skip it** on the
spreadsheet-only path; the brief's "Portfolio mapping" slot is then `none`.

**Why:** Aha! and Jira hold approved plans next to stale ideas, tickets and old releases. Loading it all
gives an unrecognizable portfolio, and independent subagents would name and slice the same capability
differently. One approved mapping, made once, is the shared vocabulary.

Procedure - the detail and every field is in [`portfolio/README.md`](portfolio/README.md):

1. Run one portfolio question round (which Aha! releases are the current portfolio, how far back shipped
   capabilities matter, the stale threshold); reuse the interview's answers on availability and team unit.
2. Start from the current Aha! portfolio and follow explicit links into the agreed Jira projects. Select
   approved current work and relevant shipped capabilities; hold stale, tentative, unlinked and
   conflicting records in an exception list. Recency ranks review priority, never approves.
3. Model capability and delivery separately: an Aha! feature is not automatically a Feature and a Jira
   epic is not automatically a Project; keep `Project delivers Feature` explicit.
4. Treat links as evidence, not proof of equal scope; a name match only proposes. Contradictory links and
   several plausible matches go to the human.
5. Keep the five date meanings apart, choose the planning date by the agreed authority, flag conflicts;
   Jira Done is not shipped. Keep capacity and effort units explicit; missing estimates stay gaps.
6. Resolve dependencies inside the portfolio; surface external prerequisites, missing owners and cycles.
7. Write the mapping ([`portfolio/portfolio-mapping.example.json`](portfolio/portfolio-mapping.example.json))
   outside the repo, show the human the readable portfolio, exclusions and ranked exceptions, and put
   its path in every subagent brief.

Hard rules:

- Read-only and creating nothing, as in discovery: no ProductManager entities, no writes to Aha! or Jira.
  It is reconciliation, not reprioritization: never reorder or re-rank the plan.
- Dataset subagents for 01, 03 and 04, and for anything that references business processes, take names
  and inclusion decisions from the mapping and never coin their own vocabulary. Fragments are prepared
  only from the mapping's selected records.
- Store no credentials, tokens or connection strings in the mapping.

> **PRD5 hook - consequential-decision review.** Before any fragment is loaded (section 9), the mapping's
> inclusion decisions, exceptions and date conflicts go through the consequential-decision review.
> Link: <!-- PRD5: link the decision-register procedure here -->

---

## 9. Assembling and loading

Both strategies use the same command:

```
dotnet run --project src/ProductManager.Cli -- seed --file <path-to-file>
```

### Strategy A: one file per dataset, loaded in dependency order

Load `01` through `12`, in that numeric order, skipping the tiers the human declined. This is the
default. It gives you a small blast radius per subagent and a readable tally per dataset.

Order matters because `SeedLoader` resolves every cross-entity reference **by name against what the
API already has**, not against the file - `src/ProductManager.Cli/Seed/NameIndex.cs` builds a
case-insensitive, trimmed name-to-id index from the API's current contents. A name that has not
been created yet raises a `SeedReferenceException`, surfaced as
`Seed file '<path>' has a broken reference: ...`. So `03` cannot reference a segment that `02`
has not loaded, and `12` cannot reference a firm that `11` has not loaded.

Four resolution rules make a per-dataset fragment work without repeating the market
(`MarketResolver`, `ProjectResolver` and `FirmResolver` in `src/ProductManager.Cli/Seed/SeedLoader.cs`):

- A file with **no `market` section** resolves initiative `targetSegmentNames` and feature
  `productName` / delta names against the workspace's market, fetched from the API on first need.
  Zero markets keeps the old "no market defined in the seed file" error; exactly one gives full
  indexes; two or more raise a `SeedReferenceException` telling you to add a
  `"market": { "name": "..." }` stub.
- A **`market` section carrying only a name** matches the existing market, creates nothing, and
  yields complete indexes - so a coverage-only or needs-only fragment (05) loads on its own.
- The **approach index covers every process in the market**, not just processes whose approaches
  the file lists, so an `approachName` may name an approach an earlier file created.
- **Project names** in `dependsOnProjectNames` and roadmap items fall back to a workspace-wide
  project listing when the file does not define them, and **firm names** on opportunities fall
  back to a market-wide firm listing.

### Strategy B: one combined file

Merge every fragment into a single SeedFile object and load it once, the way
[`example-seed.json`](example-seed.json) does. Use this when the human wants a single reproducible
artifact they can re-run against a clean database.

The catch is the **load order inside one file**, which is fixed by `SeedLoader.RunAsync`
(`src/ProductManager.Cli/Seed/SeedLoader.cs`):

```
workspace -> market -> marketDynamics -> teams -> initiatives -> features -> projects -> roadmap
```

Consequences an assembling agent must respect:

- `market` is loaded before `marketDynamics`, so everything in `marketDynamics` may reference
  market entities in the same file.
- `features` are loaded before `projects`, so a project's `deliversFeatureNames` may name a feature
  in the same file.
- `initiatives` are loaded before `projects`, so `initiativeName` may be in the same file.
- `roadmap` is last, so its items may name any project in the same file.
- A feature's `productName` must resolve against `market.products` in the **same or an earlier**
  file. If you split files, keep the market ahead of the features.

### Idempotency

Re-running a seed file is safe for the entities that have a natural key. `SeedLoader` matches each
top-level entity by name, scoped to its parent, before creating it, so a second run matches instead
of duplicating. Sub-resources the API upserts (team capacity, project effort demand, dependency
edges, delivered-feature links, roadmap plan links) are simply reapplied.

The exception is append-only facts and a few sub-resources with no natural-key upsert endpoint -
coverage assessments, needs, integrations, opportunities, covariate observations, churn events,
arrival counts. Those use hand-rolled duplicate matching, and the rules differ per entity. Read
[`../../seed/CLAUDE.md`](../../seed/CLAUDE.md) and the relevant loader method in `SeedLoader.cs`
before assuming a re-run is free; each dataset file states its own rule in
`## Loading and idempotency`.

### The tally

`pm seed` prints one line per entity kind:

```
Loaded <path>:
  team                 created   3, matched   0
  initiative           created   2, matched   0
  ...
  segmentStock         applied   2
```

`created` plus `matched` is the total the file described. On a second run of the same file, expect
`created 0` across the board. A handful of upserted sub-resources report `applied` instead, because
they have no create-versus-match story: `segmentStock`, `modulePenetration`,
`channelSegmentReach`, `channelProductAffinity` and `firmRelationship`. Everything else the API
upserts - team capacities, project effort demands, dependency edges, delivered-feature links,
roadmap plan links, assumption-set parameters, product prices - is reapplied silently and never
appears in the tally at all.

---

## 10. Verification checklist

Run this after every dataset load, and again at the end.

### Tally

Compare the `created`/`matched`/`applied` numbers to the row counts in the source data. A `created`
count lower than expected almost always means duplicate names in the source that the name index
collapsed.

### CLI

Every command below is real; check `src/ProductManager.Cli/Commands/*Commands.cs` before adding any
option not listed. `--page` and `--page-size` are available on every `list` (page size caps at 200).

| Tier | Commands |
|---|---|
| A | `pm workspace get-default`; `pm team list`; `pm initiative list`; `pm project list`; `pm feature list` |
| A | `pm roadmap list`; `pm roadmap get <id>` (returns the planned items); `pm roadmap feasibility <id>` (capacity overallocations and dependency-order violations) |
| B | `pm segment list`; `pm coverage list`; `pm need list` |
| C | `pm covariate list`; `pm covariate history <id>`; `pm assumptions list`; `pm assumptions get <id>` (includes parameters); `pm market-event list` |
| D | `pm channel list`; `pm firm list`; `pm firm-relationship list`; `pm integration list`; `pm opportunity list`; `pm calibrate list` |

Filters worth knowing: `pm team list --workspace-id <id>`, `pm project list --initiative-id <id>`,
`pm segment list --market-id <id>`, `pm firm list --market-id <id> --segment-id <id>
--epistemic-status <status>`, `pm opportunity list --firm-id <id> --channel-id <id> --stage <stage>`.
All are optional; omit them to list everything.

There is no `pm market` verb - markets, products, competitors and business processes have no
top-level CLI command (see `src/ProductManager.Cli/Program.cs`). Verify those on the frontend
(`/markets`, `/products`, `/competitors`, `/processes`) or through the API directly.

### Frontend

Open the pages listed for each loaded tier in the "What each tier unlocks" table above, and have
the human eyeball them.

### Smoke simulation (tiers C and D)

Needs a roadmap id (`pm roadmap list`) and an assumption set id (`pm assumptions list`).

```
pm sim run --roadmap-id <guid> --assumption-set-id <guid> --paths 200 --horizon-months 24
```

Options on `sim run` (`src/ProductManager.Cli/Commands/SimulationCommands.cs`): `--roadmap-id` and
`--assumption-set-id` are required; `--horizon-months` (default 60), `--paths` (default 1000),
`--seed` (default 0), `--discount-rate` (default 0.10), `--terminal-growth` (default 0.02),
`--name`, `--start-date` (`yyyy-MM-dd`, defaults to the first of the current month), `--market-id`,
`--event-ids`, `--hypothetical-event-ids`, and `--mode` (`aggregate` | `firm-level`, default
`aggregate`). It prints the run id plus NPV mean/P10/P50/P90, terminal ARR and cumulative bookings.

For tier D, repeat with `--mode firm-level`; that run also prints the funnel diagnostics, and
`pm sim diagnostics <run-id>` returns them again (it 404s on an aggregate run). `pm sim result
<id>`, `pm sim input <id>` and `pm sim compare <run-a> <run-b>` are the other inspection verbs.

Tier D calibration smoke test:

```
pm calibrate run --assumption-set <guid> --as-of 2026-08-31
```

`--assumption-set` is required; `--as-of` (`yyyy-MM-dd`, defaults to today) restricts the fit to
facts recorded on or before that date. `--name` and `--notes` are optional. It prints the run id
and a prior-versus-posterior diff table. "Correct" here means the diff table has rows: an empty
table means calibration found no facts to fit, which usually means dataset 12 did not load or its
`recordedDate` values are all after the `--as-of` date.

---

## 11. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Connection refused` on `localhost:5072` | API not running, or it crashed on an options-validation error | Start `dotnet run --project src/ProductManager.Api` and read its console output |
| `curl http://localhost:5072/api/health` returns `"databaseReachable": false` | Postgres is down but the API started anyway (`Database:ContinueOnInitializationFailure` defaults to `true`) | `docker compose up -d --wait`, then restart the API so migrations run |
| Connection refused on `localhost:5433` | Postgres container not up, or the port is taken | `docker compose ps`; `docker compose up -d --wait`; if the port is taken, free it or change the mapping in `docker-compose.yml` and the connection string in `appsettings.Development.json` |
| `dotnet ef migrations list` shows `(Pending)` | The API has not started against this database since the migration was added | Start the API (it migrates on startup), or `dotnet ef database update --project src/ProductManager.Api` |
| CLI returns 401 | Stored token expired - lifetime is 720 minutes (`Auth:TokenLifetimeMinutes`) | `pm auth login --password productmanager-dev`; confirm with `pm auth whoami` |
| CLI hits the wrong host | `~/.productmanager/config.json` holds a stale `baseUrl` | Re-run `auth login`, or pass `--base-url http://localhost:5072` |
| `Seed file '<path>' has a broken reference: ...` | A name reference does not resolve: the referenced entity is in a later file, is misspelled, or the file is being loaded out of order | Load in `01`..`12` order; check spelling against what `pm ... list` returns. Matching is case-insensitive and trims whitespace (`NameIndex.cs`), so case is not the cause - a typo or ordering is |
| `Seed file '<path>' could not be parsed: ...` | Invalid JSON, or a key that is not on `SeedFile` | Validate the JSON; check every key against `src/ProductManager.Cli/Seed/SeedModels.cs` |
| 400 with "recordedAt ... is in the future" | A `recordedDate` later than now. The ledger accepts backdating only (`src/ProductManager.Api/Controllers/ApiControllerBase.cs:40-52`) | Use a date at or before today. Backdating is intentional and supported - see [`../../seed/CLAUDE.md`](../../seed/CLAUDE.md) |
| Re-running a seed duplicated opportunities, covariate observations, churn events or arrival counts | Those are append-only facts without a natural-key upsert endpoint; duplicate matching is hand-rolled per entity and does not always dedupe | Check the loader method in `SeedLoader.cs` and the dataset file's `## Loading and idempotency` before re-running. If duplicates landed, delete them with the matching `pm ... delete <id>` verb |
| `dotnet build` fails with "framework 'Microsoft.NETCore.App' version '9.0.x' was not found" or NETSDK1045 | Only a 10.x (or 8.x) SDK is installed; every project targets `net9.0` | Install the .NET 9 SDK; verify with `dotnet --list-sdks` |
| `docker compose version` reports `docker-compose version 1.x` | Compose v1; `docker compose up -d --wait` does not exist there | Install Docker Compose v2 (bundled with current Docker Desktop) |
| Frontend loads but every request fails with a CORS error | `NEXT_PUBLIC_API_URL` points somewhere the API's CORS policy does not allow | The API allows `http://localhost:3000` by default (`Cors:AllowedOrigins`). Keep the frontend on port 3000, or add the origin to `appsettings.Development.json` |
| Seed loaded but the frontend shows nothing | The UI reads `GET /api/workspaces/default` (the oldest workspace). If a second workspace was created by hand, data can land where the UI never looks | `pm workspace list`; keep a single workspace. `pm seed` renames the bootstrap workspace rather than creating one |
