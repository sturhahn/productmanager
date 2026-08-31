# CLAUDE.md — src/ProductManager.Engine

## Invariants

- **Pure library. No project or package references at all** (`ProductManager.Engine.csproj`
  has zero `PackageReference`/`ProjectReference` entries — not even to `ProductManager.Domain`).
  Do not add EF, ASP.NET, or any service reference here. If calibration ever needs heavier
  numerics than hand-rolled sampling, this is the boundary where a Python service would slot in
  per the PRD — don't erode it by reaching into Api/Domain types.
- **Deterministic per `(seed, pathIndex)`.** `EngineRandom` uses xoshiro256** seeded via
  SplitMix64 from `(seed, pathIndex)` specifically because `System.Random` gives no
  cross-.NET-version guarantee. Consequence: increasing `Paths` on an otherwise-identical run
  leaves every earlier path's draws unchanged — `EngineResult.NpvPerPath`'s prefix is stable.
  Never introduce shared mutable RNG state across paths or anything that samples in an order
  depending on `Paths`.
- **`EngineResult` shape is frozen for byte-compatibility.** It's the contract the Api's
  `SimulationJson` persists and the CLI/frontend read back. Both engine modes (`SimulationEngine`
  v0 and `FirmLevelEngine` v1, wrapped as `FirmLevelResult(EngineResult, FirmLevelDiagnostics)`)
  emit the exact same shape so comparison/charting code never needs to know which engine
  produced a run. Changing a field here is a breaking change to every persisted run — don't do
  it casually; add fields, don't repurpose or reorder existing ones.
- **`SimulationEngine.MaxFlowMultiplier = 1000.0`** — a guard-rail on the combined
  covariate × event × attractiveness multiplier applied to a sampled rate. A run that would
  exceed it throws rather than silently producing absurd flows; the exception message points at
  covariate beta, event rate multipliers, and attractiveness as the three usual suspects. Shared
  between v0 and v1 via `EngineInternals`.
- **Covariate elasticity is `(x_t / x_0)^beta`, not `exp(beta * (x_t - x_0))`.** Beta is a
  literal elasticity: +1% move in the covariate ≈ +beta% move in the rate. The form is
  deliberately scale-free (ratios, not raw-level differences) — the semi-log form it replaced
  exploded on any series not normalized to ~1. A **nonzero-beta covariate link requires a
  strictly positive path** (and strictly positive GBM start) — the engine validates this and
  rejects anything else. Read `Contracts.cs`'s `CovariateLink` doc comment before changing this
  math; it explains why with a worked example (beta 0.6 on an index drifting 20 points → e^12.5).
- **`FlowKind` member names must stay identical to `ProductManager.Domain.Common.
  SimulationFlowKind`.** The Api maps between them by name (see Domain's `CLAUDE.md`); rename
  one and you break that mapping silently.
- **Business-process scopes/coverage/modules in the firm-level engine are 64-bit bitmasks** —
  hard limit of 64 processes per run (`FirmLevelContracts.cs`, `ProcessInput` doc comment).
  Consideration-set filtering and module bookkeeping are AND/OR/popcount, not set operations —
  don't refactor that into `HashSet<T>` without understanding why it's a bitmask (performance:
  ~1000 opportunities/year × MC paths, vectorized).
- **Calibration is deterministic — no RNG, no I/O.** `CalibrationEngine` fits by closed-form
  conjugate/moment updates (Beta for rates, Gamma→LogNormal for counts — `DistributionSpec` has
  no native Gamma family, so the Gamma posterior is moment-matched to LogNormal to keep positive
  support). **Zero churn events or zero cross-sell wins means "no fit"** — the prior passes
  through unchanged rather than collapsing to a degenerate posterior; check for this explicitly
  before assuming every parameter in a `CalibrationOutput` was actually updated.

## Working here

- This project has no logging, no DI, no config binding — every dependency is a plain
  constructor/method parameter. Keep it that way; the Api project (`Simulation/*InputBuilder.cs`)
  is responsible for assembling `EngineInput`/`FirmLevelInput`/`CalibrationInput` from the
  database, not this project.
- Prefer adding a `[Theory]` sweep over a hardcoded example when testing anything with a
  closed-form update (calibration) or a scale-free transform (covariate elasticity) — see
  `tests/ProductManager.Engine.Tests` for the existing pattern of theory-based coverage.

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
