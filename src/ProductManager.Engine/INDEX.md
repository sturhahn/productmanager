# src/ProductManager.Engine index

- `Contracts.cs` — engine v0 public I/O types: `FlowKind`, `DistributionSpec`, `CovariateLink`,
  `ParameterSpec`, `SegmentInput`, `EngineInput`, `MetricTrajectory`, `EngineResult` (the frozen
  result shape both engines emit). Start here to see the aggregate engine's whole contract.
- `SimulationEngine.cs` — engine v0: aggregate stock-and-flow Monte Carlo per segment. Sample
  parameters once per path, then Poisson/Binomial monthly ticks. `MaxFlowMultiplier` guard lives
  here.
- `FirmLevelContracts.cs` — engine v1 public I/O types: `ProcessInput`, `FirmInput`,
  `CoverageCell`/`CoverageStep`, `ChannelInput`, `ChoiceParameters`, `FirmLevelInput`,
  `FirmLevelDiagnostics`, `FirmLevelResult` (wraps the same `EngineResult` plus diagnostics).
- `FirmLevelEngine.cs` — engine v1: firm-level choice Monte Carlo. Vectorized firm state (flat
  arrays, not an object graph); business-process scopes/coverage/modules are 64-bit bitmasks
  (hard 64-process-per-run limit).
- `EngineInternals.cs` — shared internals used by both engines: covariate elasticity multiplier
  `(x_t/x_0)^beta`, the `MaxFlowMultiplier` clamp/validation, covariate-path positivity checks.
- `EngineRandom.cs` — `EngineRandom`: xoshiro256** RNG seeded via SplitMix64 from
  `(seed, pathIndex)`, byte-identical across .NET versions (unlike `System.Random`). All
  sampling (Normal/Lognormal/Beta/Gamma/Poisson/Binomial) goes through this.
- `Calibration/CalibrationContracts.cs` — calibration I/O types: `OpportunityOutcome`,
  `ChurnEvent`, `ArrivalCount`, `RelationshipSpan`, `FlowPrior`, `CalibrationInput`,
  `ParameterDiff`, `CalibrationOutput`.
- `Calibration/CalibrationEngine.cs` — deterministic (no RNG, no I/O) conjugate/moment-matching
  fits per flow kind (Beta for rates, Gamma→LogNormal for counts, lognormal log-moment blend for
  sales cycle). See its class doc comment for the exact update formula per flow kind before
  changing any of them.
