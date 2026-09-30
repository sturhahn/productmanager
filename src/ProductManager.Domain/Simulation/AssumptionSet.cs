using ProductManager.Domain.Common;

namespace ProductManager.Domain.Simulation;

/// <summary>
/// Versioned, branchable parameter collection. Every parameter carries a distribution,
/// provenance, and rationale. "What if churn is 2×" = a one-parameter branch.
/// Schema stub in Phase 1 — no engine logic.
/// </summary>
public sealed class AssumptionSet : Entity
{
    private readonly List<Parameter> _parameters = [];

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>The assumption set this one was branched from; null for a root set.</summary>
    public Guid? ParentAssumptionSetId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }

    public IReadOnlyCollection<Parameter> Parameters => _parameters.AsReadOnly();

    private AssumptionSet() { } // EF

    public AssumptionSet(Guid workspaceId, string name)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        Rename(name);
        WorkspaceId = workspaceId;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Assumption set name is required.", nameof(name));
        Name = name.Trim();
    }

    /// <summary>Sets (upserting by key) a free-form parameter — one the engine does not read.</summary>
    public Parameter SetParameter(string key, Distribution distribution, ParameterProvenance provenance,
        string? rationale = null, PriorEvidence? priorEvidence = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Parameter key is required.", nameof(key));
        _parameters.RemoveAll(p => p.Key == key.Trim());
        var parameter = new Parameter(Id, key.Trim(), distribution, provenance, rationale).WithPriorEvidence(priorEvidence);
        _parameters.Add(parameter);
        return parameter;
    }

    /// <summary>
    /// Sets (upserting) a parameter the engine reads, keyed exactly as the engine keys it:
    /// (segment, flow kind, module?). The string <see cref="Parameter.Key"/> is derived from that
    /// triple, so the same flow for the same segment can never end up stored twice.
    /// </summary>
    public Parameter SetEngineParameter(
        Guid clientSegmentId,
        SimulationFlowKind flowKind,
        Guid? moduleProductId,
        Distribution distribution,
        ParameterProvenance provenance,
        string? rationale = null,
        Guid? covariateId = null,
        double? covariateBeta = null,
        PriorEvidence? priorEvidence = null)
    {
        if (clientSegmentId == Guid.Empty)
            throw new ArgumentException("ClientSegmentId is required.", nameof(clientSegmentId));
        if (moduleProductId is not null && flowKind != SimulationFlowKind.CrossSellRate)
            throw new ArgumentException(
                "Only a CrossSellRate parameter may name a module.", nameof(moduleProductId));
        if (flowKind == SimulationFlowKind.CrossSellRate && moduleProductId is null)
            throw new ArgumentException(
                "A CrossSellRate parameter must name the module it cross-sells.", nameof(moduleProductId));
        if (covariateId is not null && covariateBeta is null)
            throw new ArgumentException("A covariate link needs a beta.", nameof(covariateBeta));
        if (covariateId is null && covariateBeta is not null)
            throw new ArgumentException("A covariate beta needs a covariate.", nameof(covariateId));

        var key = Parameter.EngineKey(flowKind, clientSegmentId, moduleProductId);
        _parameters.RemoveAll(p => p.Key == key);
        var parameter = new Parameter(Id, key, distribution, provenance, rationale)
            .WithEngineKeying(clientSegmentId, flowKind, moduleProductId)
            .WithCovariateLink(covariateId, covariateBeta)
            .WithPriorEvidence(priorEvidence);
        _parameters.Add(parameter);
        return parameter;
    }

    /// <summary>Removes one parameter by its id. Returns false when this set does not hold it.</summary>
    public bool RemoveParameter(Guid parameterId) => _parameters.RemoveAll(p => p.Id == parameterId) > 0;

    /// <summary>Branch this set: a deep copy of all parameters, parent pointing back here.</summary>
    public AssumptionSet Branch(string branchName)
    {
        var branch = new AssumptionSet(WorkspaceId, branchName) { ParentAssumptionSetId = Id };
        foreach (var p in _parameters)
            // The distribution is deep-copied, not shared: it is an owned value whose identity is
            // its owner, and one instance hanging off two parameters is not a thing that can be stored.
            branch._parameters.Add(
                new Parameter(branch.Id, p.Key, p.Distribution.Copy(), p.Provenance, p.Rationale)
                    .WithEngineKeying(p.ClientSegmentId, p.FlowKind, p.ModuleProductId)
                    .WithCovariateLink(p.CovariateId, p.CovariateBeta)
                    .WithPriorEvidence(p.PriorEvidence?.Copy()));
        return branch;
    }
}

