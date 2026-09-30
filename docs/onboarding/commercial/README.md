# Commercial normalization procedure (orchestrator detail)

Read by the orchestrator agent when [`../README.md`](../README.md) section 9 sends you here. It
covers requirements 1-9 of [PRD7](../../../PRDs/PRD7-CommercialNormalization.md); the orchestrator
guide holds the ordering and the hard rules. Worked example:
[`commercial-normalization.example.json`](commercial-normalization.example.json), derived from the
[fixture](fixture/README.md), which walks through every acceptance criterion. The register entries
the example cites are in
[`commercial-register-excerpt.example.json`](commercial-register-excerpt.example.json).

## 1. When it applies and what it needs

**Applies** when Salesforce (or another commercial source) is connected and tier C or D is requested
(datasets 06, 11, 12). **Skip it** for tier A or B alone and on the spreadsheet-only path: the dataset
subagents then work from the human's files and the brief's "Commercial normalization" slot says `none`.
A commercial gap never blocks tier A.

The step is documentation plus a reviewable artifact. There is no new connector and no helper code in
the repository: use the read access you already have and ad-hoc scripts outside the repo. The rules
below and the artifact shape are the contract.

Inputs, never re-asked: the discovery manifest ([`../discovery/README.md`](../discovery/README.md):
scope, `commercialObservationPeriod`, objects, `accessLimitations`, the answers to Q1 segmentation, Q2
contract history and Q7 outcomes) and the decision register
([`../review/README.md`](../review/README.md)). Read only the Data requirements and Units sections of
datasets [06](../datasets/06-segment-economics.md), [11](../datasets/11-firms-and-relationships.md)
and [12](../datasets/12-historical-facts.md).

Extraction: when this step reads more than discovery sampled (the full Account population after Q1,
contract and line objects, stage history), first refresh the manifest's `objects[]` entry
(`rowCount`, `sourceIds`, `rawResponseFiles`): PRD6 source identity is taken from it. Follow paging to
its end and check completeness as in discovery section 4. Keep the raw responses.

Output: one normalization artifact (section 10) kept in the import package, plus proposed register
entries for every interpretation the accepted rules do not cover. The step writes no fragments and no
ProductManager entities; the dataset subagents prepare fragments from the artifact's `normalized` rows
once the review (guide section 10) has accepted what they depend on.

## 2. Deal types (requirement 1)

Map every opportunity to one type from organization-specific fields (`Type`, record type, stage,
lead source, the contract lines it produced). The map is an accepted register **rule**
(`commercialClassification`, for example `RULE-commercial-deal-type`); a deal the rule does not cover
is its own decision. Map to existing model behavior only when the semantics agree:

| Type | Typical evidence | In the model |
|---|---|---|
| `newLogo` | Type New Business; a firm with no earlier contract | an opportunity; a decided outcome feeds `WinRate` |
| `renewal` | Type Renewal; extends a contract the firm already holds | none: excluded from opportunities and from every win count, kept with its reason; its contract still supports the relationship |
| `crossSell` | a module (a Product with a `parentProductName`) sold to a firm that already holds the parent product, named on a contract line | an opportunity with `crossSellModuleProductName`; feeds `CrossSellRate`, never `WinRate` |
| `sameProductExpansion` | more sites or units of a product the firm already holds | none: excluded until the model has an expansion behavior |
| `services` | Type Services; only one-time service lines | none: excluded, kept with its reason |
| `unknown` | any other or empty Type | unresolved until decided |

`Existing Business` style values are ambiguous by nature (a module, more units, a renewal): the rule
maps them to nothing and each deal becomes a decision, with the contract line that names the product as
its evidence. Never set `crossSellModuleProductName` on a deal that is not really a module sale.

## 3. The calibration-eligible population (requirement 2)

Eligible: `newLogo` deals (and decided `crossSell` deals for their own rate) of a firm with a reviewed
segment, opened or decided inside the agreed observation period, with an outcome the evidence supports.
Record the period and, per segment, who is in it in `calibrationPopulation`. Everything else is
`excluded` (renewal, services, duplicate, outside the period) or `unresolved`, each with a `reason`.

