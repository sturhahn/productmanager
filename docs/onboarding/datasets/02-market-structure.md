# 02 - Market structure

Defines the static skeleton of one market: the market itself, its client segments, the
business-process catalog (with optional sub-processes and alternative approaches), the
competitors, and every product in competition (ours and theirs) with list prices. This is the
vocabulary that the coverage grid ([05-coverage-and-needs](05-coverage-and-needs.md)), feature
deltas ([03-initiatives-projects-features](03-initiatives-projects-features.md)) and every
simulation dataset (06-12) reference by name. It maps to the PRD sections "Market side -
structure" and "Market side - demand & supply of capability" (BusinessProcess and Approach rows)
in `docs/PRDs/product_requirements.md`.

Commands below use `pm` as shorthand for `dotnet run --project src/ProductManager.Cli --`.

## Tier and prerequisites

- Tier B. Mandatory for any market-side use (coverage grid, feature deltas with a
  `businessProcessName`, any simulation). Optional for a roadmap-only instance, but initiatives
  with `targetSegmentNames` and features with `productName` need it loaded first.
- Prerequisites: [01-workspace-and-teams](01-workspace-and-teams.md) (the market is created in
  the workspace the seed run resolves). Nothing else.
- Everything in this file lives under one top-level `market` key. The loader creates it in the
  order segments -> businessProcesses -> approaches -> competitors -> products (with prices)
  before it touches coverages and needs (`src/ProductManager.Cli/Seed/SeedLoader.cs`,
  `LoadMarketAsync`).
- Typical load: a few dozen HTTP calls, well under a minute.

## Questions to ask the user

Ask these in order; defaults are in brackets.

1. Market name and one-sentence description. One market = one competitive arena. If the user
   sells into two arenas with different competitors and process catalogs, that is two markets
   (two seed files). [Default: the product category as the user names it in sales material.]
2. Segments: how does sales or finance bucket customers (size band, sub-vertical, geography)?
   Aim for 2-5 segments that differ in what they need or how they buy. Where is the list -
   CRM picklist, pricing sheet, board deck?
3. Per segment, two psychographics in [0,1]:
   - `suiteAffinity`: when this segment buys, does it evaluate the whole platform (1.0) or one
     process at a time, best-of-breed (0.0)? [Default 0.5.]
   - `consolidationPreference`: how much does it value fewer vendors? [Default 0.5.]
   Ask "of your last ten deals in this segment, how many were full-platform evaluations?" and
   use that fraction for `suiteAffinity`.
4. Business-process catalog: what does a customer have to accomplish that any vendor in this
   market is judged on? Source: the sales demo script, the RFP checklist your team answers, or
   the module list on the pricing page. Aim for 6-12 top-level processes. Add a sub-process
   (`parentProcessName`) only where coverage genuinely differs from the parent - "Cycle
   Counting" under "Inventory Control" earns its row if a competitor is Gold on counting but
   Bronze on the rest of inventory. Otherwise keep it as a bullet in the parent's description.
5. Approaches: is there any process where customers accomplish it by materially different
   methods, and vendors cover one method but not another (paper vs scanner-guided picking;
   partner vs direct settlement)? Add approaches only for that process. Most markets have zero
   or one such process. [Default: none.]
6. Competitors: who shows up in the last 20 competitive deals? 2-5 names, each with one line of
   strategy notes (where they win, where they lose). Each competitor needs at least one product.
7. Products: our sellable offerings and each competitor's. Is anything sold as a separately
   priced module? If yes it is a product with `parentProductName`. Product names must be
   unique across the whole market, ours and theirs together.
8. Pricing per product: recurring list price per client per month, one-time implementation fee,
   currency (3-letter ISO). If prices differ by segment, give the per-segment rows; otherwise
   one default row. [Default: one default row; competitor prices are estimates - say so in the
   product description.] Source: price book, order forms, competitor proposals seen in deals.

