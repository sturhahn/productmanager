using ProductManager.Domain.Common;
using ProductManager.Domain.Market;
using ProductManager.Domain.Seam;
using ProductManager.Domain.Simulation;

namespace ProductManager.Domain.Tests;

public class PeriodTests
{
    [Fact]
    public void RejectsInvalidMonths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Period(2026, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Period(2026, 13));
    }

    [Fact]
    public void ComparesChronologically()
    {
        Assert.True(new Period(2026, 12).CompareTo(new Period(2027, 1)) < 0);
        Assert.Equal(new Period(2026, 3), new Period(2026, 3));
    }

    [Fact]
    public void Next_RollsOverYearEnd()
    {
        Assert.Equal(new Period(2027, 1), new Period(2026, 12).Next());
    }

    [Fact]
    public void Range_IsInclusiveAndOrdered()
    {
        var range = Period.Range(new Period(2026, 11), new Period(2027, 2)).ToList();
        Assert.Equal(4, range.Count);
        Assert.Equal(new Period(2026, 11), range.First());
        Assert.Equal(new Period(2027, 2), range.Last());
    }
}

public class MoneyTests
{
    [Fact]
    public void RejectsNegativeAmountsAndBadCurrencies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-1m, "USD"));
        Assert.Throws<ArgumentException>(() => new Money(1m, "DOLLARS"));
        Assert.Throws<ArgumentException>(() => new Money(1m, ""));
    }

    [Fact]
    public void AddingDifferentCurrencies_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new Money(1m, "USD").Add(new Money(1m, "EUR")));
    }

    [Fact]
    public void NormalizesCurrencyCasing_AndAdds()
    {
        Assert.Equal("USD", new Money(1m, "usd").Currency);
        Assert.Equal(3m, new Money(1m, "USD").Add(new Money(2m, "USD")).Amount);
    }
}

public class DistributionTests
{
    [Fact]
    public void PointDistribution_MustHaveZeroWidth()
    {
        Assert.Throws<ArgumentException>(() => new Distribution(DistributionType.Point, 5, 1));
        Assert.Equal(5, Distribution.PointValue(5).ParamA);
    }

    [Fact]
    public void NormalAndLognormal_RejectNegativeScale()
    {
        Assert.Throws<ArgumentException>(() => new Distribution(DistributionType.Normal, 0, -1));
        Assert.Throws<ArgumentException>(() => new Distribution(DistributionType.Lognormal, 0, -0.1));
    }

    [Fact]
    public void Beta_RequiresStrictlyPositiveShapes()
    {
        Assert.Throws<ArgumentException>(() => new Distribution(DistributionType.Beta, 0, 2));
        Assert.Throws<ArgumentException>(() => new Distribution(DistributionType.Beta, 2, 0));
    }
}

public class CoverageTests
{
    [Fact]
    public void Unknown_IsNotBronze_AndHasNoNumericScore()
    {
        var unknown = Coverage.Unknown(Guid.NewGuid(), Guid.NewGuid());
        Assert.True(unknown.IsUnknown);
        Assert.Null(unknown.NumericScore);
        Assert.NotEqual(CoverageTier.Bronze, unknown.Tier);
    }

    [Fact]
    public void TierOrdering_SupportsChoiceMath()
    {
        var gold = Coverage.Assessed(Guid.NewGuid(), Guid.NewGuid(), CoverageTier.Gold);
        var notPlaying = Coverage.Assessed(Guid.NewGuid(), Guid.NewGuid(), CoverageTier.NotPlaying);
        Assert.Equal(1.0, gold.NumericScore);
        Assert.Equal(0.0, notPlaying.NumericScore);
        Assert.True(CoverageTier.Gold > CoverageTier.Silver);
        Assert.True(CoverageTier.Silver > CoverageTier.Bronze);
        Assert.True(CoverageTier.Bronze > CoverageTier.NotPlaying);
    }

    [Fact]
    public void ConfidenceOutsideUnitInterval_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Coverage.Assessed(Guid.NewGuid(), Guid.NewGuid(), CoverageTier.Gold, confidence: 1.5));
    }

    [Fact]
    public void Reassessing_ClearsUnknownState()
    {
        var coverage = Coverage.Unknown(Guid.NewGuid(), Guid.NewGuid());
        coverage.Reassess(CoverageTier.Silver, 0.6);
        Assert.False(coverage.IsUnknown);
        Assert.Equal(CoverageTier.Silver, coverage.Tier);
        Assert.Equal(0.7, coverage.NumericScore);
    }
}

