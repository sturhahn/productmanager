# 12 - Historical facts

The facts ledger is what calibration fits against: decided opportunities give win rates and
sales-cycle lengths, churn events give the churn hazard, and monthly arrival counts give the
new-firm and conversion rates. The ledger is **bi-temporal** and append-only - every entry
carries when it was true in the world (`effectiveDate`, `decidedDate`, `month`) and when we
learned it (`recordedDate`) - which is what lets a calibration run honestly reconstruct what was
known on a past date instead of what we know now. PRD sections: "Market side - dynamics"
(Opportunity row) and "Simulation & calibration" (CalibrationRun row) in
`docs/PRDs/product_requirements.md`.

Throughout this file `pm` is shorthand for `dotnet run --project src/ProductManager.Cli --`.
There is no installed `pm` binary.

## Tier and prerequisites

- **Tier D** (calibration). Optional, and the last thing to load. Without it every parameter in
  the assumption set stays `Asserted` - the model still runs, it just never learns from history.
- **Must already be loaded**: [02-market-structure](02-market-structure.md) (segments, business
  processes, products), [11-firms-and-relationships](11-firms-and-relationships.md) (an
  opportunity names its firm), and [10-channels-and-integrations](10-channels-and-integrations.md)
  if any opportunity names a channel. All three resolve by name at load time.
- Needed to actually calibrate: [08-assumption-sets](08-assumption-sets.md). Calibration updates a
  **prior** assumption set; with no prior parameters there is nothing to fit.
- Typical effort: 30-90 minutes, dominated by mapping the CRM export. The load is one API call per
  opportunity per lifecycle step (open, qualify, decide) plus one per fact, so a few hundred deals
  take a minute or two.

## Questions to ask the user

1. **"Can you export closed-won and closed-lost opportunities from your CRM?"** Ask for: account
   name, stage-change dates (created, qualified/SQL, closed), final stage, the competitor or reason
   if lost, the product sold, and the lead source or channel. Salesforce Opportunities, HubSpot
   Deals and Pipedrive Deals all have these under different names - ask which field means what.
2. **"How far back does it go, and how far back is it trustworthy?"** Use the trustworthy window,
   not the whole export. **Rule of thumb**: aim for at least 12 months and at least 20-30 decided
   opportunities per segment you want a win rate for. Below that the conjugate update barely moves
   the prior - which is correct behaviour, not a bug (`CalibrationEngine.cs:31-38`). If the user
   has one segment with 200 deals and another with 3, expect a fitted parameter for the first and
   a pass-through for the second.
3. **"Do you want the open pipeline in here too?"** Optional but recommended: open deals cost
   nothing (they are excluded from every fit) and make the pipeline view real.
4. **"Which closed-lost deals were lost to a named competitor, and which just went nowhere?"**
   The distinction matters - see the outcome mapping table below.
5. **"When did each of these facts enter your systems?"** -> `recordedDate`. Default: omit it, and
   the server stamps now. Set it deliberately when you genuinely know - see Units.
6. **"How many clients did you lose, and when?"** -> one `churnEvent` per lost client
   relationship, dated when the client actually left. If the user has a monthly churn count rather
   than dates, ask for dates anyway: the loader deduplicates on (segment, exact date), so three
   churns in one month need three distinct dates.
7. **"How many firms entered each segment per month, and how big was the prospect pool?"** ->
   `arrivalCounts`. Two different flows, see Units. Many users have neither; that is fine, skip
   them and leave those parameters asserted.
8. **"Is your CRM the source of record, or is there a finance system that disagrees?"** Pick one
   and say which in `notes`. Do not merge two sources into one ledger without a rule.

**Consequential choices** here: how stages and null amounts map to outcomes, whether an add-on for an existing customer is a cross-sell or a win, which system is the source of record, and the trustworthy window (`commercialClassification`). These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only.

## Data requirements

### `marketDynamics.opportunities[]` (`SeedOpportunity`, `src/ProductManager.Cli/Seed/SeedModels.cs:385-415`)

