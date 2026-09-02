# PRD1 Initial Feedback — Triage

This document triages `docs/PRD1-InitialFeedback.md` against the current codebase. Eight
investigation passes mapped every feedback item to the relevant domain, engine, API, and
frontend code; this document merges those findings into one triage. It was written as a
proposal. On 2026-08-31, every open fork below was resolved in a design review with Chris; the
**Decisions (2026-08-31)** section below is authoritative over any conflicting wording in the
per-area proposals that follow. Written 2026-08-31.

Each item below carries a short id (e.g. `CH1`, `RM3`) used for cross-reference in the decisions
and sequencing sections.

## Summary table

| Id | Area | Item | Kind | Effort | Recommendation |
|----|------|------|------|--------|-----------------|
| CH1 | Channels | Show stats: opportunities generated, monthly walk, cycle time | feature | L | Add a client-side "implied opportunities/yr" preview from calibrated data; promote existing `ChannelFunnel` onto the Channels page; treat per-channel cycle time as a real domain gap needing a schema decision. |
| CH2 | Channels | Channel weight units aren't intuitive; show sensitivity as implied opportunities/yr | feature | M | Same preview as CH1 applied to reach × originationShare; add a win-probability-delta readout for product bias. |
| RM1 | Roadmap | First column (lane label) can't be resized, too narrow | quick-win | S | Add a title tooltip now; make the column drag-resizable with a localStorage-persisted width. |
| RM2 | Roadmap | Bar click should open detail; drag starts too eagerly; [x] too easy to hit | feature | M | Add a pixel-deadzone click/drag disambiguation; open a detail view as a modal (extractable body) on click; the `[x]` unplan control stays exactly as-is — no undo toast, no confirm dialog. |
| RM3 | Roadmap | Mixed granularity across the visible date range (weekly/monthly/quarterly) | feature | XL | Decided: new persisted `RoadmapView` entity per Roadmap (one marked default), granularity-only config in v1; bands are rolling + calendar-aligned, stored as data, with Chris's scheme hardcoded as the default and no band-editor UI in v1. |
| RM4 | Roadmap | Seed data to exercise mixed-granularity view | seed-data | S | Blocked on RM3's storage decision; add a relative-to-today seed example once settled. |
| UI1 | UI/Layout | Wide table rows: action buttons misalign with rows on laptop widths | feature | M | Extract a shared `TableRowActions` pattern (sticky column + hover-reveal + kebab for 3+ actions) and apply across the six affected pages. |
| UI2 | UI/Layout | Competitor products should live on a per-competitor page; Products page confined to our own products | feature | M | Extract the shared product-list rendering, add `/competitors/[id]`, trim Products page to "our products" only. No backend change needed. |
| FM1 | Firms | Tech landscape should include partner/unreplaced systems, not just ours | quick-win | S | Already modeled and already shown; add an "ours"/competitor badge and seed 2-3 mixed-landscape firms. |
| FM2 | Firms | Evaluate switching cost vs. incumbent specifically, compare features vs. all competitors | feature | M | Engine already does this correctly for simulated opportunities; add an explicit `IncumbentProductId` to the real (CRM) `Opportunity` and matching UI. |
| FM3 | Firms | Clicking a Firm should open a CRM-like record | feature | M | Add `/firms/[id]` read view assembling firm, relationships, opportunity history, and an explicitly-labeled revenue estimate; no "usage" concept exists, scope it out or treat separately. |
| FM4 | Firms | Firm list should be filterable by client/prospect/systems-in-use presets | feature | M | Add server-side `isClient` and `hasRelationshipToProductIds` filters to `FirmsController.List`; "prospect" is just `isClient == false`. |
| PL1 | Pipeline | Filter the Decided column by date | feature | S | Client-side date-range filter on the already-fetched opportunity list; no backend change needed unless CLI/API parity is wanted. |
| PL2 | Pipeline | Seed data should include Open/Qualified opportunities, not just Decided | seed-data | S | All 32 seeded opportunities are currently decided; add entries with null `decidedDate`/`outcome`, some with `qualifiedDate` set and some not. |
| PL3 | Pipeline | Clicking an opportunity should open a CRM-like detail record (rep, probability, value, needs, competition) | feature | L | Detail view is buildable from existing data now; "sales rep," "probability," and "value" are new domain concepts needing a product decision before implementation. |
| CB1 | Calibration | Raw mu/sigma/alpha/beta shown with no units or explanation | feature | M | Add a natural-units display/edit layer (mean ± spread, converted to/from native params at the API boundary) mirroring the pattern the Calibration diff view already uses; keep an advanced raw-params toggle. |
| CB2 | Calibration | BUG: Base UI console warning on the calibration page (`nativeButton`) | bug | S | Add `nativeButton={false}` to the `<Button render={<Link .../>}>` at `calibration/page.tsx:464`. |
| RN1 | Runs | Run identity shows truncated GUID instead of name/timestamp | bug | S | Change `runLabel()`'s fallback in `simulate/page.tsx:93-95` to format `run.createdAt` instead of slicing the id. |
| RN2 | Runs | Fractional opportunity counts (e.g. "0.1 won") read as nonsensical | quick-win | S | These are correct MC-path means; make them visually distinct from literal counts (e.g. "~0.1" or muted styling); consider a "% of paths" framing for low-volume cases as a follow-up. |
| RN3 | Runs | Configurable segmentation/breakout of run results with pie/donut visuals | feature | XL | Stored trajectories carry no product axis and only firm-level runs carry channel; needs an axis-scope decision before any additive engine change. |
| SD1 | Seed/Initiatives | Add a direct sales channel; decide on origination sub-forms | seed-data | S | `Channel` already supports this as data — add named channel rows (reverse inquiry, outbound, campaigns) rather than a new schema concept. |
| SD2 | Seed/Initiatives | FIX Connectivity Upgrade should be flagged as "table stakes"; domain has no concept for it | design-question | S | Decided: add a queryable `StrategicIntent` tag (`Differentiation \| Parity \| Retention`, nullable) on `Initiative`, not just narrative; FIX Connectivity Upgrade tagged Parity in seed. No simulation pathway. |
| SD3 | Seed/Initiatives | Initiatives should have measurable KPIs, some linking into the financial simulation | feature | M | Add a general `InitiativeKpi` collection (execution-side only); any simulation-linked KPI must route through the existing `FeatureDelta` seam, not become a second execution→market path. |

