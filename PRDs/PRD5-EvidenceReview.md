# PRD5 — Evidence and consequential-decision review

Status: Draft · 2026-09-30

## Problem and outcome

The guide permits agents to choose documented defaults and report flags after loading.
That is suitable for minor formatting decisions, but choices about segmentation, capability
coverage, dates, or uncertainty can materially alter roadmap comparisons. The user needs a
concrete proposal before consequential interpretations become the baseline.

## Dependencies and scope

Consume [PRD3](PRD3-SourceDiscovery.md) source evidence and
[PRD4](PRD4-PortfolioReconciliation.md) portfolio mappings where relevant. Preserve existing
coverage unknown states, assumption provenance, and the
[authoritative model](../docs/PRDs/product_requirements.md). First delivery uses local
JSON/Markdown review artifacts and the existing agent conversation, not a new review product.

## Requirements

1. Keep a decision register with a stable decision key, affected records/fields, proposed value,
   evidence references, effective/source dates where available, alternatives, rationale,
   uncertainty or missing evidence, review state, and the accepted decision. Descriptive labels
   in this artifact must map explicitly to existing domain provenance/status values.
2. Separate mechanical transformations from interpretation. Trimming whitespace and approved
   currency/unit conversions may proceed automatically. Segment definitions, ambiguous
   identity merges, capability scopes, coverage tiers/deltas, planning-date conflicts,
   commercial classifications, and simulation assumptions require review when unsupported
   by an already accepted rule. Do not ask again for cases covered by that rule.
3. Present a consolidated review of consequential exceptions before loading affected records.
   Give a proposed answer and source evidence for each. Accepted records can proceed while
   unresolved records are excluded with reasons; required gaps defer the affected tier.
   Silence is not acceptance of a material modeling assumption.
4. For needs and coverage, provide a consistent rubric using existing tier definitions,
   representative evidence, and dates. Keep unknown competitor coverage unknown. A customer
   request is evidence of a need, not proof that implementing it changes win probability or
   establishes a particular coverage tier.
5. Keep extraction confidence separate from model uncertainty. An accurately copied asserted
   value is still asserted. Repeated sales notes are not independent observations merely
   because they appear on several records. Do not translate an agent's confidence score
   directly into a distribution width.
6. Identify quantities requiring evidence outside the three systems: market population,
   competitor installed base, channel market reach, operating costs, price sensitivity,
   switching costs, covariate elasticities, and event effects. Permit reviewed assumptions
   with existing provenance and rationale; never relabel them observed or calibrated.
7. Tie acceptance to the reviewed artifact version. A material change to the selected value,
   scope, or evidence invalidates acceptance for the affected decision. Formatting changes do
   not restart review. Keep source references and accepted assumptions with the import package.

## Acceptance criteria

- An agent can load a clean account mapping under an accepted rule without repeated questions.
- Unsupported Bronze-to-Gold deltas remain proposed until accepted and cannot leak into seed
  fragments through a dataset agent's independent default.
- Unknown competitor cells remain unknown; extraction confidence cannot make them Gold.
- The review distinguishes current source facts, inferred interpretations, and defaults.
- Changing a material accepted date triggers review of that decision only.
- A user can proceed with Tier A while deferring unsupported simulation assumptions, and can
  trace every accepted consequential choice to evidence or an explicit assumption.

## Implementation and validation

Update the orchestrator interview, dataset briefs, and assembly rules. Supply a decision-register
template and an example review with accepted, excluded, and deferred items. Where automated
seed assembly is used, add failing tests for exclusion of unresolved decisions and invalidation
of materially changed approvals. Preserve the current seed shape until a concrete need requires
an API-backed extension.

## Out of scope

Multi-user approval workflows, new RBAC, automated transcript ingestion, new inference engines,
and a universal provenance graph. These proposals do not pull the deferred intelligence phase
into onboarding implementation.
