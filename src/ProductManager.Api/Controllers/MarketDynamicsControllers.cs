using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Domain.Imports;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Market participants: prospects, our clients, competitors' clients, and synthetic firms generated
/// to fill the unobserved market. The epistemic status says which kind of knowledge a row is.
/// </summary>
[Route("api/firms")]
public sealed class FirmsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<FirmDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<FirmDto>> List(
        [FromQuery] Guid? marketId,
        [FromQuery] Guid? clientSegmentId,
        [FromQuery] EpistemicStatus? epistemicStatus,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Firms.AsNoTracking()
            .Where(f => (marketId == null || f.MarketId == marketId)
                        && (clientSegmentId == null || f.ClientSegmentId == clientSegmentId)
                        && (epistemicStatus == null || f.EpistemicStatus == epistemicStatus))
            .OrderBy(f => f.Name)
            .ToPagedResponseAsync(paging, MarketDynamicsMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FirmDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FirmDto>> Get(Guid id, CancellationToken ct)
    {
        var firm = await db.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return firm is null ? NotFoundProblem("Firm", id) : Ok(firm.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(FirmDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<FirmDto>> Create([FromBody] CreateFirmRequest request, CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);
        if (request.ClientSegmentId is not null &&
            !await db.ClientSegments.AnyAsync(s => s.Id == request.ClientSegmentId, ct))
            return NotFoundProblem("ClientSegment", request.ClientSegmentId.Value);

        var firm = new Firm(request.MarketId, request.Name, request.EpistemicStatus, request.ClientSegmentId)
        {
            AttributesJson = request.AttributesJson,
            Notes = request.Notes
        };
        db.Firms.Add(firm);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = firm.Id }, firm.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FirmDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FirmDto>> Update(Guid id, [FromBody] UpdateFirmRequest request, CancellationToken ct)
    {
        var firm = await db.Firms.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (firm is null) return NotFoundProblem("Firm", id);
        if (request.ClientSegmentId is not null &&
            !await db.ClientSegments.AnyAsync(s => s.Id == request.ClientSegmentId, ct))
            return NotFoundProblem("ClientSegment", request.ClientSegmentId.Value);

        firm.Rename(request.Name);
        firm.SetEpistemicStatus(request.EpistemicStatus);
        firm.ClientSegmentId = request.ClientSegmentId;
        firm.AttributesJson = request.AttributesJson;
        firm.Notes = request.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(firm.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var firm = await db.Firms.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (firm is null) return NotFoundProblem("Firm", id);

        db.Firms.Remove(firm);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Imports a whole roster into one market in a single call, relationships included. Items without
    /// a source match by natural key (market, name), so re-importing the same roster corrects it
    /// rather than duplicating it. Items with a source match by source identity, update only what
    /// they carry, and report per item what happened — see <see cref="FirmImportItem"/>. One item's
    /// exception never stops the others; everything that applies is saved together.
    /// </summary>
    [HttpPost("bulk")]
    [ProducesResponseType(typeof(BulkImportFirmsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BulkImportFirmsResponse>> BulkImport([FromBody] BulkImportFirmsRequest request,
        CancellationToken ct)
    {
        var market = await db.Markets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.MarketId, ct);
        if (market is null) return NotFoundProblem("Market", request.MarketId);

        var segmentIds = await db.ClientSegments.AsNoTracking()
            .Where(s => s.MarketId == request.MarketId)
            .Select(s => s.Id)
            .ToListAsync(ct);
        var productIds = await db.Products.AsNoTracking()
            .Where(p => p.MarketId == request.MarketId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var item in request.Firms)
        {
            if (item.ClientSegmentId is not null && !segmentIds.Contains(item.ClientSegmentId.Value))
                return NotFoundProblem("ClientSegment", item.ClientSegmentId.Value);
            ValidateClears(item.ClearFields, item.Source is not null, FirmClearable, item.Name, new()
            {
                ["clientSegmentId"] = item.ClientSegmentId is not null,
                ["attributesJson"] = item.AttributesJson is not null,
                ["notes"] = item.Notes is not null
            });
            foreach (var relationship in item.Relationships ?? [])
            {
                if (!productIds.Contains(relationship.ProductId))
                    return NotFoundProblem("Product", relationship.ProductId);
                ValidateClears(relationship.ClearFields, relationship.Sources is { Count: > 0 }, RelationshipClearable,
                    item.Name, new()
                    {
                        ["acquisitionDate"] = relationship.AcquisitionDate is not null,
                        ["endDate"] = relationship.EndDate is not null,
                        ["pricePaidPerPeriod"] = relationship.PricePaidPerPeriod is not null,
                        ["servedProcessIds"] = relationship.ServedProcessIds is not null
                    });
            }
        }

        var existing = await db.Firms.Where(f => f.MarketId == request.MarketId).ToListAsync(ct);
        var byName = existing
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // ponytail: loads the workspace's firm/relationship bindings whole; page by key if rosters reach 100k+.
        bool anySource = request.Firms.Any(f =>
            f.Source is not null || (f.Relationships ?? []).Any(r => r.Sources is { Count: > 0 }));
        var import = new RosterImport(market.WorkspaceId, request.MarketId, existing, anySource
            ? await db.SourceBindings
                .Where(b => b.WorkspaceId == market.WorkspaceId
                            && (b.TargetType == nameof(Firm) || b.TargetType == nameof(FirmProductRelationship)))
                .ToListAsync(ct)
            : []);

        int created = 0, updated = 0, unchanged = 0, exceptions = 0, relationshipsWritten = 0;
        var touched = new List<Firm>();
        var outcomes = new List<FirmImportOutcomeDto>();

        for (var index = 0; index < request.Firms.Count; index++)
        {
            var item = request.Firms[index];
            FirmImportOutcomeDto outcome;
            Firm? firm;

            if (item.Source is null)
            {
                string name = item.Name.Trim();
                ImportOutcome legacy;
                if (!byName.TryGetValue(name, out firm))
                {
                    firm = new Firm(request.MarketId, name, item.EpistemicStatus ?? EpistemicStatus.Observed,
                        item.ClientSegmentId);
                    db.Firms.Add(firm);
                    existing.Add(firm);
                    byName[name] = firm;
                    legacy = ImportOutcome.Created;
                }
                else
                {
                    firm.Rename(name);
                    firm.SetEpistemicStatus(item.EpistemicStatus ?? EpistemicStatus.Observed);
                    firm.ClientSegmentId = item.ClientSegmentId;
                    legacy = ImportOutcome.Updated;
                }

                firm.AttributesJson = item.AttributesJson;
                firm.Notes = item.Notes;
                outcome = new FirmImportOutcomeDto(index, name, null, firm.Id, legacy, null, [], []);
            }
            else
            {
                (firm, outcome) = await ImportSourcedFirmAsync(index, item, import, ct);
            }

            var relationshipOutcomes = new List<RelationshipImportOutcomeDto>();
            if (firm is not null)
            {
                var relationships = item.Relationships ?? [];
                for (var r = 0; r < relationships.Count; r++)
                {
                    var relationship = relationships[r].Sources is { Count: > 0 }
                        ? await ImportSourcedRelationshipAsync(r, firm, relationships[r], import, ct)
                        : await ImportLegacyRelationshipAsync(r, firm, relationships[r], ct);
                    if (relationship.Outcome is ImportOutcome.Created or ImportOutcome.Updated) relationshipsWritten++;
                    relationshipOutcomes.Add(relationship);
                }

                touched.Add(firm);
            }

            switch (outcome.Outcome)
            {
                case ImportOutcome.Created: created++; break;
                case ImportOutcome.Updated: updated++; break;
                case ImportOutcome.Unchanged: unchanged++; break;
                default: exceptions++; break;
            }

            outcomes.Add(outcome with { Relationships = relationshipOutcomes });
        }

        await db.SaveChangesAsync(ct);
        return Ok(new BulkImportFirmsResponse(request.MarketId, created, updated, relationshipsWritten,
            touched.Distinct().Select(MarketDynamicsMapping.ToDto).ToList(), unchanged, exceptions, outcomes));
    }

    private static readonly string[] FirmClearable = ["clientSegmentId", "attributesJson", "notes"];
    private static readonly string[] RelationshipClearable = ["acquisitionDate", "endDate", "pricePaidPerPeriod", "servedProcessIds"];

    /// <summary>State shared by one roster import: the market's firms and the workspace's bindings, both kept current as items apply.</summary>
    private sealed class RosterImport(Guid workspaceId, Guid marketId, List<Firm> firms, List<SourceBinding> bindings)
    {
        public Guid WorkspaceId { get; } = workspaceId;
        public Guid MarketId { get; } = marketId;
        public List<Firm> Firms { get; } = firms;
        public List<SourceBinding> Bindings { get; } = bindings;
        public HashSet<SourceRef> SeenSources { get; } = [];
        public HashSet<(Guid FirmId, Guid ProductId)> SourcedEdges { get; } = [];

        public SourceBinding? Find(SourceRef source) => Bindings.FirstOrDefault(b => b.Matches(source));
    }

    /// <summary>A clear list only means something for a source-bound item, and only for known, clearable, absent fields.</summary>
    private static void ValidateClears(IReadOnlyList<string>? clears, bool sourced, string[] clearable, string item,
        Dictionary<string, bool> present)
    {
        if (clears is not { Count: > 0 }) return;
        if (!sourced)
            throw new ArgumentException($"'{item}': clearFields applies only to items with a source.");
        foreach (var field in clears)
        {
            if (!clearable.Contains(field))
                throw new ArgumentException(
                    $"'{item}': '{field}' cannot be cleared (clearable: {string.Join(", ", clearable)}).");
            if (present[field])
                throw new ArgumentException($"'{item}': '{field}' is both supplied and cleared.");
        }
    }

    private async Task<(Firm? Firm, FirmImportOutcomeDto Outcome)> ImportSourcedFirmAsync(int index,
        FirmImportItem item, RosterImport import, CancellationToken ct)
    {
        var source = item.Source!;
        string name = item.Name.Trim();
        (Firm?, FirmImportOutcomeDto) Refuse(string reason) =>
            (null, new FirmImportOutcomeDto(index, name, source, null, ImportOutcome.Exception, reason, [], []));

        if (!import.SeenSources.Add(source))
            return Refuse($"Source record '{source.SourceRecordId}' appears more than once in this import.");

        var binding = import.Find(source);
        if (binding is not null && binding.TargetType != nameof(Firm))
            return Refuse($"Source record '{source.SourceRecordId}' is already bound to a {binding.TargetType}.");
        var firm = binding is null ? null : import.Firms.FirstOrDefault(f => f.Id == binding.TargetId);
        if (binding is not null && firm is null && await db.Firms.AnyAsync(f => f.Id == binding.TargetId, ct))
            return Refuse($"Source record '{source.SourceRecordId}' is bound to a firm in another market.");
        bool newBinding = firm is null; // unbound, or bound to a firm since deleted

        if (firm is null)
        {
            var sameName = import.Firms.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameName.Count > 1)
                return Refuse($"Several firms are named '{name}'; supply a distinct, reviewed display name.");
            if (sameName.Count == 1)
            {
                var rival = import.Bindings.FirstOrDefault(b => b.TargetType == nameof(Firm)
                                                                && b.TargetId == sameName[0].Id
                                                                && b.SourceSystem == source.SourceSystem
                                                                && b.SourceInstance == source.SourceInstance
                                                                && b.ObjectKind == source.ObjectKind);
                if (rival is not null)
                    return Refuse(
                        $"Indistinguishable same-name firms: '{name}' is already bound to {source.ObjectKind} " +
                        $"'{rival.SourceRecordId}'. Supply a distinct, reviewed display name for " +
                        $"'{source.SourceRecordId}'.");
                firm = sameName[0]; // adopt the name-loaded firm
            }
        }

        bool create = firm is null;
        firm ??= new Firm(import.MarketId, name, item.EpistemicStatus ?? EpistemicStatus.Observed);
        bool Clears(string field) => item.ClearFields?.Contains(field) == true;

        var merge = new FieldMerge(newBinding ? null : binding!.ImportedValuesJson);
        var newName = merge.Merge("name", firm.Name, true, name, ImportValues.Of);
        var segment = merge.Merge("clientSegmentId", firm.ClientSegmentId,
            item.ClientSegmentId is not null || Clears("clientSegmentId"), item.ClientSegmentId, ImportValues.Of);
        var status = merge.Merge("epistemicStatus", firm.EpistemicStatus, item.EpistemicStatus is not null,
            item.EpistemicStatus ?? firm.EpistemicStatus, ImportValues.Of);
        var attributes = merge.Merge("attributesJson", firm.AttributesJson,
            item.AttributesJson is not null || Clears("attributesJson"), item.AttributesJson, ImportValues.Of, json: true);
        var notes = merge.Merge("notes", firm.Notes, item.Notes is not null || Clears("notes"), item.Notes,
            ImportValues.Of);

        if (!create && import.Firms.Any(f => f != firm && string.Equals(f.Name, newName, StringComparison.OrdinalIgnoreCase)))
            return Refuse($"Renaming '{firm.Name}' to '{newName}' collides with another firm in this market.");

        if (create)
        {
            db.Firms.Add(firm);
            import.Firms.Add(firm);
        }

        firm.Rename(newName);
        firm.ClientSegmentId = segment;
        firm.SetEpistemicStatus(status);
        firm.AttributesJson = attributes;
        firm.Notes = notes;

        if (newBinding)
        {
            var stale = binding;
            binding = db.Bind(stale, import.WorkspaceId, source, nameof(Firm), firm.Id);
            if (stale is null) import.Bindings.Add(binding);
        }

        binding!.RecordImportedValues(merge.ImportedJson);
        var outcome = create ? ImportOutcome.Created
            : merge.Changed || newBinding ? ImportOutcome.Updated
            : ImportOutcome.Unchanged;
        return (firm, new FirmImportOutcomeDto(index, name, source, firm.Id, outcome, null, merge.Conflicts, []));
    }

    /// <summary>The pre-PRD6 edge upsert, unchanged apart from <c>endDate</c> (written only when supplied).</summary>
    private async Task<RelationshipImportOutcomeDto> ImportLegacyRelationshipAsync(int index, Firm firm,
        FirmImportRelationship incoming, CancellationToken ct)
    {
        var relationship = await db.FirmProductRelationships
            .FirstOrDefaultAsync(r => r.FirmId == firm.Id && r.ProductId == incoming.ProductId, ct);
        var outcome = ImportOutcome.Updated;
        if (relationship is null)
        {
            relationship = new FirmProductRelationship(firm.Id, incoming.ProductId, incoming.AcquisitionDate,
                incoming.PricePaidPerPeriod?.ToDomain());
            db.FirmProductRelationships.Add(relationship);
            outcome = ImportOutcome.Created;
        }
        else
        {
            if (incoming.EndDate is not null) relationship.Reopen();
            relationship.SetAcquisitionDate(incoming.AcquisitionDate);
            relationship.SetPricePaid(incoming.PricePaidPerPeriod?.ToDomain());
        }

        if (incoming.EndDate is { } end) relationship.End(end);
        relationship.SetServedProcesses(incoming.ServedProcessIds ?? []);
        relationship.SetPriceLevel(incoming.PriceLevel ?? 1.0);
        return new RelationshipImportOutcomeDto(index, incoming.ProductId, relationship.Id, outcome, null, []);
    }

    private async Task<RelationshipImportOutcomeDto> ImportSourcedRelationshipAsync(int index, Firm firm,
        FirmImportRelationship incoming, RosterImport import, CancellationToken ct)
    {
        const string edgeType = nameof(FirmProductRelationship);
        RelationshipImportOutcomeDto Refuse(string reason) =>
            new(index, incoming.ProductId, null, ImportOutcome.Exception, reason, []);

        if (!import.SourcedEdges.Add((firm.Id, incoming.ProductId)))
            return Refuse("This product appears more than once for this firm in one import; the model keeps one " +
                             "episode per firm/product edge.");
        var sources = incoming.Sources!.Distinct().ToList();
        if (sources.Any(s => !import.SeenSources.Add(s)))
            return Refuse("A listed source record appears more than once in this import.");

        var edges = await db.FirmProductRelationships
            .Where(r => r.FirmId == firm.Id && r.ProductId == incoming.ProductId)
            .ToListAsync(ct);
        edges.AddRange(db.FirmProductRelationships.Local.Where(r =>
            r.FirmId == firm.Id && r.ProductId == incoming.ProductId && !edges.Contains(r)));
        if (edges.Count > 1)
            return Refuse("Several relationships already exist for this firm and product; resolve them first.");
        var edge = edges.SingleOrDefault();

        var listed = sources.Select(s => (Source: s, Binding: import.Find(s))).ToList();
        foreach (var (source, binding) in listed)
        {
            if (binding is null) continue;
            if (binding.TargetType != edgeType)
                return Refuse($"Source record '{source.SourceRecordId}' is already bound to a {binding.TargetType}.");
            // Bound to another edge that still exists; one whose edge was deleted is simply re-pointed below.
            if (binding.TargetId != edge?.Id && await db.FirmProductRelationships.AnyAsync(r => r.Id == binding.TargetId, ct))
                return Refuse($"Source record '{source.SourceRecordId}' is already bound to another relationship.");
        }

        var boundHere = edge is null
            ? []
            : import.Bindings.Where(b => b.TargetType == edgeType && b.TargetId == edge.Id).ToList();
        var reference = listed.Select(l => l.Binding).FirstOrDefault(b => b is not null && boundHere.Contains(b));
        if (boundHere.Count > 0 && reference is null)
            return Refuse(
                $"The relationship is bound to source record(s) {string.Join(", ", boundHere.Select(b => $"'{b.SourceRecordId}'"))}; " +
                "list them together with the new record(s) to consolidate deliberately.");
        bool addsSource = listed.Any(l => l.Binding is null || !boundHere.Contains(l.Binding));
        if (edge is not null && addsSource && !edge.DescribesSameEpisode(incoming.AcquisitionDate, incoming.EndDate))
            return Refuse("The new source record describes a different episode than the stored relationship " +
                             $"({edge.AcquisitionDate:yyyy-MM-dd} to {edge.EndDate?.ToString("yyyy-MM-dd") ?? "open"}); " +
                             "the model keeps one episode per firm/product edge.");

        bool Clears(string field) => incoming.ClearFields?.Contains(field) == true;
        var merge = new FieldMerge(reference?.ImportedValuesJson);
        var acquired = merge.Merge("acquisitionDate", edge?.AcquisitionDate,
            incoming.AcquisitionDate is not null || Clears("acquisitionDate"), incoming.AcquisitionDate, ImportValues.Of);
        var ended = merge.Merge("endDate", edge?.EndDate, incoming.EndDate is not null || Clears("endDate"),
            incoming.EndDate, ImportValues.Of);
        var pricePaid = merge.Merge("pricePaidPerPeriod", edge?.PricePaidPerPeriod,
            incoming.PricePaidPerPeriod is not null || Clears("pricePaidPerPeriod"), incoming.PricePaidPerPeriod?.ToDomain(),
            ImportValues.Of);
        var priceLevel = merge.Merge("priceLevel", edge?.PriceLevel ?? 1.0, incoming.PriceLevel is not null,
            incoming.PriceLevel ?? 1.0, ImportValues.Of);
        var served = merge.Merge<IReadOnlyList<Guid>>("servedProcessIds", edge?.ServedProcessIds ?? [],
            incoming.ServedProcessIds is not null || Clears("servedProcessIds"), incoming.ServedProcessIds ?? [],
            ImportValues.Of);
        if (acquired is { } start && ended is { } stop && stop < start)
            return Refuse($"The relationship would end ({stop:yyyy-MM-dd}) before it was acquired ({start:yyyy-MM-dd}).");

        bool create = edge is null;
        if (edge is null)
        {
            edge = new FirmProductRelationship(firm.Id, incoming.ProductId);
            db.FirmProductRelationships.Add(edge);
        }

        edge.Reopen();
        edge.SetAcquisitionDate(acquired);
        if (ended is { } end) edge.End(end);
        edge.SetPricePaid(pricePaid);
        edge.SetPriceLevel(priceLevel);
        edge.SetServedProcesses(served);

        foreach (var (source, existingBinding) in listed)
        {
            var binding = existingBinding;
            if (binding is null || binding.TargetId != edge.Id)
            {
                binding = db.Bind(existingBinding, import.WorkspaceId, source, edgeType, edge.Id);
                if (existingBinding is null) import.Bindings.Add(binding);
            }

            binding.RecordImportedValues(merge.ImportedJson);
        }

        var outcome = create ? ImportOutcome.Created
            : merge.Changed || addsSource ? ImportOutcome.Updated
            : ImportOutcome.Unchanged;
        return new RelationshipImportOutcomeDto(index, incoming.ProductId, edge.Id, outcome, null, merge.Conflicts);
    }

    /// <summary>
    /// Fills every segment's measured stocks with deterministically generated synthetic firms, so a
    /// firm-level simulation runs over the whole market and not only the part we happen to have
    /// names for. Regeneration <em>replaces</em> the previous synthetic population: identities are
    /// hashes of (market, segment, role, index), so running this twice is a no-op rather than a
    /// doubling.
    /// </summary>
    [HttpPost("generate-synthetic")]
    [ProducesResponseType(typeof(GenerateSyntheticFirmsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenerateSyntheticFirmsResponse>> GenerateSynthetic(
        [FromBody] GenerateSyntheticFirmsRequest request, CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);

        var segments = await db.ClientSegments
            .Where(s => s.MarketId == request.MarketId)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var products = await db.Products
            .Where(p => p.MarketId == request.MarketId)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        // Clear the previous generation first: a synthetic population is a derived artifact of the
        // stocks, not an accumulating roster.
        var previous = await db.Firms
            .Where(f => f.MarketId == request.MarketId && f.EpistemicStatus == EpistemicStatus.Synthetic)
            .ToListAsync(ct);
        var previousIds = previous.Select(f => f.Id).ToList();
        var previousRelationships = await db.FirmProductRelationships
            .Where(r => previousIds.Contains(r.FirmId))
            .ToListAsync(ct);
        db.FirmProductRelationships.RemoveRange(previousRelationships);
        db.Firms.RemoveRange(previous);
        await db.SaveChangesAsync(ct);

        var observed = await db.Firms.AsNoTracking()
            .Where(f => f.MarketId == request.MarketId && f.EpistemicStatus != EpistemicStatus.Synthetic)
            .ToListAsync(ct);
        var observedIds = observed.Select(f => f.Id).ToList();

        var ourProductIds = products.Where(p => p.IsOurs).Select(p => p.Id).ToList();
        var clientFirmIds = await db.FirmProductRelationships.AsNoTracking()
            .Where(r => r.EndDate == null && observedIds.Contains(r.FirmId) && ourProductIds.Contains(r.ProductId))
            .Select(r => r.FirmId)
            .ToListAsync(ct);
        var clientFirmIdSet = clientFirmIds.ToHashSet();

        var fills = new List<SyntheticSegmentFill>();
        foreach (var segment in segments)
        {
            var flagship = products.FirstOrDefault(p =>
                p.IsOurs && !p.IsModule
                && p.Prices.Any(price => price.ClientSegmentId == segment.Id || price.ClientSegmentId is null));

            var modules = segment.ModulePenetrations
                .OrderBy(m => m.ModuleProductId)
                .Select(m => (Module: products.FirstOrDefault(p => p.Id == m.ModuleProductId), m.Penetration))
                .Where(m => m.Module is not null && m.Module.IsOurs)
                .Select(m => (Module: m.Module!, m.Penetration))
                .ToList();

            fills.Add(SyntheticFirmGenerator.Generate(request.MarketId, new SyntheticFirmGenerator.SegmentInputs(
                segment,
                observed.Where(f => f.ClientSegmentId == segment.Id).ToList(),
                clientFirmIdSet,
                flagship,
                modules)));
        }

        foreach (var fill in fills)
        {
            db.Firms.AddRange(fill.Firms);
            db.FirmProductRelationships.AddRange(fill.Relationships);
        }

        await db.SaveChangesAsync(ct);

        return Ok(new GenerateSyntheticFirmsResponse(
            request.MarketId,
            previous.Count,
            fills.Sum(f => f.Firms.Count),
            fills.Sum(f => f.Relationships.Count),
            fills.Select(f => new SyntheticSegmentFillDto(
                f.ClientSegmentId, f.ProspectTarget, f.ObservedProspects, f.SyntheticProspects,
                f.ClientTarget, f.ObservedClients, f.SyntheticClients)).ToList()));
    }
}

/// <summary>
/// The demand edge: what a firm — or a whole segment — must accomplish, how much it matters, and
/// what the workaround costs when it goes unmet.
/// </summary>
[Route("api/needs")]
public sealed class NeedsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<NeedDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<NeedDto>> List(
        [FromQuery] Guid? firmId,
        [FromQuery] Guid? clientSegmentId,
        [FromQuery] Guid? businessProcessId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Needs.AsNoTracking()
            .Where(n => (firmId == null || n.FirmId == firmId)
                        && (clientSegmentId == null || n.ClientSegmentId == clientSegmentId)
                        && (businessProcessId == null || n.BusinessProcessId == businessProcessId))
            .OrderByDescending(n => n.ImportanceWeight).ThenBy(n => n.Id)
            .ToPagedResponseAsync(paging, MarketDynamicsMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NeedDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NeedDto>> Get(Guid id, CancellationToken ct)
    {
        var need = await db.Needs.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        return need is null ? NotFoundProblem("Need", id) : Ok(need.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(NeedDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<NeedDto>> Create([FromBody] CreateNeedRequest request, CancellationToken ct)
    {
        if ((request.FirmId is null) == (request.ClientSegmentId is null))
            return Problem(detail: "A need belongs to exactly one of a firm or a segment.",
                statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");
        if (!await db.BusinessProcesses.AnyAsync(p => p.Id == request.BusinessProcessId, ct))
            return NotFoundProblem("BusinessProcess", request.BusinessProcessId);
        if (request.FirmId is not null && !await db.Firms.AnyAsync(f => f.Id == request.FirmId, ct))
            return NotFoundProblem("Firm", request.FirmId.Value);
        if (request.ClientSegmentId is not null &&
            !await db.ClientSegments.AnyAsync(s => s.Id == request.ClientSegmentId, ct))
            return NotFoundProblem("ClientSegment", request.ClientSegmentId.Value);

        var workaround = request.WorkaroundCostPerPeriod?.ToDomain();
        var need = request.FirmId is not null
            ? Need.ForFirm(request.FirmId.Value, request.BusinessProcessId, request.ImportanceWeight,
                request.IsHard, workaround)
            : Need.ForSegment(request.ClientSegmentId!.Value, request.BusinessProcessId, request.ImportanceWeight,
                request.IsHard, workaround);

        need.RequiredDepthJson = request.RequiredDepthJson;
        need.Conditionality = request.Conditionality;
        need.Notes = request.Notes;

        db.Needs.Add(need);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = need.Id }, need.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(NeedDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NeedDto>> Update(Guid id, [FromBody] UpdateNeedRequest request, CancellationToken ct)
    {
        var need = await db.Needs.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (need is null) return NotFoundProblem("Need", id);

        need.SetImportance(request.ImportanceWeight);
        need.SetHardness(request.IsHard, request.WorkaroundCostPerPeriod?.ToDomain());
        need.RequiredDepthJson = request.RequiredDepthJson;
        need.Conditionality = request.Conditionality;
        need.Notes = request.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(need.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var need = await db.Needs.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (need is null) return NotFoundProblem("Need", id);

        db.Needs.Remove(need);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>
/// Typed shocks that modulate simulated flows for a duration. Real events mirror the facts ledger;
/// hypothetical ones are injected into a scenario — "with the incident" vs "without".
/// </summary>
[Route("api/market-events")]
public sealed class MarketEventsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<MarketEventDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<MarketEventDto>> List(
        [FromQuery] Guid? marketId,
        [FromQuery] bool? isHypothetical,
        [FromQuery] Guid? competitorId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.MarketEvents.AsNoTracking()
            .Where(e => (marketId == null || e.MarketId == marketId)
                        && (isHypothetical == null || e.IsHypothetical == isHypothetical)
                        && (competitorId == null || e.CompetitorId == competitorId))
            .OrderBy(e => e.StartDate).ThenBy(e => e.Name)
            .ToPagedResponseAsync(paging, MarketDynamicsMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MarketEventDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarketEventDto>> Get(Guid id, CancellationToken ct)
    {
        var marketEvent = await db.MarketEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return marketEvent is null ? NotFoundProblem("MarketEvent", id) : Ok(marketEvent.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(MarketEventDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<MarketEventDto>> Create([FromBody] CreateMarketEventRequest request,
        CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);
        if (request.CompetitorId is not null && !await db.Competitors.AnyAsync(c => c.Id == request.CompetitorId, ct))
            return NotFoundProblem("Competitor", request.CompetitorId.Value);

        var marketEvent = new MarketEvent(request.MarketId, request.Type, request.Name, request.StartDate,
            request.IsHypothetical, request.EndDate)
        {
            Magnitude = request.Magnitude,
            CompetitorId = request.CompetitorId,
            Notes = request.Notes
        };
        marketEvent.ReplaceRateMultipliers(request.RateMultipliers.ToDomain());

        db.MarketEvents.Add(marketEvent);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = marketEvent.Id }, marketEvent.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(MarketEventDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarketEventDto>> Update(Guid id, [FromBody] UpdateMarketEventRequest request,
        CancellationToken ct)
    {
        var marketEvent = await db.MarketEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (marketEvent is null) return NotFoundProblem("MarketEvent", id);
        if (request.CompetitorId is not null && !await db.Competitors.AnyAsync(c => c.Id == request.CompetitorId, ct))
            return NotFoundProblem("Competitor", request.CompetitorId.Value);

        marketEvent.Rename(request.Name);
        marketEvent.SetType(request.Type);
        marketEvent.Reschedule(request.StartDate, request.EndDate);
        marketEvent.SetHypothetical(request.IsHypothetical);
        marketEvent.Magnitude = request.Magnitude;
        marketEvent.CompetitorId = request.CompetitorId;
        marketEvent.Notes = request.Notes;
        marketEvent.ReplaceRateMultipliers(request.RateMultipliers.ToDomain());

        await db.SaveChangesAsync(ct);
        return Ok(marketEvent.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var marketEvent = await db.MarketEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (marketEvent is null) return NotFoundProblem("MarketEvent", id);

        db.MarketEvents.Remove(marketEvent);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
