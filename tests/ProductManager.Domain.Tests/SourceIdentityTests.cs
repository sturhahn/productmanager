using ProductManager.Domain.Common;
using ProductManager.Domain.Imports;
using ProductManager.Domain.Market;

namespace ProductManager.Domain.Tests;

/// <summary>
/// PRD6: a source record binds to exactly one target, and repeating a lifecycle transition an
/// import already applied (a retry after a lost response) is a no-op rather than a second event.
/// </summary>
public class SourceBindingTests
{
    private static readonly Guid Workspace = Guid.NewGuid();

    [Theory]
    [InlineData("", "prod", "Account", "001")]
    [InlineData("crm", " ", "Account", "001")]
    [InlineData("crm", "prod", "", "001")]
    [InlineData("crm", "prod", "Account", "")]
    public void Every_key_part_is_required(string system, string instance, string kind, string recordId)
    {
        Assert.Throws<ArgumentException>(() =>
            new SourceBinding(Workspace, system, instance, kind, recordId, "Firm", Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Key_parts_are_bounded_and_kept_verbatim()
    {
        var tooLong = new string('x', SourceBinding.MaxKeyPartLength + 1);
        Assert.Throws<ArgumentException>(() =>
            new SourceBinding(Workspace, "crm", "prod", "Account", tooLong, "Firm", Guid.NewGuid(), DateTimeOffset.UtcNow));

        // Source ids are case-sensitive in many systems: never normalised.
        var binding = new SourceBinding(Workspace, "crm", "prod", "Account", "001aB", "Firm", Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        Assert.Equal("001aB", binding.SourceRecordId);
    }

    [Fact]
    public void A_binding_needs_a_workspace_and_a_target()
    {
        Assert.Throws<ArgumentException>(() =>
            new SourceBinding(Guid.Empty, "crm", "prod", "Account", "001", "Firm", Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() =>
            new SourceBinding(Workspace, "crm", "prod", "Account", "001", "Firm", Guid.Empty, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() =>
            new SourceBinding(Workspace, "crm", "prod", "Account", "001", " ", Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rebinding_points_the_one_binding_at_a_new_target_and_forgets_the_old_import()
    {
        var binding = new SourceBinding(Workspace, "crm", "prod", "Account", "001", "Firm", Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        binding.RecordImportedValues("{\"name\":\"Acme\"}");

        var replacement = Guid.NewGuid();
        binding.RebindTo(replacement);

        Assert.Equal(replacement, binding.TargetId);
        Assert.Null(binding.ImportedValuesJson);
    }
}

public class OpportunityRepeatSafetyTests
{
    private static readonly DateTimeOffset Opened = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static Opportunity Open() => new(Guid.NewGuid(), Opened);

    [Fact]
    public void Repeating_the_same_qualification_is_a_no_op()
    {
        var opp = Open();
        Assert.True(opp.Qualify(Opened.AddDays(3)));
        Assert.False(opp.Qualify(Opened.AddDays(3)));
        Assert.Equal(Opened.AddDays(3), opp.QualifiedAt);
    }

    [Fact]
    public void A_different_qualification_date_is_still_a_conflict()
    {
        var opp = Open();
        opp.Qualify(Opened.AddDays(3));
        Assert.Throws<DomainConflictException>(() => opp.Qualify(Opened.AddDays(4)));
    }

    [Fact]
    public void Repeating_the_same_qualification_after_the_decision_is_still_a_no_op()
    {
        var opp = Open();
        opp.Qualify(Opened.AddDays(3));
        opp.Decide(OpportunityStage.Lost, Opened.AddDays(30));
        Assert.False(opp.Qualify(Opened.AddDays(3)));
        Assert.Equal(OpportunityStage.Lost, opp.Stage);
    }

    [Fact]
    public void Repeating_the_same_decision_is_a_no_op()
    {
        var opp = Open();
        var product = Guid.NewGuid();
        Assert.True(opp.Decide(OpportunityStage.Won, Opened.AddDays(30), product));
        Assert.False(opp.Decide(OpportunityStage.Won, Opened.AddDays(30), product));
        Assert.Equal(OpportunityStage.Won, opp.Stage);
    }

    [Fact]
    public void Sub_microsecond_differences_are_the_same_instant()
    {
        // Postgres keeps microseconds; a retry of a tick-precise instant must still read as identical.
        var opp = Open();
        var at = Opened.AddDays(30).AddTicks(1234567);
        opp.Decide(OpportunityStage.Lost, at);
        Assert.True(opp.IsSameDecision(OpportunityStage.Lost, at.AddTicks(-7), null));
    }

    [Fact]
    public void A_changed_outcome_winner_or_date_is_not_the_same_decision()
    {
        var opp = Open();
        var product = Guid.NewGuid();
        opp.Decide(OpportunityStage.Won, Opened.AddDays(30), product);

        Assert.True(opp.IsSameDecision(OpportunityStage.Won, Opened.AddDays(30), product));
        Assert.False(opp.IsSameDecision(OpportunityStage.Lost, Opened.AddDays(30), null));
        Assert.False(opp.IsSameDecision(OpportunityStage.Won, Opened.AddDays(30), Guid.NewGuid()));
        Assert.False(opp.IsSameDecision(OpportunityStage.Won, Opened.AddDays(31), product));
        Assert.Throws<DomainConflictException>(() => opp.Decide(OpportunityStage.Lost, Opened.AddDays(30)));
    }

    [Fact]
    public void An_undecided_opportunity_has_no_decision_to_match()
    {
        Assert.False(Open().IsSameDecision(OpportunityStage.Lost, Opened, null));
    }
}

public class RelationshipEpisodeTests
{
    private static FirmProductRelationship Edge(DateOnly? acquired, DateOnly? ended)
    {
        var r = new FirmProductRelationship(Guid.NewGuid(), Guid.NewGuid(), acquired);
        if (ended is { } end) r.End(end);
        return r;
    }

    [Fact]
    public void Overlapping_or_undated_spans_describe_the_same_episode()
    {
        var edge = Edge(new DateOnly(2020, 1, 1), new DateOnly(2023, 6, 30));
        Assert.True(edge.DescribesSameEpisode(new DateOnly(2020, 1, 1), null));
        Assert.True(edge.DescribesSameEpisode(new DateOnly(2021, 5, 1), new DateOnly(2023, 1, 1)));
        Assert.True(edge.DescribesSameEpisode(null, null));
        Assert.True(Edge(null, null).DescribesSameEpisode(new DateOnly(2024, 1, 1), null));
    }

    [Fact]
    public void A_span_starting_after_the_stored_end_or_ending_before_the_stored_start_is_another_episode()
    {
        var edge = Edge(new DateOnly(2020, 1, 1), new DateOnly(2023, 6, 30));
        Assert.False(edge.DescribesSameEpisode(new DateOnly(2024, 2, 1), null));
        Assert.False(edge.DescribesSameEpisode(new DateOnly(2018, 1, 1), new DateOnly(2019, 1, 1)));
    }
}
