Product documentation: the authoritative PRD, PRD follow-ups and their triage, and the agentic
onboarding guide. Read `PRDs/product_requirements.md` before any change to the domain model,
simulation semantics, or phasing; it is the authoritative source, and no other file in this repo
should duplicate its content. `onboarding/` is what a new user hands to an agent to load their
own data into a fresh instance (guide plus one requirements file per dataset), including a
read-only source-discovery step (`onboarding/discovery/`) that assesses connected Aha!, Jira and
Salesforce sources before any data is prepared.