| Field | Required? | Type | Unit / allowed values | Notes |
|---|---|---|---|---|
| `firmName` | yes | string | must already exist | From [11-firms-and-relationships](11-firms-and-relationships.md). |
| `channelName` | no | string | must already exist | Null = channel unknown/direct. |
| `openedDate` | yes | string | ISO `yyyy-MM-dd` | Non-nullable; never `""`. Part of the duplicate key. |
| `qualifiedDate` | no | string | ISO `yyyy-MM-dd` | Must not precede `openedDate` (`Dynamics.cs:91-99`). Recorded on the row; **not read by calibration**. |
| `decidedDate` | no | string | ISO `yyyy-MM-dd` | **Null = still an open pipeline entry.** Must not precede `openedDate`. |
| `outcome` | no | string | `Won` \| `Lost` \| `NoDecision` | Exact member names of `OpportunityStage` (`Enums.cs:129-136`). Null iff `decidedDate` is null. |
| `scopeProcessNames[]` | no | list of strings | must already exist | The processes the deal was for. Part of the duplicate key. |
| `winningProductName` | no | string | must already exist | **Required when `outcome` is `Won`** - and "won" means a decision was made, so a competitor's product goes here for a `Lost` deal too. |
| `crossSellModuleProductName` | no | string | must already exist | Setting it makes the opportunity a cross-sell (`Dynamics.cs:55`), which routes it to `CrossSellRate` instead of `WinRate`. |
| `notes` | no | string | free text | |
| `recordedDate` | no | string | ISO `yyyy-MM-dd`, **never in the future** | Omitted = stamped now. Only used when the opportunity is decided. |
| `lateRecorded` | no | bool | default `false` | **Purely descriptive.** Never sent to the API; a flag for humans and for `SeedFileTests`. |

### `marketDynamics.churnEvents[]` (`SeedChurnEvent`)

| Field | Required? | Type | Unit / allowed values | Notes |
|---|---|---|---|---|
| `segmentName` | yes | string | must already exist | The fact's subject is the segment, not the firm. |
| `effectiveDate` | yes | string | ISO `yyyy-MM-dd` | When the client actually left. Non-nullable; never `""`. |
| `notes` | no | string | free text | |
| `recordedDate` | no | string | ISO `yyyy-MM-dd`, never in the future | Omitted = stamped now. |

### `marketDynamics.arrivalCounts[]` (`SeedArrivalCount`)

| Field | Required? | Type | Unit / allowed values | Notes |
|---|---|---|---|---|
| `segmentName` | yes | string | must already exist | |
| `flowKind` | yes | string | `NewFirmArrival` \| `ConversionToMarket` | `SimulationFlowKind` member names (`Enums.cs:57-64`); defaults to `NewFirmArrival`. Other members parse but are dropped by the calibration mapper (`CalibrationInputBuilder.cs:118-124`). |
| `month` | yes | string | ISO `yyyy-MM-dd` | Any day in the month; the loader normalizes to the 1st. Non-nullable; never `""`. |
| `count` | yes | int | whole firms observed that month | 0 is meaningful evidence - see Units. |
| `exposure` | no | double | see Units | Defaults to 1.0. Must be **> 0** or the observation is dropped (`CalibrationInputBuilder.cs:122`). |
| `recordedDate` | no | string | ISO `yyyy-MM-dd`, never in the future | Omitted = stamped now. |

### CRM field mapping

