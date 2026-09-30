# 10 - Channels and integrations

Channels are the routes an opportunity arrives through: how much of a segment a route can see,
how much of that segment's opportunity flow it originates, and which products it tilts toward.
Integrations are Product-to-Product quality edges, including edges to competitor products, and
they enter a candidate's utility against the stack a firm already runs. Both feed the firm-level
choice engine only (`src/ProductManager.Engine/FirmLevelEngine.cs`); the aggregate engine ignores
them. PRD sections: "Market side - structure" (Channel, Integration rows) and "The choice model
(how a firm decides)" step 1 (consideration set) and step 2 (utility) in
`docs/PRDs/product_requirements.md`.

Throughout this file `pm` is shorthand for `dotnet run --project src/ProductManager.Cli --`.
There is no installed `pm` binary.

## Tier and prerequisites

- **Tier D** (firm-level engine). Optional: skip it entirely if the user stops at tier C
  (aggregate Monte Carlo). Without channels every opportunity originates through the engine's
  implicit unbiased "direct" route, which is a valid model - only add channels when routes to
  market actually differ.
- **Must already be loaded**: [02-market-structure](02-market-structure.md) - channels reference
  segments and products by name, and both must exist in the API before this file loads.
- Recommended before running a firm-level simulation:
  [11-firms-and-relationships](11-firms-and-relationships.md) (integrations only matter against a
  firm's existing stack) and [08-assumption-sets](08-assumption-sets.md) (the choice parameters
  that scale the integration term).
- Typical effort: 20-40 minutes of elicitation for a handful of channels; the load itself is one
  API call per channel plus one per reach row and one per affinity row, and finishes in seconds.

## Questions to ask the user

1. **"Which routes does a deal actually arrive through?"** List them. Direct sales is a channel
   like any other - the PRD is explicit that "direct sales is just another channel". Typical set:
   direct sales, a consultant/advisor network, a reseller or systems integrator, a marketplace, an
   OEM/embedded partner. Default if the user has only one motion: seed a single "Direct sales"
   channel, or skip this dataset.
2. **"Where does your CRM record lead source or channel on a closed deal?"** That field is the
   estimator for `originationShare` (see Units below). If the CRM has no such field, ask for a
   split by count of last year's closed opportunities and accept a rough estimate - this is a
   share, not an accounting figure.
3. **Per channel, per segment: "Of the firms in this segment, what fraction could this route
   reach at all?"** -> `reach`. A consultant network that advises most of the mid-market has high
   reach even if it sources few deals. Default 1.0 for your own direct sales team.
4. **Per channel, per segment: "Of the opportunities that open in this segment, what fraction does
   this route originate?"** -> `originationShare`. Default 0 (a channel that influences but never
   sources).
5. **"Does this channel sell anything, or only ours?"** -> `originatesOpportunities`. A channel
   that never originates has **no effect at all** on the engine (see Units), so ask before
   spending time on its numbers.
6. **"Does this channel only present a fixed list of products?"** -> `carriesOnlyListedProducts`.
   True for an exclusive reseller or a marketplace with a closed catalog; false for an independent
   advisor who will name anyone.
7. **Consultant bias elicitation** - `bias` is the hardest number to get honestly. Ask it as a
   comparison, never as an absolute: *"Walk into a deal this consultant sourced, everything else
   equal - how much more often do they land on the competitor than on us?"* Convert the answer to
   a small signed number using the log-odds rule in Units below, then read it back: "so roughly
   60/40 in their favour before any product difference - right?" Ask for the sign and the ranking
   first (who is favoured, and by a lot or a little); the magnitude is a second-order concern.
   Default: 0 for every product on every channel, which is the honest starting point.
8. **"Which products does yours integrate with, and how well?"** Include **competitor products**.
   The partial-stack case is the point: a firm running a competitor's picking module alongside our
   WMS is common, and the edge is what makes us winnable there. Ask for a 0-1 quality score, or
   ask for a word and map it (see Units).
9. **"Do your own modules integrate with your own parent product?"** Usually yes and near 1.0 -
   seed it, because the engine averages integration quality over the firm's whole stack and a
   missing edge reads as zero.

