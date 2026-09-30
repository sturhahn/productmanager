# PRD7 — Commercial normalization from Salesforce evidence

Status: Draft · 2026-09-30

## Problem and outcome

Salesforce records mix new-logo sales, renewals, expansions, services, and lost evaluations.
Account creation is not firm formation, contract value is not necessarily ARR, and Closed Lost
does not identify whether a competitor won. A mechanically valid import can therefore bias
economics and calibration. Produce a reviewed, reproducible commercial baseline with explicit
classification and denominator rules.

## Dependencies and scope

Use source discovery from [PRD3](PRD3-SourceDiscovery.md), consequential mapping review from
[PRD5](PRD5-EvidenceReview.md), and [PRD6](PRD6-ReliableSourceImports.md) for repeatable application.
Extend the instructions for datasets [06](../docs/onboarding/datasets/06-segment-economics.md),
[11](../docs/onboarding/datasets/11-firms-and-relationships.md), and
[12](../docs/onboarding/datasets/12-historical-facts.md). Preserve existing financial semantics
in the [authoritative spec](../docs/PRDs/product_requirements.md).

## Requirements

1. Define a reviewed mapping from organization-specific fields and statuses to deal types:
   new logo, renewal, cross-sell, same-product expansion, services, and unknown. Map to existing
   model behavior only when semantics agree. Renewals, services, and unsupported expansions
   do not automatically count as new-logo wins or module cross-sells.
2. Determine the calibration-eligible deal population and observation period. Keep excluded
   records with reasons. Competitive losses remain Lost; evidenced non-purchases become
   NoDecision; unknown loss reasons are unresolved rather than invented NoDecision records.
   Unknown competitor identity can remain unknown without fabricating a winning product.
3. Validate the meaning of opened, qualified, decided, and recorded dates. An expected close
   date on an active deal is not a decision. Prefer available transition history for actual
   lifecycle dates. Neither latest modification nor original record creation automatically
   establishes when a terminal outcome became known. Document date precision and gaps; use
   import-time recording when history cannot honestly support a backdated knowledge date.
4. Define the billing account/firm unit and segmentation rule. Deduplicate contacts, leads,
   accounts, subsidiaries, and contract records under that rule. An account with an old won
   deal is not automatically an active customer. Identify active relationships from reliable
   current contract/subscription evidence, with finance reconciliation where available.
5. Normalize recurring amounts by contract term and quantity. Separate base product, modules,
   one-time implementation, services, and usage components; prevent double counting module
   ARR in base ARPU. Distinguish actual contracted revenue from list price. Document any
   unsupported usage estimate instead of presenting it as directly observed recurring revenue.
6. Choose one reporting currency and record any approved exchange-rate source and date.
   Convert outside the engine or keep different currency populations separate. Do not silently
   sum raw amounts across currencies. Costs remain separately sourced or explicitly assumed.
7. Measure module penetration using unique eligible active firms, not won-opportunity counts.
   Reconcile relationship start/end dates and churn events. Record whether churn evidence is
   logo-level or product-level; do not mix these interpretations in one fitted rate.
8. Do not use Salesforce Account/Lead creation counts as market firm births or the CRM list
   as TAM without supporting evidence. Keep CRM opportunity rates and channel attribution
   distinct from market-wide conversion exposure, channel reach, and competitor affinity.
9. Produce normalized rows, mapping decisions, excluded/unresolved records, conversion formulas,
   source counts, and per-segment customer/revenue totals. Exact IDs and source dates survive
   into the import package; unresolved values do not silently deserialize into observed zeros.

## Acceptance criteria

- A renewal and a services-only opportunity do not increase new-logo calibration wins.
- A three-year recurring contract is normalized using its actual term; implementation fees
  and module revenue are separated without double counting base ARR.
- An open deal with a future CloseDate stays open. A known competitive loss stays Lost;
  a blank loss reason is reported as unresolved.
- A churned account with historical wins is excluded from current active-customer totals
  while retaining supported historical relationships and deal evidence.
- A mixed-currency extract is either converted under an accepted rule or explicitly deferred.
- CRM population growth and missing market exposure remain labeled gaps or assumptions.
- Source totals reconcile to imported, intentionally excluded, and unresolved records, with
  reproducible per-segment revenue and customer calculations.

## Implementation and validation

Deliver source mapping examples and normalization checks alongside the existing onboarding
instructions. Use existing source access and standard scripts rather than a new Salesforce
connector. For implemented transformations, first add failing fixtures for deal classification,
term/quantity conversion, currencies, duplicate accounts, ended subscriptions, ambiguous
losses, and unavailable history. Preserve applicable legacy import behavior.

## Out of scope

Replacing finance's revenue recognition, comprehensive CPQ/billing integration, forecasting
every expansion type, automatically discovering TAM, and fitting causal price/event effects.
