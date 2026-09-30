# 08 - Assumption sets

An assumption set is a named, versioned, branchable collection of parameters. Each parameter is
a distribution (type + two numbers), a provenance (asserted / calibrated / default) and a
rationale. The aggregate engine reads exactly five kinds of parameter per segment - the flow
rates `NewFirmArrival`, `ConversionToMarket`, `WinRate`, `ChurnHazard` and, per module,
`CrossSellRate` - and the firm-level engine additionally reads a namespaced family of
choice-model knobs. Everything else in a set is documentation. A simulation run always names one
assumption set. PRD: "Simulation & calibration" (AssumptionSet) and "The choice model".
Every parameter a calibration can move also declares what informed it (`priorEvidence`), so a later fit
does not count the same observations twice - see [readiness](../readiness/README.md) section 2.
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
   Then ask the question that decides whether calibration may later use the same history:
   "**What observations informed this number?**" (next item).
4. "What informed each number: recorded outcomes, or a benchmark, a sales estimate, a flat guess?"
   Every engine parameter (`segmentName` + `flowKind`) declares it: `priorEvidence` with basis
   `Independent` (not derived from the facts in the ledger) or `Observations` (derived from outcomes
   effective in a stated period, with the population). Ask for the period and the population, not just
   the figure. A number whose basis nobody can state is left undeclared and returned as a proposed
   register entry.
5. "Should any flow move with an external series?" If yes, which covariate from 07 and how
   strongly (elasticity: +1% series -> +beta% rate). Default: no link.
6. "Do you want more than one set?" Default: one set named for the planning cycle
   (Harborline: "Baseline 2026"). Branch scenarios later with `pm assumptions branch <id>
   --name "..."` rather than seeding near-duplicates.
7. (Firm-level only) "How long is a typical sales cycle per segment, in months?" and "How
   price-sensitive is each segment relative to the others?" Defaults: instant decisions and
   sensitivity 1.0 - see the choice-model table.

