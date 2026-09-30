# PRD3 — Source discovery and onboarding scope

Status: Draft · 2026-09-30

## Problem and outcome

The current [onboarding guide](../docs/onboarding/README.md) asks the user to locate data and
answer dataset questions before discovering what connected systems already contain. With
Salesforce, Aha!, and Jira access, this creates unnecessary questions and encourages importing
large amounts of irrelevant data. The agent should first produce a bounded source assessment,
then ask only questions that affect selection or interpretation.

## Dependencies and scope

Build on [PRD2](../docs/PRD2-Onboarding.md). Assume existing agent connector/API access or
user-provided exports; do not require new native connectors. Preserve the existing A–D tiers
and [product architecture](../docs/PRDs/product_requirements.md).

## Requirements

1. Before broad extraction, establish the requested outcome, planning horizon, commercial
   observation period, and initial source containers: Aha! workspace/roadmap, Jira projects,
   Salesforce organization and account/opportunity filters. Unspecified boundaries become
   focused questions rather than permission to crawl every record.
2. Inspect accessible field definitions, statuses, relationships, and bounded sample records.
   Record missing access separately from absent data. Complete paginated extraction for the
   agreed population; never interpret one page as the full result.
3. Produce an agent-readable source manifest with source system and instance identity,
   extraction timestamp, filters, observation period, object types, fields, source IDs/links,
   row counts, available history, completeness checks, and known access limitations. Store no
   credentials. Use JSON plus a short Markdown assessment; no database writes are needed.
4. Propose authority by concept: Salesforce for commercial records, Aha! for approved product
   intent, Jira for execution evidence. Confirm company-specific exceptions. Do not use
   Jira completion as proof of customer availability or a sales commitment as a delivery date.
5. Map each requested dataset to available evidence and classify it as ready, requires a
   mapping decision, requires another source, or intentionally deferred. Distinguish observed
   values from proposed assumptions. Finance costs and market-wide totals remain gaps when
   absent from the accessible systems.
6. Present one consolidated question list ranked by consequence, including candidate answers
   found in the sources. Reuse the answers across dataset agents. Offer a narrower usable
   tier when evidence is insufficient for the requested simulation tier.
7. Preserve the existing environment checks, authentication, API-only writes, load order,
   and verification. Discovery precedes per-dataset preparation and any real-data load.

## Acceptance criteria

- Given accessible segmentation fields, the agent proposes candidates and asks which is
  authoritative instead of requesting a manual account export by default.
- Given inaccessible contract history, the assessment states the access limitation and the
  affected relationship/calibration fields; it does not conclude that no history exists.
- Given a multi-page source response, manifest counts include all pages in the selected scope.
- Given a roadmap-only request, commercial model gaps do not prevent completing Tier A.
- Every extraction has reproducible scope, source instance identity, and timestamp; the
  discovery step creates no ProductManager entities and changes no source-system records.

## Implementation and validation

Extend the onboarding guide with discovery instructions, a manifest example, and the
consolidated interview. Keep source-specific field names in mapping artifacts, outside the
industry-agnostic domain. If helper code is necessary, add failing checks for pagination,
completeness classification, and bounded selection before implementing it. Validate the
workflow against a small fixture with missing fields, restricted history, and multiple pages.

## Out of scope

Native OAuth setup, connector installation, scheduled sync, organization-wide crawling,
automatic market research, and a new onboarding UI.