| Evidence | Outcome (seed) |
|---|---|
| won | `Won`, `winningProductName` = our product |
| lost to a named competitor | `Lost`; `winningProductName` = that competitor's product only when the competitor maps to exactly one product in the market |
| lost to a competitor the source does not name | `Lost`, no `winningProductName` (unknown stays unknown) |
| an evidenced non-purchase (a recorded reason and a note that nobody was chosen) | `NoDecision`, no winner |
| lost, reason blank or not in the rule | unresolved: not loaded, and not invented as `NoDecision` |
| still in an active stage | open: no `decidedDate`, no `outcome` |

`NoDecision` leaves the win-rate denominator ([dataset 12](../datasets/12-historical-facts.md)), so
inventing it for an unexplained loss inflates the fitted rate; dropping a real loss does the same. An
unresolved loss therefore also caps what the counts mean: write that in
`calibrationPopulation.winRate[segment].denominatorCaveat`.

## 4. Dates (requirement 3)

Four dates, kept apart; day precision, UTC dates from timestamps. An accepted rule
(`RULE-commercial-dates`) fixes the source of each one:

| Meaning | Seed field | Source, in order of preference |
|---|---|---|
| opened | `openedDate` | the first stage-history row; else the record's creation date |
| qualified | `qualifiedDate` | the first history row of a qualification stage; else omit it |
| decided | `decidedDate` | the first history row of a terminal stage; else the close date of a deal already in a terminal stage, labelled as such |
| recorded | `recordedDate` | the terminal transition's date when history proves it; else omit it (the server stamps import time) |
| expected close | no field | the close date of an open deal: kept as `expectedClose` in the artifact, never loaded |

- An expected close date on an active deal is a forecast, not a decision, even when it is in the past.
- Neither the last-modified date (it moves with every later edit) nor the creation date establishes
  when a terminal outcome became known.
- Backdate `recordedDate` only when a readable history row proves when the CRM learned it. With no
  history, omit it: an as-of calibration before the import date then reads nothing from that fact, which
  is the honest answer ([dataset 12](../datasets/12-historical-facts.md), Units).
- Say what history was and was not readable. A history object that returns 403 is an access limitation,
  not proof that nothing happened; use the current field values it would have audited (contract start
  and termination dates) and list what stays unverifiable under `gaps`.

## 5. Firms, identity and active relationships (requirement 4)

- **Unit.** The firm is the contracting (billing) Account, segmented by the accepted
  `segmentDefinition` rule. A subsidiary that contracts and is billed on its own is its own firm; one
  billed through its parent rolls up. State the choice in the rule.
- **Deduplication.** Contacts and leads are never firms. Two accounts are one firm only under an
  accepted `identityMerge` rule on hard identity (`RULE-commercial-identity`: same website host and
  country); a name similarity only proposes. Keep the duplicate in `excluded` with the survivor's id.
  A draft or superseded contract record is a duplicate of the activated one. A renewal chain is one
  edge that lists every recurring line in `sources` (a deliberate consolidation); two separate episodes
  of one product raise a question, because the model keeps one episode per edge.
- **Active means contract evidence.** An old won deal is not an active customer, and neither is a CRM
  type of Customer. `RULE-commercial-active-relationship` says which contract status and dates make an
  edge active on the as-of date. Reconcile the active count and recurring total per segment with the
  finance report when one is connected; record the difference, or list "no finance system" in `gaps`.
- **Ended.** An ended subscription becomes the relationship's `endDate` and a churn event
  (section 8). The firm, its historical edge and its historical wins stay in the fragments; it is out of
  the active totals.
