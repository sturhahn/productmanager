using ProductManager.Api.Contracts;
using ProductManager.Domain.Common;
using ProductManager.Domain.Simulation;
using ProductManager.Engine;
using ProductManager.Engine.Calibration;

namespace ProductManager.Api.Simulation;

/// <summary>
/// Which observations of a calibration input apply to one parameter, and whether a prior's own
/// evidence overlaps them (PRD8 requirement 3). Pure functions over the calibration contract; the
/// selection rules mirror <see cref="CalibrationEngine"/>'s per-flow fits exactly, so the counts here
/// are the counts the engine used.
/// </summary>
public static class CalibrationEvidence
{
    /// <summary>An inclusive effective-date period.</summary>
    public sealed record Period(DateOnly From, DateOnly Through);

    /// <summary>
    /// One parameter the calibration can fit: a flow (<see cref="Flow"/> set) or a sales cycle
    /// (<see cref="Flow"/> null, keyed by its mu key). Ids are the calibration contract's strings.
    /// </summary>
    public sealed record Target(string Key, FlowKind? Flow, string SegmentId, string? ModuleId, string? ProductId);

    /// <summary>
    /// The observations one parameter's fit reads. <see cref="Observations"/> is the denominator count in
    /// the parameter's own units (decided deals, churn events, decided cross-sells, observed months,
    /// decided deals with dates); <see cref="Exposure"/> client-months or exposure-months;
    /// <see cref="From"/>/<see cref="Through"/> the effective dates covered.
    /// </summary>
    public sealed record Summary(
        int Observations, int Successes, int Failures, int Excluded, double Exposure, DateOnly? From, DateOnly? Through);

    public static Target ForFlow(string segmentId, FlowKind flow, string? moduleId) => new(
        Guid.TryParse(segmentId, out var segment)
            ? Parameter.EngineKey(CalibrationInputBuilder.ToDomainFlow(flow), segment, Guid.TryParse(moduleId, out var m) ? m : null)
            : $"{flow}:{segmentId}",
        flow, segmentId, moduleId, null);

    public static Target ForSalesCycle(string segmentId, string? productId) => new(
        Guid.TryParse(segmentId, out var segment)
            ? ChoiceParameterKeys.SalesCycleMu(segment, Guid.TryParse(productId, out var p) ? p : null)
            : $"{ChoiceParameterKeys.SalesCycleMuPrefix}{segmentId}",
        null, segmentId, null, productId);

    /// <summary>Every parameter the output moved, flows first then sales cycles.</summary>
    public static IEnumerable<Target> Fitted(CalibrationOutput output) =>
        output.FittedFlows.Select(f => ForFlow(f.SegmentId, f.FlowKind, f.ModuleId))
            .Concat(output.FittedSalesCycles.Select(c => ForSalesCycle(c.SegmentId, c.ProductId)));

    /// <summary>
    /// The observations <paramref name="target"/>'s fit reads from <paramref name="input"/>, optionally only
    /// those effective inside <paramref name="within"/> (exposure: only the client-months inside it).
    /// </summary>
    public static Summary Summarize(CalibrationInput input, Target target, Period? within = null)
    {
        int observations = 0, successes = 0, failures = 0, excluded = 0;
        double exposure = 0;
        DateOnly? from = null, through = null;

        void Seen(DateOnly first, DateOnly last)
        {
            if (from is null || first < from) from = first;
            if (through is null || last > through) through = last;
        }

        bool Inside(DateOnly d) => within is null || (d >= within.From && d <= within.Through);
        bool Segment(string s) => string.Equals(s, target.SegmentId, StringComparison.Ordinal);

        switch (target.Flow)
        {
            case FlowKind.WinRate:
                foreach (var opp in input.Opportunities)
                {
                    if (opp.IsCrossSell || opp.Outcome is not { } outcome || !Segment(opp.SegmentId)) continue;
                    var d = opp.DecidedDate ?? opp.OpenedDate;
                    if (!Inside(d)) continue;
                    if (outcome == OpportunityResult.NoDecision) { excluded++; continue; }
                    if (outcome == OpportunityResult.Won) successes++; else failures++;
                    observations++;
                    Seen(d, d);
                }
                break;

            case FlowKind.ChurnHazard:
                foreach (var churn in input.ChurnEvents)
                {
                    if (!Segment(churn.SegmentId) || !Inside(churn.EffectiveDate)) continue;
                    successes++;
                    observations++;
                    Seen(churn.EffectiveDate, churn.EffectiveDate);
                }
                exposure = SpanExposure(input, target.SegmentId, within, Seen);
                break;

            case FlowKind.CrossSellRate:
                foreach (var opp in input.Opportunities)
                {
                    if (!opp.IsCrossSell || opp.Outcome is not { } outcome || !Segment(opp.SegmentId)) continue;
                    if (!string.Equals(opp.ModuleId, target.ModuleId, StringComparison.Ordinal)) continue;
                    var d = opp.DecidedDate ?? opp.OpenedDate;
                    if (!Inside(d)) continue;
                    observations++; // the engine's sample size counts every decided cross-sell
                    if (outcome == OpportunityResult.Won) successes++;
                    else if (outcome == OpportunityResult.Lost) failures++;
                    else excluded++;
                    Seen(d, d);
                }
                exposure = SpanExposure(input, target.SegmentId, within, Seen);
                break;

            case FlowKind.NewFirmArrival or FlowKind.ConversionToMarket:
                foreach (var row in input.ArrivalCounts)
                {
                    if (row.FlowKind != target.Flow || !Segment(row.SegmentId)) continue;
                    var monthEnd = row.Month.AddMonths(1).AddDays(-1);
                    if (within is not null && (row.Month > within.Through || monthEnd < within.From)) continue;
                    observations++;
                    successes += row.Count;
                    exposure += row.Exposure;
                    Seen(row.Month, monthEnd);
                }
                break;

            case null: // sales cycle: every decided opportunity with dates; product-specific = won by it
                foreach (var opp in input.Opportunities)
                {
                    if (opp.Outcome is null || opp.DecidedDate is not { } d || !Segment(opp.SegmentId)) continue;
                    if (target.ProductId is not null
                        && !string.Equals(opp.WinnerProductId, target.ProductId, StringComparison.Ordinal)) continue;
                    if (!Inside(d)) continue;
                    observations++;
                    Seen(d, d);
                }
                break;
        }

        return new Summary(observations, successes, failures, excluded, exposure, from, through);
    }