/// <summary>
/// One model parameter: distribution (type + two numbers) + provenance
/// (asserted / calibrated / default) + rationale.
/// </summary>
public sealed class Parameter : Entity
{
    public Guid AssumptionSetId { get; private set; }
    public string Key { get; private set; } = null!;
    public Distribution Distribution { get; private set; } = null!;
    public ParameterProvenance Provenance { get; private set; }
    public string? Rationale { get; private set; }

    // ---- Structured keying: set on parameters the engine reads, null on free-form ones ----

    /// <summary>The segment this rate governs; null on a free-form parameter.</summary>
    public Guid? ClientSegmentId { get; private set; }

    /// <summary>The flow this rate governs; null on a free-form parameter.</summary>
    public SimulationFlowKind? FlowKind { get; private set; }

    /// <summary>The cross-sold module; only ever set for <see cref="SimulationFlowKind.CrossSellRate"/>.</summary>
    public Guid? ModuleProductId { get; private set; }

    /// <summary>Covariate whose movement scales this rate; null = no link.</summary>
    public Guid? CovariateId { get; private set; }

    /// <summary>
    /// Elasticity of the rate with respect to the covariate: rate_t = rate * (x_t / x_0)^beta,
    /// i.e. a +1% move in the covariate moves the rate by about +beta%. Being a ratio form it is
    /// scale-free — the covariate's units do not matter — but it does require a strictly positive
    /// covariate path, which the engine enforces.
    /// </summary>
    public double? CovariateBeta { get; private set; }

    /// <summary>
    /// Which observations informed this distribution; null = unknown. Provenance metadata for the
    /// evidence-reuse check at calibration time — never read by the engine.
    /// </summary>
    public PriorEvidence? PriorEvidence { get; private set; }

    /// <summary>True when this parameter is keyed the way the engine keys its parameters.</summary>
    public bool IsEngineParameter => ClientSegmentId is not null && FlowKind is not null;

    private Parameter() { } // EF

    public Parameter(Guid assumptionSetId, string key, Distribution distribution, ParameterProvenance provenance,
        string? rationale)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Parameter key is required.", nameof(key));
        AssumptionSetId = assumptionSetId;
        Key = key.Trim();
        Distribution = distribution ?? throw new ArgumentNullException(nameof(distribution));
        Provenance = provenance;
        Rationale = rationale;
    }

    /// <summary>The canonical storage key for an engine parameter — the engine's own (segment, flow, module) key.</summary>
    public static string EngineKey(SimulationFlowKind flowKind, Guid clientSegmentId, Guid? moduleProductId) =>
        moduleProductId is null
            ? $"{flowKind}:{clientSegmentId}"
            : $"{flowKind}:{clientSegmentId}:{moduleProductId}";

    internal Parameter WithEngineKeying(Guid? clientSegmentId, SimulationFlowKind? flowKind, Guid? moduleProductId)
    {
        ClientSegmentId = clientSegmentId;
        FlowKind = flowKind;
        ModuleProductId = moduleProductId;
        return this;
    }

    internal Parameter WithCovariateLink(Guid? covariateId, double? covariateBeta)
    {
        CovariateId = covariateId;
        CovariateBeta = covariateBeta;
        return this;
    }

    internal Parameter WithPriorEvidence(PriorEvidence? priorEvidence)
    {
        PriorEvidence = priorEvidence;
        return this;
    }
}

