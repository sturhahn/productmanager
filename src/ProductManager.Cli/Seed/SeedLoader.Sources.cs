using System.Text.Json;
using System.Text.Json.Nodes;
using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;

namespace ProductManager.Cli.Seed;

/// <summary>
/// The firm roster and the historical facts (Phase 3), where PRD6 source identity applies. A row
/// with a <c>source</c> is matched by its source binding (GET /api/source-bindings) and every write
/// it causes is repeat-safe on the API side; a row without one keeps the pre-PRD6 natural-key
/// matching exactly. The boundary: an import may <em>advance</em> a record (open -> qualified ->
/// decided, a still-open deal's fields, a churn not yet recorded); it never rewrites one. A changed
/// decision, a deal moved to another firm, or a restated fact that differs is reported as a
/// correction-required exception and left as it is.
/// </summary>
public static partial class SeedLoader
{
    // ---------- keys (shared with SeedPreflight so problems and report rows name records the same way) ----------

    public static string OpportunityKey(SeedOpportunity o) =>
        $"{(string.IsNullOrWhiteSpace(o.FirmName) ? o.FirmSource?.SourceRecordId : o.FirmName)} opened {o.OpenedDate}" +
        (o.Source is null ? $" [{string.Join(", ", o.ScopeProcessNames)}]" : $" [{o.Source.SourceRecordId}]");

    public static string FactKey(string segmentName, string date, SeedSourceRef? source) =>
        $"{segmentName} {date}" + (source is null ? "" : $" [{source.SourceRecordId}]");

    /// <summary>
    /// Whether a seed opportunity carries a decision that should be pushed through the API's decide
    /// call. False for a still-open/qualified pipeline entry (DecidedDate/Outcome null).
    /// Public (rather than internal) so it can be unit-tested directly, matching ParseHelpers'
    /// convention in this project.
    /// </summary>
    public static bool HasDecision(SeedOpportunity opportunity) => opportunity.DecidedDate is not null;

    // ---------- firms ----------

