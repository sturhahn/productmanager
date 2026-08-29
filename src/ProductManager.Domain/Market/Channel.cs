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

    /// <summary>Sets (upserting) this channel's reach into a segment (fraction of the segment reachable, [0, 1]).</summary>
    public void SetSegmentReach(Guid clientSegmentId, double reach)
    {
        if (clientSegmentId == Guid.Empty) throw new ArgumentException("Segment id is required.", nameof(clientSegmentId));
        if (reach is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(reach), "Reach must be in [0, 1].");
        _segmentReaches.RemoveAll(r => r.ClientSegmentId == clientSegmentId);
        _segmentReaches.Add(new ChannelSegmentReach(clientSegmentId, reach));
    }

    /// <summary>Sets (upserting) this channel's bias toward a product (e.g. consultants tilted toward competitors). Signed; 0 = neutral.</summary>
    public void SetProductAffinity(Guid productId, double bias)
    {
        if (productId == Guid.Empty) throw new ArgumentException("Product id is required.", nameof(productId));
        _productAffinities.RemoveAll(a => a.ProductId == productId);
        _productAffinities.Add(new ChannelProductAffinity(productId, bias));
    }
}

/// <summary>Owned by Channel: reach into one segment.</summary>
public sealed class ChannelSegmentReach
{
    public Guid ClientSegmentId { get; private set; }
    public double Reach { get; private set; }

    private ChannelSegmentReach() { } // EF

    public ChannelSegmentReach(Guid clientSegmentId, double reach)
    {
        ClientSegmentId = clientSegmentId;
        Reach = reach;
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
