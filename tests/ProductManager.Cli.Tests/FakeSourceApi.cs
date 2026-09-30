using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Tests;

/// <summary>
/// A small, stateful stand-in for the API's PRD6 import surface, enough to run the real
/// <see cref="ProductManager.Cli.Seed.SeedLoader"/> end to end: one workspace and market with fixed
/// segments/products/processes/channel, and in-memory firms, relationships, opportunities, facts and
/// source bindings. Its repeat-safety rules mirror the server's (see
/// <c>src/ProductManager.Api/Controllers/OpportunitiesController.cs</c>, <c>FactsAndCovariatesControllers.cs</c>
/// and the bulk import in <c>MarketDynamicsControllers.cs</c>): a bound source returns the bound row, a
/// materially different restatement is a 409 correction-required, identical qualify/decide is a no-op.
/// <para>
/// Fault injection: <see cref="FailWrite"/> makes the Nth write from now fail — with a 500 before it is
/// applied, or (<see cref="LoseResponse"/>) applied and then the response lost.
/// </para>
/// </summary>
internal sealed class FakeSourceApi : HttpMessageHandler
{
    public static readonly Guid WorkspaceId = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid MarketId = Guid.Parse("10000000-0000-0000-0000-00000000000a");
    public static readonly Guid SmbSegmentId = Guid.Parse("20000000-0000-0000-0000-00000000000a");
    public static readonly Guid EnterpriseSegmentId = Guid.Parse("20000000-0000-0000-0000-00000000000b");
    public static readonly Guid OurProductId = Guid.Parse("50000000-0000-0000-0000-00000000000a");
    public static readonly Guid RivalProductId = Guid.Parse("50000000-0000-0000-0000-00000000000b");
    public static readonly Guid PickingProcessId = Guid.Parse("30000000-0000-0000-0000-00000000000a");
    public static readonly Guid ReceivingProcessId = Guid.Parse("30000000-0000-0000-0000-00000000000b");
    public static readonly Guid DirectChannelId = Guid.Parse("a0000000-0000-0000-0000-00000000000a");

    private const string CorrectionRequired = "urn:productmanager:problem:correction-required";

    public List<FakeFirm> Firms { get; } = [];
    public List<FakeRelationship> Relationships { get; } = [];
    public List<FakeOpportunity> Opportunities { get; } = [];
    public List<FakeFact> Facts { get; } = [];
    public List<FakeBinding> Bindings { get; } = [];

    /// <summary>Every non-GET request, in order.</summary>
    public List<(string Method, string Path, string Body)> Writes { get; } = [];

    private int _writesSeen;
    private int? _failAt;
    private bool _loseResponse;

    public ProductManagerApiClient Client => new(new HttpClient(this) { BaseAddress = new Uri("http://fake.local/") });

    /// <summary>Makes the <paramref name="nth"/> write from now fail.</summary>
    public void FailWrite(int nth, bool loseResponse = false)
    {
        _failAt = _writesSeen + nth;
        _loseResponse = loseResponse;
    }

    public void ClearFault() => _failAt = null;

    public FakeFirm AddFirm(string name, Guid? segmentId = null)
    {
        var firm = new FakeFirm { Id = Guid.NewGuid(), Name = name, SegmentId = segmentId ?? SmbSegmentId };
        Firms.Add(firm);
        return firm;
    }

    public void Bind(SourceRef source, string targetType, Guid targetId) =>
        Bindings.Add(new FakeBinding(Guid.NewGuid(), source, targetType) { TargetId = targetId });

