# Readiness walkthrough (Harborline, fictional)

The worked example for [`../README.md`](../README.md). There is no separate extract: the subject is the
[commercial normalization example](../../commercial/commercial-normalization.example.json) and the three
fragments generated from it in [`../../commercial/fixture/seed/`](../../commercial/fixture/seed). Suppose
they are loaded on 2026-09-30 and one calibration run is made as of that date. The API behavior is pinned
by `tests/ProductManager.Api.Tests/CalibrationReadinessTests.cs`; what only an agent can add (the
reconciliation, the verdict, the sensitivity comparisons) is in
[`../calibration-readiness.example.json`](../calibration-readiness.example.json), whose `report` is the API
report and whose numbers the onboarding tests recompute from the PRD7 artifact and fragments.

## What the run says

| Parameter (Regional 3PLs unless noted) | Prior | Evidence read | API status | Presented |
|---|---|---|---|---|
| `WinRate` | `Beta(1, 1)`, Independent | 3 won, 0 lost, 1 no-decision left out | Ready, `Beta(4, 1)` | NotReady: the blank-reason loss is unresolved |
| `ChurnHazard` | `Beta(2, 198)`, Independent | 1 churn over 48.887 client-months | Ready, `Beta(3, 245.887)` | NotReady: logo-level event against relationship-level exposure |
| `CrossSellRate` (Carrier Connect) | lognormal, Independent | no decided cross-sell (the candidate is unresolved) | Unfitted: the prior stands | Unfitted |
| `WinRate` (E-commerce) | `Point(0.22)`, no evidence declared | 1 lost | NotReady: `PriorEvidenceUnknown`, `Beta(11, 40)` | NotReady |

Run verdict `Exploratory`: one issue open, one parameter unfitted, two choice-model quantities asserted
(`price sensitivity`, `switching cost`). The holdout through 2026-12-31 is `Unsupported` for every
parameter, because the extract ends on the as-of date. Nothing is called validated.

## Acceptance criteria

| PRD8 criterion | Where it shows |
|---|---|
| 1. An independent `Beta(1,1)` prior and 6 wins/4 losses fit to `Beta(7,5)`; reusing the same ten deals on that fitted prior is flagged before fitting | API: `AC1_Independent_uniform_prior_fits_Beta_7_5_and_refitting_the_same_ten_deals_is_refused_before_fitting` (the refusal is 409, stores nothing, and a window after the first run fits only the four newer deals), and `R3_A_hand_declared_observation_prior_overlapping_the_fit_is_refused_and_a_disjoint_one_fits` for the `Beta(wins + 1, losses + 1)` recipe. Artifact: `refusedAttempts` holds that recipe on the three Regional 3PLs deals (overlap 3 of 3, exit code 6) and its resolution; the `WinRate` parameter is then fitted once, `Beta(4, 1)` |
| 2. Two genuine same-day churns count twice; ended relationships give exposure only to their end date; missing acquisition dates are reported | API: `AC2_Same_day_churns_count_twice_ended_relationships_stop_exposure_and_missing_acquisition_dates_are_reported`. Artifact: Tidewater's edge ends 2026-06-30 and adds 15.97 of the 48.887 client-months; `endedRelationships` is 1 against `churnEvents` 1, so no `ChurnWithoutEndDate`; `relationshipsWithoutAcquisitionDate` is 0, so no `MissingAcquisitionDate` |
| 3. An observed zero-arrival month contributes exposure; an unobserved month is not fabricated as zero | API: `AC3_An_observed_zero_month_contributes_exposure_and_an_unobserved_month_is_reported_missing`. Artifact: the fixture has no arrival counts (CRM creation is not firm formation), so `arrivalObservations` is empty, no arrival parameter is in the fit, and the `monthlyObservations` figure reconciles 0 to 0 |
| 4. A fit as of a date before a later-recorded outcome excludes it; unsupported relationship knowledge-history reconstruction is explained | API: `AC4_A_fit_as_of_before_a_later_recorded_outcome_excludes_it_and_explains_relationship_history`. Artifact: `exclusions` counts what the cut left out (0 here, every decision was recorded on the as-of date), and the first fixed note in `notes` says relationship spans are not reconstructed |
| 5. Reconciled source outcomes equal imported plus excluded and unresolved outcomes; a mismatch prevents Ready for the affected parameters | Artifact: `reconciliation.figures`. `closedNewLogoDeals:Regional 3PLs` is 4 in the system against 5 in `calibrationPopulation.winRate`; the named reason is one `unresolved` deal (`006EX000000A009AAA`, `DEC-commercial-northgate-a009-loss-reason`), nothing is unexplained, and `verdict` presents `WinRate` as NotReady although the API says Ready. `clientMonths` is the second, for the logo-versus-relationship exposure. The other figures match, the fee within its F8 rounding tolerance |
| 6. A successful run with unsupported choice assumptions stays exploratory and lists them; insufficient holdout history is visible and not a false pass | API: `R6_Complete_independent_evidence_is_Ready_and_one_asserted_choice_knob_makes_the_run_exploratory` and `R7_Held_out_decided_deals_are_compared_with_the_posterior_and_none_is_unsupported_never_a_pass`. Artifact: `overall` Exploratory with `asserted` listing both asserted quantities and the defaulted ones; every `holdout` comparison is Unsupported with its reason |

## Other things the example shows

- **Unknown evidence is an open issue, not a block.** The E-commerce `WinRate` has no declared
  `priorEvidence`: the fit ran and is `NotReady` with `PriorEvidenceUnknown` (API: `R3_R6_Unknown_prior_evidence_fits_and_leaves_an_open_issue_asserted_choice_knobs_keep_the_run_exploratory`).
  The remedy is a register entry, a declaration and a new run; `proposedRegisterEntries` holds the question.
- **Unfitted is an honest result.** The cross-sell candidate is unresolved, so there is nothing to fit and
  the prior stands with its own provenance. Its reason text comes from the engine verbatim.
- **Exposure holdouts are never reconstructed.** The ChurnHazard comparison is Unsupported because exposure
  over the window would come from current relationship rows (API: `R7_Exposure_denominated_parameters_have_no_holdout_without_relationship_history`).
- **Sensitivity comparisons are planned, not run.** There is no instance behind this example, so the three
  comparisons carry commands and no numbers.

## Keep in step

The report's numbers follow from the fragments: change a fixture record and the populations, posteriors
and figures change with it. Change the PRD7 artifact, its fragments, this artifact and this walkthrough
together, and run `dotnet test tests/ProductManager.Cli.Tests --filter FullyQualifiedName~OnboardingDocsTests`.