- **Binding.** A source record binds to one row of one kind. Bind each edge to its recurring
  `Subscription__c` line(s), not to the Contract header (one contract can carry several edges), and give
  the churn event a different record (the contract's termination, or a billing cancellation).

## 6. Recurring amounts (requirement 5)

Formulas, recorded in the artifact `formulas` with these stable keys:

| Key | Formula |
|---|---|
| `F1-term-months` | whole calendar months from `StartDate` to `EndDate` + 1 day; equal to the stated term |
| `F2-recurring-monthly` | `Quantity x UnitPrice / termMonths` (a unit's price over the whole line term) |
| `F3-annual` | `monthly x 12` |
| `F4-currency` | `sourceAmount x rate`, per line, 2 decimals (section 7) |
| `F5-base-arpu` | sum of current base monthly over active edges / active firms with a base edge |
| `F6-module-arpu` | sum of current module monthly over active module edges / holders |
| `F7-penetration` | holders / active firms with a base edge |
| `F8-implementation-fee` | mean of the imported implementation one-time amounts of the segment's new-logo contracts |
| `F9-price-level` | contracted monthly / list monthly (the [dataset 02](../datasets/02-market-structure.md) price row), 4 decimals |
| `F10-reconcile` | the lines of a contract sum to its opportunity's Amount |

Components are separated by an accepted rule (`RULE-commercial-recurring`):

| Component | Becomes |
|---|---|
| `recurringBase` | base ARPU (F5) and the base relationship's `priceLevel` (F9) |
| `recurringModule` | the module relationship, its `priceLevel`, module ARPU (F6) and penetration (F7) |
| `implementation` | `implementationFee` (F8); a module's implementation is a price row in dataset 02, not this |
| `services` | excluded, kept with its reason |
| `usageEstimate` | excluded: a quoted volume is not a contracted fee. Never present it as observed recurring revenue; a usage figure the sources do support is its own reviewed decision |

Base ARPU never includes module revenue (the engine adds module revenue on top); the artifact records
the naive figure beside the one used (`doubleCountCheck`). An account-level ARR roll-up that includes
modules must have module ARR subtracted first. Contracted revenue, not list price, is what is
normalized; list price only enters as the F9 denominator. A contract that does not reconcile (F10) is
unresolved, not adjusted.

## 7. Currency (requirement 6)

Choose one reporting currency (the market's currency, guide question 3). Convert outside the engine
with an approved rate: record the source, the rate date and the rate in the artifact `currency`
(`RULE-commercial-currency`), apply one rate date to the whole import, and keep every row's source
amount, currency and rate. Never add raw amounts across currencies.

If there is no accepted rate, defer: the records of the other currency go to `unresolved`, and the
totals report the other population beside the reporting one and never inside it
(`currency.deferredAlternative` shows the numbers). Costs are separately sourced or explicit reviewed
assumptions (`operatingCosts`), never derived from revenue fields.

## 8. Module penetration and churn (requirement 7)

- **Penetration** is the share of active firms that hold the module: unique firms with an active edge
  to it over active firms with a base edge (F7). Won-opportunity counts are not penetration: a module
  can be held without a won cross-sell deal in the extract, and a deal can be lost or unclassified.
- **Reconcile** edges and churn both ways: every ended edge has a churn event on its `endDate`, and
  every churn event names an ended edge.
- **Level.** Record on each churn event whether the evidence is `logo` (the firm left entirely) or
  `product` (an edge ended, the firm stayed). Use one level per segment for the fitted `ChurnHazard`;
  events of the other level stay out of the ledger as ended edges and are listed in
  `calibrationPopulation.churn`. Calibration counts every relationship of ours in a segment as
  client-months, module edges included, so a logo-level event set over-weights the denominator for
  firms that hold a module; PRD8 checks that. `sourceCustomerRef` and `sourceRelationshipRef`
  ([dataset 12](../datasets/12-historical-facts.md)) carry the provenance.

## 9. CRM is not the market (requirement 8)

| CRM fact | It is not | Keep as |
|---|---|---|
| Account or Lead creation counts | firm births (`NewFirmArrival`) | gap `firmBirthsFromCrmCreation`; no `arrivalCounts` |
| the account list | the addressable pool | reviewed assumption `marketPopulation` (guide section 10 lists the outside-evidence quantities) |
| CRM opportunity rates | market-wide conversion exposure (`ConversionToMarket` needs the pool as exposure) | gap `marketWideConversionExposure` |
| lead source on deals | channel reach or origination share | gap `channelReachAndAttribution` |
| one named competitor win | a competitor's affinity or installed base | gap `competitorAffinity` |

Each stays a labelled `gap` or, once a human supplies a number, an `assumption` with a decision key
(`class` `assumption`, provenance `Asserted`; never relabelled observed or calibrated).

## 10. Outputs, reconciliation and stable keys (requirement 9)

The artifact ([example](commercial-normalization.example.json)) is written outside the repository and
kept with the register, the manifest and the fragments. Keys marked stable are the ones PRD8
(calibration readiness) and the dataset subagents rely on; they are never renamed.

| Key | Content |
|---|---|
| `normalizationVersion` (stable) | integer, 1 |
| `manifest`, `register`, `extract` | the artifacts it is built on, by reference (no copied identity), and the object counts read |
| `mappingRules` | one entry per accepted rule (`decisionKey`), including the six deal types |
| `currency` | `reporting`, `rates[]` (`from`, `to`, `rate`, `rateDate`, `source`, `decisionKey`), `deferredAlternative` |
| `formulas[]` (stable) | `key` (`F1-term-months` to `F10-reconcile`), `expression`, `unit` |
| `normalized.firms` (stable) | `sourceId`, `classification`, reviewed `name`, `segment`, `status`, `mergedDuplicateIds` |
| `normalized.deals` (stable) | `sourceId`, `classification` (deal type), `outcome`, `dates` by meaning with `basis`, `eligibleForCalibration`, `winningProductName` |
| `normalized.contracts`, `normalized.lines` | term, status, dates; components with quantity, unit price, term, source amount, `rate`, `reporting` amounts and the `formula` applied |
| `normalized.relationships` (stable) | `key`, `firmSourceId`, `product`, `kind`, `sourceIds`, `acquisitionDate`, `endDate`, `active`, `priceLevel` and its inputs |
| `normalized.churnEvents` (stable) | `key`, `firmSourceId`, `segment`, `effectiveDate`, `level`, `sourceId` |
| `excluded[]`, `unresolved[]` (stable) | `objectType`, `sourceId`, `classification` or `reason`, `decisionKey`, and a `question` when unresolved |
| `sourceCounts[]` (stable) | per `objectType`: `source` = `imported` + `excluded` + `unresolved` |
| `calibrationPopulation` (stable) | `winRate` per segment (won, lost, noDecision, open, unresolved ids), `excludedByReason`, `salesCycle`, `crossSell`, `churn`, `arrivals`, `relationships` |
| `segmentTotals[]` (stable) | per segment: `status`, `activeCustomers`, `baseMonthly`, `baseArr`, `baseArpuPerMonth`, `modules[]`, `implementationFee`, `assumptionInputs`, and the inputs of every figure |
| `gaps[]` (stable) | `key`, `status` (`gap` or `assumption`), `quantity`, `decisionKey`, `reason` |

Every source record of an extracted object ends in exactly one of `normalized`, `excluded` or
`unresolved`; `sourceCounts` must reconcile, and a total is reproducible from the rows listed as its
inputs. Mapping decisions are register keys (`RULE-commercial-deal-type`, `RULE-commercial-outcome`,
`RULE-commercial-dates`, `RULE-commercial-identity`, `RULE-commercial-active-relationship`,
`RULE-commercial-recurring`, `RULE-commercial-currency`); a record no rule covers is a proposed entry,
and nothing consequential loads before the review accepts it.

Exact ids and dates survive into the import package: every fragment row that comes from a record
carries `source` (`objectKind` plus the exact `sourceRecordId`, [dataset 11](../datasets/11-firms-and-relationships.md)),
and the dates of a deal are the ones the `dates` block names.

## 11. Unresolved is never zero

The seed cannot say "unknown" for a number, so the encoding of unresolved is the absence of the row.
This table is what an omitted field becomes, and what to do instead:

| Seed field | If left out or 0 | Do this |
|---|---|---|
| `segmentStockStates[]` numbers, and module `penetration` / `arpuPerMonth` | stored as 0; the PUT replaces the whole state | leave the whole row out until every number is sourced or is an accepted assumption; an accepted 0 is a reviewed value, an omission never is |
| firm `segmentName` | the firm is silently dropped from firm-level runs | hold the firm unresolved |
| relationship `priceLevel` | 1.0 on a new edge (list price) | compute F9; if the contract cannot be normalized, record 1.0 as a reviewed `default` in the register and say so in `notes` |
| relationship `acquisitionDate` | null: no exposure | leave null and list the gap |
| opportunity `outcome` / `decidedDate` | live (open) | only for a really open deal; an unknown loss is left out entirely |
| `recordedDate` | stamped at import | omit when history cannot prove it |
| `arrivalCounts` `count` / `exposure` | 0 / 1.0 | load none from CRM creation counts |

## 12. The worked example

[`fixture/README.md`](fixture/README.md) walks the seven PRD7 acceptance criteria over the Salesforce
extract, the artifact and the three fragments; the tests recompute every figure from the raw records.
