# PRD8 — Calibration readiness and baseline reconciliation

Status: Implemented · 2026-09-30 (branch onboarding-prds-3-8)

## Problem and outcome

The current verification establishes that records loaded and simulations/calibration execute.
A nonempty fit does not establish correct populations, exposure, or useful forecast behavior.
Dataset 08 also suggests a historical win-count Beta as a starting assumption: fitting those
same deals again counts the evidence twice. Establish a transparent readiness report before
presenting a calibrated baseline as support for roadmap decisions.

## Dependencies and scope

Consume accepted evidence from [PRD5](PRD5-EvidenceReview.md), reliable historical loading from
[PRD6](PRD6-ReliableSourceImports.md), and normalized commercial populations from
[PRD7](PRD7-CommercialNormalization.md). Roadmap comparison also uses
[PRD4](PRD4-PortfolioReconciliation.md). Preserve the
[pure engine and reproducible artifacts](../docs/PRDs/product_requirements.md).

## Requirements

1. Report readiness per parameter and segment, rather than one all-or-nothing fit flag.
   Include observation period, as-of date, eligible population, source counts, exclusions,
   observations used, exposure denominator, prior provenance, posterior, assumptions, and
   unsupported or unfitted parameters. Every rate carries units and denominator meaning.
2. Reconcile imported firms, active customers, module holders, base/module ARR, decision
   outcomes, churn, and monthly observations against the selected sources. Differences must
   have named reasons, including synthetic fill and unresolved data. Document tolerances
   for rounding and approved financial approximations; do not invent a universal tolerance.
3. Track which observation population and period informed a prior. Prevent calibration from
   applying the same evidence twice: use a prior independent of fit observations or disjoint
   prior/fit periods. Unknown overlap is an unresolved readiness issue. Do not implement an
   automatic subtraction of arbitrary priors. A previously fitted prior is also subject to
   this check on a later calibration run.
4. Check exposure completeness and cutoff behavior. Missing acquisition dates, omitted end
   dates, incompatible relationship episodes, or unreviewed logo/product churn definitions
   must be visible before fitting. Identify the current effective-date approximation for
   relationship history in as-of fits; do not claim full historical knowledge reconstruction.
5. Preserve zero-arrival months only when the source confirms an observed zero; missing
   months remain missing. Distinguish no observed churn/cross-sell events from missing data
   and explain existing no-fit behavior. Do not silently label an unchanged prior calibrated.
6. Treat calibration as parameter-specific. Historical win rates and cycle times do not by
   themselves validate price sensitivity, switching costs, feature effects, integration
   quality, or channel bias. Keep unsupported choice-model quantities explicitly asserted
   and show their influence with a small set of user-relevant sensitivity comparisons.
7. When trustworthy history permits, reserve a later observation period before fitting and
   compare supported predictions with held-out outcomes. State discrepancies and sample
   limitations without promising forecast accuracy. If historical snapshots or necessary
   inputs are unavailable, mark the backtest unsupported rather than reconstructing it with
   current data and future knowledge.
8. Keep execution smoke tests separate from model readiness. Permit exploratory simulation
   with explicit assumptions, but do not describe it as a validated forecast. A roadmap-only
   user is unaffected. Store the report and source/decision references alongside the run IDs
   and input artifacts; numeric calibration still uses the existing engine/API boundary.

## Acceptance criteria

- For an independent Beta(1,1) prior and 6 wins/4 losses, one fit yields Beta(7,5).
  A request to reuse the same ten deals on that fitted prior is flagged before fitting.
- Two genuine same-day churns are counted twice; ended relationships contribute exposure
  only through their end date; missing acquisition dates are reported.
- An observed zero-arrival month contributes its exposure, while an unobserved month does not
  become fabricated zero evidence.
- A fit as of a date before a genuinely later-recorded outcome excludes that outcome.
  The report also explains unsupported relationship knowledge-history reconstruction.
- Reconciled source outcomes match imported plus excluded/unresolved outcomes. A mismatch
  prevents a ready designation for affected parameters.
- A successful run with unsupported choice assumptions remains exploratory and lists those
  assumptions. Insufficient holdout history is visible and does not become a false pass.

## Implementation and validation

Update assumption initialization, historical-fact instructions, and final verification.
First implement an agent-readable readiness/reconciliation artifact, reusing existing run
inputs, fit diffs, and diagnostics. Implement overlap checks and arithmetic validation at the
API/CLI boundary as appropriate; keep source metadata outside numerical engine inputs unless
mathematically required. Add failing calibration/domain tests for evidence reuse, exposure,
zero observations, and date cutoffs before changing behavior, plus API/CLI tests for reporting.
Run the engine/calibration tests, affected integration tests, and onboarding-document checks.

## Out of scope

New inference algorithms, full automated causal validation, universal accuracy thresholds,
automatic fitting of all choice-model weights, and rewriting historical facts or immutable runs.