**Consequential choices** here: every parameter with no source in Salesforce, Aha! or Jira - market population, competitor installed base, channel reach, operating costs, price sensitivity, switching costs, covariate elasticities and event effects ([review section 7](../review/README.md#7-quantities-that-need-outside-evidence)). Each is a reviewed assumption with provenance `Asserted` (or `Default`) and a rationale; never relabel it observed or calibrated, and never turn an agent's confidence score into a distribution width. An accurately copied asserted value is still asserted. These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only. So is the `priorEvidence` declaration: saying that a prior is `Independent` of the ledger is a claim that decides whether calibration may use that history, so it is a register entry like any other assumption.

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
| `priorEvidence` | engine parameters: yes (omitted = unknown) | object | see below | What informed the distribution. Not read by either engine; calibration refuses to count the same observations twice, and the readiness report flags an unknown one. |

`priorEvidence` (`SeedPriorEvidence`, `src/ProductManager.Cli/Seed/SeedModels.cs`):

| Field | Required | Unit / format | Notes |
|---|---|---|---|
| `basis` | yes | `Independent` or `Observations` | Exact spelling (`PriorEvidenceBasis`). |
| `observedFrom`, `observedThrough` | `Observations` with a period | ISO `yyyy-MM-dd`, inclusive, both or neither | The **effective** dates of the outcomes the prior was derived from (deal `decidedDate`, churn `effectiveDate`, arrival `month`). `observedFrom` not after `observedThrough`. |
| `recordedThrough` | `Observations` without a period | ISO `yyyy-MM-dd` | The prior was derived from the ledger as recorded through that date. This is what a calibration run stamps on what it fits; write it by hand only for a prior you copied from a previous fit. |
| `population` | no | string | Which observations: "2024 decided new-logo deals, Regional 3PLs (CRM export)". |

`Independent` has no dates: a flat `Beta(1, 1)`, a published benchmark, a sales estimate. `Observations`
needs a period or `recordedThrough`. The rule a later fit follows is in
[readiness](../readiness/README.md) section 2: an `Observations` prior whose period holds any observation
the fit reads is refused (409 `prior-evidence-reuse`, CLI exit code 6), so a prior derived from the
same deals as the fit must declare them, and the fit must use a disjoint window.

Engine flow kinds - unit, and a sensible starting distribution when all you have is a guess
(`src/ProductManager.Engine/Contracts.cs` `FlowKind`, `SimulationEngine.cs` tick loop):

| `flowKind` | What the sampled number is | Draw in the engine | Start with |
|---|---|---|---|
| `NewFirmArrival` | New firms entering the segment's prospect pool per month (Poisson mean) | `newFirms ~ Poisson(rate x multipliers)` | `Lognormal` with p1 = ln(expected firms/month), p2 = 0.3 |
| `ConversionToMarket` | Fraction of the prospect pool that opens an opportunity per month | `opps ~ Poisson(pool x rate x multipliers)`, capped at floor(pool) | `Beta` around 0.02-0.05 |
| `WinRate` | Probability that one opportunity is won | `wins ~ Binomial(opps, clamp01(rate x attractiveness x multipliers))` | `Beta(1, 1)` or a benchmark, declared `Independent`; or a win-count `Beta` from deals in an earlier period, declared `Observations` with that period (see Units) |
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
| Wins W and losses L over a period P | W / (W + L); as `Beta(W + 1, L + 1)`, **only with `priorEvidence` `Observations` and P as its period** | `WinRate` |
| Annual logo churn c (fraction, e.g. 0.11) | 1 - (1 - c)^(1/12) | `ChurnHazard` monthly (0.11 -> 0.0097) |
| Monthly churn quoted directly | as is | `ChurnHazard` |
| Module attaches per year A among H clients without it | A / (12 x H) | `CrossSellRate` |
| A probability with mean m and sd s | k = m(1-m)/s^2 - 1; alpha = m k; beta = (1-m) k | `Beta` p1, p2 |
| A positive rate with median med and "typically within a factor f" | p1 = ln(med); p2 = ln(f) / 2 | `Lognormal` |
| A positive rate with mean m and coefficient of variation cv | p2 = sqrt(ln(1 + cv^2)); p1 = ln(m) - p2^2 / 2 | `Lognormal` |
| Typical sales cycle of D months, "usually 0.5x to 2x" | `SalesCycleMu` p1 = ln(D); `SalesCycleSigma` p1 = ln(2) / 2 = 0.35 | choice kinds |
| Percent ("30% win rate") | / 100 | 0.30 |

**Counting evidence once.** A calibration run updates a prior with the facts in the ledger
(dataset 12). A `Beta(wins + 1, losses + 1)` built from last year's deals and then fitted against those
same deals counts the same evidence twice, and the posterior is overconfident. Pick one of two forms
for every parameter the fit will move:

- **An independent prior**: `Beta(1, 1)` (flat), a benchmark or an estimate that does not come from
  the ledger's facts, declared `priorEvidence.basis: Independent`. The fit counts the ledger once. Use
  this whenever the history you will load in dataset 12 is the history the numbers came from.
- **A win-count prior from a period the ledger does not hold**, declared `Observations` with
  `observedFrom` and `observedThrough`. The fit must then read a disjoint window: facts effective
  outside that period, or recorded after it (`pm calibrate run --observations-recorded-after
  <the prior's last recorded date>`). If the fit would read any outcome inside the period it is refused
  with 409 `prior-evidence-reuse` and exit code 6, before anything is stored.

Nothing is subtracted automatically: the API never removes the overlap, it only refuses. A prior the
fit moves and that declares nothing is allowed to fit, and stays an open `PriorEvidenceUnknown`
issue in the readiness report. After a fit, the fitted set carries its own evidence (`Observations`,
`recordedThrough` = the as-of date, the run id), so fitting it again over the same facts is refused too.

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
        "notes": "Planning baseline for the 2026 roadmap. Funnel rates are sales-ops and finance estimates, each declaring what informed it; everything asserted pending the first calibration run.",
        "parameters": [
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "NewFirmArrival",
            "distType": "Lognormal",
            "p1": 1.7918,
            "p2": 0.3,
            "provenance": "Asserted",
            "rationale": "About 6 new regional 3PLs per month appear in the prospect list (72 in 2025); moves with parcel volume.",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Sales-ops estimate from the prospect list; not derived from recorded arrival counts."
            },
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
            "rationale": "About 3% of the pool opens an opportunity each month (roughly 12 opportunities/month from 420 firms).",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Sales-ops rule of thumb; not derived from recorded arrival counts."
            }
          },
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "WinRate",
            "distType": "Beta",
            "p1": 9,
            "p2": 21,
            "provenance": "Asserted",
            "rationale": "Won 8 of 28 decided deals in 2024 against Stackline and Palletto; Beta(wins + 1, losses + 1). Declared Observations for 2024: a fit must read a disjoint window.",
            "priorEvidence": {
              "basis": "Observations",
              "observedFrom": "2024-01-01",
              "observedThrough": "2024-12-31",
              "population": "2024 decided new-logo deals, Regional 3PLs (CRM export)"
            }
          },
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "ChurnHazard",
            "distType": "Beta",
            "p1": 2,
            "p2": 198,
            "provenance": "Asserted",
            "rationale": "11% annual logo churn -> 1 - 0.89^(1/12) = 0.0097 per month.",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Finance planning benchmark; not derived from the churn facts in the ledger."
            }
          },
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "CrossSellRate",
            "moduleProductName": "Harborline Carrier Connect",
            "distType": "Lognormal",
            "p1": -3.912,
            "p2": 0.4,
            "provenance": "Asserted",
            "rationale": "About 2% of clients without Carrier Connect add it each month (ln 0.02 = -3.912).",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Sales estimate; not derived from recorded attaches."
            }
          },
          {
            "segmentName": "E-commerce Fulfillment",
            "flowKind": "NewFirmArrival",
            "distType": "Normal",
            "p1": 9,
            "p2": 2,
            "provenance": "Asserted",
            "rationale": "About 9 new fulfillment operators per month; the most parcel-sensitive segment.",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Analyst estimate; not derived from recorded arrival counts."
            },
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
            "rationale": "About 4% of the pool comes to market each month; fast-moving segment.",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Analyst estimate; not derived from recorded arrival counts."
            }
          },
          {
            "segmentName": "E-commerce Fulfillment",
            "flowKind": "WinRate",
            "distType": "Point",
            "p1": 0.22,
            "provenance": "Default",
            "rationale": "Placeholder until Carrier Connect v1 ships; too few decided deals in 2025 to fit a Beta.",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Placeholder; not derived from observations."
            }
          },
          {
            "segmentName": "E-commerce Fulfillment",
            "flowKind": "ChurnHazard",
            "distType": "Beta",
            "p1": 3,
            "p2": 197,
            "provenance": "Asserted",
            "rationale": "17% annual logo churn -> 0.0154 per month; operators fail or get acquired often.",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Industry estimate; not derived from the churn facts in the ledger."
            }
          },
          {
            "segmentName": "E-commerce Fulfillment",
            "flowKind": "CrossSellRate",
            "moduleProductName": "Harborline Carrier Connect",
            "distType": "Beta",
            "p1": 3,
            "p2": 97,
            "provenance": "Asserted",
            "rationale": "About 3% monthly attach among clients without the module.",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Analyst estimate; not derived from recorded attaches."
            }
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

