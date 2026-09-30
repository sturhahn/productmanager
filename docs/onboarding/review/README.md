# Evidence and consequential-decision review (orchestrator detail)

Read by the orchestrator agent when [`../README.md`](../README.md) section 10 sends you here. It covers
requirements 1-7 of [PRD5](../../../PRDs/PRD5-EvidenceReview.md); the orchestrator guide holds the
ordering and the hard rules. Worked example: [`decision-register.example.json`](decision-register.example.json)
and its human-facing review [`review.example.md`](review.example.md), built on the Harborline
[discovery manifest](../discovery/source-manifest.example.json) and
[portfolio mapping](../portfolio/portfolio-mapping.example.json). Blank register:
[`decision-register.template.json`](decision-register.template.json).

## 1. When it applies and what it reviews

**Applies** to every onboarding, with or without connected sources: any choice that could change
which records exist, what a capability is worth, when it lands or what a simulation assumes is
reviewed before it becomes the baseline. The spreadsheet-only path uses the same register with
`human:` evidence in place of manifest and mapping references.

Inputs, never re-asked and never copied into the register (it references them by key):

- the discovery manifest: `openQuestions`, `datasetReadiness[].observed` against
  `proposedAssumptions`, `gaps`, `accessLimitations`;
- the portfolio mapping: `exceptions` (held and flagged), `conventions`, the date meanings;
- what the human says in the review itself (a statement is recorded once as `humanEvidence`).

Old behaviour this replaces: the dataset agent picked a documented default and flagged it after
loading. That stays true for mechanical choices only (section 2). A consequential choice is decided
by the human before the record loads, and the decision outlives the conversation in the register.

## 2. Mechanical versus interpretive

A choice is **mechanical** when a rule already fixes the outcome and no reading of the evidence is
involved. Proceed automatically, and say nothing to the human unless it fails:

- trimming and collapsing whitespace, case-folding a name for matching (the loader already does);
- ISO date formatting of an unambiguous source date;
- currency and unit conversions the human approved (recorded once in the mapping `conventions`,
  for example `unitConversion`); an unapproved conversion is interpretive;
- copying a source value into a seed field that means the same thing, exactly as read;
- applying an **accepted rule** to a record it covers (below).

A choice is **interpretive** when the outcome depends on judgement. Review it unless an accepted
rule covers it:

| Topic (`topic`) | Example |
|---|---|
| `segmentDefinition` | which field defines segments; which segment an account with no value belongs to |
| `identityMerge` | two similarly named records that may be one; an epic that may deliver a capability |
| `capabilityScope` | what a Feature covers; splitting or joining capabilities |
| `coverageTier` | a tier in a product x process cell, ours or a competitor's |
| `coverageDelta` | a Feature's from/to tier change |
| `need` | a segment's importance weight, hardness or workaround cost for a process |
| `planningDate` | which date a roadmap item follows when sources disagree |
| `commercialClassification` | a won deal as new logo or cross-sell; a null amount as unknown or zero |
| `simulationAssumption` | any parameter of an assumption set |

**Accepted rules.** A rule is itself a register entry (`entryType: "rule"`): it names what it covers
(`covers`), when it applies (`appliesWhen`) and the value or map it applies (for example a `valueMap` from a source picklist to segment names). Once accepted, every
record it covers is loaded without another question, and the record's own entry (when you keep one,
for its evidence) carries `appliedRule` and no `question`. A record the rule does not cover, such as an
account whose segment field is empty, is a new decision. Ask for a rule when the same question would
otherwise come up more than twice.

Rules are approved like decisions and reopen like decisions (section 8); a covered record does not
need its own approval.

## 3. The decision register

One JSON artifact per onboarding, kept outside the repository like the manifest and the mapping
(it names real records). Start from [`decision-register.template.json`](decision-register.template.json).
Top-level keys: `registerVersion` (1), `manifest`, `mapping` (the artifacts it reviews),
`reviewedOn`, `labelMap`, `humanEvidence`, `tierOutcome`, `decisions[]`. Each entry of `decisions[]`
(a decision or a rule) has every key below; unknown facts are explicit `null`s, never omitted.

