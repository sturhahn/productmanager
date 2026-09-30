# Calibration readiness and baseline reconciliation (PRD8)

You are the orchestrator agent, tier D, after datasets 08, 11 and 12 are loaded and a calibration run
exists. This procedure says whether a fitted baseline may be presented as support for roadmap decisions,
and what to tell the human when it may not. Follow-up spec: [`PRDs/PRD8-CalibrationReadiness.md`](../../../PRDs/PRD8-CalibrationReadiness.md);
the authoritative product spec stays [`product_requirements.md`](../../PRDs/product_requirements.md).

`pm` means `dotnet run --project src/ProductManager.Cli --`.

## 1. What readiness is and is not (requirement 8)

**Readiness is evidence sufficiency, not forecast accuracy.** A parameter is `Ready` when a known,
non-reused prior met a complete denominator. It says nothing about whether the model predicts well. No
status in this procedure means "validated", and you never call a run a validated forecast.

| Check | Question | Command | Proves |
|---|---|---|---|
| Smoke test | did records load, did a simulation or fit execute? | `pm sim run`, `pm calibrate run` | the pipeline runs; nothing about the numbers |
| Model readiness | is the evidence behind each fitted number sufficient, non-reused, reconciled to the sources? | `pm calibrate readiness <run-id>` plus section 5 | evidence sufficiency |

Keep the two apart in everything you write to the human. A nonempty diff table is a smoke result.

**Exploratory versus ready.** The API's run verdict (`overall`) is `Ready` only when every fitted
parameter is `Ready` and nothing asserted is in play; everything else is `Exploratory`. Choice-model
quantities the set does not define are listed as asserted with their engine default, so a run is in
practice always `Exploratory`; per-parameter `Ready` is what a fitted rate can earn. An exploratory
run is usable for what-if simulation with its assumptions listed beside the result: the `asserted`
list (choice-model quantities the fit cannot validate), the unfitted parameters (their priors stand)
and the open issues. Present it as "exploratory, with these assumptions", never as a validated
forecast, and never hide an unfitted or asserted quantity behind an aggregate.
What may be presented as support for a roadmap decision:

- a parameter that is `Ready` in the API **and** not blocked by your source reconciliation
  (section 5) may be described as "fitted from N observations, evidence complete", with its period,
  population and denominator;
- every other parameter is described by its status and reasons, as an assumption;
- choice-model quantities (price sensitivity, switching cost, feature effects, integration quality,
  channel bias) are always stated as assumptions, with a sensitivity comparison (section 8).

**A roadmap-only user is unaffected.** Tiers A and B, and tier C without calibration, never run this
procedure. It applies only when a calibration run exists or the human asks for a "calibrated" baseline.
Nothing here changes how the roadmap, feasibility or coverage views work.

**Out of scope** (PRD8): new inference algorithms, causal validation, universal accuracy thresholds,
automatic fitting of choice weights, rewriting facts or stored runs. The report never refits a run and
never edits one; a correction is a new run on corrected inputs.

## 2. Declaring prior evidence (requirement 3)