## Quick wins & bugs

These are the S-effort items plus the calibration Base UI bug — small, mostly independent, and
suitable for one batch of work.

- **CB2 — BUG: Base UI console warning on the calibration page.** `frontend/src/app/(app)/calibration/page.tsx:463-474` renders `<Button variant="outline" size="sm" render={<Link href={...} />}>`. `Button` forwards to base-ui's `ButtonPrimitive`, whose `nativeButton` prop defaults to `true`; rendering it as an anchor via `Link` (not a `<button>`) triggers the mismatch warning. **Exact fix:** add `nativeButton={false}` to that call — `<Button variant="outline" size="sm" nativeButton={false} render={<Link href={...} />}>`. This is the only spot in the codebase where `Button` is given a `render` prop pointing at a non-button element; no other usage is affected.
- **RN1 — Run identity shows a truncated GUID.** `simulate/page.tsx:93-95`'s `runLabel()` fallback is `Run ${run.id.slice(0, 8)}`. Fix: fall back to a formatted `run.createdAt` (already fetched and used elsewhere on the same page) instead of slicing the id.
- **RN2 — Fractional opportunity counts read as nonsensical.** These are correct Monte Carlo path means (`FirmLevelEngine.cs:129-148`); the fix is presentational — visually distinguish means from literal counts (e.g. "~0.1" or muted styling), no engine change.
- **RM1 — Roadmap label column too narrow.** Add a `title` tooltip to the label span immediately; the drag-resize + localStorage persistence is a slightly larger follow-up within the same S estimate.
- **FM1 — Tech landscape already supports partner/legacy systems.** Add an "ours"/competitor badge to the relationship list and 2-3 seed firms demonstrating a mixed landscape.
- **PL1 — Filter the Decided column by date.** Pure client-side filter on already-fetched data; add `filterByDecidedRange()` to `pipeline.ts` with a colocated test.
- **PL2 — Seed Open/Qualified opportunities.** All 32 seeded opportunities currently have a `decidedDate`; add entries with `decidedDate: null`.
- **SD1 — Add a direct sales channel.** `Channel` already models this as data; add named rows (e.g. "Inside Sales – Reverse Inquiry") to `seed/phase3-seed.json`.
- **SD2 — Flag "table stakes" initiatives.** Expressible today via `FeatureDelta.Rationale` and `Initiative.Outcome`/`HypothesizedValue` narrative text — zero code change.
- **RM4 — Seed data for mixed granularity.** Blocked on RM3's storage decision (see below); don't scope independently until that's settled.

## Channels

### CH1 — Show stats on the channel (opportunities generated, monthly walk, cycle time)

**Current state:** Channel weight (`src/ProductManager.Domain/Market/Channel.cs:53-61`) is a per-(channel, segment) `reach × originationShare` pair. The engine (`src/ProductManager.Engine/FirmLevelEngine.cs:310-339`) uses this only to route already-generated opportunities to a channel — it has zero effect on total opportunity volume, which is driven separately by segment-level arrival/conversion parameters (`FirmLevelContracts.cs:231-232`) that channels don't reference. The engine does compute a per-run `ChannelFunnel(ChannelId, Opened, Won, WinRate)` (`FirmLevelContracts.cs:275`, populated `FirmLevelEngine.cs:142-148`), exposed as `EngineChannelFunnel` (`frontend/src/lib/api/types.ts:1169`) and rendered only on the Simulate page, never the Channels page, and only as a single run-total, never a monthly walk. There is no per-channel cycle-time concept: `SalesCycleSpec` (`FirmLevelContracts.cs:188`) is keyed by (segment, product), and the only cycle-time diagnostic is a single run-wide average (`avgSalesCycleMonths`, `funnel.ts:138`).

**Proposal:** Three separable pieces. (1) A lightweight, non-Engine "implied opportunities/yr" preview computed from calibrated segment arrival/conversion means and the channel's current reach × originationShare, surfaced directly on the Channels page next to the reach/origination inputs — following the pattern of `lib/coverage.ts`/`lib/masking.ts` mirroring engine semantics client-side. (2) Promote the existing `ChannelFunnel`/`EngineChannelFunnel` onto the Channels page detail view (read from the most recent run), and extend diagnostics to bucket `Opened` by month as well as by channel for the monthly walk — an aggregation change over the existing per-path export (`FirmLevelEngine.cs` ~1067-1150), not new sampling. (3) Per-channel cycle time is a real schema gap: `SalesCycleSpec` would need an optional `ChannelId` dimension, plus a new per-channel win-to-close metric in diagnostics.

**Risks:** The "implied opportunities/yr" preview is an approximation of an MC process with a point estimate — could mislead if presented without a range/uncertainty band. The monthly-walk ask is ambiguous between a pre-run projection and a post-run actual, which are very different builds.

**Effort:** L.

### CH2 — Channel weight units aren't intuitive; show sensitivity as implied opportunities/yr

**Current state:** `reach` and `originationShare` are unitless [0,1] fractions (`Channel.cs:53-61`). The frontend (`channels/page.tsx:242-249`) gives a one-line formula but no worked example and doesn't correct the natural misreading that a higher `originationShare` means more total opportunities rather than a larger share of a fixed pool. Product bias (`ChannelProductAffinity.Bias`, `Channel.cs:71-77`) is an unbounded signed logit-utility nudge with a UI slider clamped to [-2, 2] (`page.tsx:514`) and no explanation of its effect on win probability.

**Proposal:** Same underlying fix as CH1's preview, applied to both fields. (a) Add the derived "implied opportunities/yr" readout for reach × originationShare — it replaces two abstract fractions with one concrete quantity and, since the total across channels stays constant as one slider moves, self-teaches the "routing share of a fixed pool" semantics. (b) For product bias, show a win-probability delta at a reference utility gap, computed as a closed-form softmax/logit calculation mirroring the engine's existing choice-probability math (not a full simulation). Both readouts should update live as the input changes, not require a save+re-run.

