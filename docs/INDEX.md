# docs/ index

- `PRDs/product_requirements.md` — the authoritative product requirements doc: vision,
  architectural principles, full domain model (execution side, the seam, market side,
  simulation & calibration), the choice model, financial model, technical stack, testing
  posture, phasing, and explicitly deferred/cut scope. This is the single source of truth for
  product decisions — every directory's `CLAUDE.md` in this repo points back here rather than
  restating it.
- `PRD1-InitialFeedback-triage.md` — triage of `PRD1-InitialFeedback.md` against the codebase:
  per-item current-state citations, proposals, effort, and suggested sequencing. Every open fork
  was resolved with Chris on 2026-08-31; the doc now carries the authoritative decision register.
- `PRD1-InitialFeedback.md` — the raw first-use feedback list that the triage above works
  from.
- `PRD2-Onboarding.md` — the short PRD for agent-driven onboarding; implemented by
  `onboarding/`.
- `onboarding/README.md` — the orchestrator agent's onboarding guide: environment checks,
  running the stack, source discovery, the mandatory/optional dataset task list, the consolidated
  interview, portfolio selection, commercial normalization, consequential-decision review, load order, verification. Hand this file to an agent onboarding a new instance.
- `onboarding/discovery/README.md` - the detailed source-discovery procedure (PRD3): scope
  boundaries, inspection, pagination, authority by concept, dataset-to-evidence mapping, manifest
  field reference. Open when the human has connected sources.
- `onboarding/discovery/source-manifest.example.json` - the agent-readable manifest example
  (Harborline, fictional); guarded by tests.
- `onboarding/discovery/source-assessment.example.md` - the short human-facing assessment, ending in
  the consolidated ranked question list.
- `onboarding/discovery/fixture/` - trimmed Salesforce/Aha!/Jira responses (multi-page, missing
  fields, unreadable history) the examples are derived from; `fixture/README.md` walks through them.
- `onboarding/portfolio/README.md` - the detailed portfolio-reconciliation procedure (PRD4): selecting
  the current portfolio, capability versus delivery, matching evidence, date meanings, units,
  dependencies, outputs, mapping field reference. Open when Aha! or Jira are connected.
- `onboarding/portfolio/portfolio-mapping.example.json` - the shared mapping artifact example
  (Harborline, fictional); guarded by tests.
- `onboarding/portfolio/fixture/` - Aha!/Jira/Salesforce extracts, the seed fragments generated from the
  mapping (`seed/`), and `README.md`, the walkthrough of every PRD4 acceptance criterion.
- `onboarding/commercial/README.md` - the detailed commercial-normalization procedure (PRD7): deal types,
  the calibration-eligible population and outcomes, date meanings, firms and active relationships, recurring
  amounts and formulas (F1-F10), currency, module penetration and churn level, CRM-is-not-the-market, the
  artifact field reference and stable keys, and how an unresolved value is encoded. Open when Salesforce is
  connected and tier C or D is requested.
- `onboarding/commercial/commercial-normalization.example.json` and `commercial-register-excerpt.example.json` -
  the worked normalization artifact and the register entries it cites (Harborline, fictional); guarded by tests.
- `onboarding/commercial/fixture/` - the Salesforce extract (accounts, opportunities, stage history,
  contracts, subscription lines), the seed fragments generated from the normalized rows (`seed/`: 06, 11, 12),
  and `README.md`, the walkthrough of every PRD7 acceptance criterion.
- `onboarding/review/README.md` - the detailed consequential-decision review procedure (PRD5): mechanical
  versus interpretive choices, the decision register and its label map, the consolidated review, the
  needs and coverage rubric, confidence versus uncertainty, outside-evidence quantities, acceptance tied to
  the reviewed version, the import package. Open before any interpretation loads.
- `onboarding/review/decision-register.template.json` and `decision-register.example.json` - the blank and
  the worked register (Harborline, fictional); guarded by tests.
- `onboarding/review/review.example.md` - the consolidated human-facing review of the example register.
- `onboarding/review/fixture/seed/` - the seed fragments generated from the example register's accepted
  decisions only (`03-feature-deltas`, `04-roadmap`, `05-coverage-and-needs`, `11-firms`).
- `onboarding/datasets/NN-*.md` — one file per dataset (12, numbered in load order): data
  requirements, units and conversions, seed JSON shape, idempotency rule, verification. Each
  follows the same eight-heading skeleton; a subagent reads exactly one.
- `onboarding/example-seed.json` — a complete, coherent example seed ("Harborline", a fictional
  warehouse-software vendor) that every dataset file's snippet is drawn from; loads via
  `pm seed` as a smoke test.