    /// <summary>A snapshot of everything stored, for comparing two runs' end states.</summary>
    public string StateFingerprint() => JsonSerializer.Serialize(new
    {
        firms = Firms.Select(f => new { f.Name, f.SegmentId, f.Status, f.Attributes, f.Notes }).OrderBy(f => f.Name),
        relationships = Relationships.Select(r => new
        {
            firm = Firms.Single(f => f.Id == r.FirmId).Name, r.ProductId, r.PriceLevel, r.Acquisition, r.End,
            served = string.Join(",", r.Served.Order())
        }).OrderBy(r => r.firm).ThenBy(r => r.ProductId),
        opportunities = Opportunities.Select(o => new
        {
            firm = Firms.Single(f => f.Id == o.FirmId).Name, o.OpenedAt, o.Stage, o.QualifiedAt, o.DecidedAt,
            o.WinningProductId, o.Notes, o.ChannelId, scope = string.Join(",", o.Scope.Order())
        }).OrderBy(o => o.firm).ThenBy(o => o.OpenedAt).ThenBy(o => o.Notes),
        facts = Facts.Select(f => new { f.Kind, f.EffectiveDate, f.SubjectType, f.Payload })
            .OrderBy(f => f.Kind).ThenBy(f => f.EffectiveDate).ThenBy(f => f.Payload),
        bindings = Bindings.Select(b => b.Source.ToString()).Order()
    });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        var query = ParseQuery(request.RequestUri.Query);
        if (request.Method == HttpMethod.Get) return Get(path, query);

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Writes.Add((request.Method.Method, path, body));
        var fail = ++_writesSeen == _failAt;
        if (fail && !_loseResponse)
            return Json(HttpStatusCode.InternalServerError, new { title = "Unexpected error", status = 500 });

