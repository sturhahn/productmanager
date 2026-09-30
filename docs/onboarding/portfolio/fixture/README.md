# Portfolio fixture and walkthrough (Harborline, fictional)

Trimmed Aha!, Jira and Salesforce extracts plus the seed fragments generated from the
[mapping example](../portfolio-mapping.example.json). It extends the world of the
[discovery fixture](../../discovery/fixture/README.md): `HWMS-1` to `HWMS-3`, `HINT-12`, `HCORE-7`
and `HWEB-3` are the same records with the same IDs and values, and Salesforce `006EX000000A004AAA` is
its Bluewater opportunity. Everything else is added here. Extracts are `{ request, httpStatus, response }`;
no credentials.

## Files

| File | Shows |
|---|---|
| `aha/features.json` | 7 Aha! features (1 page): current, shipped, stale, tentative and closed |
| `jira/epics.json` | 9 epics (1 page) with links, dates, story points, one person-hour estimate |
| `salesforce/commitments.json` | 1 opportunity carrying a sales commitment for `HWMS-1` |
| `seed/01-workspace-and-teams.json`, `03-initiatives-projects-features.json`, `04-roadmap.json` | fragments generated from the mapping's selected records only (roadmap-only variant) |

## From 16 records to a portfolio

16 candidate records (7 Aha! features, 9 Jira epics) become 3 initiatives, 6 features, 5 projects and 3
teams. Six of the sixteen never reach the seed. This is what the human reviews, not the ticket list:

| Initiative | Feature (approved name) | Delivered by | Planning date | Notes |
|---|---|---|---|---|
| Carrier connectivity parity | Multi-carrier rate shopping | Carrier Connect v1; Carrier Connect rate-shopping UI | 2026-10-31 | one capability, two epics; sales promise conflicts |
| Win e-commerce fulfillment | Wave picking | none yet | 2026-12-31 | delivery held for review |
| Win e-commerce fulfillment | Cycle counting | Cycle count workflows (Done) | 2026-11-30 | Planned, not shipped |
| Win e-commerce fulfillment | Returns portal | Returns portal | 2027-03-31 | stale, external prerequisite, no estimate |
| Warehouse efficiency | Zone-based putaway; Directed replenishment | Putaway and replenishment (Done) | shipped 2026-03-31 | one Aha! feature split in two |

Held for review: `HCORE-7` and `HINT-16` (ambiguous, blocking each other), `HWEB-15` (contradictory
link), `HWMS-5` (tentative). Excluded: `HWMS-7` and `HCORE-11` (closed as Will not do / Won't Do).

## Acceptance criteria

| PRD4 criterion | Where it shows |
|---|---|
| 1. One Aha! capability, two Jira epics: one Feature, appropriate Projects, three sources traceable | `HWMS-1` is Feature "Multi-carrier rate shopping"; `HINT-12` (Integrations) and `HWEB-9` (Web) are two Projects that deliver it; the Aha! record lists both epics and both epics carry the `HWMS-1` reference. The fragment has one Feature, two Projects, and no Feature named after "Carrier Connect v1" |
| 2. Two similarly named, unlinked epics stay separate | `HCORE-7` "Wave picking engine" and `HINT-16` "Wave picking engine - carrier cut-offs": no Aha! reference on either, none listed by `HWMS-2`. Exception `EX-wave-picking-delivery` (held, `merge: false`) proposes `HWMS-2` as a match on name only and asks the human. No Project for either is in the fragment, so Wave picking has a Feature and no delivering Project |
| 3. Done epic, unreleased capability: no fabricated ship date | `HWEB-3` is Done (2026-09-22); `HWMS-3` is Ready to release on 2026-11-30. Feature "Cycle counting" is `Planned` with no `shippedDate`; `actualCompletion` is recorded, `customerAvailability` is `null`, and the roadmap item ends 2026-11-30, not on the Jira resolution date. Flagged `EX-done-not-released` |
| 4. Sales promise conflicting with the plan is an exception, not an overwrite | The Bluewater opportunity promises rate shopping on 2026-10-09; the product target is 2026-10-31 and the engineering forecast 2026-10-28. `proposedPlanningDate` stays 2026-10-31 (rule R1), the roadmap item ends then, and `EX-sales-commitment-date` is flagged high |
| 5. Stale item still on the approved roadmap is flagged, not discarded | `HWMS-4` (updated 2025-08-12) sits on release 2027.1, an approved current release. It is selected with flag `stale`, is in the fragments and the roadmap, and `EX-stale-returns-portal` ranks it high. Contrast `HWMS-5`: updated days ago but tentative, so held |
| 6. Every record has a reason; every material dependency mapped or unresolved; one vocabulary | All 16 records appear exactly once as selected, excluded or held (a test checks it). Dependencies: `D-ui-after-v1` resolved; `D-returns-sso` external with no known owner (`HPLAT-31`, outside the agreed Jira projects); the `HCORE-7`/`HINT-16` cycle unresolved. Every name in `seed/` equals an approved name in the mapping, and the reverse |

## The other fixture cases

- **Many-to-one and one-to-many.** Two epics, one capability (above); one Aha! feature `HWMS-6`
  "Warehouse optimization pack" split into two shipped Features delivered by one epic, `HCORE-2`.
- **Conflicting dates.** All five date meanings appear on Multi-carrier rate shopping and Zone-based
  putaway; Zone-based putaway is `Shipped` only because the Aha! release is released.
- **Ambiguous names.** The Wave picking pair, and the `HWEB-15` reference to `HWMS-6` that `HWMS-6` does
  not confirm (`EX-contradictory-link-hweb-15`).
- **External dependency with a missing owner.** `HWEB-14` blocked by `HPLAT-31`; no date invented.
- **Incompatible team units and a missing estimate.** Integrations and Core Platform work in story
  points; `HWEB-9` is 120 person-hours. Without a reviewed convention its demand is a gap (no rows), and
  `HWEB-14` has no estimate at all. Only `HINT-12` yields demand rows: 34 points as 17 + 17 over 2026-09
  and 2026-10, against Integrations capacity of 20 per month.

## What changes when the human answers

The mapping is re-run, never patched. If the human answers `EX-wave-picking-delivery` with "`HCORE-7`
only", the mapping gains a Project "Wave picking engine" (55 points, Core Platform) delivering Wave
picking, its roadmap item ends 2026-12-31, and `fixture/seed/` is regenerated. If they supply an
hours-to-points convention, it is written to `conventions.unitConversion` and applied to capacity and
effort alike.
