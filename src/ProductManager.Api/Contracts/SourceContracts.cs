using System.ComponentModel.DataAnnotations;
using ProductManager.Domain.Imports;

namespace ProductManager.Api.Contracts;

/// <summary>
/// Identity of one record in an external source system, e.g.
/// <c>{"sourceSystem":"salesforce","sourceInstance":"acme.my.salesforce.com","objectKind":"Account","sourceRecordId":"001…"}</c>.
/// Free strings — import metadata, never a list of vendors — compared exactly (case-sensitive,
/// untrimmed). A source record is bound to exactly one domain row per workspace; binding the same
/// key to a second row is refused.
/// </summary>
public sealed record SourceRef(
    [Required, MaxLength(SourceBinding.MaxKeyPartLength)] string SourceSystem,
    [Required, MaxLength(SourceBinding.MaxKeyPartLength)] string SourceInstance,
    [Required, MaxLength(SourceBinding.MaxKeyPartLength)] string ObjectKind,
    [Required, MaxLength(SourceBinding.MaxKeyPartLength)] string SourceRecordId);

/// <summary>
/// One source record and the row it became. <c>targetType</c> is Firm, Opportunity,
/// FirmProductRelationship or FactEvent. <c>importedValuesJson</c> is the source-owned field values
/// as last imported (field → value), which is what local-edit conflicts are detected against.
/// </summary>
public sealed record SourceBindingDto(
    Guid Id,
    Guid WorkspaceId,
    string SourceSystem,
    string SourceInstance,
    string ObjectKind,
    string SourceRecordId,
    string TargetType,
    Guid TargetId,
    DateTimeOffset BoundAt,
    string? ImportedValuesJson);

/// <summary>What an import did with one item.</summary>
public enum ImportOutcome
{
    Created,
    Updated,

    /// <summary>The item matched what is stored; nothing was written.</summary>
    Unchanged,

    /// <summary>Not applied — see the reason. Nothing stored was changed by this item.</summary>
    Exception
}

/// <summary>
/// A source-owned field the import did <em>not</em> apply because it was edited locally since the
/// last import: the stored value differs from <c>lastImportedValue</c>, and the incoming value
/// differs from both (the source moved too). The stored (local) value is kept and
/// <c>lastImportedValue</c> is not advanced, so the conflict recurs on every import until the local
/// value is made equal to the source's. Values are canonical strings
/// (ids as GUIDs, dates as yyyy-MM-dd, money as "1000.00 USD", lists comma-joined), null when empty.
/// </summary>
public sealed record FieldConflictDto(string Field, string? StoredValue, string? IncomingValue, string? LastImportedValue);

/// <summary>Per-relationship result of a bulk firm import, by position in the item's <c>relationships</c>.</summary>
public sealed record RelationshipImportOutcomeDto(
    int Index,
    Guid ProductId,
    Guid? RelationshipId,
    ImportOutcome Outcome,
    string? Reason,
    IReadOnlyList<FieldConflictDto> Conflicts);

/// <summary>
/// Per-item result of a bulk firm import, by position in the request's <c>firms</c>. <c>outcome</c>
/// is about the firm row itself; its relationships report their own. Items without a
/// <c>source</c> report Created or Updated exactly as the counters always have.
/// </summary>
public sealed record FirmImportOutcomeDto(
    int Index,
    string Name,
    SourceRef? Source,
    Guid? FirmId,
    ImportOutcome Outcome,
    string? Reason,
    IReadOnlyList<FieldConflictDto> Conflicts,
    IReadOnlyList<RelationshipImportOutcomeDto> Relationships);

/// <summary>Stable ProblemDetails <c>type</c> values a client can branch on.</summary>
public static class ProblemTypes
{
    /// <summary>
    /// 409: the request restates something already recorded (a decision, a sourced fact) with
    /// different material values. Nothing was changed; the ledger is append-only, so applying it
    /// needs a deliberate correction, not a retry.
    /// </summary>
    public const string CorrectionRequired = "urn:productmanager:problem:correction-required";

    /// <summary>
    /// 409 from <c>POST /api/calibration-runs</c>: a prior the run would fit was informed by observations
    /// the fit would apply again (see <see cref="PriorEvidenceReuseProblem"/>). Nothing was fitted or
    /// stored; narrow the window with <c>observationsRecordedAfter</c> or change the prior's evidence.
    /// </summary>
    public const string PriorEvidenceReuse = "urn:productmanager:problem:prior-evidence-reuse";
}

public static class SourceMapping
{
    public static SourceBindingDto ToDto(this SourceBinding b) =>
        new(b.Id, b.WorkspaceId, b.SourceSystem, b.SourceInstance, b.ObjectKind, b.SourceRecordId, b.TargetType,
            b.TargetId, b.BoundAt, b.ImportedValuesJson);
}