/// <summary>
/// Immutable fit: facts ledger as of a date + prior AssumptionSet → fitted AssumptionSet,
/// diffable against the prior. Schema stub — no calibration logic in Phase 1.
/// </summary>
public sealed class CalibrationRun : Entity
{
    public Guid WorkspaceId { get; private set; }

    /// <summary>Reconstruct "what we knew as of date D" from the facts ledger.</summary>
    public DateOnly FactsAsOfDate { get; private set; }

    public Guid PriorAssumptionSetId { get; private set; }
    public Guid? FittedAssumptionSetId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional human label for the fit ("Q3 refit", "after the migration window").</summary>
    public string? Name { get; private set; }

    /// <summary>
    /// Lower bound of the fit's observation window: only facts recorded after this date were read, and
    /// relationship exposure starts here. Null = the whole ledger up to the as-of date.
    /// </summary>
    public DateOnly? ObservationsRecordedAfter { get; private set; }

    /// <summary>
    /// The prior set's parameters as they stood when the fit ran (JSON, including prior evidence), so
    /// a readiness report on this run does not depend on later edits to the prior set.
    /// </summary>
    public string? PriorParametersJson { get; private set; }

    /// <summary>
    /// The exact calibration input this run was executed against (JSON): priors plus the
    /// observations mapped out of the ledger as of <see cref="FactsAsOfDate"/>. Re-running the pure
    /// calibration module on this document alone reproduces the stored output.
    /// </summary>
    public string? InputSnapshotJson { get; private set; }

    /// <summary>The fitted posteriors and their prior/posterior diffs (JSON), once executed.</summary>
    public string? OutputJson { get; private set; }

    public string? Notes { get; set; }

    /// <summary>True once the fit has run and produced a fitted assumption set.</summary>
    public bool IsExecuted => OutputJson is not null;

    private CalibrationRun() { } // EF

    public CalibrationRun(Guid workspaceId, DateOnly factsAsOfDate, Guid priorAssumptionSetId, string? name = null,
        DateOnly? observationsRecordedAfter = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        if (priorAssumptionSetId == Guid.Empty)
            throw new ArgumentException("Prior assumption set is required.", nameof(priorAssumptionSetId));
        if (observationsRecordedAfter >= factsAsOfDate)
            throw new ArgumentException(
                "The observation window must start before the as-of date (observationsRecordedAfter < asOfDate).",
                nameof(observationsRecordedAfter));
        ObservationsRecordedAfter = observationsRecordedAfter;
        WorkspaceId = workspaceId;
        FactsAsOfDate = factsAsOfDate;
        PriorAssumptionSetId = priorAssumptionSetId;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    /// <summary>
    /// Records the fit: the assumption set it produced, and the input/output documents that make it
    /// reproducible and diffable. A run is immutable once fitted.
    /// </summary>
    public void RecordFit(Guid fittedAssumptionSetId, string? inputSnapshotJson = null, string? outputJson = null,
        string? priorParametersJson = null)
    {
        if (FittedAssumptionSetId is not null)
            throw new DomainConflictException("A calibration run is immutable once fitted.");
        FittedAssumptionSetId = fittedAssumptionSetId;
        InputSnapshotJson = inputSnapshotJson;
        OutputJson = outputJson;
        PriorParametersJson = priorParametersJson;
    }
}

/// <summary>
/// Immutable execution: references roadmap branch + AssumptionSet version + covariate scenario +
/// event overlay + seed → trajectories. Reproducible; comparisons are diffs of these references.
/// Schema stub — no engine in Phase 1.
/// </summary>
public sealed class SimulationRun : Entity
{
    public Guid WorkspaceId { get; private set; }
    public Guid RoadmapId { get; private set; }
    public Guid AssumptionSetId { get; private set; }

    /// <summary>Optional human label for the run ("base case", "with the incident").</summary>
    public string? Name { get; private set; }

