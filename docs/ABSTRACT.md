Product documentation: the authoritative PRD, PRD follow-ups and their triage, and the agentic
onboarding guide. Read `PRDs/product_requirements.md` before any change to the domain model,
simulation semantics, or phasing; it is the authoritative source, and no other file in this repo
should duplicate its content. `onboarding/` is what a new user hands to an agent to load their
own data into a fresh instance (guide plus one requirements file per dataset), including a
read-only source-discovery step (`onboarding/discovery/`) that assesses connected Aha!, Jira and
Salesforce sources before any data is prepared, and a portfolio-reconciliation step
(`onboarding/portfolio/`) that turns them into one approved, traceable mapping every dataset agent uses, and
a commercial-normalization step (`onboarding/commercial/`) that classifies CRM deals, normalizes contract
amounts and states the calibration population before any commercial row is prepared, and
a consequential-decision review (`onboarding/review/`) that keeps interpretations out of the seed until the
human has accepted them, and a calibration-readiness step (`onboarding/readiness/`) that says whether a
fitted baseline's evidence is sufficient, non-reused and reconciled to the sources before it supports a
roadmap decision.
