# CLAUDE.md — tests/

## Rigor by project (PRD testing posture: "Strict TDD and high coverage on the engine,
calibration, and domain invariants — wrong numbers silently corrupt decisions. Conventional
integration tests on CRUD/API. Light touch on UI.")

- **`ProductManager.Domain.Tests` / `ProductManager.Engine.Tests`** — invariant- and
  property-focused. Expect (and write) tests like "same seed produces byte-identical results,"
  "a point distribution must have zero width," "beta requires strictly positive shapes," theory
  sweeps over parameter ranges. A behavior change here that isn't backed by a new/updated test is
  under-tested by this codebase's own standard — don't treat these two projects like ordinary
  CRUD tests.
- **`ProductManager.Api.Tests`** — conventional integration tests: real ASP.NET pipeline via
  `WebApplicationFactory` (`ApiFactory.cs`) against a private SQLite in-memory DB per test
  (`ApiTestBase.cs`). CRUD round trips are enough rigor here; don't over-engineer Api tests to
  Domain/Engine standards.
- **`ProductManager.Cli.Tests`** — unit tests on pure formatting/parsing logic, plus the
  contract-drift guard (`ApiContractDriftTests.cs`) that is the actual safety net keeping the
  CLI's hand-mirrored DTOs honest against `openapi/productmanager.json`.
- **No frontend/UI test project beyond `frontend/src/lib/*.test.ts`** (Vitest, colocated with
  the code) — consistent with "light touch on UI." Don't expect or demand UI test coverage
  matching the .NET side.

## Gotchas

- `ApiFactory` uses **SQLite**, not the EF in-memory provider, specifically because the domain
  leans on relational behavior the EF in-memory provider doesn't faithfully emulate. SQLite's
  quirks (no native `DateTimeOffset` ordering, no native array types) are handled by
  `AppDbContext.OnModelCreating`'s non-Npgsql fallbacks — see `src/ProductManager.Api/CLAUDE.md`.
  If a test fails only under SQLite, suspect one of those fallback converters before suspecting
  the test.
- `OpenApiDocumentTests.cs` is not just a test — running it (via `dotnet test` or
  `scripts/generate-openapi.sh`) **regenerates** `openapi/productmanager.json`. If you add or
  change an API contract, run the full Api.Tests suite (or at least that filter) and commit the
  resulting diff to the OpenAPI file before running `ApiContractDriftTests` in Cli.Tests — the
  latter reads the checked-in file, not the live route.
- `SeedFileTests.cs` parses the real `seed/phase1-seed.json` — if you add a field to the seed
  JSON schema, check this test still resolves natural-key references correctly.

- `OnboardingDocsTests.cs` reads markdown under `docs/onboarding/` — a seed-schema change in
  `SeedModels.cs` can fail it (unknown keys are disallowed when parsing the doc examples). Fix
  the docs, not the test.

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
