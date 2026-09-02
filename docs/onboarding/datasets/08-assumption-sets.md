# 08 - Assumption sets

An assumption set is a named, versioned, branchable collection of parameters. Each parameter is
a distribution (type + two numbers), a provenance (asserted / calibrated / default) and a
rationale. The aggregate engine reads exactly five kinds of parameter per segment - the flow
rates `NewFirmArrival`, `ConversionToMarket`, `WinRate`, `ChurnHazard` and, per module,
`CrossSellRate` - and the firm-level engine additionally reads a namespaced family of
choice-model knobs. Everything else in a set is documentation. A simulation run always names one
assumption set. PRD: "Simulation & calibration" (AssumptionSet) and "The choice model".
`pm` below means `dotnet run --project src/ProductManager.Cli --`.

## Tier and prerequisites

- Tier C (aggregate simulation). Required for any `pm sim run` (`--assumption-set-id` is
  mandatory). A segment with no parameter for a flow simulates that flow as off: a missing
  (segment, flow) key defaults to `Point(0)` (`src/ProductManager.Engine/Contracts.cs`,
  `ParameterSpec` remarks).
- Must already be loaded: [01-workspace-and-teams](01-workspace-and-teams.md),
  [02-market-structure](02-market-structure.md) (segments, products, business processes),
  [06-segment-economics](06-segment-economics.md) (so the rates have stocks to act on) and
  [07-covariates](07-covariates.md) if any parameter uses `covariateName`.
- Choice-model parameters (`choiceKind`) only matter for `pm sim run --mode firm-level`, which
  also needs tier D data ([10-channels-and-integrations](10-channels-and-integrations.md),
  [11-firms-and-relationships](11-firms-and-relationships.md)). They load fine without it.
- Load time: seconds; one PUT per parameter.

## Questions to ask the user

1. "Where do your funnel numbers live?" (CRM pipeline export, sales ops dashboard, board
   deck). Needed per segment: new prospects appearing per month, opportunities opened per
   month, win rate on decided deals, logo churn, module attach rate.
2. "What period do the numbers cover, and are they annual or monthly?" Default: trailing 12
   months, converted to monthly below.
3. "How sure are you?" Use it to pick a width: counts of wins/losses give a Beta directly; a
   guess gets a wide Lognormal or Beta. Default provenance is `Asserted`; use `Calibrated`
   only for values produced by a calibration run, `Default` for placeholders you want flagged.
4. "Should any flow move with an external series?" If yes, which covariate from 07 and how
   strongly (elasticity: +1% series -> +beta% rate). Default: no link.
5. "Do you want more than one set?" Default: one set named for the planning cycle
   (Harborline: "Baseline 2026"). Branch scenarios later with `pm assumptions branch <id>
   --name "..."` rather than seeding near-duplicates.
6. (Firm-level only) "How long is a typical sales cycle per segment, in months?" and "How
   price-sensitive is each segment relative to the others?" Defaults: instant decisions and
   sensitivity 1.0 - see the choice-model table.

## Data requirements

Entity: `marketDynamics.assumptionSets[]` (natural key `name`, scoped to the workspace).

| Field | Required | Type | Unit / format | Notes |
|---|---|---|---|---|
| `name` | yes | string | | Natural key. |
| `notes` | no | string | | Applied only when the set is created (see Loading). |
| `parameters` | no | array | | Default empty. |

Entity: `assumptionSets[].parameters[]`. Exactly one addressing mode per parameter:

| Mode | Give | Key the API stores | Read by |
|---|---|---|---|
| Engine parameter | `segmentName` + `flowKind` (+ `moduleProductName` when `flowKind` is `CrossSellRate`) | `{FlowKind}:{segmentId}` or `{FlowKind}:{segmentId}:{moduleProductId}` | aggregate and firm-level engines |
| Choice-model parameter | `choiceKind` (+ `segmentName` / `businessProcessName` / `moduleProductName` as the kind requires) | `choice.<kind>[:{ids}]` or `utility.<kind>:{ids}` | firm-level engine only |
| Free-form parameter | `key` (any non-empty string) | the string as given | nobody - documentation |

