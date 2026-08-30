using ProductManager.Domain.Common;

namespace ProductManager.Domain.Market;

/// <summary>
/// A sellable offering, ours or a competitor's. A *module* is a Product with a parent Product —
/// priced and cross-sold individually, same machinery. Pricing: recurring price per module
/// (optionally per segment), one-time implementation fee, placeholder for usage-based (deferred).
/// </summary>
public sealed class Product : Entity
{
    private readonly List<ProductPrice> _prices = [];

    public Guid MarketId { get; private set; }

    /// <summary>Owning competitor; null means the product is ours.</summary>
    public Guid? CompetitorId { get; private set; }

    /// <summary>Parent product when this is a module.</summary>
    public Guid? ParentProductId { get; private set; }

    public string Name { get; private set; } = null!;
    public string? Description { get; set; }

    public bool IsOurs => CompetitorId is null;
    public bool IsModule => ParentProductId is not null;

    public IReadOnlyCollection<ProductPrice> Prices => _prices.AsReadOnly();

    private Product() { } // EF

    public Product(Guid marketId, string name, Guid? competitorId = null, Guid? parentProductId = null)
    {
        if (marketId == Guid.Empty) throw new ArgumentException("MarketId is required.", nameof(marketId));
        Rename(name);
        MarketId = marketId;
        CompetitorId = competitorId;
        ParentProductId = parentProductId;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetParent(Guid? parentProductId)
    {
        if (parentProductId == Id)
            throw new DomainConflictException("A product cannot be its own parent.");
        ParentProductId = parentProductId;
    }

    /// <summary>Sets (upserting) pricing, optionally segment-specific. Null segment = default price.</summary>
    public void SetPrice(Money recurringPricePerPeriod, Money implementationFee, Guid? clientSegmentId = null,
        string? usageBasedPlaceholder = null)
    {
        var existing = _prices.FirstOrDefault(p => p.ClientSegmentId == clientSegmentId);
        if (existing is not null) _prices.Remove(existing);
        _prices.Add(new ProductPrice(clientSegmentId, recurringPricePerPeriod, implementationFee, usageBasedPlaceholder));
    }

    /// <summary>Drops a price row. A null segment drops the default price. False when there was none.</summary>
    public bool RemovePrice(Guid? clientSegmentId) =>
        _prices.RemoveAll(p => p.ClientSegmentId == clientSegmentId) > 0;
}

/// <summary>Pricing for a product, optionally per segment. Owned by Product.</summary>
public sealed class ProductPrice
{
    /// <summary>Null = the default price; set = a segment-specific override.</summary>
    public Guid? ClientSegmentId { get; private set; }

    public Money RecurringPricePerPeriod { get; private set; } = null!;
    public Money ImplementationFee { get; private set; } = null!;

    /// <summary>Placeholder for usage-based pricing (deferred to a future release).</summary>
    public string? UsageBasedPlaceholder { get; private set; }

    private ProductPrice() { } // EF

    public ProductPrice(Guid? clientSegmentId, Money recurringPricePerPeriod, Money implementationFee,
        string? usageBasedPlaceholder)
    {
        ClientSegmentId = clientSegmentId;
        RecurringPricePerPeriod = recurringPricePerPeriod ?? throw new ArgumentNullException(nameof(recurringPricePerPeriod));
        ImplementationFee = implementationFee ?? throw new ArgumentNullException(nameof(implementationFee));
        UsageBasedPlaceholder = usageBasedPlaceholder;
    }
}

/// <summary>
/// "Being a client" is an edge, not an entity: firm holds product (module), at a price, since a
/// date. Same edge shape for competitors' client relationships.
/// </summary>
public sealed class FirmProductRelationship : Entity
{
    public Guid FirmId { get; private set; }
    public Guid ProductId { get; private set; }
    public Money? PricePaidPerPeriod { get; private set; }
    public DateOnly? AcquisitionDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string? Notes { get; set; }

    /// <summary>
    /// The modules held: which business processes this product actually serves at this firm. An
    /// empty list means "whatever the product covers" — the honest reading of a relationship nobody
    /// has decomposed yet. This is what the choice engine measures switching cost and scope overlap
    /// against, so it is a list of processes rather than a count.
    /// </summary>
    public List<Guid> ServedProcessIds { get; private set; } = [];