**Consequential choices** here: how segments are defined and which segment an account belongs to (`segmentDefinition`), the process catalog and approaches, the `suiteAffinity` and `consolidationPreference` values, and competitor price estimates. These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only.

## Data requirements

Every field below exists on the POCOs in `src/ProductManager.Cli/Seed/SeedModels.cs`
(`SeedMarket`, `SeedSegment`, `SeedBusinessProcess`, `SeedApproach`, `SeedCompetitor`,
`SeedProduct`, `SeedPrice`). JSON keys are camelCase; unknown keys are a defect.

### market

| field | required | type | unit / format | notes |
|---|---|---|---|---|
| `name` | yes | string (<= 200) | | Natural key within the workspace. Trimmed, matched case-insensitively. |
| `description` | no | string | | |
| `segments` | no | array | | See below. |
| `businessProcesses` | no | array | | Parents before children. |
| `approaches` | no | array | | |
| `competitors` | no | array | | |
| `products` | no | array | | Parents before modules. |
| `coverages`, `needs` | no | array | | Documented in [05-coverage-and-needs](05-coverage-and-needs.md). |

There is no currency on the market. Currency is carried per price row (below). Stock-state
economics (ARPU, costs) are unit-less doubles documented in
[06-segment-economics](06-segment-economics.md).

### segments[]

| field | required | type | unit / format | notes |
|---|---|---|---|---|
| `name` | yes | string (<= 200) | | Natural key within the market. |
| `description` | no | string | | |
| `suiteAffinity` | no | number | fraction in [0,1] | Default 0.5. API rejects values outside [0,1] (`src/ProductManager.Api/Contracts/MarketContracts.cs`, `CreateClientSegmentRequest`). |
| `consolidationPreference` | no | number | fraction in [0,1] | Default 0.5. Same validation. |