**Risks:** Needs a decision on whether these previews reflect the calibrated baseline only or the currently-active covariate/event scenario (the latter ties the Channels page to run state it doesn't depend on today). Should be clearly labeled as an estimate to avoid being mistaken for actual simulated output.

**Effort:** M.

## Roadmap

### RM1 — First column (lane label) can't be resized and is too narrow

**Current state:** `frontend/src/components/roadmap/timeline.tsx` hardcodes `LABEL_W = 208` (line 30), used for the header spacer (line 332), each lane's label div (line 432), and the total scrollable width calc (line 322). The label is `<span className="truncate ...">` (lines 450-452) with no tooltip on the label itself (only the bar has a `title` attr, line 470). No drag handle, no persisted width anywhere.

**Proposal:** (1) Quick win — add a `title={lane.initiative?.name ?? "No initiative"}` tooltip and bump the default width modestly (208→256). (2) Real fix — add a drag handle on the column's right edge, track width in component state, clamp to a min/max (e.g. 140-420px), and persist per-browser via `localStorage` — a per-viewer UI convenience, no API/domain change needed.

**Risks:** None significant; low-risk, additive change.

**Effort:** S.

### RM2 — Bar click should open detail; drag starts too eagerly; [x] too easy to hit

**Current state:** `timeline.tsx:466-491` — the bar's `onPointerDown` immediately calls `startBarDrag` (lines 198-219), which enters drag mode on the very first pointerdown; there is no click affordance today, only drag. The `[x]` unplan button (lines 499-510) is a 12px icon-only control requiring no confirmation, calling `onUnplan` → `Roadmap.UnplanProject` (`src/ProductManager.Domain/Execution/Roadmap.cs:59`), which `RemoveAll`s the `RoadmapItem` plan link only — **it does not delete the Project or Initiative**; the item returns to the Unplanned tray and can be re-planned. The feedback's core complaint (too easy to trigger accidentally) still holds regardless of what the button actually does.

**Proposal:** (a) Add a pixel-deadzone (e.g. 4-6px) click-vs-drag disambiguation: track a "pending" pointerdown state and only commit to drag once movement exceeds the threshold; pointerup under threshold fires a new `onViewItem(projectId)` callback instead. Resize handles keep their existing immediate-capture behavior since they're narrow, purpose-built strips. (b) **Decided:** open a detail view as a MODAL via `onViewItem`, showing project, initiative, editable planned dates, team/capacity, and violations — built as an extractable body component so it can be dropped into a future `/projects/[id]` route without a rewrite. (c) **Decided:** the `[x]` unplan control stays exactly as-is — no undo toast, no confirm dialog. It only returns the item to the Unplanned tray, which is reversible enough as-is.

**Risks:** None outstanding — see Decisions (2026-08-31) below (RM2, RM3-c).

**Effort:** M.

### RM3 — Mixed granularity across the visible date range (weekly/monthly/quarterly)

**Current state:** The timeline has no granularity concept today — every day renders at a fixed `DAY_PX = 4` (`frontend/src/lib/timeline.ts:7`); `monthCells`/`quarterCells` (lines 113-149) are display-label groupings over a uniform pixel-per-day scale. There is no `RoadmapView` or per-roadmap display-settings entity anywhere in the domain (confirmed via full-repo grep — no hits for `RoadmapView`/`granularity`). This is greenfield, not a bug fix.

**Proposal:** Granularity is a display concern and must stay on the execution/planning side, out of the Engine. Two viable placements were proposed: (A) frontend-only — a granularity config stored in `localStorage` per roadmap id, zero backend change. (B) persisted per-Roadmap — a `DisplayGranularity` value object on `Roadmap` (or a 1:1 `RoadmapViewSettings`) plus API fields, needed if the choice should survive across devices/users. **Decided: (B).** A new persisted `RoadmapView` entity holds multiple named views per `Roadmap`, one marked default; v1 config is granularity bands ONLY (no filters, no lane config). Bands are rolling (resolved against today at render time) and calendar-aligned (edges snap to week/month/quarter boundaries), stored as data on the view, with Chris's scheme (rest of current month + next full month weekly; rest of quarter monthly; quarterly beyond) hardcoded as the default and no band-editor UI in v1. Regardless of storage, the timeline math needs a segment-aware scale replacing the uniform `dateToX`/`daysBetween*DAY_PX` calc. **Decided:** drag/resize stays day-precise everywhere with NO display-grid snapping — pointer position maps to dates through the piecewise scale (position→date, not delta-px→delta-days); the RM2 modal's typed dates are the precision path for far-zone items.

**Risks:** None outstanding — see Decisions (2026-08-31) below (RM3-a, RM3-b, RM3-c). Still the largest-effort item in the roadmap area; drag-across-boundary behavior remains the trickiest part of implementation even though the design fork is resolved.

**Effort:** XL.

### RM4 — Seed data should exercise the mixed-granularity view

**Current state:** `seed/phase1-seed.json` has no roadmap-view/granularity concept to seed, since none exists yet (see RM3).

**Proposal:** Once RM3's storage location is decided, add the corresponding seed entry (JSON block if persisted server-side, or a documented default in seed's `CLAUDE.md` if frontend-only) demonstrating the PM's exact scheme, anchored relative to seed-load date (computed from `DateTime.UtcNow`) rather than hardcoded dates, so it stays "remainder of this month" regardless of when seed is run.

**Risks:** Fully blocked on RM3; don't scope independently.

**Effort:** S.

## UI and layout

### UI1 — Wide table rows: action buttons misalign with rows on laptop widths

**Current state:** `Table`/`TableRow`/`TableCell` (`frontend/src/components/ui/table.tsx`) already have a row hover highlight and `whitespace-nowrap` cells, but every page with a data table puts action buttons in a plain trailing column with no sticky/pinned positioning: `features/page.tsx:159,251`; `projects/page.tsx:136,160`; `initiatives/page.tsx:98,123`; `teams/page.tsx:98,117`; `firms/page.tsx:265,306`; `competitors/page.tsx:105,118`. On wider rows (e.g. features, with name/description, status badge, project badges, delta-count tooltip) this pushes actions far right of identifying content, worsened by the table's `overflow-x-auto` wrapper. By contrast, the Products page already solves this with a flex `<ul>`/`<li>` layout and hover-reveal actions at the end of each row's flex container (`products/page.tsx:120-208`).

