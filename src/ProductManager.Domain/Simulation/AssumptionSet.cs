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

    /// <summary>Sets (upserting by key) a parameter.</summary>
    public Parameter SetParameter(string key, Distribution distribution, ParameterProvenance provenance,
        string? rationale = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Parameter key is required.", nameof(key));
        _parameters.RemoveAll(p => p.Key == key.Trim());
        var parameter = new Parameter(Id, key.Trim(), distribution, provenance, rationale);
        _parameters.Add(parameter);
        return parameter;
    }

    /// <summary>Branch this set: a deep copy of all parameters, parent pointing back here.</summary>
    public AssumptionSet Branch(string branchName)
    {
        var branch = new AssumptionSet(WorkspaceId, branchName) { ParentAssumptionSetId = Id };
        foreach (var p in _parameters)
            branch._parameters.Add(new Parameter(branch.Id, p.Key, p.Distribution, p.Provenance, p.Rationale));
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
    public string? Notes { get; set; }

    private CalibrationRun() { } // EF

    public CalibrationRun(Guid workspaceId, DateOnly factsAsOfDate, Guid priorAssumptionSetId)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        if (priorAssumptionSetId == Guid.Empty)
            throw new ArgumentException("Prior assumption set is required.", nameof(priorAssumptionSetId));
        WorkspaceId = workspaceId;
        FactsAsOfDate = factsAsOfDate;
        PriorAssumptionSetId = priorAssumptionSetId;
    }

    public void RecordFit(Guid fittedAssumptionSetId)
    {
        if (FittedAssumptionSetId is not null)
            throw new InvalidOperationException("A calibration run is immutable once fitted.");
        FittedAssumptionSetId = fittedAssumptionSetId;
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

    /// <summary>Covariate scenario specification snapshot (JSON).</summary>
    public string? CovariateScenarioJson { get; private set; }

    /// <summary>Hypothetical-event overlay snapshot (JSON).</summary>
    public string? EventOverlayJson { get; private set; }

    public int Seed { get; private set; }
    public string? ModelVersion { get; private set; }

    /// <summary>Full input snapshot for exact reproducibility (JSON).</summary>
    public string? InputSnapshotJson { get; private set; }

    /// <summary>Output trajectories (JSON), once executed.</summary>
    public string? TrajectoriesJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private SimulationRun() { } // EF

    public SimulationRun(Guid workspaceId, Guid roadmapId, Guid assumptionSetId, int seed,
        string? covariateScenarioJson = null, string? eventOverlayJson = null, string? modelVersion = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        if (roadmapId == Guid.Empty) throw new ArgumentException("RoadmapId is required.", nameof(roadmapId));
        if (assumptionSetId == Guid.Empty)
            throw new ArgumentException("AssumptionSetId is required.", nameof(assumptionSetId));
        WorkspaceId = workspaceId;
        RoadmapId = roadmapId;
        AssumptionSetId = assumptionSetId;
        Seed = seed;
        CovariateScenarioJson = covariateScenarioJson;
        EventOverlayJson = eventOverlayJson;
        ModelVersion = modelVersion;
    }

    public void RecordResult(string inputSnapshotJson, string trajectoriesJson)
    {
        if (TrajectoriesJson is not null)
            throw new InvalidOperationException("A simulation run is immutable once executed.");
        InputSnapshotJson = inputSnapshotJson;
        TrajectoriesJson = trajectoriesJson;
    }
}
