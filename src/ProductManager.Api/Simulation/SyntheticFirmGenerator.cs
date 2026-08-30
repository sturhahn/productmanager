using System.Security.Cryptography;
using System.Text;
using ProductManager.Domain.Common;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Simulation;

/// <summary>One segment's synthetic fill: the roster additions and what they were filling.</summary>
public sealed record SyntheticSegmentFill(
    Guid ClientSegmentId,
    int ProspectTarget,
    int ObservedProspects,
    int SyntheticProspects,
    int ClientTarget,
    int ObservedClients,
    int SyntheticClients,
    IReadOnlyList<Firm> Firms,
    IReadOnlyList<FirmProductRelationship> Relationships);

/// <summary>
/// Generates the firms nobody has observed: the difference between a segment's measured stocks and
/// the roster we actually hold names for.
///
/// <para>
/// Generation is <b>deterministic</b> — every identity is a hash of (market, segment, role, index),
/// so regenerating a market produces exactly the same firms rather than a second population beside
/// the first. That is what makes "regenerate" a safe, repeatable operation and what lets a
/// firm-level run stay reproducible across regenerations of the same stocks.
/// </para>
///
/// <para>
/// Synthetic firms carry no latent attributes: importance weights and price sensitivity are sampled
/// per Monte Carlo path from their segment's distributions inside the engine. Persisting a sampled
/// value here would freeze one draw and quietly destroy the parameter uncertainty the whole design
/// is built to preserve.
/// </para>
/// </summary>
public static class SyntheticFirmGenerator
{
    /// <summary>Everything the generator needs about one segment's world.</summary>
    public sealed record SegmentInputs(
        ClientSegment Segment,
        IReadOnlyList<Firm> ObservedFirms,
        IReadOnlySet<Guid> ClientFirmIds,
        Product? FlagshipProduct,
        IReadOnlyList<(Product Module, double Penetration)> Modules);

    public static SyntheticSegmentFill Generate(Guid marketId, SegmentInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var segment = inputs.Segment;

        int prospectTarget = (int)Math.Round(segment.StockState.InitialProspectPool, MidpointRounding.AwayFromZero);
        int clientTarget = segment.StockState.InitialClientCount;

        int observedClients = inputs.ObservedFirms.Count(f => inputs.ClientFirmIds.Contains(f.Id));
        int observedProspects = inputs.ObservedFirms.Count - observedClients;

        // A segment with no priced product of ours cannot have our clients, synthetic or otherwise.
        int syntheticClients = inputs.FlagshipProduct is null ? 0 : Math.Max(0, clientTarget - observedClients);
        int syntheticProspects = Math.Max(0, prospectTarget - observedProspects);

        var firms = new List<Firm>(syntheticClients + syntheticProspects);
        var relationships = new List<FirmProductRelationship>();

        for (int i = 0; i < syntheticClients; i++)
        {
            var firm = Firm.Synthetic(
                DeterministicId(marketId, segment.Id, "client", i),
                marketId,
                segment.Id,
                $"Synthetic {segment.Name} client {i + 1}");
            firms.Add(firm);

            relationships.Add(FirmProductRelationship.WithIdentity(
                DeterministicId(marketId, segment.Id, $"client-rel:{inputs.FlagshipProduct!.Id}", i),
                firm.Id,
                inputs.FlagshipProduct.Id));

            // Modules are held by the first round(clients x penetration) synthetic clients: the
            // recorded penetration is a share of the segment's clients, and taking it off the front
            // of a deterministic list keeps regeneration stable.
            foreach (var (module, penetration) in inputs.Modules)
            {
                int holders = (int)Math.Round(syntheticClients * penetration, MidpointRounding.AwayFromZero);
                if (i >= holders) continue;
                relationships.Add(FirmProductRelationship.WithIdentity(
                    DeterministicId(marketId, segment.Id, $"module-rel:{module.Id}", i),
                    firm.Id,
                    module.Id));
            }
        }

        for (int i = 0; i < syntheticProspects; i++)
        {
            firms.Add(Firm.Synthetic(
                DeterministicId(marketId, segment.Id, "prospect", i),
                marketId,
                segment.Id,
                $"Synthetic {segment.Name} prospect {i + 1}"));
        }

        return new SyntheticSegmentFill(
            segment.Id, prospectTarget, observedProspects, syntheticProspects,
            clientTarget, observedClients, syntheticClients, firms, relationships);
    }

    /// <summary>
    /// A stable identity for a generated row: the first 16 bytes of SHA-256 over the canonical
    /// (market, segment, role, index) string. Hashing rather than counting because the id must not
    /// depend on how many other segments were generated first.
    /// </summary>
    public static Guid DeterministicId(Guid marketId, Guid segmentId, string role, int index)
    {
        var payload = Encoding.UTF8.GetBytes($"synthetic|{marketId}|{segmentId}|{role}|{index}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(payload, hash);
        return new Guid(hash[..16]);
    }
}
