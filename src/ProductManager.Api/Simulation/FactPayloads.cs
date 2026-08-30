using ProductManager.Domain.Common;

namespace ProductManager.Api.Simulation;

/// <summary>
/// The payload of a <see cref="FactEventKind.OpportunityDecided"/> ledger entry.
///
/// <para>
/// It is deliberately <b>self-contained</b>: segment, channel, dates, scope and winner are copied
/// into the fact rather than referenced through the opportunity row. A calibration "as of date D"
/// is only honest if it reads what was recorded by D — re-reading today's mutable opportunity (its
/// scope may have expanded, its firm may have been re-segmented) would quietly leak the present
/// into the past.
/// </para>
///
/// <para>
/// <see cref="Outcome"/> is already from our perspective: a deal won by a competitor's product is
/// recorded as <c>Lost</c>, because who "won" is a fact about the deal but what matters to the fit
/// is whether we did.
/// </para>
/// </summary>
public sealed record OpportunityDecisionFact(
    Guid OpportunityId,
    Guid FirmId,
    Guid? ClientSegmentId,
    Guid? ChannelId,
    DateOnly OpenedDate,
    DateOnly DecidedDate,
    string Outcome,
    IReadOnlyList<Guid> ScopeProcessIds,
    Guid? WinningProductId,
    bool WinnerIsOurs,
    bool IsCrossSell,
    Guid? CrossSellModuleProductId)
{
    /// <summary>Outcome value recorded when we won the deal.</summary>
    public const string WonOutcome = "Won";

    /// <summary>Outcome value recorded when a decision was made and we did not win it.</summary>
    public const string LostOutcome = "Lost";

    /// <summary>Outcome value recorded when the firm decided not to decide.</summary>
    public const string NoDecisionOutcome = "NoDecision";
}

/// <summary>
/// The payload of a <see cref="FactEventKind.SegmentArrivalObserved"/> ledger entry: one month's
/// observed arrivals for a segment. <c>NewFirmArrival</c> is an absolute monthly intensity
/// (exposure 1); <c>ConversionToMarket</c> is a per-prospect rate, so its exposure is that month's
/// prospect-pool size.
/// </summary>
public sealed record SegmentArrivalFact(
    Guid ClientSegmentId,
    SimulationFlowKind FlowKind,
    int Count,
    double Exposure = 1.0);

/// <summary>
/// The payload of a <see cref="FactEventKind.Churn"/> ledger entry. The segment is carried in the
/// payload for the same reason opportunity decisions carry theirs — a firm re-segmented later must
/// not move a churn that already happened.
/// </summary>
public sealed record ChurnFact(Guid ClientSegmentId, Guid? FirmId = null, Guid? ProductId = null);
