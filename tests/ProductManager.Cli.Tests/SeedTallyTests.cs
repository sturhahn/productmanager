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
}
