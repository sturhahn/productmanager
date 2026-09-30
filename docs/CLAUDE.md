# CLAUDE.md — docs/

## Conventions

- **`PRDs/product_requirements.md` is authoritative and off-limits to casual editing.** It's
  explicitly excluded from the automated documentation pass that created this directory's
  ABSTRACT/INDEX/CLAUDE trio — treat it the same way: don't edit it as a side effect of a code
  change. If a product decision genuinely changes, that's a deliberate edit to the PRD itself
  (by the user, or by an agent explicitly asked to update it), not something to patch quietly
  while doing something else.
- **Do not duplicate the PRD's content into other files.** Every other `CLAUDE.md` in this repo
  cites the PRD by reference for domain-model rationale rather than re-explaining it — keep
  doing that. If you're about to write a paragraph here or elsewhere that restates PRD content,
  link to the relevant PRD section instead.
- New product docs (design notes, ADRs, phase retrospectives) belong under `docs/`, in a
  sensibly named subdirectory (mirroring `PRDs/`) — don't scatter product-level docs into `src/`
  or the repo root.
- **`onboarding/` is guarded by a test.** `tests/ProductManager.Cli.Tests/OnboardingDocsTests.cs`
  parses every ```json block in `onboarding/datasets/*.md` strictly against the CLI's
  `SeedModels` (unknown keys fail), checks enum spellings and ISO dates, checks that the eight
  H2 headings appear in order, that every relative link resolves, and that
  `onboarding/example-seed.json` resolves all of its by-name references. When you change the
  seed schema (`src/ProductManager.Cli/Seed/SeedModels.cs`), update the affected dataset file
  and the example seed in the same change, then run
  `dotnet test tests/ProductManager.Cli.Tests --filter FullyQualifiedName~OnboardingDocsTests`.
- **`onboarding/discovery/` is documentation plus examples only** (PRD3): no helper code, API,
  database or seed-schema changes. The tests also check that `source-manifest.example.json` parses
  and has the required keys, that its row counts equal the records summed across the fixture page
  files, that it holds no credentials, that all 12 datasets carry one of the four readiness values,
  and that unreadable history is an access limitation rather than absent data. Change the manifest,
  fixture and assessment together. Source-specific field names (for example `StageName`) stay in
  these mapping artifacts, never in the seed schema. The directory has no ABSTRACT/INDEX/CLAUDE
  trio, matching `onboarding/` and `datasets/`.
- **`onboarding/portfolio/` is documentation plus examples only** (PRD4): no transformation code, API,
  database or seed-schema changes; reconciliation is done by the orchestrator agent. The tests check
  that `portfolio-mapping.example.json` references the discovery manifest without repeating instance
  identity, that every record, exclusion and exception carries a reason, that every fixture Aha! feature
  and Jira epic is selected, excluded or held exactly once, that the three fragments in
  `portfolio/fixture/seed/` parse strictly against `SeedModels` and use exactly the approved names, and
  that dependencies, dates and units follow the rules in `portfolio/README.md`. Change the mapping, the
  fixture extracts, the fragments and the walkthrough together. The guide's section 8 (portfolio
  selection) sits between the interview and loading; if you renumber guide sections, fix every
  cross-reference (grep for "section"). Section 9 is commercial normalization, section 10 the
  consequential-decision review, and assembling and loading is section 11. No ABSTRACT/INDEX/CLAUDE trio
  inside this directory, matching `discovery/`.
- **`onboarding/review/` is documentation plus examples only** (PRD5): no assembly code, API, database or
  seed-schema changes; the register is a local artifact and the seed shape is unchanged. The tests check that
  the template and example register parse with every required key and an allowed review state, that every
  label maps to a real CLI enum member (`Client/Enums.cs`), that every evidence and scope reference resolves
  to the manifest, the mapping, the fixtures or the register, that no entry outside force leaves its proposed
  value in `review/fixture/seed/` (the Bronze-to-Gold case), that unknown competitor cells stay unknown, that
  a material change (value, scope, evidence refs) reopens exactly that entry while formatting does not, that
  rule-covered records carry no question, that tier outcomes follow the required entries, and that the
  guide, brief template and all 12 dataset files point at the procedure. Change the register, the review
  walkthrough and the fragments together. The dataset files keep their eight headings; each carries one
  "Consequential choices" note. No ABSTRACT/INDEX/CLAUDE trio inside this directory.
- **`onboarding/commercial/` is documentation plus examples only** (PRD7): no connector, transformation
  code, API, database or seed-schema changes; the orchestrator agent normalizes with its existing source
  access and ad-hoc scripts kept outside the repo. The tests recompute the fixture's arithmetic from the raw
  extract with the documented formulas F1-F10 (term, quantity, currency, ARPU, penetration, fee, price level,
  reconciliation) and compare it with `commercial-normalization.example.json`; they check that every extract
  record is imported, excluded or unresolved exactly once and that `sourceCounts` reconciles, that renewals
  and services never reach dataset 12, that an open deal stays open and a blank loss reason stays
  unresolved, that a churned account is out of the active totals but keeps its ended edge, that the three
  fragments in `commercial/fixture/seed/` parse strictly, bind only extract records and hold no unresolved
  value as a zero, and that every decision key exists in the PRD5 register or the commercial excerpt.
  Change the artifact, the extract, the fragments, the excerpt and the walkthrough together. The artifact's
  stable keys (README section 10) are what PRD8 references. Salesforce field names stay in these artifacts.
- **`onboarding/readiness/` is documentation plus examples only** (PRD8): no code, API or seed-schema changes
  beyond the `priorEvidence` seed key the code half added; the API computes the readiness report, the
  orchestrator agent reconciles it against the sources and keeps the result outside the repo. The tests parse
  the example's `report` into the CLI's own `CalibrationReadinessDto` with unknown members disallowed, pin
  the status, issue-code and holdout vocabularies to the CLI enum mirrors, recompute populations, client-months
  and posteriors from the PRD7 artifact and fragments, check every reconciliation figure (system, source,
  difference, named reasons, per-figure tolerance), that a mismatch keeps its parameters from being presented
  as Ready, that refusals and holdouts never read as a pass, that sensitivity comparisons invent no results,
  that every `pm` verb and option the readiness docs, dataset 08, dataset 12 and the guide's readiness
  subsection name exists in `src/ProductManager.Cli/Commands/`, that every dataset 08 and example-seed engine
  parameter declares valid `priorEvidence`, that line citations into `CalibrationInputBuilder.cs` still point
  at spans, arrivals or exposure, and that the walkthrough's API test names exist. Change the artifact, the
  PRD7 artifact and fragments, the walkthrough and the procedure together; when the readiness contracts
  (`CalibrationReadinessContracts.cs`) change, update the procedure's tables and the CLI mirrors first. Guide
  section 12 has a separate "Model readiness" subsection after the smoke simulation: keep the two apart.
- Dataset files are written for an agent reader: state units only when verified in code, cite
  the file, keep the shared Harborline example world consistent across files, and never invent a
  `pm` verb or option.

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
