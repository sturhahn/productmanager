# Product Requirements — Roadmap & Market Simulation System

**Status:** Refined 2026-08-29 (grilling session). Supersedes the initial draft.

## Vision

A product management system that handles roadmaps *in their business context*. It starts as
slick, user-friendly roadmap software (initiatives, projects, timelines, capacity) and grows
into a decision-support tool: Monte Carlo simulation of market dynamics, calibrated to
observed behavior, so that roadmap scenarios can be compared on projected bookings, ARR,
and NPV — with full transparency into every assumption.

The system does not make decisions; it shows the tradeoffs inherent in different roadmap
scenarios, lets the user tweak parameters, re-run quickly, and compare against prior runs.
Trust comes from transparency: every number traces to parameters with provenance.

**Posture:** built as if it were a SaaS product (workspace-rooted, API-first), used first as
an internal tool by a single PM. Heavy tenancy machinery (SSO, RBAC, billing) is deferred.

**Industry-agnostic:** the domain model contains no finance-industry concepts. Industry
specifics (jurisdictions, prime brokers, DTCC vs SWIFT vs blockchain settlement, ETF/SMA
shifts) enter as *data* — segments, attributes, channels, approaches — never as schema.

## Architectural Principles

1. **Market side and execution side are separate models, joined at exactly one seam:**
   `Project delivers Feature`. Simulation runs entirely on the market side over Features;
   planning runs on the execution side; the roadmap connects them ("this sequence of
   projects makes these features real on these dates").
2. **Temporal strategy — buy history only where it earns its keep:**
   - **Facts ledger** (bi-temporal, append-only): market observations — client won/lost,
     churn, competitor shipped a feature, pricing changed, incident occurred, covariate
     observed. Each fact has an *effective date* and a *recorded date*, so calibration can
     honestly reconstruct "what we knew as of date D."
   - **Branch/version semantics** for plans: the live roadmap is a living document with
     named versions; a *scenario* is a branch of the roadmap and/or assumption set;
     adopting a simulation result is a merge back to main.
   - **Immutable run artifacts**: simulation and calibration runs capture a full snapshot
     of their inputs (parameters, model version, roadmap branch, covariate scenario, seed)
     and are exactly reproducible and diffable.
   - **Plain CRUD + lightweight audit log** for everything else (teams, descriptions).
3. **API-first:** an OpenAPI-described REST API is the *only* write path. The web app, the
   CLI, and the MCP server are all clients of it. Seed data, transcript-derived updates,
   and automation all arrive through the same door.
4. **Engine as a pure library:** `(WorldSnapshot, AssumptionSet, CovariateScenario,
   EventOverlay, Seed) → Trajectories`. No EF, no database access, no service references.
   Array-oriented (firms × attributes as dense matrices, MC paths batched) over
   BLAS-backed linear algebra. If calibration ever outgrows C#, this boundary is where a
   Python service would slot in.
5. **Distributions from day one:** model parameters carry distributions (normal /
   lognormal / beta — two numbers each; a point value is a zero-width special case). MC
   paths sample parameter uncertainty as well as process noise, so output bands honestly
   reflect how little is known, not just process randomness.

## Domain Model

### Execution side (roadmap & delivery)

| Entity | Purpose | Key content |
|---|---|---|
| **Workspace** | Root aggregate (future tenant boundary) | everything hangs off it |
| **Roadmap** | Branchable, versioned plan | sequences Initiatives; named versions/snapshots; scenarios are branches; merge = adopt |
| **Initiative** | Strategic bet — the *why* | named outcome, target segments, target needs, hypothesized value; contains Projects |
| **Project** | Unit of execution | dates, estimated effort per Team per period, dependencies, status; belongs to an Initiative; *delivers* Features |
| **Team** | Capacity budget unit | capacity per period (FTE-months/points), coarse specialization tag. **No per-person allocation, no skills model** — capacity is a feasibility constraint: a scenario where demand exceeds team capacity in a period is flagged infeasible or auto-slips |

### The seam

| Entity | Purpose | Key content |
|---|---|---|
| **Feature** | Product *capability* (not a work item) | can be hypothetical (unbuilt) or shipped; delivered by Projects. Its market meaning is a set of **deltas**: coverage deltas ("bronze → gold on process P for segment S") and occasionally other deltas (e.g. a data importer = switching-cost delta). "Value of feature F" = diff of simulations with and without F's deltas |

### Market side — structure

| Entity | Purpose | Key content |
|---|---|---|
| **Market** | A competitive arena | contains segments, business-process catalog, products in competition |
| **ClientSegment** | Group of firms with shared profile | needs profile, approach-adoption mix (e.g. 99% DTCC / 1% chain, trendable), opportunity-scoping tendencies (suite vs best-of-breed), consolidation preference. Segment evolution (ETF→SMA shift) = new segment in same market with arrivals routed disproportionately to it |
| **Firm** | Any market participant | prospect, our client, competitor's client, or **synthetic** (flagged; generated to fill the unobserved market). Attributes carry epistemic status: *observed* (databases, CRM, transcripts) or *inferred/latent* (a distribution updated by evidence — e.g. "uses Competitor A" → posterior over needs and price sensitivity). Real firms are ground truth for calibration; simulation is vectorized over firm matrices; cohorts are a computational optimization (rows sharing a posterior), not a domain concept |
| **Firm↔Product relationship** | "Being a client" is an edge, not an entity | provider, modules held, price paid, acquisition date; same edge shape for competitors' client relationships |
| **Product** | A sellable offering, ours or a competitor's | has Features, Coverage assessments, pricing. A *module* is a Product with a parent Product — priced and cross-sold individually, same machinery |
| **Competitor** | Organization owning rival products | strategy notes, event history (incidents, forced migrations, pricing changes) |
| **Integration** | Product↔Product edge | quality score; includes integrations with partial competitors; enters best-of-breed utility against a firm's existing stack |
| **Channel** | Gatekeepers & routes to market | reach into segments, opportunity origination (e.g. prime brokers), per-product affinity/bias (e.g. consultants tilted toward competitors); direct sales is just another channel |

### Market side — demand & supply of capability

| Entity | Purpose | Key content |
|---|---|---|
| **BusinessProcess** | Catalog of what firms must accomplish | hierarchical (process → sub-process), per market; the shared vocabulary demand and supply both point at |
| **Approach** | Optional: alternative *method* for a process | e.g. settle via partner / direct messaging / on-chain; self-serve / managed service / agentic. Nullable dimension — most processes have one implicit approach. Segments carry adoption mixes; a firm's effective coverage = tier weighted by approach mix (prevents "gold on blockchain × 1% adoption" mistakes). A workflow-changing product = us offering a new approach with different cost/efficiency and adoption friction |
| **Need** | Demand edge: (firm-or-segment × BusinessProcess) | importance weight, required depth/scale (quantitative dimensions — e.g. portfolio counts, volumes), conditionality on firm attributes ("operates in X"), hard vs soft, **workaround cost** if unmet (post-processing, point-solution vendor). Regulatory requirements are conditional needs, usually soft-with-workaround-cost, occasionally hard. Transcript evidence attaches here |
| **Coverage** | Supply assessment: (Product × BusinessProcess × Approach? × Segment?) → tier | tier vocabulary: **gold** (market-leading) / **silver** (competitive) / **bronze** (check-the-box, below competitive) / **not playing**; numeric mapping underneath for choice math; capacity/scale limits; effective date; rationale note; **confidence / unknown state** (competitor data is same schema, sparser — unknown ≠ bronze). Assessed at product×process level; feature-level detail is optional evidence underneath |

*(Cut: `Workflow` as an entity — replaced by BusinessProcess hierarchy + Approach. Cut: `Skills`, `KPI` from v1.)*

### Market side — dynamics

| Entity | Purpose | Key content |
|---|---|---|
| **Opportunity** | **The central hinge**: a firm seeking coverage for a *scoped set of BusinessProcesses* | scope (dynamic — can expand mid-cycle: land-and-expand), channel, competitors present, lifecycle (open → qualified → decided: won/lost/no-decision) with stage timestamps. Suite markets = broad scopes; best-of-breed = narrow scopes — a spectrum in data, not market types. Cross-sell/upsell = an opportunity scoped to new processes at an existing client (no special case). Real opportunities are calibration data for cycle lengths and win rates, and the landing zone for transcript extraction |
| **MarketEvent** | Typed shock modulating model terms for a duration | production incident, forced migration, pricing-model change, regulatory change, feature launch. Real events live in the facts ledger; *hypothetical* events can be injected into any scenario → "with/without the incident," "their forced-upgrade window vs our feature date." Own production stability enters here, linking R&D quality spend to churn |
| **Covariate** | Named external time series | e.g. equity index: observed history in the facts ledger; future = scenario path spec (deterministic or simple stochastic process) sampled per MC run. Drives flows like new-firm formation |

### Simulation & calibration

| Entity | Purpose | Key content |
|---|---|---|
| **AssumptionSet** | Versioned, branchable parameter collection | flow rates, utility weights by segment, price sensitivities, switching costs, event effect sizes, approach-adoption trends. Every parameter: distribution + **provenance** (asserted / calibrated / default) + rationale. "What if churn is 2×" = a one-parameter branch |
| **CalibrationRun** | Immutable fit | facts ledger *as of a date* + prior AssumptionSet → fitted AssumptionSet, diffable against the prior |
| **SimulationRun** | Immutable execution | references roadmap branch + AssumptionSet version + covariate scenario + event overlay + seed → trajectories. Reproducible; comparisons are diffs of these references |
| **Scenario** | A branch of roadmap and/or assumptions | compare runs side-by-side; merge accepted changes into the live roadmap |

### The choice model (how a firm decides)

For each Opportunity, among candidate solutions for its scope:

1. **Consideration set** filter (channel reach, segment visibility, hard-constraint
   disqualification).
2. **Utility** = Σ over in-scope processes (importance × effective coverage, approach-mix
   weighted) − price term + soft-constraint workaround costs − switching cost vs incumbent
   + integration quality vs the firm's existing stack + brand/channel bias + noise.
3. **Logit** choice; **sales-cycle delay** per product/segment between open and decision;
   scope-expansion probability driven by coverage advantage on adjacent processes.

Flows that create/destroy relationships: opportunity arrival (new-firm formation is
covariate-driven; conversions = pool × rate-to-market), churn hazard (event-modulated),
cross-sell arrival. All vectorized; firm-level; ~1000 opportunities/year scale.

### Financial model (deliberately simple)

- **Pricing** on Product: recurring price per module (per segment), one-time
  implementation fee, placeholder for usage-based (deferred).
- **Costs** as simple rates: per-client servicing, per-product infrastructure
  (fixed + per-client), implementation cost, R&D drag (support/bugs/incidents — the knob
  linking under-investment in stability to incident-driven churn).
- **Outputs are trajectories, not a number:** bookings (new ARR at signature), recognized
  ARR, gross adds/churn, contribution margin per product/segment; collapsed to NPV with
  explicit discount rate and terminal growth rate; distributions across MC paths
  (P10/P50/P90) including parameter uncertainty.

## Technical Stack

- **Backend:** C# / .NET (latest), EF Core, PostgreSQL. Web API with OpenAPI.
- **Engine:** pure C# class library, Math.NET Numerics + native MKL provider (BLAS),
  array-oriented data layout. Target: hundreds of ms per run; seconds acceptable.
- **Frontend:** Next.js (latest) + TypeScript, shadcn/ui, echarts. Dark mode. Desktop+tablet.
- **CLI:** C# (System.CommandLine) — seed data loading, entity CRUD, run triggering.
- **MCP server:** C# (official MCP SDK) — same API client as the CLI.
- **Auth:** single-user JWT now; real tenancy/SSO deferred.
- The pre-existing scaffold (Startup.cs / WeatherForecast / CRA ClientApp) is discarded.

## Testing Posture

Strict TDD and high coverage on the **engine, calibration, and domain invariants** —
wrong numbers silently corrupt decisions. Conventional integration tests on CRUD/API.
Light touch on UI. (No uniform coverage bar.)

## Phasing

Iterative throughout, driven by a goal framework. Phases 1–3 now; 4 deferred.

1. **Foundation + working roadmap** — full domain schema, API + OpenAPI, CLI with
   seed-data loading, Next.js app: initiative/project/feature CRUD, roadmap timeline with
   drag-and-drop sequencing, team-capacity feasibility warnings. *Adopted for daily use at
   work from here.* Single-user token; no export/collab.
2. **Market model + engine v0** — markets, segments, firms, business processes, the
   gold/silver/bronze coverage grid (useful as a static strategy view before any
   simulation), needs, competitors, products. Engine v0 = aggregate stock-and-flow per
   segment with distributions and covariates — validates the whole loop (assumptions → run
   → trajectories → compare two roadmap scenarios) with 10% of the machinery. Scenario
   branching + run-comparison UI.
3. **Firm-level choice engine** — opportunities, utilities, consideration sets, switching
   costs, channels, integrations, events, calibration runs against the accumulating facts
   ledger.
4. **Intelligence layer (deferred)** — sales-call transcript ingestion (extract needs,
   business-process nuances, microsegmentation, resonating features, competitive intel),
   competitor likely-moves analysis mode, pricing/usage simulation, export & multi-user
   polish.

## Explicitly Deferred / Cut

- Per-person resource allocation, skills & proficiency model, skill-gap analysis
- Real-time collaboration indicators; PDF/Excel export
- Multi-tenancy machinery (SSO, RBAC, billing); generic KPI entity
- Usage statistics & pricing-model simulation (future release)
- Competitor participation in Monte Carlo (they are static/event-driven; a separate
  "likely moves" analysis mode comes in Phase 4)