Approach adoption shares (what fraction of the segment's firms use each approach) are not
seedable: set them after loading with `pm segment set-approach-adoption <segment-id>
--approach-id <guid> --share <fraction>`. An empty mix means one implicit approach.

### businessProcesses[]

| field | required | type | unit / format | notes |
|---|---|---|---|---|
| `name` | yes | string (<= 200) | | Natural key within the market, across all levels of the hierarchy - a sub-process may not reuse a name used elsewhere in the market. |
| `description` | no | string | | |
| `parentProcessName` | no | string | | Must name a process that appears earlier in this array (or already exists in the market). Omit for a top-level process. |

### approaches[]

| field | required | type | unit / format | notes |
|---|---|---|---|---|
| `name` | yes | string (<= 200) | | Natural key within its process (the loader keys on process + approach name). |
| `businessProcessName` | yes | string | | Must resolve to a loaded process. |
| `description` | no | string | | |

### competitors[]

| field | required | type | unit / format | notes |
|---|---|---|---|---|
| `name` | yes | string (<= 200) | | Natural key within the market. |
| `strategyNotes` | no | string | | Free text; shown on the Competitors page, not read by the engine. |

### products[]

| field | required | type | unit / format | notes |
|---|---|---|---|---|
| `name` | yes | string (<= 200) | | Natural key within the market, shared between our products and competitors'. |
| `description` | no | string | | |
| `competitorName` | no | string | | Omit for our own product (`Product.IsOurs` is "no competitor", `src/ProductManager.Domain/Market/Product.cs`). Must resolve to a loaded competitor. |
| `parentProductName` | no | string | | Makes this product a module of the parent. Parent must appear earlier in the array. |
| `prices` | no | array | | Zero or more rows; at most one row per segment plus at most one default row. |

### products[].prices[]

| field | required | type | unit / format | notes |
|---|---|---|---|---|
| `segmentName` | no | string | | Omit for the default (all-segment) price; set for a segment override. Must resolve to a loaded segment. |
| `recurringAmount` | yes | decimal >= 0 | money per client per month | See "Units and conversions". |
| `currency` | no | 3-letter ISO | | Default "USD". API requires exactly 3 characters (`MoneyDto`). |
| `implementationFeeAmount` | yes | decimal >= 0 | money, one-time | Use 0 when there is none. |
| `implementationFeeCurrency` | no | 3-letter ISO | | Defaults to `currency`. |
| `usageBasedPlaceholder` | no | string | | Free-text note for usage-based components. Stored, not priced (deferred per `Product.cs`). |

## Units and conversions

- `recurringAmount` is the list price one client pays for this product per month. Evidence:
  the Products page labels it "Recurring price per month" / "/mo"
  (`frontend/src/app/(app)/products/page.tsx`), and the firm-level input builder maps
  `RecurringPricePerPeriod.Amount` onto the engine's `ArpuPerProcessPerMonth`
  (`src/ProductManager.Api/Simulation/FirmLevelInputBuilder.cs`, `BuildPricing`;
  `src/ProductManager.Engine/FirmLevelContracts.cs`, `ProductPrice`). Conversions:
  - annual contract value / 12 -> `recurringAmount`
  - quarterly fee / 3 -> `recurringAmount`
  - per-seat or per-site pricing: multiply by the typical seat/site count of a client in that
    segment and record the result as a per-segment row; say so in the product description
  - usage-based components: fold the expected monthly usage revenue into `recurringAmount` and
    describe the metric in `usageBasedPlaceholder`
- `implementationFeeAmount` is one-time per new client relationship (the engine charges it per
  new relationship or extension, `FirmLevelContracts.cs`, `ProductPrice`). Do not divide it.
- Per-segment vs single price: the engine looks for a row with the firm's segment first, then
  the default row (`FirmLevelInputBuilder.cs`, `BuildPricing`). A product with neither is not
  sellable in that segment and is excluded from consideration there. Give competitors at least a
  default row or they never win a simulated deal.
- Engine pricing is per covered process: the builder divides the whole-product monthly price by
  the number of processes the product covers in that segment (floored at 1), so a full-scope
  win earns about the list price and a partial-scope win earns that fraction
  (`FirmLevelInputBuilder.cs`, `BuildPricing` remarks). Record the whole-product price; do not
  pre-divide.
- Currency: 3-letter ISO, upper-cased on save (`src/ProductManager.Domain/Common/Money.cs`).
  Keep one currency per market; the aggregate engine's financial outputs are unit-less and
  needs' workaround costs are stamped "USD" by the seed loader regardless of what you enter
  elsewhere (see [05-coverage-and-needs](05-coverage-and-needs.md)).
- `suiteAffinity` is the probability that an opportunity from this segment scopes every
  process at once (a suite purchase) instead of a cluster of processes: the builder passes it as
  `BroadScopeProbability` (`FirmLevelInputBuilder.cs`, `OpportunityScoping`;
  `FirmLevelEngine.cs`, `DrawScope`). 0 = pure best-of-breed, 1 = pure suite
  (`src/ProductManager.Domain/Market/Market.cs`, `ClientSegment.SuiteAffinity`).
- `consolidationPreference` is a utility bonus: each candidate gains
  `consolidationPreference x (covered in-scope processes / scope size)`, so a segment at 0.8
  rewards one vendor covering the whole scope by up to 0.8 utility points, on the same scale as
  importance x coverage (`FirmLevelEngine.cs`, the `consolTerm` in the utility loop). It is the
  segment default for firms without their own value.
- Percentages from the user ("70% of deals are platform evaluations") become fractions (0.7).

## Seed JSON shape

```json
{
  "market": {
    "name": "Mid-market 3PL warehouse software",
    "description": "Warehouse management software for third-party logistics providers and e-commerce fulfillment operators running 1-20 sites.",
    "segments": [
      {
        "name": "Regional 3PLs",
        "description": "Multi-client third-party logistics providers with 2-10 warehouses, billing each client per activity.",
        "suiteAffinity": 0.7,
        "consolidationPreference": 0.6
      },
      {
        "name": "E-commerce Fulfillment",
        "description": "High-volume direct-to-consumer fulfillment operators; parcel shipping and pick speed dominate.",
        "suiteAffinity": 0.4,
        "consolidationPreference": 0.3
      }
    ],
    "businessProcesses": [
      { "name": "Inbound Receiving", "description": "Receiving, putaway, and ASN reconciliation." },
      { "name": "Inventory Control", "description": "Stock accuracy, locations, lot and serial tracking." },
      { "name": "Cycle Counting", "description": "Scheduled and ad-hoc counts reconciled against book inventory.", "parentProcessName": "Inventory Control" },
      { "name": "Order Picking", "description": "Releasing, batching, and executing pick work." },
      { "name": "Shipping & Carrier Integration", "description": "Rate shopping, labels, manifests, and carrier tracking." },
      { "name": "Billing", "description": "Activity-based client invoicing for 3PL operators." }
    ],
    "approaches": [
      { "name": "Paper Pick Lists", "businessProcessName": "Order Picking", "description": "Printed lists; confirmation keyed in afterwards." },
      { "name": "RF Scanner Guided", "businessProcessName": "Order Picking", "description": "Handheld-directed picking with scan confirmation at each step." }
    ],
    "competitors": [
      { "name": "Stackline Systems", "strategyNotes": "Incumbent on-premise WMS; strong in regional 3PL billing, slow on parcel carrier updates." },
      { "name": "Palletto", "strategyNotes": "Cloud-native, e-commerce first; wins on carrier connectivity, thin on multi-client billing." }
    ],
    "products": [
      {
        "name": "Harborline WMS",
        "description": "Our core warehouse management platform.",
        "prices": [
          { "recurringAmount": 4500, "currency": "USD", "implementationFeeAmount": 25000, "implementationFeeCurrency": "USD" },
          { "segmentName": "E-commerce Fulfillment", "recurringAmount": 3500, "currency": "USD", "implementationFeeAmount": 15000, "implementationFeeCurrency": "USD" }
        ]
      },
      {
        "name": "Harborline Carrier Connect",
        "description": "Multi-carrier shipping module, sold as an add-on to Harborline WMS.",
        "parentProductName": "Harborline WMS",
        "prices": [
          { "recurringAmount": 900, "currency": "USD", "implementationFeeAmount": 5000 }
        ]
      },
      {
        "name": "Stackline WMS",
        "description": "Stackline's on-premise WMS. Prices are estimates from lost-deal debriefs.",
        "competitorName": "Stackline Systems",
        "prices": [
          { "recurringAmount": 5200, "currency": "USD", "implementationFeeAmount": 40000, "implementationFeeCurrency": "USD" }
        ]
      },
      {
        "name": "Palletto Cloud",
        "description": "Palletto's cloud WMS. Prices from their public pricing page, mid tier.",
        "competitorName": "Palletto",
        "prices": [
          { "recurringAmount": 2800, "currency": "USD", "implementationFeeAmount": 8000, "implementationFeeCurrency": "USD" }
        ]
      }
    ]
  }
}
```

## Loading and idempotency

Command (from the repo root, API running, `pm auth login` done):

```
dotnet run --project src/ProductManager.Cli -- seed --file <your-seed>.json
```

Position in `SeedLoader.RunAsync`: workspace -> **market** -> marketDynamics -> teams ->
initiatives -> features -> projects -> roadmap. Inside the market: segments, businessProcesses,
approaches, competitors, products (+ prices), then coverages and needs.

Duplicate rules (all via `GetOrCreateAsync` over a `NameIndex`: names trimmed and compared
case-insensitively, `src/ProductManager.Cli/Seed/NameIndex.cs`):

| entity | matched against | on match | on miss |
|---|---|---|---|
| market | markets in the workspace, by name | reuse; description not updated | `POST /api/markets` |
| segment | segments in the market, by name | reuse; psychographics not updated | `POST /api/client-segments` |
| business process | all processes in the market, by name (any level) | reuse; parent not updated | `POST /api/business-processes` |
| approach | approaches of the named process, by name | reuse | `POST /api/approaches` |
| competitor | competitors in the market, by name | reuse; notes not updated | `POST /api/competitors` |
| product | all products in the market, by name | reuse; competitor/parent not updated | `POST /api/products` |
| price | n/a | always re-applied | `PUT /api/products/{id}/prices` upserts by segment (`Product.SetPrice` replaces the row for the same segment) |

Every index above is built from the API's listing for the parent (the workspace for markets, the
market for everything else), never from the file's own entries, so a fragment that adds one
product resolves segment, process and competitor names an earlier file created. The approach
index additionally covers every process in the market, not only the processes whose approaches
this file lists, so a later `approachName` reference resolves against approaches loaded earlier.

