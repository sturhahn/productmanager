using ProductManager.Domain.Common;

namespace ProductManager.Domain.Imports;

/// <summary>
/// Where an imported record came from: one record in an external system — identified by
/// (<see cref="SourceSystem"/>, <see cref="SourceInstance"/>, <see cref="ObjectKind"/>,
/// <see cref="SourceRecordId"/>) — bound to the one domain row it became. All four parts are free
/// strings: import metadata, never an enum of vendors, so a new source system is data, not schema.
///
/// <para>
/// A source record binds to exactly one target (a unique index per workspace enforces it); several
/// source records may bind to the same target when the importer deliberately consolidates them.
/// Bindings live beside the domain rows rather than on them, so the Engine and calibration never
/// see a source system.
/// </para>
/// </summary>
public sealed class SourceBinding : Entity
{
    public const int MaxKeyPartLength = 200;

    public Guid WorkspaceId { get; private set; }
    public string SourceSystem { get; private set; } = null!;
    public string SourceInstance { get; private set; } = null!;
    public string ObjectKind { get; private set; } = null!;

    /// <summary>The record's id in its source system, verbatim (many systems' ids are case-sensitive).</summary>
    public string SourceRecordId { get; private set; } = null!;

    /// <summary>Type name of the bound row (e.g. "Firm", "Opportunity"), as on <c>FactEvent.SubjectType</c>.</summary>
    public string TargetType { get; private set; } = null!;

    public Guid TargetId { get; private set; }

    /// <summary>
    /// The source-owned field values as last imported (JSON object, field → value). A stored value
    /// that differs from this snapshot was edited locally since, which is how an import tells a
    /// source correction from an overwrite of someone's work. Null until something was imported.
    /// </summary>
    public string? ImportedValuesJson { get; private set; }

    public DateTimeOffset BoundAt { get; private set; }

    private SourceBinding() { } // EF

    public SourceBinding(Guid workspaceId, string sourceSystem, string sourceInstance, string objectKind,
        string sourceRecordId, string targetType, Guid targetId, DateTimeOffset boundAt)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(targetType)) throw new ArgumentException("TargetType is required.", nameof(targetType));
        WorkspaceId = workspaceId;
        SourceSystem = KeyPart(sourceSystem, nameof(sourceSystem));
        SourceInstance = KeyPart(sourceInstance, nameof(sourceInstance));
        ObjectKind = KeyPart(objectKind, nameof(objectKind));
        SourceRecordId = KeyPart(sourceRecordId, nameof(sourceRecordId));
        TargetType = targetType;
        RebindTo(targetId);
        BoundAt = boundAt;
    }

    /// <summary>
    /// Points the binding at a new target — only for a target that no longer exists (deleted, then
    /// re-imported). What was imported into the old target says nothing about the new one.
    /// </summary>
    public void RebindTo(Guid targetId)
    {
        if (targetId == Guid.Empty) throw new ArgumentException("TargetId is required.", nameof(targetId));
        TargetId = targetId;
        ImportedValuesJson = null;
    }

    public void RecordImportedValues(string? importedValuesJson) => ImportedValuesJson = importedValuesJson;

    private static string KeyPart(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
        if (value.Length > MaxKeyPartLength)
            throw new ArgumentException($"{name} is longer than {MaxKeyPartLength} characters.", name);
        return value;
    }
}
