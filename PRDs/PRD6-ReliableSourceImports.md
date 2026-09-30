# PRD6 — Reliable source identity and import lifecycle

Status: Draft · 2026-09-30

## Problem and outcome

The existing [seed loader](../src/ProductManager.Cli/Seed/SeedLoader.cs) primarily identifies
entities by name. Opportunities match on firm, opened day, and scope; a match is skipped.
Churn matches on segment and day. Distinct source records can collapse, renamed records can
duplicate, and an imported open deal cannot advance on a later import. Safe onboarding needs
stable source identity, repeatable lifecycle application, and recovery from partial failures.

## Dependencies and scope

Use [PRD3](PRD3-SourceDiscovery.md) source instance identities. This PRD can be implemented
against fixtures before live discovery is available. Extend existing API/CLI imports and
preserve the [API-only write path and append-only facts](../docs/PRDs/product_requirements.md).
Any new persisted source identity remains industry-agnostic import metadata.

## Requirements

1. Identify imported firms, opportunities, relationship records, and historical observations
   by source system, source instance, object kind, and source record ID. Persist the binding
   through the API with uniqueness enforced at its appropriate workspace/market scope.
   Source IDs in notes alone do not provide identity enforcement.
2. Continue accepting legacy seed files. Use source identity when supplied; preserve existing
   legacy matching for files without it. Conflicting identity bindings or indistinguishable
   same-name firms must produce explicit exceptions instead of silent merging. Support a
   reviewed alias/display-name mapping where existing domain name uniqueness requires it.
3. An unchanged source record is a no-op. A renamed firm updates its existing identity. An open
   opportunity may advance through the valid qualify/decide lifecycle exactly once; rerunning
   the same closed state must not append another decision fact. Enforce repeat safety at the
   API boundary, including retries after a lost response.
4. Detect a changed outcome on an already decided opportunity before applying it. Report a
   correction-required exception and preserve the existing fact. First delivery does not
   rewrite or delete ledger history. Define the boundary clearly in the loader report.
5. Preserve distinct churn events on the same day using source event identity. Include the
   source relationship/customer reference in provenance so reconciliation can identify the
   affected customer. Do not invent dates to avoid duplicate matching.
6. Import relationship acquisition and end dates. Omission from an incremental file does not
   end a relationship. Multiple source records mapping to one domain firm/product edge must
   be deliberately consolidated; incompatible historical episodes become explicit exceptions
   rather than overwriting each other or changing the relationship model silently.
7. Define field ownership and partial-update behavior. Absent fields preserve existing values;
   explicitly clearing a field is distinct from omission. Approved imports may update
   source-owned fields; conflicts with locally maintained values must be reported.
8. Preflight schema, dates, source-ID duplicates, references, and lifecycle transitions before
   applying an import. Report planned creates, updates, no-ops, and exceptions. Apply in
   dependency order and record actual per-record outcomes so a partially completed run can
   resume. Do not promise a transaction across every dataset.
9. Preserve current pagination completeness when resolving existing records; retrying imports
   must not duplicate records beyond the first API listing page.

## Acceptance criteria

- Same-named Salesforce accounts with different IDs remain distinguishable; a rename of one
  source account does not create a second firm.
- Two same-day, same-scope opportunities with different IDs both import; an identical retry
  creates no extra opportunities or facts.
- Open → qualified → won imports advance one opportunity and append one decision fact,
  including when a successful API response was lost and the client retries.
- Two same-segment, same-day churn records survive and contribute two observations.
- Ended relationships stop contributing exposure after their real end dates; missing rows
  in an incremental import remain untouched.
- A corrected terminal outcome is reported without changing historical facts. A partial failure
  followed by a rerun produces the same supported final state as a successful first run.
- Legacy demonstration seeds still load; new fields and endpoints appear in the generated
  OpenAPI contract and onboarding examples.

## Implementation and validation

Start with failing domain/API tests for identity uniqueness and lifecycle/fact idempotency,
then CLI integration tests for repeats, lost-response recovery, partial updates, pagination,
and legacy compatibility. Extend existing contracts, loader, and persistence as needed; keep
the pure engine unaware of source systems. Update affected dataset docs and seed examples,
run onboarding-document checks and the .NET suite, and retain regenerated OpenAPI changes.

## Out of scope

Scheduled synchronization, source-system writes, generic connector infrastructure, arbitrary
ledger correction/retraction, and supporting multiple historical relationship episodes without
a separate deliberate domain decision.
