# CLAUDE.md — frontend/

Note: `AGENTS.md` in this directory is auto-managed by `next dev` (a generic warning that this
Next.js version may differ from training data) — it is not project-specific guidance. This file
is the real convention doc and is not touched by Next.js tooling.

## Invariants

- **The API client is hand-written, not generated.** `src/lib/api/types.ts` and
  `src/lib/api/client.ts` are hand-maintained against `openapi/productmanager.json` — there is
  no codegen step in `package.json`. When the API contract changes (new endpoint, new/renamed
  field), update these two files yourself; nothing will catch a drift automatically on the
  frontend side the way `ApiContractDriftTests` does for the CLI. Re-read the relevant section of
  `openapi/productmanager.json` (or `src/ProductManager.Api/Contracts/*.cs`) before hand-editing.
- **TanStack Query keys are scoped by active workspace / active market.** `src/lib/queries.ts`
  keys everything workspace- or market-scoped, e.g. `markets: (ws) => ["markets", ws]`,
  `segments: (marketId) => ["segments", marketId]`. Active workspace lives in
  `activeWorkspaceStore` (`client.ts`, localStorage-backed); active market lives in
  `market-store.ts`'s `activeMarketStore`, keyed *by* workspace id. Any new query hook must
  follow this pattern — an unscoped query key will leak stale data across workspace/market
  switches.
- **UI logic that touches coverage-tier resolution or attractiveness must mirror
  `SimulationInputBuilder`'s semantics exactly, and say so in a comment.** Confirmed live
  examples: `src/lib/coverage.ts` ("the same exact-then-generic rule the simulation engine uses
  to find the 'currently assessed' tier") and `src/lib/masking.ts` ("the client-side mirror of
  the simulation's attractiveness schedule, see SimulationInputBuilder.
  BuildAttractivenessSchedule" — including the "does not attribute a delta's impact to the
  delivering feature's own product" nuance). The backend rule: resolved tier for a process at
  month M = `max(base assessed tier, any planned delta whose LiveFrom <= M)`. If you change
  either side (`src/ProductManager.Api/Simulation/SimulationInputBuilder.cs` /
  `FirmLevelInputBuilder.cs`, or these two frontend files), update the other in the same change
  and re-run both the relevant `.test.ts` files and the backend simulation tests.
- **Theme tokens are CSS variables, defined together for light and dark.** `src/app/globals.css`
  defines `:root` tokens (oklch colors) and `.dark` redefines the same variable *names* —
  including domain-specific ones like `--tier-gold`/`--tier-silver`/`--tier-bronze` (a metallic
  ramp deliberately kept clear of the amber "capacity"/red "violation" color conventions used
  elsewhere). There's no automated check for this, but the working convention is: **any new
  color token must be added to both `:root` and `.dark` in the same change**, and any new
  component using color should be checked in both themes (`theme-toggle.tsx` / the app's
  dark-mode toggle) before considering it done. `charts/echart.tsx` re-resolves these tokens to
  `rgb()` on every theme flip because echarts cannot parse `oklch()` — if you add a new chart,
  route its colors through that same resolution helper rather than hardcoding oklch/hex.
- **Auth is single-user JWT, no client-side secret.** `src/lib/auth.tsx` posts the password to
  `POST /api/auth/token` and stores only the returned token; the actual password lives
  server-side (`src/ProductManager.Api/appsettings.Development.json`), never in frontend code or
  `.env`. Don't add a hardcoded password or default credential anywhere in `frontend/`.

## Working here

- New route pages go under `src/app/(app)/<name>/page.tsx` and use `PageHeader` +
  `market-picker.tsx` for the standard shell if the page is market-scoped.
- shadcn primitives (`src/components/ui/`) are vendor code — prefer the shadcn CLI to add/update
  a primitive rather than hand-editing generated output, so upstream fixes aren't lost.
- Business logic that's unit-testable belongs in `src/lib/*.ts` with a colocated `*.test.ts`
  (Vitest) — see `coverage.ts`/`masking.ts`/`fanchart.ts`/`funnel.ts`/`pipeline.ts`/`timeline.ts`
  for the existing pattern of pure-function-plus-test-file. Prefer extracting new
  chart/derivation math into `lib/` this way rather than inlining it in a page component.

---
Update this trio (`ABSTRACT.md` / `INDEX.md` / `CLAUDE.md`) whenever you add, remove, or
substantially rework a file in this directory.
