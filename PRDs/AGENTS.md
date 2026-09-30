# Working in PRDs

- Read `ABSTRACT.md` first; use `INDEX.md` to choose the relevant requirement.
- New follow-up PRDs belong here and use `PRD[n]-FeatureName.md`. Continue the numbering
  across this directory and existing `docs/PRD*.md`; PRD1 and PRD2 already exist under `docs/`.
- Each PRD states its problem, outcome, dependencies, requirements, exclusions, acceptance
  criteria, and implementation/test scope. Draft requirements describe future behavior, not
  capabilities already implemented.
- Refer to `../docs/PRDs/product_requirements.md` for architecture and domain semantics.
  Do not silently change or duplicate that spec. Any required architectural departure must
  be identified and resolved before implementation.
- Prefer the existing CLI/API and agent-readable artifacts. Connector access is an input
  assumption; a requirement to use source evidence does not imply building an integration platform.
- Update this directory's trio, the root index, and the `PRDs` SolutionItems folder when files change.
