using ProductManager.Domain.Common;

namespace ProductManager.Domain.Market;

/// <summary>
/// The central hinge: a firm seeking coverage for a scoped set of BusinessProcesses.
/// Scope is dynamic (land-and-expand). Lifecycle: open → qualified → decided (won/lost/no-decision)
/// with stage timestamps. Cross-sell = an opportunity scoped to new processes at an existing client.
/// </summary>
public sealed class Opportunity : Entity
{
    private readonly List<OpportunityScopeItem> _scope = [];

    public Guid FirmId { get; private set; }
    public Guid? ChannelId { get; set; }
    public OpportunityStage Stage { get; private set; } = OpportunityStage.Open;

    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? QualifiedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Competitor ids present in the deal.</summary>
    public List<Guid> CompetitorIds { get; private set; } = [];

    /// <summary>The product that won (ours or a competitor's), when decided Won.</summary>
    public Guid? WinningProductId { get; private set; }

    /// <summary>
    /// The module being sold when this is a cross-sell — an opportunity scoped to new processes at
    /// an existing client. Non-null *is* the cross-sell flag: cross-sell needs no special case in
    /// the model, only a pointer at which module it was selling, because that is what calibration
    /// keys a CrossSellRate on.
    /// </summary>
    public Guid? CrossSellModuleProductId { get; private set; }

    public string? Notes { get; set; }

    public IReadOnlyCollection<OpportunityScopeItem> Scope => _scope.AsReadOnly();

    private Opportunity() { } // EF

    public Opportunity(Guid firmId, DateTimeOffset openedAt, Guid? channelId = null,
        Guid? crossSellModuleProductId = null)
    {
        if (firmId == Guid.Empty) throw new ArgumentException("FirmId is required.", nameof(firmId));
        FirmId = firmId;
        OpenedAt = openedAt;
        ChannelId = channelId;
        CrossSellModuleProductId = crossSellModuleProductId;
    }

    public bool IsDecided => Stage is OpportunityStage.Won or OpportunityStage.Lost or OpportunityStage.NoDecision;

    /// <summary>True when this opportunity sells new processes into an existing client relationship.</summary>
    public bool IsCrossSell => CrossSellModuleProductId is not null;

    /// <summary>Replaces the competitors present in the deal (deduplicated).</summary>
    public void SetCompetitors(IEnumerable<Guid> competitorIds)
    {
        ArgumentNullException.ThrowIfNull(competitorIds);
        CompetitorIds = competitorIds.Where(c => c != Guid.Empty).Distinct().ToList();
    }

    /// <summary>Points the opportunity at the module it cross-sells (or clears it). Not after a decision.</summary>
    public void SetCrossSellModule(Guid? moduleProductId)
    {
        if (IsDecided)
            throw new DomainConflictException("Cannot change the cross-sell module of a decided opportunity.");
        CrossSellModuleProductId = moduleProductId;
    }

    /// <summary>Adds a business process to the scope (idempotent). Scope can expand mid-cycle until decided.</summary>
    public void AddToScope(Guid businessProcessId, DateTimeOffset addedAt)
    {
        if (businessProcessId == Guid.Empty)
            throw new ArgumentException("BusinessProcessId is required.", nameof(businessProcessId));
        if (IsDecided)
            throw new DomainConflictException("Cannot change the scope of a decided opportunity.");
        if (_scope.All(s => s.BusinessProcessId != businessProcessId))
            _scope.Add(new OpportunityScopeItem(businessProcessId, addedAt));
    }

    /// <summary>Drops a business process from the scope. False when it was not in scope.</summary>
    public bool RemoveFromScope(Guid businessProcessId)
    {
        if (IsDecided)
            throw new DomainConflictException("Cannot change the scope of a decided opportunity.");
        return _scope.RemoveAll(s => s.BusinessProcessId == businessProcessId) > 0;
    }