**Proposal:** Establish one shared row-actions pattern (a `TableRowActions` component colocated with `table.tsx`) and apply to all six pages: (1) sticky/pinned action column (`position: sticky; right: 0`, solid background, subtle border) so actions stay anchored regardless of horizontal scroll; (2) hover-reveal on non-touch viewports, with an always-visible fallback for coarse-pointer devices via `@media (hover: none)`; (3) collapse to a single kebab (`DropdownMenu`) when a row has 3+ actions (features, firms); (4) keep the existing row hover highlight, optionally add zebra striping on the widest tables. Do not add row-click-opens-detail on top of this — no detail routes exist yet for these entities and it would conflict with sticky actions.

**Risks:** Needs a decision on whether the action column should actually be sticky (stronger fix, more chrome) vs. just tighter column widths, and whether touch/tablet is a real target.

**Effort:** M.

### UI2 — Competitor products should live on a per-competitor page; Products page confined to our own products

**Current state:** Ownership is already data, not schema: `ProductDto.competitorId` is nullable (null = ours). `products/page.tsx` renders two sections today — "Our products" (`isOurs` filter, line 105) and, below it, a per-competitor "Competitor products" block with its own list and Add-product button (lines 259-305). The shared row-rendering logic (`renderRows`, lines 120-209) is a local closure, not an extracted component, so it can't be reused from another route. `competitors/page.tsx` shows only a product-count badge per competitor (lines 58-59, 113) with no way to see or manage that competitor's products from there.

**Proposal:** Extract `renderRows` and its dialogs into a shared component (e.g. `frontend/src/components/product-list.tsx`) taking `products`, `competitors`, and `ownerCompetitorId` as props; add `frontend/src/app/(app)/competitors/[id]/page.tsx` rendering it scoped to one competitor; make competitor rows/badges link to that route; trim `products/page.tsx` to "our products" only, deleting the competitor block and `byCompetitor` derivation (lines 106-109), and prevent `New product` on this page from ever setting a `competitorId`. No backend/API or domain-model change is needed.

**Risks:** Needs a decision on whether competitor products keep full editing parity (Pricing/Integrations dialogs) or get a lighter view, and whether strategy notes move onto the new detail page.

**Effort:** M.

## Firms

### FM1 — Client firms' tech landscape should include partner/unreplaced systems, not just ours

**Current state:** This already works as designed. `FirmProductRelationship` (`src/ProductManager.Domain/Market/Product.cs:97-176`) is a generic firm↔product edge with no ours-only restriction — `Product.IsOurs` is just `CompetitorId is null`. Neither `FirmsController`/`FirmProductRelationshipsController` nor the frontend's "Incumbent products" column (`firms/page.tsx:270-296`) or `RelationshipsSection` (`page.tsx:583-714`) filter to our products; every active relationship for a firm is already shown.

**Proposal:** No schema or engine change needed. The real gap is data entry and labeling: (a) add a small badge distinguishing "ours" vs. competitor/partner rows (sourced from `Product.IsOurs`, already fetched); (b) no seed data currently demonstrates a mixed landscape — add 2-3 such firms to `seed/phase3-seed.json`.

**Risks:** Confirm with the PM this is a labeling/seed ask, not a modeling gap, before doing any engine work. If a distinct "partner" concept beyond "non-ours relationship" is wanted (e.g. a product we resell/OEM), that's a new attribute, not just a label.

**Effort:** S.

### FM2 — Evaluate switching cost vs. the incumbent specifically; compare features vs. all competitors in the deal

**Current state:** The choice engine already implements this asymmetry, but only inside the simulation, never persisted or exposed. `FirmLevelEngine.OpenOpportunity` (`src/ProductManager.Engine/FirmLevelEngine.cs:1308`, called from line 1175) derives an incumbent at simulation time and applies `SwitchingCostBase` as a penalty only to non-incumbent candidates (line 1371), while feature/coverage utility is computed against every candidate in the choice set — matching the PM's ask exactly, but only for the simulated flow. The real (CRM) `Opportunity` (`src/ProductManager.Domain/Market/Dynamics.cs:10-115`) has `CompetitorIds` and `WinningProductId` but no `IncumbentProductId` field, and neither `OpportunitiesController` nor `opportunities/page.tsx` (lines 246-249, 617-635) let a user mark the incumbent.

**Proposal:** Add `Guid? IncumbentProductId` (or `IncumbentFirmProductRelationshipId`, for traceability to price/served-process data) on `Opportunity`, settable via a new domain method, exposed through the DTO/request contracts. Default it to the firm's sole active relationship when exactly one exists; require a pick otherwise. On the frontend, replace/augment the competitor-org multiselect with an "Incumbent system" single-select plus a product-level "Competing systems" multiselect. This is display/tracking only — it does not feed back into `FirmLevelEngine`, which already derives its own incumbent independently.

**Risks:** Needs a decision on whether this hand-entered incumbent should ever feed calibration (ground truth for `SwitchingCostBase`) or stay purely informational — the former needs a Facts-ledger event too.

**Effort:** M.

### FM3 — Clicking a Firm should open a CRM-like record (landscape, opportunity history, revenue/usage)

**Current state:** No firm detail page exists — `firms/page.tsx` is a paginated list plus an edit-form dialog (`FirmEditorDialog`, lines 428-579) with no opportunity history or revenue/usage rollup. Most raw material already exists via the API: `GET /api/firms/{id}`, `GET /api/firm-product-relationships?firmId=` (with `PricePaidPerPeriod`/`PriceLevel`/`AcquisitionDate`), and `OpportunitiesController`. No "usage" concept exists anywhere in the domain (no telemetry entity); "revenue" would have to be derived client-side, with no rollup endpoint.

