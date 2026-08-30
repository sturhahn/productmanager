namespace ProductManager.Domain.Market;

/// <summary>
/// The *observed starting state* of a client segment: the stocks the simulation starts from and
/// the money each client is worth. Owned by <see cref="ClientSegment"/>.
///
/// Amounts are per month, in the workspace's implicit single currency — the engine is unit-agnostic
/// and never converts, so a run mixing currencies would silently add apples to oranges.
/// </summary>
public sealed class SegmentStockState
{
    /// <summary>Firms in the segment that are not clients yet — the pool opportunities are drawn from.</summary>
    public double InitialProspectPool { get; private set; }

    /// <summary>Firms in the segment that are already our clients at month 0.</summary>
    public int InitialClientCount { get; private set; }

    /// <summary>Average recurring revenue per client per month, base product only (modules price separately).</summary>
    public double ArpuPerMonth { get; private set; }

    /// <summary>One-time fee recognised when a client signs.</summary>
    public double ImplementationFee { get; private set; }

    /// <summary>Cost of servicing one client for one month.</summary>
    public double ServicingCostPerClient { get; private set; }

    /// <summary>Infrastructure cost that does not scale with the client count, per month.</summary>
    public double InfraFixedCostPerMonth { get; private set; }

    /// <summary>Infrastructure cost per client per month.</summary>
    public double InfraCostPerClient { get; private set; }

    /// <summary>
    /// R&amp;D drag: support, bugs and incident work per month. The knob linking under-investment in
    /// stability to incident-driven churn.
    /// </summary>
    public double RdDragPerMonth { get; private set; }

    private SegmentStockState() { } // EF

    public SegmentStockState(
        double initialProspectPool,
        int initialClientCount,
        double arpuPerMonth,
        double implementationFee,
        double servicingCostPerClient,
        double infraFixedCostPerMonth,
        double infraCostPerClient,
        double rdDragPerMonth)
    {
        NonNegative(initialProspectPool, nameof(initialProspectPool));
        if (initialClientCount < 0)
            throw new ArgumentOutOfRangeException(nameof(initialClientCount), "Client count cannot be negative.");
        NonNegative(arpuPerMonth, nameof(arpuPerMonth));
        NonNegative(implementationFee, nameof(implementationFee));
        NonNegative(servicingCostPerClient, nameof(servicingCostPerClient));
        NonNegative(infraFixedCostPerMonth, nameof(infraFixedCostPerMonth));
        NonNegative(infraCostPerClient, nameof(infraCostPerClient));
        NonNegative(rdDragPerMonth, nameof(rdDragPerMonth));

        InitialProspectPool = initialProspectPool;
        InitialClientCount = initialClientCount;
        ArpuPerMonth = arpuPerMonth;
        ImplementationFee = implementationFee;
        ServicingCostPerClient = servicingCostPerClient;
        InfraFixedCostPerMonth = infraFixedCostPerMonth;
        InfraCostPerClient = infraCostPerClient;
        RdDragPerMonth = rdDragPerMonth;
    }

    /// <summary>
    /// A fresh all-zero state — the honest default for a segment nobody has measured yet.
    /// A factory rather than a shared instance: EF owned entities cannot be shared between owners.
    /// </summary>
    public static SegmentStockState Zero() => new(0, 0, 0, 0, 0, 0, 0, 0);

    private static void NonNegative(double value, string name)
    {
        if (value < 0 || double.IsNaN(value))
            throw new ArgumentOutOfRangeException(name, "Value must be a non-negative number.");
    }
}

/// <summary>
/// Initial penetration and pricing of one cross-sellable module inside a segment. The module is a
/// Product with a parent Product. Owned by <see cref="ClientSegment"/>.
/// </summary>
public sealed class SegmentModulePenetration
{
    public Guid ModuleProductId { get; private set; }

    /// <summary>Share of the segment's clients already holding the module at month 0, in [0, 1].</summary>
    public double Penetration { get; private set; }

    /// <summary>Recurring revenue per month per client holding this module.</summary>
    public double ArpuPerMonth { get; private set; }

    private SegmentModulePenetration() { } // EF

    public SegmentModulePenetration(Guid moduleProductId, double penetration, double arpuPerMonth)
    {
        if (moduleProductId == Guid.Empty)
            throw new ArgumentException("ModuleProductId is required.", nameof(moduleProductId));
        if (penetration is < 0 or > 1 || double.IsNaN(penetration))
            throw new ArgumentOutOfRangeException(nameof(penetration), "Penetration must be in [0, 1].");
        if (arpuPerMonth < 0 || double.IsNaN(arpuPerMonth))
            throw new ArgumentOutOfRangeException(nameof(arpuPerMonth), "ARPU cannot be negative.");

        ModuleProductId = moduleProductId;
        Penetration = penetration;
        ArpuPerMonth = arpuPerMonth;
    }
}

/// <summary>
/// Share of a segment's firms using one alternative approach to a business process. Shares are
/// normalized where they are consumed, so a mix that does not sum to 1 is a rounding detail rather
/// than an error. Owned by <see cref="ClientSegment"/>.
/// </summary>
public sealed class SegmentApproachAdoption
{
    public Guid ApproachId { get; private set; }

    /// <summary>Share of the segment's firms on this approach, in [0, 1].</summary>
    public double Share { get; private set; }

    private SegmentApproachAdoption() { } // EF

    public SegmentApproachAdoption(Guid approachId, double share)
    {
        if (approachId == Guid.Empty)
            throw new ArgumentException("ApproachId is required.", nameof(approachId));
        if (share is < 0 or > 1 || double.IsNaN(share))
            throw new ArgumentOutOfRangeException(nameof(share), "An adoption share must be in [0, 1].");

        ApproachId = approachId;
        Share = share;
    }
}