| CRM concept | Seed field | Rule |
|---|---|---|
| Opportunity created / Stage = Prospecting date | `openedDate` | The start of the sales cycle. Calibration measures cycle length from here. |
| Stage = Qualified / SQL date | `qualifiedDate` | Optional; omit if the CRM has no such stage. |
| Close date (any terminal stage) | `decidedDate` | Leave null for anything still open. |
| Stage = Closed Won | `outcome: "Won"` + `winningProductName` = our product | |
| Stage = Closed Lost, reason "Lost to Palletto" | `outcome: "Lost"` + `winningProductName` = `"Palletto Cloud"` | Lost is from our perspective; the winner is still recorded. |
| Stage = Closed Lost, reason "budget" / "no decision" / went quiet | `outcome: "NoDecision"`, `winningProductName` null | |
| Stage = Closed Lost, reason "churned out" before deciding | `outcome: "NoDecision"` | A departure is not a vendor choice. Record the departure itself as a `churnEvent`. |
| Anything still in an active stage | `decidedDate: null`, `outcome: null` | |
| Lead source / channel field | `channelName` | Must match a channel name from [10-channels-and-integrations](10-channels-and-integrations.md). |

## Units and conversions

- **All dates are ISO `yyyy-MM-dd`.** The loader converts each to UTC midnight
  (`SeedLoader.cs:962`); there are no times of day in this dataset.
- **`effectiveDate` vs `recordedDate` is the bi-temporal pair.** Effective = when it was true;
  recorded = when it entered our ledger (`src/ProductManager.Domain/Facts/FactEvent.cs:5-21`).
  For an opportunity the effective date is `decidedDate` and the recorded date is `recordedDate`.
  - **Omit `recordedDate` and the server stamps now** (`ApiControllerBase.cs:43-46`). That is the
    right default for a live entry.
  - **Set it when you genuinely know when the fact entered your systems.** Importing last year's
    closed deals without it stamps them all as learned today, and then every backdated calibration
    reads an empty ledger (`Phase3Contracts.cs:230-236`).
  - **Backdating is allowed and intentional; a future date is refused.** A `recordedAt` after now
    returns a 400: *"the recorded date is when the fact entered the ledger; it can be backdated for
    an import, never post-dated"* (`ApiControllerBase.cs:40-52`).
  - `recordedDate` may be **later** than the effective date (we found out afterwards) - that is the
    normal import case. It may also be earlier, which is legal though rare.
  - `lateRecorded: true` is a human label for the handful you deliberately backdated by a month or
    more. It changes nothing in the API.
- **As-of calibration.** `pm calibrate run --assumption-set <id> --as-of yyyy-MM-dd` reads only
  facts with `recordedDate <= asOf` (`Phase3Contracts.cs:265-275`; the cut happens in the API
  before the pure module sees anything, `CalibrationContracts.cs:6-14`). `--as-of` defaults to
  today. This is how you ask "what would we have believed in March?" - and it only works if
  `recordedDate` was set honestly.
  - Documented approximation: relationship spans (the exposure denominators) are cut by
    **effective** date, not recorded date, because there is no ledger of relationship starts yet
    (`CalibrationInputBuilder.cs:44-51`).
- **What calibration fits, per source:**

  | Source | Parameter | How |
  |---|---|---|
  | Decided, non-cross-sell opportunities | `WinRate` **per segment** | Beta prior + Binomial: `Beta(a + wins, b + losses)`. `NoDecision` is excluded from the denominator - the win rate is P(win \| a decision was made) (`CalibrationEngine.cs:124-155`). Not per product. |
  | Decided opportunities of any outcome | sales-cycle `mu` / `sigma` per segment (optionally per product) | Lognormal log-moment blend over `decidedDate - openedDate`, converted to months at 30.4375 days/month. `qualifiedDate` is **not** used. A product-specific prior only counts deals that product won (`CalibrationEngine.cs:309-345`). |
  | Won cross-sell opportunities | `CrossSellRate` per segment x module | Beta over client-months. Needs relationship spans; **zero wins = no fit**. |
  | `churnEvents` | `ChurnHazard` per segment | Beta over client-months from relationship spans. **Zero churn events = no fit** (a zero would be indistinguishable from the bi-temporal cut mismatch). Without spans it falls back to `churnCount * windowMonths / 2`, which overstates the hazard (`CalibrationEngine.cs:12-17`). |
  | `arrivalCounts` | `NewFirmArrival`, `ConversionToMarket` per segment | Gamma prior + Poisson: `Gamma(a + sum(count), b + sum(exposure))`, reported moment-matched as a Lognormal (`CalibrationEngine.cs:22-25`). |