public class FeatureDeltaTests
{
    [Fact]
    public void CoverageDelta_MustRaiseTheTier()
    {
        var feature = new Feature(Guid.NewGuid(), "Importer");
        Assert.Throws<ArgumentException>(() =>
            feature.AddCoverageDelta(Guid.NewGuid(), CoverageTier.Gold, CoverageTier.Bronze));
        Assert.Throws<ArgumentException>(() =>
            feature.AddCoverageDelta(Guid.NewGuid(), CoverageTier.Silver, CoverageTier.Silver));
    }

    [Fact]
    public void CoverageDelta_FromNothingToATier_IsAllowed()
    {
        var feature = new Feature(Guid.NewGuid(), "New capability");
        var delta = feature.AddCoverageDelta(Guid.NewGuid(), fromTier: null, toTier: CoverageTier.Bronze);
        Assert.Equal(FeatureDeltaKind.Coverage, delta.Kind);
        Assert.Single(feature.Deltas);
    }

    [Fact]
    public void NonCoverageDelta_CannotUseTheCoverageKind()
    {
        var feature = new Feature(Guid.NewGuid(), "Importer");
        Assert.Throws<ArgumentException>(() =>
            feature.AddOtherDelta(FeatureDeltaKind.Coverage, magnitude: 0.5));
        var delta = feature.AddOtherDelta(FeatureDeltaKind.SwitchingCost, magnitude: -0.3);
        Assert.Equal(FeatureDeltaKind.SwitchingCost, delta.Kind);
    }
}

public class AssumptionSetTests
{
    [Fact]
    public void Branch_DeepCopiesParameters_AndPointsAtParent()
    {
        var set = new AssumptionSet(Guid.NewGuid(), "Base");
        set.SetParameter("churn.rate", Distribution.PointValue(0.05), ParameterProvenance.Asserted, "gut feel");

        var branch = set.Branch("What if churn is 2x");
        branch.SetParameter("churn.rate", Distribution.PointValue(0.10), ParameterProvenance.Asserted, "stress test");

        Assert.Equal(set.Id, branch.ParentAssumptionSetId);
        Assert.Equal(0.05, set.Parameters.Single().Distribution.ParamA);
        Assert.Equal(0.10, branch.Parameters.Single().Distribution.ParamA);
    }

    [Fact]
    public void SetParameter_UpsertsByKey()
    {
        var set = new AssumptionSet(Guid.NewGuid(), "Base");
        set.SetParameter("k", Distribution.PointValue(1), ParameterProvenance.Default);
        set.SetParameter("k", Distribution.PointValue(2), ParameterProvenance.Calibrated);

        var parameter = Assert.Single(set.Parameters);
        Assert.Equal(2, parameter.Distribution.ParamA);
        Assert.Equal(ParameterProvenance.Calibrated, parameter.Provenance);
    }
}

public class OpportunityLifecycleTests
{
    private static Opportunity Open() => new(Guid.NewGuid(), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void WonDecision_RequiresAWinningProduct()
    {
        var opp = Open();
        Assert.Throws<ArgumentException>(() =>
            opp.Decide(OpportunityStage.Won, DateTimeOffset.UtcNow, winningProductId: null));
    }

    [Fact]
    public void DecidedOpportunity_CannotBeDecidedAgain_OrChangeScope()
    {
        var opp = Open();
        opp.AddToScope(Guid.NewGuid(), opp.OpenedAt);
        opp.Decide(OpportunityStage.Lost, opp.OpenedAt.AddDays(30));

        Assert.Throws<InvalidOperationException>(() => opp.Decide(OpportunityStage.NoDecision, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => opp.AddToScope(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ScopeCanExpandMidCycle_Idempotently()
    {
        var opp = Open();
        var process = Guid.NewGuid();
        opp.AddToScope(process, opp.OpenedAt);
        opp.Qualify(opp.OpenedAt.AddDays(10));
        opp.AddToScope(process, opp.OpenedAt.AddDays(20)); // duplicate ignored
        opp.AddToScope(Guid.NewGuid(), opp.OpenedAt.AddDays(20)); // land-and-expand

        Assert.Equal(2, opp.Scope.Count);
        Assert.Equal(OpportunityStage.Qualified, opp.Stage);
    }

    [Fact]
    public void StageTimestamps_CannotPrecedeOpening()
    {
        var opp = Open();
        Assert.Throws<ArgumentException>(() => opp.Qualify(opp.OpenedAt.AddDays(-1)));
    }
}
