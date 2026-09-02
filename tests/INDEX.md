# tests/ index

## ProductManager.Api.Tests — integration tests, real ASP.NET pipeline + SQLite in-memory DB
- `ApiFactory.cs` — `WebApplicationFactory` harness wiring SQLite in-memory DB and test auth
  config; sets `Database:ContinueOnInitializationFailure=false`.
- `ApiTestBase.cs` — base class giving each test a fresh authenticated client/DB.
- `AuthTests.cs` — token issuance and auth-guard behavior.
- `BiTemporalBackdatingTests.cs` — backfilled facts carry their true recorded date, not loader
  wall-clock.
- `CalibrationRunTests.cs` — calibration fit against facts-as-of-date, diffable vs prior.
- `CrudRoundTripTests.cs` — basic CRUD round trips across core entities.
- `ExceptionHandlingTests.cs` — exception-to-HTTP-status classification, including DB-outage
  edge case.
- `FeasibilityTests.cs` — roadmap capacity/sequencing feasibility checks.
- `FirmLevelCoverageMappingTests.cs` — coverage grid and roadmap deltas map correctly into the
  choice model.
- `FirmLevelSimulationTests.cs` — end-to-end firm-level simulation runs via the choice model.
- `OpenApiDocumentTests.cs` — regenerates/validates `openapi/productmanager.json` from the live
  route (this test IS the generator — see `src/ProductManager.Api/CLAUDE.md`).
- `Phase2MarketCrudTests.cs` — Phase 2 market-side CRUD (segments, products, competitors).
- `Phase3CrudTests.cs` — Phase 3 CRUD (channels, integrations, firm relationships, opportunities).
- `Phase3TestBase.cs` — small pre-built Phase 3 world (segment, processes, product, rival,
  coverage) shared by Phase 3 tests.
- `RoadmapTests.cs` — roadmap CRUD, planning, branching.
- `SimulationRunTests.cs` — assumption sets, branches, and simulation-run round trips.

## ProductManager.Cli.Tests — unit tests + contract-drift guard
- `ApiContractDriftTests.cs` — guards CLI DTOs/enums/routes against
  `openapi/productmanager.json` (see `src/ProductManager.Cli/CLAUDE.md`).
- `CalibrationSummaryTests.cs` — formatting logic for calibration diff summaries.
- `OnboardingDocsTests.cs` — guards `docs/onboarding/`: every dataset file's JSON example parses
  strictly into `SeedFile`, enum spellings and ISO dates are exact, headings and links are
  intact, and `example-seed.json` resolves its by-name references.
- `ChoiceParameterKeysTests.cs` — CLI's mirror of the API's choice-model parameter key scheme.
- `NameIndexTests.cs` — name-matching rules for idempotent seed loading.
- `ParameterKeyHelperTests.cs` — engine-vs-free-form parameter key validation logic.
- `ParseHelpersTests.cs` — date/enum/id-list CLI option parsing.
- `Phase3ParseHelpersTests.cs` — Phase 3 CLI-friendly enum spelling parsers.
- `SeedFileTests.cs` — parses the real `seed/phase1-seed.json` and `seed/phase3-seed.json` and
  checks natural-key references resolve, plus seed-shape invariants (decided/undecided
  opportunity fields, scale counts, mixed-technology firms).
- `SeedLoaderCrossFileReferenceTests.cs` — runs `SeedLoader` against a scripted `HttpMessageHandler`
  to pin cross-file reference resolution: a file with no `market` section resolves against the
  workspace's single market (and reports zero/several clearly), a name-only `market` stub creates
  nothing, the approach index spans every process, and project/firm references fall back to
  workspace- and market-wide listings (roadmap-only, dependency, opportunities-only and
  churn/arrival-only fragments).
- `SeedLoaderOpportunityDecisionTests.cs` — `SeedLoader.HasDecision` guard: an opportunity with a
  null `decidedDate` is left Open/Qualified rather than passed to `DecideOpportunityAsync`.
- `SeedTallyTests.cs` — created/matched/applied counting logic.
- `SimulationSummaryTests.cs` — headline-metric math over an `EngineResult`.
- `WireFormatTests.cs` — pins camelCase properties and by-name enum serialization against real
  samples.

## ProductManager.Domain.Tests — invariant-focused unit tests
- `CapacityFeasibilityTests.cs` — team capacity vs. project demand feasibility rules.
- `InitiativeTests.cs` — StrategicIntent tag set/clear behavior.
- `RoadmapBranchingTests.cs` — roadmap branch/adopt invariants.
- `RoadmapSequencingTests.cs` — dependency/sequencing violation detection.
- `ValueObjectAndInvariantTests.cs` — Period, Money, Distribution value-object invariants.

## ProductManager.Engine.Tests — determinism/statistical-property tests
- `CalibrationEngineTests.cs` — fitting behavior of the calibration engine (per-flow-kind
  closed-form updates).
- `FirmLevelEngineTests.cs` — firm-level choice-model engine behavior.
- `SimulationEngineTests.cs` — determinism, seeding, and covariate-driven simulation properties
  (e.g. same-seed byte-identical results, scale-free covariate effect).