    /// <summary>
    /// Price actually paid as a multiplier on list price (1.0 = list, 0.8 = a 20% discount).
    /// Revenue uses it; utilities compare candidates at list price, because a prospect does not
    /// know what the incumbent negotiated.
    /// </summary>
    public double PriceLevel { get; private set; } = 1.0;

    public bool IsActive => EndDate is null;

    private FirmProductRelationship() { } // EF

    public FirmProductRelationship(Guid firmId, Guid productId, DateOnly? acquisitionDate = null,
        Money? pricePaidPerPeriod = null)
    {
        if (firmId == Guid.Empty) throw new ArgumentException("FirmId is required.", nameof(firmId));
        if (productId == Guid.Empty) throw new ArgumentException("ProductId is required.", nameof(productId));
        FirmId = firmId;
        ProductId = productId;
        AcquisitionDate = acquisitionDate;
        PricePaidPerPeriod = pricePaidPerPeriod;
    }

    /// <summary>
    /// A relationship with a caller-supplied identity, so a deterministically generated synthetic
    /// population reproduces exactly rather than duplicating itself on regeneration.
    /// </summary>
    public static FirmProductRelationship WithIdentity(Guid id, Guid firmId, Guid productId,
        DateOnly? acquisitionDate = null, Money? pricePaidPerPeriod = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("An identity is required.", nameof(id));
        return new FirmProductRelationship(firmId, productId, acquisitionDate, pricePaidPerPeriod) { Id = id };
    }

    public void SetPricePaid(Money? pricePaidPerPeriod) => PricePaidPerPeriod = pricePaidPerPeriod;

    /// <summary>Replaces the set of processes this product serves at the firm (deduplicated, ordered).</summary>
    public void SetServedProcesses(IEnumerable<Guid> processIds)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        ServedProcessIds = processIds.Where(p => p != Guid.Empty).Distinct().OrderBy(p => p).ToList();
    }

    public void SetPriceLevel(double priceLevel)
    {
        if (priceLevel <= 0 || double.IsNaN(priceLevel))
            throw new ArgumentOutOfRangeException(nameof(priceLevel), "Price level must be a positive multiplier of list price.");
        PriceLevel = priceLevel;
    }

    public void SetAcquisitionDate(DateOnly? acquisitionDate)
    {
        if (acquisitionDate.HasValue && EndDate.HasValue && EndDate.Value < acquisitionDate.Value)
            throw new ArgumentException("Relationship cannot end before it was acquired.");
        AcquisitionDate = acquisitionDate;
    }

    /// <summary>Clears the end date — the relationship is active again (a correction, not a renewal).</summary>
    public void Reopen() => EndDate = null;

    public void End(DateOnly endDate)
    {
        if (AcquisitionDate.HasValue && endDate < AcquisitionDate.Value)
            throw new ArgumentException("Relationship cannot end before it was acquired.");
        EndDate = endDate;
    }
}

/// <summary>
/// Product↔Product edge with a quality score; includes integrations with partial competitors.
/// Enters best-of-breed utility against a firm's existing stack.
/// </summary>
public sealed class Integration : Entity
{
    public Guid ProductAId { get; private set; }
    public Guid ProductBId { get; private set; }

    /// <summary>Integration quality in [0, 1].</summary>
    public double QualityScore { get; private set; }

    public string? Notes { get; set; }

    private Integration() { } // EF

    public Integration(Guid productAId, Guid productBId, double qualityScore)
    {
        if (productAId == Guid.Empty || productBId == Guid.Empty)
            throw new ArgumentException("Both product ids are required.");
        if (productAId == productBId)
            throw new ArgumentException("A product cannot integrate with itself.");
        ProductAId = productAId;
        ProductBId = productBId;
        SetQuality(qualityScore);
    }

    public void SetQuality(double qualityScore)
    {
        if (qualityScore is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(qualityScore), "Quality score must be in [0, 1].");
        QualityScore = qualityScore;
    }
}