A fit updates a prior with observations. If the prior was itself built from those observations (a
`Beta(wins + 1, losses + 1)` from last year's deals, then a fit over the same deals) the evidence is
counted twice. So every assumption-set parameter that a fit will move (every engine parameter: the
five flows, plus a sales-cycle `mu`) declares what informed it.

| `priorEvidence` | Meaning | Fields |
|---|---|---|
| omitted | **unknown**. The fit runs; the report carries an open `PriorEvidenceUnknown` issue | none |
| `basis: Independent` | not derived from ledger observations: a flat `Beta(1,1)`, a benchmark, an expert estimate | optional `population` (what it is); no dates |
| `basis: Observations` with `observedFrom` and `observedThrough` | derived from outcomes effective in that inclusive period (a hand-declared prior) | both dates, optional `population` |
| `basis: Observations` with `recordedThrough` | derived from the ledger as recorded through that date (what a fit stamps) | `recordedThrough`, optional period (a stamp carries the period it inherited), `sourceCalibrationRunId` |

Declare it where you write the prior: a seed parameter's `priorEvidence` object (dataset 08), or
`pm assumptions set-param <set-id> ... --prior-basis Independent`, or `--prior-basis Observations
--prior-observed-from 2024-01-01 --prior-observed-through 2024-12-31 --prior-population "..."` (also
`--prior-recorded-through`). An `Observations` prior with neither a period nor a cut-off is refused at
write time; so is an `Independent` one that carries dates.

**Choosing the basis is a claim about the world, so it goes through the register.** Do not declare
`Independent` to make a refusal go away. If you cannot say what informed a prior, leave it undeclared (it
stays visible as `PriorEvidenceUnknown`) and return a proposed register entry asking the human.

**How a fit stamps it.** Every parameter a fit moves leaves the run with `basis: Observations`,
`recordedThrough` = the run's as-of date, `sourceCalibrationRunId` = the run, the observation period of the
prior it updated carried over unchanged (none when the chain started `Independent` or unknown), and a
`population` sentence naming the dates the fit itself read. An edit to the parameter that says nothing about
evidence keeps the stamp; clearing it takes `--clear-prior-evidence`, and relabelling a fitted posterior's
evidence without changing its distribution is refused. Parameters a fit did not move
travel into the fitted set unchanged, provenance and evidence included; they are never relabeled `Calibrated`.

**The overlap rule, in plain words.** Before anything is stored the API checks each parameter the fit
would move, by every rule its evidence carries (a fitted prior with an inherited period is checked by both):

- unknown or `Independent`: no overlap (unknown is reported as an issue instead);
- `Observations` with `recordedThrough` R (a fitted prior): the fit must read only facts recorded after
  R, that is `observationsRecordedAfter` >= R. Otherwise it re-reads what the prior already counted;
- `Observations` with only a period (hand-declared): none of the observations the fit reads for that
  parameter may be effective inside the period (for ChurnHazard and CrossSellRate, no client-month
  either: exposure runs from the `--observations-recorded-after` day, so the first disjoint window is the day
  after `observedThrough`). A window does not help with events: an old deal recorded late is still inside the
  period. Declare the real period; do not stretch it to look disjoint.

There is **no automatic subtraction**: the API never removes the overlapping observations, never backs
counts out of a Beta and never guesses an overlap. You change the window, the period or the prior.
A previously fitted prior is checked the same way on a later run: fitting a fitted set over the same
facts is refused.

## 3. Running the fit and the evidence-reuse refusal (requirement 3)

```
pm calibrate run --assumption-set <prior-set-id> --as-of 2026-09-30 --name "Baseline fit"
pm calibrate run --assumption-set <fitted-set-id> --as-of 2026-12-31 --observations-recorded-after 2026-09-30
```

`--as-of` keeps only facts recorded on or before it. `--observations-recorded-after` (must be before
`--as-of`; API field `observationsRecordedAfter`) keeps only facts recorded after it, and relationship
exposure starts on that date too: it is the sequential update of a prior fitted through that date. Both
dates are recorded on the run.

**The refusal.** When a prior the fit would move was informed by observations the fit would apply again,
the API answers 409 with problem type `urn:productmanager:problem:prior-evidence-reuse`, and `pm calibrate
run` prints the overlap and exits with **exit code 6**. Nothing was fitted or stored: no run, no fitted
set. Each refused parameter lists its `priorEvidence`, `overlapFrom` and `overlapThrough`,
`overlappingObservations` (and `overlappingExposure` for client-month flows) out of `fitObservations`.

What to do, in this order:

1. Read the overlap. Is it real (the same deals, the same months) or a period declared too widely?
   A wrong declaration is corrected in the prior set (and in the register), never papered over.
2. A fitted prior: rerun with `--observations-recorded-after` set to the prior's `recordedThrough`
   (the refusal message names it), so the run counts only newer facts.
3. A hand-declared prior whose period covers the ledger: replace it with an independent prior (flat
   `Beta(1,1)` or a benchmark, declared `Independent`), or restate it from a period the ledger does not
   hold. Both are decisions for the human; propose a register entry.
4. Or do not fit that parameter: leave it asserted and remove its fit from the claim you make.

A refusal is a finding to report, not an error to retry around. Put it in the readiness artifact
(`refusedAttempts`, see section 10).

The prior set is snapshotted on the run. Declaring evidence on the set afterwards does not change the
report of an existing run; fix the set and run again.

## 4. Reading the readiness report (requirement 1)

```
pm calibrate readiness <run-id> [--holdout-through 2027-03-31] [--output <file>] [--json]
```

API: `GET /api/calibration-runs/{id}/readiness?holdoutThrough=`. The report is computed on read from the
run's stored input, output and prior-parameter snapshots plus current reference data and ledger
counts; it never refits and never edits the run. Without `--json` the CLI prints a summary (run verdict,
per-parameter table, issues, asserted quantities, holdout, notes); `--output` also writes the full JSON
to a file, which is what you keep (section 10). Exact field names are in
[`calibration-readiness.example.json`](calibration-readiness.example.json) under `report`.

| Field | Read it as |
|---|---|
| `overall`, `overallReasons` | `Ready` only when every fitted parameter is `Ready` and `asserted` is empty; otherwise `Exploratory`, with one reason per open issue, unfitted parameter and asserted group. Never "validated" |
| `parameters[]` | one row per parameter the run could fit, per segment (and module, or product for sales cycles) |
| `asserted[]` | quantities the fit did not and cannot fit: choice-model knobs in the prior set, the ones it does not define (provenance `Default`, the engine's default value), plus `reference:*` rows (integration quality, channel bias) with their `count` |
| `populations[]` | per segment at the as-of date: firm counts by `epistemicStatus` (`observedFirms`, `inferredFirms`, `syntheticFirms`), `activeCustomers`, `moduleHolders`, decision counts, churn events, relationships, `clientMonths` |
| `exclusions[]` | per fact kind, facts the fit did not read: `recordedAfterAsOf`, `recordedOnOrBeforeWindowStart`, `unattributed` (no segment, unreadable), `addedAfterRun` (a later backdated import the run never saw) |
| `arrivalObservations[]` | per segment and flow: `observedMonths`, `zeroMonths`, `missingMonths`, `duplicateMonths` |
| `notes` | fixed caveats, below |
| `holdout` | present only with `--holdout-through` (section 9) |

**Parameter status** (closed set):

| Status | Meaning |
|---|---|
| `Ready` | fitted, and no readiness issue is open |
| `NotReady` | fitted, and at least one issue is open (`issues`) |
| `Unfitted` | the engine did not fit it; `unfittedReason` says why; the prior stands unchanged, with its own provenance |
| `Asserted` | (on `asserted[]` only) a quantity calibration does not fit: an assumption |

Unfitted reasons you will meet: no decided opportunities, no churn events ("no fit on zero evidence", an
exposure with no events is not a rate), no won cross-sells, no relationship spans for a cross-sell rate,
no monthly counts, or a prior that cannot be a probability or rate. All are honest results; report them.

**Units and denominators** (every parameter row carries `units` and `denominator`; say them aloud):

| Flow | `units` | Denominator | `exposure` |
|---|---|---|---|
| `WinRate` | probability per decided opportunity | decided new-business opportunities (won + lost); `NoDecision` (`excluded`) and cross-sells left out | none |
| `ChurnHazard` | probability per client-month | client-months: every relationship of ours in the segment, base and module alike, from acquisition to end or as-of; numerator = churn events | client-months |
| `CrossSellRate` | probability per client-month | the same client-months; numerator = won cross-sells for the module | client-months |
| `NewFirmArrival` | firms per month | observed months (1 each; an observed zero counts, a missing month does not) | months |
| `ConversionToMarket` | conversions per prospect per month | prospect-months: the sum of each observed month's prospect pool | prospect-months |
| sales cycle | log-months (lognormal mu) | decided opportunities with dates (product-specific: won by that product) | none |

`observations` is the denominator count in those units, `successes` and `failures` the wins and losses
(churns, cross-sell wins or arrivals), `observedFrom` and `observedThrough` the effective dates actually
used, `prior`, `priorProvenance`, `priorEvidence`, `posterior` and `posteriorMean` the update. The API sets
no minimum sample size and no accuracy threshold; state `observations` beside every posterior.

**Issue codes** (closed set; every code is an open issue on that parameter):

| Code | Means | What you do |
|---|---|---|
| `PriorEvidenceUnknown` | the prior has no `priorEvidence`; overlap cannot be ruled out | ask the human what informed it, declare it (section 2), refit as a new run |
| `MissingAcquisitionDate` | relationships in the segment have no acquisition date and add no client-months | source the dates ([dataset 11](../datasets/11-firms-and-relationships.md)), reload, refit; until then the hazard is inflated |
| `ChurnWithoutEndDate` | more churn events than ended relationships: an end date was probably omitted, so a churned relationship keeps adding exposure | find the relationships without `endDate`, correct them at the source, refit |
| `LogoLevelChurn` | churn facts name no product while firms hold more than one of our products: logo-level events over relationship-level exposure | settle the churn level (section 6) |
| `UnsegmentedEvidence` | firms or facts without a segment were left out of every segment's fit | resolve the segment through the register, or keep the exclusion and reconcile it (section 5) |
| `ExposureApproximated` | no relationship spans: the engine approximated exposure as churns x window / 2 | load relationships with acquisition dates, refit |
| `ArrivalMonthsMissing` | months inside the observed arrival window have no observation | section 7 |
| `DuplicateArrivalMonth` | a month was observed more than once; each copy counted as a month of exposure | deduplicate at the source (one fact per segment, flow and month; give the rows `source`), refit |
| `LedgerChangedSinceRun` | facts of a kind the parameter reads were recorded on or before the as-of date but loaded after the run was stored (`exclusions[].addedAfterRun`): the run's snapshot no longer matches the ledger | refit as a new run; the stored run is never changed |

`populations` and the issue checks that read relationships are **current state**, not reconstructions of
the as-of date. After you correct relationships or facts, refit: the old run's report shows current
counts beside a fit that used the old ones.

**Fixed notes** (always in `notes`; repeat them when you present the baseline):

- relationship knowledge-history is not reconstructed: spans come from current relationship rows cut at the
  as-of date by effective acquisition and end dates, so a relationship entered later but acquired earlier
  still contributes exposure, which makes exposure an upper bound on what was known then (and the hazard
  biased low);
- firm counts, segment membership and module holdings are current state; decision, churn and arrival counts
  are the run's own snapshot;
- readiness is about evidence, not forecast accuracy;
- choice-model quantities the set does not define are listed in `asserted` with provenance `Default` and the
  firm-level engine's default value; the flows are never evidence for them.

## 5. Reconciling against the sources (requirement 2)

The API knows what is in the ledger, not what was in the sources, so this step is yours. It runs after
every calibration run you intend to present, against the [commercial normalization](../commercial/README.md)
artifact (section 10 of that procedure names its stable keys) or, without one, against the human's own
figures. It is read-only: it creates no entity and changes no record.

Compare the report's `populations` and `exclusions` with the artifact, one **figure** per row:

| Figure | System side (report, or the loaded stock state) | Source side (PRD7 artifact) |
|---|---|---|
| `importedFirms` | `observedFirms` + `inferredFirms` + `syntheticFirms` | `normalized.firms` of the segment; `sourceCounts` for `Account` |
| `activeCustomers` | `activeCustomers` | `segmentTotals[].activeCustomers` (null when the segment is unresolved) |
| `moduleHolders` | `moduleHolders[]` for the module | `segmentTotals[].modules[].holders` |
| `closedNewLogoDeals` | `decisions.won` + `lost` + `noDecision` | `calibrationPopulation.winRate[segment]`: won + lost + noDecision + unresolved |
| `churnEvents` | `churnEvents` | `normalized.churnEvents` of the segment |
| `clientMonths` | `clientMonths` (relationship-months: base and module edges) | months recomputed at the level `calibrationPopulation.churn.level` says |
| `baseArr`, `moduleArr` | `arpuPerMonth` x `initialClientCount` x 12 (and module penetration) from `pm segment get <id>` | `segmentTotals[].baseArr`, `modules[].arr` |
| `implementationFee` | the stock state's fee | mean of the `implementationFee.inputs` |
| `monthlyObservations` | `arrivalObservations[].observedMonths` | months the source can confirm (`calibrationPopulation.arrivals`) |

For each figure record the system value, the source value, the difference (system minus source), the
**named reasons** that account for it, what is left unexplained, the tolerance and the effect on
readiness. Reasons come from a closed list: `syntheticFill` (the roster was topped up with synthetic
firms, see [dataset 11](../datasets/11-firms-and-relationships.md)), `unresolved` (the record is held in the
artifact's `unresolved` list, with its decision key), `excludedByRule` (renewal, services, duplicate),
`excludedByAsOfCut` (recorded after the as-of date; see `exclusions`), `open` (not yet decided),
`notLoaded` (deferred or a gap), `edgeVersusLogoExposure` (section 6) and `rounding`. A difference with
no named reason is `unexplained`, which is a defect in the import, not a finding to present.

**Tolerances are per figure, never universal.** Counts are integers and reconcile exactly. A tolerance
is allowed only where a rounding step or an approved financial approximation in the source artifact
produces it, and it states its basis: the implementation fee is rounded to cents by formula F8, so its
tolerance is 0.005; a month figure shown to four decimals carries the rounding of that display. Say
which formula or approval applies, or use zero. Do not borrow one figure's tolerance for another.

**A mismatch keeps the affected parameters from being presented as Ready.** When a figure is not
matched and touches a parameter's numerator, denominator or exposure, record that parameter as
`NotReady` in your own verdict, with the figure id, even when the API says `Ready`: the API cannot see
an unresolved source record. Figures whose reasons are only `excludedByRule` or `open` describe records
outside the parameter's population by definition and do not block. The standard blocker is
`unresolved`: a held-out deal that may belong to the population makes "3 of 3 decided" an upper bound
until the human answers its decision key. Lift the block only by answering, reloading and refitting.

Sources that cannot be reconciled (no artifact, an unreadable history) are a finding in their own right:
say what was compared and what could not be.

## 6. Exposure completeness and churn definitions (requirement 4)

The exposure a hazard is divided by comes from relationship rows, not facts, so their completeness is
checked before you trust a fit. The report surfaces it through the issue codes; map each to where the
cause lives:

| Issue code | Where to look | Register decision |
|---|---|---|
| `MissingAcquisitionDate` | `firmRelationship` rows in the import report with no `acquisitionDate`; the artifact's `gaps` or `unresolved` list | an acquisition date of "first of the year they signed" is a reviewed choice, not a mechanical default |
| `ChurnWithoutEndDate` | a `churnEvent` row whose firm's `firmRelationship` has no `endDate`; `calibrationPopulation.relationships.endedEdgeKeys` against `normalized.churnEvents` | the end date comes from the contract evidence; an ended subscription is one end date and one churn event |
| `LogoLevelChurn`, and the level itself | `calibrationPopulation.churn.level` (`logo` or `product`) and `exposureNote`; churn rows with no `productName` | the churn level is an interpretation: a register entry, answered before the fit, one level per fitted rate |
| `UnsegmentedEvidence` | `exclusions[].unattributed`; firms held in `unresolved` for want of a segment | the segment decision of the firm |
| `ExposureApproximated` | no `firmRelationship` rows for the segment | load the relationships (dataset 11) |

Loader-report `EXCEPTION` lines (exit code 5) mean a record is **not** in the ledger: a correction-required
deal, a churn or arrival fact the loader refused. Every such row is a reconciliation reason
(`unresolved`) until resolved; it is never counted as loaded.

**Logo versus product churn.** ChurnHazard divides churn events by client-months counted per relationship
(every base and module edge). A logo-level event (the firm left) against that denominator over-weights
firms holding a module: their exposure counts twice while the event counts once. A product-level event
(one relationship ended) matches the denominator. So: read `churn.level` from the normalization artifact,
compare the report's `clientMonths` with the months recomputed at that level (reason
`edgeVersusLogoExposure`), and propose a register entry for the level and, when the two figures differ,
for accepting the edge-level reading. Do not choose the level yourself and do not change the facts to fit it.

**What the effective-date approximation means.** Facts are cut by the date they were recorded; relationship
spans are cut by their effective acquisition and end dates, because relationships have no history of what
was known when. For a fit as of a past date this has two effects. A relationship entered later but
acquired before the as-of date still contributes exposure the numerator cannot match, so exposure is an
upper bound and a hazard is biased low (the engine refuses to fit a hazard with zero events, which is
where that bias would do most damage). And no holdout can use relationship exposure (section 9). The
report says this in its first fixed note; repeat it whenever you present an as-of fit of ChurnHazard or
CrossSellRate. Do not describe it as historical knowledge reconstruction.

## 7. Zero months versus missing months (requirement 5)

**An observed zero is evidence; a missing month is not.** `arrivalCounts` rows feed `NewFirmArrival` and
`ConversionToMarket`. A month the source confirms as zero (a report that lists the month with no new
firms) is loaded as a row with `count: 0`: it adds its exposure (one month, or the prospect pool) and pulls
the rate down, correctly. A month the source does not cover is **not** loaded, and never as `count: 0`;
a zero you invented biases the rate low, and the seed cannot say "unknown" for a number, so its encoding is
the absent row ([`commercial/README.md`](../commercial/README.md#11-unresolved-is-never-zero)).

How the report shows it, per segment and flow in `arrivalObservations`:

| Field | Meaning |
|---|---|
| `observedMonths`, `zeroMonths` | months with a row; how many of them are observed zeros |
| `firstMonth`, `lastMonth` | the observed window; only gaps **inside** it are checked |
| `missingMonths` | months inside the window with no row: missing, not zero. Raises `ArrivalMonthsMissing` |
| `duplicateMonths` | months observed more than once; each copy counts as a month. Raises `DuplicateArrivalMonth` |

For each missing month ask the source owner which it is. A confirmed zero: add the row with `count: 0`,
refit. Not recorded: leave it, keep the issue open, and say the window is shorter than it looks. Months
before `firstMonth` or after `lastMonth` are outside the window, not missing; state the window you fitted.

**No events is not the same as no data.** The engine does not fit a ChurnHazard with zero churn events, or
a CrossSellRate with no won cross-sells, however much exposure there is: the parameter is `Unfitted`, its
prior stands with its original provenance, and `unfittedReason` says so. An unchanged prior is never
labeled calibrated, and you do not seed a zero hazard to make the segment "fit". Report it as: "N
client-months observed, no churn recorded; the prior is unchanged". Whether no churn happened or none
was recorded is for the source owner to confirm; put the answer in the readiness artifact.

## 8. Parameter-specific calibration and sensitivity comparisons (requirement 6)

A win-rate or cycle-time fit validates that rate or that duration for that segment. It does **not**
validate price sensitivity, switching costs, feature effects (process importance weights, coverage deltas),
integration quality, channel bias, scope-cluster counts, brand bias or the expansion propensity. Those stay
assumptions, and the report lists them in `asserted` with their provenance, distribution and rationale
(`reference:integration-quality` and `reference:channel-product-bias` are reference-data rows with a
`count`, not parameters). Loading history never changes their status; only a new source does. They are
also why a firm-level run is exploratory whatever the flows say. A run with any `asserted` row is
`Exploratory`, and you list those assumptions next to every result you present.

**Sensitivity comparisons show what the assumptions are worth.** Pick three to five comparisons the human
cares about, vary one asserted quantity per comparison, and compare one user-relevant output. Existing
commands only:

1. Branch the set: `pm assumptions branch <fitted-set-id> --name "Sensitivity: switching cost 0.3"`.
2. Change one asserted quantity on the branch: `pm assumptions set-param <branch-id> --key
   choice.switching-cost-base --dist-type Point --p1 0.3 --rationale "sensitivity variant"`. Keyed
   knobs take their full key (for example `choice.price-sensitivity:<segment-id>`), a flow takes
   `--segment-id` and `--flow-kind`. The branch copies every parameter; the baseline is untouched.
3. Run both sets with everything else fixed (same roadmap, `--seed`, `--paths`, `--horizon-months`,
   `--mode firm-level`): `pm sim run --roadmap-id <id> --assumption-set-id <set-id> --mode firm-level`.
4. Compare: `pm sim compare <baseline-run> <variant-run>`; for a funnel question also
   `pm sim diagnostics <run-id>`.

Suggested comparisons (vary one thing in each, choose a range the human finds plausible):

| Comparison | What varies | Compare |
|---|---|---|
| Price sensitivity | `choice.price-sensitivity:<segment>` | bookings and win share in the segment |
| Switching cost | `choice.switching-cost-base` | wins against the incumbent, churned clients |
| Coverage delta | the roadmap: `pm roadmap branch <roadmap-id> --name ...`, then `pm roadmap unplan <branch-id> --project-id <project>` for the project that delivers the feature | terminal ARR and NPV of the two roadmaps |
| Integration quality or channel bias | `pm integration update <id> --quality 0.5`, `pm channel set-product-affinity <id> --product-id <product> --bias 0`. These edit live reference data, not a branch: note the original value, run, restore it | win share of the affected product |
| Win-rate uncertainty | the prior or posterior of a fitted `WinRate`, branched as above (a fitted value, so this shows sample-size sensitivity, not validity) | NPV band width |

Record each in the readiness artifact under `sensitivityComparisons`: the asserted key, baseline, variant,
the commands, the output compared and, once run, the two run ids and the result. A comparison you have
not run is `planned` with no result. Never fill in a number you did not get. A large swing for a modest
change is the finding: that assumption deserves a human decision and outside evidence before the roadmap
ranking leans on it ([`../review/README.md`](../review/README.md#7-quantities-that-need-outside-evidence)).

## 9. Holdout (requirement 7)

```
pm calibrate readiness <run-id> --holdout-through 2027-03-31
```

**What it does.** A holdout compares what the fit predicts with outcomes it never saw. The window is
the day after the run's as-of date through `--holdout-through` (API `holdoutThrough`, which must be after
the as-of date). Outcomes are taken by **effective** date, as recorded now. Each fitted WinRate and arrival
parameter is compared with the central 90% posterior-predictive interval of the held-out rate for that
sample size (beta-binomial for win rates, gamma-poisson for arrivals). `observedInsideInterval` is a
statement about one sample, not a score.

**When it is supported.** The history must be trustworthy past the as-of date: decided deals and
observed months whose `effectiveDate` and `recordedDate` you set honestly ([dataset 12](../datasets/12-historical-facts.md)),
imported with enough later history to hold out. Choose the as-of date so that (a) the fit still has
the sample you want, and (b) the window after it holds a period you would accept as a test, for example
fit as of 2026-03-31 and hold out through 2026-09-30. Do not move the as-of date after seeing a comparison.

| Status | Meaning |
|---|---|
| `Compared` | held-out outcomes exist (`sampleSize` > 0); the row gives `observedRate`, `posteriorMean` and the interval |
| `Unsupported` | no comparison; `reason` says why |

`Unsupported` reasons you will see: no held-out decided opportunities or monthly observations in the window
(the extract simply ends at the as-of date, as in the worked example); ChurnHazard and CrossSellRate,
because their client-month exposure over the window would have to be reconstructed from current
relationship rows with future knowledge (never done); sales-cycle durations (no comparison is implemented).
Report every `Unsupported` row as "not tested" with its reason; it is not a pass, and an empty holdout is
not evidence for the fit.

**What `Compared` does not mean.** Inside the interval is not "validated"; outside is a discrepancy to
explain (a changed market, a mapping error, a segment that shifted), not a failure verdict. With small
samples the interval is wide and says little either way: state `sampleSize` and `observedEvents` beside
every comparison. Never promise forecast accuracy from a holdout, and never build one from current data
by another route.

## 10. Storing the artifact (requirement 8)

Keep these together in the import package, outside the repository
([`review/README.md`](../review/README.md#9-the-import-package) lists the rest), for every run you present:

| Item | How you get it |
|---|---|
| the run id, as-of date, observation window and the fitted set id | `pm calibrate run` output, `pm calibrate show <run-id>` |
| the readiness report (JSON) | `pm calibrate readiness <run-id> --output <package>/run-<id>.readiness.json` (add `--holdout-through` if you used one) |
| the input snapshot | `GET /api/calibration-runs/{id}/input`: there is no `pm` verb for it, so fetch it with the bearer token `pm auth login` stored, and never write the token into the package |
| the prior-versus-posterior diff | `pm calibrate diff <run-id>` (redirect the table, or keep the JSON of `pm calibrate show`) |
| refused attempts | the refusal message and overlap rows of any run that exited with code 6 |
| your reconciliation, verdict and sensitivity comparisons | the agent-side artifact below |
| the decision register, the normalization artifact, the import reports and the fragments | as in the guide |

The **agent-side readiness artifact** ([example](calibration-readiness.example.json)) is one JSON file
per run that embeds the API report and adds what the API cannot know:

| Key | Content |
|---|---|
| `run` | run id, as-of date, window, prior and fitted set ids, the commands, the package file names |
| `refusedAttempts` | refusals (409, exit code 6), each with its overlap rows and what you did |
| `report` | the API report, unchanged |
| `reconciliation` | `tolerancePolicy` and `figures[]`: `system`, `source`, `difference`, `reasons`, `unexplained`, `tolerance`, `status`, `affectsParameters`, `effect` |
| `verdict` | per parameter the API status and the status you present, with the causes; the run verdict; `presentableAs`; the unsupported assumptions |
| `sensitivityComparisons` | section 8 |
| `proposedRegisterEntries` | decisions you need from the human |

Figure `status` is `matched` (within tolerance), `explained` (beyond it, fully accounted for by named
reasons), `unexplained` (a defect) or `unresolvedInSource` (the source value is itself unresolved).
`effect` is `none` or `notReady`. A run is re-presented only after a new readiness report and a fresh
reconciliation; the old artifact stays with its run.

## 11. The worked example

[`fixture/README.md`](fixture/README.md) walks the six PRD8 acceptance criteria over the Harborline world
from the [commercial normalization](../commercial/README.md#12-the-worked-example) example, pointing at
the API tests that pin the behavior and at the example artifact for the parts only an agent can do. The
tests recompute the artifact's populations, figures and posteriors from the PRD7 artifact and fragments.
The PRD7 extract is too small to fit most things, and the example says so: three parameters fit, one is
`Unfitted`, the run is `Exploratory`, the holdout is `Unsupported`, and one figure does not reconcile.
