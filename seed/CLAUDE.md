# CLAUDE.md — seed/

## Invariants

- **Three layered files, load in order: phase1 → phase2 → phase3.** phase1 seeds the
  self-contained execution/roadmap layer plus a base market; phase2 and phase3 both nest their
  content under a `marketDynamics` key and build on phase1's market by name, so loading them out
  of order (or without phase1 already loaded) will fail to resolve references.
- **Idempotent by natural key, not by file identity.** The loader
  (`src/ProductManager.Cli/Seed/SeedLoader.cs` + `NameIndex.cs`) matches each top-level entity
  by name (case-insensitive, trimmed, scoped to its parent) against what the API already has
  before creating it — re-running the same file against an already-seeded workspace does not
  duplicate entities. Sub-resources without a natural-key upsert endpoint on the API (coverage
  assessments, needs, integrations, opportunities, and append-only facts like covariate
  observations/churn events/arrival counts) use hand-rolled duplicate-matching instead — check
  `SeedLoader.cs` for the specific rule before assuming every entity type dedupes on name alone.
- **References in the JSON are by name, not by id** (`SeedModels.cs` POCOs are name-referenced)
  — because ids are self-assigned by the Domain (see Domain's `CLAUDE.md`) and don't exist until
  the API creates the row. Keep new seed entries name-referenced; don't hardcode a Guid into
  these files. **A name may point at something an earlier file already loaded**: market-side names
  (segments, products, processes, approaches, channels, competitors, covariates, firms) resolve
  against what the API already holds for the market, and project names against the whole
  workspace — so a single-dataset fragment (initiatives-only, features-only, roadmap-only,
  firms-only, opportunities-only) loads fine on top of the files before it. Market-side names in a
  file with no `market` section resolve against the workspace's single market; if the workspace
  holds more than one, give the file a `"market": { "name": "..." }` stub to say which one it
  means — a name-only stub matches the existing market and creates nothing.
- **`recordedDate` vs `effectiveDate` is the bi-temporal facts-ledger pattern from the PRD** —
  `effectiveDate` is when something was true in the world, `recordedDate` is when we learned it.
  Most seed entries set both to the same date. **Backdating is supported and intentional**: a
  seed entry can set `recordedDate` later than its effective/decided date to simulate "we found
  out about this two months after it happened" (see `phase3-seed.json`'s opportunities with a
  `decidedDate` months before their `recordedDate`, flagged `lateRecorded: true`, and
  `tests/ProductManager.Api.Tests/BiTemporalBackdatingTests.cs`). **Backdating only** — a
  `recordedDate` in the future relative to "now" is rejected by the API. When adding new seed
  facts that are meant to exercise calibration-as-of-a-date behavior, set `recordedDate`
  deliberately rather than letting it default.

## Working here

- Load via the CLI, never by hand-inserting rows or bypassing the API — the API is the only
  write path (see root `CLAUDE.md`). Example: `dotnet run --project src/ProductManager.Cli --
  seed --file seed/phase1-seed.json`.
- If you add a new entity kind to a seed file, check whether `src/ProductManager.Cli/Seed/
  SeedLoader.cs` already has a matching loader method with the natural-key or duplicate-matching
  behavior you expect — don't assume a new loader branch dedupes correctly without checking.
- `tests/ProductManager.Cli.Tests/SeedFileTests.cs` parses `phase1-seed.json` directly — keep
  its shape parseable by `SeedModels.cs` when you edit it.

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
