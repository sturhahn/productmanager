namespace ProductManager.Cli.Commands;

/// <summary>
/// Shared "resolve a Guid to a human name, falling back to a short id prefix when the lookup
/// misses" used by both <see cref="NameResolver"/> (the funnel diagnostics table) and
/// <see cref="ParameterKeyTranslator"/> (the calibration diff table) — kept in one place so a
/// lookup miss renders identically everywhere instead of each caller inventing its own fallback.
/// </summary>
internal static class GuidDisplay
{
    /// <summary>Looks <paramref name="id"/> up in <paramref name="names"/>; on a miss, falls back to
    /// a short, parenthesized id prefix rather than the full 36-character Guid.</summary>
    public static string Resolve(IReadOnlyDictionary<Guid, string> names, Guid id) =>
        names.TryGetValue(id, out var name) ? name : Short(id);

    /// <summary>Same as <see cref="Resolve(IReadOnlyDictionary{Guid,string},Guid)"/>, but for a
    /// caller holding a raw id *string* that might not even be a Guid — the firm-level engine's
    /// funnel rows use plain strings for segment/channel ids, and some of those (e.g. "direct") are
    /// already human-readable sentinels rather than database ids. A non-Guid string passes through
    /// unchanged.</summary>
    public static string ResolveOrPassThrough(IReadOnlyDictionary<Guid, string> names, string idOrName) =>
        Guid.TryParse(idOrName, out var id) ? Resolve(names, id) : idOrName;

    private static string Short(Guid id) => $"({id.ToString()[..8]}…)";
}
