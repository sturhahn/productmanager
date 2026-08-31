# seed/ index

- `phase1-seed.json` — roadmap layer: workspace, teams, initiatives, projects, features,
  roadmap, market (segments, products, coverage/needs). Load this first — everything else
  layers onto it by name reference.
- `phase2-seed.json` — market-dynamics layer (`marketDynamics` key): segment stock states,
  covariates (with monthly observations), an assumption set, market events. Depends on
  phase1's market existing.
- `phase3-seed.json` — firm-level layer (`marketDynamics` key): channels, integrations, firms,
  an assumption set, opportunities, churn events, arrival-count facts. The largest file —
  exercises the full bi-temporal facts ledger, including backdated `recordedDate`s. Depends on
  phase1 + phase2.

Load order matters: `pm seed --file seed/phase1-seed.json`, then phase2, then phase3. See
`CLAUDE.md` for the natural-key and backdating semantics, and
`src/ProductManager.Cli/Seed/` for the loader implementation.