| Key | Content |
|---|---|
| `key` | stable decision key, never reused or renamed (`DEC-<topic>-<subject>`, `RULE-<subject>`); every reference to a decision uses it |
| `entryType` | `decision` or `rule`; a rule adds `appliesWhen` and `covers` |
| `topic` | one of the nine topics in section 2 |
| `class` | what the proposed value is: `sourceFact`, `inferredInterpretation`, `default` or `assumption` (below) |
| `tier`, `datasets`, `requiredForTier` | the tier (A-D) and dataset ids it affects; `requiredForTier: true` means that tier cannot proceed without it |
| `affects` | scope: `{ ref, fields[] }`, one per record touched. `ref` is `mapping:<record, exception or dependency key>`, `salesforce:` / `aha:` / `jira:<source id>`, or `seed:<natural key>` |
| `seedTarget` | where the value lands: `fragment` (file), `collection` (dotted path to the array), `match` (natural-key values), or `null` when nothing is loaded from it. It is how an agent, and a test, find the row |
| `proposedValue` | the value in **seed shape** (the fields the row would carry), so accepted values can be copied and compared |
| `evidence` | `{ ref, note }` entries; `ref` is `mapping:`, `manifest:` (`Q<n>`, `dataset:<NN>`, `gap:<start of the concept>`), a source id as above, `human:<id>` (an entry of `humanEvidence`) or `register:<key>` |
| `sourceDate`, `effectiveDate` | when the evidence was recorded or extracted; when the decided fact holds (ISO), each `null` if unknown |
| `independentObservations` | how many of the evidence entries are independent (section 6) |
| `extractionConfidence`, `modelUncertainty` | two different facts (section 6) |
| `alternatives` | `{ value, why }` for each other reasonable choice, including "unknown" and "leave it out" |
| `rationale`, `missingEvidence` | why this value; what would settle it (`none` if nothing) |
| `outsideEvidenceQuantity` | one of the eight quantities in section 7, else `null` |
| `domainMapping` | the enum member the value lands on when the seed slot has a provenance or status (`{ enum, member }`), else `null` |
| `question` | the single question for the human, `null` when nothing is open |
| `reviewState` | `proposed`, `accepted`, `rejected` or `deferred` |
| `exclusionReason` | why the affected records are not loaded; `null` only when the decision is in force |
| `appliedRule` | the rule key when an accepted rule covers this record, else `null` |
| `accepted` | `null`, or `{ by, on, decision, material }`: who accepted it (`user` or `rule:<key>`), when, in words, and the copy of the material fields as accepted (section 8) |

Review states: `proposed` (a proposal awaiting the human), `accepted`, `rejected` (the human declined;
write the replacement as a new proposal), `deferred` (parked because a required gap or missing evidence
blocks it; the affected tier waits). A fifth state, `reopened`, is never stored: an entry whose
`reviewState` is `accepted` but whose accepted snapshot no longer equals its material fields is
reopened (section 8) and is treated exactly like `proposed`.

**Three kinds of value, kept apart.** `class` answers "what is this?":

| `class` | Meaning | Example |
|---|---|---|
| `sourceFact` | read as-is from a current source record, or stated as such by the human | Aha! release date; a picklist value; a price on the grid |
| `inferredInterpretation` | a reading across records or systems, or of a value's meaning | a name match; a Done epic read as not yet available |
| `default` | a documented fallback chosen when no evidence exists | unknown competitor coverage; a placeholder rate |
| `assumption` | a modelling judgement with no source in the connected systems, supplied or confirmed by the human | an importance weight; price sensitivity |

**Label map.** The register's descriptive labels are not domain values. Where the seed slot has a
provenance or status, the label becomes exactly one existing enum member; the register keeps the
table in `labelMap` and each entry names its landing in `domainMapping`. Real members only
(`ParameterProvenance`, `EpistemicStatus`, `CoverageTier`, `FeatureStatus`, `ProjectStatus` in
[`Enums.cs`](../../../src/ProductManager.Cli/Client/Enums.cs)):