    /// <summary>
    /// The whole roster goes to POST /api/firms/bulk in one call; the API matches each item (by source
    /// binding, else by (market, name)) and reports a per-item outcome, which is recorded as the
    /// actual outcome. The planned action next to it is this loader's own prediction from the API's
    /// current firms, bindings and relationships — what a dry run prints.
    /// </summary>
    private static async Task LoadFirmsAsync(
        ProductManagerApiClient client, Guid workspaceId, MarketIndexes market, List<SeedFirm> firms,
        SeedSourceDefaults? defaults, FirmResolver resolver, SeedTally tally, CancellationToken ct)
    {
        if (firms.Count == 0) return;

        var items = new List<FirmImportItem>();
        foreach (var firm in firms)
            await Guard(tally, "firm", firm.Name, () =>
            {
                items.Add(ToFirmImportItem(firm, market, defaults));
                return Task.CompletedTask;
            });
        if (items.Count != firms.Count) return; // a row did not parse; the plan already lists why

        var plans = await PlanFirmsAsync(client, workspaceId, market.MarketId, items, resolver, tally, ct);

        if (tally.DryRun)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var (firmPlan, firmId, relationshipPlans, reason) = plans[i];
                tally.Record("firm", items[i].Name, firmPlan, null, reason, items[i].Source);
                RecordRelationshipPlans(firms[i], items[i], relationshipPlans, null, tally);
                if (firmPlan != SeedAction.Exception)
                    resolver.Add(items[i].Name, items[i].Source, firmId ?? tally.Placeholder());
            }
            return;
        }

        var response = await client.BulkImportFirmsAsync(new BulkImportFirmsRequest(market.MarketId, items), ct);
        foreach (var outcome in response.Outcomes ?? [])
        {
            var item = items[outcome.Index];
            var (firmPlan, _, relationshipPlans, _) = plans[outcome.Index];
            // A legacy (unsourced) item reports Created/Updated exactly as the counters always have;
            // "updated by name" is what the tally has always called matched.
            var actual = item.Source is null && outcome.Outcome == ImportOutcome.Updated ? SeedAction.Matched : outcome.Outcome.ToString();
            tally.Record("firm", item.Name, firmPlan, actual, outcome.Reason, item.Source, outcome.Conflicts);
            RecordRelationshipPlans(firms[outcome.Index], item, relationshipPlans, outcome.Relationships, tally);
            if (outcome.FirmId is { } id)
                resolver.Add(item.Name, item.Source, id);
        }
    }

    /// <summary>One report row per relationship. <paramref name="outcomes"/> is null in a dry run; an
    /// item whose firm was an exception has none, because the API never looked at its relationships.</summary>
    private static void RecordRelationshipPlans(SeedFirm firm, FirmImportItem item, List<string> plans,
        IReadOnlyList<RelationshipImportOutcomeDto>? outcomes, SeedTally tally)
    {
        var relationships = item.Relationships ?? [];
        for (var r = 0; r < relationships.Count; r++)
        {
            var sourced = relationships[r].Sources is { Count: > 0 };
            var outcome = outcomes?.FirstOrDefault(o => o.Index == r);
            var reason = outcome?.Reason;
            string? actual = null;
            if (outcomes is not null && outcome is null)
            {
                actual = SeedAction.Exception;
                reason = "not applied: the firm itself was an exception";
            }
            else if (outcome is not null)
            {
                // A legacy edge has always been counted as applied, created or not.
                actual = !sourced && outcome.Outcome is ImportOutcome.Created or ImportOutcome.Updated
                    ? SeedAction.Applied
                    : outcome.Outcome.ToString();
            }

            tally.Record("firmRelationship", $"{firm.Name} / {firm.Relationships[r].ProductName}", plans[r], actual, reason,
                sourced ? relationships[r].Sources![0] : null, outcome?.Conflicts);
        }
    }

    private static FirmImportItem ToFirmImportItem(SeedFirm firm, MarketIndexes market, SeedSourceDefaults? defaults)
    {
        Guid? segmentId = firm.SegmentName is null ? null : market.Segments.GetOrThrow(firm.SegmentName);
        var status = ParseHelpers.ParseOptionalEnum<EpistemicStatus>(firm.EpistemicStatus, "firm epistemicStatus");
        var relationships = firm.Relationships.Count == 0
            ? null
            : firm.Relationships.Select(r => ToFirmImportRelationship(r, market, defaults)).ToList();
        var source = firm.Source is null ? null : SeedPreflight.Resolve(firm.Source, defaults);

        return new FirmImportItem(firm.Name, segmentId, status, firm.AttributesJson, firm.Notes, relationships,
            source, firm.ClearFields.Count == 0 ? null : firm.ClearFields);
    }

    private static FirmImportRelationship ToFirmImportRelationship(SeedFirmRelationship relationship, MarketIndexes market,
        SeedSourceDefaults? defaults)
    {
        var productId = market.Products.GetOrThrow(relationship.ProductName);
        var servedProcessIds = relationship.ServedProcessNames.Count == 0
            ? null
            : relationship.ServedProcessNames.Select(market.BusinessProcesses.GetOrThrow).ToList();
        var sources = relationship.Sources.Count == 0 ? null : relationship.Sources.Select(s => SeedPreflight.Resolve(s, defaults)).ToList();

        return new FirmImportRelationship(
            productId, servedProcessIds, relationship.PriceLevel, PricePaidPerPeriod: null,
            ParseHelpers.ParseOptionalDate(relationship.AcquisitionDate, "firm relationship acquisitionDate"),
            ParseHelpers.ParseOptionalDate(relationship.EndDate, "firm relationship endDate"),
            sources, relationship.ClearFields.Count == 0 ? null : relationship.ClearFields);
    }

    /// <summary>
    /// Predicts, per item, what the bulk import will do — the same matching rules the API applies
    /// (see its FirmImportItem docs), read against its current firms, bindings and relationships.
    /// The API's reported outcome remains the authority; this is the plan printed beside it.
    /// </summary>
    private static async Task<List<(string Firm, Guid? FirmId, List<string> Relationships, string? Reason)>> PlanFirmsAsync(
        ProductManagerApiClient client, Guid workspaceId, Guid marketId, List<FirmImportItem> items, FirmResolver resolver,
        SeedTally tally, CancellationToken ct)
    {
        var listed = await resolver.ListAsync(ct);
        var sourced = items.Any(i => i.Source is not null);
        var firmBindings = sourced ? await ListBindingsAsync(client, workspaceId, "Firm", ct) : [];
        var edgeBindings = items.Any(i => (i.Relationships ?? []).Any(r => r.Sources is { Count: > 0 }))
            ? await ListBindingsAsync(client, workspaceId, "FirmProductRelationship", ct)
            : [];
        var edges = new Dictionary<(Guid, Guid), FirmProductRelationshipDto>();
        foreach (var productId in items.SelectMany(i => i.Relationships ?? []).Where(r => r.Sources is { Count: > 0 }).Select(r => r.ProductId).Distinct())
            foreach (var edge in await ListChildrenAsync(tally, marketId, page => client.ListFirmRelationshipsAsync(null, productId, null, page, ListPageSize, ct)))
                edges.TryAdd((edge.FirmId, edge.ProductId), edge);

        var plannedNames = new Dictionary<string, SourceRef?>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<(string, Guid?, List<string>, string?)>();
        foreach (var item in items)
        {
            var name = item.Name.Trim();
            var (plan, firm, reason) = item.Source is null
                ? PlanLegacyFirm(name, listed, plannedNames)
                : PlanSourcedFirm(item, name, listed, firmBindings, plannedNames);
            if (plan == SeedAction.Create) plannedNames.TryAdd(name, item.Source);

            var relationshipPlans = (item.Relationships ?? []).Select(r =>
                plan == SeedAction.Exception ? SeedAction.Exception
                : r.Sources is not { Count: > 0 } ? SeedAction.Apply
                : firm is null || !edges.TryGetValue((firm.Id, r.ProductId), out var edge) ? SeedAction.Create
                : RelationshipDiffers(edge, r, edgeBindings) ? SeedAction.Update
                : SeedAction.NoOp).ToList();
            plans.Add((plan, firm?.Id, relationshipPlans, reason));
        }

        return plans;
    }

    private static (string, FirmDto?, string?) PlanLegacyFirm(string name, List<FirmDto> listed, Dictionary<string, SourceRef?> plannedNames)
    {
        var firm = listed.FirstOrDefault(f => SameName(f.Name, name));
        return (firm is not null || plannedNames.ContainsKey(name) ? SeedAction.Match : SeedAction.Create, firm, null);
    }

    private const string DistinctName = "Supply a distinct, reviewed display name.";

    private static (string, FirmDto?, string?) PlanSourcedFirm(FirmImportItem item, string name, List<FirmDto> listed,
        List<SourceBindingDto> bindings, Dictionary<string, SourceRef?> plannedNames)
    {
        var source = item.Source!;
        var binding = bindings.FirstOrDefault(b => Matches(b, source));
        // ponytail: a binding whose firm is gone (or in another market) is planned as a create; the API's outcome says which.
        var bound = binding is null ? null : listed.FirstOrDefault(f => f.Id == binding.TargetId);
        if (bound is not null)
            return listed.Any(f => f.Id != bound.Id && SameName(f.Name, name))
                ? (SeedAction.Exception, bound, $"Renaming '{bound.Name}' to '{name}' collides with another firm in this market.")
                : (FirmDiffers(bound, item) ? SeedAction.Update : SeedAction.NoOp, bound, null);

        bool Rival(SourceRef? other) => other is not null && other != source && other.SourceSystem == source.SourceSystem
                                        && other.SourceInstance == source.SourceInstance && other.ObjectKind == source.ObjectKind;

        var sameName = listed.Where(f => SameName(f.Name, name)).ToList();
        if (sameName.Count > 1) return (SeedAction.Exception, null, $"Several firms are named '{name}'. {DistinctName}");
        if (sameName.Count == 1)
            return bindings.Any(b => b.TargetId == sameName[0].Id && Rival(ToRef(b)))
                ? (SeedAction.Exception, null, $"Indistinguishable same-name firms: '{name}' is already bound to another {source.ObjectKind}. {DistinctName}")
                : (SeedAction.Update, sameName[0], null);
        return plannedNames.TryGetValue(name, out var earlier) && Rival(earlier)
            ? (SeedAction.Exception, null, $"Indistinguishable same-name firms: an earlier row in this file is also '{name}'. {DistinctName}")
            : (SeedAction.Create, null, null);
    }

    /// <summary>Whether a source-bound item would change any stored field (absent fields never do).</summary>
    private static bool FirmDiffers(FirmDto firm, FirmImportItem item)
    {
        bool Clears(string field) => item.ClearFields?.Contains(field) == true;
        return firm.Name != item.Name.Trim()
               || (item.ClientSegmentId is not null && item.ClientSegmentId != firm.ClientSegmentId)
               || (Clears("clientSegmentId") && firm.ClientSegmentId is not null)
               || (item.EpistemicStatus is not null && item.EpistemicStatus != firm.EpistemicStatus)
               || (item.AttributesJson is not null && !SameJson(item.AttributesJson, firm.AttributesJson))
               || (Clears("attributesJson") && firm.AttributesJson is not null)
               || (item.Notes is not null && item.Notes != firm.Notes)
               || (Clears("notes") && firm.Notes is not null);
    }

    private static bool RelationshipDiffers(FirmProductRelationshipDto edge, FirmImportRelationship r, List<SourceBindingDto> edgeBindings)
    {
        bool Clears(string field) => r.ClearFields?.Contains(field) == true;
        return r.Sources!.Any(s => !edgeBindings.Any(b => b.TargetId == edge.Id && Matches(b, s)))
               || (r.AcquisitionDate is not null && r.AcquisitionDate != edge.AcquisitionDate)
               || (Clears("acquisitionDate") && edge.AcquisitionDate is not null)
               || (r.EndDate is not null && r.EndDate != edge.EndDate)
               || (Clears("endDate") && edge.EndDate is not null)
               || (r.PriceLevel is not null && r.PriceLevel != edge.PriceLevel)
               || (r.ServedProcessIds is not null && !r.ServedProcessIds.ToHashSet().SetEquals(edge.ServedProcessIds))
               || (Clears("servedProcessIds") && edge.ServedProcessIds.Count > 0)
               || (Clears("pricePaidPerPeriod") && edge.PricePaidPerPeriod is not null);
    }

    // ---------- opportunities ----------

    private sealed record Decision(OpportunityStage Outcome, Guid? WinningProductId, DateTimeOffset At, DateTimeOffset? RecordedAt);

    private sealed record WantedOpportunity(
        Guid FirmId, DateTimeOffset OpenedAt, Guid? ChannelId, Guid? CrossSellModuleProductId, List<Guid> ScopeIds,
        string? Notes, DateTimeOffset? QualifiedAt, Decision? Decision);

    /// <summary>
    /// Historical and live opportunities, loaded through the real open -&gt; qualify -&gt; decide
    /// lifecycle so the API appends the same OpportunityDecided fact a live decision would — that is
    /// what makes seed data real calibration input. With a source the row is matched by its binding
    /// and advanced to what the file says; without one, a firm's existing opportunities are matched by
    /// (opened date, scope) and a match is left alone, as before PRD6.
    /// </summary>
    private static async Task LoadOpportunitiesAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedOpportunity> opportunities, SeedSourceDefaults? defaults,
        FirmResolver firms, NameIndex channels, MarketIndexes market, SeedTally tally, CancellationToken ct)
    {
        var byFirm = new Dictionary<Guid, List<OpportunityDto>>();

        foreach (var seedOpportunity in opportunities)
            await Guard(tally, "opportunity", OpportunityKey(seedOpportunity), async () =>
            {
                var key = OpportunityKey(seedOpportunity);
                var source = seedOpportunity.Source is null ? null : SeedPreflight.Resolve(seedOpportunity.Source, defaults);
                var partial = ParseOpportunity(seedOpportunity, channels, market);
                var firmId = seedOpportunity.FirmSource is not null
                    ? await firms.ResolveAsync(SeedPreflight.Resolve(seedOpportunity.FirmSource, defaults), ct)
                    : await firms.ResolveAsync(seedOpportunity.FirmName, ct);
                var want = partial with { FirmId = firmId };

                if (source is null)
                    await LoadLegacyOpportunityAsync(client, want, key, byFirm, tally, ct);
                else
                    await LoadSourcedOpportunityAsync(client, workspaceId, source, want, key, byFirm, tally, ct);
            });
    }

    /// <summary>Parses everything the lifecycle needs up front, so a dry run reports every bad value
    /// and ordering problem before anything is written.</summary>
    private static WantedOpportunity ParseOpportunity(SeedOpportunity o, NameIndex channels, MarketIndexes market)
    {
        var opened = ParseHelpers.ParseDate(o.OpenedDate, "opportunity openedDate");
        var qualified = ParseHelpers.ParseOptionalDate(o.QualifiedDate, "opportunity qualifiedDate");
        var decided = ParseHelpers.ParseOptionalDate(o.DecidedDate, "opportunity decidedDate");
        var recorded = ParseHelpers.ParseOptionalDate(o.RecordedDate, "opportunity recordedDate");

        if (qualified < opened) throw new ArgumentException($"qualifiedDate {o.QualifiedDate} precedes openedDate {o.OpenedDate}.");
        if (decided < opened) throw new ArgumentException($"decidedDate {o.DecidedDate} precedes openedDate {o.OpenedDate}.");
        if (decided < qualified) throw new ArgumentException($"decidedDate {o.DecidedDate} precedes qualifiedDate {o.QualifiedDate}.");

        Decision? decision = null;
        if (HasDecision(o))
        {
            if (o.Outcome is null) throw new ArgumentException("decidedDate is set but outcome is missing (Won | Lost | NoDecision).");
            var outcome = ParseHelpers.ParseEnum<OpportunityStage>(o.Outcome, "opportunity outcome");
            if (outcome is not (OpportunityStage.Won or OpportunityStage.Lost or OpportunityStage.NoDecision))
                throw new ArgumentException($"outcome '{o.Outcome}' is not a decision (Won | Lost | NoDecision).");
            Guid? winner = o.WinningProductName is null ? null : market.Products.GetOrThrow(o.WinningProductName);
            if (outcome == OpportunityStage.Won && winner is null)
                throw new ArgumentException("a Won opportunity needs winningProductName.");
            decision = new Decision(outcome, winner, ToUtcMidnight(decided!.Value), ToUtcMidnight(recorded));
        }
        else if (o.Outcome is not null)
        {
            throw new ArgumentException($"outcome '{o.Outcome}' is set but decidedDate is missing; both or neither.");
        }

        return new WantedOpportunity(
            Guid.Empty,
            ToUtcMidnight(opened),
            o.ChannelName is null ? null : channels.GetOrThrow(o.ChannelName),
            o.CrossSellModuleProductName is null ? null : market.Products.GetOrThrow(o.CrossSellModuleProductName),
            o.ScopeProcessNames.Select(market.BusinessProcesses.GetOrThrow).Distinct().ToList(),
            o.Notes,
            ToUtcMidnight(qualified),
            decision);
    }

    /// <summary>The pre-PRD6 rule, unchanged apart from paging the firm's listing to the end.</summary>
    private static async Task LoadLegacyOpportunityAsync(ProductManagerApiClient client, WantedOpportunity want, string key,
        Dictionary<Guid, List<OpportunityDto>> byFirm, SeedTally tally, CancellationToken ct)
    {
        var existingForFirm = await FirmOpportunitiesAsync(client, want.FirmId, byFirm, tally, ct);
        if (existingForFirm.Any(o => LegacyMatch(o, want)))
        {
            tally.RecordMatched("opportunity", key);
            return;
        }

        var created = tally.DryRun
            ? new OpportunityDto(tally.Placeholder(), want.FirmId, want.ChannelId, OpportunityStage.Open, want.OpenedAt, null, null, [],
                null, want.CrossSellModuleProductId, false, false, want.Notes,
                want.ScopeIds.Select(id => new OpportunityScopeItemDto(id, want.OpenedAt)).ToList())
            : await client.CreateOpportunityAsync(new CreateOpportunityRequest(
                want.FirmId, want.OpenedAt, want.ChannelId, want.CrossSellModuleProductId, want.ScopeIds, CompetitorIds: null, want.Notes), ct);
        tally.RecordCreated("opportunity", key);
        existingForFirm.Add(created); // keeps duplicate detection correct within this same run

        if (want.QualifiedAt is { } qualifiedAt)
            await WriteAsync(tally, () => client.QualifyOpportunityAsync(created.Id, new QualifyOpportunityRequest(qualifiedAt), ct));

        // An opportunity with no decision is a live pipeline entry (Open, or Qualified) rather than a
        // historical fact — it must not be forced through the API's decide call.
        if (want.Decision is { } d)
            await WriteAsync(tally, () => client.DecideOpportunityAsync(created.Id,
                new DecideOpportunityRequest(d.Outcome, d.WinningProductId, d.At, d.RecordedAt), ct));
    }

    private static async Task<List<OpportunityDto>> FirmOpportunitiesAsync(ProductManagerApiClient client, Guid firmId,
        Dictionary<Guid, List<OpportunityDto>> byFirm, SeedTally tally, CancellationToken ct)
    {
        if (!byFirm.TryGetValue(firmId, out var existing))
            byFirm[firmId] = existing = await ListChildrenAsync(tally, firmId,
                page => client.ListOpportunitiesAsync(firmId, null, null, null, page, ListPageSize, ct));
        return existing;
    }

    /// <summary>The pre-PRD6 duplicate key: same firm, same opened day, same set of scope processes.</summary>
    private static bool LegacyMatch(OpportunityDto o, WantedOpportunity want) =>
        o.OpenedAt.UtcDateTime.Date == want.OpenedAt.UtcDateTime.Date &&
        o.Scope.Select(s => s.BusinessProcessId).ToHashSet().SetEquals(want.ScopeIds);

    private static async Task LoadSourcedOpportunityAsync(ProductManagerApiClient client, Guid workspaceId, SourceRef source,
        WantedOpportunity want, string key, Dictionary<Guid, List<OpportunityDto>> byFirm, SeedTally tally, CancellationToken ct)
    {
        const string kind = "opportunity";
        var binding = await FindBindingAsync(client, workspaceId, source, ct);
        if (binding is not null && binding.TargetType != "Opportunity")
        {
            tally.Record(kind, key, SeedAction.Exception, SeedAction.Exception,
                $"source record is already bound to a {binding.TargetType}.", source);
            return;
        }

        if (binding is null)
            foreach (var twin in (await FirmOpportunitiesAsync(client, want.FirmId, byFirm, tally, ct)).Where(o => LegacyMatch(o, want)))
                if (await IsUnboundAsync(client, workspaceId, "Opportunity", twin.Id, ct))
                {
                    tally.Record(kind, key, SeedAction.Exception, SeedAction.Exception, UnboundTwin($"opportunity {twin.Id}",
                        "firm, opened date and scope", "its decision"), source);
                    return;
                }

        var existing = binding is null ? null : await GetOrNullAsync(() => client.GetOpportunityAsync(binding.TargetId, ct));
        if (existing is not null)
        {
            var (correction, steps) = Advance(existing, want);
            if (correction is not null)
            {
                tally.Record(kind, key, SeedAction.Exception, SeedAction.Exception, correction, source);
                return;
            }
            if (!steps.Any)
            {
                tally.Record(kind, key, SeedAction.NoOp, SeedAction.Unchanged, source: source);
                return;
            }
        }

        var planned = existing is null ? SeedAction.Create : SeedAction.Update;
        if (tally.DryRun)
        {
            tally.Record(kind, key, planned, null, source: source);
            return;
        }

        try
        {
            // With a source the create is repeat-safe: an already-bound record comes back as it is
            // (a lost response on an earlier run), and Advance then picks up wherever it stands.
            var opportunity = existing ?? await client.CreateOpportunityAsync(new CreateOpportunityRequest(
                want.FirmId, want.OpenedAt, want.ChannelId, want.CrossSellModuleProductId, want.ScopeIds, CompetitorIds: null,
                want.Notes, source), ct);

            var (correction, steps) = Advance(opportunity, want);
            if (correction is not null)
            {
                tally.Record(kind, key, planned, SeedAction.Exception, correction, source);
                return;
            }

            await ApplyStepsAsync(client, opportunity, want, steps, ct);
            tally.Record(kind, key, planned, existing is null ? SeedAction.Created : SeedAction.Updated, source: source);
        }
        catch (ApiRequestException ex) when (ex.IsCorrectionRequired)
        {
            tally.Record(kind, key, planned, SeedAction.Exception, $"Correction required: {ex.Detail}", source);
        }
    }

    private sealed record Steps(bool Fields, List<Guid> AddScope, List<Guid> RemoveScope, bool Qualify, bool Decide)
    {
        public bool Any => Fields || AddScope.Count > 0 || RemoveScope.Count > 0 || Qualify || Decide;
    }

    /// <summary>
    /// What it takes to bring a stored opportunity to the file's state, or why it cannot be done
    /// without rewriting history (the correction-required boundary). Only an open or qualified deal
    /// takes field and scope changes; a field the file leaves out keeps its stored value.
    /// </summary>
    private static (string? Correction, Steps Steps) Advance(OpportunityDto o, WantedOpportunity want)
    {
        var none = new Steps(false, [], [], false, false);
        const string boundary = "Correction required: the stored record and its ledger fact are kept; this import does not rewrite history.";

        if (o.FirmId != want.FirmId)
            return ($"{boundary} The source record is opportunity {o.Id} at firm {o.FirmId}; the file now places it at firm {want.FirmId}.", none);
        if (o.QualifiedAt is { } q && want.QualifiedAt is { } wq && q != wq)
            return ($"{boundary} Qualified on {q:yyyy-MM-dd}; the file says {wq:yyyy-MM-dd}.", none);

        if (o.IsDecided)
        {
            var stored = $"{o.Stage} on {o.DecidedAt:yyyy-MM-dd}, winner {o.WinningProductId?.ToString() ?? "none"}";
            if (want.Decision is not { } d)
                return ($"{boundary} Already decided ({stored}); the file shows it still open.", none);
            if (d.Outcome != o.Stage || d.WinningProductId != o.WinningProductId || d.At != o.DecidedAt)
                return ($"{boundary} Already decided ({stored}); the file says {d.Outcome} on {d.At:yyyy-MM-dd}, " +
                        $"winner {d.WinningProductId?.ToString() ?? "none"}.", none);
            if (want.QualifiedAt is not null && o.QualifiedAt is null)
                return ($"{boundary} Decided without a qualification; the file now adds one.", none);
            return (null, none);
        }

        var fields = (want.ChannelId is not null && want.ChannelId != o.ChannelId)
                     || (want.CrossSellModuleProductId is not null && want.CrossSellModuleProductId != o.CrossSellModuleProductId)
                     || (want.Notes is not null && want.Notes != o.Notes);
        var storedScope = o.Scope.Select(s => s.BusinessProcessId).ToList();
        var add = want.ScopeIds.Count == 0 ? [] : want.ScopeIds.Except(storedScope).ToList();
        var remove = want.ScopeIds.Count == 0 ? [] : storedScope.Except(want.ScopeIds).ToList();
        return (null, new Steps(fields, add, remove, want.QualifiedAt is not null && o.QualifiedAt is null, want.Decision is not null));
    }

    /// <summary>Field and scope changes first (a decided deal refuses them), then qualify, then decide.</summary>
    private static async Task ApplyStepsAsync(ProductManagerApiClient client, OpportunityDto o, WantedOpportunity want, Steps steps,
        CancellationToken ct)
    {
        if (steps.Fields)
            await client.UpdateOpportunityAsync(o.Id, new UpdateOpportunityRequest(
                want.ChannelId ?? o.ChannelId, want.CrossSellModuleProductId ?? o.CrossSellModuleProductId, o.CompetitorIds,
                want.Notes ?? o.Notes), ct);
        foreach (var processId in steps.AddScope)
            await client.AddOpportunityScopeAsync(o.Id, new AddOpportunityScopeRequest(processId), ct);
        foreach (var processId in steps.RemoveScope)
            await client.RemoveOpportunityScopeAsync(o.Id, processId, ct);
        if (steps.Qualify)
            await client.QualifyOpportunityAsync(o.Id, new QualifyOpportunityRequest(want.QualifiedAt), ct);
        if (steps.Decide && want.Decision is { } d)
            await client.DecideOpportunityAsync(o.Id, new DecideOpportunityRequest(d.Outcome, d.WinningProductId, d.At, d.RecordedAt), ct);
    }

    // ---------- churn and arrivals ----------

    private static async Task LoadChurnEventsAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedChurnEvent> churnEvents, SeedSourceDefaults? defaults,
        MarketIndexes market, FirmResolver firms, SeedTally tally, CancellationToken ct)
    {
        foreach (var churn in churnEvents)
        {
            var key = FactKey(churn.SegmentName, churn.EffectiveDate, churn.Source);
            await Guard(tally, "churnEvent", key, async () =>
            {
                var segmentId = market.Segments.GetOrThrow(churn.SegmentName);
                var effectiveDate = ParseHelpers.ParseDate(churn.EffectiveDate, "churn event effectiveDate");
                var recordedAt = ToUtcMidnight(ParseHelpers.ParseOptionalDate(churn.RecordedDate, "churn event recordedDate"));
                var firmSource = churn.FirmSource is null ? null : SeedPreflight.Resolve(churn.FirmSource, defaults);
                Guid? firmId = firmSource is not null ? await firms.ResolveAsync(firmSource, ct)
                    : churn.FirmName is not null ? await firms.ResolveAsync(churn.FirmName, ct)
                    : null;
                Guid? productId = churn.ProductName is null ? null : market.Products.GetOrThrow(churn.ProductName);

                // The segment rides in the payload (calibration reads it); the rest is provenance, so
                // reconciliation can find the affected customer. Absent parts are left out entirely,
                // which keeps a legacy churn's payload byte-identical to what it always was.
                var payload = new JsonObject { ["clientSegmentId"] = segmentId.ToString() };
                if (firmId is not null) payload["firmId"] = firmId.ToString();
                if (productId is not null) payload["productId"] = productId.ToString();
                if ((churn.SourceCustomerRef ?? firmSource?.SourceRecordId) is { } customer) payload["sourceCustomerRef"] = customer;
                if (churn.SourceRelationshipRef is not null) payload["sourceRelationshipRef"] = churn.SourceRelationshipRef;

                var request = new CreateFactEventRequest(workspaceId, FactEventKind.Churn, effectiveDate, "ClientSegment", segmentId,
                    payload.ToJsonString(), churn.Notes, recordedAt);

                // Without a source the ledger's idempotency is the loader's job: a (segment, date) pair that
                // already has a churn fact is skipped rather than appended again.
                async Task<List<FactEventDto>> SameDay() => await ListChildrenAsync(tally, segmentId, page => client.ListFactsAsync(
                    workspaceId, FactEventKind.Churn, "ClientSegment", segmentId, effectiveDate, effectiveDate, null, page, ListPageSize, ct));

                if (churn.Source is not null)
                {
                    await LoadSourcedFactAsync(client, request with { Source = SeedPreflight.Resolve(churn.Source, defaults) }, "churnEvent", key,
                        SameDay, "segment and date", tally, ct);
                    return;
                }

                if ((await SameDay()).Count > 0)
                {
                    tally.RecordMatched("churnEvent", key);
                    return;
                }

                await WriteAsync(tally, () => client.CreateFactAsync(request, ct));
                tally.RecordCreated("churnEvent", key);
            });
        }
    }

    private static async Task LoadArrivalCountsAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedArrivalCount> arrivals, SeedSourceDefaults? defaults,
        NameIndex segments, SeedTally tally, CancellationToken ct)
    {
        foreach (var arrival in arrivals)
        {
            var key = FactKey(arrival.SegmentName, arrival.Month, arrival.Source);
            await Guard(tally, "arrivalCount", key, async () =>
            {
                var segmentId = segments.GetOrThrow(arrival.SegmentName);
                var flowKind = ParseHelpers.ParseEnum<SimulationFlowKind>(arrival.FlowKind, "arrival count flowKind");
                var month = ParseHelpers.ParseDate(arrival.Month, "arrival count month");
                var monthStart = new DateOnly(month.Year, month.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                var recordedAt = ToUtcMidnight(ParseHelpers.ParseOptionalDate(arrival.RecordedDate, "arrival count recordedDate"));

                var payload = $"{{\"clientSegmentId\":\"{segmentId}\",\"flowKind\":\"{flowKind}\",\"count\":{arrival.Count}," +
                              $"\"exposure\":{arrival.Exposure.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
                var request = new CreateFactEventRequest(workspaceId, FactEventKind.SegmentArrivalObserved, monthStart, "ClientSegment",
                    segmentId, payload, null, recordedAt);

                async Task<List<FactEventDto>> SameMonth() => (await ListChildrenAsync(tally, segmentId, page => client.ListFactsAsync(
                        workspaceId, FactEventKind.SegmentArrivalObserved, "ClientSegment", segmentId, monthStart, monthEnd, null, page, ListPageSize, ct)))
                    .Where(f => PayloadFlowKindEquals(f.PayloadJson, flowKind)).ToList();

                if (arrival.Source is not null)
                {
                    await LoadSourcedFactAsync(client, request with { Source = SeedPreflight.Resolve(arrival.Source, defaults) }, "arrivalCount", key,
                        SameMonth, "segment, month and flowKind", tally, ct);
                    return;
                }

                if ((await SameMonth()).Count > 0)
                {
                    tally.RecordMatched("arrivalCount", key);
                    return;
                }

                await WriteAsync(tally, () => client.CreateFactAsync(request, ct));
                tally.RecordCreated("arrivalCount", key);
            });
        }
    }

    /// <summary>A sourced fact: already bound and materially identical is a no-op; bound but different
    /// is a correction-required exception (nothing appended); unbound is appended with its source.</summary>
    private static async Task LoadSourcedFactAsync(ProductManagerApiClient client, CreateFactEventRequest request, string kind, string key,
        Func<Task<List<FactEventDto>>> legacyMatches, string legacyKey, SeedTally tally, CancellationToken ct)
    {
        var source = request.Source!;
        var binding = await FindBindingAsync(client, request.WorkspaceId, source, ct);
        if (binding is not null && binding.TargetType != "FactEvent")
        {
            tally.Record(kind, key, SeedAction.Exception, SeedAction.Exception, $"source record is already bound to a {binding.TargetType}.", source);
            return;
        }

        var bound = binding is null ? null : await GetOrNullAsync(() => client.GetFactAsync(binding.TargetId, ct));

        if (bound is not null)
        {
            var same = bound.Kind == request.Kind && bound.EffectiveDate == request.EffectiveDate && bound.SubjectType == request.SubjectType
                       && bound.SubjectId == request.SubjectId && SameJson(bound.PayloadJson, request.PayloadJson);
            if (same) tally.Record(kind, key, SeedAction.NoOp, SeedAction.Unchanged, source: source);
            else
                tally.Record(kind, key, SeedAction.Exception, SeedAction.Exception,
                    $"Correction required: source record is already fact {bound.Id} ({bound.Kind} on {bound.EffectiveDate:yyyy-MM-dd}) " +
                    "with a different date, subject or payload. The ledger is append-only; nothing is appended.", source);
            return;
        }

        if (binding is null)
            foreach (var twin in await legacyMatches())
                if (await IsUnboundAsync(client, request.WorkspaceId, "FactEvent", twin.Id, ct))
                {
                    tally.Record(kind, key, SeedAction.Exception, SeedAction.Exception, UnboundTwin($"fact {twin.Id}", legacyKey, "the observation"), source);
                    return;
                }

        if (tally.DryRun)
        {
            tally.Record(kind, key, SeedAction.Create, null, source: source);
            return;
        }

        try
        {
            await client.CreateFactAsync(request, ct);
            tally.Record(kind, key, SeedAction.Create, SeedAction.Created, source: source);
        }
        catch (ApiRequestException ex) when (ex.IsCorrectionRequired)
        {
            tally.Record(kind, key, SeedAction.Create, SeedAction.Exception, $"Correction required: {ex.Detail}", source);
        }
    }

    /// <summary>
    /// Reads <c>flowKind</c> out of a stored arrival fact's payload. This must parse rather than
    /// substring-match: the payload column is jsonb, and Postgres re-serializes it with a space after
    /// each colon (<c>{"flowKind": "NewFirmArrival"}</c>), so a match against the compact text the
    /// loader writes never fires and every re-run appends a duplicate fact.
    /// </summary>
    private static bool PayloadFlowKindEquals(string? payloadJson, SimulationFlowKind flowKind)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("flowKind", out var element)) return false;
            if (element.ValueKind != JsonValueKind.String) return false;
            return string.Equals(element.GetString(), flowKind.ToString(), StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ---------- source bindings ----------

    private static async Task<SourceBindingDto?> FindBindingAsync(ProductManagerApiClient client, Guid workspaceId, SourceRef source,
        CancellationToken ct) =>
        (await client.ListSourceBindingsAsync(workspaceId, source.SourceSystem, source.SourceInstance, source.ObjectKind,
            source.SourceRecordId, null, null, 1, ListPageSize, ct)).Items.FirstOrDefault(b => Matches(b, source));

    private static Task<List<SourceBindingDto>> ListBindingsAsync(ProductManagerApiClient client, Guid workspaceId, string targetType,
        CancellationToken ct) =>
        ListAllAsync(page => client.ListSourceBindingsAsync(workspaceId, null, null, null, null, targetType, null, page, ListPageSize, ct));

    private static bool Matches(SourceBindingDto b, SourceRef s) => ToRef(b) == s;

    private static async Task<bool> IsUnboundAsync(ProductManagerApiClient client, Guid workspaceId, string targetType, Guid targetId,
        CancellationToken ct) =>
        (await client.ListSourceBindingsAsync(workspaceId, null, null, null, null, targetType, targetId, 1, 1, ct)).Items.Count == 0;

    /// <summary>Why a sourced row that matches a record loaded before PRD6 is not simply created: the API
    /// has no way to attach a source to an existing opportunity or fact, and a second copy would count twice.</summary>
    private static string UnboundTwin(string what, string matchedOn, string counted) =>
        $"{what} was loaded earlier without source identity and matches this row on {matchedOn}. The API cannot attach " +
        $"a source record to it, and creating another would count {counted} twice. Keep loading this row without a " +
        "source, or resolve the earlier record deliberately first.";

    private static SourceRef ToRef(SourceBindingDto b) => new(b.SourceSystem, b.SourceInstance, b.ObjectKind, b.SourceRecordId);

    /// <summary>A bound row that has since been deleted reads as absent; the API re-points the binding on the next write.</summary>
    private static async Task<T?> GetOrNullAsync<T>(Func<Task<T>> get) where T : class
    {
        try
        {
            return await get();
        }
        catch (ApiRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static bool SameJson(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b);
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));
        }
        catch (JsonException)
        {
            return a == b;
        }
    }

    /// <summary>
    /// Firm references from opportunities and churn: by source identity (the binding), or by name —
    /// first among the firms this file imported, then the market's whole roster, listed once (paged)
    /// on first use. A name that the roster holds more than once is refused, not guessed: reference
    /// that firm by <c>firmSource</c> instead.
    /// </summary>
    private sealed class FirmResolver(ProductManagerApiClient client, Guid workspaceId, Guid marketId, SeedTally tally)
    {
        private List<FirmDto>? _listed;
        private NameIndex? _listing;
        private readonly Dictionary<SourceRef, Guid> _bySource = new();

        /// <summary>The firms this file itself imported; their ids win over the market-wide listing.</summary>
        private NameIndex Index { get; } = new();

        public async Task<List<FirmDto>> ListAsync(CancellationToken ct) =>
            _listed ??= await ListChildrenAsync(tally, marketId, page => client.ListFirmsAsync(marketId, null, null, page, ListPageSize, ct));

        public void Add(string name, SourceRef? source, Guid id)
        {
            Index.Add(name, id);
            if (source is not null) _bySource[source] = id;
        }

        public async Task<Guid> ResolveAsync(string name, CancellationToken ct)
        {
            _listing ??= new NameIndex((await ListAsync(ct)).Select(f => (f.Name, f.Id)));
            if (_listing.IsAmbiguous(name))
                throw SeedReferenceException.WithMessage(
                    $"firm name '{name}' matches more than one firm in the market; reference the firm by firmSource instead.");
            if (Index.TryGet(name, out var id) || _listing.TryGet(name, out id)) return id;
            throw new SeedReferenceException(name);
        }

        public async Task<Guid> ResolveAsync(SourceRef source, CancellationToken ct)
        {
            if (_bySource.TryGetValue(source, out var id)) return id;
            var binding = await FindBindingAsync(client, workspaceId, source, ct);
            if (binding is null || binding.TargetType != "Firm")
                throw SeedReferenceException.WithMessage(
                    $"firmSource {SeedPreflight.Describe(source)} is not bound to a firm; import that firm with this source first.");
            return _bySource[source] = binding.TargetId;
        }
    }
}
