# Source assessment: Harborline (example)

Human-facing companion to [`source-manifest.example.json`](source-manifest.example.json); derived
from the [fixture](fixture/README.md). Fictional data. Extracted 2026-09-30; read-only, no
ProductManager entities created, no source records changed.

## Scope

- Outcome: the Harborline 2026 roadmap and, if the evidence supports it, a calibrated firm-level
  simulation (requested tier D).
- Planning horizon 2026-10-01 to 2027-09-30; commercial observation period 2024-10-01 to 2026-09-30.
- Salesforce org `00DEX000000HRBLAAA` (Accounts of type Customer or Prospect, and their
  Opportunities); Aha! product HWMS at `harborline-example.aha.io`; Jira projects HCORE, HINT, HWEB at
  `harborline-example.atlassian.net`.

## What we found

- Salesforce Opportunities: 5 records, fetched across 2 pages (3 + 2), matching the reported total of
  5. Three Closed Won, one Closed Lost, one open (Negotiation).
- Salesforce Accounts: 3 sampled of 7 in scope. `AnnualRevenue` is null in every sampled row.
- Aha!: 3 features (HWMS-1 to HWMS-3) with releases, dates and initiatives; 1 page. No score or
  estimate is set.
- Jira: 3 epics, 1 page. HCORE-7 has no Aha! reference; HWEB-3 is Done since 2026-09-22 while its
  Aha! release date is 2026-11-30.

## What we could not read

- **Salesforce `ContractHistory`: access limitation, not absent data.** The integration user got HTTP
  403 (`INSUFFICIENT_ACCESS`). History may exist; we do not know. Affected fields:
  `marketDynamics.firms[].relationships[].acquisitionDate` (dataset 11),
  `marketDynamics.churnEvents[].effectiveDate` (dataset 12), and calibration exposure for churn and
  relationship-age flows.

## Dataset readiness

| Dataset | Classification | Note |
|---|---|---|
| 01 workspace and teams | requires a mapping decision | Jira projects as teams; capacity from you (Q5) |
| 02 market structure | requires a mapping decision | segmentation field (Q1) |
| 03 initiatives, projects, features | ready | Aha! intent plus Jira execution |
| 04 roadmap | ready | Aha! start and due dates |
| 05 coverage and needs | requires another source | no system holds a coverage grid |
| 06 segment economics | requires another source | finance costs and prospect pool are gaps |
| 07 covariates | intentionally deferred | none requested |
| 08 assumption sets | requires a mapping decision | win-rate prior from 5 deals, provenance Asserted |
| 09 market events | intentionally deferred | none supplied |
| 10 channels and integrations | requires a mapping decision | `LeadSource` (Q6) |
| 11 firms and relationships | requires a mapping decision | segmentation (Q1); acquisition dates blocked by Q2 |
| 12 historical facts | requires another source | churn needs contract history (Q2); stage mapping (Q7) |

Tier A (01, 03, 04) can complete now: no commercial gap touches it. Tier B follows once a coverage
grid is supplied. C and D need finance costs and readable contract history. Suggested: start with
tier A now, decide B to D after Q2 and Q3.

## Questions, ranked by consequence

1. **Q1** Which Account field is authoritative for segmentation?
   Candidates: `Segment__c` (populated in 2 of 3 sampled accounts); `Industry` (populated, one
   distinct value in the sample); `NumberOfEmployees` banded into size tiers.
2. **Q2** Can ContractHistory be made readable, or can you supply a Contract export?
   Candidates: grant read access to ContractHistory; supply a Contract export with start and end
   dates; accept a tier without churn calibration.
3. **Q3** Where do cost lines and the wider prospect pool come from?
   Candidates: a finance export by segment; your estimates recorded as Asserted; stop at tier A now.
4. **Q4** Which signal marks a feature as available to customers?
   Candidates: Aha! `release_date` and status (proposed default); Jira `resolutiondate` (HWEB-3 is
   Done but its Aha! release is 2026-11-30, so not proposed).
5. **Q5** Are the three Jira projects your teams, and what is capacity measured in?
   Candidates: HCORE, HINT, HWEB as Core Platform, Integrations, Web; story points per month (Jira
   `customfield_10016` is populated on all 3 epics).
6. **Q6** Is LeadSource the channel, and which values are channels?
   Candidates: Direct, Partner - Consultant, Inbound Web as channels with Existing Customer as a
   cross-sell marker; ignore LeadSource and use one direct channel.
7. **Q7** How do StageName and null Amount map to outcomes?
   Candidates: Closed Won to Won, Closed Lost to Lost, Negotiation to open; treat null Amount as
   unknown, not zero.