    /// <summary>
    /// Qualifies an open opportunity. Repeating a qualification already applied at the same instant
    /// is a no-op returning false — a retried import must not fail on its own earlier success; any
    /// other qualification of a non-open opportunity is a conflict.
    /// </summary>
    public bool Qualify(DateTimeOffset at)
    {
        if (QualifiedAt is { } qualified && SameInstant(qualified, at)) return false;
        if (Stage != OpportunityStage.Open)
            throw new DomainConflictException($"Only an open opportunity can be qualified (stage is {Stage}).");
        if (at < OpenedAt)
            throw new ArgumentException("Qualification cannot precede opening.");
        Stage = OpportunityStage.Qualified;
        QualifiedAt = at;
        return true;
    }

    /// <summary>True when this opportunity is already decided with exactly this outcome, winner and date.</summary>
    public bool IsSameDecision(OpportunityStage outcome, DateTimeOffset at, Guid? winningProductId) =>
        IsDecided && Stage == outcome && WinningProductId == winningProductId
        && DecidedAt is { } decided && SameInstant(decided, at);

    /// <summary>
    /// Decides the opportunity. Repeating the identical decision is a no-op returning false; a
    /// different decision on a decided opportunity is a conflict — changing a recorded outcome is a
    /// correction of history, not a transition.
    /// </summary>
    public bool Decide(OpportunityStage outcome, DateTimeOffset at, Guid? winningProductId = null)
    {
        if (outcome is not (OpportunityStage.Won or OpportunityStage.Lost or OpportunityStage.NoDecision))
            throw new ArgumentException("Decision outcome must be Won, Lost, or NoDecision.", nameof(outcome));
        if (IsSameDecision(outcome, at, winningProductId)) return false;
        if (IsDecided)
            throw new DomainConflictException("Opportunity is already decided.");
        if (at < OpenedAt)
            throw new ArgumentException("Decision cannot precede opening.");
        if (outcome == OpportunityStage.Won && winningProductId is null)
            throw new ArgumentException("A won opportunity must record the winning product.");
        Stage = outcome;
        DecidedAt = at;
        WinningProductId = winningProductId;
        return true;
    }

    /// <summary>Equal to the microsecond: the resolution Postgres stores, so a stored instant still matches its retry.</summary>
    public static bool SameInstant(DateTimeOffset a, DateTimeOffset b) => a.UtcTicks / 10 == b.UtcTicks / 10;
}

/// <summary>One business process in an opportunity's scope, with when it entered. Owned by Opportunity.</summary>
public sealed class OpportunityScopeItem
{
    public Guid BusinessProcessId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    private OpportunityScopeItem() { } // EF

    public OpportunityScopeItem(Guid businessProcessId, DateTimeOffset addedAt)
    {
        BusinessProcessId = businessProcessId;
        AddedAt = addedAt;
    }
}

/// <summary>
/// Typed shock modulating model terms for a duration. Real events live in the facts ledger;
/// hypothetical events can be injected into any scenario.
/// </summary>
public sealed class MarketEvent : Entity
{
    private readonly List<MarketEventRateMultiplier> _rateMultipliers = [];

