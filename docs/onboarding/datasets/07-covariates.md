# 07 - Covariates

A covariate is a named external time series (a parcel volume index, an e-commerce share, an
interest rate) that can drive a simulated flow: an assumption-set parameter links to it with an
elasticity (`covariateBeta`, see [08-assumption-sets](08-assumption-sets.md)), and the engine
scales that flow's rate month by month as the series moves. Observed history is stored as
`CovariateObserved` facts in the bi-temporal facts ledger; the future is either a geometric
Brownian motion (GBM) scenario sampled per Monte Carlo path or the last observation held flat.
PRD: "Market side - dynamics", row Covariate. `pm` below means
`dotnet run --project src/ProductManager.Cli --`.

## Tier and prerequisites

- Tier C (aggregate simulation). Optional. Only needed when some flow (typically
  `NewFirmArrival`) should respond to an external series; a run with no covariate links is
  valid.
- Must already be loaded: [01-workspace-and-teams](01-workspace-and-teams.md) and
  [02-market-structure](02-market-structure.md) (the `marketName` must resolve, even though
  covariates themselves are workspace-scoped, not market-scoped).
- Load before [08-assumption-sets](08-assumption-sets.md): a parameter's `covariateName` is
  resolved against covariates already created in the same run or earlier.
- Load time: seconds; one POST per covariate plus one GET and one POST per observation.

## Questions to ask the user

1. "Which external series do you believe moves new-firm formation, conversion, win rate or
   churn in this market?" Default: none - skip this file. Do not add a covariate without a
   parameter that will link to it; unlinked covariates are ignored by the engine
   (`SimulationInputBuilder.Build` only passes covariates referenced by an engine parameter).
2. "Where does the history come from?" (public statistics release, a data vendor CSV, an
   internal KPI). Ask for the unit as printed at the source; `unit` is free text.
3. "What cadence?" The engine is monthly. Default: one observation per month, dated the first
   of the month. Quarterly or annual series are fine - the engine holds the latest value.
4. "When did each reading become known?" A statistics release lags the period it describes.
   Default `recordedDate` = publication date; if unknown, omit it (the API stamps "now").
5. "Do you want a stochastic future path?" If yes, ask for an expected annual growth rate and
   an annual volatility for the series. Default: no GBM; the latest observation is held flat,
   which moves no rate.
6. "Is the series strictly positive?" Required if any parameter links to it with a nonzero
   beta. A series that crosses zero (a spread, a net balance) must be rebased (e.g. index it
   to 100) before loading.

## Data requirements

Entity: `marketDynamics.covariates[]` (natural key `name`, scoped to the workspace).

| Field | Required | Type | Unit / format | Notes |
|---|---|---|---|---|
| `name` | yes | string | max 200 chars | Natural key. Referenced by `parameters[].covariateName` in 08. |
| `description` | no | string | | Say what the series is and why it should move a flow. |
| `unit` | no | string | free text | Documentation only; the engine reads ratios, so units never enter the math. |
| `gbmStart` | no | number | same unit as the series; must be > 0 if linked | Month-0 level of the future path. Give all three `gbm*` or none. |
| `gbmDrift` | no | number | per-month log drift | e.g. 0.003 is about +0.3%/month. |
| `gbmVol` | no | number | per-month log volatility, >= 0 | Sampled independently per MC path. |
| `observations` | no | array | | Default empty. |

Entity: `covariates[].observations[]` (natural key `effectiveDate` within the covariate).

| Field | Required | Type | Unit / format | Notes |
|---|---|---|---|---|
| `effectiveDate` | yes | string | `yyyy-MM-dd` | When the value was true in the world. Must be present and non-empty. |
| `value` | yes | number | the series' unit | Stored as `{"value": ...}` payload of a `CovariateObserved` fact. |
| `notes` | no | string | | Copied to the fact's notes. |
| `recordedDate` | no | string | `yyyy-MM-dd`, not in the future | When the reading entered the ledger (UTC midnight). Omitted = now. A future date is refused by the API. |

## Units and conversions

How the engine uses a covariate (all verified in `src/ProductManager.Engine/Contracts.cs`,
`EngineInternals.cs` and `src/ProductManager.Api/Simulation/SimulationInputBuilder.cs`):