**Proposal:** Add `frontend/src/app/(app)/firms/[id]/page.tsx` as a read-focused page with: header, "Technology landscape" (the existing relationship list, extracted into a shared component), "Opportunity history" (Opportunities filtered by `firmId` — confirm/add server-side filtering), and "Revenue" (sum of `PricePaidPerPeriod × PriceLevel` across active relationships, explicitly labeled as an estimate, not a booked-revenue ledger). Do not build a "usage" section — no usage data source exists in the domain; scope it out or treat as a separate feature request. Make firm rows/names clickable links to the new route.

**Risks:** "Usage" and "revenue" are both genuinely undefined in the domain today — needs a decision on whether usage means real telemetry (new ingestion path) or a proxy (modules held / served-process count), and whether revenue means list-adjusted MRR (available today) or historical booked revenue (would need a ledger).

**Effort:** M.

### FM4 — Firm list should be filterable by presets: clients, prospects, systems in use (multiselect)

**Current state:** The Firms page filters only by Segment and EpistemicStatus today (`firms/page.tsx:92-99, 195-233`), passed to `GET /api/firms`'s `clientSegmentId`/`epistemicStatus` params (`FirmsController.List`). "Client" isn't a stored boolean — it's implicitly "has an active relationship to one of our products," already computed client-side per row (`useRelationshipsForFirms`, lines 105-109, 267) but not used for filtering. "Systems in use" has no query-param support on the Firms endpoint at all.

**Proposal:** Add server-side filters to `FirmsController.List` so filtering doesn't break pagination: `isClient: bool?` (EXISTS an active relationship to an our-product) and `hasRelationshipToProductIds: Guid[]?` (multiselect, EXISTS an active relationship to any given product id). "Prospect" is just `isClient == false` — no new flag needed. Add a segmented All/Clients/Prospects toggle and a "Systems in use" multiselect to the frontend filter row, wired into the existing query params.

