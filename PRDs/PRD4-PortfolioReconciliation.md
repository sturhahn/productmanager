# PRD4 — Portfolio reconciliation across Aha! and Jira

Status: Implemented · 2026-09-30 (branch onboarding-prds-3-8)

## Problem and outcome

Aha! and Jira contain approved plans alongside stale ideas, implementation tickets, old
releases, tentative dates, and conflicting statuses. Loading all records produces an
unrecognizable portfolio. Independent dataset agents can also assign different names and
granularity to the same capability. Produce one traceable, proposed portfolio before splitting
it into the [execution and roadmap datasets](../docs/onboarding/datasets/03-initiatives-projects-features.md).

## Dependencies and scope

Use the scoped sources and authority decisions from [PRD3](PRD3-SourceDiscovery.md).
Follow the execution model and `Project delivers Feature` seam in the
[authoritative spec](../docs/PRDs/product_requirements.md). This is source reconciliation,
not a change to product prioritization or automatic roadmap optimization.

## Requirements

1. Start from the user-designated current Aha! portfolio and follow explicit links into the
   agreed Jira projects. Include approved current work and relevant shipped capabilities;
   keep stale, tentative, unlinked, and conflicting records in an exception list. Recency
   informs review priority but never independently determines approval or authority.
2. Create one shared mapping artifact for initiatives, projects, features, teams, and referenced
   business processes. Include a stable local key, source IDs/links, selected destination
   concept, name, inclusion decision, rationale, and relationship links. All dataset agents
   consume this mapping instead of creating their own vocabularies.
3. Support many source records contributing to one capability and one source item describing
   several separately delivered capabilities. An Aha! feature is not automatically a domain
   Feature; a Jira epic is not automatically a domain Project. Model capability and delivery
   work separately and retain their explicit seam.
4. Treat explicit links as evidence of association, not automatic proof of equivalent scope.
   Name similarity may propose a match but cannot silently merge records. Detect contradictory
   explicit links and multiple plausible matches for human review.
5. Preserve date meaning: product target, engineering forecast, actual completion, customer
   availability, and sales commitment. Choose a proposed planning date using the agreed
   authority rule; flag conflicts. Do not mark a capability shipped solely because Jira says Done.
6. Keep capacity and effort units explicit. Do not compare raw story points across teams or
   infer available capacity from ticket counts. Missing estimates remain gaps; any conversion
   requires a documented, reviewed convention shared by capacity and effort datasets.
7. Resolve dependency references within the selected portfolio. Surface external prerequisites,
   missing owners, and cycles rather than dropping edges or inventing dates.
8. Output the proposed portfolio, source-to-destination mapping, exclusions with reasons,
   and material exceptions. Prepare seed fragments only from the selected mapping; apply the
   consequential-decision review in [PRD5](PRD5-EvidenceReview.md) before loading.

## Acceptance criteria

- One Aha! capability linked to two Jira epics becomes one Feature delivered by appropriate
  Projects, with all three source records traceable and no duplicate capability.
- Two similarly named but unlinked epics are separate until their equivalence is established.
- A Done epic with an unreleased product capability does not produce a fabricated ship date.
- A sales promise that conflicts with the delivery plan is visible as an exception and does
  not silently overwrite the selected roadmap date.
- A stale item still explicitly included in the approved roadmap is flagged, not discarded
  merely because it has not been updated recently.
- Every selected and excluded record has a reason; every material dependency is either mapped
  or explicitly unresolved. All generated dataset fragments use the same approved names.

## Implementation and validation

Add a portfolio-selection step and mapping examples to the onboarding workflow. Reuse existing
seed entities and feasibility checks. Test any transformation code before implementation with
fixtures covering many-to-one mappings, conflicting dates, ambiguous names, external
dependencies, and incompatible team units. A fixture walkthrough must demonstrate that the
result is a readable portfolio rather than a ticket dump.

## Out of scope

Importing every issue, backlog reprioritization, writing back to Aha!/Jira, per-person resource
planning, automatic unit conversion without evidence, and a generic entity-matching service.