| Field | Required | Type | Unit / format | Notes |
|---|---|---|---|---|
| `segmentName` | engine: yes; choice: per kind | string | exact segment name | Do NOT combine with `key` alone (see Common mistakes). |
| `flowKind` | engine: yes | string | `NewFirmArrival`, `ConversionToMarket`, `WinRate`, `ChurnHazard`, `CrossSellRate` | Exact spelling (`SimulationFlowKind`). |
| `moduleProductName` | `CrossSellRate`: yes; `BrandBias`: yes; `SalesCycleMu/Sigma`: optional | string | exact product name | The cross-sold module, or "the product this concerns" for choice kinds. Refused on other flow kinds. |
| `key` | free-form: yes | string | | Ignored when `choiceKind` is set; ignored when `flowKind` is set. |
| `choiceKind` | choice: yes | string | one of the 13 kinds below | Exact spelling (`ChoiceParameterKeys.Kinds`). |
| `businessProcessName` | `ImportanceWeight`: yes | string | exact process name | |
| `distType` | no | string | `Point`, `Normal`, `Lognormal`, `Beta` | Default `Point`. Note "Lognormal", one word. |
| `p1` | yes | number | see distribution table | Default 0. |
| `p2` | Point: must be 0/omitted; others: yes | number | see distribution table | Default 0. |
| `provenance` | no | string | `Asserted`, `Calibrated`, `Default` | Default `Asserted`. |
| `rationale` | no | string | | Write one; it is the only place the "why" survives. |
| `covariateName` | no | string | exact covariate name from 07 | Engine parameters only; silently dropped on choice/free-form parameters. Needs `covariateBeta`. |
| `covariateBeta` | with `covariateName` | number | elasticity | Refused without `covariateName`. |

Engine flow kinds - unit, and a sensible starting distribution when all you have is a guess
(`src/ProductManager.Engine/Contracts.cs` `FlowKind`, `SimulationEngine.cs` tick loop):

| `flowKind` | What the sampled number is | Draw in the engine | Start with |
|---|---|---|---|
| `NewFirmArrival` | New firms entering the segment's prospect pool per month (Poisson mean) | `newFirms ~ Poisson(rate x multipliers)` | `Lognormal` with p1 = ln(expected firms/month), p2 = 0.3 |
| `ConversionToMarket` | Fraction of the prospect pool that opens an opportunity per month | `opps ~ Poisson(pool x rate x multipliers)`, capped at floor(pool) | `Beta` around 0.02-0.05 |
| `WinRate` | Probability that one opportunity is won | `wins ~ Binomial(opps, clamp01(rate x attractiveness x multipliers))` | `Beta(wins + 1, losses + 1)` from last year's decided deals |
| `ChurnHazard` | Probability that one client churns in a month | `churned ~ Binomial(clients, clamp01(rate x multipliers))` | `Beta` around the monthly hazard (see conversions) |
| `CrossSellRate` | Probability per month that a client without the module adds it | `adds ~ Binomial(clients - holders, clamp01(rate x multipliers))`, per module | `Beta` or `Lognormal` around 0.01-0.03 |

Each parameter is sampled once per Monte Carlo path (parameter uncertainty), then the monthly
counting draws add process noise. Negative samples are clamped to 0; probabilities are clamped
to [0, 1] after multipliers. `WinRate` is additionally scaled by the roadmap-driven
attractiveness multiplier in [0.5, 1.5] (`SimulationInputBuilder.BuildAttractivenessSchedule`).

Choice-model kinds (`src/ProductManager.Api/Simulation/ChoiceParameterKeys.cs`,
`src/ProductManager.Engine/FirmLevelContracts.cs` `ChoiceParameters`). Scalar knobs read the
distribution's mean; distribution-valued knobs are sampled per path.