    public Guid MarketId { get; private set; }
    public MarketEventType Type { get; private set; }
    public string Name { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    /// <summary>Effect size (units defined by the assumption it modulates; data, not schema).</summary>
    public double? Magnitude { get; set; }

    /// <summary>Hypothetical events can be injected into scenarios; real ones mirror the facts ledger.</summary>
    public bool IsHypothetical { get; private set; }

    /// <summary>Who the event concerns (e.g. a competitor), when applicable.</summary>
    public Guid? CompetitorId { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// How this event modulates simulated flows while it is active. Overlapping events multiply.
    /// </summary>
    public IReadOnlyCollection<MarketEventRateMultiplier> RateMultipliers => _rateMultipliers.AsReadOnly();

    private MarketEvent() { } // EF

    public MarketEvent(Guid marketId, MarketEventType type, string name, DateOnly startDate,
        bool isHypothetical, DateOnly? endDate = null)
    {
        if (marketId == Guid.Empty) throw new ArgumentException("MarketId is required.", nameof(marketId));
        MarketId = marketId;
        Type = type;
        Rename(name);
        Reschedule(startDate, endDate);
        IsHypothetical = isHypothetical;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Event name is required.", nameof(name));
        Name = name.Trim();
    }

    public void Reschedule(DateOnly startDate, DateOnly? endDate)
    {
        if (endDate.HasValue && endDate.Value < startDate)
            throw new ArgumentException("Event end cannot precede its start.");
        StartDate = startDate;
        EndDate = endDate;
    }

    public void SetType(MarketEventType type) => Type = type;

    public void SetHypothetical(bool isHypothetical) => IsHypothetical = isHypothetical;

    /// <summary>
    /// Replaces the whole multiplier set. Wholesale replacement, because the set is the event's
    /// effect — editing one row of it in isolation has no meaning.
    /// </summary>
    public void ReplaceRateMultipliers(IEnumerable<MarketEventRateMultiplier> multipliers)
    {
        ArgumentNullException.ThrowIfNull(multipliers);
        _rateMultipliers.Clear();
        _rateMultipliers.AddRange(multipliers);
    }
}

/// <summary>
/// One multiplier an event applies to a simulated flow while active. A null segment means every
/// segment. Owned by <see cref="MarketEvent"/>.
/// </summary>
public sealed class MarketEventRateMultiplier
{
    /// <summary>Null = the multiplier applies to every segment.</summary>
    public Guid? ClientSegmentId { get; private set; }

    public SimulationFlowKind FlowKind { get; private set; }

    /// <summary>Non-negative scale factor on the flow's rate; 1.0 is no effect.</summary>
    public double Multiplier { get; private set; }

    private MarketEventRateMultiplier() { } // EF

    public MarketEventRateMultiplier(Guid? clientSegmentId, SimulationFlowKind flowKind, double multiplier)
    {
        if (multiplier < 0 || double.IsNaN(multiplier))
            throw new ArgumentOutOfRangeException(nameof(multiplier), "A rate multiplier must be a non-negative number.");
        ClientSegmentId = clientSegmentId;
        FlowKind = flowKind;
        Multiplier = multiplier;
    }
}

/// <summary>
/// Named external time series (e.g. an equity index). Observed history lives in the facts
/// ledger; future paths are scenario specs sampled per MC run (Phase 2+).
/// </summary>
public sealed class Covariate : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; set; }
    public string? Unit { get; set; }

    /// <summary>Starting level of the future geometric-Brownian-motion path; null = no stochastic future spec.</summary>
    public double? GbmStart { get; private set; }

    /// <summary>Per-month log drift of the future path.</summary>
    public double? GbmDrift { get; private set; }

    /// <summary>Per-month log volatility of the future path; sampled independently per MC path.</summary>
    public double? GbmVol { get; private set; }

    /// <summary>True when this covariate carries a complete stochastic future-path specification.</summary>
    public bool HasGbmSpec => GbmStart is not null && GbmDrift is not null && GbmVol is not null;

    private Covariate() { } // EF

    public Covariate(Guid workspaceId, string name, string? unit = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        Rename(name);
        WorkspaceId = workspaceId;
        Unit = unit;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Covariate name is required.", nameof(name));
        Name = name.Trim();
    }

    /// <summary>
    /// Sets (or clears, with all-null arguments) the stochastic future path. All three numbers are
    /// set together or not at all: a half-specified process is not a process.
    /// </summary>
    public void SetGbmSpec(double? start, double? drift, double? vol)
    {
        var given = new[] { start, drift, vol }.Count(v => v is not null);
        if (given is not (0 or 3))
            throw new ArgumentException("A covariate future path needs gbmStart, gbmDrift and gbmVol together, or none of them.");
        if (vol is < 0)
            throw new ArgumentOutOfRangeException(nameof(vol), "Volatility cannot be negative.");
        GbmStart = start;
        GbmDrift = drift;
        GbmVol = vol;
    }
}
