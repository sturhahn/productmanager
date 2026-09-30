# Source discovery procedure (orchestrator detail)

Read by the orchestrator agent when [`../README.md`](../README.md) section 6 sends you here. It
covers requirements 1-6 of [PRD3](../../../PRDs/PRD3-SourceDiscovery.md); the orchestrator guide
holds the hard rules and the ordering.

## 1. Scope boundaries first

Before any extraction beyond identity checks, get these from the human (one focused question
round; propose a default for each):

| Boundary | Default to propose |
|---|---|
| Requested outcome and tier | Tier A unless the human asked for simulation or calibration |
| Planning horizon | next 12 months, whole months |
| Commercial observation period | last 24 months, ending today |
| Aha! container | one workspace/product and its roadmap or releases |
| Jira containers | explicit project keys, and an issue type and updated-since filter |
| Salesforce container | the org, plus an Account filter (for example `Type`) and an Opportunity filter tied to the observation period |

An unspecified boundary becomes a question. It is never permission to crawl a whole workspace, every
Jira project, or every Salesforce record. Record the agreed boundaries in the manifest `scope` block.
Only the containers and objects a requested dataset needs are in scope (see section 6).

## 2. Inspect before extracting

Discovery is read-only. Use only read operations on whatever connector, API or export access you
already have; do not install connectors or set up OAuth.

For each in-scope source, in this order:

1. **Identity.** Record the instance identity (Salesforce org ID and instance URL, Aha! account
   subdomain and product prefix, Jira site URL and project keys) and the extraction timestamp. Take it
   from the source's own response, not from what the human said.
2. **Definitions.** Read field definitions, picklist values, statuses and relationships for the
   objects in scope (Salesforce describe, Aha! and Jira field and status listings).
3. **Bounded samples.** Pull a small sample (a handful of rows, `LIMIT` or `maxResults`) plus a count
   of the filtered population. Use the sample to judge which fields are populated; do not use it as
   data.
4. **Extraction plan.** Only then fetch the full agreed population, and only for objects a requested
   dataset needs. If a field decision is still open (for example which field defines segments), keep
   that object at sample and gate its full extraction on the answer.

## 3. Access limits versus absent data

Two different facts, recorded in two different manifest lists:

- `accessLimitations`: the source may hold the data but you could not read it (HTTP 403, missing
  object permission, field-level security, an export that omits columns). Record the object, the error
  evidence, the ProductManager fields that depend on it, and what would resolve it.
- `absentData`: the read succeeded and the data is not there (a field that is null in every row, a
  field that does not exist, a query that returned no rows for the period).

Never conclude "no history exists" from a failed or empty-because-unreadable read. `availableHistory`
uses status `readable`, `not readable` or `not requested`; an unreadable history has no earliest or
latest date because they are unknown.

## 4. Complete pagination

One page is never the result. For every extracted object:

- Follow the source's paging until its own end marker: Salesforce `done: true` with no
  `nextRecordsUrl`; Aha! `pagination.current_page == total_pages`; Jira `startAt` plus returned
  issues reaching `total`.
- Save each raw page response and list every file in the object's `rawResponseFiles`.
- Set `rowCount` to the sum across all pages, `pagesFetched` to the number of pages, and list one
  entry per record in `sourceIds` and `sourceLinks`.
- Add a `completeness` check that compares the summed count to the source's own total. A mismatch is a
  `fail`; do not report counts until it is resolved or recorded as a limitation.

## 5. Authority by concept

