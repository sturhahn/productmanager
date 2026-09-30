using ProductManager.Domain.Common;
using ProductManager.Domain.Simulation;

namespace ProductManager.Domain.Tests;

/// <summary>
/// PRD8 requirement 3: a prior records which observations informed it, so a fit cannot apply the
/// same evidence twice. Null evidence means unknown; these tests pin the shape of a known one.
/// </summary>
public class PriorEvidenceTests
{
    private static readonly DateOnly Jan = new(2025, 1, 1);
    private static readonly DateOnly Dec = new(2025, 12, 31);

    [Fact]
    public void An_independent_prior_carries_no_observation_period()
    {
        var evidence = PriorEvidence.Independent("Industry benchmark, flat Beta(1,1)");
        Assert.Equal(PriorEvidenceBasis.Independent, evidence.Basis);
        Assert.Null(evidence.ObservedFrom);
        Assert.Null(evidence.RecordedThrough);

        Assert.Throws<ArgumentException>(() =>
            new PriorEvidence(PriorEvidenceBasis.Independent, Jan, Dec));
        Assert.Throws<ArgumentException>(() =>
            new PriorEvidence(PriorEvidenceBasis.Independent, recordedThrough: Dec));
        Assert.Throws<ArgumentException>(() =>
            new PriorEvidence(PriorEvidenceBasis.Independent, sourceCalibrationRunId: Guid.NewGuid()));
    }

    [Fact]
    public void An_observation_prior_needs_a_period_or_a_recorded_cutoff()
    {
        Assert.Throws<ArgumentException>(() => new PriorEvidence(PriorEvidenceBasis.Observations));

        var byPeriod = new PriorEvidence(PriorEvidenceBasis.Observations, Jan, Dec, population: "2025 decided deals");
        Assert.Equal((Jan, Dec), (byPeriod.ObservedFrom!.Value, byPeriod.ObservedThrough!.Value));

        var byCutoff = new PriorEvidence(PriorEvidenceBasis.Observations, recordedThrough: Dec);
        Assert.Equal(Dec, byCutoff.RecordedThrough);
    }

    [Fact]
    public void The_observation_period_is_ordered_and_given_whole()
    {
        Assert.Throws<ArgumentException>(() => new PriorEvidence(PriorEvidenceBasis.Observations, Dec, Jan));
        Assert.Throws<ArgumentException>(() => new PriorEvidence(PriorEvidenceBasis.Observations, Jan));
        Assert.Throws<ArgumentException>(() =>
            new PriorEvidence(PriorEvidenceBasis.Observations, observedThrough: Dec, recordedThrough: Dec));

        var singleDay = new PriorEvidence(PriorEvidenceBasis.Observations, Jan, Jan);
        Assert.Equal(singleDay.ObservedFrom, singleDay.ObservedThrough);
    }

    [Fact]
    public void A_source_run_implies_the_recorded_cutoff_it_read()
    {
        Assert.Throws<ArgumentException>(() =>
            new PriorEvidence(PriorEvidenceBasis.Observations, Jan, Dec, sourceCalibrationRunId: Guid.NewGuid()));
    }

    [Fact]
    public void A_fit_stamps_observations_with_its_run_and_cutoff()
    {
        var runId = Guid.NewGuid();
        var stamp = PriorEvidence.FromCalibration(runId, recordedThrough: Dec, Jan, new DateOnly(2025, 6, 30),
            "10 decided opportunities");

        Assert.Equal(PriorEvidenceBasis.Observations, stamp.Basis);
        Assert.Equal(runId, stamp.SourceCalibrationRunId);
        Assert.Equal(Dec, stamp.RecordedThrough);
        Assert.Equal(Jan, stamp.ObservedFrom);
    }

    [Fact]
    public void Branch_carries_prior_evidence_as_an_independent_copy()
    {
        var set = new AssumptionSet(Guid.NewGuid(), "Base");
        var segment = Guid.NewGuid();
        var evidence = new PriorEvidence(PriorEvidenceBasis.Observations, Jan, Dec, population: "2025 deals");
        set.SetEngineParameter(segment, SimulationFlowKind.WinRate, null,
            new Distribution(DistributionType.Beta, 9, 21), ParameterProvenance.Asserted, priorEvidence: evidence);
        set.SetParameter("choice.switching-cost-base", Distribution.PointValue(0.5), ParameterProvenance.Asserted,
            priorEvidence: PriorEvidence.Independent());
        set.SetParameter("free-form", Distribution.PointValue(1), ParameterProvenance.Asserted);

        var branch = set.Branch("Scenario");

        var copied = branch.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate).PriorEvidence;
        Assert.Equal(evidence, copied);
        Assert.NotSame(evidence, copied);
        Assert.Equal(PriorEvidenceBasis.Independent,
            branch.Parameters.Single(p => p.Key == "choice.switching-cost-base").PriorEvidence!.Basis);
        Assert.Null(branch.Parameters.Single(p => p.Key == "free-form").PriorEvidence);
    }

    [Fact]
    public void A_calibration_run_window_must_start_before_its_as_of_date()
    {
        var asOf = new DateOnly(2026, 6, 30);
        Assert.Throws<ArgumentException>(() => new CalibrationRun(Guid.NewGuid(), asOf, Guid.NewGuid(), null, asOf));

        var run = new CalibrationRun(Guid.NewGuid(), asOf, Guid.NewGuid(), null, Dec);
        Assert.Equal(Dec, run.ObservationsRecordedAfter);
    }
}