**Consequential choices** here: which values are channels, each channel's reach and origination share (`channelMarketReach`), consultant bias, and integration quality scores. These go through the [decision register](../review/README.md), not a bracketed default above: a consequential choice not covered by an accepted decision or rule is returned to the orchestrator as a proposed register entry and is never defaulted into the fragment. Bracketed defaults apply to mechanical choices only.

## Data requirements

### `marketDynamics.channels[]` (`SeedChannel`, `src/ProductManager.Cli/Seed/SeedModels.cs`)

| Field | Required? | Type | Unit / allowed values | Notes |
|---|---|---|---|---|
| `name` | yes | string | max 200 chars | Natural key, scoped to the market. Case-insensitive, trimmed. |
| `description` | no | string | free text | Null by default. |
| `originatesOpportunities` | no | bool | default `false` | **False makes the channel inert in the engine**: `FirmLevelInputBuilder.cs:133-139` sends an empty origination-share list, so the draw weight is 0 and its product bias never applies. |
| `carriesOnlyListedProducts` | no | bool | default `false` | True turns `productAffinities` into a whitelist - see Units. |
| `segmentReaches[]` | no | list | - | Upserted by segment. |
| `segmentReaches[].segmentName` | yes | string | must already exist | Resolved via `NameIndex.GetOrThrow`. |
| `segmentReaches[].reach` | yes | double | fraction in [0, 1] | `[Range(0, 1)]` on `SetChannelSegmentReachRequest`, `src/ProductManager.Api/Contracts/Phase3Contracts.cs:52-55`. |
| `segmentReaches[].originationShare` | yes in the seed model | double | fraction in [0, 1] | Same `[Range(0, 1)]`; the API request defaults it to 0, the seed POCO to 0.0. |
| `productAffinities[]` | no | list | - | Upserted by product. |
| `productAffinities[].productName` | yes | string | must already exist | Ours or a competitor's. |
| `productAffinities[].bias` | yes | double | signed, unbounded | Additive utility term. 0 is neutral. No `[Range]` - see Units for the sane band. |

### `marketDynamics.integrations[]` (`SeedIntegration`)

| Field | Required? | Type | Unit / allowed values | Notes |
|---|---|---|---|---|
| `productAName` | yes | string | must already exist | Order is irrelevant - the edge is symmetric. |
| `productBName` | yes | string | must already exist | Posting the reverse pair to the API is a conflict; the seed loader matches either ordering first. |
| `qualityScore` | yes | double | **[0, 1]** | `[Range(0, 1)]`, `src/ProductManager.Api/Contracts/Phase3Contracts.cs:72-76`. |
| `notes` | no | string | free text | Use it to record the evidence ("phased migration at two accounts"). |

## Units and conversions

- **`reach`** is the fraction of the segment's firms the channel can see. **`originationShare`** is
  the fraction of that segment's opportunity flow the channel sources. Both are fractions in
  [0, 1], **not percents** - 35% is `0.35`.
- **The engine only ever reads their product.** The channel draw weight per segment is
  `reach * originationShare` (`FirmLevelEngine.cs:317-324`). The engine cannot distinguish
  `(0.5, 0.4)` from `(1.0, 0.2)`. Keep them separate anyway - they answer different questions and
  the UI shows both - but if you want `originationShare` to read literally as "share of
  opportunities", set `reach` to `1.0` for that channel.
- **The shares do not have to sum to 1, and are not renormalized upward.** Per segment, let
  `S = sum(reach * originationShare)` over the listed channels. If `S < 1`, the residual `1 - S`
  originates through an **implicit unbiased direct channel** - full product visibility, no bias,
  reported as the `direct` row in the funnel diagnostics. If `S > 1` the weights are scaled down
  proportionally and no residual remains. `S == 1` leaves zero residual.
  (`FirmLevelEngine.cs:310-337`.) So: if you model your own direct sales as an explicit channel
  *and* leave a residual, part of your flow silently arrives through a second, unbiased direct
  route. Make the listed weights sum to 1 unless you mean that.
- **`bias`** is an additive term in the linear utility, added just before a multinomial-logit
  draw (`FirmLevelEngine.cs:1388-1395`, then the logit at `1400-1410`). It is therefore a
  **log-odds shift**: `bias = ln(odds ratio)`. Conversions: "twice as likely, all else equal" ->
  `ln 2 = 0.69`; "60/40 in their favour" -> `ln(1.5) = 0.41`; "a slight tilt" -> `0.1` to `0.2`;
  "against us a little" -> `-0.1`. Utility contributions from coverage are typically of order
  0.1-1 per in-scope process, so a bias above about `1.0` will dominate the product comparison
  entirely - treat that as a modelling error unless the channel really is an exclusive.