| Label | Seed slot | Becomes |
|---|---|---|
| `sourceFact` | firm `epistemicStatus` | `EpistemicStatus.Observed` |
| `inferredInterpretation` | firm `epistemicStatus` | `EpistemicStatus.Inferred` (never `Synthetic`; synthetic firms are generated, not seeded) |
| `sourceFact`, `inferredInterpretation`, `assumption` | assumption-set parameter `provenance` | `ParameterProvenance.Asserted` (an accurately copied value is still asserted) |
| `default` | assumption-set parameter `provenance` | `ParameterProvenance.Default` |
| any label | assumption-set parameter `provenance` | never `ParameterProvenance.Calibrated`: only a calibration run produces it |
| unknown coverage | coverage `isUnknown` | `isUnknown: true`, which is a flag, not a `CoverageTier` (dataset 05) |
| a tier value | coverage `tier`, delta `fromTier` / `toTier` | `Gold`, `Silver`, `Bronze`, `NotPlaying` exactly |
| Jira Done | project `status` | `ProjectStatus.Done`; the Feature stays `FeatureStatus.Planned` until customer availability |

Coverage rows, needs and dates have no provenance slot, so their `class` stays in the register and the
row's `rationale` (or `notes`) names the decision key.

## 4. The consolidated review, loading and deferral

Run it once, after portfolio selection (or after the interview on the spreadsheet-only path) and before
any fragment loads; repeat only for entries that reopen or that new evidence adds.

1. **Collect.** Turn every held or flagged mapping exception, every open manifest question that
   changes a record, every judgement a dataset agent returned (below) and every quantity in section 7
   into one register entry with a proposed answer. Do not copy the evidence; reference it.
2. **Apply accepted rules first.** Records a rule covers get no question. What is left is the review list.
3. **Present one consolidated review** ([`review.example.md`](review.example.md)): what proceeds
   (accepted, with the rules that did it), what reopened, and the entries awaiting an answer. Each
   awaiting entry shows the proposed answer, the evidence, the alternatives and the one question; rank
   by how many records and tiers the answer changes. Show the human the readable review, not the JSON.
4. **Record the answers.** Set `reviewState`, fill `accepted` with the snapshot (section 8). A changed
   answer is a changed proposal: update `proposedValue`, keep the old one in `alternatives`, then accept.
5. **Load only what is in force.** A dataset agent's fragment contains the records of accepted entries
   (and of covered records) and nothing else. Every non-accepted entry keeps its `exclusionReason` and its
   records stay out; the report to the human says which and why.
6. **Defer a tier, not the whole onboarding.** A `requiredForTier` entry that is not accepted defers that
   tier: record `tierOutcome` (`proceed` or `deferred`) per tier. Tier A proceeds while tier C waits for its
   assumptions; the human may still choose to stop at any tier.

**Silence is not acceptance.** An unanswered proposal stays `proposed`. A dataset agent's own default
for a consequential choice is not an answer: it returns the choice to you as a proposed register entry and
never puts it in the fragment. A held mapping exception stays held.

**What a dataset agent returns.** The brief template's "Decision register" slot names the register path. A
consequential choice the register does not cover comes back in the agent's report as a draft entry (topic,
affected records, proposed value in seed shape, evidence it used, alternatives), and the agent leaves that
record out of the fragment.

## 5. Needs and coverage rubric

Use the tier definitions in [dataset 05](../datasets/05-coverage-and-needs.md) (the PRD's `Gold`,
`Silver`, `Bronze`, `NotPlaying`); do not invent finer tiers. For each cell or delta the register entry
records the tier, representative evidence and its date, in this order:

1. **Whose cell?** Our own product: a value the human states or a source shows; every cell needs an answer.
   A competitor's: only what evidence supports.
2. **Representative evidence, with a date.** A capability grid, an RFP response, a demo, an analyst report,
   a price page. Give `sourceDate` and `effectiveDate`; an undated claim is weak.
3. **Is there enough to pick a tier?** Gold is market-leading, Silver competitive, Bronze check-the-box,
   `NotPlaying` no offering. If the evidence does not separate two tiers, propose the lower and list the
   other in `alternatives`, or propose unknown.
