# src/ProductManager.Api index

- `Program.cs` — composition root: DB, JWT auth, MVC/ProblemDetails, Swagger, CORS, hosted DB
  initializer.
- `ProductManager.Api.http` — manual HTTP request scratchpad for local API exploration.
- `Properties/launchSettings.json` — local launch profiles/ports (`http` = :5072).
- `appsettings.json` — base config: logging, auth issuer/audience/lifetime, CORS (no secrets).
- `appsettings.Development.json` — dev connection string + dev password/signing key (dev-only).

## Auth/
- `AuthOptions.cs` — bound config: single shared password, JWT signing key, issuer/audience/
  lifetime (Phase 1: one shared password, no user accounts).
- `TokenIssuer.cs` — constant-time password check (`CryptographicOperations.FixedTimeEquals`)
  and single-user JWT issuance.

## Contracts/ — request/response DTOs, one file per area
- `AuthContracts.cs` — `TokenRequest`/`TokenResponse`.
- `ExecutionContracts.cs` — Workspace/Period/Roadmap-execution-side DTOs and create/update
  requests.
- `MarketContracts.cs` — Market and ClientSegment DTOs/requests.
- `MarketDynamicsContracts.cs` — Firm, Competitor, Product and related market-participant DTOs.
- `Phase3Contracts.cs` — Channel, Integration, firm-relationship, Phase 3 routing-to-market DTOs.
- `RoadmapContracts.cs` — RoadmapItem/Roadmap DTOs including branch/adopt/order requests.
- `SeamContracts.cs` — Feature and FeatureDelta DTOs.
- `SimulationContracts.cs` — Distribution/AssumptionSet/Parameter/simulation-run DTOs.
- These are the CLI's mirroring target and the frontend's hand-written-type source — see
  `CLAUDE.md` before renaming/removing a field.

## Controllers/ — one per resource, all `[Authorize]` via `ApiControllerBase`
- `ApiControllerBase.cs` — shared auth/ProblemDetails base class, `TryResolveRecordedDate`
  bi-temporal helper.
- `AssumptionSetsController.cs` — versioned/branchable parameter-set CRUD and branching.
- `AuthController.cs` — `POST /api/auth/token`.
- `CalibrationRunsController.cs` — calibration run creation, diff, input inspection.
- `FactsAndCovariatesControllers.cs` — bi-temporal facts ledger + covariate/history endpoints.
- `FeaturesController.cs` — Feature CRUD and delta management.
- `HealthController.cs` — anonymous liveness probe hitting the DbContext.
- `InitiativesController.cs` — Initiative CRUD.
- `MarketCatalogControllers.cs` — market/segment/business-process/approach catalog endpoints.
- `MarketDynamicsControllers.cs` — firms, competitors, products, firm-product relationships.
- `OpportunitiesController.cs` — opportunity lifecycle: scope/qualify/decide.
- `Phase3MarketControllers.cs` — channels, integrations, segment reach, product affinity.
- `ProjectsController.cs` — Project CRUD, dependencies, delivered-feature linking.
- `RoadmapsController.cs` — Roadmap CRUD, branching, adoption, ordering, feasibility.
- `SimulationRunsController.cs` — simulation run creation, results, diagnostics.
- `TeamsController.cs` — Team CRUD.
- `WorkspacesController.cs` — Workspace CRUD (root aggregate).

## Infrastructure/
- `AggregateTracking.cs` — `MutateChildren`: EF-tracking helper for non-owned child entities
  that arrive with a pre-populated key (see `CLAUDE.md`).
- `DatabaseInitializer.cs` — hosted service: `MigrateAsync` on Postgres / `EnsureCreatedAsync`
  otherwise, plus default-workspace bootstrap; swallows failures unless
  `Database:ContinueOnInitializationFailure=false`.
- `DomainExceptionHandler.cs` — maps domain/infrastructure exceptions to RFC 7807
  ProblemDetails (409/400/404/503/500 — see `CLAUDE.md`).
- `OpenApiSchemaIds.cs` — stable, collision-free schema-id naming for generic and
  cross-namespace DTOs (e.g. the `Engine`-prefix rule for `DistributionType`).
- `PagedResponse.cs` — `PagedResponse<T>`, `PageQuery` (max page size 200, default 50).

## Persistence/
- `AppDbContext.cs` — DbContext with all DbSets; non-Npgsql value-converter fallbacks
  (`List<Guid>`↔string, `DateTimeOffset`↔UTC ticks, `jsonb`↔plain text) for SQLite tests.
- `Configurations/ConfigurationExtensions.cs` — shared EF fluent-config extension helpers.
- `Configurations/DemandSupplyDynamicsConfigurations.cs` — EF configs: BusinessProcess,
  Approach, Need, Coverage.
- `Configurations/ExecutionConfigurations.cs` — EF configs: Workspace, Roadmap, Initiative,
  Project, Team.
- `Configurations/SeamAndMarketConfigurations.cs` — EF configs: Feature, market-structure
  entities.
- `Configurations/SimulationAndFactsConfigurations.cs` — EF configs: AssumptionSet, Parameter,
  CalibrationRun, SimulationRun, FactEvent.
- `Migrations/` — three EF migrations (`InitialSchema`, `Phase2`, `Phase3`) + current model
  snapshot. Postgres-only; SQLite tests use `EnsureCreatedAsync` from the live model instead
  (see `CLAUDE.md` — nothing enforces the two stay in sync).

## Simulation/ — turns persisted state into Engine input
- `CalibrationInputBuilder.cs` — assembles everything a calibration run needs "as of" a date.
- `ChoiceParameterKeys.cs` — structural key scheme for firm-level choice-model parameters.
- `CovariateHistoryReader.cs` — reads covariate observation history back out of the facts ledger.
- `FactPayloads.cs` — payload shape for `OpportunityDecided` fact-ledger entries.
- `FirmLevelInputBuilder.cs` — builds firm-level (`FirmLevelInput`) simulation world.
- `SimulationInputBuilder.cs` — builds engine v0 input; coverage tier resolution
  (`max(base, scheduled delta)`) and attractiveness schedule live here — this is the semantics
  the frontend must mirror (see `CLAUDE.md`).
- `SimulationJson.cs` — single shared `JsonSerializerOptions` for simulation run persistence.
- `SyntheticFirmGenerator.cs` — generates synthetic firms to fill unobserved market segments.
