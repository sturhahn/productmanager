# Consolidated review: Harborline (example)

Human-facing companion to [`decision-register.example.json`](decision-register.example.json), built on the
[source assessment](../discovery/source-assessment.example.md) and the
[portfolio mapping](../portfolio/portfolio-mapping.example.json). Fictional data. Review held 2026-09-30.
Nothing below has been loaded except the rows under "Proceeding". Fragments:
[`fixture/seed/`](fixture/seed).

**Kind** says what a proposed value is: source fact (read from a current source), inferred interpretation
(a reading across records), default (a fallback with no evidence) or assumption (a modelling judgement with
no source in Salesforce, Aha! or Jira). A source fact copied correctly is still only as good as its source.

## Where you stand

| Tier | Outcome | Why |
|---|---|---|
| A roadmap | proceeds | Two roadmap items load; one waits for a re-accepted date, one for a delivery answer |
| B market and coverage | proceeds | Our own cells, one need, one accepted delta and the accepted firms load; unsupported items are out |
| C aggregate simulation | deferred | The market population is an unsupported assumption (`DEC-assumption-new-firm-arrival-regional-3pl`) |
| D firm-level and calibration | deferred | Price sensitivity has no evidence (`DEC-assumption-price-sensitivity-ecommerce`) |

You can go ahead with A and B now and come back to C and D.

## Proceeding (accepted, in force)

| Decision | Kind | What loads | Evidence |
|---|---|---|---|
| `RULE-segment-from-segment-c` | source fact | Bluewater Logistics and Cargo Nine as firms in Regional 3PLs and E-commerce Fulfillment, no further questions | Q1; two Account records |
| `RULE-unknown-competitor-coverage` | default | competitor cells with no evidence-backed tier load as unknown | dataset 05 has no source for competitors |
| `RULE-date-authority` | inferred interpretation | Carrier Connect v1 keeps its Jira window (2026-09-01 to 2026-10-15) | Q4; mapping R1-R3 |
| `DEC-date-sales-commitment-rate-shopping` | source fact | the promise of 2026-10-09 is recorded as a sales commitment only; the plan does not move | Bluewater add-on opportunity |
| `DEC-date-cycle-counting-plan` | inferred interpretation | Cycle count workflows ends 2026-11-30 (the Aha! release), not 2026-09-22 (Jira Done) | HWEB-3, HWMS-3 |
| `DEC-delta-cycle-counting` | inferred interpretation | Cycle counting delivers Cycle Counting from NotPlaying to Silver | your statement, 2026-09-30 |
| `DEC-cov-harborline-shipping` | source fact | Harborline WMS is Bronze on Shipping & Carrier Integration, confidence 0.8 (yours) | product marketing grid, 2026-06-30 |
| `DEC-cov-stackline-cycle-counting` | default | Stackline WMS on Cycle Counting stays unknown | one repeated sales claim |
| `DEC-need-ecommerce-shipping` | assumption | E-commerce Fulfillment weights Shipping & Carrier Integration 0.8, a soft need | your ranking; a lost deal shows the need |

## Reopened: needs a new answer

| Decision | What changed | Question |
|---|---|---|
| `DEC-date-rate-shopping-planning` (source fact) | you accepted an end date of 2026-10-15 for Carrier Connect rate-shopping UI; the Aha! release of HWMS-1 is now 2026-10-31 | Should the roadmap item end on 2026-10-31? |

Only this entry reopened. The sales promise of 2026-10-09 on the same feature is unchanged and stays
accepted, but the gap between the promise and the plan is now three weeks; that is a product conversation
outside onboarding, flagged since the mapping (`EX-sales-commitment-date`).

## Awaiting your answer (proposed, not loaded)

Ranked by how much the answer changes.

| # | Decision | Kind | Proposed | Evidence and gap | Alternatives | Question |
|---|---|---|---|---|---|---|
| 1 | `DEC-delta-rate-shopping-shipping` | inferred interpretation | Multi-carrier rate shopping moves Shipping & Carrier Integration from Bronze to Gold | one lost deal (Cargo Nine) shows a need, not a tier; no competitor cell is known | Bronze to Silver; no delta | After it ships, is Harborline Silver or Gold there, and what supports it? |
| 2 | `DEC-scope-wave-picking-delivery` | inferred interpretation | HCORE-7 "Wave picking engine" delivers HWMS-2 | the titles match; neither record links the other; HINT-16 may also deliver it | both epics deliver it; neither does | Which epic delivers Wave picking, and are HCORE-7 and HINT-16 one piece of work? |
| 3 | `DEC-cov-palletto-shipping-ecommerce` | inferred interpretation | Palletto Cloud is Gold on Shipping & Carrier Integration for e-commerce | a sales impression and one lost deal; extraction confidence 0.7, model uncertainty high | Silver; unknown | Any evidence beyond the impression? |
| 4 | `DEC-account-meridian-segment` | inferred interpretation | Meridian Fulfillment is E-commerce Fulfillment | `Segment__c` is null; the rule does not cover it; 60 employees is the only hint | Regional 3PLs; no segment | Which segment, or wait for Salesforce? |
| 5 | `DEC-commercial-a004-existing-business` | inferred interpretation | the Bluewater add-on is a cross-sell of Carrier Connect, outcome Won | Type Existing Business, LeadSource Existing Customer; the product is not named | a new-client win; leave it out | Cross-sell or new-client win? |

Until answered, these records are not loaded: Wave picking has a Feature and no delivering Project or
roadmap item; Multi-carrier rate shopping is a Feature with no coverage delta; Palletto's cell is unknown;
Meridian Fulfillment is not a firm; the add-on is not in the historical facts. A silence on any row leaves
it that way.

## Deferred (a tier waits)

| Decision | Kind | Tier | Why deferred |
|---|---|---|---|
| `DEC-assumption-new-firm-arrival-regional-3pl` | assumption | C | market population has no source in the three systems; a placeholder of about 6 firms a month is proposed only if you confirm it as Asserted |
| `DEC-assumption-price-sensitivity-ecommerce` | assumption | D | no evidence of price sensitivity; Lognormal median 1.0 is a starting guess, not a measurement |

## What the fragments contain

- `03-feature-deltas.json`: both features; only Cycle counting carries a delta.
- `04-roadmap.json`: Carrier Connect v1 and Cycle count workflows. Carrier Connect rate-shopping UI waits
  (reopened date); Wave picking engine waits (delivery undecided).
- `05-coverage-and-needs.json`: our shipping cell, two unknown competitor cells, one need.
- `11-firms.json`: Bluewater Logistics and Cargo Nine; not Meridian Fulfillment.

Everything else in the portfolio loads from the [portfolio fragments](../portfolio/fixture/seed) as
before; the review only touches what it lists.
