# src/ProductManager.Domain index

## Common/ — value objects, base types, enums
- `Entity.cs` — base class: self-assigned `Guid Id`, `protected set`.
- `DomainException.cs` — `DomainException` (abstract) / `DomainConflictException` (409-mapped
  state-rule violations). Open this when you need to know which exception type to throw.
- `Distribution.cs` — `Distribution` value object: type + two shape numbers (Point/Normal/
  Lognormal/Beta).
- `Money.cs` — amount + ISO currency value object; `Add` throws `DomainConflictException` on
  currency mismatch.
- `Period.cs` — calendar-month value object, totally ordered; the unit of capacity budgeting.
- `Enums.cs` — every domain enum: `CoverageTier`, `EpistemicStatus`, `ProjectStatus`,
  `FeatureStatus`, `OpportunityStage`, `MarketEventType`, `DistributionType`,
  `ParameterProvenance`, `FactEventKind`, `SimulationEngineMode`, `SimulationFlowKind`
  (member names mirror `ProductManager.Engine.FlowKind` deliberately), `FeatureDeltaKind`,
  `StrategicIntent` (Initiative-only queryable label; no Engine/calibration pathway).

## Execution/ — the roadmap & delivery side
- `Workspace.cs` — `Workspace`: root aggregate, future tenant boundary.
- `Roadmap.cs` — `Roadmap`, `RoadmapItem`: branchable/versioned plan; branch = deep copy,
  adopt = merge back to live.
- `Initiative.cs` — `Initiative`: strategic bet, targets segments/needs by id (cross-aggregate
  reference, no navigation property); optional `StrategicIntent` label
  (Differentiation/Parity/Retention), set via `SetStrategicIntent`.
- `Project.cs` — `Project`, `ProjectFeature`, `ProjectDependency`, `EffortDemand`: unit of
  execution; delivers Features (the seam); per-team per-period effort demand.
- `Team.cs` — `Team`, `TeamCapacity`: capacity budget unit, no per-person allocation.

## Seam/ — where execution meets market
- `Feature.cs` — `Feature`, `FeatureDelta`: a capability's market meaning is its deltas
  (coverage deltas mostly; `AddCoverageDelta` requires `toTier > fromTier`).

## Facts/ — the bi-temporal ledger
- `FactEvent.cs` — `FactEvent`: append-only, immutable. `EffectiveDate` (when true in the
  world) vs `RecordedDate` (when we learned it) — read this before touching anything
  calibration-adjacent.

## Market/ — market structure, demand/supply, dynamics
- `Market.cs` — `Market` (competitive arena), `ClientSegment`, `Firm`, `Competitor`.
- `Product.cs` — `Product` (a module is a Product with a parent Product), `ProductPrice`,
  `FirmProductRelationship`, `Integration`.
- `Channel.cs` — `Channel`, `ChannelSegmentReach`, `ChannelProductAffinity`: routes to market.
- `DemandSupply.cs` — `BusinessProcess` (hierarchical catalog), `Approach` (optional method
  dimension), `Need` (demand edge), `Coverage` (supply assessment → tier).
- `Dynamics.cs` — `Opportunity` (the central hinge), `OpportunityScopeItem`, `MarketEvent`,
  `MarketEventRateMultiplier`, `Covariate`.
- `SegmentStockState.cs` — `SegmentStockState`, `SegmentModulePenetration`,
  `SegmentApproachAdoption`: observed starting stocks the engine v0 simulation starts from.

## Simulation/ — parameters, not engine logic
- `AssumptionSet.cs` — `AssumptionSet`, `Parameter`, `CalibrationRun`, `SimulationRun`.
  Schema stub only — no engine math lives here; see `src/ProductManager.Engine`.

## Services/ — pure domain services (stateless, static)
- `CapacityFeasibilityService.cs` — `CapacityWarning`, `ComputeOverallocations`: demand vs
  team capacity per period.
- `RoadmapSequencingService.cs` — `SequencingViolationKind`, `SequencingViolation`: dependency
  ordering checks over a roadmap's items.

`ProductManager.Domain.csproj` — no package or project references at all (see `CLAUDE.md`).
