# ProductManager

ProductManager is a roadmap planning tool that grows into a Monte Carlo market-simulation
decision-support system: plan initiatives, projects and teams on the execution side, model markets
(segments, firms, coverage, opportunities) on the market side, and simulate roadmap scenarios to
compare projected bookings, ARR and NPV. The backend is C#/.NET (Domain, Engine, Api, Cli) and the
frontend is Next.js; the REST API is the only write path, and both the web app and the `pm` CLI
are clients of it.

## Onboarding a new instance with an agent

**Onboarding is designed to be done by an agent, not by hand.** Hand
[`docs/onboarding/README.md`](docs/onboarding/README.md) to a coding agent (Claude Code or
similar) and tell it to onboard your instance. That guide is written for an orchestrator agent:
it verifies your environment, brings the stack up, interviews you about where your data lives,
then spawns one subagent per dataset using the per-dataset briefs under
`docs/onboarding/datasets/`, starting with
[`01-workspace-and-teams.md`](docs/onboarding/datasets/01-workspace-and-teams.md).

Onboarding is tiered. Tier A (workspace, teams, initiatives, projects, features, roadmap) is all
you need for a working roadmap tool; tiers B, C and D add the static market view, aggregate
simulation, and the firm-level engine plus calibration. You can stop after any tier. A complete
worked example seed file lives at
[`docs/onboarding/example-seed.json`](docs/onboarding/example-seed.json).

## Run it locally

Prerequisites: .NET SDK 9.x, Node.js 20.9+, Docker with Compose v2. Full version checks are in the
[onboarding guide](docs/onboarding/README.md).

```
docker compose up -d --wait                      # Postgres 17 on port 5433
dotnet run --project src/ProductManager.Api      # API on http://localhost:5072
cd frontend && cp .env.example .env.local && npm install && npm run dev   # app on http://localhost:3000
```

The API applies EF migrations at startup, so there is no separate migrate step. Sign in with the
dev password `productmanager-dev` (from `src/ProductManager.Api/appsettings.Development.json` -
dev-only, never a real secret). Build and test with `dotnet build`, `dotnet test`, and
`cd frontend && npm test`.

## Where the docs live

- [`docs/PRDs/product_requirements.md`](docs/PRDs/product_requirements.md) is the authoritative
  product spec: the domain model, the simulation semantics, and the phasing. Read it before any
  change that touches the domain, the engine, or the roadmap of the product itself.
- [`CLAUDE.md`](CLAUDE.md) is the agent entry point for working on the code: dev environment,
  build and test commands, and repo conventions.
- [`seed/CLAUDE.md`](seed/CLAUDE.md) covers seed-file loading rules and idempotency.

## The documentation pattern (for agents working on the code)

Every major directory (the repo root, each project under `src/`, plus `frontend`, `tests`, `seed`
and `docs`) carries the same trio of files:

- `ABSTRACT.md` - two to four sentences on what the directory is and why it exists. Read this
  first for any directory you touch.
- `INDEX.md` - one line per file or subdirectory: what it contains and when to open it. Open it
  when you are hunting for a specific file.
- `CLAUDE.md` - working guidance for an agent editing that directory: local conventions,
  invariants, gotchas.

Read order for unfamiliar territory: [`CLAUDE.md`](CLAUDE.md), then the target directory's
`ABSTRACT.md`, its `CLAUDE.md`, and its `INDEX.md`. Keep all three current when you change a
directory's structure.
