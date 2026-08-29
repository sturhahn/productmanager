using ProductManager.Cli.Seed;

namespace ProductManager.Cli.Tests;

/// <summary>
/// NameIndex is the core of idempotent seed loading: "match existing entities by name before
/// creating." These tests pin down the matching rules directly, without any HTTP involved.
/// </summary>
public class NameIndexTests
{
    [Fact]
    public void TryGet_finds_an_exact_match()
    {
        var index = new NameIndex([("Platform Engineering", Guid.Parse("11111111-1111-1111-1111-111111111111"))]);

        var found = index.TryGet("Platform Engineering", out var id);

        Assert.True(found);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), id);
    }

    [Fact]
    public void TryGet_is_case_insensitive()
    {
        var id = Guid.NewGuid();
        var index = new NameIndex([("Platform Engineering", id)]);

        Assert.True(index.TryGet("PLATFORM ENGINEERING", out var found));
        Assert.Equal(id, found);
    }

    [Fact]
    public void TryGet_trims_surrounding_whitespace_on_both_sides()
    {
        var id = Guid.NewGuid();
        var index = new NameIndex([("  Platform Engineering  ", id)]);

        Assert.True(index.TryGet("Platform Engineering", out var found));
        Assert.Equal(id, found);
    }

    [Fact]
    public void TryGet_returns_false_for_an_unknown_name()
    {
        var index = new NameIndex([]);

        Assert.False(index.TryGet("Nonexistent Team", out _));
    }

    [Fact]
    public void Constructor_keeps_the_first_id_when_the_source_has_duplicate_names()
    {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var index = new NameIndex([("Duplicate", firstId), ("Duplicate", secondId)]);

        Assert.True(index.TryGet("Duplicate", out var found));
        Assert.Equal(firstId, found);
    }

    [Fact]
    public void Add_registers_a_freshly_created_entity_for_later_lookups_in_the_same_run()
    {
        var index = new NameIndex([]);
        var id = Guid.NewGuid();

        index.Add("New Team", id);

        Assert.True(index.TryGet("new team", out var found));
        Assert.Equal(id, found);
    }

    [Fact]
    public void Add_overwrites_a_previous_entry_for_the_same_normalized_name()
    {
        var original = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        var index = new NameIndex([("Team", original)]);

        index.Add("  team  ", replacement);

        Assert.True(index.TryGet("Team", out var found));
        Assert.Equal(replacement, found);
    }

    [Fact]
    public void GetOrThrow_returns_the_id_when_present()
    {
        var id = Guid.NewGuid();
        var index = new NameIndex([("Team", id)]);

        Assert.Equal(id, index.GetOrThrow("Team"));
    }

    [Fact]
    public void GetOrThrow_throws_a_SeedReferenceException_naming_the_missing_reference()
    {
        var index = new NameIndex([]);

        var ex = Assert.Throws<SeedReferenceException>(() => index.GetOrThrow("Ghost Project"));
        Assert.Contains("Ghost Project", ex.Message);
    }

    [Fact]
    public void Count_reflects_deduplicated_entries()
    {
        var index = new NameIndex([("A", Guid.NewGuid()), ("a", Guid.NewGuid()), ("B", Guid.NewGuid())]);

        Assert.Equal(2, index.Count);
    }
}