- **`exposure` means two different things, and only one of them is read:**
  - `NewFirmArrival`: exposure is the **observation window** - 1 per observed month. The parameter
    is an absolute monthly intensity. **The mapper hard-codes 1.0 here regardless of what you
    seed** (`CalibrationInputBuilder.cs:131`), so write `1.0` and move on.
  - `ConversionToMarket`: exposure is the **prospect-pool size that month**, and the parameter is a
    per-prospect monthly rate with `count ~ Poisson(rate * pool)`. This one is read as given. Get
    it from the same source as `initialProspectPool` in
    [06-segment-economics](06-segment-economics.md).
  - A month with `count: 0` is honest Poisson evidence and should be seeded, not skipped - the
    exposure *is* the observation there (`CalibrationEngine.cs:46-49`).
- **`priceLevel`, ARPU and money do not appear in this dataset.** Historical facts are counts and
  dates only; the deal's revenue comes from the relationship in
  [11-firms-and-relationships](11-firms-and-relationships.md).

## Seed JSON shape

Four opportunities in the Harborline world: a won new-logo deal through direct sales, a loss to
Palletto through the consultants, a cross-sell that was decided in December but only recorded in
February (`lateRecorded`), and a still-open deal at the prospect. Plus one churn event and two
arrival counts.

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "opportunities": [
      {
        "firmName": "Bluewater Logistics",
        "channelName": "Direct sales",
        "openedDate": "2025-01-13",
        "qualifiedDate": "2025-02-10",
        "decidedDate": "2025-04-28",
        "outcome": "Won",
        "scopeProcessNames": ["Inbound Receiving", "Inventory Control", "Order Picking"],
        "winningProductName": "Harborline WMS",
        "notes": "New logo. Replaced a spreadsheet-and-paper operation across three sites.",
        "recordedDate": "2025-04-28",
        "lateRecorded": false
      },
      {
        "firmName": "Cargo Nine",
        "channelName": "Supply chain consultants",
        "openedDate": "2025-03-04",
        "qualifiedDate": "2025-04-02",
        "decidedDate": "2025-07-15",
        "outcome": "Lost",
        "scopeProcessNames": ["Order Picking", "Shipping & Carrier Integration"],
        "winningProductName": "Palletto Cloud",
        "notes": "Lost to Palletto Cloud on the consultant's shortlist; carrier breadth cited as the deciding factor.",
        "recordedDate": "2025-07-15",
        "lateRecorded": false
      },
      {
        "firmName": "Bluewater Logistics",
        "channelName": "Direct sales",
        "openedDate": "2025-09-15",
        "qualifiedDate": "2025-10-06",
        "decidedDate": "2025-12-18",
        "outcome": "Won",
        "scopeProcessNames": ["Shipping & Carrier Integration"],
        "winningProductName": "Harborline Carrier Connect",
        "crossSellModuleProductName": "Harborline Carrier Connect",
        "notes": "Cross-sell. Signed in December but only reached the CRM at the February quarter close.",
        "recordedDate": "2026-02-10",
        "lateRecorded": true
      },
      {
        "firmName": "Meridian Fulfillment",
        "channelName": "Direct sales",
        "openedDate": "2026-07-06",
        "qualifiedDate": "2026-08-03",
        "decidedDate": null,
        "outcome": null,
        "scopeProcessNames": ["Inventory Control", "Cycle Counting", "Order Picking"],
        "notes": "Open pipeline entry: qualified, in evaluation, no decision yet."
      }
    ],
    "churnEvents": [
      {
        "segmentName": "Regional 3PLs",
        "effectiveDate": "2025-06-30",
        "notes": "Client wound down its third-party warehousing arm; not attributed to a competitor.",
        "recordedDate": "2025-07-31"
      }
    ],
    "arrivalCounts": [
      {
        "segmentName": "Regional 3PLs",
        "flowKind": "NewFirmArrival",
        "month": "2025-06-01",
        "count": 2,
        "exposure": 1.0,
        "recordedDate": "2025-07-05"
      },
      {
        "segmentName": "E-commerce Fulfillment",
        "flowKind": "ConversionToMarket",
        "month": "2025-06-01",
        "count": 9,
        "exposure": 240.0,
        "recordedDate": "2025-07-05"
      }
    ]
  }
}
```

Variant - three churn events in the same month. They need three distinct `effectiveDate` values,
because the loader deduplicates on (segment, exact effective date):

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "churnEvents": [
      { "segmentName": "E-commerce Fulfillment", "effectiveDate": "2025-10-03", "recordedDate": "2025-10-31" },
      { "segmentName": "E-commerce Fulfillment", "effectiveDate": "2025-10-17", "recordedDate": "2025-10-31" },
      { "segmentName": "E-commerce Fulfillment", "effectiveDate": "2025-10-28", "recordedDate": "2025-11-30" }
    ]
  }
}
```