Consequences:

- Re-running the same file creates nothing new and rewrites prices to the file's values. To
  change a name, description, psychographic, parent, or competitor link, use the API or the web
  app; the seed loader never updates a matched entity.
- Renaming an entity in the file and re-running creates a second entity under the new name.
- A `parentProcessName`, `parentProductName`, `competitorName`, or price `segmentName` that
  does not resolve is a preflight problem ("Seed file references '<name>' ..."), so nothing
  loads - order parents before children.
- The tally the CLI prints has one line per kind: `market`, `clientSegment`, `businessProcess`,
  `approach`, `competitor`, `product`, each with `created N, matched M`. Prices are not tallied.
  A clean re-run shows `created 0` on every line.

## Verification

- Workspace id: `pm workspace get-default`.
- Segments: `pm segment list --market-id <market-guid>`; `pm segment get <segment-guid>` shows
  `suiteAffinity`, `consolidationPreference`, and (after you set them) `approachAdoptions`.
- There are no `pm` verbs for markets, business processes, approaches, competitors, or products
  (`src/ProductManager.Cli/Program.cs` registers none). Use the API with the bearer token from
  `pm auth login`, or the Swagger UI at `http://localhost:5072/swagger` in Development:
  - `GET /api/markets?workspaceId=<guid>`
  - `GET /api/business-processes?marketId=<guid>` (`parentProcessId` on each row)
  - `GET /api/approaches?businessProcessId=<guid>`
  - `GET /api/competitors?marketId=<guid>`
  - `GET /api/products?marketId=<guid>` (each row carries `competitorId`, `parentProductId`,
    and `prices[]` with `recurringPricePerPeriod` and `implementationFee`)
  The contract is `openapi/productmanager.json`; controllers are in
  `src/ProductManager.Api/Controllers/MarketCatalogControllers.cs`.
- Web app (`http://localhost:3000`): `/markets`, `/segments`, `/processes` (tree with
  sub-processes), `/competitors`, `/products` (prices shown as "<amount> <currency>/mo" plus
  the implementation fee). Correct looks like: every product has at least one price row, every
  competitor has at least one product, sub-processes sit under their parent, approaches appear
  only on the process you intended.

## Common mistakes

- Percent instead of fraction for `suiteAffinity` / `consolidationPreference` (70 instead of
  0.7): the API rejects it with a 400 validation error.
- Annual contract value in `recurringAmount`: every simulated deal books 12x. Divide by 12.
- Dividing the recurring price by process count yourself: the engine already does that. Record
  the whole-product monthly price.
- A competitor product with no price row: it is silently excluded from every simulated
  consideration set, so we win too often. Give it an estimated default row.
- Sub-process or module listed before its parent: broken-reference error. Parents first.
- Duplicate product name across ours and a competitor's (both called "WMS"): the second is
  matched to the first and never created. Prefix with the vendor name.
- Adding approaches to every process: each approach cell has to be assessed separately and the
  segment mix has to be set by hand; add an approach only where vendors genuinely cover one
  method and not another.
- Renaming an entity in the seed file after a load: creates a duplicate rather than renaming.
