# Onboarding PRD index

All six PRDs are drafts dated 2026-09-30. They extend [PRD2](../docs/PRD2-Onboarding.md)
and preserve the [authoritative product spec](../docs/PRDs/product_requirements.md).

| PRD | Outcome | Dependencies |
|---|---|---|
| [PRD3-SourceDiscovery.md](PRD3-SourceDiscovery.md) | Inspect source availability and agree scope before interviewing or importing. | Existing onboarding guide |
| [PRD4-PortfolioReconciliation.md](PRD4-PortfolioReconciliation.md) | Select a coherent portfolio from noisy Aha! and Jira records. | PRD3 |
| [PRD5-EvidenceReview.md](PRD5-EvidenceReview.md) | Review consequential mappings and assumptions before loading them. | PRD3; PRD4 for portfolio evidence |
| [PRD6-ReliableSourceImports.md](PRD6-ReliableSourceImports.md) | Preserve source identity, handle lifecycle updates, and resume imports safely. | Existing seed/API contracts; consumes PRD3 source identities |
| [PRD7-CommercialNormalization.md](PRD7-CommercialNormalization.md) | Convert Salesforce evidence into correctly classified deals, relationships, and economics. | PRD3, PRD5; PRD6 for repeatable application |
| [PRD8-CalibrationReadiness.md](PRD8-CalibrationReadiness.md) | Reconcile inputs and prevent misleading calibration baselines. | PRD5, PRD6, PRD7; PRD4 for roadmap comparison |

Recommended delivery: PRD3 → PRD4 → PRD5 → PRD7 → PRD8, with PRD6 implemented before
repeatable source loading. Discovery and portfolio review are useful before any import changes.

- `ABSTRACT.md` — directory purpose and relationship to the authoritative spec.
- `AGENTS.md` — numbering, requirement structure, and editing conventions.
- `INDEX.md` — deliverables, dependencies, and recommended sequence.