- **`carriesOnlyListedProducts: true`** means the `productAffinities` list *is* the carried
  catalog: any product not listed drops out of the consideration set for opportunities this
  channel originates (`FirmLevelInputBuilder.cs:145-151`, `ChannelInput.CarriesProducts`). To
  carry a product without biasing it, list it with `"bias": 0.0`.
- **`qualityScore`** is a 0-1 quality, not a percent and not a star rating. Word mapping to offer
  the user: native/shared data model `0.9-1.0`; supported certified connector `0.6-0.8`; works but
  needs services `0.3-0.5`; a one-off customer-built bridge `0.1-0.3`.
- **How the score enters utility**: the edge is symmetric and the engine takes the max of the two
  directions if both are supplied (`FirmLevelEngine.cs:245-253`). For a candidate product `j` at a
  firm, the term is `integrationWeight * mean(quality(j, k))` over every **other** product `k` the
  firm currently holds (`FirmLevelEngine.cs:1376-1382`). It is a **mean, not a sum**: a product
  the firm holds with no integration edge contributes a 0 and drags the average down. A firm with
  an empty stack gets no integration term at all. `integrationWeight` is a global choice parameter
  seeded in [08-assumption-sets](08-assumption-sets.md).

## Seed JSON shape

Harborline: two routes to market. Direct sales owns most of the flow in both segments; the
consultant network sources the rest and is tilted toward Stackline. In each segment the listed
weights sum to exactly 1.0, so nothing leaks to the implicit direct residual: Regional 3PLs
`1.0*0.8 + 0.5*0.4 = 1.0`; E-commerce Fulfillment `1.0*0.6 + 0.8*0.5 = 1.0`.

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "channels": [
      {
        "name": "Direct sales",
        "description": "Our own account executives, sourcing from outbound, events and inbound web enquiries.",
        "originatesOpportunities": true,
        "carriesOnlyListedProducts": false,
        "segmentReaches": [
          { "segmentName": "Regional 3PLs", "reach": 1.0, "originationShare": 0.8 },
          { "segmentName": "E-commerce Fulfillment", "reach": 1.0, "originationShare": 0.6 }
        ],
        "productAffinities": [
          { "productName": "Harborline WMS", "bias": 0.15 }
        ]
      },
      {
        "name": "Supply chain consultants",
        "description": "Independent selection advisors running RFPs for 3PLs; historically tilted toward Stackline's brand.",
        "originatesOpportunities": true,
        "carriesOnlyListedProducts": false,
        "segmentReaches": [
          { "segmentName": "Regional 3PLs", "reach": 0.5, "originationShare": 0.4 },
          { "segmentName": "E-commerce Fulfillment", "reach": 0.8, "originationShare": 0.5 }
        ],
        "productAffinities": [
          { "productName": "Stackline WMS", "bias": 0.3 },
          { "productName": "Harborline WMS", "bias": -0.1 }
        ]
      }
    ],
    "integrations": [
      {
        "productAName": "Harborline WMS",
        "productBName": "Harborline Carrier Connect",
        "qualityScore": 0.95,
        "notes": "Native: shared shipment and inventory data model, no connector to configure."
      },
      {
        "productAName": "Harborline Carrier Connect",
        "productBName": "Palletto Cloud",
        "qualityScore": 0.4,
        "notes": "Partial-stack reality: two Palletto accounts run our carrier layer over their WMS via the supported connector."
      }
    ]
  }
}
```

Variant - an exclusive reseller that carries only two products. `Harborline WMS` is listed with
`bias: 0.0` so it stays in the consideration set without being tilted:

```json
{
  "marketDynamics": {
    "marketName": "Mid-market 3PL warehouse software",
    "channels": [
      {
        "name": "Direct sales",
        "originatesOpportunities": true,
        "carriesOnlyListedProducts": true,
        "segmentReaches": [
          { "segmentName": "Regional 3PLs", "reach": 1.0, "originationShare": 1.0 }
        ],
        "productAffinities": [
          { "productName": "Harborline WMS", "bias": 0.0 },
          { "productName": "Harborline Carrier Connect", "bias": 0.0 }
        ]
      }
    ]
  }
}
```

## Loading and idempotency

```
pm seed --file <your-file>.json
```

- Load order: `SeedLoader.LoadAsync` runs workspace -> market -> **marketDynamics** -> teams ->
  initiatives -> features -> projects -> roadmap (`src/ProductManager.Cli/Seed/SeedLoader.cs`).
  Inside marketDynamics, channels load first among the Phase 3 entities, then integrations, then
  firms, then opportunities, then churn events, then arrival counts (`LoadMarketDynamicsAsync`).
  Channels must therefore exist before [12-historical-facts](12-historical-facts.md) references
  them by name - in the same file or an earlier run.
- **Channel duplicate rule**: matched by name within the market (case-insensitive, trimmed) before
  creating (`SeedLoader.LoadChannelsAsync`, `NameIndex.cs`). A re-run matches. **A matched channel's
  `description`, `originatesOpportunities` and `carriesOnlyListedProducts` are NOT updated** - only
  its reach and affinity rows are re-applied. To change those three, use `pm channel update`.
- **Reach and affinity rows are PUT upserts** keyed by segment / by product, re-applied on every
  run (`LoadChannelsAsync`). Changing a number in the file and re-running does update it.
  Removing a row from the file does **not** delete it - use `pm channel remove-segment-reach` or
  `pm channel remove-product-affinity`.
- **Integration duplicate rule**: no natural key. The loader lists the integrations touching
  product A and matches **either ordering** of the pair before creating (`LoadIntegrationsAsync`).
  A matched edge is left alone, so **editing `qualityScore` in the seed file and re-running has no
  effect** - use `pm integration update <id> --quality <n>`.
- Tally lines printed by `pm seed`:
  ```
  channel              created   2, matched   0
  integration          created   2, matched   0
  channelSegmentReach  applied   4
  channelProductAffinity applied   3
  ```

## Verification

- `pm channel list --market-id <id>` - every channel with its reach and affinity rows.
  `pm channel list --market-id <id> --originates-opportunities true` narrows to the ones that
  actually source deals. `pm channel get <id>` for one.
- `pm integration list --product-id <id>` - every edge touching a product, both directions.
  `pm integration get <id>` for one.
- Frontend: `/channels` (page title "Channels", `frontend/src/app/(app)/channels/page.tsx`) and
  `/products` for the integration edges.
- The real proof is a firm-level run: `pm sim run --roadmap-id <id> --assumption-set-id <id>
  --mode firm-level`, then `pm sim diagnostics <run-id>`. The funnel-by-channel table names each
  channel plus a `direct` row. **Correct looks like**: each channel's `Opened` share roughly
  matches the `reach * originationShare` weight you seeded, and the `direct` row carries the
  residual you intended (usually near zero). A large unexpected `direct` row means your weights
  sum to less than 1.

## Common mistakes

- **Percent instead of fraction.** `"reach": 35` is rejected by `[Range(0, 1)]` with a 400. 35%
  is `0.35`.
- **A channel with `originatesOpportunities: false` and carefully tuned numbers.** It is inert:
  the builder drops its origination shares, the draw weight is 0, and its product bias is never
  applied. Set the flag or do not bother with the channel.
- **Assuming the shares are renormalized.** They are not scaled up. Weights summing to 0.6 send
  40% of the flow through the implicit unbiased direct route - which, if you also seeded a channel
  called "Direct sales", double-counts direct and reports it in two places.
- **Forgetting that the weight is the product.** `reach: 0.5, originationShare: 0.8` is a weight of
  0.4, not 0.8. This is the single most common source of "the funnel does not match what I
  entered".
- **`bias` as a probability or a percent.** It is a signed log-odds shift added to utility. `0.6`
  does not mean "60%"; `bias: 60` will make that product win essentially every deal the channel
  originates.
- **`carriesOnlyListedProducts: true` with a short affinity list.** Every product you did not list
  - including your own modules - vanishes from the consideration set of that channel's
  opportunities. List them with `bias: 0.0`.
- **Integrations only among our own products.** The competitor edges are what make partial-stack
  firms winnable; omitting them understates our position at exactly the accounts most in play.
- **Expecting a re-run to fix a quality score or a channel flag.** Matched integrations and matched
  channels are left alone. Re-seeding is not an edit path; use the `pm` update verbs.
