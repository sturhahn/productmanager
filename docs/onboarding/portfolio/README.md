# Portfolio reconciliation procedure (orchestrator detail)

Read by the orchestrator agent when [`../README.md`](../README.md) section 8 sends you here. It
covers requirements 1-8 of [PRD4](../../../PRDs/PRD4-PortfolioReconciliation.md); the orchestrator
guide holds the hard rules and the ordering. Worked example: [`portfolio-mapping.example.json`](portfolio-mapping.example.json),
derived from the [fixture](fixture/README.md), which walks through every acceptance criterion.

## 1. When it applies and what it needs

**Applies** when Aha! or Jira sources are connected and tier A (datasets 01, 03, 04) is requested.
**Skip it** on the spreadsheet-only path: dataset subagents work from the files as before and the
"Portfolio mapping" slot in their brief says `none`.

Inputs, all from earlier steps (never re-asked):

- the discovery manifest: scoped sources, `authorityByConcept`, `authorityRules`, and the answers to the
  availability and team/unit questions;
- one portfolio question round with the human, defaults proposed: which Aha! product, releases and
  initiatives are the *current* portfolio (an item is approved when it sits on one of those releases);
  how far back a shipped capability is still relevant (default: 12 months before the planning horizon);
  the stale threshold (default 180 days since last update); and who may approve a name.

Record the answers in the mapping `portfolio` and `conventions` blocks. If only one of Aha! and Jira is
connected, run the same steps without the missing side's links: an Aha!-only portfolio yields Features
with no delivering Project (flagged), a Jira-only one yields Projects whose Features the human names.

## 2. Select the portfolio

Ordered procedure. Every candidate record ends in exactly one of three states: selected, excluded, or
held exception.

1. Start from the designated current Aha! portfolio: features on the current releases, plus shipped
   capabilities inside the shipped window. Nothing else in Aha! is a candidate by default.
2. Follow **explicit links** from those features into the agreed Jira projects and read the Aha!
   reference on every Jira epic in those projects. Fetch a linked issue by key even when the discovery
   updated-since filter would have hidden it, and never widen to other Jira projects.
3. Classify each candidate:

| Situation | State |
|---|---|
| Approved current work; relevant shipped capability | selected |
| Explicitly on the approved roadmap but stale | selected, flagged, plus a flagged exception |
| Closed by its own source (Will not do, Won't Do, cancelled) | excluded, with the source status as the reason |
| Tentative (no release or roadmap), unlinked, contradictory links, ambiguous match | held exception |
| Selected but a date, unit, estimate or prerequisite conflicts | selected, plus a flagged exception |

4. **Recency informs review priority, never approval.** A stale item that is explicitly approved is
   kept and ranked high for review; a fresh tentative idea is held and ranked medium. Neither age nor
   activity moves a record between states.
5. Give every selected, excluded and held record a rationale or reason. Nothing is dropped silently.

## 3. Capability and delivery are separate

The domain models the market side and the execution side separately, joined by one seam, `Project
delivers Feature` (see the [product spec](../../PRDs/product_requirements.md), "Domain Model"). Source
records do not line up with that split one to one, so map by role, not by record type:

| Destination | Is | Comes from |
|---|---|---|
| Feature | a capability a customer could use | usually an Aha! feature, but its title may name a release or a delivery; the approved name is the capability |
| Project | delivery work with its own team, dates and effort | usually a Jira epic; an epic is a Project only if it is such work |
| Initiative | a strategic bet | the Aha! initiative |
| Team | a capacity budget unit | a Jira project, once the human confirmed it is a team |
| Business process | a process a capability improves | tags or the human; dataset 02 owns the definition, the name is fixed here |

- **Many to one.** One capability delivered by several epics is one Feature and one Project per
  delivering epic (normally per team), all delivering that Feature. Never a Feature per epic and never a
  Feature named after the Aha! release.
- **One to many.** One source item describing several separately delivered capabilities becomes
  several Features. Ask the human to approve the split; each part gets its own key and shares the
  source reference (role `capability part`). One epic may deliver several Features.
- **The seam** is the pair `relations.deliveredBy` on a Feature and `relations.delivers` on a
  Project. A Feature with no delivering Project is allowed and flagged. A Project that delivers no
  selected Feature is held as an exception.
- Names: use the source title when it names the capability; otherwise propose one and get the human's
  approval (`nameBasis`). Approved names are final: every later artifact uses them verbatim.

## 4. Matching evidence

- An **explicit link** is evidence of association, not proof of equal scope. Read it from both sides and
  compare title, team, release and status before relying on it.
- **Name similarity may only propose.** Write it in `proposedMatches` of a held exception with
  `merge: false`. Never link, merge or rename records on similarity, and never reuse one record's
  name for another.
- **Contradictory links** (a link present on one side only, one epic claiming a capability that lists a
  different epic, two capabilities claiming one epic) are a held exception of kind `contradictoryLink`.
- **Multiple plausible matches** (two epics that could each deliver one capability) are a held exception
  of kind `ambiguousMatch`. Give the human the candidate readings and ask.
- Two similarly named records with no link stay separate until equivalence is established, by a human
  answer or by a link added in the source. Then re-run the mapping; do not patch the seed.

## 5. Dates

Dates carry different meanings. Record each one under its own key on the Feature, with source and
field, and leave it `null` when the sources hold nothing:

| Key | Meaning | Typical source |
|---|---|---|
| `productTarget` | when product intends to release | the Aha! release date |
| `engineeringForecast` | when engineering expects to finish | the Jira due date |
| `actualCompletion` | when delivery work finished | the Jira resolution date |
| `customerAvailability` | when customers can use it | the Aha! release date of a released release, per the availability answer |
| `salesCommitment` | what a customer was promised | a Salesforce commitment field |

1. Write the **date authority rule** in `conventions.dateAuthorityRule` from what the human confirmed.
   Default: planning date from the product-intent authority; execution dates from the execution
   authority; conflicts flagged, never overwritten. The example spells this out as R1-R3.
2. Choose `proposedPlanningDate` by that rule and name its `meaning`. It is never a sales commitment
   and never a Jira resolution date.
3. Compare every other date with it. A sales commitment earlier than the plan, an engineering forecast
   later than the product target, or Done work under an unreleased capability is a flagged exception
   (`dateConflict`, `doneNotReleased`). The exception states its effect on the plan (usually none) and
   the human decides; the planning date does not move.
4. **Shipped means customer availability from the availability authority.** Jira Done makes the
   Project Done and fills `actualCompletion`; the Feature stays Planned with no `shippedDate` until the
   release ships.
5. A simulation applies a Feature's coverage deltas at the end of its delivering Project's roadmap
   item ([dataset 04](../datasets/04-roadmap.md)). So the last Project delivering a capability ends its
   roadmap item on the capability's planning date, not on a Jira resolution date; that is how a Done
   epic avoids fabricating a ship date.

## 6. Effort and capacity units

- Capacity (dataset 01) and effort demand (dataset 03) share one unit, recorded once in
  `conventions` and repeated on every team capacity and every project effort. Take it from the
  answered team/unit question; the monthly capacity numbers come from the human.
- Do not compare or sum story points across teams, and do not infer capacity or effort from ticket
  counts. Effort is compared with capacity only within the same team and unit.
- A missing estimate is a **gap**: status `gap`, a reason, no demand rows. Do not borrow from similar
  epics.
- An estimate in another unit than the convention (hours against points) is also a gap; keep the
  reported value in `reportedAs`. Convert only under a documented, human-reviewed convention that both
  the capacity and the effort dataset use; write it in `conventions.unitConversion`. Without one,
  the value is never converted.
- Delivery work already Done has `complete` effort: no remaining demand.
- Spread an estimate over the project's months as [dataset 03](../datasets/03-initiatives-projects-features.md)
  describes, inside the roadmap window.

## 7. Dependencies

List every dependency edge found between candidates in `dependencies`; drop none.

| Status | Meaning | Handling |
|---|---|---|
| `resolved` | both ends are selected Projects | becomes `dependsOnProjectNames` in dataset 03 |
| `external` | the prerequisite is outside the selected portfolio (another Jira project, a vendor) | keep the edge with `external`, give its owner or `null`; no date is invented; a missing owner is a flagged exception |
| `unresolved` | an end is held out, or the edges form a cycle (`kind: cycle`) | point at the exception that holds it; propose no order |

Check that a dependent Project's planned start is not before its dependency's planned end, or accept
that the roadmap feasibility check flags it.

## 8. Outputs and seed fragments

Produce four outputs and show the human the first, not raw tickets:

1. **Proposed portfolio**: a short readable summary grouped by initiative, one line per Feature and
   per delivering Project (name, status, planning date, flags). Under 30 lines for a real portfolio;
   if it reads like a ticket list, the mapping is at the wrong granularity.
2. **Source-to-destination mapping**: the JSON artifact (section 9).
3. **Exclusions** with a reason each.
4. **Material exceptions**, ranked by `reviewPriority`, each with a question and candidate answers.

Then the dataset subagents for 01, 03 and 04 (and any dataset that names a business process, feature
delta or team) prepare seed fragments **only from the selected records**, with the mapping path in
their brief. Fragment rules:

- Every initiative, project, feature and team name is the approved name, verbatim; a name a subagent
  thinks is wrong goes back to the orchestrator, it is never edited in the fragment.
- Held exceptions and excluded records appear in no fragment. Gaps stay empty: no demand rows, no
  `shippedDate`, no roadmap item without a window.
- Feature `Shipped` only with customer availability; Project status from execution status; roadmap
  items from `roadmapItem`. The fragments in [`fixture/seed/`](fixture/seed) are the worked result;
  they are the roadmap-only variant (no market referenced), so business processes and deltas wait for
  dataset 02.

Before any fragment is loaded, the consequential-decision review in
[guide section 9](../README.md#9-consequential-decision-review-before-any-consequential-record-loads)
applies to the inclusion decisions, exceptions and date conflicts; its procedure is in
[`../review/README.md`](../review/README.md).

## 9. Mapping field reference

Worked example: [`portfolio-mapping.example.json`](portfolio-mapping.example.json). Write your own
outside the repository (it carries instance identity and record IDs) and pass its path to every
subagent.

| Key | Content |
|---|---|
| `manifest` | `file`, `runId`, `sourceIdsUsed`: a reference to the discovery manifest. Instance identity is never repeated; every `link` is relative to that source's instance |
| `portfolio` | the human's designation: product, `currentReleases`, shipped window, agreed Jira projects, horizon, `extractedAt`, extract files |
| `conventions` | `answeredQuestions`, `dateAuthorityRule`, `staleAfterDays`, `effortUnit`, `capacityUnit`, `unitConversion` |
| `records[]` | one per destination entity: `key` (stable, local), `concept` (`initiative`, `project`, `feature`, `team`, `businessProcess`), `name` (approved), `nameBasis`, `decision` (`selected`), `rationale`, `sources[]` (`system`, `id`, `link`, `role`), `relations`, `status`, `flags` |
| `records[]` Feature | `dates` with the five meaning keys plus `proposedPlanningDate` (`value`, `meaning`, `rule`, `conflicts`); `relations.deliveredBy`, `improvesProcesses` |
| `records[]` Project | `relations.initiative`, `team`, `delivers`; `roadmapItem` (`start`, `end`, `basis`) or `null` with `roadmapNote`; `effort` (`status` of `estimated`, `gap`, `complete`; `total`, `unit`, `team`, `basis`, `reportedAs`, `reason`) |
| `records[]` Team | `capacity` (`unit`, `perMonth`, `months`, `source`) |
| `dependencies[]` | `key`, `status`, `from`/`to` (record keys) or `fromSource`/`toSource`, `external` (`id`, `owner`), `kind`, `evidence`, `reason`, `exception` |
| `exclusions[]` | `key`, `sources[]`, `reason` |
| `exceptions[]` | `key`, `kind`, `disposition` (`held`: sources kept out of the seed; `flagged`: selected `recordKeys` kept and reviewed), `sources` or `recordKeys`, `evidence`, `reason`, `question`, `candidates`, `reviewPriority`, `priorityBasis`; held ambiguities add `proposedMatches` and `merge: false` |

Exception kinds used in the example: `ambiguousMatch`, `contradictoryLink`, `tentative`, `dateConflict`,
`doneNotReleased`, `staleInApprovedRoadmap`, `externalPrerequisite`, `incompatibleUnits`,
`missingEstimate`. Add kinds as needed; keep each exception one decision for one human.

Keys that other steps may rely on: `records[].key`, `.concept`, `.name`, `.sources`, `.decision`,
`.relations`; `dependencies[].status`; `exceptions[].key`, `.disposition`.