| `choiceKind` | Extra fields | Scalar or distribution | What it does | Default when absent |
|---|---|---|---|---|
| `SwitchingCostBase` | none | scalar (utility) | Penalty on a non-incumbent candidate, scaled by the share of scope already served by others | 0.0 (no friction) |
| `WorkaroundCostWeight` | none | scalar (multiplier) | Multiplies soft-need workaround costs | 1.0 |
| `ExpansionPropensity` | none | scalar (probability scale) | Scales the chance of one mid-cycle scope expansion | 0.0 (off) |
| `NoDecisionUtility` | none | scalar (utility) | Utility of the outside option (no decision; for incumbents, churn) | 0.0 |
| `IntegrationWeight` | none | scalar (multiplier) | Multiplies mean integration quality with the firm's stack | 1.0 |
| `PriceScale` | none | scalar (currency per month) | Divisor on monthly scope price before price sensitivity applies | 1000.0 |
| `SalesCyclePriorWeight` | none | scalar (pseudo-observations) | Calibration prior strength for sales cycles; not read by simulation | 10.0 |
| `PriceSensitivity` | `segmentName` | distribution (utility per PriceScale of price) | `utility -= sensitivity x price / PriceScale` | `Point(1.0)` |
| `ScopeClusterCount` | `segmentName` | distribution (count, rounded, min 1) | Clusters in a narrow-scope opportunity | `Point(1.0)` |
| `SalesCycleMu` | `segmentName` (+ `moduleProductName` for a per-product override) | scalar (mean of log months) | Sales-cycle length ~ LogNormal(mu, sigma) in months | none: decision is instant |
| `SalesCycleSigma` | same as `SalesCycleMu` | scalar (sd of log months), clamped >= 0 | Spread of the cycle | 0.0 |
| `ImportanceWeight` | `segmentName` + `businessProcessName` | distribution (weight) | Overrides the need's point weight for that process; only fires if the need exists (05) | the need's own weight |
| `BrandBias` | `moduleProductName` (the product) + `segmentName` | scalar (utility, additive) | Brand/reputation bias of a product in a segment | 0.0 |

## Units and conversions

Distribution parameters (`src/ProductManager.Domain/Common/Distribution.cs`,
`src/ProductManager.Engine/EngineRandom.cs` `Sample`):

| `distType` | `p1` | `p2` | Sample | Validation |
|---|---|---|---|---|
| `Point` | the value | must be 0 (omit it) | p1 | p2 != 0 is refused |
| `Normal` | mean | standard deviation | p1 + p2 x z | p2 >= 0 |
| `Lognormal` | mu = mean of ln(x) | sigma = sd of ln(x) | exp(p1 + p2 x z) | p2 >= 0 |
| `Beta` | alpha | beta | Beta(alpha, beta) in (0, 1) | alpha > 0 and beta > 0 |

Means for scalar choice knobs: Point/Normal p1; Lognormal exp(p1 + p2^2/2); Beta p1/(p1+p2).

Rate conversions (the engine step is one month):

| Source figure | Conversion | Target |
|---|---|---|
| New prospects per year N | N / 12 | `NewFirmArrival` mean; Lognormal p1 = ln(N/12) |
| Opportunities opened per year O from a pool of P firms | O / (12 x P) | `ConversionToMarket` |
| Wins W and losses L over the period | W / (W + L); as `Beta(W + 1, L + 1)` | `WinRate` |
| Annual logo churn c (fraction, e.g. 0.11) | 1 - (1 - c)^(1/12) | `ChurnHazard` monthly (0.11 -> 0.0097) |
| Monthly churn quoted directly | as is | `ChurnHazard` |
| Module attaches per year A among H clients without it | A / (12 x H) | `CrossSellRate` |
| A probability with mean m and sd s | k = m(1-m)/s^2 - 1; alpha = m k; beta = (1-m) k | `Beta` p1, p2 |
| A positive rate with median med and "typically within a factor f" | p1 = ln(med); p2 = ln(f) / 2 | `Lognormal` |
| A positive rate with mean m and coefficient of variation cv | p2 = sqrt(ln(1 + cv^2)); p1 = ln(m) - p2^2 / 2 | `Lognormal` |
| Typical sales cycle of D months, "usually 0.5x to 2x" | `SalesCycleMu` p1 = ln(D); `SalesCycleSigma` p1 = ln(2) / 2 = 0.35 | choice kinds |
| Percent ("30% win rate") | / 100 | 0.30 |

Covariate link: the sampled rate in month t is multiplied by `(x_t / x_0)^covariateBeta`
(`EngineInternals.CovariateMultiplier`); beta 1.0 means the rate moves one-for-one in percent
with the series, 0.5 means half as much. Combined multipliers above 1000x abort the run.