Propose this default, list it in `authorityByConcept` with `status: "proposed"`, and ask the human to
confirm it or name exceptions (an exception goes in that concept's `exceptions` list):

| Concept | Authoritative system |
|---|---|
| Commercial records: accounts, opportunities, contracts | Salesforce |
| Approved product intent: initiatives, features, planned releases | Aha! |
| Execution evidence: epics, status, resolution dates | Jira |

Two rules, kept in `authorityRules`: a Jira Done status is execution evidence, not proof a customer
can use the feature (availability comes from the Aha! release and status, or whatever the human names
in the availability question); a Salesforce commitment or close date is a commercial fact, not a
delivery date. When two systems disagree, do not pick silently: surface the disagreement as a question
with both values as candidates.

## 6. Map datasets to evidence

For each requested dataset (01-12), read only its `## Tier and prerequisites` and
`## Data requirements` sections to learn what evidence it needs, then classify it with exactly one of:

| Classification | Meaning |
|---|---|
| `ready` | Evidence is in scope and maps without a further decision (a match between records by name alone is only a proposal, so it is never `ready`; see the [portfolio step](../portfolio/README.md)) |
| `requires a mapping decision` | Evidence exists but the human must choose which field or interpretation is authoritative |
| `requires another source` | Needed values are absent from, or unreadable in, the accessible systems |
| `intentionally deferred` | Outside the requested tier, or optional and not requested |

Write one `datasetReadiness` entry per dataset with `datasetId`, `file`, `tier`, `classification`,
`observed`, `proposedAssumptions`, `gaps` and `dependsOnQuestions`. Source-specific field names
(for example Salesforce `StageName`) belong only in these mapping artifacts; they never become
seed or domain schema. Datasets the human did not request are `intentionally deferred`, not skipped
silently.

Tier A (datasets 01, 03, 04) is judged on its own datasets. A commercial gap (finance costs, contract
history, prospect totals) must not block a roadmap-only onboarding: `tierReadiness.A` stays
`completable` when only tier B-D evidence is missing.

## 7. Observed values versus proposed assumptions

Keep them apart in every entry. `observed` lists values read from a source (cite the source ID or
count). `proposedAssumptions` lists anything you derived, defaulted or interpreted, and is what the
human confirms. An assumption loaded into an assumption set carries provenance `Asserted`; do not
present a rate computed from a handful of deals as calibrated.

## 8. Gaps that stay gaps

Finance costs (servicing, infrastructure, R&D drag, implementation fee) and market-wide totals (the
prospect pool beyond accounts the CRM knows about) stay listed in `gaps` when no accessible system
holds them. Do not estimate them from unrelated fields. A gap becomes an open question with candidate
ways to fill it (a finance export, a human estimate recorded as Asserted, or stopping at a lower tier).

## 9. Manifest field reference

Worked example: [`source-manifest.example.json`](source-manifest.example.json), derived from the
[fixture](fixture/README.md). Keys other steps may rely on are marked stable.

| Key | Content |
|---|---|
| `manifestVersion` (stable) | integer, 1 |
| `discovery` | `runId`, `startedAt`, `completedAt`, `readOnly`, `productManagerEntitiesCreated` (0), `sourceRecordsChanged` (0) |
| `scope` | `requestedOutcome`, `requestedTier`, `planningHorizon`, `commercialObservationPeriod`, `containers`, `boundariesConfirmedBy`, `unspecifiedBoundaries` |
| `sources[]` (stable) | one per system: `sourceId`, `system`, `instance`, `extractedAt`, `filters`, `observationPeriod`, `objects`, `availableHistory`, `completeness`, `absentData`, `accessLimitations` |
| `sources[].objects[]` (stable) | `objectType`, `extractionMode` (`full` or `sample`), `populationSize`, `rowCount`, `pagesFetched`, `fields`, `sourceIds`, `sourceLinks`, `rawResponseFiles`, `definitionFiles`, optional `segmentationCandidates` |
| `authorityByConcept`, `authorityRules` | section 5 |
| `gaps` | section 8: `concept`, `status: "gap"`, `reason`, `affectsDatasets` |
| `datasetReadiness[]` (stable) | section 6, keyed by `datasetId` |
| `tierReadiness` | per tier A-D: `datasets`, `status`, `blockedByCommercialGaps`, `dependsOnQuestions`; plus `requested` and `offer` |
| `openQuestions[]` (stable) | `id`, `rank`, `question`, `consequence`, `candidateAnswers`, `affectsDatasets` |

Never store credentials, tokens, session IDs or connection strings anywhere in the manifest or in saved
raw responses. `sourceIds` and `sourceLinks` let a later step re-read a record without re-crawling.

## 10. Consolidated questions and tier offer

Build one question list from everything above: unresolved boundaries, field-authority choices,
authority exceptions, access limitations, gaps and mapping decisions. Rank by consequence, meaning how
many datasets and tiers change with the answer. Give each question candidate answers found in the
sources (with counts or example values) and the datasets it affects. Ask for a decision between
candidates; do not ask for a manual export where accessible fields could be proposed. Ask each
question once: the answers are reused by every dataset subagent (see the brief template in
[`../README.md`](../README.md)).

When evidence is insufficient for the requested tier, put a narrower usable tier in
`tierReadiness.offer` and in the assessment, and let the human choose. See
[`source-assessment.example.md`](source-assessment.example.md) for the human-facing shape.

## 11. Where the artifacts go

Write `source-manifest.json` and `source-assessment.md` (and the raw page files) to a working
directory outside the repository, and do not commit them: they carry instance identity, record IDs and
real customer names. The files in this directory are fictional examples only.
