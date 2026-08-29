using ProductManager.Domain.Common;

namespace ProductManager.Domain.Facts;

/// <summary>
/// Bi-temporal, append-only market observation: client won/lost, churn, competitor shipped,
/// pricing changed, incident, covariate observation. EffectiveDate = when it happened in the
/// world; RecordedDate = when we learned it — so calibration can honestly reconstruct
/// "what we knew as of date D". Immutable after creation; never updated or deleted.
/// </summary>
public sealed class FactEvent : Entity
{
    public Guid WorkspaceId { get; private set; }
    public FactEventKind Kind { get; private set; }

    /// <summary>When the fact was true in the world.</summary>
    public DateOnly EffectiveDate { get; private set; }

    /// <summary>When the fact entered our ledger.</summary>
    public DateTimeOffset RecordedDate { get; private set; }

    /// <summary>Type name of the primary subject entity (e.g. "Firm", "Covariate"), if any.</summary>
    public string? SubjectType { get; private set; }

    public Guid? SubjectId { get; private set; }

    /// <summary>Structured payload (JSON) — e.g. the observed covariate value, the deal size. Data, not schema.</summary>
    public string? PayloadJson { get; private set; }

    public string? Notes { get; private set; }

    private FactEvent() { } // EF

    public FactEvent(Guid workspaceId, FactEventKind kind, DateOnly effectiveDate, DateTimeOffset recordedDate,
        string? subjectType = null, Guid? subjectId = null, string? payloadJson = null, string? notes = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        WorkspaceId = workspaceId;
        Kind = kind;
        EffectiveDate = effectiveDate;
        RecordedDate = recordedDate;
        SubjectType = subjectType;
        SubjectId = subjectId;
        PayloadJson = payloadJson;
        Notes = notes;
    }
}