## Seed JSON shape

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "assumptionSets": [
      {
        "name": "Baseline 2026",
        "notes": "Planning baseline for the 2026 roadmap. Funnel rates from the 2025 CRM export; everything asserted pending the first calibration run.",
        "parameters": [
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "NewFirmArrival",
            "distType": "Lognormal",
            "p1": 1.7918,
            "p2": 0.3,
            "provenance": "Asserted",
            "rationale": "About 6 new regional 3PLs per month appear in the prospect list (72 in 2025); moves with parcel volume.",
            "covariateName": "US parcel volume index",
            "covariateBeta": 0.8
          },
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "ConversionToMarket",
            "distType": "Beta",
            "p1": 3,
            "p2": 97,
            "provenance": "Asserted",
            "rationale": "About 3% of the pool opens an opportunity each month (roughly 12 opportunities/month from 420 firms)."
          },
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "WinRate",
            "distType": "Beta",
            "p1": 9,
            "p2": 21,
            "provenance": "Asserted",
            "rationale": "Won 8 of 28 decided deals in 2025 against Stackline and Palletto; Beta(wins + 1, losses + 1)."
          },
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "ChurnHazard",
            "distType": "Beta",
            "p1": 2,
            "p2": 198,
            "provenance": "Asserted",
            "rationale": "11% annual logo churn -> 1 - 0.89^(1/12) = 0.0097 per month."
          },
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "CrossSellRate",
            "moduleProductName": "Harborline Carrier Connect",
            "distType": "Lognormal",
            "p1": -3.912,
            "p2": 0.4,
            "provenance": "Asserted",
            "rationale": "About 2% of clients without Carrier Connect add it each month (ln 0.02 = -3.912)."
          },
          {
            "segmentName": "E-commerce Fulfillment",
            "flowKind": "NewFirmArrival",
            "distType": "Normal",
            "p1": 9,
            "p2": 2,
            "provenance": "Asserted",
            "rationale": "About 9 new fulfillment operators per month; the most parcel-sensitive segment.",
            "covariateName": "US parcel volume index",
            "covariateBeta": 1.2
          },
          {
            "segmentName": "E-commerce Fulfillment",
            "flowKind": "ConversionToMarket",
            "distType": "Beta",
            "p1": 4,
            "p2": 96,
            "provenance": "Asserted",
            "rationale": "About 4% of the pool comes to market each month; fast-moving segment."
          },
          {
            "segmentName": "E-commerce Fulfillment",
            "flowKind": "WinRate",
            "distType": "Point",
            "p1": 0.22,
            "provenance": "Default",
            "rationale": "Placeholder until Carrier Connect v1 ships; too few decided deals in 2025 to fit a Beta."
          },
          {
            "segmentName": "E-commerce Fulfillment",
            "flowKind": "ChurnHazard",
            "distType": "Beta",
            "p1": 3,
            "p2": 197,
            "provenance": "Asserted",
            "rationale": "17% annual logo churn -> 0.0154 per month; operators fail or get acquired often."
          },
          {
            "segmentName": "E-commerce Fulfillment",
            "flowKind": "CrossSellRate",
            "moduleProductName": "Harborline Carrier Connect",
            "distType": "Beta",
            "p1": 3,
            "p2": 97,
            "provenance": "Asserted",
            "rationale": "About 3% monthly attach among clients without the module."
          },
          {
            "choiceKind": "PriceSensitivity",
            "segmentName": "E-commerce Fulfillment",
            "distType": "Lognormal",
            "p1": 0,
            "p2": 0.35,
            "provenance": "Asserted",
            "rationale": "Median sensitivity 1.0 (one utility unit per PriceScale of monthly price), typically within a factor of 2."
          },
          {
            "choiceKind": "SalesCycleMu",
            "segmentName": "Regional 3PLs",
            "distType": "Point",
            "p1": 1.5,
            "provenance": "Asserted",
            "rationale": "Median sales cycle about 4.5 months (exp 1.5) for regional 3PLs."
          },
          {
            "choiceKind": "SwitchingCostBase",
            "distType": "Point",
            "p1": 0.6,
            "provenance": "Asserted",
            "rationale": "Replacing an incumbent WMS is a multi-month migration; moderate switching friction."
          }
        ]
      }
    ]
  }
}
```

Variant - a free-form parameter. The engine ignores it; it is a documented assumption that
travels with the set and shows up in `pm assumptions get`:

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "assumptionSets": [
      {
        "name": "Baseline 2026",
        "parameters": [
          {
            "key": "sales.average-deal-cycle-days-2025",
            "distType": "Point",
            "p1": 135,
            "provenance": "Asserted",
            "rationale": "Documentation only: measured from CRM open-to-close dates. Not read by either engine."
          }
        ]
      }
    ]
  }
}
```

## Loading and idempotency

Command: `pm seed --file <path>`.