        var response = Write(request.Method.Method, path, body == "" ? null : JsonNode.Parse(body));
        if (fail) throw new HttpRequestException("The response was lost (connection reset).");
        return response;
    }

    // ---------- reads ----------

    private HttpResponseMessage Get(string path, Dictionary<string, string> q)
    {
        var parts = path.Split('/');
        switch (path)
        {
            case "api/workspaces/default":
                return Ok(new { id = WorkspaceId, name = "Default", createdAt = "2026-01-01T00:00:00+00:00" });
            case "api/markets":
                return Page(q, [new { id = MarketId, workspaceId = WorkspaceId, name = "Warehouse Software" }]);
            case "api/client-segments":
                return Page(q, [Named(SmbSegmentId, "SMB"), Named(EnterpriseSegmentId, "Enterprise")]);
            case "api/business-processes":
                return Page(q, [Named(PickingProcessId, "Order Picking"), Named(ReceivingProcessId, "Receiving")]);
            case "api/products":
                return Page(q, [Named(OurProductId, "Acme WMS"), Named(RivalProductId, "Rival WMS")]);
            case "api/channels":
                return Page(q, [Named(DirectChannelId, "Direct")]);
            case "api/firms":
                return Page(q, Firms.Select(FirmDto));
            case "api/firm-relationships":
                return Page(q, Relationships
                    .Where(r => !q.TryGetValue("firmId", out var f) || r.FirmId == Guid.Parse(f))
                    .Where(r => !q.TryGetValue("productId", out var p) || r.ProductId == Guid.Parse(p))
                    .Select(RelationshipDto));
            case "api/opportunities":
                return Page(q, Opportunities
                    .Where(o => !q.TryGetValue("firmId", out var f) || o.FirmId == Guid.Parse(f))
                    .OrderByDescending(o => o.OpenedAt)
                    .Select(OpportunityDto));
            case "api/facts":
                return Page(q, Facts
                    .Where(f => !q.TryGetValue("kind", out var k) || f.Kind == k)
                    .Where(f => !q.TryGetValue("subjectType", out var t) || f.SubjectType == t)
                    .Where(f => !q.TryGetValue("subjectId", out var s) || f.SubjectId == Guid.Parse(s))
                    .Where(f => !q.TryGetValue("effectiveFrom", out var from) || string.CompareOrdinal(f.EffectiveDate, from) >= 0)
                    .Where(f => !q.TryGetValue("effectiveTo", out var to) || string.CompareOrdinal(f.EffectiveDate, to) <= 0)
                    .Select(FactDto));
            case "api/source-bindings":
                return Page(q, Bindings
                    .Where(b => Matches(q, "sourceSystem", b.Source.SourceSystem) && Matches(q, "sourceInstance", b.Source.SourceInstance)
                                && Matches(q, "objectKind", b.Source.ObjectKind) && Matches(q, "sourceRecordId", b.Source.SourceRecordId)
                                && Matches(q, "targetType", b.TargetType)
                                && (!q.TryGetValue("targetId", out var id) || b.TargetId == Guid.Parse(id)))
                    .Select(b => new
                    {
                        id = b.Id, workspaceId = WorkspaceId, b.Source.SourceSystem, b.Source.SourceInstance,
                        b.Source.ObjectKind, b.Source.SourceRecordId, b.TargetType, b.TargetId,
                        boundAt = "2026-01-01T00:00:00+00:00", importedValuesJson = (string?)null
                    }));
        }

        if (parts is ["api", "opportunities", var oppId])
            return Opportunities.FirstOrDefault(o => o.Id == Guid.Parse(oppId)) is { } o ? Ok(OpportunityDto(o)) : NotFound();
        if (parts is ["api", "facts", var factId])
            return Facts.FirstOrDefault(f => f.Id == Guid.Parse(factId)) is { } f ? Ok(FactDto(f)) : NotFound();

        // Any other listing (teams, covariates, ...) is empty in this world.
        return parts.Length == 2 ? Page(q, []) : NotFound();
    }

    private static bool Matches(Dictionary<string, string> q, string key, string value) =>
        !q.TryGetValue(key, out var wanted) || wanted == value;

    // ---------- writes ----------

    private HttpResponseMessage Write(string method, string path, JsonNode? body)
    {
        var parts = path.Split('/');
        return (method, parts) switch
        {
            ("POST", ["api", "firms", "bulk"]) => BulkImport(body!),
            ("POST", ["api", "opportunities"]) => CreateOpportunity(body!),
            ("PUT", ["api", "opportunities", var id]) => UpdateOpportunity(Opp(id), body!),
            ("POST", ["api", "opportunities", var id, "scope"]) => AddScope(Opp(id), body!),
            ("DELETE", ["api", "opportunities", var id, "scope", var processId]) => RemoveScope(Opp(id), Guid.Parse(processId)),
            ("POST", ["api", "opportunities", var id, "qualify"]) => Qualify(Opp(id), body!),
            ("POST", ["api", "opportunities", var id, "decide"]) => Decide(Opp(id), body!),
            ("POST", ["api", "facts"]) => CreateFact(body!),
            _ => Ok(new { id = Guid.NewGuid() })
        };
    }

    private FakeOpportunity Opp(string id) => Opportunities.Single(o => o.Id == Guid.Parse(id));

    private HttpResponseMessage CreateOpportunity(JsonNode body)
    {
        var firmId = Guid.Parse((string)body["firmId"]!);
        var source = Source(body["source"]);
        if (source is not null && FindBinding(source) is { } binding)
        {
            var bound = Opportunities.Single(o => o.Id == binding.TargetId);
            return bound.FirmId == firmId ? Ok(OpportunityDto(bound)) : Correction("Bound to another firm.");
        }

        var openedAt = DateTimeOffset.Parse((string)body["openedAt"]!);
        var opportunity = new FakeOpportunity
        {
            Id = Guid.NewGuid(), FirmId = firmId, OpenedAt = openedAt, ChannelId = Id(body["channelId"]),
            CrossSellModuleProductId = Id(body["crossSellModuleProductId"]), Notes = (string?)body["notes"],
            Scope = Ids(body["scopeProcessIds"])
        };
        Opportunities.Add(opportunity);
        if (source is not null) Bindings.Add(new FakeBinding(Guid.NewGuid(), source, "Opportunity") { TargetId = opportunity.Id });
        return Json(HttpStatusCode.Created, OpportunityDto(opportunity));
    }

    private HttpResponseMessage UpdateOpportunity(FakeOpportunity o, JsonNode body)
    {
        o.ChannelId = Id(body["channelId"]);
        o.CrossSellModuleProductId = Id(body["crossSellModuleProductId"]);
        o.CompetitorIds = Ids(body["competitorIds"]);
        o.Notes = (string?)body["notes"];
        return Ok(OpportunityDto(o));
    }

    private HttpResponseMessage AddScope(FakeOpportunity o, JsonNode body)
    {
        if (o.IsDecided) return Json(HttpStatusCode.Conflict, new { title = "Conflict", status = 409 });
        var processId = Guid.Parse((string)body["businessProcessId"]!);
        if (!o.Scope.Contains(processId)) o.Scope.Add(processId);
        return Ok(OpportunityDto(o));
    }

    private HttpResponseMessage RemoveScope(FakeOpportunity o, Guid processId)
    {
        o.Scope.Remove(processId);
        return Ok(OpportunityDto(o));
    }

    private HttpResponseMessage Qualify(FakeOpportunity o, JsonNode body)
    {
        var at = DateTimeOffset.Parse((string)body["at"]!);
        if (o.QualifiedAt is { } qualified) return qualified == at ? Ok(OpportunityDto(o)) : Correction("Already qualified.");
        if (o.Stage != "Open") return Json(HttpStatusCode.Conflict, new { title = "Conflict", status = 409 });
        o.Stage = "Qualified";
        o.QualifiedAt = at;
        return Ok(OpportunityDto(o));
    }

    private HttpResponseMessage Decide(FakeOpportunity o, JsonNode body)
    {
        var outcome = (string)body["outcome"]!;
        var at = DateTimeOffset.Parse((string)body["at"]!);
        var winner = Id(body["winningProductId"]);
        if (o.IsDecided)
            return o.Stage == outcome && o.DecidedAt == at && o.WinningProductId == winner
                ? Ok(OpportunityDto(o))
                : Correction("Already decided.");

        o.Stage = outcome;
        o.DecidedAt = at;
        o.WinningProductId = winner;
        Facts.Add(new FakeFact
        {
            Id = Guid.NewGuid(), Kind = "OpportunityDecided", EffectiveDate = at.ToString("yyyy-MM-dd"),
            SubjectType = "Opportunity", SubjectId = o.Id, Payload = $"{{\"outcome\":\"{outcome}\"}}"
        });
        return Ok(OpportunityDto(o));
    }

    private HttpResponseMessage CreateFact(JsonNode body)
    {
        var fact = new FakeFact
        {
            Id = Guid.NewGuid(), Kind = (string)body["kind"]!, EffectiveDate = (string)body["effectiveDate"]!,
            SubjectType = (string?)body["subjectType"], SubjectId = Id(body["subjectId"]),
            Payload = (string?)body["payloadJson"], Notes = (string?)body["notes"]
        };

        var source = Source(body["source"]);
        if (source is not null && FindBinding(source) is { } binding)
        {
            var bound = Facts.Single(f => f.Id == binding.TargetId);
            var same = bound.Kind == fact.Kind && bound.EffectiveDate == fact.EffectiveDate && bound.SubjectType == fact.SubjectType
                       && bound.SubjectId == fact.SubjectId && JsonNode.DeepEquals(JsonNode.Parse(bound.Payload ?? "null"), JsonNode.Parse(fact.Payload ?? "null"));
            return same ? Ok(FactDto(bound)) : Correction("Materially different fact.");
        }

        Facts.Add(fact);
        if (source is not null) Bindings.Add(new FakeBinding(Guid.NewGuid(), source, "FactEvent") { TargetId = fact.Id });
        return Json(HttpStatusCode.Created, FactDto(fact));
    }

    // ---------- bulk firm import (the server's matching rules, relationships included) ----------

    private HttpResponseMessage BulkImport(JsonNode body)
    {
        var seen = new HashSet<SourceRef>();
        var sourcedEdges = new HashSet<(Guid, Guid)>();
        var outcomes = new List<object>();
        int created = 0, updated = 0, unchanged = 0, exceptions = 0, written = 0;
        var items = body["firms"]!.AsArray();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index]!;
            var name = ((string)item["name"]!).Trim();
            var source = Source(item["source"]);
            FakeFirm? firm;
            string outcome;
            string? reason = null;
            var conflicts = new List<object>();

            if (source is null)
            {
                firm = Firms.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                outcome = firm is null ? "Created" : "Updated";
                if (firm is null) Firms.Add(firm = new FakeFirm { Id = Guid.NewGuid(), Name = name });
                firm.SegmentId = Id(item["clientSegmentId"]);
                firm.Status = (string?)item["epistemicStatus"] ?? "Observed";
                firm.Attributes = (string?)item["attributesJson"];
                firm.Notes = (string?)item["notes"];
            }
            else
            {
                (firm, outcome, reason) = ImportSourcedFirm(item, name, source, seen, conflicts);
            }

            var relationshipOutcomes = new List<object>();
            if (firm is not null)
            {
                var relationships = item["relationships"]?.AsArray() ?? [];
                for (var r = 0; r < relationships.Count; r++)
                {
                    var (relOutcome, relReason, relId) = ImportRelationship(firm, relationships[r]!, seen, sourcedEdges);
                    if (relOutcome is "Created" or "Updated") written++;
                    relationshipOutcomes.Add(new
                    {
                        index = r, productId = Guid.Parse((string)relationships[r]!["productId"]!), relationshipId = relId,
                        outcome = relOutcome, reason = relReason, conflicts = Array.Empty<object>()
                    });
                }
            }

            switch (outcome)
            {
                case "Created": created++; break;
                case "Updated": updated++; break;
                case "Unchanged": unchanged++; break;
                default: exceptions++; break;
            }

            outcomes.Add(new
            {
                index, name, source, firmId = firm?.Id, outcome, reason, conflicts, relationships = relationshipOutcomes
            });
        }

        return Ok(new
        {
            marketId = MarketId, created, updated, relationshipsWritten = written, unchanged, exceptions,
            firms = Firms.Select(FirmDto), outcomes
        });
    }

    private (FakeFirm?, string, string?) ImportSourcedFirm(JsonNode item, string name, SourceRef source,
        HashSet<SourceRef> seen, List<object> conflicts)
    {
        if (!seen.Add(source)) return (null, "Exception", "appears more than once in this import");

        var binding = FindBinding(source);
        if (binding is not null && binding.TargetType != "Firm")
            return (null, "Exception", $"Source record '{source.SourceRecordId}' is already bound to a {binding.TargetType}.");
        var firm = binding is null ? null : Firms.FirstOrDefault(f => f.Id == binding.TargetId);
        var newBinding = firm is null;
        if (firm is null)
        {
            var sameName = Firms.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameName.Count > 1) return (null, "Exception", $"Several firms are named '{name}'.");
            if (sameName.Count == 1)
            {
                var rival = Bindings.FirstOrDefault(b => b.TargetType == "Firm" && b.TargetId == sameName[0].Id
                                                         && b.Source.SourceSystem == source.SourceSystem
                                                         && b.Source.SourceInstance == source.SourceInstance
                                                         && b.Source.ObjectKind == source.ObjectKind);
                if (rival is not null)
                    return (null, "Exception",
                        $"Indistinguishable same-name firms: '{name}' is already bound to '{rival.Source.SourceRecordId}'. " +
                        "Supply a distinct, reviewed display name.");
                firm = sameName[0];
            }
        }

        var create = firm is null;
        firm ??= new FakeFirm { Id = Guid.NewGuid(), Name = name };
        var clears = (item["clearFields"]?.AsArray() ?? []).Select(n => (string)n!).ToHashSet();
        var last = newBinding ? null : binding!.Imported;
        var imported = new Dictionary<string, string?>(last ?? []);
        var changed = false;

        // Mirrors FieldMerge: an absent field records the stored value as its baseline (once); a local
        // edit is kept silently while the source still sends its last value, reported once it moves.
        string? Merge(string field, string? stored, bool present, string? incoming)
        {
            if (!present)
            {
                imported.TryAdd(field, stored);
                return stored;
            }
            if (stored == incoming)
            {
                imported[field] = incoming;
                return stored;
            }
            if (last is not null && last.TryGetValue(field, out var previous) && previous != stored)
            {
                if (incoming != previous)
                    conflicts.Add(new { field, storedValue = stored, incomingValue = incoming, lastImportedValue = previous });
                return stored;
            }
            imported[field] = incoming;
            changed = true;
            return incoming;
        }

        var newName = Merge("name", firm.Name, true, name)!;
        if (!create && Firms.Any(f => f != firm && string.Equals(f.Name, newName, StringComparison.OrdinalIgnoreCase)))
            return (null, "Exception", $"Renaming '{firm.Name}' to '{newName}' collides with another firm.");

        var segment = Merge("clientSegmentId", firm.SegmentId?.ToString(),
            item["clientSegmentId"] is not null || clears.Contains("clientSegmentId"), (string?)item["clientSegmentId"]);
        var status = Merge("epistemicStatus", firm.Status, item["epistemicStatus"] is not null,
            (string?)item["epistemicStatus"] ?? firm.Status)!;
        var attributes = Merge("attributesJson", firm.Attributes,
            item["attributesJson"] is not null || clears.Contains("attributesJson"), (string?)item["attributesJson"]);
        var notes = Merge("notes", firm.Notes, item["notes"] is not null || clears.Contains("notes"), (string?)item["notes"]);

        if (create) Firms.Add(firm);
        firm.Name = newName;
        firm.SegmentId = segment is null ? null : Guid.Parse(segment);
        firm.Status = status;
        firm.Attributes = attributes;
        firm.Notes = notes;
        if (newBinding)
        {
            if (binding is not null) binding.TargetId = firm.Id;
            else Bindings.Add(binding = new FakeBinding(Guid.NewGuid(), source, "Firm") { TargetId = firm.Id });
        }

        binding!.Imported = imported;
        return (firm, create ? "Created" : changed || newBinding ? "Updated" : "Unchanged", null);
    }

    private (string Outcome, string? Reason, Guid? Id) ImportRelationship(FakeFirm firm, JsonNode incoming,
        HashSet<SourceRef> seen, HashSet<(Guid, Guid)> sourcedEdges)
    {
        var productId = Guid.Parse((string)incoming["productId"]!);
        var edge = Relationships.FirstOrDefault(r => r.FirmId == firm.Id && r.ProductId == productId);
        var sources = (incoming["sources"]?.AsArray() ?? []).Select(n => Source(n)!).ToList();
        var acquisition = (string?)incoming["acquisitionDate"];
        var end = (string?)incoming["endDate"];
        var served = incoming["servedProcessIds"] is null ? null : Ids(incoming["servedProcessIds"]);
        var priceLevel = (double?)incoming["priceLevel"];

        if (sources.Count == 0)
        {
            var created = edge is null;
            if (edge is null) Relationships.Add(edge = new FakeRelationship { Id = Guid.NewGuid(), FirmId = firm.Id, ProductId = productId });
            edge.Acquisition = acquisition;
            if (end is not null) edge.End = end;
            edge.Served = served ?? [];
            edge.PriceLevel = priceLevel ?? 1.0;
            return (created ? "Created" : "Updated", null, edge.Id);
        }

        if (!sourcedEdges.Add((firm.Id, productId))) return ("Exception", "This product appears more than once for this firm.", null);
        if (sources.Any(s => !seen.Add(s))) return ("Exception", "A listed source record appears more than once.", null);
        foreach (var s in sources)
            if (FindBinding(s) is { } b && b.TargetId != edge?.Id)
                return ("Exception", $"Source record '{s.SourceRecordId}' is already bound to another relationship.", null);
        var boundHere = edge is null ? [] : Bindings.Where(b => b.TargetId == edge.Id).ToList();
        if (boundHere.Count > 0 && !boundHere.Any(b => sources.Contains(b.Source)))
            return ("Exception", "The relationship is bound to other source records; list them together to consolidate.", null);

        var clears = (incoming["clearFields"]?.AsArray() ?? []).Select(n => (string)n!).ToHashSet();
        var create = edge is null;
        edge ??= new FakeRelationship { Id = Guid.NewGuid(), FirmId = firm.Id, ProductId = productId };
        var before = JsonSerializer.Serialize(edge);
        if (acquisition is not null || clears.Contains("acquisitionDate")) edge.Acquisition = acquisition;
        if (end is not null || clears.Contains("endDate")) edge.End = end;
        if (served is not null || clears.Contains("servedProcessIds")) edge.Served = served ?? [];
        if (priceLevel is not null) edge.PriceLevel = priceLevel.Value;
        if (create) Relationships.Add(edge);
        var addsSource = sources.Any(s => boundHere.All(b => b.Source != s));
        foreach (var s in sources.Where(s => FindBinding(s) is null))
            Bindings.Add(new FakeBinding(Guid.NewGuid(), s, "FirmProductRelationship") { TargetId = edge.Id });

        var outcome = create ? "Created" : before != JsonSerializer.Serialize(edge) || addsSource ? "Updated" : "Unchanged";
        return (outcome, null, edge.Id);
    }

    // ---------- helpers ----------

    private FakeBinding? FindBinding(SourceRef source) => Bindings.FirstOrDefault(b => b.Source == source);

    private static SourceRef? Source(JsonNode? node) => node is null
        ? null
        : new SourceRef((string)node["sourceSystem"]!, (string)node["sourceInstance"]!, (string)node["objectKind"]!,
            (string)node["sourceRecordId"]!);

    private static Guid? Id(JsonNode? node) => node is null ? null : Guid.Parse((string)node!);

    private static List<Guid> Ids(JsonNode? node) => node is null ? [] : node.AsArray().Select(n => Guid.Parse((string)n!)).ToList();

    private static object Named(Guid id, string name) => new { id, marketId = MarketId, name };

    private static object FirmDto(FakeFirm f) => new
    {
        id = f.Id, marketId = MarketId, clientSegmentId = f.SegmentId, name = f.Name, epistemicStatus = f.Status,
        attributesJson = f.Attributes, notes = f.Notes
    };

    private static object RelationshipDto(FakeRelationship r) => new
    {
        id = r.Id, firmId = r.FirmId, productId = r.ProductId, servedProcessIds = r.Served, priceLevel = r.PriceLevel,
        pricePaidPerPeriod = (object?)null, acquisitionDate = r.Acquisition, endDate = r.End, isActive = r.End is null,
        notes = (string?)null
    };

    private static object OpportunityDto(FakeOpportunity o) => new
    {
        id = o.Id, firmId = o.FirmId, channelId = o.ChannelId, stage = o.Stage, openedAt = o.OpenedAt,
        qualifiedAt = o.QualifiedAt, decidedAt = o.DecidedAt, competitorIds = o.CompetitorIds,
        winningProductId = o.WinningProductId, crossSellModuleProductId = o.CrossSellModuleProductId,
        isCrossSell = o.CrossSellModuleProductId is not null, isDecided = o.IsDecided, notes = o.Notes,
        scope = o.Scope.Select(p => new { businessProcessId = p, addedAt = o.OpenedAt })
    };

    private static object FactDto(FakeFact f) => new
    {
        id = f.Id, workspaceId = WorkspaceId, kind = f.Kind, effectiveDate = f.EffectiveDate,
        recordedDate = "2026-01-01T00:00:00+00:00", subjectType = f.SubjectType, subjectId = f.SubjectId,
        payloadJson = f.Payload, notes = f.Notes
    };

    private static HttpResponseMessage Page(Dictionary<string, string> q, IEnumerable<object> all)
    {
        var items = all.ToList();
        var page = q.TryGetValue("page", out var p) ? int.Parse(p) : 1;
        var size = Math.Min(q.TryGetValue("pageSize", out var s) ? int.Parse(s) : 50, 200);
        return Ok(new { items = items.Skip((page - 1) * size).Take(size), page, pageSize = size, totalCount = items.Count });
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv.Length > 1 ? kv[1] : ""));

    private static HttpResponseMessage Correction(string detail) =>
        Json(HttpStatusCode.Conflict, new { type = CorrectionRequired, title = "Correction required", status = 409, detail });

    private static HttpResponseMessage NotFound() => Json(HttpStatusCode.NotFound, new { title = "Not found", status = 404 });

    private static HttpResponseMessage Ok(object payload) => Json(HttpStatusCode.OK, payload);

    private static HttpResponseMessage Json(HttpStatusCode status, object payload) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload, ProductManagerApiClient.JsonOptions), Encoding.UTF8, "application/json")
    };
}

