# Commercial fixture and walkthrough (Harborline, fictional)

A fuller Salesforce capture of the same fictional org as the [discovery fixture](../../discovery/fixture/README.md),
plus the seed fragments generated from the [normalization example](../commercial-normalization.example.json).
The five discovery opportunities (`006EX000000A001AAA` to `A005AAA`) and accounts `B001` to `B004` keep their
IDs and values; every other record exists only here, and this capture's counts (11 opportunities, 7
accounts) belong to the normalization run, not to the discovery manifest. Extracts are
`{ request, httpStatus, response }`; no credentials. Instance identity comes from the discovery manifest.

## Files

| File | Shows |
|---|---|
| `salesforce/accounts.json` | 7 accounts: a duplicate (`B007`), a stale Customer type (`B005`), a null `Segment__c` (`B003`), a German account (`B006`) |
| `salesforce/opportunities.json` | 11 opportunities: new logo, renewal, services, an add-on, an open deal, a named-competitor loss, a no-decision loss, a loss with a blank reason |
| `salesforce/opportunity-history.json` | stage transitions for the two oldest deals only; every other deal has no history rows |
| `salesforce/contracts.json` | 6 contracts: a three-year one, its Draft copy, a module add-on, a renewal chain ending in a termination, a EUR contract |
| `salesforce/subscription-lines.json` | 12 lines: base, module, implementation, services and a usage estimate |
| `seed/06-segment-economics.json`, `11-firms-and-relationships.json`, `12-historical-facts.json` | fragments generated from the normalized rows, with PRD6 source identity |

`ContractHistory` stays unreadable (the 403 in the
[discovery fixture](../../discovery/fixture/salesforce/contract-history-denied.json)); `Contract` and
`Subscription__c` are readable. What is used instead of the history, and what stays unresolved, is in
"Unavailable history" below. The registers the example cites are the PRD5
[decision register](../../review/decision-register.example.json) (segment rule, the Meridian segment
decision, the Bluewater add-on decision) and the [excerpt](../commercial-register-excerpt.example.json)
that adds this step's rules and decisions.

## From 36 source records to a baseline

| Object | Source | Imported | Excluded | Unresolved |
|---|---|---|---|---|
| Account | 7 | 5 | 1 duplicate (`B007`) | 1 (`B003`, no segment) |
| Opportunity | 11 | 6 | 2 (renewal `A007`, services `A010`) | 3 (`A003`, `A004`, `A009`) |
| Contract | 6 | 5 | 1 Draft duplicate (`C006`) | 0 |
| Subscription__c | 12 | 8 | 4 (services, usage estimate, module implementation, Draft line) | 0 |

The artifact's `sourceCounts` says the same; the tests recompute it from the raw files.

## Acceptance criteria

| PRD7 criterion | Where it shows |
|---|---|
| 1. A renewal and a services-only opportunity do not increase new-logo wins | `A007` (Type Renewal, Tidewater) and `A010` (Type Services, on the duplicate account) are in `excluded` with their reason; `12-historical-facts.json` has no row for either. The Won rows in it are exactly the three new-logo wins (`A001`, `A006`, `A008`), the same three as `calibrationPopulation.winRate` |
| 2. A three-year recurring contract is normalized by its actual term; fee and module separated, base ARR not double counted | `C001` runs 2025-05-01 to 2028-04-30: 36 months (F1), so base `162,000 / 36 = 4,500` per month, 54,000 ARR, not `240,000 / 12`. Its Amount 240,000 splits into base 162,000, implementation 25,000, services 41,000 and a usage estimate 12,000 (F10). The module is a separate contract (`C002`, 900 per month): base ARPU is 4,194, and the naive 4,644 with module revenue in it is recorded and not used (`doubleCountCheck`) |
| 3. An open deal with a future CloseDate stays open; a known loss stays Lost; a blank loss reason is unresolved | `A005` (Negotiation, CloseDate 2026-11-30, null Amount) has no `decidedDate` or `outcome`, and its close date is only `expectedClose`. `A002` (Lost to Competitor, Palletto) is `Lost` with `winningProductName` Palletto Cloud. `A009` (Closed Lost, blank reason) is in `unresolved` and absent from the fragment; `A011` (reason No Decision plus a note) is `NoDecision` with no winner |
| 4. A churned account with historical wins is out of the active totals but keeps its history | Tidewater (`B005`): CRM Type still Customer, contract `C004` Terminated 2026-06-30. Its edge in `11-...json` has `endDate` 2026-06-30 (two contracts, one edge), its win `A006` is a Won row, its churn is a logo-level event. It is not in `segmentTotals.activeFirmSourceIds` |
| 5. A mixed-currency extract is converted under an accepted rule or deferred | `C005` and `A008` are EUR. `RULE-commercial-currency` (accepted) converts at 1.08 on 2026-09-30: 3,600 EUR per month becomes 3,888 USD (F4). Deferred alternative, in `currency.deferredAlternative`: without the rule the Alpenhaus records are unresolved and the totals become 1 customer and 54,000 ARR in USD, with 43,200 EUR ARR reported beside them and never added |
| 6. CRM population growth and missing market exposure are labelled gaps or assumptions | `gaps`: `firmBirthsFromCrmCreation`, `marketWideConversionExposure`, `channelReachAndAttribution`, `competitorAffinity` are gaps; the prospect pool (420) and cost lines are accepted assumptions with decision keys, not CRM counts (the CRM has 3 prospect accounts). The fragment has no `arrivalCounts` |
| 7. Source totals reconcile; per-segment revenue and customers are reproducible | `sourceCounts` (table above). Regional 3PLs: 2 active customers (`B001`, `B006`), base 4,500 + 3,888 = 8,388 per month, 100,656 ARR, ARPU 4,194; Carrier Connect held by 1 of 2 firms (0.5) at 900; implementation fee (25,000 + 12,000 + 6,480) / 3 = 14,493.33 |