Load order: workspace -> market -> marketDynamics -> teams -> ... Inside marketDynamics,
assumption sets load after `segmentStockStates` and `covariates` and before `marketEvents`
(`SeedLoader.LoadMarketDynamicsAsync`).

Duplicate rule (`SeedLoader.LoadAssumptionSetAsync`, `src/ProductManager.Cli/Seed/SeedLoader.cs`):

- Set: matched by `name` among the workspace's assumption sets. Match = reused; `notes` in the
  file are NOT applied to an existing set (create-only; use `pm assumptions update <id> --name
  ... --notes ...`). Tally: `assumptionSet created N, matched M`.
- Parameters: every parameter in the file is PUT on every run. The API upserts by key
  (`AssumptionSet.SetEngineParameter` / `SetParameter`): an engine parameter replaces any
  existing parameter with the same `{FlowKind}:{segmentId}[:{moduleId}]` key, a choice or
  free-form parameter replaces the one with the same string key. So re-running the same file
  is a no-op and running a changed value overwrites it. Parameters already in the set but not
  in the file are left alone (remove with `pm assumptions delete-param <setId> --parameter-id
  <paramId>`). Parameters are not counted in the tally.
- Name resolution happens before the PUT: an unknown `segmentName`, `moduleProductName`,
  `businessProcessName` or `covariateName` aborts the load with a reference error.
- Server-side validation (`AssumptionSetsController.UpsertParameter`, then the domain): giving
  `segmentName` without `flowKind` (or the reverse) is refused with "An engine parameter needs
  both clientSegmentId and flowKind"; a `CrossSellRate` without `moduleProductName`, or a
  `moduleProductName` on any other flow kind, is refused; `covariateName` without
  `covariateBeta` (or the reverse) is refused; `Point` with `p2 != 0`, `Beta` with a
  non-positive shape, or a negative `p2` on Normal/Lognormal is refused. The free-form `key`
  string itself is not validated beyond being non-empty.

## Verification

- `pm assumptions list` - the set exists once, with its id.
- `pm assumptions get <setId>` - every parameter with its stored key, distribution,
  provenance, rationale and covariate link. Expect one `{FlowKind}:{segmentId}` key per flow
  per segment, `CrossSellRate:{segmentId}:{moduleId}` per module, and `choice.*` /
  `utility.*` keys for choice kinds.
- Frontend: `/assumptions` (`frontend/src/app/(app)/assumptions/page.tsx`) lists sets and
  their parameters, shows `rate x (<covariate> / start)^beta` on linked parameters, and lets you
  edit or add parameters.
- Smoke run: `pm sim run --roadmap-id <roadmapId> --assumption-set-id <setId> --horizon-months
  24 --paths 200` prints a headline digest; `pm sim input <runId>` shows the exact
  `parameters` array the engine received (only engine-keyed parameters appear there).
  `--mode firm-level` exercises the choice kinds.
- Correct looks like: monthly rates in [0, 1] for the four probability-like flows, arrival
  means in "firms per month", no flow you meant to model missing from `pm sim input`.

## Common mistakes

- Annual rates in monthly slots: a `ChurnHazard` of 0.11 churns 11% of clients every month.
  Convert with 1 - (1 - c)^(1/12).
- `"distType": "LogNormal"` (capital N): the seed enum is `Lognormal`. The engine-side enum
  spells it differently, but the seed file uses the domain spelling.
- Lognormal `p1` as the rate itself: p1 is ln(rate). `p1: 0.02` means a median of exp(0.02) =
  1.02, i.e. a 102% monthly hazard, clamped to 1.
- `Point` with a `p2`: refused ("A point distribution has no second parameter").
- `segmentName` plus `key` with no `flowKind`: the loader sends a segment id without a flow
  kind and the API refuses it. A free-form parameter takes `key` only; a segment-scoped choice
  knob takes `choiceKind` + `segmentName`.
- Hand-typed choice keys such as `"key": "choice.price-sensitivity:E-commerce Fulfillment"`:
  stored, never read (the engine expects GUIDs in those keys). Use `choiceKind`.
- Forgetting `moduleProductName` on `CrossSellRate`, or adding one to another flow: refused.
- A covariate link on a choice or free-form parameter: accepted and silently dropped.
- Wide `Normal` on a probability: samples below 0 are clamped to 0 and above 1 to 1, skewing
  the mean. Use `Beta` for probabilities and `Lognormal` for positive rates.
