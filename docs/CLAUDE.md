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

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