## The rest of the PRD7 fixture list

**Duplicate accounts.** `B001` "Bluewater Logistics" and `B007` "Bluewater Logistics Inc" share a website host
(`www.` and `https` differ) and a billing country, so `RULE-commercial-identity` merges them: `B007` is excluded
as a duplicate of `B001` and the services deal attached to it (`A010`) is excluded as services. A name
similarity alone would only have proposed a merge. A Draft duplicate contract (`C006`, same account, opportunity,
dates and price as `C001`) is excluded the same way and would otherwise double Bluewater's base ARR.

**Ended subscriptions.** Tidewater's first contract (`C003`, 12 months) expired on 2026-02-28 and was renewed
(`C004`, opportunity `A007`); `C004` was terminated on 2026-06-30. The renewal chain is one edge whose `sources`
list both base lines, `acquisitionDate` is the first contract's start (2025-03-01), and `endDate` is the
termination. The churn event's source is `C004`, a different record from the edge's lines, because a source
record binds to one row of one kind.

**Ambiguous losses.** Three closed-lost deals, three outcomes: `A002` names a competitor (Lost), `A011`
records a reason and a note that nobody was chosen (NoDecision), `A009` records nothing (unresolved, decision
`DEC-commercial-northgate-a009-loss-reason`). Nothing is invented to fill the blank, so the Regional 3PLs
count of 3 wins in 3 decided deals carries a `denominatorCaveat`.

**Unavailable history.** `ContractHistory` is a 403, an access limitation and not "no history". Used instead:
`Contract.StartDate`, `EndDate`, `Status` and `Terminated_Date__c` as they are now, and `OpportunityHistory`,
which is readable but only has rows for two deals. Dates follow `RULE-commercial-dates`: `A001` and `A002`
have history, so their qualified and decided dates come from it and `recordedDate` equals the terminal
transition. `A006`, `A008` and `A011` have none, so `decidedDate` is the close date of a Closed deal and
`recordedDate` is omitted. Every deal's `LastModifiedDate` differs from its decision date on purpose (`A001` was
edited on 2026-08-14) and is never used. Unresolved: whether contract start dates were edited after signing,
and any contract chain that began before the earliest readable contract (`gaps.contractHistory`); the churn
event has no `recordedDate` for the same reason.

**Unresolved records.** Meridian (`B003`) has a null `Segment__c`, so the accepted segment rule does not cover it
(PRD5's `DEC-account-meridian-segment` is still proposed), and its Closed Won deal (`A003`) has no contract
record: an old won deal does not make an active customer. The E-commerce Fulfillment total is therefore
unresolved, not 0, and no stock state is loaded for it. `A004` (Type Existing Business) is not covered by the
deal-type rule; the line on `C002` names Carrier Connect, which is the evidence PRD5's entry says is missing, so
it is re-proposed. Bluewater's module edge is loaded from the contract regardless, and penetration counts
firms, not that deal.

## The fragments

Generated from the `normalized` rows only, and strictly parseable against `SeedModels`.

| Fragment | Holds | Left out, and why |
|---|---|---|
| `06-segment-economics.json` | one stock state, Regional 3PLs: 2 clients, ARPU 4,194, fee 14,493.33, Carrier Connect 0.5 at 900; the pool and cost lines are the accepted assumptions | E-commerce Fulfillment: unresolved. A stock state cannot hold an unknown, so the whole row is absent |
| `11-firms-and-relationships.json` | 5 firms and 4 edges with `source` identity, `priceLevel` from F9, Tidewater's `endDate` | `B003` (no segment), `B007` (duplicate) |
| `12-historical-facts.json` | 6 opportunities (3 Won, 1 Lost, 1 NoDecision, 1 open) and 1 churn event | `A003`, `A004`, `A009` (unresolved), `A007`, `A010` (excluded); no `crossSellModuleProductName`, no `arrivalCounts`, no `channelName`, no `scopeProcessNames` (not in the source) |

Every `sourceRecordId` exists in the extract, no source record is used on two rows of a file, and no unresolved
value is written as a zero. The seed's stock-state numbers are non-nullable, which is why the safe
encoding of unresolved is the missing row ([README section 11](../README.md#11-unresolved-is-never-zero)).
