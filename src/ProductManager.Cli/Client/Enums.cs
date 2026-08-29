namespace ProductManager.Cli.Client;

// Mirrors ProductManager.Domain.Common enums. Kept as independent copies (rather than a project
// reference to the Domain assembly) so the CLI stays a pure HTTP client of the documented
// contract, matching the wire format (enums travel as their names — see openapi/productmanager.json).

public enum ProjectStatus
{
    Proposed,
    Planned,
    InProgress,
    Done,
    Cancelled
}

public enum FeatureStatus
{
    Hypothetical,
    Planned,
    Shipped
}

/// <summary>Coverage tier vocabulary. Unknown is not a tier — see <see cref="CoverageDto.IsUnknown"/>.</summary>
public enum CoverageTier
{
    NotPlaying,
    Bronze,
    Silver,
    Gold
}

public enum FeatureDeltaKind
{
    Coverage,
    SwitchingCost,
    Other
}

public enum SequencingViolationKind
{
    StartsBeforeDependencyEnds,
    DependencyNotOnRoadmap,
    DependencyCycle
}
