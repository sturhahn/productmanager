# CLAUDE.md — src/ProductManager.Domain

Conventions for editing the domain model. See `docs/PRDs/product_requirements.md` for the
domain model's *rationale*; this file is the mechanical how.

## Invariants

- **Zero dependencies.** `ProductManager.Domain.csproj` has no package references and no
  project references — not even EF Core. Do not add any. EF Core materializes these entities by
  reflection from the `Api` project instead (see "EF ctors" below); if you find yourself wanting
  an EF attribute here, put the mapping in
  `src/ProductManager.Api/Persistence/Configurations/` instead.
- **Entities self-assign identity.** `Entity.Id` is `Guid.NewGuid()` by default (`Common/
  Entity.cs`) — never pass an id in from outside or let a caller set it. This matters for EF:
  a child object that already has a populated key when EF Core first meets it reads as "this row
  already exists" and issues a no-op UPDATE — see `src/ProductManager.Api/CLAUDE.md`
  (`AggregateTracking.MutateChildren`) for how the Api project works around it.
- **Throw `DomainConflictException`, not `InvalidOperationException`, for state-rule
  violations** (`Common/DomainException.cs`): "the request is well-formed, but the aggregate's
  current state forbids it" — e.g. adopting into a non-live roadmap, deciding an already-decided
  Opportunity, a business process being its own parent. This maps to HTTP 409 in
  `DomainExceptionHandler`. Framework exception types like `InvalidOperationException` are
  reserved for infrastructure failures (ADO.NET, EF Core), so they can't be reused for domain
  rules without confusing the two.
- **Use `ArgumentException` for malformed input** ("your values are wrong," not "the world
  won't allow this") — maps to HTTP 400. E.g. empty required strings, a coverage delta that
  doesn't raise the tier, a negative `Money` amount.
- **Industry specifics are data, not schema.** Nothing here should reference finance/DTCC/SWIFT/
  ETF/SMA-style concepts by name. Those enter as *values* in `ClientSegment`, `BusinessProcess`,
  `Approach`, `Need`, seed JSON, etc. If a change adds an enum member or column that only makes
  sense for one industry, it's very likely misplaced — model it as data instead. (See
  `FeatureDelta.Magnitude` and `FactEvent.PayloadJson`, both explicitly commented "data, not
  schema.")
- **`SimulationFlowKind` (Common/Enums.cs) member names must stay identical to
  `ProductManager.Engine.FlowKind`.** The Api maps between the two by *name* precisely so the
  Domain never needs a project reference to the Engine assembly — renaming one without the
  other silently breaks that mapping at runtime, not at compile time.
- **`Coverage.IsUnknown` is not a tier.** `CoverageTier` has no "unknown" member — competitor
  coverage that hasn't been assessed is a separate boolean/null state, never `NotPlaying`.
  Conflating the two silently biases the choice model against competitors we simply haven't
  scored yet.
- **Distributions and Money are immutable value objects (`sealed record`).** `Distribution.
  Copy()` exists because a distribution is owned by the parameter that carries it — copying a
  parameter must deep-copy its distribution, never share the reference.
- **`Period` is a calendar month**, not a `DateOnly` — capacity and effort demand are budgeted
  per month. Use `Period.FromDate` / `Period.Range` rather than hand-rolling month arithmetic.

## EF ctors

Every entity/value-object has a `private` (or in one case `internal`) parameterless constructor
commented `// EF` — that's what lets EF Core materialize instances via reflection without this
project referencing EF at all. Keep adding that pattern to new entities; don't make the real
constructor parameterless just to satisfy EF.

## Where things point

- Domain services (`Services/`) are pure, static, and stateless — they take entities in and
  return value objects/records out (`CapacityWarning`, `SequencingViolation`). They never
  mutate what's passed in.
- Cross-aggregate references (e.g. `Initiative` → target segments/needs, `Project` →
  `TeamId`) are `Guid` ids, not navigation properties — aggregates stay independently loadable.
- The Feature seam (`Seam/Feature.cs`) is the *only* place execution-side and market-side
  concepts touch inside the Domain. If you're tempted to reference a `Project` from a `Market`
  type or vice versa, that belongs on `Feature`/`FeatureDelta` instead.
- Engine math (elasticity, flows, choice model) does not live here — `Simulation/
  AssumptionSet.cs` is a schema stub for parameters only. Actual simulation logic is
  `src/ProductManager.Engine`.

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