## Loading and idempotency

```
pm seed --file <your-file>.json
```

- Load order: `SeedLoader.RunAsync` runs workspace -> market -> **marketDynamics** -> teams ->
  initiatives -> features -> projects -> roadmap (`SeedLoader.cs:23-48`). Within marketDynamics
  these load last: channels, integrations, firms, **opportunities, churn events, arrival counts**
  (`SeedLoader.cs:547-552`). So one file can carry firms and their history.
- A facts-only file loads on its own. `marketDynamics.marketName` must name an existing market;
  the segment, product, process and channel indexes come from the API's listing for that market,
  and an opportunity's `firmName` is looked up first among the firms this file imported and then
  in a paged listing of every firm in the market, fetched once and only on the first miss
  (`SeedLoader.FirmResolver`, `SeedLoader.cs:1078-1101`). So the `firms` block from
  [11-firms-and-relationships](11-firms-and-relationships.md) does not have to be repeated here.
- **Opportunities are loaded through the real lifecycle** - create, then qualify if
  `qualifiedDate` is set, then decide if `decidedDate` is set - so the API appends the same
  `OpportunityDecided` fact a live decision would. That fact, not the row, is what calibration
  reads (`SeedLoader.cs:815-872`).
  - **A null `decidedDate` leaves the entry live**: stage `Open`, or `Qualified` if
    `qualifiedDate` was set. It is never pushed through the decide call, and `outcome` /
    `winningProductName` are meaningless for it (`SeedLoader.HasDecision`, `SeedLoader.cs:881`).
  - `recordedDate` is passed as `recordedAt` on the **decide** call only. It has no effect on an
    undecided entry.
- **Opportunity duplicate rule**: there is no natural key. For each firm the loader lists existing
  opportunities and matches on **(opened date, exact set of scope process ids)**
  (`SeedLoader.cs:827-842`). A match is skipped and left alone - **never re-decided**, because a
  decided opportunity refuses a second decide. Consequences:
  - Two genuinely different deals at the same firm, opened the same day, with the same scope
    collapse into one. Vary the opened date or the scope if that is real.
  - Changing an outcome in the seed file and re-running does nothing. Fix it with
    `pm opportunity decide` on a fresh opportunity, or delete and reload.
- **Churn duplicate rule**: the ledger is append-only, so the loader queries for an existing
  `Churn` fact on **(segment, exact effectiveDate)** and skips if it finds one
  (`SeedLoader.cs:883-908`). Two churns in one segment on the same day collapse to one.
- **Arrival duplicate rule**: matched on **(segment, month, flowKind)** - the loader queries the
  month window and then parses each candidate fact's payload for `flowKind`
  (`SeedLoader.cs:910-937`). `month` is normalized to the 1st of its month before both the query
  and the write, so `2025-06-17` and `2025-06-01` are the same observation.
  - **Caveat worth knowing** (`SeedLoader.cs:939-960`): the payload column is `jsonb` and Postgres
    re-serializes it with a space after each colon, so this match *must* parse the JSON rather than
    compare text. It does. But because the match is on (segment, month, flowKind) only, **editing
    `count` or `exposure` and re-running is a no-op** - the existing fact wins.
