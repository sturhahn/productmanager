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

    public string? Notes { get; set; }

    public IReadOnlyCollection<OpportunityScopeItem> Scope => _scope.AsReadOnly();

    private Opportunity() { } // EF

    public Opportunity(Guid firmId, DateTimeOffset openedAt, Guid? channelId = null)
    {
        if (firmId == Guid.Empty) throw new ArgumentException("FirmId is required.", nameof(firmId));
        FirmId = firmId;
        OpenedAt = openedAt;
        ChannelId = channelId;
    }

    public bool IsDecided => Stage is OpportunityStage.Won or OpportunityStage.Lost or OpportunityStage.NoDecision;

    /// <summary>Adds a business process to the scope (idempotent). Scope can expand mid-cycle until decided.</summary>
    public void AddToScope(Guid businessProcessId, DateTimeOffset addedAt)
    {
        if (businessProcessId == Guid.Empty)
            throw new ArgumentException("BusinessProcessId is required.", nameof(businessProcessId));
        if (IsDecided)
            throw new InvalidOperationException("Cannot change the scope of a decided opportunity.");
        if (_scope.All(s => s.BusinessProcessId != businessProcessId))
            _scope.Add(new OpportunityScopeItem(businessProcessId, addedAt));
    }

    public void Qualify(DateTimeOffset at)
    {
        if (Stage != OpportunityStage.Open)
            throw new InvalidOperationException($"Only an open opportunity can be qualified (stage is {Stage}).");
        if (at < OpenedAt)
            throw new ArgumentException("Qualification cannot precede opening.");
        Stage = OpportunityStage.Qualified;
        QualifiedAt = at;
    }

    public void Decide(OpportunityStage outcome, DateTimeOffset at, Guid? winningProductId = null)
    {
        if (outcome is not (OpportunityStage.Won or OpportunityStage.Lost or OpportunityStage.NoDecision))
            throw new ArgumentException("Decision outcome must be Won, Lost, or NoDecision.", nameof(outcome));
        if (IsDecided)
            throw new InvalidOperationException("Opportunity is already decided.");
        if (at < OpenedAt)
            throw new ArgumentException("Decision cannot precede opening.");
        if (outcome == OpportunityStage.Won && winningProductId is null)
            throw new ArgumentException("A won opportunity must record the winning product.");
        Stage = outcome;
        DecidedAt = at;
        WinningProductId = winningProductId;
    }
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

    private MarketEvent() { } // EF

    public MarketEvent(Guid marketId, MarketEventType type, string name, DateOnly startDate,
        bool isHypothetical, DateOnly? endDate = null)
    {
        if (marketId == Guid.Empty) throw new ArgumentException("MarketId is required.", nameof(marketId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Event name is required.", nameof(name));
        if (endDate.HasValue && endDate.Value < startDate)
            throw new ArgumentException("Event end cannot precede its start.");
        MarketId = marketId;
        Type = type;
        Name = name.Trim();
        StartDate = startDate;
        EndDate = endDate;
        IsHypothetical = isHypothetical;
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
}
