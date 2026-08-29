using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;

namespace ProductManager.Cli.Tests;

public class ParseHelpersTests
{
    [Fact]
    public void ParseGuidList_returns_null_when_the_option_was_not_given()
    {
        // Null must round-trip as null (not []) so an update command that omits the option
        // leaves the existing target list untouched rather than clearing it.
        Assert.Null(ParseHelpers.ParseGuidList(null));
    }

    [Fact]
    public void ParseGuidList_returns_an_empty_list_for_an_explicitly_empty_value()
    {
        var result = ParseHelpers.ParseGuidList("");

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseGuidList_parses_comma_separated_ids_and_trims_whitespace()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = ParseHelpers.ParseGuidList($" {a} , {b} ");

        Assert.Equal([a, b], result);
    }

    [Fact]
    public void ParseGuidList_rejects_a_malformed_id_with_a_clear_message()
    {
        var ex = Assert.Throws<ArgumentException>(() => ParseHelpers.ParseGuidList("not-a-guid"));
        Assert.Contains("not-a-guid", ex.Message);
    }

    [Fact]
    public void ParseDate_parses_iso_dates()
    {
        Assert.Equal(new DateOnly(2026, 9, 1), ParseHelpers.ParseDate("2026-09-01", "--start"));
    }

    [Fact]
    public void ParseDate_rejects_an_unparseable_value_naming_the_option()
    {
        var ex = Assert.Throws<ArgumentException>(() => ParseHelpers.ParseDate("not-a-date", "--start"));
        Assert.Contains("--start", ex.Message);
    }

    [Fact]
    public void ParseOptionalDate_returns_null_for_null_input()
    {
        Assert.Null(ParseHelpers.ParseOptionalDate(null, "--start"));
    }

    [Fact]
    public void ParseEnum_is_case_insensitive()
    {
        Assert.Equal(CoverageTier.Gold, ParseHelpers.ParseEnum<CoverageTier>("gold", "--tier"));
        Assert.Equal(CoverageTier.NotPlaying, ParseHelpers.ParseEnum<CoverageTier>("NotPlaying", "--tier"));
    }

    [Fact]
    public void ParseEnum_rejects_an_unknown_value_and_lists_the_allowed_ones()
    {
        var ex = Assert.Throws<ArgumentException>(() => ParseHelpers.ParseEnum<CoverageTier>("Platinum", "--tier"));
        Assert.Contains("Gold", ex.Message);
        Assert.Contains("NotPlaying", ex.Message);
    }

    [Fact]
    public void ParseOptionalEnum_returns_null_for_null_input_without_validating()
    {
        Assert.Null(ParseHelpers.ParseOptionalEnum<ProjectStatus>(null, "--status"));
    }
}