    /// <summary>Client-months of one segment, cut exactly as the engine cuts them, optionally inside a period.</summary>
    private static double SpanExposure(CalibrationInput input, string segmentId, Period? within, Action<DateOnly, DateOnly> seen)
    {
        double months = 0;
        foreach (var span in input.RelationshipSpans)
        {
            if (!string.Equals(span.SegmentId, segmentId, StringComparison.Ordinal)) continue;
            if (span.StartDate >= input.AsOfDate) continue;
            var start = span.StartDate;
            var end = span.EndDate is { } e && e < input.AsOfDate ? e : input.AsOfDate;
            if (within is not null)
            {
                if (within.From > start) start = within.From;
                var periodEnd = within.Through.AddDays(1);
                if (periodEnd < end) end = periodEnd;
            }
            int days = end.DayNumber - start.DayNumber;
            if (days <= 0) continue;
            months += days / CalibrationEngine.DaysPerMonth;
            seen(start, end);
        }
        return months;
    }

    /// <summary>
    /// The evidence-reuse check, run on the dry-run output before anything is stored. For each prior the
    /// engine would move, compares the prior's evidence with the observations this fit applies to it. Both
    /// rules run whenever their field is present (a fitted prior can carry an inherited period and a cut-off):
    /// <list type="bullet">
    /// <item>no evidence (unknown) or <c>Independent</c>: no overlap (unknown is a readiness issue instead);</item>
    /// <item><c>RecordedThrough</c> R: disjoint only when the fit's window starts at or after R
    /// (<c>ObservationsRecordedAfter &gt;= R</c>); otherwise the overlap is what the same fit would read from
    /// facts recorded on or before R, with exposure cut at R;</item>
    /// <item>an observation period [from, through]: the fit's observations effective inside it, and for
    /// exposure-denominated flows its client-months inside it. Exposure is half-open by day ([start, end)), so a
    /// window starting on <c>through</c> still covers that day; the first disjoint window is <c>through</c> + 1 day.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<PriorEvidenceOverlapDto> FindOverlaps(CalibrationWorld world, CalibrationInput input,
        CalibrationOutput output)
    {
        var priorByKey = world.Prior.Parameters.ToDictionary(p => p.Key, StringComparer.Ordinal);
        var overlaps = new List<PriorEvidenceOverlapDto>();

        foreach (var target in Fitted(output))
        {
            if (!priorByKey.TryGetValue(target.Key, out var parameter)
                || parameter.PriorEvidence is not { Basis: PriorEvidenceBasis.Observations } evidence) continue;

            if (evidence is { ObservedFrom: { } from, ObservedThrough: { } through })
                Add(PriorEvidenceRule.ObservedPeriod, Summarize(input, target, new Period(from, through)));

            if (evidence.RecordedThrough is { } recordedThrough && !(world.ObservationsRecordedAfter >= recordedThrough))
            {
                var cut = new DateTimeOffset(recordedThrough.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var known = world with
                {
                    Facts = world.Facts.Where(f => f.RecordedDate < cut).ToList(),
                    AsOfDate = recordedThrough < world.AsOfDate ? recordedThrough : world.AsOfDate
                };
                Add(PriorEvidenceRule.RecordedThrough, Summarize(CalibrationInputBuilder.Build(known), target));
            }

            void Add(PriorEvidenceRule rule, Summary overlap)
            {
                bool exposureFlow = target.Flow is FlowKind.ChurnHazard or FlowKind.CrossSellRate;
                if (overlap.Observations == 0 && !(exposureFlow && overlap.Exposure > 0)) return;
                overlaps.Add(new PriorEvidenceOverlapDto(
                    target.Key,
                    target.Flow is { } f ? CalibrationInputBuilder.ToDomainFlow(f) : null,
                    Guid.TryParse(target.SegmentId, out var s) ? s : null,
                    Guid.TryParse(target.ModuleId, out var m) ? m : null,
                    Guid.TryParse(target.ProductId, out var p) ? p : null,
                    PriorEvidenceDto.From(evidence)!,
                    overlap.From,
                    overlap.Through,
                    overlap.Observations,
                    exposureFlow ? overlap.Exposure : null,
                    Summarize(input, target).Observations,
                    rule));
            }
        }

        return overlaps;
    }

    /// <summary>
    /// The prior evidence each fitted parameter carries after this run: <c>Observations</c>, recorded through
    /// the as-of date, stamped with the run. The observation period is <em>inherited verbatim</em> from the
    /// prior it updated (null when the chain started Independent or unknown), so a hand-declared period keeps
    /// being checked by the period rule on every later fit. The effective dates this fit itself read go into
    /// the population text only: they are covered by the recorded cut-off, and widening the period with them
    /// would make a hazard's period reach the as-of day and overlap the next window.
    /// </summary>
    public static IReadOnlyDictionary<string, PriorEvidence> Stamps(Guid runId, CalibrationWorld world,
        CalibrationInput input, CalibrationOutput output)
    {
        var priorByKey = world.Prior.Parameters.ToDictionary(p => p.Key, StringComparer.Ordinal);
        var stamps = new Dictionary<string, PriorEvidence>(StringComparer.Ordinal);

        foreach (var target in Fitted(output))
        {
            var used = Summarize(input, target);
            var previous = priorByKey.GetValueOrDefault(target.Key)?.PriorEvidence;

            string window = world.ObservationsRecordedAfter is { } after
                ? $"recorded after {after:yyyy-MM-dd} through {world.AsOfDate:yyyy-MM-dd}"
                : $"recorded through {world.AsOfDate:yyyy-MM-dd}";
            string effective = used.From is { } f && used.Through is { } t ? $", effective {f:yyyy-MM-dd}..{t:yyyy-MM-dd}," : "";
            string before = previous switch
            {
                null => " Evidence behind the prior it updated: unknown.",
                { Population: { } population } => $" Prior: {population}",
                _ => ""
            };
            string text = $"Calibration fit on {used.Observations} {Noun(target.Flow)}{effective} {window}.{before}";
            stamps[target.Key] = PriorEvidence.FromCalibration(runId, world.AsOfDate,
                previous?.ObservedFrom, previous?.ObservedThrough, text.Length <= 500 ? text : text[..499] + "…");
        }

        return stamps;
    }

    private static string Noun(FlowKind? flow) => flow switch
    {
        FlowKind.WinRate => "decided opportunities",
        FlowKind.ChurnHazard => "churn events",
        FlowKind.CrossSellRate => "decided cross-sell opportunities",
        FlowKind.NewFirmArrival or FlowKind.ConversionToMarket => "observed months",
        _ => "decided opportunities with dates"
    };

    /// <summary>Units of a parameter, in words.</summary>
    public static string Units(FlowKind? flow) => flow switch
    {
        FlowKind.WinRate => "probability per decided opportunity",
        FlowKind.ChurnHazard => "probability per client-month",
        FlowKind.CrossSellRate => "probability per client-month",
        FlowKind.NewFirmArrival => "firms per month",
        FlowKind.ConversionToMarket => "conversions per prospect per month",
        _ => "log-months (lognormal mu of the sales-cycle duration)"
    };

    /// <summary>What the denominator counts, in words.</summary>
    public static string Denominator(FlowKind? flow) => flow switch
    {
        FlowKind.WinRate =>
            "decided new-business opportunities (won + lost); no-decision outcomes and cross-sells excluded",
        FlowKind.ChurnHazard =>
            "client-months: every relationship of ours in the segment (base products and modules alike, one per relationship) " +
            "from acquisition to its end or the as-of date; numerator = churn events",
        FlowKind.CrossSellRate =>
            "client-months as for ChurnHazard; numerator = won cross-sell opportunities for the module; observations = " +
            "its decided cross-sell opportunities (won, lost and no-decision; no-decision counted in excluded)",
        FlowKind.NewFirmArrival => "observed months (exposure 1 each; an observed zero counts, a missing month does not)",
        FlowKind.ConversionToMarket => "prospect-months: the sum of each observed month's prospect pool",
        _ => "decided opportunities with dates (won, lost and no-decision; product-specific: won by that product)"
    };
}
