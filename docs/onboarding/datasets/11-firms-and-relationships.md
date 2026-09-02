# 11 - Firms and relationships

The firm roster is the market's population: prospects, our clients, competitors' clients, and the
synthetic firms the engine generates to fill the part of the market nobody has names for. "Being a
client" is not an entity but an edge - a firm-to-product relationship carrying the modules held,
the price level paid, and the acquisition date - and the same edge shape describes a competitor's
clients. The firm-level engine is vectorized over this roster; calibration reads the relationship
spans for its exposure denominators. PRD sections: "Market side - structure" (Firm and
Firm<->Product relationship rows) in `docs/PRDs/product_requirements.md`.

Throughout this file `pm` is shorthand for `dotnet run --project src/ProductManager.Cli --`.
There is no installed `pm` binary.

## Tier and prerequisites

- **Tier D** (firm-level engine). Optional: the aggregate engine (tier C) needs only segment
  stocks, not named firms. Load this when the user wants firm-level simulation or calibration.
- **Must already be loaded**: [02-market-structure](02-market-structure.md) - firms reference a
  segment by name, and relationships reference products and business processes by name.
- Load alongside or before: [10-channels-and-integrations](10-channels-and-integrations.md)
  (integration edges only bite against a firm's existing stack) and
  [12-historical-facts](12-historical-facts.md) (opportunities reference firms by name and must
  load after them - they do, in the same run).
- Recommended: [06-segment-economics](06-segment-economics.md) first. Its
  `initialProspectPool` and `initialClientCount` are the totals the roster is measured against;
  the engine fills the gap with synthetic firms.
- Typical effort: 30-60 minutes, most of it mapping a CRM account export. The load is one bulk API
  call for the whole roster, so hundreds of firms take seconds.

## Questions to ask the user

1. **"Can you export your CRM accounts to CSV?"** That export is the roster. Ask for: account
   name, the field that identifies the segment (industry, tier, size band), account type or
   status (customer / prospect / competitor account), the product or plan they are on, contract
   start date, and current contract value. Salesforce Accounts, HubSpot Companies and Dynamics
   Accounts all carry these under different names - ask which field means what rather than
   guessing.
2. **"How many accounts is that?"** Hundreds is fine - the loader sends the whole roster in one
   `POST /api/firms/bulk` call (`SeedLoader.cs:764-782`). If the answer is thousands, **ask to
   sample**: take every client and every actively-worked prospect, plus a random sample of the
   cold ones, and let the segment stocks in [06-segment-economics](06-segment-economics.md) carry
   the rest as synthetic fill. A roster is evidence, not a census.
3. **"Which of these are your clients, and on which product?"** -> a relationship to one of our
   products.
4. **"Which are on a competitor, and which one?"** -> a relationship to that competitor's product.
   Same edge shape, same fields. This is the most valuable and most often skipped data: without it
   every competitor account looks like a greenfield prospect to the engine.
5. **"Which are prospects with no system, or a system you do not model?"** -> a firm with an empty
   `relationships` list. Those count toward the segment's prospect pool.
6. **"For each client relationship, which processes does the product actually run?"** ->
   `servedProcessNames`. If nobody knows, leave the list empty - that is an honest state with a
   defined meaning (see Units), not a gap you must fill.
7. **"What does each account actually pay, versus list?"** -> `priceLevel`. See Units for the
   conversion from contract value. Default 1.0.
8. **"When did each relationship start?"** -> `acquisitionDate`. Push for this one: without it the
   relationship contributes **zero exposure** to churn and cross-sell calibration
   (`CalibrationInputBuilder.cs:135-147`). "The year they signed, first of January" is far better
   than null.
9. **"How sure are you about each row?"** -> `epistemicStatus`. `Observed` for anything from your
   own CRM or billing system; `Inferred` for a competitor client you deduced from a press release,
   a job ad, or a conversation. Default `Observed` for your own accounts, `Inferred` for
   competitor accounts you did not confirm directly.
10. **"Is there anything else about these accounts worth keeping?"** -> `attributesJson`. Free-form
    and never read by the engine or calibration (see Units) - useful for the CRM id, warehouse
    count, region. Offer it, do not insist.

**Do not ask about synthetic firms.** They are generated, not seeded - see Units.

## Data requirements

### `marketDynamics.firms[]` (`SeedFirm`, `src/ProductManager.Cli/Seed/SeedModels.cs:373-381`)

| Field | Required? | Type | Unit / allowed values | Notes |
|---|---|---|---|---|
| `name` | yes | string | max 200 chars | Natural key, scoped to the market, case-insensitive and trimmed. Opportunities in [12-historical-facts](12-historical-facts.md) reference it. |
| `segmentName` | technically no | string | must already exist | **Treat it as required.** A firm with no segment is silently dropped from every firm-level run (`FirmLevelInputBuilder.cs:607-609` filters on `ClientSegmentId is not null`). |
| `epistemicStatus` | no | string | `Observed` \| `Inferred` \| `Synthetic` | Exact member names, `src/ProductManager.Cli/Client/Enums.cs:108-113`. Defaults to `Observed`. |
| `attributesJson` | no | string | **a JSON string** | Stored in a `jsonb` column (`SeamAndMarketConfigurations.cs:94`), so it must itself parse as JSON. In the seed file it is a JSON *string* whose content is JSON - escape the inner quotes. |
| `notes` | no | string | free text | Good place for the CRM account id and the provenance of an inferred row. |
| `relationships[]` | no | list | - | Empty list = a prospect. |

### `marketDynamics.firms[].relationships[]` (`SeedFirmRelationship`)

| Field | Required? | Type | Unit / allowed values | Notes |
|---|---|---|---|---|
| `productName` | yes | string | must already exist | Ours or a competitor's - same edge shape. One relationship per (firm, product). |
| `servedProcessNames[]` | no | list of strings | must already exist | Empty means "whatever the product covers in this firm's segment" - a defined fallback, see Units. |
| `priceLevel` | no | double | multiplier on list price, `[Range(0.0001, 1000)]` | 1.0 = list. `Phase3Contracts.cs:135-140`. Defaults to 1.0. |
| `acquisitionDate` | no | string | ISO `yyyy-MM-dd` | Null is accepted but costs you calibration exposure - see Units. |

Not settable from a seed file: `pricePaidPerPeriod` (the loader always sends null,
`SeedLoader.cs:803`), `endDate`, and `notes` on the relationship. Use
`pm firm-relationship update` for those.

## Units and conversions

- **`priceLevel` is a multiplier on list price, not money.** 1.0 = list, 0.85 = a 15% discount,
  1.2 = a premium. Conversion from a CRM contract value:
  `priceLevel = (annual contract value / 12) / (sum of the product's monthly list price over the
  processes it serves at that firm)`. The list price comes from the segment price rows in
  [02-market-structure](02-market-structure.md).
  - Sanity band: 0.5 to 1.2. Anything outside that usually means the contract value covers modules
    or services the relationship does not list.
  - If the user cannot produce per-account values, use 1.0 everywhere and say so in `notes`. A
    uniform 1.0 is a defensible assumption; a fabricated spread is not.
  - **What it changes**: revenue only. Monthly recurring revenue for a relationship is
    `modules held * segment ARPU per process * priceLevel` (`FirmLevelEngine.cs:1266`). Utilities
    use **list** price for every candidate, so a discount does not make us more likely to win in
    the model - it only lowers the revenue we book. Relationships the engine wins mid-run are
    priced at list (`FirmLevelEngine.cs:1576`).
- **`servedProcessNames` is the module set the product actually runs at that firm**, and it does
  three things: it is the incumbent's coverage of an opportunity's scope, it sets the switching
  cost charged to every *other* candidate (`switchingCostBase * (processes held by others in
  scope) / scope size`, `FirmLevelEngine.cs:1371-1372`), and it multiplies revenue.
  - **Empty list = "whatever the product covers in this firm's segment"**
    (`FirmLevelInputBuilder.cs:613-627`), resolved from the coverage grid at load time. That is
    the honest state for a relationship nobody has decomposed, and it is usually generous - the
    firm is credited with every process the product covers.
  - Listing a subset is how you model a partial deployment, and it is what makes a cross-sell
    opportunity possible: cross-sell scopes are restricted to processes the firm does **not**
    already hold.
- **`acquisitionDate` is the calibration exposure clock.** Every relationship with an
  `acquisitionDate` on or before the calibration as-of date contributes client-months to the
  denominators of `ChurnHazard` and `CrossSellRate` (`CalibrationInputBuilder.cs:135-147`,
  `CalibrationEngine.cs:12-21`). A null `acquisitionDate` contributes nothing, which inflates
  those fitted hazards. Month precision is enough - use the 1st of the month.
- **`epistemicStatus`**:
  - `Observed` - from your own systems. The default.
  - `Inferred` - deduced from indirect evidence. Carried and queryable
    (`pm firm list --epistemic-status Inferred`) but treated exactly like `Observed` by the engine.
  - `Synthetic` - **generated, not seeded.** `POST /api/firms/synthetic` (`pm firm
    generate-synthetic --market <id>`) deterministically fills the gap between a segment's measured
    stocks and the roster, and **regeneration replaces the previous synthetic population** rather
    than adding a second one (`src/ProductManager.Api/Simulation/SyntheticFirmGenerator.cs:20-36`).
    The engine also fills stock gaps in-memory per run (`FirmLevelEngine.cs:443-490`). Seeding a
    firm as `Synthetic` is legal and occasionally useful (a named placeholder you want the
    regenerate step to sweep away), but if you do it by accident you get a firm that a later
    `generate-synthetic` deletes. **Default: do not seed `Synthetic`.**
- **`attributesJson` is data the schema never reads.** Grep confirms it is only ever mapped
  through DTOs and stored (`Market.cs:136-138`, `FirmLevelInputBuilder` never touches it) - no
  engine or calibration code reads a key out of it. The PRD's "industry specifics live in data,
  never in schema" line lives here. Two consequences: put anything you like in it, and do **not**
  expect a value in it to change a simulation.
- Latent firm attributes - importance weights, price sensitivity, consolidation preference - are
  **not** part of this dataset. Per-firm importance weights come from firm-scoped needs in
  [05-coverage-and-needs](05-coverage-and-needs.md); anything unset is sampled per Monte Carlo path
  from the segment's distributions (`FirmLevelContracts.cs`, `FirmInput` remarks).

## Seed JSON shape

Three firms: our client, a competitor's client, and a prospect. Bluewater's Harborline WMS
relationship is a partial deployment at a 15% discount; Cargo Nine's Palletto relationship has
undecomposed modules; Meridian Fulfillment has no relationships at all.

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "firms": [
      {
        "name": "Bluewater Logistics",
        "segmentName": "Regional 3PLs",
        "epistemicStatus": "Observed",
        "attributesJson": "{\"crmAccountId\":\"ACC-3142\",\"warehouses\":6,\"region\":\"US-Midwest\"}",
        "notes": "Our client. Won as a new logo in 2025, expanded onto Carrier Connect at the end of the year.",
        "relationships": [
          {
            "productName": "Harborline WMS",
            "servedProcessNames": ["Inbound Receiving", "Inventory Control", "Order Picking"],
            "priceLevel": 0.85,
            "acquisitionDate": "2025-05-01"
          },
          {
            "productName": "Harborline Carrier Connect",
            "servedProcessNames": ["Shipping & Carrier Integration"],
            "priceLevel": 1.0,
            "acquisitionDate": "2026-01-01"
          }
        ]
      },
      {
        "name": "Cargo Nine",
        "segmentName": "E-commerce Fulfillment",
        "epistemicStatus": "Inferred",
        "notes": "Competitor account. Palletto win confirmed by their published case study; module breakdown unknown.",
        "relationships": [
          {
            "productName": "Palletto Cloud",
            "servedProcessNames": [],
            "priceLevel": 1.0,
            "acquisitionDate": "2025-08-01"
          }
        ]
      },
      {
        "name": "Meridian Fulfillment",
        "segmentName": "E-commerce Fulfillment",
        "epistemicStatus": "Observed",
        "notes": "Prospect. Running spreadsheets and a carrier portal; open opportunity in the pipeline.",
        "relationships": []
      }
    ]
  }
}
```

## Loading and idempotency

```
pm seed --file <your-file>.json
```

- Load order: `SeedLoader.RunAsync` runs workspace -> market -> **marketDynamics** -> teams ->
  initiatives -> features -> projects -> roadmap (`SeedLoader.cs:23-48`). Within marketDynamics,
  firms load after channels and integrations and **before** opportunities
  (`SeedLoader.cs:547-552`), so a single file can carry firms and the historical facts that
  reference them.
- A firms-only file loads on its own. `marketDynamics.marketName` must name a market that already
  exists, and the segment, product and process indexes the rows resolve against are built from the
  API's listing for that market rather than from this file - so `segmentName`, `productName` and
  `servedProcessNames` resolve against whatever [02-market-structure](02-market-structure.md)
  created in an earlier run.
- **Firm duplicate rule**: the whole roster goes to `POST /api/firms/bulk` in one call, upserted by
  **(market, name)** case-insensitively (`MarketDynamicsControllers.cs:125-150`). A re-run updates
  rather than duplicates. What an update **overwrites**: `epistemicStatus`, `clientSegmentId`,
  `attributesJson`, `notes`. Omitting `attributesJson` on a re-import therefore **clears** it -
  always re-send the full row.
- **Relationship rule**: upserted by **(firm, product)** (`MarketDynamicsControllers.cs:155-176`).
  Re-running updates `servedProcessNames`, `priceLevel` and `acquisitionDate` in place. A
  relationship **not** present in the file is never removed - dropping a row from the seed file
  does not end the relationship. To end one, use `pm firm-relationship update <id> --end-date
  yyyy-MM-dd`; to remove it, `pm firm-relationship delete <id>`.
- A firm omitted from a later import is likewise not deleted.
- Tally lines printed by `pm seed`:
  ```
  firm                 created   3, matched   0
  firmRelationship     applied   3
  ```
  `matched` counts firms the bulk import updated rather than created.
- After loading the roster and the segment stocks, run `pm firm generate-synthetic --market <id>`
  to fill the unobserved market. It is safe to re-run: the previous synthetic population is
  replaced, not duplicated.

## Verification

- `pm firm list --market-id <id>` - the whole roster. Narrow with `--segment-id <id>` or
  `--epistemic-status Observed|Inferred|Synthetic`. `pm firm get <id>` for one.
- `pm firm-relationship list --firm-id <id>` - the edges for one firm; `--product-id <id>` for
  everyone on a product (this is the "who is on the competitor" query); `--active-only true` to
  exclude ended relationships.
- Frontend: `/firms` (page title "Firms", `frontend/src/app/(app)/firms/page.tsx`), which also has
  the generate-synthetic action.
- **Correct looks like**: the count of firms per segment with at least one relationship to one of
  our products is close to that segment's `initialClientCount`, and the count with no
  relationships is at or below `initialProspectPool` (both from
  [06-segment-economics](06-segment-economics.md)). Synthetic generation reports the gap it filled
  per segment; a huge synthetic client fill means the roster and the stocks disagree, and one of
  the two is wrong.

## Common mistakes

- **Omitting `segmentName`.** The seed model allows null, and the firm loads fine - then it is
  silently excluded from every firm-level simulation. Nothing warns you.
- **Seeding `epistemicStatus: "Synthetic"` for firms you inferred.** `Inferred` is the status for
  "we think this is true"; `Synthetic` means "we made this firm up to fill a stock", and a later
  `generate-synthetic` deletes it.
- **Wrong enum spelling.** `"Estimated"`, `"Assumed"` and anything else outside the vocabulary
  fail the loader's parse. `"observed"` and `"INFERRED"` happen to parse (`ParseHelpers.ParseEnum`
  passes `ignoreCase: true`) but are off-contract; write exactly `Observed`, `Inferred`,
  `Synthetic`.
- **`attributesJson` that is not JSON.** The column is `jsonb`. `"attributesJson": "6 warehouses"`
  is rejected by the database, not by validation. It must be a JSON string containing JSON:
  `"{\"warehouses\":6}"`.
- **Putting money in `priceLevel`.** It is a multiplier - `"priceLevel": 48000` is outside the
  `[0.0001, 1000]` range and returns a 400. Even 1.5 already means "pays 50% above list".
- **Leaving every `acquisitionDate` null.** Calibration then sees zero client-months and either
  refuses to fit `ChurnHazard` and `CrossSellRate` or fits them on a fallback approximation. A
  guessed month beats a null.
- **Skipping competitors' clients.** Without those edges the engine sees a greenfield market: no
  switching costs, no integration term, and win rates that flatter us everywhere.
- **Expecting a re-run to prune.** The import is an upsert, never a replace. Firms and
  relationships you delete from the seed file stay in the database.
- **Loading firms before the market.** `productName`, `segmentName` and `servedProcessNames`
  resolve against what the API already has; a miss aborts the load with
  `Seed file references '<name>', which was not found among the entities loaded so far.`
