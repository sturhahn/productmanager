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
  interview, load order, verification. Hand this file to an agent onboarding a new instance.
- `onboarding/discovery/README.md` - the detailed source-discovery procedure (PRD3): scope
  boundaries, inspection, pagination, authority by concept, dataset-to-evidence mapping, manifest
  field reference. Open when the human has connected sources.
- `onboarding/discovery/source-manifest.example.json` - the agent-readable manifest example
  (Harborline, fictional); guarded by tests.
- `onboarding/discovery/source-assessment.example.md` - the short human-facing assessment, ending in
  the consolidated ranked question list.
- `onboarding/discovery/fixture/` - trimmed Salesforce/Aha!/Jira responses (multi-page, missing
  fields, unreadable history) the examples are derived from; `fixture/README.md` walks through them.
- `onboarding/datasets/NN-*.md` — one file per dataset (12, numbered in load order): data
  requirements, units and conversions, seed JSON shape, idempotency rule, verification. Each
  follows the same eight-heading skeleton; a subagent reads exactly one.
- `onboarding/example-seed.json` — a complete, coherent example seed ("Harborline", a fictional
  warehouse-software vendor) that every dataset file's snippet is drawn from; loads via
  `pm seed` as a smoke test.
