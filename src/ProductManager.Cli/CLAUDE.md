# CLAUDE.md — src/ProductManager.Cli

## Invariants

- **No project reference to Api or Domain** (`ProductManager.Cli.csproj` has only a
  `System.CommandLine` package reference). The CLI is a pure HTTP client — everything it knows
  about the API's shape is a hand-mirrored copy, not shared code.
- **DTO mirrors must track API contracts in lockstep.** `Client/Dtos.cs` and `Client/Enums.cs`
  are hand-written mirrors of `src/ProductManager.Api/Contracts/*.cs` and
  `ProductManager.Domain.Common` enums respectively. When you change a field/enum on the API
  side, update the mirror here in the same change — nothing compiles-time-enforces this, but
  `tests/ProductManager.Cli.Tests/ApiContractDriftTests.cs` will catch a drifted DTO or enum at
  test time (see below). Run `dotnet test tests/ProductManager.Cli.Tests` after any API contract
  change.
- **`ApiContractDriftTests` uses a `DtoSchemaMap`/`EnumSchemaMap` registration** — a
  `Dictionary<string,string>` mapping each CLI type to its OpenAPI schema name in
  `openapi/productmanager.json`. A new endpoint or a newly-referenced DTO/enum that isn't
  registered there fails loudly rather than silently skipping the check — **when you add a new
  CLI DTO or enum, register it in that map**. The test checks: every route the client calls
  exists in the OpenAPI doc; each registered DTO's camelCase property names are a subset of the
  schema's properties (and the schema's `required` set is a subset of the DTO's properties);
  each registered enum's member names match the schema's `enum` values exactly. It does **not**
  compare property types — a type mismatch won't be caught by this test.
- **Wire format is camelCase with enums serialized by member name**, not integer —
  `Client/ProductManagerApiClient.cs`'s `JsonSerializerOptions` sets
  `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` and adds a `JsonStringEnumConverter()`.
  `tests/ProductManager.Cli.Tests/WireFormatTests.cs` pins this against real serialized samples
  (e.g. asserts `"tier":"NotPlaying"`, not `"tier":0`). Don't add a custom converter that
  changes either behavior without updating that test.
- **Seed loading is idempotent by natural key, not by blind re-run.** `Seed/NameIndex.cs`
  builds a case-insensitive, whitespace-trimmed name→id index from what the API already has;
  `SeedLoader` checks that index before creating anything, so re-running the same seed file
  against an already-seeded workspace does not duplicate entities. Sub-resources without a
  natural-key upsert endpoint (coverage, needs, integrations, opportunities, append-only facts
  like covariate observations/churn/arrivals) use hand-rolled duplicate-matching instead — check
  the relevant loader method in `SeedLoader.cs` before assuming a blanket "match by name" rule
  applies everywhere.
- **Rows with `source` identity (PRD6) match by source binding instead** (`Seed/SeedLoader.Sources.cs`):
  firms and relationships through the bulk import's own matching, opportunities advanced through
  qualify/decide (`Advance`), churn/arrival facts posted with their source. A row without `source`
  must keep the pre-PRD6 rule exactly — legacy seed files depend on it. The boundary is fixed:
  the loader advances records but never rewrites one; a 409 whose ProblemDetails `type` is
  correction-required (`ApiRequestException.IsCorrectionRequired`) becomes that record's
  exception and the pass continues. Any other refused write stops the pass (resume = rerun).
- **Every load is plan, then apply, through the same code.** `PlanAsync` runs every loader method
  with `SeedTally.DryRun` set: reads happen, **no write may** — wrap every client write in
  `WriteAsync(tally, ...)` (or check `tally.DryRun`), and never send a `tally.Placeholder()` id to
  the API: read children of a possibly-planned parent through `ListChildrenAsync`. Wrap each
  record in `Guard(tally, kind, key, ...)` so a dry run collects every broken reference or bad
  value as a problem instead of stopping at the first. Record each record once, with a key, via
  `tally.Record*`. Page every listing to the end (`ListAllAsync`) — never trust page 1.
- **Seed references resolve against the whole API, not just the file being loaded** — every index
  the loader resolves against is built from the API's listing for the parent (market or
  workspace), never from the file's own entries, so a single-dataset fragment resolves names an
  earlier file created. Three cases need more than a listing, in `SeedLoader.cs` / `SeedLoader.Sources.cs`:
  `MarketResolver` (a file with no `market` section resolves segment/product/process/approach
  names against the workspace's *single* market — zero markets keeps the old "no market defined in
  the seed file" errors, two or more throws asking for a `"market": { "name": "..." }` stub, which
  matches an existing market and creates nothing), `ProjectResolver` (roadmap items and
  `dependsOnProjectNames` fall back to a paged workspace-wide project listing) and `FirmResolver`
  (opportunity/churn firm references: this file's imports, then a paged market-wide firm listing,
  refusing a name the market holds twice; `firmSource` resolves through the source binding). All
  three fetch lazily, so a file that never names one of those pays nothing and still loads where
  none exist.
  **When you add a loader method, build its index from the API listing, not from the seed file's
  list of that entity** — that is the bug class these three resolvers exist to close.

## Working here

- Command files are organized by entity/area, not by CRUD verb — a new endpoint usually means
  adding a subcommand to the matching `*Commands.cs` file (or a new file if it's a new area),
  plus the matching `Dtos.cs`/`Enums.cs` mirror and `DtoSchemaMap` registration.
- `CliInfrastructure.cs` is the place for cross-cutting CLI concerns (auth, base URL, error
  formatting) — don't duplicate that plumbing in individual command files.
- `ParameterKeyHelper.cs` / `ChoiceParameterKeys.cs` mirror server-side parameter-key validation
  logic (`src/ProductManager.Api/Simulation/ChoiceParameterKeys.cs`) — keep both sides' keying
  rules identical or the CLI will accept keys the API rejects (or vice versa).

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
