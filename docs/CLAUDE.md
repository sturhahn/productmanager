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
- Dataset files are written for an agent reader: state units only when verified in code, cite
  the file, keep the shared Harborline example world consistent across files, and never invent a
  `pm` verb or option.

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
