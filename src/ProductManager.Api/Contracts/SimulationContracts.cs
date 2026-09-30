using System.ComponentModel.DataAnnotations;
using ProductManager.Domain.Common;
using ProductManager.Domain.Simulation;

namespace ProductManager.Api.Contracts;

// ---------- Distributions ----------

/// <summary>
/// A parameter distribution: a family plus two numbers.
/// Point: p1 = the value, p2 must be 0. Normal: p1 = mean, p2 = std dev.
/// Lognormal: p1 = mu, p2 = sigma (both on the log scale). Beta: p1 = alpha, p2 = beta.
/// </summary>
public sealed record DistributionSpecDto(DistributionType Type, double P1, double P2 = 0)
{
    public Distribution ToDomain() => new(Type, P1, P2);

    public static DistributionSpecDto From(Distribution d) => new(d.Type, d.ParamA, d.ParamB);
}

// ---------- AssumptionSet / Parameter ----------

public sealed record ParameterDto(
    Guid Id,
    Guid AssumptionSetId,
    string Key,
    DistributionSpecDto Distribution,
    ParameterProvenance Provenance,
    string? Rationale,
    Guid? ClientSegmentId,
    SimulationFlowKind? FlowKind,
    Guid? ModuleProductId,
    Guid? CovariateId,
    double? CovariateBeta,
    bool IsEngineParameter,
    PriorEvidenceDto? PriorEvidence = null);

public sealed record AssumptionSetDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    Guid? ParentAssumptionSetId,
    DateTimeOffset CreatedAt,
    string? Notes,
    IReadOnlyList<ParameterDto> Parameters);

public sealed record CreateAssumptionSetRequest(
    [Required] Guid WorkspaceId,
    [Required, MaxLength(200)] string Name,
    string? Notes = null);

public sealed record UpdateAssumptionSetRequest(
    [Required, MaxLength(200)] string Name,
    string? Notes = null);

public sealed record BranchAssumptionSetRequest([Required, MaxLength(200)] string Name);

/// <summary>
/// Sets (upserting) one parameter. Give <c>clientSegmentId</c> + <c>flowKind</c> (+ <c>moduleProductId</c>
/// for CrossSellRate) to write a parameter the engine reads — those three are the engine's own
/// parameter key, so writing the same triple twice replaces rather than duplicates. Give <c>key</c>
/// instead for a free-form assumption the engine ignores.
/// <para>
/// <c>covariateBeta</c> is an <em>elasticity</em>: the engine applies rate_t = rate * (x_t / x_0)^beta,
/// so +1% on the covariate is about +beta% on the rate. The covariate's units are irrelevant (the
/// term is a ratio), but its path must be strictly positive wherever beta is nonzero.
/// </para>
/// <para>
/// <c>priorEvidence</c> records which observations informed the distribution (see
/// <see cref="PriorEvidenceDto"/>). Omitted, the parameter keeps the evidence it already had (unknown for a new
/// one); <c>clearPriorEvidence: true</c> clears it deliberately (not together with <c>priorEvidence</c>: 400).
/// Declaring different evidence over a calibration run's stamp while the distribution is unchanged is a 409
/// <see cref="ProblemTypes.CorrectionRequired"/>: that distribution is the run's posterior.
/// </para>
/// </summary>
public sealed record UpsertParameterRequest(
    [Required] DistributionSpecDto Distribution,
    ParameterProvenance Provenance = ParameterProvenance.Asserted,
    string? Rationale = null,
    Guid? ClientSegmentId = null,
    SimulationFlowKind? FlowKind = null,
    Guid? ModuleProductId = null,
    Guid? CovariateId = null,
    double? CovariateBeta = null,
    [MaxLength(300)] string? Key = null,
    PriorEvidenceDto? PriorEvidence = null,
    bool ClearPriorEvidence = false);

// ---------- SimulationRun ----------

