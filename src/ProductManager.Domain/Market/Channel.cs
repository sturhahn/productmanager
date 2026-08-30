using ProductManager.Domain.Common;

namespace ProductManager.Domain.Market;

/// <summary>
/// Gatekeepers and routes to market: reach into segments, opportunity origination, per-product
/// affinity/bias. Direct sales is just another channel.
/// </summary>
public sealed class Channel : Entity
{
    private readonly List<ChannelSegmentReach> _segmentReaches = [];
    private readonly List<ChannelProductAffinity> _productAffinities = [];

    public Guid MarketId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; set; }

    /// <summary>Whether this channel originates opportunities (e.g. prime brokers).</summary>
    public bool OriginatesOpportunities { get; set; }

    /// <summary>
    /// When true the channel only presents the products it has an affinity row for: everything else
    /// is out of the consideration set of the opportunities it originates (the "channel reach"
    /// filter). False — the default — means the channel presents every product in the market.
    /// </summary>
    public bool CarriesOnlyListedProducts { get; set; }

    public IReadOnlyCollection<ChannelSegmentReach> SegmentReaches => _segmentReaches.AsReadOnly();
    public IReadOnlyCollection<ChannelProductAffinity> ProductAffinities => _productAffinities.AsReadOnly();

    private Channel() { } // EF

    public Channel(Guid marketId, string name)
    {
        if (marketId == Guid.Empty) throw new ArgumentException("MarketId is required.", nameof(marketId));
        Rename(name);
        MarketId = marketId;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Channel name is required.", nameof(name));
        Name = name.Trim();
    }

    /// <summary>
    /// Sets (upserting) this channel's reach into a segment (fraction of the segment reachable,
    /// [0, 1]) and the share of that segment's opportunities it originates ([0, 1]). Reach and
    /// origination are different questions — a consultant may see the whole segment yet originate
    /// nothing — and the choice engine multiplies them into the channel draw weight.
    /// </summary>
    public void SetSegmentReach(Guid clientSegmentId, double reach, double originationShare = 0.0)
    {
        if (clientSegmentId == Guid.Empty) throw new ArgumentException("Segment id is required.", nameof(clientSegmentId));
        if (reach is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(reach), "Reach must be in [0, 1].");
        if (originationShare is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(originationShare), "Origination share must be in [0, 1].");
        _segmentReaches.RemoveAll(r => r.ClientSegmentId == clientSegmentId);
        _segmentReaches.Add(new ChannelSegmentReach(clientSegmentId, reach, originationShare));
    }

    /// <summary>Drops this channel's reach into one segment. False when there was none.</summary>
    public bool RemoveSegmentReach(Guid clientSegmentId) =>
        _segmentReaches.RemoveAll(r => r.ClientSegmentId == clientSegmentId) > 0;

    /// <summary>Drops this channel's bias toward one product. False when there was none.</summary>
    public bool RemoveProductAffinity(Guid productId) =>
        _productAffinities.RemoveAll(a => a.ProductId == productId) > 0;

    /// <summary>Sets (upserting) this channel's bias toward a product (e.g. consultants tilted toward competitors). Signed; 0 = neutral.</summary>
    public void SetProductAffinity(Guid productId, double bias)
    {
        if (productId == Guid.Empty) throw new ArgumentException("Product id is required.", nameof(productId));
        _productAffinities.RemoveAll(a => a.ProductId == productId);
        _productAffinities.Add(new ChannelProductAffinity(productId, bias));
    }
}

/// <summary>Owned by Channel: reach into one segment, and the share of its opportunities originated here.</summary>
public sealed class ChannelSegmentReach
{
    public Guid ClientSegmentId { get; private set; }

    /// <summary>Fraction of the segment this channel can reach, [0, 1].</summary>
    public double Reach { get; private set; }

    /// <summary>Share of the segment's opportunities this channel originates, [0, 1].</summary>
    public double OriginationShare { get; private set; }

    private ChannelSegmentReach() { } // EF

    public ChannelSegmentReach(Guid clientSegmentId, double reach, double originationShare = 0.0)
    {
        ClientSegmentId = clientSegmentId;
        Reach = reach;
        OriginationShare = originationShare;
    }
}

/// <summary>Owned by Channel: signed affinity/bias toward one product.</summary>
public sealed class ChannelProductAffinity
{
    public Guid ProductId { get; private set; }
    public double Bias { get; private set; }

    private ChannelProductAffinity() { } // EF

    public ChannelProductAffinity(Guid productId, double bias)
    {
        ProductId = productId;
        Bias = bias;
    }
}
