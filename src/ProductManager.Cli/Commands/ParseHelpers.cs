using System.Globalization;

namespace ProductManager.Cli.Commands;

/// <summary>Small parsing helpers for option values System.CommandLine's built-in binder does not
/// natively convert (dates, enums, comma-separated id lists) — kept explicit so parse failures
/// produce a clear message instead of a raw exception.</summary>
/// <summary>Public (rather than internal) so it can be unit-tested directly without exposing the
/// rest of the command-wiring internals.</summary>
public static class ParseHelpers
{
    public static DateOnly ParseDate(string value, string optionName)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        throw new ArgumentException($"'{value}' is not a valid date for {optionName} (expected yyyy-MM-dd).");
    }

    public static DateOnly? ParseOptionalDate(string? value, string optionName) =>
        value is null ? null : ParseDate(value, optionName);

    public static TEnum ParseEnum<TEnum>(string value, string optionName) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) && Enum.IsDefined(result))
            return result;
        var allowed = string.Join(", ", Enum.GetNames<TEnum>());
        throw new ArgumentException($"'{value}' is not a valid {optionName} (expected one of: {allowed}).");
    }

    public static TEnum? ParseOptionalEnum<TEnum>(string? value, string optionName) where TEnum : struct, Enum =>
        value is null ? null : ParseEnum<TEnum>(value, optionName);

    /// <summary>
    /// Parses a comma-separated id list option. Null (the option was not given at all) is
    /// preserved as null so update commands leave the field untouched rather than wiping it —
    /// pass an empty string explicitly to clear it.
    /// </summary>
    public static IReadOnlyList<Guid>? ParseGuidList(string? value)
    {
        if (value is null) return null;
        if (value.Trim().Length == 0) return [];
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => ParseGuid(token, "id list"))
            .ToList();
    }

    public static Guid ParseGuid(string value, string optionName)
    {
        if (Guid.TryParse(value, out var id)) return id;
        throw new ArgumentException($"'{value}' is not a valid id for {optionName}.");
    }
}
