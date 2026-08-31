# src/ProductManager.Cli index

- `Program.cs` — builds the `pm` root command and wires every command-group subcommand.

## Client/ — the HTTP layer
- `ApiException.cs` — exception hierarchy for connection failures vs. non-success API responses
  (RFC 7807).
- `Dtos.cs` — hand-mirrored request/response records for every API contract, PascalCase (mapped
  to camelCase wire format by `JsonSerializerOptions`). Must track
  `src/ProductManager.Api/Contracts/*.cs` — see `CLAUDE.md`.
- `Enums.cs` — hand-mirrored copies of Domain enums (`ProjectStatus`, `FeatureStatus`,
  `CoverageTier`, etc.) — deliberately independent copies rather than a Domain project
  reference, so the CLI stays a pure HTTP client.
- `ProductManagerApiClient.cs` — typed HTTP client wrapping every API route with auth/error
  handling.

## Commands/ — one file per command group (verbs registered on the `pm` root command)
- `AssumptionSetCommands.cs` — versioned/branchable parameter collections.
- `AuthCommands.cs` — `pm auth login`, obtaining and persisting a bearer token.
- `CalibrationCommands.cs` — run/diff calibration fits against the facts ledger.
- `CalibrationSummary.cs` — pure formatter: `CalibrationDiffDto` → prior-vs-posterior table.
- `ChannelCommands.cs` — Channel CRUD + `IntegrationCommands`, `FirmRelationshipCommands`.
- `ChoiceParameterKeys.cs` — mirrors the API's free-form parameter-key scheme for the choice
  model.
- `CliInfrastructure.cs` — shared `--base-url` option, authenticated client builder, uniform
  error handling.
- `CovariateCommands.cs` — named external time series (equity index, rate, volume).
- `CoverageCommands.cs` — coverage-assessment CRUD (product × process × approach/segment).
- `FeatureCommands.cs` — Feature CRUD and delta management.
- `InitiativeCommands.cs` — Initiative CRUD.
- `MarketDynamicsCommands.cs` — hosts `FirmCommands`, `NeedCommands`, `MarketEventCommands`.
- `OpportunityCommands.cs` — opportunity lifecycle (open/qualify/decide) verbs.
- `ParameterKeyHelper.cs` — client-side mirror of the server's engine-vs-free-form parameter key
  validation.
- `ParseHelpers.cs` — shared date/enum/id-list option parsers with clear failure messages.
- `Phase3ParseHelpers.cs` — CLI-friendly spellings for Phase 3 enums (e.g. "no-decision").
- `ProjectCommands.cs` — Project CRUD, dependencies, effort demand, feature links.
- `RoadmapCommands.cs` — Roadmap CRUD, planning, branching, feasibility checks.
- `SeedCommand.cs` — `pm seed --file`, loads a seed JSON file through `SeedLoader`.
- `SegmentCommands.cs` — ClientSegment reads + Phase 2 stock-state/module-penetration writes.
- `SimulationCommands.cs` — runs, inspects, and compares Monte Carlo simulation runs.
- `SimulationSummary.cs` — pure headline-metric math over an `EngineResult`.
- `TeamCommands.cs` — Team CRUD and capacity setting.
- `WorkspaceCommands.cs` — Workspace CRUD.

## Config/
- `CliConfig.cs` — persisted config record (base URL, bearer token, expiry) at
  `~/.productmanager/config.json`.
- `ConfigStore.cs` — loads/saves `CliConfig`, tolerating a corrupt file.

## Seed/ — the natural-key idempotent loader (see `seed/CLAUDE.md` for the file format)
- `NameIndex.cs` — case-insensitive name→id lookup, the core idempotency primitive.
- `SeedLoader.cs` — drives a full seed file through the API, natural-key matched.
- `SeedModels.cs` — POCOs mirroring the seed JSON schema (name-referenced, not id-referenced).
- `SeedTally.cs` — tracks created/matched/applied counts per entity kind for the run summary.
