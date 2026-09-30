# CLAUDE.md — src/ProductManager.Api

## Persistence gotchas

- **`AppDbContext` runs on Postgres in dev/prod and SQLite in tests.** The switch isn't in
  `AppDbContext` itself — `Program.cs` registers Npgsql unless
  `builder.Environment.IsEnvironment("Testing")`, and `tests/ProductManager.Api.Tests/
  ApiFactory.cs` registers SQLite for that environment. `AppDbContext.OnModelCreating` checks
  `!Database.IsNpgsql()` to add portable fallbacks: `List<Guid>` ↔ delimited string,
  `DateTimeOffset` ↔ UTC-ticks `long` (SQLite can't `ORDER BY` a `DateTimeOffset`), and clears
  any `jsonb` column type. When you add a new Postgres-specific column type or converter, add
  the SQLite fallback in the same place or tests will fail confusingly.
- **Migrations only run against Postgres.** `DatabaseInitializer` calls `MigrateAsync` on
  Npgsql and `EnsureCreatedAsync` (straight from the live model) on everything else — so the
  checked-in migrations and the SQLite test schema are **not** cross-checked against each other
  by anything. If a migration and the current model snapshot silently diverge, tests won't catch
  it; only running the API against real Postgres will. Always add a migration
  (`dotnet ef migrations add ...` from repo root, using the pinned `dotnet-ef` tool) when you
  change an entity shape — don't rely on `EnsureCreatedAsync` parity as a substitute.
- **`AggregateTracking.MutateChildren`** exists because Domain entities self-assign their `Guid`
  Id in the constructor (see Domain's `CLAUDE.md`): a child object EF meets for the first time
  through a navigation already has a populated key, so EF reads it as "already exists" and
  issues a no-op UPDATE instead of an INSERT. Use `db.MutateChildren(() => parent.Children,
  () => parent.DoTheMutation(...))` for any child collection that holds entities with their own
  identity (i.e. not an EF *owned type*) — see call sites in `AssumptionSetsController`,
  `ProjectsController`, `RoadmapsController` for the pattern. Removals still ride on EF's own
  orphan detection; this helper is only for adds/updates.
- Startup DB failures are swallowed by default (`Database:ContinueOnInitializationFailure`
  defaults `true`, so `/health` and Swagger stay up even with a broken DB). Tests flip this to
  `false` so a broken test DB fails loudly instead of masking as a passing-but-empty run.

## OpenAPI contract discipline

- **`openapi/productmanager.json` is generated, not hand-written — `dotnet test` regenerates
  it.** `tests/ProductManager.Api.Tests/OpenApiDocumentTests.cs` doc comment: "this test is the
  generator." It only rewrites the file when content actually differs, so an unchanged contract
  stays out of your diff. `scripts/generate-openapi.sh` is a shortcut that runs just that one
  test. **Any endpoint/DTO change should be followed by `dotnet test` before committing**, and
  the resulting diff to `openapi/productmanager.json` must be committed too — the CLI's
  `ApiContractDriftTests` and (informally) the frontend's hand-written types are both checked
  against this file.
- Keep schema ids stable and collision-free via `OpenApiSchemaIds.cs` — generic DTOs
  (`PagedResponse<T>`) need their type argument baked into the id, and
  `ProductManager.Engine.DistributionType` needs an `Engine`-prefix to avoid colliding with the
  Domain's own `DistributionType`. Follow the existing pattern for any new generic/ambiguous DTO
  rather than letting Swashbuckle default-name it.
- There's no automated "additive only" check in this codebase today — but the CLI's
  `ApiContractDriftTests` (`tests/ProductManager.Cli.Tests/`) will fail if you rename or remove
  a property the CLI's hand-mirrored DTOs reference, and the frontend's hand-written
  `frontend/src/lib/api/types.ts` will silently drift if you do the same there. Treat contract
  changes as additive by default (new optional fields, new endpoints) and grep both
  `src/ProductManager.Cli/Client/Dtos.cs` and `frontend/src/lib/api/types.ts` before removing or
  renaming anything in `Contracts/`.

## Error mapping (`Infrastructure/DomainExceptionHandler.cs`)

- `DomainConflictException` → 409 "Operation not allowed in this state"
- `ArgumentException` (and subclasses) → 400 "Invalid request"
- `KeyNotFoundException` → 404 "Not found"
- `DbUpdateException` → classified further: unique/FK violations and concurrency conflicts →
  409 with a specific title (from the Postgres SQLSTATE); anything that looks like an
  infrastructure failure → 503
- Infrastructure failure (unreachable/broken DB — transient `PostgresException`, any other
  `NpgsqlException`, socket/timeout errors, or anything thrown from inside `Npgsql`/
  `Microsoft.Data.Sqlite`/`System.Data.Common`) → **503** "Service temporarily unavailable" with
  `Retry-After: 5`
- Anything else → 500 "Unexpected error"
- When you throw a new exception type from a controller or service, make sure it lands in one
  of these buckets on purpose — an unclassified exception silently becomes a 500.

## Source imports (PRD6)

- **Identity lives in one table, `SourceBinding`** (Domain `Imports/`): (workspace, sourceSystem,
  sourceInstance, objectKind, sourceRecordId) → (targetType, targetId), unique per workspace. Never
  add source columns to entities, and never let it reach the Engine or `CalibrationInput`.
  Workspace scope, not market: facts have no market. Known ceiling: one source account cannot be a
  firm in two markets of one workspace (the second is a per-item exception).
- Every write that takes a `source`/`sources` is optional and additive: **without it, behaviour is
  exactly the pre-PRD6 behaviour** (bulk import upserts by name and overwrites; creates always create).
- Repeat safety: a bound source returns the bound row (200) instead of creating; bound but materially
  different (fact, opportunity firm) → 409 with `type = ProblemTypes.CorrectionRequired`
  (`CorrectionRequiredProblem` in `ApiControllerBase`). Qualify/decide are idempotent for every
  opportunity; a different decision/qualification date → the same correction-required 409. Nothing
  ever rewrites or deletes a fact. The partial unique index `IX_FactEvents_OneDecisionPerOpportunity`
  (SubjectId where Kind = 'OpportunityDecided') is what stops two overlapping decide calls from both
  appending; the loser gets the ordinary unique-violation 409 and its retry returns 200.
- Source-bound bulk items are partial updates through `FieldMerge` (`Infrastructure/SourceImport.cs`):
  absent keeps (and records the stored value as the field's baseline the first time), `clearFields`
  clears, and a stored value that differs from the binding's `ImportedValuesJson` snapshot is a local
  edit → local value kept; reported as a conflict only when the incoming value also differs from the
  snapshot, and the snapshot is not advanced, so it recurs until resolved. Every binding on a
  consolidated edge gets the new snapshot. The field
  ownership lists are in the XML docs on `FirmImportItem` / `FirmImportRelationship` — keep those, the
  `FirmClearable`/`RelationshipClearable` arrays and the merge calls in `FirmsController` in step.
- A binding whose target was deleted is re-pointed (`SourceBinding.RebindTo`), never duplicated.

## Other conventions

- Every controller inherits `ApiControllerBase` (`[ApiController] [Authorize]` + ProblemDetails
  helpers + `TryResolveRecordedDate` for bi-temporal writes). There is **no** implicit
  workspace-scoping in the base class — each controller enforces workspace scope itself
  (typically via query/route params), so don't assume a shared filter is doing it for you.
- Pagination is `PagedResponse<T>` / `PageQuery` (`Infrastructure/PagedResponse.cs`):
  `MaxPageSize = 200`, default `PageSize = 50`.
- **`Simulation/SimulationInputBuilder.cs` coverage-tier resolution is the semantics of record:
  resolved tier for a process at month M = `max(currently-assessed base tier, any planned
  coverage delta whose LiveFrom <= M)`.** `FirmLevelInputBuilder.cs` resolves the coverage grid
  with the engine's own "exact-then-generic, approach first" precedence. Both the CLI's
  simulation summaries and the frontend (`frontend/src/lib/coverage.ts`,
  `frontend/src/lib/masking.ts`) mirror this logic — if you change either resolution rule here,
  update those client-side mirrors in the same change, and check their doc comments (which name
  this builder explicitly) for what exactly they replicate.
- Auth is single-user (Phase 1): `AuthOptions` binds one shared password + JWT signing key from
  config, `Program.cs` builds `JwtBearerOptions` *from* `AuthOptions` rather than raw config "so
  the token issuer and the token validator can never disagree about the signing key." Don't add
  a second, independently-configured JWT source.
- `ProductManager.Api.csproj` references only `ProductManager.Domain` and
  `ProductManager.Engine` — never add a reference to `ProductManager.Cli`.

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