- Tally lines printed by `pm seed`:
  ```
  opportunity          created   4, matched   0
  churnEvent           created   1, matched   0
  arrivalCount         created   2, matched   0
  ```

## Verification

- `pm opportunity list --firm-id <id>` - the deals at one firm. Narrow with
  `--stage Open|Qualified|Won|Lost|NoDecision`, `--channel-id <id>`, or `--is-cross-sell true`.
  `pm opportunity get <id>` shows the stage timestamps and scope.
- Frontend: `/opportunities` (page title "Pipeline",
  `frontend/src/app/(app)/opportunities/page.tsx`) shows Open / Qualified / Decided columns.
- There is no `pm` verb for the churn and arrival facts - they are ledger entries. Verify them the
  way they matter, by fitting:
  ```
  pm calibrate run --assumption-set <id> --as-of 2026-08-31
  ```
  which prints the run id and the prior-vs-posterior diff table (parameter, prior mean +/- sd,
  posterior mean +/- sd, shift, N). `pm calibrate diff <run-id>` reprints it;
  `pm calibrate list --workspace-id <id>` lists past runs.
- **Correct looks like**: `WinRate` rows for the segments where you loaded decided deals, with `N`
  equal to the number of won-plus-lost (not no-decision) deals in that segment; `ChurnHazard` and
  the arrival rates fitted for the segments where you loaded events. Anything unfitted is listed
  separately with the reason, and the reason names the as-of date - read it, it is usually "no
  events as of that date", which means your `recordedDate` values are later than your `--as-of`.
- Bi-temporal proof: run the same fit twice with two `--as-of` dates that straddle a
  `lateRecorded` deal. The earlier run must not see it. If both runs agree, your `recordedDate`
  values are not doing anything.

## Common mistakes

- **A `recordedDate` in the future.** The API refuses it with a 400 and the whole seed load stops.
  Today is the constraint: never write a date after today's date.
- **Importing history with no `recordedDate` at all.** Legal, and it silently destroys as-of
  calibration: every 2025 deal is stamped as learned on import day, so a fit as of any earlier date
  reads an empty ledger.
- **`outcome` set on an open deal, or `decidedDate` set with no `outcome`.** The pair is
  all-or-nothing: `decidedDate` null means live, and the loader will not decide it. A `decidedDate`
  with a null `outcome` throws while parsing the enum.
- **Wrong enum spelling.** `"No Decision"`, `"no-decision"` and `"NoDecison"` all fail the loader's
  parse. `"WON"` happens to parse (`ParseHelpers.ParseEnum` passes `ignoreCase: true`) but is
  off-contract. The three decided values are exactly `Won`, `Lost`, `NoDecision`. (The
  `pm opportunity decide` CLI accepts the friendlier `won | lost | no-decision`; the seed file
  does not.)
- **`outcome: "Won"` with no `winningProductName`.** The API rejects it - a won deal must name the
  winning product.
- **Mapping "lost to a competitor" as `NoDecision`.** That drops a real loss out of the win-rate
  denominator and inflates the fitted win rate. `NoDecision` is only for deals where the firm chose
  nobody.
- **Recording the winner only for our wins.** A `Lost` deal should still carry the competitor's
  product in `winningProductName` - it is the evidence of who beats us where.
- **Several churn events on the same date in the same segment.** All but the first are silently
  skipped. Use the real dates.
- **Skipping months with zero arrivals.** A zero is evidence; omitting it biases the fitted rate
  upward.
- **Expecting a re-run to correct a number.** Matched opportunities are never re-decided and
  matched facts are never rewritten - the ledger is append-only by design.
- **Referencing a firm, channel or product that has not loaded yet.** The load aborts with
  `Seed file references '<name>', which was not found among the entities loaded so far.`
