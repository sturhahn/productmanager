using ProductManager.Cli.Seed;

namespace ProductManager.Cli.Tests;

public class SeedTallyTests
{
    [Fact]
    public void RecordCreated_and_RecordMatched_accumulate_independently_per_kind()
    {
        var tally = new SeedTally();

        tally.RecordCreated("team");
        tally.RecordCreated("team");
        tally.RecordMatched("team");
        tally.RecordMatched("initiative");

        var snapshot = tally.Snapshot;
        Assert.Equal((2, 1), snapshot["team"]);
        Assert.Equal((0, 1), snapshot["initiative"]);
    }

    [Fact]
    public void Snapshot_has_no_entry_for_a_kind_that_was_never_recorded()
    {
        var tally = new SeedTally();

        Assert.False(tally.Snapshot.ContainsKey("feature"));
    }

    [Fact]
    public void RecordApplied_counts_upserts_separately_from_created_and_matched()
    {
        var tally = new SeedTally();

        tally.RecordApplied("segmentStock");
        tally.RecordApplied("segmentStock");
        tally.RecordApplied("modulePenetration");
        tally.RecordCreated("segmentStock");

        Assert.Equal(2, tally.AppliedSnapshot["segmentStock"]);
        Assert.Equal(1, tally.AppliedSnapshot["modulePenetration"]);
        // The two counters are independent views: an applied upsert is not a create or a match.
        Assert.Equal((1, 0), tally.Snapshot["segmentStock"]);
        Assert.False(tally.Snapshot.ContainsKey("modulePenetration"));
    }

    [Fact]
    public void AppliedSnapshot_is_empty_until_an_upsert_is_recorded()
    {
        var tally = new SeedTally();
        tally.RecordCreated("team");

        Assert.Empty(tally.AppliedSnapshot);
    }
}