Variant - an independent win-rate prior, for a segment whose deals calibration will fit. The flat
prior claims nothing about the win rate, so a fit over the loaded deals counts each deal once. Declare it
`Independent`; do not keep a win-count prior whose deals are the ones in dataset 12:

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "assumptionSets": [
      {
        "name": "Baseline 2026",
        "parameters": [
          {
            "segmentName": "Regional 3PLs",
            "flowKind": "WinRate",
            "distType": "Beta",
            "p1": 1,
            "p2": 1,
            "provenance": "Asserted",
            "rationale": "Flat prior: nothing is assumed before the recorded deals, so calibration counts them once.",
            "priorEvidence": {
              "basis": "Independent",
              "population": "Flat Beta(1, 1); no observations behind it."
            }
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
- `priorEvidence` is stored with the parameter and upserted with it: a PUT without it **keeps** the
  evidence the parameter already had (unknown for a new one); clearing it takes an explicit
  `clearPriorEvidence` (`pm assumptions set-param ... --clear-prior-evidence`). Declaring different evidence
  over a calibration run's stamp while the distribution is unchanged is refused (409, correction required). `basis` and the dates are parsed by the loader (a misspelled `basis` or a non-ISO date
  is a loader error); the API refuses an `Independent` prior with dates and an
  `Observations` prior with neither a period nor `recordedThrough` (400). `pm assumptions set-param
  <setId> ... --prior-basis Independent` (or `Observations` with `--prior-observed-from`,
  `--prior-observed-through` or `--prior-recorded-through`, and `--prior-population`) writes the same
  object by hand. A branch (`pm assumptions branch`) copies it.

## Verification

- `pm assumptions list` - the set exists once, with its id.
- `pm assumptions get <setId>` - every parameter with its stored key, distribution,
  provenance, rationale, covariate link and `priorEvidence` (null means unknown). Expect one `{FlowKind}:{segmentId}` key per flow
  per segment, `CrossSellRate:{segmentId}:{moduleId}` per module, and `choice.*` /
  `utility.*` keys for choice kinds.
- Frontend: `/assumptions` (`frontend/src/app/(app)/assumptions/page.tsx`) lists sets and
  their parameters, shows `rate x (<covariate> / start)^beta` on linked parameters, and lets you
  edit or add parameters. Choice-model parameters (`choice.*` keys) are listed under the heading
  "Free-form assumptions (ignored by the engine)" - that label is about the aggregate engine;
  `pm sim run --mode firm-level` does read them, so their appearance there is expected.
- Smoke run: `pm sim run --roadmap-id <roadmapId> --assumption-set-id <setId> --horizon-months
  24 --paths 200` prints a headline digest; `pm sim input <runId>` shows the exact
  `parameters` array the engine received (only engine-keyed parameters appear there).
  `--mode firm-level` exercises the choice kinds.
- Correct looks like: monthly rates in [0, 1] for the four probability-like flows, arrival
  means in "firms per month", no flow you meant to model missing from `pm sim input`.
- Evidence check, before any calibration run: every engine parameter in `pm assumptions get` has a
  `priorEvidence`, and every `Observations` period ends before the history dataset 12 will load (or the
  fit will use `--observations-recorded-after`). A parameter with null evidence is not wrong, but a fit
  will report it as an open `PriorEvidenceUnknown` issue; see
  [readiness](../readiness/README.md) section 4. Whether the fitted numbers are usable is not checked
  here, and a smoke run says nothing about it.

## Common mistakes

- A win-count prior and the same deals in the ledger: seeding `Beta(wins + 1, losses + 1)` from the
  deals you will also load in dataset 12 and then calibrating counts every deal twice. Declare the
  prior `Observations` with its period (the run is then refused until the fit uses a disjoint window), or
  use an `Independent` flat prior.
- Declaring `Independent` to get past a refusal: the declaration is a statement about the prior's source.
  If the numbers did come from the same deals, it is false, and the readiness report cannot tell.
- Omitting `priorEvidence` on an engine parameter because nobody knows: acceptable, and visible
  (`PriorEvidenceUnknown`); returning it as a proposed register entry is better.
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