4. **Unknown stays unknown.** A competitor cell with no accepted evidence-backed tier is loaded as
   `isUnknown: true` with no tier and no confidence. It is never Bronze "to be safe" (it would drag the
   competitor down), and a pending proposal does not change that. Put this in a rule
   (`RULE-unknown-competitor-coverage`), so every such cell is covered without a question.
5. **A request is evidence of a need.** A customer asking for a capability, or a deal lost over it, supports
   a `need` row for that segment and process. It is not evidence of any product's coverage tier and not proof
   that shipping the capability changes win probability; those need their own evidence and their own
   entries. A delta that jumps tiers (Bronze to Gold) needs evidence for the destination tier, not just for
   the need.
6. **Deltas apply at delivery.** A Feature's `coverageDelta` takes effect when its last delivering Project
   ends on the roadmap ([portfolio section 5](../portfolio/README.md#5-dates)), so a delta and the planning
   date decision review together.

## 6. Confidence, uncertainty and independence

Three different facts, three different fields. Never merge them.

- `extractionConfidence` (0 to 1, or `null`): how faithfully you read the value from the source or the
  statement. A copied number read correctly is 1.0 however weak the number is.
- `modelUncertainty` (`{ level, statement }`, `level` one of `low`, `medium`, `high`, `unquantified`): how
  sure anyone can be of the modelled fact. It is words, plus, where the human supplied one, their own
  confidence in the seed's `confidence` field. It never carries a distribution width.
- `independentObservations`: how many of the `evidence` entries are separate observations. Several notes
  that repeat one claim are one observation. It is at most the number of evidence entries.

Consequences:

- **An accurately copied asserted value is still asserted.** Reading a sales lead's estimate perfectly does
  not make it observed; the label map keeps it at `ParameterProvenance.Asserted`.
- **An agent's confidence is never a width.** Do not turn 0.7 into a Normal spread, a Beta shape or the
  seed `confidence` of a coverage row. Distribution widths come from counts, a source's own range or the
  human's stated spread (dataset 08 says how), recorded in the `rationale`.
- **Extraction confidence cannot move a tier.** An unknown cell read with confidence 0.9 is a correctly read
  unknown, not a Gold at 0.9.

## 7. Quantities that need outside evidence

Eight quantities normally have no source in the three systems (Salesforce, Aha!, Jira). Whenever one
appears, set `outsideEvidenceQuantity` to its key:

| `outsideEvidenceQuantity` | Typically lands in |
|---|---|
| `marketPopulation` | prospect pool, `NewFirmArrival`, `ConversionToMarket` (datasets 06, 08) |
| `competitorInstalledBase` | competitor firms and relationships (dataset 11) |
| `channelMarketReach` | channel `reach` and `originationShare` (dataset 10) |
| `operatingCosts` | servicing, infrastructure and R&D drag (dataset 06) |
| `priceSensitivity` | `PriceSensitivity` choice parameter (dataset 08) |
| `switchingCosts` | `SwitchingCostBase` (dataset 08), feature `SwitchingCost` deltas |
| `covariateElasticities` | `covariateBeta` (dataset 08) |
| `eventEffects` | event rate multipliers (dataset 09) |

Rules: the entry's `class` is `assumption` (or `default` for a flagged placeholder), never `sourceFact`;
`domainMapping` is `ParameterProvenance.Asserted` (or `Default`), and **never relabel it observed or
calibrated**: `Observed` is a firm status, and `Calibrated` belongs to calibration runs. It carries a
`rationale` and `missingEvidence`. A reviewed assumption may load once accepted; an unsupported one is
deferred. When no value is supportable the tier waits (`requiredForTier`).

## 8. Acceptance is tied to the reviewed version

What was accepted is a specific value, over a specific scope, on specific evidence. Those three are the
**material fields** of an entry:

| Material field | Register keys |
|---|---|
| selected value | `proposedValue` |
| scope | `affects` (each `ref` with its `fields`) |
| evidence | the `ref` of every `evidence` entry (the notes are commentary) |

When a human accepts, copy the material fields into `accepted.material` as `{ value, affects, evidence }`
(`evidence` is the list of refs). That copy is the reviewed version. Nothing is hashed: an agent compares
by hand, and the tests compare the same way.

**Check before every load.** Compare `accepted.material` with the entry's current `proposedValue`,
`affects` and evidence refs after normalizing: trim strings and collapse runs of whitespace, ignore key
order, treat arrays as sets (order does not matter). If they are equal the acceptance stands. If not,
the entry is **reopened**: treated like `proposed`, excluded from the fragments, listed in the next review
with a `question` that says what changed, and re-accepted with a fresh snapshot.

| Change | Result |
|---|---|
| a different `proposedValue` (a date moved, a tier changed) | reopens that entry |
| a record or field added to or removed from `affects` | reopens that entry |
| an evidence ref added, removed or replaced | reopens that entry |
| whitespace, key or array order, JSON layout | nothing |
| `rationale`, `alternatives`, `evidence` notes, `modelUncertainty`, `question` wording | nothing |
| a change in another entry, even for the same feature | nothing: only the entry whose material fields differ reopens |

A rule reopens the same way. If a rule reopens, the records it covered are no longer covered and become
open questions until the rule is re-accepted.

Worked case (the example register): `DEC-date-rate-shopping-planning` was accepted with an end date of
2026-10-15; the refreshed mapping now shows 2026-10-31 for the Aha! release of HWMS-1. Its snapshot no
longer equals its proposal, so it reopens and the roadmap item for the last delivering project waits.
`DEC-date-sales-commitment-rate-shopping`, on the same feature, is unchanged and stays accepted.

## 9. The import package

Keep these together, outside the repository, for as long as the instance is used:

- the decision register, with the accepted snapshots and the accepted assumptions;
- the source references it cites: the discovery manifest (with its `sourceIds`, `sourceLinks` and
  `rawResponseFiles`) and the portfolio mapping;
- the seed fragments as loaded, one per dataset.

With these, a later step can answer "why is this cell Silver?" without re-asking: find the fragment
row, its decision key in the `rationale`, the entry, its evidence and the source record. Re-running
after a source changes means recomputing each entry's acceptance (section 8) and regenerating only what
reopened. The seed shape is unchanged; the register is a local artifact, never loaded, and there is no
API for it.

## 10. The worked example against the acceptance criteria

[`decision-register.example.json`](decision-register.example.json) holds 3 rules and 14 decisions on
Harborline; [`review.example.md`](review.example.md) is what the human reads;
[`fixture/seed/`](fixture/seed) holds the fragments generated from the entries in force. The tests check
each row below.

| PRD5 acceptance criterion | Where it shows |
|---|---|
| 1. A clean mapping loads under an accepted rule without repeated questions | `RULE-segment-from-segment-c` covers Bluewater and Cargo Nine; they are the two firms in `11-firms.json` and no entry asks about them. Meridian, with a null `Segment__c`, is `DEC-account-meridian-segment`, proposed and not loaded |
| 2. An unsupported Bronze-to-Gold delta stays proposed and cannot leak | `DEC-delta-rate-shopping-shipping` is `proposed`; `03-feature-deltas.json` has the Feature and no delta. The brief's rule stops a dataset agent adding it as a default |
| 3. Unknown competitor cells stay unknown | `DEC-cov-stackline-cycle-counting` (extraction confidence 0.9, one independent observation among four references) and the pending `DEC-cov-palletto-shipping-ecommerce` both load as `isUnknown: true`, no tier, no confidence |
| 4. Facts, interpretations and defaults are distinguished | every entry's `class`, and the review's "Kind" column |
| 5. A changed material date triggers review of that decision only | `DEC-date-rate-shopping-planning` reopens; every other accepted entry, including the sales commitment on the same feature, stays valid |
| 6. Tier A proceeds while unsupported simulation assumptions are deferred; every accepted choice traces to evidence or an assumption | `tierOutcome`: A and B `proceed`, C and D `deferred` by `DEC-assumption-new-firm-arrival-regional-3pl` and `DEC-assumption-price-sensitivity-ecommerce`; each accepted entry has evidence, or is an `assumption` (the need weight) with its basis in the human evidence |