public sealed record SimulationRunDto(
    Guid Id,
    Guid WorkspaceId,
    Guid RoadmapId,
    Guid AssumptionSetId,
    string? Name,
    DateOnly StartDate,
    int HorizonMonths,
    int Paths,
    int Seed,
    double DiscountRateAnnual,
    double TerminalGrowthAnnual,
    string? ModelVersion,
    bool IsExecuted,
    long? ElapsedMs,
    DateTimeOffset CreatedAt,
    SimulationEngineMode EngineMode,
    bool HasDiagnostics);

/// <summary>
/// Executes one simulation synchronously and stores it. The run is immutable once executed: the
/// engine input is snapshotted in full, so re-running the engine against the stored snapshot alone
/// reproduces the stored result exactly.
/// </summary>
/// <param name="RoadmapId">The roadmap branch whose planned projects supply the feature coverage deltas.</param>
/// <param name="AssumptionSetId">The parameter collection the engine samples from.</param>
/// <param name="HorizonMonths">Length of the simulated horizon in months.</param>
/// <param name="Paths">Monte Carlo paths; each samples parameter uncertainty and process noise.</param>
/// <param name="Seed">Base RNG seed — the same seed and input reproduce the run exactly.</param>
/// <param name="DiscountRateAnnual">Annual discount rate used to collapse margin trajectories to NPV.</param>
/// <param name="TerminalGrowthAnnual">Annual terminal growth rate for the Gordon terminal value.</param>
/// <param name="Name">Optional human label for the run.</param>
/// <param name="StartDate">Month 0 of the horizon; defaults to the first day of the current month.</param>
/// <param name="MarketId">Restricts the simulated segments to one market; omit to simulate the whole workspace.</param>
/// <param name="EventIds">Real market events to overlay.</param>
/// <param name="HypotheticalEventIds">Hypothetical events to inject — the "what if the incident happened" arm.</param>
/// <param name="EngineMode">
/// Which engine executes the run. <c>Aggregate</c> (the default) is engine v0: stock-and-flow per
/// segment, the fast preview. <c>FirmLevel</c> is engine v1: opportunities, consideration sets and
/// utilities over the actual firm roster (synthetic firms included). Both emit the same trajectory
/// format, so a comparison can mix them; only the firm-level mode also produces diagnostics.
/// </param>
public sealed record CreateSimulationRunRequest(
    [Required] Guid RoadmapId,
    [Required] Guid AssumptionSetId,
    [Range(1, 1200)] int HorizonMonths = 60,
    [Range(1, 100_000)] int Paths = 1000,
    int Seed = 0,
    [Range(-1, 10)] double DiscountRateAnnual = 0.10,
    [Range(-1, 10)] double TerminalGrowthAnnual = 0.02,
    string? Name = null,
    DateOnly? StartDate = null,
    Guid? MarketId = null,
    IReadOnlyList<Guid>? EventIds = null,
    IReadOnlyList<Guid>? HypotheticalEventIds = null,
    SimulationEngineMode EngineMode = SimulationEngineMode.Aggregate);

public static class SimulationMapping
{
    public static ParameterDto ToDto(this Parameter p) =>
        new(p.Id, p.AssumptionSetId, p.Key, DistributionSpecDto.From(p.Distribution), p.Provenance, p.Rationale,
            p.ClientSegmentId, p.FlowKind, p.ModuleProductId, p.CovariateId, p.CovariateBeta, p.IsEngineParameter,
            PriorEvidenceDto.From(p.PriorEvidence));

    public static AssumptionSetDto ToDto(this AssumptionSet a) =>
        new(a.Id, a.WorkspaceId, a.Name, a.ParentAssumptionSetId, a.CreatedAt, a.Notes,
            a.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(ToDto).ToList());

    public static SimulationRunDto ToDto(this SimulationRun r) =>
        new(r.Id, r.WorkspaceId, r.RoadmapId, r.AssumptionSetId, r.Name, r.StartDate, r.HorizonMonths, r.Paths,
            r.Seed, r.DiscountRateAnnual, r.TerminalGrowthAnnual, r.ModelVersion, r.IsExecuted, r.ElapsedMs,
            r.CreatedAt, r.EngineMode, r.DiagnosticsJson is not null);
}