**Risks:** Needs a decision on whether "client" means "active relationship to one of our products" specifically vs. any active relationship at all (a firm using only a competitor's product would otherwise read as a "client"), and whether the systems-in-use filter is OR or AND across selected products.

**Effort:** M.

## Pipeline

### PL1 — Filter the Decided column by date

**Current state:** The Pipeline page (`opportunities/page.tsx`) renders three static columns built by `groupOpportunities` (`frontend/src/lib/pipeline.ts:20-50`), which sorts decided items by `decidedAt` but applies no filtering. `OpportunitiesController.List` already accepts `firmId`/`channelId`/`stage`/`isCrossSell` filters but no date-range param. No date picker exists on the page today.

**Proposal:** A client-side filter, not a new API param: the opportunities query already returns the full set. Add a date-range control and a small `filterByDecidedRange(items, from, to)` helper in `pipeline.ts` with a colocated test, applied before rendering the Decided column. No backend change needed unless the PM wants server-side date filtering for the CLI/future MCP client too.

**Risks:** Needs a decision on whether the filter should apply only to Decided or become a global pipeline date filter (e.g. also affecting Open/Qualified via `openedAt`).

**Effort:** S.

### PL2 — Seed data should include Open and Qualified opportunities, not just Decided

**Current state:** `seed/phase3-seed.json`'s 32 seeded opportunities all carry a non-null `decidedDate` and `outcome` (13 Won / 12 Lost / 7 NoDecision, 0 null) — verified by parsing the file. Open and Qualified columns are always empty on a freshly seeded instance.

**Proposal:** Add new entries with `decidedDate: null`/`outcome: null` — Decide is already a separate POST from Create (`OpportunitiesController.cs:56-88` vs. 175-242), so this is legitimate today. Include some with `qualifiedDate` set (Qualified) and some without (Open), across a spread of `openedDate`s so the pipeline's age/cycle-status coloring has visible variety. Confirm `SeedLoader.cs` handles a null `decidedDate`/`outcome` cleanly before adding entries.

**Risks:** None significant.

**Effort:** S.

### PL3 — Clicking an opportunity should open a CRM-like detail record (rep, probability, value, competition, needs)

**Current state:** Opportunity cards on the Pipeline page are not clickable today; the Edit dialog (`page.tsx:676-906`) shows channel, cross-sell module, competitors, notes, and scope, but not firm details inline, stage history, computed probability, deal value, or needs. `Opportunity` (`src/ProductManager.Domain/Market/Dynamics.cs:10-115`) has no `SalesRepId`/owner and no `Probability`/`Value` field. "Needs identified" isn't opportunity-owned — `Need` (`DemandSupply.cs:74`) is a (firm-or-segment × BusinessProcess) edge independent of any opportunity.

**Proposal:** Split into a detail view (buildable now from existing data: firm, stage timeline, competitors, scope, and a "Needs for this deal" section computed client-side by filtering the market's Needs on firm + scope) and new fields (a genuine product decision before implementation). "Sales rep" is a straightforward additive nullable field, though Phase 1 has no user-account model to reference. "Probability" is architecturally trickier: the system's real win probability is a simulation output, not a manually-entered percentage — recommend surfacing the engine's implied win probability rather than stapling on a stored field, unless the PM explicitly wants a distinct, clearly-labeled "rep estimate." "Value"/deal size has no domain home today and needs the PM to define its meaning before it's added.

**Risks:** The probability and value questions are the crux — building the wrong one duplicates or contradicts what the market model already computes, or under-builds what the PM actually wants tied into bookings/ARR.

**Effort:** L.

## Calibration

### CB1 — Raw statistical parameterization shown with no units or explanation

**Current state:** Two places disagree today. `assumptions/page.tsx:76-81` (`DISTRIBUTION_PARAM_LABELS`) is where priors are edited: raw "mu (log)"/"sigma (log)" for Lognormal, "alpha"/"beta" for Beta, PATCHed straight through with no unit conversion (`ParameterRow`, lines 441-625). `calibration/page.tsx`'s diff view is already unit-friendlier — it shows fitted-parameter mean ± stddev via `ParameterDiff`'s `PriorMean/PriorStdDev/PosteriorMean/PosteriorStdDev` (`CalibrationContracts.cs:122-128`, moment-matched in `CalibrationEngine.Moments()`). On engine semantics: `NewFirmArrival`'s natural unit is firms/month (`ArrivalCount` doc comment, `CalibrationContracts.cs:66-70`); `WinRate` is `Beta(a,b)` with mean `= a/(a+b) = P(win | decided)` — the UI shows two bare integers with no implied-win-rate readout anywhere.

**Proposal:** Keep storage in the engine's native parameterization (`DistributionSpec{type,p1,p2}`, since `CalibrationEngine.TryBetaPrior`/`TryGammaPrior` moment-match against it). Add a display/edit-in-natural-units layer per `FlowKind` (e.g. "typical rate: 2.3 firms/month, ± 0.4"), converting to/from mu/sigma or alpha/beta at the API-call boundary — porting the read-direction of the moment-matching math already in `CalibrationEngine`. This mirrors the pattern the calibration diff view already proves. Keep an "advanced" toggle showing raw params for power users. Add a one-line "what this measures" string per `FlowKind` alongside (not replacing) the existing rationale tooltip. `NewFirmArrival`'s missing time unit is a pure label fix ("per month") once the unit table exists — the engine already fixes exposure at 1.0/month.

**Risks:** Needs a decision on whether the natural-unit editor replaces the raw inputs by default (with an advanced escape hatch) or coexists as a toggle, and what the two natural read-outs for Beta parameters (mean+spread vs. mean+implied-sample-size) should be — these have different round-trip tradeoffs through moment-matching.

**Effort:** M.

### CB2 — BUG: Base UI console warning on the calibration page

See Quick wins & bugs above for the exact fix (`nativeButton={false}` at `calibration/page.tsx:464`).

**Effort:** S.

## Runs

### RN1 — Run identity shows a truncated GUID instead of name/timestamp

**Current state:** `simulate/page.tsx:93-95`, `runLabel()`: `return run.name || \`Run ${run.id.slice(0, 8)}\`;`. Used in the runs table, Compare-view pickers, and the results header. A named run already displays correctly — the bug is only the fallback for unnamed runs. `SimulationRunDto.createdAt` is already fetched and used elsewhere on the same page (line 259).

**Proposal:** Change the fallback branch to format `run.createdAt` instead of slicing the id. No backend change needed; could move to `lib/runs.ts` with a colocated test if unit coverage is wanted.

**Risks:** None significant — cosmetic decision only (absolute vs. relative timestamp format).

**Effort:** S.

### RN2 — Fractional opportunity counts read as nonsensical

**Current state:** Confirmed correct: these are means across Monte Carlo paths, not fractional entities. `FirmLevelEngine.cs:129-148` accumulates integer per-path counts and divides by `paths`, producing `double` fields by design (`FirmLevelContracts.cs:262-275`). The frontend already partially discloses this — `lib/funnel.ts`'s `formatMeanCount()` rounds to one decimal with a doc comment "no false precision," and the diagnostics panel already captions "mean across paths." The gap is that a raw "0.1" still reads as an absurd literal count when skimmed.

**Proposal:** (1) Make mean-count values visually distinct from literal counts (e.g. "~0.1", muted styling, or a "/ path" unit suffix) — pure presentation, no engine change. (2) As a follow-up: for genuinely small means, consider surfacing "opened in 8% of paths" instead, which needs an additive `pathsWithAtLeastOne` counter in the per-path accumulation and a new DTO field (doesn't violate the frozen `EngineResult` shape, since it's additive).

**Risks:** None for (1). (2) is a bigger lift and should only be pursued if the label change alone doesn't resolve the confusion.

**Effort:** S.

### RN3 — Configurable segmentation/breakout of run results with pie/donut visuals

**Current state:** The stored trajectory data doesn't carry the needed dimensions. `EngineResult` (`src/ProductManager.Engine/Contracts.cs:157-180`) only carries `SegmentTrajectories` keyed by segment; per `src/ProductManager.Engine/CLAUDE.md`, this shape is frozen for byte-compatibility across both engine modes. `FirmLevelDiagnostics` adds `funnelBySegment`/`funnelByChannel`, but those are opportunity-count funnels only — no dollars, no product axis. Aggregate-mode runs (the default "fast preview") carry no channel or product breakdown at all. Product is not present as a dimension anywhere in stored trajectories.

**Proposal:** Needs a design phase before implementation. Recommended approach: model breakout as a generic small set of orthogonal dimensions (segment × channel × product) rather than bespoke fields, consistent with "dimensions enter as data." Extend `FirmLevelEngine`'s per-path accumulation to roll bookings/ARR up by 2-way cross-tabs (segment×channel, segment×product, channel×product — not a full 3-way cube, to keep result size and MC noise sane), added as new fields alongside the frozen `EngineResult` (never repurposing existing ones). Aggregate mode has no channel/product concept in its input, so full breakout is realistically firm-level-only. On the frontend, reuse the existing scope-select and `FanChartCard` small-multiples pattern for time series, and add a new donut/stacked-bar component (following the `charts/echart.tsx` token-resolution convention) for point-in-time share breakdowns — letting the breakout dimension's cardinality pick donut vs. stacked bar.

**Risks:** The largest open-design item in the runs area. Which axes are actually wanted (segment/channel/product/firm), whether Aggregate-mode needs to support this at all, and whether "filter then view" means re-slicing existing output vs. conditioning the simulation itself, are all unresolved and materially change scope.

**Effort:** XL.

## Seed and initiatives

### SD1 — Add a direct sales channel; decide on origination sub-forms

**Current state:** `Channel` (`src/ProductManager.Domain/Market/Channel.cs:9-114`) is a flat entity — one row per named channel with per-segment reach/originationShare and product affinity, no sub-type or origination-mode dimension. `seed/phase3-seed.json` defines only two channels today ("Prime Broker Referrals," "Consultant Network") — no "Direct Sales" channel exists. There is no `OpportunitySource`/origination-type enum anywhere in the domain.

**Proposal:** This is seed-data-and-modeling, not a schema gap — per the doc comment at `Channel.cs:7` ("Direct sales is just another channel"), each origination mode becomes its own named `Channel` row. Do not add a sub-type/enum, which would violate "industry specifics are data, not schema." Recommended seed additions: "Inside Sales – Reverse Inquiry," "Inside Sales – Outbound Prospecting," "Marketing – Campaigns," optionally "Targeted Account Campaigns." If the PM later wants campaign-specific behavior (e.g. targeting a hand-picked firm list rather than probabilistic segment reach), that's a genuinely new mechanism — flag separately if it comes up.

**Risks:** Needs a decision on whether "targeted outreach to 30 firms" is really a standing channel (steady-state reach/originationShare) or a one-off campaign needing firm-level targeting distinct from the segment-reach model.

**Effort:** S.

### SD2 — FIX Connectivity Upgrade should be flagged as "table stakes"; domain has no concept for it

**Current state:** `Initiative` (`src/ProductManager.Domain/Execution/Initiative.cs:9-43`) has no notion of competitive necessity. `CoverageTier` (`src/ProductManager.Domain/Common/Enums.cs:4-10`) is the closest existing mechanism but describes coverage level, not "every competitor must have this." In seed data, FIX Connectivity Upgrade (`seed/phase1-seed.json:71-81`) is described purely operationally with no market-consequence framing.

**Proposal:** "Table stakes" is a real market-dynamics concept the domain can already express without new schema: a `FeatureDelta` whose effect is framed around risk-of-loss/disqualification rather than incremental win-rate. Two options were on the table: (1) narrative-only, zero code change — set the initiative's `Outcome`/`HypothesizedValue` and the `FeatureDelta.Rationale` to say so explicitly; (2) add a `StrategicIntent` tag (`Differentiation | Parity | Retention`) on `Initiative` — a legitimate schema addition since it's a genuine domain concept, but still just a label; simulation impact continues to run entirely through existing coverage deltas. **Decided: option (2).** Add the `StrategicIntent` tag (nullable) on `Initiative`, queryable and enabling portfolio parity-vs-differentiation views; NO simulation pathway — a separate churn/disqualification penalty would double-count what coverage deltas already model. FIX Connectivity Upgrade is tagged Parity in seed, with rationale text.

**Risks:** None outstanding — see Decisions (2026-08-31) below (SD2).

**Effort:** S.

### SD3 — Initiatives should have measurable KPIs, some linking into the financial simulation

**Current state:** `Initiative` has only free-text `Outcome`/`HypothesizedValue` — no KPI collection, no measurable target, no link to simulation. The market/execution seam is exactly one point: `Feature` delivered by `Project`, expressed through `FeatureDelta`. There is no code path today from `Initiative` to any simulation input other than via the Features its Projects deliver.

**Proposal:** Two separable pieces. (a) A general KPI list — pure execution-side modeling, doesn't touch the seam: an owned `InitiativeKpi` collection (Name, Unit, BaselineValue, TargetValue, optional TargetDate), analogous to `Channel`'s owned collections. Safe, additive. (b) Linking a KPI to the simulation must respect the one-seam rule: it must not become a second execution→market path. A KPI meant to move the simulation should be expressed as a `FeatureDelta` on a Feature the initiative's projects deliver (Coverage or Other/magnitude delta), with the KPI carrying only an optional pointer (`LinkedFeatureDeltaId`) to the delta that's meant to realize it — the KPI documents intent, it doesn't carry simulation logic itself. KPIs with no FeatureDelta mapping (e.g. internal engineering efficiency) stay simulation-inert by design, matching the PM's own framing that "some may not directly impact simulations."

**Risks:** Needs a decision on whether a KPI's simulation link is documentation-only (a reference to an existing delta) or should actually parameterize the delta's magnitude at commit time — the latter is a larger (L) change touching `AssumptionSet`/Engine wiring, not just Domain. Also whether a KPI is single-valued or needs a time series, which looks more like the existing bi-temporal facts-ledger pattern.

**Effort:** M.

## Decisions (2026-08-31)

Resolved in a design review with Chris on 2026-08-31. Deduplicated across areas; grouped by
area; item ids match the summary table and per-area sections above. These decisions are
authoritative over any conflicting wording elsewhere in this document.

**Channels (CH1, CH2)**
- **CH1-cycle:** Cycle time is measured before it's modeled — show observed per-channel cycle time (median + sample count from `openedDate`→`decidedDate` on real opportunities) as a Channels-page diagnostic. NO `SalesCycleSpec` channel dimension until the observed spread is material and calibratable. Established as a general principle: measure in diagnostics before modeling in schema.
- **CH2-preview:** The live "implied opportunities/yr" preview computes from the calibrated baseline only, explicitly labeled as such; scenario-conditioned numbers come only from the promoted `ChannelFunnel` (latest-run actuals).
- **CH1-walk:** The monthly walk is a post-run diagnostic — bucket per-path channel opens by month from the latest firm-level run, shown as median with a P10–P90 band; honest empty state ("run a firm-level simulation…") and staleness labeling; no client-side deterministic projection.

**Roadmap (RM2, RM3)**
- **RM3-a:** New persisted `RoadmapView` entity — multiple named views per `Roadmap`, one marked default; v1 config is granularity bands ONLY (no filters, no lane config).
- **RM3-b:** Bands are rolling (resolved against today at render time) and calendar-aligned (edges snap to week/month/quarter boundaries); stored as data on the view; Chris's scheme is the hardcoded default; no editor UI in v1.
- **RM3-c:** Drag/resize stays day-precise everywhere with NO display-grid snapping; pointer position maps to dates through the piecewise scale (position→date, not delta-px→delta-days); the RM2 modal's typed dates are the precision path for far-zone items.
- **RM2:** Click-vs-drag disambiguation via pixel deadzone; click opens a MODAL (project + initiative, editable planned dates, team/capacity, violations) built as an extractable body component for a future `/projects/[id]` route; the `[x]` unplan control stays exactly as-is with no confirmation or undo toast (it only returns the item to the tray).

**UI/Layout (UI1, UI2)**
- **UI1:** Shared `TableRowActions` component — sticky right action column (solid background, subtle border/shadow), hover-reveal on pointer devices with an always-visible fallback via `@media (hover: none)`, collapse to a kebab menu at 3+ actions; applied to all six affected pages.
- **UI2:** `/competitors/[id]` gets full editing parity via the extracted shared product-list component; the competitors list page stays a table (names/badges link to detail); competitor strategy notes move onto the detail page.

**Firms (FM2, FM3, FM4)**
- **FM4:** Three mutually exclusive firm states with precedence Client > Prospect > No engagement. Client = ≥1 active relationship to one of OUR products; Prospect = not a client AND ≥1 open (undecided) opportunity; No engagement = neither. Four-way toggle (All / Clients / Prospects / No engagement) computed server-side; systems-in-use multiselect uses OR semantics.
- **FM2:** Incumbent is informational in v1 (no calibration feed); stored as `IncumbentFirmProductRelationshipId` (guarantees landscape membership, carries price/acquisition context for future switching-cost calibration); auto-defaulted when the firm has exactly one active relationship, required pick otherwise.
- **FM3:** The section is called "Footprint" (modules/products held, served processes, tenure) — never "usage"; no telemetry ingestion. Revenue = derived run-rate Σ(`PricePaidPerPeriod` × `PriceLevel`) across active our-product relationships, labeled "estimated ARR (from relationship pricing)"; no bookings ledger in v1.

**Pipeline (PL3)**
- **PL3-probability:** NO stored manual probability field, ever; model-implied win probability (scoring the real choice set through the engine's utility math via a small API endpoint over the pure engine library) is a fast-follow feature, not part of the v1 detail view. Rep gut-feel that disagrees with the model is a note, not a number.
- **PL3-value:** v1 shows a derived estimate (scoped products at list price, annualized, labeled "at list") PLUS a stored optional quoted-value field named "Opportunity value"; neither feeds simulation/calibration in v1 (quoted-vs-list is a future `PriceLevel` calibration candidate).
- **PL3-rep:** Minimal `SalesRep` entity (id + display name only), deliberately NOT a user account; nullable `SalesRepId` on `Opportunity`; inline-create from the opportunity form; links to real user accounts if/when those arrive.

**Calibration (CB1)**
- **CB1:** Natural-units editor is the DEFAULT; raw distribution params behind an "Advanced" toggle. Uniform editing convention across distributions: mean ± spread, each with explicit units. Beta's equivalent-sample-size (α+β) shown as a read-only caption, not a second edit mode. Moment-matching round-trips validated with honest clamping messages (a Beta's stddev is bounded given its mean), never silent adjustment. Storage stays native `DistributionSpec`.

**Runs (RN3)**
- **RN3-axes:** Breakout axes are segment, channel, product; accumulate 2-way cross-tabs only (segment×channel, segment×product, channel×product); NO 3-way cube (MC noise per cell); NO firm axis (cardinality/noise; per-firm questions are drill-down, not segmentation). Product becomes a new dimension in engine per-path accumulation.
- **RN3-mode:** Breakouts are firm-level-run only; aggregate runs keep segment-only breakouts; channel/product controls shown DISABLED with "requires a firm-level run" (not hidden); no allocation-rule retrofit for aggregate mode, ever, without its own design review.
- **RN3-metrics:** Each cross-tab cell carries opened, won, win rate, bookings, and ARR; NPV remains run-level only (per-cell discounted trajectories excluded deliberately).

**Seed and initiatives (SD1, SD2, SD3)**
- **SD1:** Seed three standing direct-sales channels ("Inside Sales – Reverse Inquiry", "Inside Sales – Outbound Prospecting", "Marketing – Campaigns"); targeted-account campaigns are NOT a channel — earmarked as a future event-overlay mechanism (time-bounded, firm-targeted arrival boost) to be designed separately.
- **SD2:** Add the `StrategicIntent` tag (`Differentiation | Parity | Retention`, nullable) on `Initiative` — queryable, enables portfolio parity-vs-differentiation views; NO simulation pathway (a separate churn/disqualification penalty would double-count what coverage deltas already model). FIX Connectivity Upgrade gets tagged Parity in seed with rationale text.
- **SD3:** `InitiativeKpi` (Name, Unit, BaselineValue, TargetValue, optional TargetDate) with optional `LinkedFeatureDeltaId` pointer that is documentation-only — the KPI never mechanically sets a delta's magnitude (auto-parameterization is rejected on principle as a second execution→market path and a smuggled modeling judgment; KPI-vs-delta discrepancy is information, not an error). KPIs single-valued in v1; time series is the facts-ledger pattern, later.

**Minor**
- **PL1:** Decided-date filter applies to the Decided column only (client-side); no global pipeline date filter for now.
- **RN1:** Unnamed-run fallback label = absolute compact timestamp (e.g. "Aug 31, 14:02"), not relative time.

## Suggested sequencing

1. **Bugs and quick wins (CB2, RN1, RN2, RM1, FM1, PL1, PL2, SD1, SD2).** All are S-effort, mostly independent, and several are pure bug fixes or already-working features needing only labeling/seed data — the highest ratio of visible improvement to risk, and they don't block on any open design question.
2. **Seed data (PL2, FM1, SD1 seed additions, and RM4 once RM3's `RoadmapView` lands).** Cheap to do right after the quick wins, makes every other area's UI work demoable against realistic data, and several other items (FM3's CRM record, RN3's breakouts) are easier to evaluate visually once the pipeline has non-decided opportunities and firms have mixed landscapes.
3. **UI affordances with no open design questions (RM2, UI1, UI2, FM4, CH2's win-probability readout).** These are scoped enough to build now — they touch existing data and existing patterns (ConfirmDialog, DropdownMenu, shared component extraction) without needing a product decision first.
4. **Items that needed Chris's input before building (CH1's cycle-time schema, RM3 granularity storage, CB1 natural-units default, SD3 KPI-to-simulation linking, FM2 incumbent tracking, FM3's usage/revenue definitions).** Each had a specific fork; all were resolved in the 2026-08-31 design review (see Decisions above), so these items are now unblocked and can proceed in this slot without further design work.
5. **The two largest design items last: RN3 (segmentation/breakout of run results) and PL3 (opportunity CRM detail with probability/value).** Both are XL/L — their design forks are now resolved (RN3-axes/mode/metrics, PL3-probability/value/rep above) but they remain the largest builds — and both are more valuable once steps 1-4 have made the underlying data (seeded pipeline, firm/channel breakdowns) rich enough to make the new views worth building against.