- Elasticity form. For a parameter linked with beta, the sampled base rate in month t is
  multiplied by `(x_t / x_0) ^ beta`, where `x_0` is the covariate's month-0 value. A +1% move
  in the covariate moves the rate by about +beta%. Because it is a ratio, the series' unit and
  scale do not matter: an index at 108 and the same index normalized to 1.0 give identical
  trajectories.
- Strict positivity. Any covariate linked with a nonzero beta must have a strictly positive
  path (and a strictly positive `gbmStart`); the engine refuses the run otherwise.
- Future path. If `gbmStart`, `gbmDrift` and `gbmVol` are all set, the engine ignores the
  observations and simulates `x_t = x_{t-1} x exp(drift - vol^2/2 + vol x z_t)` from `x_0 =
  gbmStart`, with a fresh normal draw `z_t` per month per MC path. Drift and vol are per month,
  on the log scale.
- No GBM. The engine takes the latest observation with `effectiveDate <= run start date` (or,
  if none, the earliest observation; or 1.0 if there are no observations at all) and holds it
  flat for the horizon. A flat series gives a multiplier of exactly 1: history alone changes
  nothing, it just fixes the baseline `x_0`.
- Baseline. Pick `gbmStart` equal to the latest observed level so the asserted base rate in 08
  is read "at today's level of the series". If you set `gbmStart` far from the last
  observation, the rate is silently rescaled by `(gbmStart / last)^beta` relative to what you
  asserted.
- Guard. The combined multiplier (covariate x events x attractiveness) may not exceed 1000x in
  any month; the engine throws with the offending segment, flow and month
  (`SimulationEngine.MaxFlowMultiplier`).

Conversion table:

| Source figure | Conversion | Target |
|---|---|---|
| Expected annual growth g of the series (e.g. 4% = 0.04) | ln(1 + g) / 12 | `gbmDrift` (0.04 -> 0.00327) |
| Annual volatility s of the series (e.g. 7% = 0.07) | s / sqrt(12) | `gbmVol` (0.07 -> 0.0202) |
| Percent series ("16.9%") | keep the printed number (16.9) or the fraction (0.169) consistently across all observations | `value` (ratios make either fine) |
| A series that can be <= 0 | rebase, e.g. `100 x value / value_at_base_period` | `value` |
| Quarterly reading for Q3 2025 | date it `2025-07-01` (or `2025-09-30`, be consistent) | `effectiveDate` |
| Publication date of the reading | `yyyy-MM-dd` | `recordedDate` |

