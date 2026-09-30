# ProductManager — agent entry point

## What this is

A product management system for roadmaps *in their business context*: it starts as roadmap
software (initiatives, projects, timelines, team capacity) and grows into a decision-support
tool — Monte Carlo simulation of market dynamics (segments, firms, coverage, opportunities),
calibrated to observed behavior, so roadmap scenarios can be compared on projected bookings,
ARR, and NPV.

**`docs/PRDs/product_requirements.md` is the authoritative product spec.** Read it before any
change that touches the domain model, the simulation engine, or phasing. Do not duplicate its
content here — this file covers environment, commands, and the documentation pattern; the PRD
covers the *why* and the domain model.

Key architectural facts (see the PRD for full detail):
- Market side (simulation) and execution side (planning) are separate models joined at exactly
  one seam: `Project delivers Feature`.
- The domain is industry-agnostic: industry specifics enter as *data*, never as schema.
- API-first: the REST API is the only write path. Web app, CLI, and any future MCP server are
  all clients of it.
- The Engine is a pure library: `(WorldSnapshot, AssumptionSet, CovariateScenario, EventOverlay,
  Seed) → Trajectories`. No EF, no database access, no service references.
- Testing posture is not uniform: strict TDD / high coverage on engine, calibration, and domain
  invariants; conventional integration tests on CRUD/API; light touch on UI.

## Dev environment

- Postgres 17 in Docker, port **5433**: `docker compose up -d --wait` (healthcheck-gated, so
  this blocks until the DB actually accepts connections).
- API: `dotnet run --project src/ProductManager.Api` — profile `http`, `http://localhost:5072`.
  Dev connection string, dev JWT signing key, and dev password (`productmanager-dev`) live in
  `src/ProductManager.Api/appsettings.Development.json` — dev-only, never real secrets.
- Frontend: `cd frontend && npm run dev` — `http://localhost:3000`. Talks to the API via
  `NEXT_PUBLIC_API_URL` (see `frontend/.env.example`).
- Auth is single-user JWT (Phase 1): sign in with the dev password above; no user accounts, no
  SSO yet.
- Seed data: load through the `pm` CLI, not by hand-inserting rows —
  `dotnet run --project src/ProductManager.Cli -- seed --file seed/phase1-seed.json` (then
  phase2, then phase3 — see `seed/AGENTS.md`).
- `dotnet tool restore` picks up `dotnet-ef` (pinned in `.config/dotnet-tools.json`) for
  migrations.

## Build & test

- Build everything: `dotnet build` (solution root).
- Run all .NET tests: `dotnet test`. Note: this **regenerates
  `openapi/productmanager.json`** as a side effect of `ProductManager.Api.Tests` — if that file
  changes, commit it; it is the checked-in API contract every client (frontend, CLI) is written
  against.
- Run one test project: `dotnet test tests/ProductManager.Engine.Tests` (etc.).
- Frontend tests: `cd frontend && npm test` (Vitest, `*.test.ts` files next to the code they
  test in `src/lib/`).
- Frontend lint: `cd frontend && npm run lint`.

## The documentation pattern

Every major directory (repo root plus each project under `src/`, `frontend`, `tests`, `seed`,
`docs`) carries the same trio:

- **`ABSTRACT.md`** — 2-4 sentences: what the directory is, why it exists. Read this first, for
  every directory you touch — it tells you in 10 seconds whether you need to go deeper.
- **`INDEX.md`** — one line per file/subdirectory: what it contains, when to open it. Open this
  only when you're hunting for a specific file — don't read it just to "get oriented," the
  ABSTRACT already did that.
- **`AGENTS.md`** — working guidance for an agent editing *that* directory: local conventions,
  invariants, gotchas, pointers deeper. This is where the load-bearing detail lives.

Read order for unfamiliar territory: this file → the target directory's `ABSTRACT.md` → its
`AGENTS.md` for conventions → its `INDEX.md` only if you still need to locate a specific file.

**Keep all three updated when you change a directory's structure or conventions** — that's the
entire point of the pattern: it exists to reduce how much of the codebase an agent has to read
to work safely. A stale trio is worse than no trio.

## Rider Solution Explorer coverage

`ProductManager.sln` only auto-discovers the 8 `.csproj` projects. Everything else (frontend
source, seed data, docs, the OpenAPI contract, scripts, `docker-compose.yml`) is surfaced via
Solution Folders (`ProjectSection(SolutionItems)` blocks, GUID
`{2150E333-8FDC-42A3-9474-1A3956D46DE8}`) so it's visible in Rider's Solution Explorer without
switching to the File System view. Folders: `Solution Items` (root config), `docs`, `seed`,
`openapi`, `scripts`, and a `frontend` tree mirroring `frontend/src` (`app`, `app/(app)`,
`components` + its `charts`/`roadmap`/`ui` subfolders, `lib` + its `api` subfolder).

**When you add, rename, move, or delete a file under `frontend/src`** (or any of the other
surfaced directories), update the matching `ProjectSection(SolutionItems)` block in
`ProductManager.sln` to match — Rider does not do this automatically for solution-folder items.
If the frontend tree drifts significantly, it's fine to regenerate the `frontend` folder entries
wholesale rather than hand-patch them; just keep `node_modules`, `.next`, and other generated
output out of the solution file. After editing, sanity-check with `dotnet sln ProductManager.sln
list` (should still show only the 8 real projects) and `dotnet build`.

## Follow-up PRDs

New follow-up requirements use root `PRDs/PRD[n]-FeatureName.md`, continuing the numbering
from `docs/PRD1-InitialFeedback.md` and `docs/PRD2-Onboarding.md`. Read `PRDs/ABSTRACT.md`
and `PRDs/AGENTS.md` before editing them. The authoritative product spec remains
`docs/PRDs/product_requirements.md`; follow-up drafts do not silently supersede it.

## Rules
- For any bug first create a test showing the failure, then fix it and verify with the test.
- For any feature first create a test showing the desired behavior, then implement it and verify
  with the test.
