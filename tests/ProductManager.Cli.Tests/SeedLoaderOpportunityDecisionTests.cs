using ProductManager.Cli.Seed;

namespace ProductManager.Cli.Tests;

/// <summary>
/// PL2: seed opportunities may be left undecided (DecidedDate/Outcome null) to represent live
/// Open/Qualified pipeline entries. SeedLoader.LoadOpportunitiesAsync must not push those through
/// the API's decide call — before the fix it called ParseHelpers.ParseDate(seedOpportunity.
/// DecidedDate, ...) unconditionally, which throws ArgumentException on a null DecidedDate.
/// HasDecision is the pure guard that prevents that; these tests pin its behavior directly since
/// LoadOpportunitiesAsync itself needs a live API client to exercise.
/// </summary>
public class SeedLoaderOpportunityDecisionTests
{
    [Fact]
    public void HasDecision_is_false_for_an_open_or_qualified_opportunity_with_no_decidedDate()
    {
        var opportunity = new SeedOpportunity
        {
            FirmName = "Acme",
            OpenedDate = "2026-01-01",
            QualifiedDate = "2026-02-01",
            DecidedDate = null,
            Outcome = null,
        };

        Assert.False(SeedLoader.HasDecision(opportunity));
    }

    [Fact]
    public void HasDecision_is_true_for_a_decided_opportunity()
    {
        var opportunity = new SeedOpportunity
        {
            FirmName = "Acme",
            OpenedDate = "2026-01-01",
            DecidedDate = "2026-03-01",
            Outcome = "Won",
        };

        Assert.True(SeedLoader.HasDecision(opportunity));
    }
}