    /// <summary>Calendar month the horizon starts in — month 0 of every trajectory.</summary>
    public DateOnly StartDate { get; private set; }

    public int HorizonMonths { get; private set; }
    public int Paths { get; private set; }
    public double DiscountRateAnnual { get; private set; }
    public double TerminalGrowthAnnual { get; private set; }

    /// <summary>Covariate scenario specification snapshot (JSON).</summary>
    public string? CovariateScenarioJson { get; private set; }

    /// <summary>Event overlay snapshot — the real and hypothetical events included in this run (JSON).</summary>
    public string? EventOverlayJson { get; private set; }

    public int Seed { get; private set; }
    public string? ModelVersion { get; private set; }

    /// <summary>
    /// Which engine executed the run. Both modes emit the same trajectory format, so this is
    /// recorded for provenance and for reading the diagnostics, never to reshape the result.
    /// </summary>
    public SimulationEngineMode EngineMode { get; private set; } = SimulationEngineMode.Aggregate;

    /// <summary>
    /// Firm-level diagnostics (JSON) — opportunity funnels, sales-cycle length, expansions. Null on
    /// aggregate runs, which have no opportunities to count. Kept beside the trajectories rather
    /// than inside them so the trajectory document stays byte-identical across both modes.
    /// </summary>
    public string? DiagnosticsJson { get; private set; }

    /// <summary>
    /// Full engine input snapshot (JSON). This is the reproducibility guarantee: re-running the
    /// engine on this snapshot alone — no database reads — reproduces the stored result byte for byte.
    /// </summary>
    public string? InputSnapshotJson { get; private set; }

    /// <summary>Output trajectories (JSON), once executed.</summary>
    public string? TrajectoriesJson { get; private set; }

    /// <summary>Wall-clock engine time, once executed.</summary>
    public long? ElapsedMs { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsExecuted => TrajectoriesJson is not null;

    private SimulationRun() { } // EF

    public SimulationRun(
        Guid workspaceId,
        Guid roadmapId,
        Guid assumptionSetId,
        DateOnly startDate,
        int horizonMonths,
        int paths,
        int seed,
        double discountRateAnnual,
        double terminalGrowthAnnual,
        string? name = null,
        string? covariateScenarioJson = null,
        string? eventOverlayJson = null,
        string? modelVersion = null,
        SimulationEngineMode engineMode = SimulationEngineMode.Aggregate)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        if (roadmapId == Guid.Empty) throw new ArgumentException("RoadmapId is required.", nameof(roadmapId));
        if (assumptionSetId == Guid.Empty)
            throw new ArgumentException("AssumptionSetId is required.", nameof(assumptionSetId));
        if (horizonMonths < 1)
            throw new ArgumentOutOfRangeException(nameof(horizonMonths), "A run needs at least one month.");
        if (paths < 1)
            throw new ArgumentOutOfRangeException(nameof(paths), "A run needs at least one Monte Carlo path.");

        WorkspaceId = workspaceId;
        RoadmapId = roadmapId;
        AssumptionSetId = assumptionSetId;
        StartDate = startDate;
        HorizonMonths = horizonMonths;
        Paths = paths;
        Seed = seed;
        DiscountRateAnnual = discountRateAnnual;
        TerminalGrowthAnnual = terminalGrowthAnnual;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        CovariateScenarioJson = covariateScenarioJson;
        EventOverlayJson = eventOverlayJson;
        ModelVersion = modelVersion;
        EngineMode = engineMode;
    }

    public void RecordResult(string inputSnapshotJson, string trajectoriesJson, long? elapsedMs = null,
        string? diagnosticsJson = null)
    {
        if (TrajectoriesJson is not null)
            throw new DomainConflictException("A simulation run is immutable once executed.");
        InputSnapshotJson = inputSnapshotJson;
        TrajectoriesJson = trajectoriesJson;
        ElapsedMs = elapsedMs;
        DiagnosticsJson = diagnosticsJson;
    }
}
