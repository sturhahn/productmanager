namespace ProductManager.Engine;

/// <summary>
/// The flow a stochastic parameter governs. Serialized by member name (matches API-wide
/// JsonStringEnumConverter convention).
/// </summary>
public enum FlowKind
{
    /// <summary>Absolute Poisson intensity of new firms entering the segment's prospect pool per month.</summary>
    NewFirmArrival,

    /// <summary>Monthly rate at which pooled prospects come to market (opportunities ~ Poisson(pool * rate)).</summary>
    ConversionToMarket,

    /// <summary>Per-opportunity win probability (Binomial), further scaled by attractiveness and events.</summary>
    WinRate,

    /// <summary>Per-client monthly churn probability (Binomial).</summary>
    ChurnHazard,

    /// <summary>Per-module monthly adoption probability among clients not yet holding the module (Binomial).</summary>
    CrossSellRate,
}

/// <summary>Supported parameter distribution families.</summary>
public enum DistributionType
{
    /// <summary>P1 = mean, P2 = standard deviation.</summary>
    Normal,

    /// <summary>P1 = mu (mean of log), P2 = sigma (std dev of log).</summary>
    LogNormal,

    /// <summary>P1 = alpha, P2 = beta shape parameters.</summary>
    Beta,

    /// <summary>P1 = the value, P2 ignored. A zero-width distribution.</summary>
    Point,
}

/// <summary>A two-number distribution spec. Meaning of P1/P2 depends on <see cref="Type"/>.</summary>
public sealed record DistributionSpec(DistributionType Type, double P1, double P2 = 0.0)
{
    public static DistributionSpec Point(double value) => new(DistributionType.Point, value);
}

/// <summary>
/// Optional covariate link on a parameter. The sampled base rate is multiplied by
/// (x_t / x_0)^Beta where x_t is the covariate value in month t and x_0 its month-0 value —
/// i.e. the base rate is interpreted at the covariate's starting level, and <b>Beta is an
/// elasticity</b>: a +1% move in the covariate moves the rate by about +Beta%.
/// <para>
/// The form is deliberately scale-free: it reads the covariate in ratios, so an index quoted
/// at 108.6 and the same index normalised to 1.0 produce identical trajectories. (The earlier
/// semi-log form, exp(Beta * (x_t - x_0)), read raw level *differences* and so exploded on any
/// series not normalised to ~1 — beta 0.6 on an equity index drifting 20 points produced a
/// multiplier of e^12.5.)
/// </para>
/// <para>
/// Because the ratio and the power both demand positivity, a covariate linked with a nonzero
/// Beta must have a strictly positive path (and a strictly positive GBM start); the engine
/// rejects anything else at validation time.
/// </para>
/// </summary>
public sealed record CovariateLink(string CovariateId, double Beta);

/// <summary>
/// One stochastic parameter, keyed by (SegmentId, FlowKind, ModuleId?). ModuleId is only
/// meaningful for <see cref="FlowKind.CrossSellRate"/> and must be null otherwise.
/// A missing parameter for any (segment, flow) key defaults to Point(0) — the flow is off.
/// </summary>
public sealed record ParameterSpec(
    string SegmentId,
    FlowKind FlowKind,
    string? ModuleId,
    DistributionSpec Distribution,
    CovariateLink? Covariate = null);

/// <summary>Initial penetration and pricing of one cross-sellable module within a segment.</summary>
public sealed record ModulePenetration(string ModuleId, double Penetration, double ArpuPerMonth);

/// <summary>Simple monthly cost rates for a segment.</summary>
public sealed record CostRates(
    double ServicingPerClient,
    double InfraFixed,
    double InfraPerClient,
    double RdDragPerMonth);

/// <summary>One client segment's initial stocks, pricing and cost structure.</summary>
public sealed record SegmentInput(
    string Id,
    double InitialProspectPool,
    int InitialClientCount,
    double ArpuPerMonth,
    double ImplementationFee,
    IReadOnlyList<ModulePenetration> PerModulePenetration,
    CostRates CostRates);

/// <summary>
/// Geometric Brownian motion spec for a covariate future path. Drift and Vol are per-month
/// (log-scale drift and volatility). x_t = x_{t-1} * exp(Drift - Vol^2/2 + Vol * z_t).
/// </summary>
public sealed record GbmSpec(double Start, double Drift, double Vol);

/// <summary>
/// A named external time series. Exactly one of <see cref="MonthlyPath"/> (pre-resolved,
/// shared across all MC paths; length &gt;= HorizonMonths) or <see cref="Gbm"/> (sampled
/// independently per MC path) must be provided.
/// </summary>
public sealed record CovariateInput(string Id, double[]? MonthlyPath = null, GbmSpec? Gbm = null);

/// <summary>
/// One rate multiplier applied by an event. SegmentId null means all segments.
/// </summary>
public sealed record EventRateMultiplier(string? SegmentId, FlowKind FlowKind, double Multiplier);

/// <summary>
/// A shock active over an inclusive month window [StartMonth, EndMonth] (0-based month
/// indices). While active, its multipliers scale the matching flows' rates.
/// </summary>
public sealed record EventInput(
    string Id,
    int StartMonth,
    int EndMonth,
    IReadOnlyList<EventRateMultiplier> RateMultipliers);

/// <summary>
/// A multiplier on WinRate for (SegmentId, Month). This is how roadmap feature coverage
/// deltas enter engine v0: the caller computes it from coverage tiers and scheduled feature
/// ship dates. Missing entries default to 1.0. Month is 0-based.
/// </summary>
public sealed record AttractivenessEntry(string SegmentId, int Month, double Multiplier);

/// <summary>Complete, self-contained input of one simulation run. Pure data; JSON-serialization-friendly.</summary>
public sealed record EngineInput(
    IReadOnlyList<SegmentInput> Segments,
    IReadOnlyList<ParameterSpec> Parameters,
    IReadOnlyList<CovariateInput> Covariates,
    IReadOnlyList<EventInput> Events,
    IReadOnlyList<AttractivenessEntry> AttractivenessSchedule,
    int HorizonMonths,
    int Paths = 1000,
    int Seed = 0,
    double DiscountRateAnnual = 0.10,
    double TerminalGrowthAnnual = 0.02);

/// <summary>Summary statistics of a scalar distribution across MC paths.</summary>
public sealed record DistributionSummary(double Mean, double P10, double P50, double P90);

/// <summary>
/// Per-month summary bands of one metric across MC paths. All arrays have length HorizonMonths;
/// index = month (0-based).
/// </summary>
public sealed record MetricTrajectory(double[] Mean, double[] P10, double[] P50, double[] P90);

/// <summary>All tracked metric trajectories for one segment (or the cross-segment total).</summary>
public sealed record SegmentTrajectories(
    string SegmentId,
    MetricTrajectory Clients,
    MetricTrajectory ProspectPool,
    MetricTrajectory NewWins,
    MetricTrajectory Churned,
    MetricTrajectory Bookings,
    MetricTrajectory RecognizedArr,
    MetricTrajectory ContributionMargin);

/// <summary>
/// Result of one engine run. Deterministic for a given (input, seed): per-path RNG streams are
/// derived stably from (Seed, pathIndex), so increasing Paths leaves earlier paths' draws
/// unchanged (observable via <see cref="NpvPerPath"/> prefix stability).
/// </summary>
public sealed record EngineResult(
    IReadOnlyList<SegmentTrajectories> Segments,
    SegmentTrajectories Total,
    DistributionSummary Npv,
    double[] NpvPerPath,
    int Paths,
    int Seed,
    int HorizonMonths,
    long ElapsedMs);