## Seed JSON shape

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "covariates": [
      {
        "name": "US parcel volume index",
        "description": "Monthly US domestic parcel volume, indexed to the 2024 monthly average = 100. More parcels bring more fulfillment operators to market.",
        "unit": "index (2024 = 100)",
        "gbmStart": 108.2,
        "gbmDrift": 0.003,
        "gbmVol": 0.02,
        "observations": [
          { "effectiveDate": "2025-09-01", "value": 101.4, "recordedDate": "2025-09-08" },
          { "effectiveDate": "2025-11-01", "value": 112.9, "recordedDate": "2025-11-07", "notes": "Peak season" },
          { "effectiveDate": "2026-01-01", "value": 98.7, "recordedDate": "2026-01-09" },
          { "effectiveDate": "2026-03-01", "value": 103.5, "recordedDate": "2026-03-06" },
          { "effectiveDate": "2026-05-01", "value": 105.1, "recordedDate": "2026-05-08" },
          { "effectiveDate": "2026-07-01", "value": 106.8, "recordedDate": "2026-07-09" },
          { "effectiveDate": "2026-08-01", "value": 108.2, "recordedDate": "2026-08-07" }
        ]
      },
      {
        "name": "US e-commerce share of retail",
        "description": "Quarterly e-commerce share of total US retail sales. Held flat in simulation (no GBM spec); kept for calibration context.",
        "unit": "percent of retail sales",
        "observations": [
          { "effectiveDate": "2025-12-01", "value": 16.9, "recordedDate": "2026-02-20", "notes": "Q4 2025 release" },
          { "effectiveDate": "2026-03-01", "value": 16.6, "recordedDate": "2026-05-19", "notes": "Q1 2026 release" },
          { "effectiveDate": "2026-06-01", "value": 17.1, "recordedDate": "2026-08-19", "notes": "Q2 2026 release" }
        ]
      }
    ]
  }
}
```

The first covariate has a full GBM spec (`gbmStart` equals the latest reading), so a run
starting 2026-09-01 samples its future; the second has none and is held at 17.1.

## Loading and idempotency

Command: `pm seed --file <path>`.

Load order: workspace -> market -> marketDynamics -> teams -> ... Inside marketDynamics,
covariates load after `segmentStockStates` and before `assumptionSets` and `marketEvents`
(`SeedLoader.LoadMarketDynamicsAsync`).

Duplicate rule (`SeedLoader.LoadCovariatesAsync` / `ApplyCovariateObservationAsync`):

- Covariate: matched by `name` among the workspace's existing covariates. Match = reused as is;
  `description`, `unit` and the `gbm*` fields in the file are NOT applied to an existing
  covariate (create-only). To change them: `pm covariate update <id> --name ... --gbm-start
  ... --gbm-drift ... --gbm-vol ...`. Tally: `covariate created N, matched M`.
- Observation: the facts ledger is append-only, so idempotency is the loader's job. For each
  observation it fetches the covariate's history for exactly that `effectiveDate`; if any
  reading exists for that date the row is skipped (counted as matched), otherwise one
  `CovariateObserved` fact is appended with the file's `value`, `notes` and `recordedDate`
  (as UTC midnight; omitted = server now). Tally: `covariateObservation created N, matched M`.
- Consequence: you cannot correct a value by re-running the seed with a new number for the
  same date - the date already has a reading. Append a correction with `pm covariate
  add-observation <id> --workspace-id <wsId> --effective-date 2026-01-01 --value 99.1`; when the
  engine reads history it keeps the latest *recorded* value per effective date
  (`CovariateHistoryReader.ToObservations`).
- A `recordedDate` after today is refused by the API (`recordedAt ... is in the future`) and
  aborts the load at that row.
- GBM fields must be all three or none; a partial spec is refused ("A covariate future path
  needs gbmStart, gbmDrift and gbmVol together"). `gbmVol` < 0 is refused.

## Verification

- `pm covariate list` - names, units, `gbmStart/gbmDrift/gbmVol` and `hasGbmSpec`.
- `pm covariate history <covariateId> --effective-from 2025-01-01 --effective-to 2026-08-31` -
  the observations as the engine will read them (one value per effective date). Add
  `--known-as-of <ISO instant>` to see what the ledger knew at an earlier time.
- Frontend: there is no dedicated covariates page. `/assumptions`
  (`frontend/src/app/(app)/assumptions/page.tsx`) lists covariates in the parameter editor's
  covariate picker and shows `rate x (<covariate> / start)^beta` on linked parameters.
- After loading 08, `pm sim run ... ` then `pm sim input <runId>` shows the covariate as the
  engine received it: either a `gbm` block or a `monthlyPath` array of the held-flat level.
- Correct looks like: one row per period, values in the printed unit, `gbmStart` equal to the
  last observation, `hasGbmSpec: true` only where a stochastic future is intended.

## Common mistakes

- A covariate with no linking parameter: loads fine, does nothing. Add `covariateName` +
  `covariateBeta` on a parameter in 08.
- `gbmDrift` as an annual rate: 0.04 per month compounds to about 60% a year. Convert with
  ln(1 + g) / 12.
- `gbmStart` far from the latest observation: silently rescales every linked rate by
  `(gbmStart / latest)^beta`.
- A series that is zero or negative anywhere on its path while linked with nonzero beta: the
  run is refused. Rebase to an index.
- Re-running the seed to "fix" a value: the date is skipped as already observed. Append a
  correction with `pm covariate add-observation` instead.
- Future `recordedDate` (anything after today, 2026-08-31 in the examples): the API refuses it.
- Missing or empty `effectiveDate`: the loader fails to parse the date ("expected yyyy-MM-dd").
