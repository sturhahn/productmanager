# Discovery fixture (Harborline, fictional)

Hand-assembled, trimmed captures of the responses a discovery run would save: Salesforce, Aha!
and Jira, a few records each. The [manifest example](../source-manifest.example.json) and the
[assessment example](../source-assessment.example.md) are derived from these files. Every file is
`{ request, httpStatus, response }`. No credentials.

## Files

| File | Shows |
|---|---|
| `salesforce/org-identity.json` | instance identity (org ID, name, sandbox flag) |
| `salesforce/account-describe.json` | field definitions and picklist values, including the custom `Segment__c` |
| `salesforce/account-count.json` | population size (7) for the bounded Account sample |
| `salesforce/accounts-sample.json` | bounded sample of 3 Accounts; `AnnualRevenue` null everywhere, `Segment__c` null for one |
| `salesforce/opportunities-page-1.json`, `-page-2.json` | one query split across two pages (3 + 2 records, `totalSize` 5, `done` false then true) |
| `salesforce/contract-history-denied.json` | HTTP 403 on `ContractHistory` |
| `aha/features.json` | 3 features, 1 page, `score` and `original_estimate` requested but unset |
| `jira/epics.json` | 3 epics, 1 page; one epic without an Aha! reference; one Done epic |

## How the artifacts follow from the fixture

1. Identity and timestamps: `sources[].instance` comes from `org-identity.json` and the request base
   URLs; `extractedAt` is when each response was saved.
2. `objects[]`: `fields` are the queried fields; `sourceIds` and `sourceLinks` come from each record's
   `Id` and `attributes.url` (Aha! `id` and `url`; Jira `key` and `self`); `rawResponseFiles` lists the
   files above. Accounts are `extractionMode: "sample"` with `populationSize` 7 from
   `account-count.json`, gated on Q1. Opportunities are `full`.
3. `absentData`: the null `AnnualRevenue`, null `Segment__c`, null `Amount`, missing `Channel__c`,
   unset Aha! `score`, and the Jira epic with no Aha! reference: reads that succeeded and found nothing.
4. `accessLimitations`: the 403 file, the only place the fixture shows a read that failed.
5. `authorityRules`: `HWEB-3` is Done in Jira (`resolutiondate` 2026-09-22) while Aha! `HWMS-3` has
   release date 2026-11-30, the concrete case behind "Jira Done is not customer availability" (Q4).
6. `HCORE-7` has no Aha! reference (null `customfield_10200`) while its title equals Aha! `HWMS-2`. A name
   match only proposes, so dataset 03 is `requires a mapping decision` with question Q8, not `ready`;
   the [portfolio step](../../portfolio/README.md) resolves it.
7. `datasetReadiness`, `tierReadiness` and `openQuestions` are the discovery judgments on top of the
   evidence; the assessment restates them for the human.

## Acceptance criteria

| PRD3 criterion | Where it shows |
|---|---|
| 1. Segmentation candidates proposed, no manual export request | `Segment__c`, `Industry` and `NumberOfEmployees` in `account-describe.json` and `accounts-sample.json` become `segmentationCandidates` on the Account object; Q1 asks which is authoritative |
| 2. Inaccessible history stated as an access limitation with affected fields | `contract-history-denied.json` becomes the `ContractHistory` entry in Salesforce `accessLimitations` (affected: relationship `acquisitionDate`, `churnEvents`, calibration exposure), `availableHistory` status `not readable`, and is absent from `absentData` |
| 3. Multi-page counts include all pages | the two opportunity page files: 3 + 2 = `rowCount` 5, `pagesFetched` 2; a test sums the files |
| 4. Roadmap-only Tier A not blocked by commercial gaps | `tierReadiness.A` is `completable`; none of the `gaps` (finance costs, market-wide totals, coverage) affects datasets 01, 03, 04 |
| 5. Reproducible scope, identity and timestamp; no writes | `scope`, `sources[].instance`, `sources[].filters` and `extractedAt`; `discovery.productManagerEntitiesCreated` and `sourceRecordsChanged` are 0 |
