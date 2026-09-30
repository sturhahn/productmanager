using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Common;
using ProductManager.Domain.Imports;

namespace ProductManager.Api.Infrastructure;

/// <summary>Finding and writing source bindings, shared by every import path.</summary>
internal static class SourceBindingStore
{
    public static bool Matches(this SourceBinding b, SourceRef s) =>
        b.SourceSystem == s.SourceSystem && b.SourceInstance == s.SourceInstance
        && b.ObjectKind == s.ObjectKind && b.SourceRecordId == s.SourceRecordId;

    public static Task<SourceBinding?> FindBindingAsync(this AppDbContext db, Guid workspaceId, SourceRef s,
        CancellationToken ct) =>
        db.SourceBindings.FirstOrDefaultAsync(b => b.WorkspaceId == workspaceId
                                                   && b.SourceSystem == s.SourceSystem
                                                   && b.SourceInstance == s.SourceInstance
                                                   && b.ObjectKind == s.ObjectKind
                                                   && b.SourceRecordId == s.SourceRecordId, ct);

    /// <summary>
    /// Binds a source record to a target: re-points the record's stale binding (its target was
    /// deleted) rather than adding a second row the unique index would refuse.
    /// </summary>
    public static SourceBinding Bind(this AppDbContext db, SourceBinding? stale, Guid workspaceId, SourceRef s,
        string targetType, Guid targetId)
    {
        if (stale is not null)
        {
            stale.RebindTo(targetId);
            return stale;
        }

        var binding = new SourceBinding(workspaceId, s.SourceSystem, s.SourceInstance, s.ObjectKind, s.SourceRecordId,
            targetType, targetId, DateTimeOffset.UtcNow);
        db.SourceBindings.Add(binding);
        return binding;
    }
}

/// <summary>
/// Canonical strings for source-owned field values — what is compared, snapshotted on the binding
/// and shown in a conflict. Stable across providers: Postgres rounds money to cents and rewrites
/// jsonb, so both are compared by value, not by text.
/// </summary>
internal static class ImportValues
{
    public static string? Of(string? value) => value;
    public static string? Of(Guid? value) => value?.ToString();
    public static string? Of(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string? Of(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    public static string? Of(EpistemicStatus value) => value.ToString();

    public static string? Of(Money? value) =>
        value is null ? null : $"{value.Amount.ToString("F2", CultureInfo.InvariantCulture)} {value.Currency}";

    /// <summary>Order-insensitive; an empty list reads as null ("nothing decomposed").</summary>
    public static string? Of(IEnumerable<Guid> value)
    {
        var ids = value.Distinct().Order().ToList();
        return ids.Count == 0 ? null : string.Join(",", ids);
    }

    /// <summary>JSON documents equal by value (property order, whitespace and number spelling aside).</summary>
    public static bool SameJson(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b);
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));
        }
        catch (JsonException)
        {
            return a == b;
        }
    }
}

/// <summary>
/// Partial update of one source-bound row's source-owned fields, one field at a time. An absent
/// field keeps the stored value; a present (or explicitly cleared) one is applied — unless the
/// stored value was edited locally since the last import through this binding (stored ≠ last
/// imported), in which case the local value is kept and a conflict is reported. Nothing is written
/// to the row here: <see cref="Merge{T}"/> returns the value to store, so the caller can still
/// refuse the whole item before touching anything.
/// </summary>
internal sealed class FieldMerge(string? lastImportedJson)
{
    /// <summary>Null when nothing was imported through this binding yet (new or adopting binding): no conflicts possible.</summary>
    private readonly Dictionary<string, string?>? _lastImported = lastImportedJson is null
        ? null
        : JsonSerializer.Deserialize<Dictionary<string, string?>>(lastImportedJson);

    private readonly Dictionary<string, string?> _imported = lastImportedJson is null
        ? []
        : JsonSerializer.Deserialize<Dictionary<string, string?>>(lastImportedJson) ?? [];

    public List<FieldConflictDto> Conflicts { get; } = [];

    /// <summary>True when at least one stored value changes.</summary>
    public bool Changed { get; private set; }

    /// <summary>The snapshot to record on the binding(s) afterwards.</summary>
    public string ImportedJson => JsonSerializer.Serialize(_imported);

    public T Merge<T>(string field, T stored, bool present, T incoming, Func<T, string?> canonical, bool json = false)
    {
        if (!present) return stored;
        string? storedValue = canonical(stored), incomingValue = canonical(incoming);
        if (Same(storedValue, incomingValue, json))
        {
            _imported[field] = incomingValue;
            return stored;
        }

        if (_lastImported is not null && _lastImported.TryGetValue(field, out var last) && !Same(storedValue, last, json))
        {
            Conflicts.Add(new FieldConflictDto(field, storedValue, incomingValue, last));
            return stored;
        }

        _imported[field] = incomingValue;
        Changed = true;
        return incoming;
    }

    private static bool Same(string? a, string? b, bool json) => json ? ImportValues.SameJson(a, b) : a == b;
}
