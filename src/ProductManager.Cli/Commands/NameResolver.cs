using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>
/// Resolves segment/channel/product/business-process ids to their names for human-facing CLI
/// tables — the firm-level funnel diagnostics table ('sim run --mode firm-level', 'sim diagnostics')
/// and the calibration diff table ('calibrate run'/'diff', via <see cref="ParameterKeyTranslator"/>).
///
/// Built once per command invocation (<see cref="BuildAsync"/>) by walking every market in the
/// target workspace and paging through each catalog endpoint — client-segments, channels, products,
/// business-processes are all keyed by market rather than workspace directly, so a workspace-wide
/// name map means listing markets first. There is no persistent cache: a fresh instance is built,
/// used to render one command's output, and discarded.
///
/// Construction never throws: a fetch failure (offline, an expired token mid-command, a slow API)
/// is swallowed and whatever was already collected is kept, so a name-resolution problem degrades
/// individual cells to their short-guid form instead of crashing a command that otherwise completed
/// successfully. The public constructor (bypassing I/O entirely) exists so tests can supply a fake
/// name map directly.
/// </summary>
public sealed class NameResolver
{
    private const int PageSize = 200; // matches the API's PagedResponse.MaxPageSize

    private readonly IReadOnlyDictionary<Guid, string> _segments;
    private readonly IReadOnlyDictionary<Guid, string> _channels;
    private readonly IReadOnlyDictionary<Guid, string> _products;
    private readonly IReadOnlyDictionary<Guid, string> _businessProcesses;

    public NameResolver(
        IReadOnlyDictionary<Guid, string>? segments = null,
        IReadOnlyDictionary<Guid, string>? channels = null,
        IReadOnlyDictionary<Guid, string>? products = null,
        IReadOnlyDictionary<Guid, string>? businessProcesses = null)
    {
        _segments = segments ?? new Dictionary<Guid, string>();
        _channels = channels ?? new Dictionary<Guid, string>();
        _products = products ?? new Dictionary<Guid, string>();
        _businessProcesses = businessProcesses ?? new Dictionary<Guid, string>();
    }

    /// <summary>An empty resolver — every lookup falls back to the short-guid form. Handy as a
    /// no-op default without needing a nullable parameter at every call site.</summary>
    public static readonly NameResolver Empty = new();

    /// <summary>Fetches every market in <paramref name="workspaceId"/>, then every segment, channel,
    /// product, and business process within each of those markets, and builds the name maps from
    /// them. Never throws — see the class doc.</summary>
    public static async Task<NameResolver> BuildAsync(ProductManagerApiClient client, Guid workspaceId, CancellationToken ct)
    {
        var segments = new Dictionary<Guid, string>();
        var channels = new Dictionary<Guid, string>();
        var products = new Dictionary<Guid, string>();
        var businessProcesses = new Dictionary<Guid, string>();

        try
        {
            var markets = await FetchAllAsync(
                (page, ct2) => client.ListMarketsAsync(workspaceId, page, PageSize, ct2), ct);

            foreach (var market in markets)
            {
                foreach (var s in await FetchAllAsync((page, ct2) => client.ListClientSegmentsAsync(market.Id, page, PageSize, ct2), ct))
                    segments[s.Id] = s.Name;

                foreach (var c in await FetchAllAsync((page, ct2) => client.ListChannelsAsync(market.Id, null, page, PageSize, ct2), ct))
                    channels[c.Id] = c.Name;

                foreach (var p in await FetchAllAsync((page, ct2) => client.ListProductsAsync(market.Id, null, null, page, PageSize, ct2), ct))
                    products[p.Id] = p.Name;

                foreach (var bp in await FetchAllAsync((page, ct2) => client.ListBusinessProcessesAsync(market.Id, null, page, PageSize, ct2), ct))
                    businessProcesses[bp.Id] = bp.Name;
            }
        }
        catch (ApiClientException)
        {
            // Offline, unreachable, or the token expired mid-fetch. Whatever was collected before
            // the failure is still used; anything missing falls back to its short-guid form. A name
            // lookup is a display nicety, not something worth failing an otherwise-successful command over.
        }

        return new NameResolver(segments, channels, products, businessProcesses);
    }

    private static async Task<List<T>> FetchAllAsync<T>(
        Func<int, CancellationToken, Task<PagedResponse<T>>> fetchPage, CancellationToken ct)
    {
        var items = new List<T>();
        var page = 1;
        while (true)
        {
            var response = await fetchPage(page, ct);
            items.AddRange(response.Items);
            if (response.Items.Count == 0 || items.Count >= response.TotalCount) break;
            page++;
        }
        return items;
    }

    /// <summary>Resolves a firm-level funnel row's segment id — a raw string that is usually a Guid
    /// but may already be a readable sentinel (see <see cref="GuidDisplay.ResolveOrPassThrough"/>).</summary>
    public string Segment(string idOrName) => GuidDisplay.ResolveOrPassThrough(_segments, idOrName);

    /// <summary>Resolves a firm-level funnel row's channel id — same caveat as <see cref="Segment(string)"/>
    /// (e.g. "direct" is already a name, not an id to look up).</summary>
    public string Channel(string idOrName) => GuidDisplay.ResolveOrPassThrough(_channels, idOrName);

    /// <summary>The maps a calibration parameter key needs to translate into a display name.</summary>
    public ParameterNameMaps ParameterMaps => new(_segments, _products, _businessProcesses);
}