internal sealed class FakeFirm
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? SegmentId { get; set; }
    public string Status { get; set; } = "Observed";
    public string? Attributes { get; set; }
    public string? Notes { get; set; }
}

internal sealed class FakeRelationship
{
    public Guid Id { get; set; }
    public Guid FirmId { get; set; }
    public Guid ProductId { get; set; }
    public List<Guid> Served { get; set; } = [];
    public double PriceLevel { get; set; } = 1.0;
    public string? Acquisition { get; set; }
    public string? End { get; set; }
}

internal sealed class FakeOpportunity
{
    public Guid Id { get; set; }
    public Guid FirmId { get; set; }
    public Guid? ChannelId { get; set; }
    public string Stage { get; set; } = "Open";
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? QualifiedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? WinningProductId { get; set; }
    public Guid? CrossSellModuleProductId { get; set; }
    public string? Notes { get; set; }
    public List<Guid> Scope { get; set; } = [];
    public List<Guid> CompetitorIds { get; set; } = [];
    public bool IsDecided => Stage is "Won" or "Lost" or "NoDecision";
}

internal sealed class FakeFact
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public string EffectiveDate { get; set; } = "";
    public string? SubjectType { get; set; }
    public Guid? SubjectId { get; set; }
    public string? Payload { get; set; }
    public string? Notes { get; set; }
}

internal sealed record FakeBinding(Guid Id, SourceRef Source, string TargetType)
{
    public Guid TargetId { get; set; }
    public Dictionary<string, string?>? Imported { get; set; }
}
