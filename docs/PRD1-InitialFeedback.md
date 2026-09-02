# Product Manager Feedback

## Channels

Right now it's very difficult to understand what the weights mean on the channels.  Perhaps we should show some sort of statistics on:
- How many opportunities are generated from the channel
- A walk for the coming months to show when we would first see the opportunities
- A sales channel may also need a cycle time between opportunity creation and close (or loss)

## General UI
1) On some of the screens the rows in a table are very wide, with all the action buttons on the far right.  On a laptop, this makes it hard to see which buttons align with which rows.  Is there a design pattern that handles this?

## Roadmap
1) You can't resize the first column and it's too narrow to be able to see the full names of the initiatives.
2) It would be great to be able to view individual items by clicking on them.  Right now clicking drags them, I don't want to remove that so we need to be careful, but perhaps a view button that pulls up a modal?  What are good design patterns here?  I don't think the [x] if it actually deletes is a good idea, an project/initiative is far too large an item for one click to accidentally delete it or even assign it out of scope. 
3) We should be able to see the near-term roadmap in a different granularity from the long-term roadmap.  This should be a property of the roadmap view (different views could have different choices).  For the seed data, we should do the next remainder of this month and the next full month weekly, the remaining full quarter monthly, and then quarterly beyond.

## Layout
1) Currently, you edit competitor's products on the products page.  I think it would be better if we used the same sort of control, but had a competitor page where all their products appeared.  The Products page should be confined to our products.

## Seed data
Great job creating seed data for this, but let's make a few tweaks:
1) There should be a direct sales channel.  We need to think about whether we should include forms of this (or how these would work).  For example, we can have direct sales that originate from:
    - Reverse inquiry
    - Direct outreach (inside sales)
    - Marketing campaigns
    - Targeted outreach campaigns (i.e. We are going to target feature X to 30 firms that match some criteria.)
2) I see an initiative to upgrade FIX, I think that's a "table stakes" sort of upgrade in this market.

## The purpose of Initiatives
1) Initiatives should always have a goal, we have that in a basic way now, but a good goal will also have measurable KPIs.  Some of the KPIs may be things that don't directly impact simulations, but some might (i.e. increasing conversion rate, shortening implementation cycles, etc. could have impact on the financial simulation).  I would like the ability to link some KPIs to the future where appropriate.

## Firms: Competitors and Systems
1) If a firm is a current client, its systems are its current technology landscape.  This would include partner systems, systems we didn't replace, etc.  
2) If a firm is in an opportunity, we should focus on the competitors but also the incumbent system.  Meaning, we should evaluate the switching costs vs. the incumbent, but compare feature-wise against all other systems in the competition.
3) Clicking on a Firm should open a CRM-like record where you can see their current technology landscape, Opportunity history, stats on their revenue, usage, etc. if a clinet.  We may have to include some seed data in this.
4) We should also be able to filter the Firm list by presets, including current clients, prospects, current systems in use (multiselect)

## Pipeline
- We should be able to filter the "Decided" by date
- It would be good to have some seed data that is in the Open and Qualified stages
- Clicking on an opportunity should open a CRM-like record where you can see the details of the opportunity, including the firm, the sales rep, the stage, the probability, the value, etc.  This should especially include the competition for the opportunity, needs identified, and the other parts pertinent to our framework.

## Channels
I don't really understand the units of the numbers at the moment.  Is there a way to show some sort of impact or graphic of what happens when you change these numbers?  What is the sensitivity?  Can we show the implication for opportunities/yr with the numbers to make them more intuitive?

## Calibration
- The calibration is very technical at the moment, and it's not clear what everything does or what units it's in.
- A few examples:
  - New firm arrival is set as a point (without a rate) with its calibration value set to a mu and a sigma.  Instead of using the negative mu as the calibration value, it would be more intuitive to have the calibration expressed in the same units as the output and converted in the middle if necessary.
  - Also to pick on new firm arrival, it needs a time period associated with it.
  - Win rate is set to a Beta with seemingly integers for alpha and beta.  But it's not clear what the beta is to?  Or whether the result is a high number a low number?

Calibrated numbers should always have units.  The explanation of the calculation and data use on the tooltip is good, but an explanation of what it measures and what it's used for would also be helpful.

## Runs
- Really like this page, but a few comments
- Instead of showing part of a GUID to identify a run, if it's named use that, if it's not named use the timestamp it was run for display purposes
- In a simulation run on firms, I'm not sure how you can generate .1 of an opportunity?  Feels like it should exist or not...
- It would be really cool to be able to view this page by a segmentation of the data, as in I'd like to filter it to a particular channel then look at the data compared by product.  The filters and the breakout should both be configurable.  Put some thought into this one, it's a key feature to get right.  We should make sure the results are easy to see, also perhaps with pie graphs to show breakdowns, etc.  

## Errors

Witnessed the following errors:
```
## Error Type
Console Error

## Error Message

Base UI: A component that acts as a button expected a native <button> because the `nativeButton` prop is true. Rendering a non-<button> removes native button semantics, which can impact forms and accessibility. Use a real <button> in the `render` prop, or set `nativeButton` to `false`.
CalibrationResult@http://localhost:3000/_next/static/chunks/src_0hjgs0c._.js:766:252
CalibrationPage@http://localhost:3000/_next/static/chunks/src_0hjgs0c._.js:121:259


    at CalibrationResult (src/app/(app)/calibration/page.tsx:464:11)
    at CalibrationPage (src/app/(app)/calibration/page.tsx:126:15)

## Code Frame
462 |         </div>
463 |         {run.fittedAssumptionSetId && (
> 464 |           <Button
|           ^
465 |             variant="outline"
466 |             size="sm"
467 |             render={

Next.js version: 16.3.3 (Turbopack)
```
