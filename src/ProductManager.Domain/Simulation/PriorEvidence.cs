using ProductManager.Domain.Common;

namespace ProductManager.Domain.Simulation;

/// <summary>
/// Which observations informed a prior, so a calibration can refuse to apply the same evidence
/// twice (PRD8 requirement 3). Metadata about provenance, never an engine input. A parameter
/// without one has <em>unknown</em> evidence.
///
/// <para>Two ways to describe <see cref="PriorEvidenceBasis.Observations"/>, checked by different rules:</para>
/// <list type="bullet">
/// <item><see cref="RecordedThrough"/>: derived from the ledger as recorded through that date (what a
/// fit stamps). A later fit is disjoint only if it reads facts recorded after it.</item>
/// <item><see cref="ObservedFrom"/>..<see cref="ObservedThrough"/> without a recorded cut-off: derived
/// from outcomes effective in that period (a hand-declared prior). A later fit is disjoint only if
/// none of its observations for the parameter is effective inside it.</item>
/// </list>
/// </summary>
public sealed record PriorEvidence
{
    public PriorEvidenceBasis Basis { get; }

    /// <summary>First day of the observation period (effective dates), inclusive.</summary>
    public DateOnly? ObservedFrom { get; }

    /// <summary>Last day of the observation period (effective dates), inclusive.</summary>
    public DateOnly? ObservedThrough { get; }

    /// <summary>The prior used ledger facts recorded on or before this date.</summary>
    public DateOnly? RecordedThrough { get; }

    /// <summary>Free text: which population the observations came from ("2025 decided deals, EU mid-market").</summary>
    public string? Population { get; }

    /// <summary>The calibration run that produced this prior, when it is a fitted posterior.</summary>
    public Guid? SourceCalibrationRunId { get; }

    private PriorEvidence() { } // EF

    public PriorEvidence(
        PriorEvidenceBasis basis,
        DateOnly? observedFrom = null,
        DateOnly? observedThrough = null,
        DateOnly? recordedThrough = null,
        string? population = null,
        Guid? sourceCalibrationRunId = null)
    {
        if ((observedFrom is null) != (observedThrough is null))
            throw new ArgumentException("An observation period needs both observedFrom and observedThrough.");
        if (observedFrom > observedThrough)
            throw new ArgumentException("observedFrom must not be after observedThrough.");
        if (sourceCalibrationRunId is not null && recordedThrough is null)
            throw new ArgumentException("A prior produced by a calibration run needs the recorded cut-off that run read.");

        if (basis == PriorEvidenceBasis.Independent)
        {
            if (observedFrom is not null || recordedThrough is not null || sourceCalibrationRunId is not null)
                throw new ArgumentException(
                    "An independent prior was not derived from observations: it has no observation period, " +
                    "recorded cut-off or source run.");
        }
        else if (observedFrom is null && recordedThrough is null)
        {
            throw new ArgumentException(
                "A prior derived from observations needs the period it observed (observedFrom/observedThrough) " +
                "or the recorded cut-off of the ledger it read (recordedThrough).");
        }

        Basis = basis;
        ObservedFrom = observedFrom;
        ObservedThrough = observedThrough;
        RecordedThrough = recordedThrough;
        Population = string.IsNullOrWhiteSpace(population) ? null : population.Trim();
        SourceCalibrationRunId = sourceCalibrationRunId;
    }

    public static PriorEvidence Independent(string? population = null) => new(PriorEvidenceBasis.Independent, population: population);

    /// <summary>The stamp a fit puts on a parameter it moved: everything it read was recorded through <paramref name="recordedThrough"/>.</summary>
    public static PriorEvidence FromCalibration(Guid runId, DateOnly recordedThrough, DateOnly? observedFrom,
        DateOnly? observedThrough, string? population) =>
        new(PriorEvidenceBasis.Observations, observedFrom, observedThrough, recordedThrough, population, runId);

    /// <summary>An owned value is stored with its owner, so a branch needs its own instance.</summary>
    public PriorEvidence Copy() => new(Basis, ObservedFrom, ObservedThrough, RecordedThrough, Population, SourceCalibrationRunId);
}
