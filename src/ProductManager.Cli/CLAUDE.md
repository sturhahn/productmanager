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
