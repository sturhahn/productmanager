using System.Text.Json;
using ProductManager.Cli.Client;
using ProductManager.Cli.Seed;

namespace ProductManager.Cli.Tests;

/// <summary>
/// PRD6 on the loader side: source identity when a row supplies it, the pre-PRD6 natural-key
/// matching when it does not, a preflight/plan before any write, and per-record outcomes. Runs the
/// real <see cref="SeedLoader"/> against <see cref="FakeSourceApi"/>, whose repeat-safety rules mirror
/// the server's.
/// </summary>
public class SeedLoaderSourceImportTests
{
    private const string SourceSystem = "salesforce";
    private const string Instance = "00DEX0000001";

    // ---------- firms: identity, rename, partial update, conflicts ----------

    [Fact]
    public async Task Same_named_source_accounts_with_different_ids_are_reported_not_merged_and_reviewed_names_keep_both()
    {
        var api = new FakeSourceApi();

        var tally = await SeedLoader.RunAsync(api.Client, File(d => d.Firms =
        [
            Firm("Acme", "001A"),
            Firm("Acme", "001B")
        ]), CancellationToken.None);

        Assert.Single(api.Firms);
        var refused = Assert.Single(tally.Records, r => r.Kind == "firm" && r.Outcome == SeedAction.Exception);
        Assert.Equal(Ref("Account", "001B"), refused.Source);
        Assert.Contains("distinct, reviewed display name", refused.Reason);
        Assert.True(tally.HasExceptions);

        // The reviewer's alias: the same source account under a distinct display name.
        await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [Firm("Acme", "001A"), Firm("Acme (Texas)", "001B")]),
            CancellationToken.None);

        Assert.Equal(["Acme", "Acme (Texas)"], api.Firms.Select(f => f.Name).Order());
    }

    [Fact]
    public async Task A_rename_through_the_same_source_id_updates_the_one_firm()
    {
        var api = new FakeSourceApi();
        await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [Firm("Acme", "001A")]), CancellationToken.None);

        var tally = await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [Firm("Acme Logistics", "001A")]),
            CancellationToken.None);

        Assert.Equal("Acme Logistics", Assert.Single(api.Firms).Name);
        Assert.Equal(SeedAction.Updated, Assert.Single(tally.Records, r => r.Kind == "firm").Outcome);
    }

    [Fact]
    public async Task An_absent_field_is_preserved_and_clearFields_clears_it()
    {
        var api = new FakeSourceApi();
        var first = Firm("Acme", "001A");
        first.Notes = "Key account";
        first.AttributesJson = "{\"warehouses\":6}";
        await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [first]), CancellationToken.None);

        await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [Firm("Acme", "001A")]), CancellationToken.None);
        Assert.Equal("Key account", api.Firms[0].Notes);
        Assert.Equal("{\"warehouses\":6}", api.Firms[0].Attributes);

        var clearing = Firm("Acme", "001A");
        clearing.ClearFields = ["notes"];
        await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [clearing]), CancellationToken.None);
        Assert.Null(api.Firms[0].Notes);
        Assert.Equal("{\"warehouses\":6}", api.Firms[0].Attributes);
    }

    [Fact]
    public async Task A_locally_edited_field_is_reported_as_a_conflict_and_an_unchanged_firm_as_unchanged()
    {
        var api = new FakeSourceApi();
        var firm = Firm("Acme", "001A");
        firm.Notes = "From CRM";
        await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [firm]), CancellationToken.None);

        var unchanged = await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [firm]), CancellationToken.None);
        Assert.Equal(SeedAction.Unchanged, Assert.Single(unchanged.Records, r => r.Kind == "firm").Outcome);
        Assert.Equal(SeedAction.NoOp, Assert.Single(unchanged.Records, r => r.Kind == "firm").Planned);

        api.Firms[0].Notes = "Edited in the app";
        firm.Notes = "From CRM, revised";
        var tally = await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [firm]), CancellationToken.None);

        var conflict = Assert.Single(Assert.Single(tally.Records, r => r.Kind == "firm").Conflicts!);
        Assert.Equal("notes", conflict.Field);
        Assert.Equal("Edited in the app", api.Firms[0].Notes);

        var printed = new StringWriter();
        SeedReport.Print(printed, "firms.json", tally);
        Assert.Contains("CONFLICT  firm Acme: 'notes' was edited locally; kept 'Edited in the app'", printed.ToString());
        Assert.False(tally.HasExceptions); // a kept local edit is reported, not an exception
    }

    // ---------- relationships ----------

    [Fact]
    public async Task A_relationship_end_date_is_imported_and_omitting_the_relationship_later_leaves_it_untouched()
    {
        var api = new FakeSourceApi();
        var firm = Firm("Acme", "001A");
        firm.Relationships =
        [
            new SeedFirmRelationship
            {
                ProductName = "Acme WMS", AcquisitionDate = "2024-01-01", EndDate = "2025-06-30",
                Sources = [Src("Contract", "800A")]
            }
        ];
        await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [firm]), CancellationToken.None);

        var bulk = JsonDocument.Parse(api.Writes.Single(w => w.Path == "api/firms/bulk").Body).RootElement;
        var sent = bulk.GetProperty("firms")[0].GetProperty("relationships")[0];
        Assert.Equal("2025-06-30", sent.GetProperty("endDate").GetString());
        Assert.Equal("800A", sent.GetProperty("sources")[0].GetProperty("sourceRecordId").GetString());

        // An incremental file that does not mention the relationship.
        await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [Firm("Acme", "001A")]), CancellationToken.None);

        var edge = Assert.Single(api.Relationships);
        Assert.Equal("2025-06-30", edge.End);
        Assert.Equal("2024-01-01", edge.Acquisition);
    }

    [Fact]
    public async Task Two_rows_for_one_firm_and_product_are_a_preflight_problem_unless_consolidated_on_one_row()
    {
        var api = new FakeSourceApi();
        var firm = Firm("Acme", "001A");
        firm.Relationships =
        [
            new SeedFirmRelationship { ProductName = "Acme WMS", Sources = [Src("Contract", "800A")] },
            new SeedFirmRelationship { ProductName = "Acme WMS", Sources = [Src("Contract", "800B")] }
        ];

        var plan = await SeedLoader.PlanAsync(api.Client, File(d => d.Firms = [firm]), CancellationToken.None);

        Assert.Contains(plan.Problems, p => p.Contains("Acme WMS") && p.Contains("consolidat"));
        Assert.Empty(api.Writes);

        firm.Relationships = [new SeedFirmRelationship { ProductName = "Acme WMS", Sources = [Src("Contract", "800A"), Src("Contract", "800B")] }];
        await SeedLoader.RunAsync(api.Client, File(d => d.Firms = [firm]), CancellationToken.None);
        Assert.Single(api.Relationships);
        Assert.Equal(2, api.Bindings.Count(b => b.TargetType == "FirmProductRelationship"));
    }

    // ---------- opportunities ----------

    [Fact]
    public async Task Two_same_day_same_scope_opportunities_with_different_ids_both_import_and_an_identical_rerun_is_all_no_ops()
    {
        var api = new FakeSourceApi();
        api.AddFirm("Acme");
        var seed = File(d => d.Opportunities =
        [
            Opp("006A", "Acme", "2025-01-13", decided: "2025-04-28", outcome: "Won", winner: "Acme WMS"),
            Opp("006B", "Acme", "2025-01-13", decided: "2025-05-02", outcome: "Lost", winner: "Rival WMS")
        ]);

        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);
        Assert.Equal(2, api.Opportunities.Count);
        Assert.Equal(2, api.Facts.Count(f => f.Kind == "OpportunityDecided"));

        var writesBefore = api.Writes.Count;
        var rerun = await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        Assert.Equal(writesBefore, api.Writes.Count);
        Assert.All(rerun.Records.Where(r => r.Kind == "opportunity"), r => Assert.Equal(SeedAction.Unchanged, r.Outcome));
        Assert.Equal(2, api.Opportunities.Count);
        Assert.Equal(2, api.Facts.Count);
    }

    [Fact]
    public async Task Open_then_qualified_then_won_across_three_files_advances_one_opportunity_with_one_decision()
    {
        var api = new FakeSourceApi();
        api.AddFirm("Acme");

        await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities = [Opp("006A", "Acme", "2025-01-13")]), CancellationToken.None);
        Assert.Equal("Open", Assert.Single(api.Opportunities).Stage);

        var qualified = await SeedLoader.RunAsync(api.Client,
            File(d => d.Opportunities = [Opp("006A", "Acme", "2025-01-13", qualified: "2025-02-10")]), CancellationToken.None);
        Assert.Equal("Qualified", Assert.Single(api.Opportunities).Stage);
        Assert.Equal(SeedAction.Updated, Assert.Single(qualified.Records, r => r.Kind == "opportunity").Outcome);

        await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities =
            [Opp("006A", "Acme", "2025-01-13", qualified: "2025-02-10", decided: "2025-04-28", outcome: "Won", winner: "Acme WMS")]),
            CancellationToken.None);

        var opportunity = Assert.Single(api.Opportunities);
        Assert.Equal("Won", opportunity.Stage);
        Assert.Single(api.Facts, f => f.Kind == "OpportunityDecided");
        Assert.Single(api.Writes, w => w.Path.EndsWith("/decide"));
        Assert.Single(api.Writes, w => w.Path.EndsWith("/qualify"));
    }

    [Fact]
    public async Task An_open_opportunity_takes_changed_source_fields_before_it_is_decided()
    {
        var api = new FakeSourceApi();
        api.AddFirm("Acme");
        await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities = [Opp("006A", "Acme", "2025-01-13")]), CancellationToken.None);

        var changed = Opp("006A", "Acme", "2025-01-13");
        changed.ChannelName = "Direct";
        changed.Notes = "Now via direct sales";
        changed.ScopeProcessNames = ["Order Picking", "Receiving"];
        await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities = [changed]), CancellationToken.None);

        var opportunity = Assert.Single(api.Opportunities);
        Assert.Equal(FakeSourceApi.DirectChannelId, opportunity.ChannelId);
        Assert.Equal("Now via direct sales", opportunity.Notes);
        Assert.Equal(2, opportunity.Scope.Count);
    }

    [Fact]
    public async Task A_lost_decide_response_and_a_rerun_leave_one_opportunity_and_one_decision_fact()
    {
        var api = new FakeSourceApi();
        api.AddFirm("Acme");
        var seed = File(d => d.Opportunities =
            [Opp("006A", "Acme", "2025-01-13", qualified: "2025-02-10", decided: "2025-04-28", outcome: "Won", winner: "Acme WMS")]);

        api.FailWrite(3, loseResponse: true); // create, qualify, decide <- applied, response lost
        await Assert.ThrowsAsync<ApiConnectionException>(() => SeedLoader.RunAsync(api.Client, seed, CancellationToken.None));
        Assert.Equal("Won", Assert.Single(api.Opportunities).Stage);

        var rerun = await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        Assert.Single(api.Opportunities);
        Assert.Single(api.Facts, f => f.Kind == "OpportunityDecided");
        Assert.Equal(SeedAction.Unchanged, Assert.Single(rerun.Records, r => r.Kind == "opportunity").Outcome);
    }

    [Fact]
    public async Task A_changed_terminal_outcome_or_firm_is_a_correction_required_exception_and_the_run_continues()
    {
        var api = new FakeSourceApi();
        api.AddFirm("Acme");
        api.AddFirm("Beta");
        await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities =
        [
            Opp("006A", "Acme", "2025-01-13", decided: "2025-04-28", outcome: "Won", winner: "Acme WMS"),
            Opp("006B", "Acme", "2025-02-01")
        ]), CancellationToken.None);

        var tally = await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities =
        [
            Opp("006A", "Acme", "2025-01-13", decided: "2025-04-28", outcome: "Lost", winner: "Rival WMS"),
            Opp("006B", "Beta", "2025-02-01"),
            Opp("006C", "Acme", "2025-03-01")
        ]), CancellationToken.None);

        var corrections = tally.Records.Where(r => r.Kind == "opportunity" && r.Planned == SeedAction.Exception).ToList();
        Assert.Equal(2, corrections.Count);
        Assert.All(corrections, r => Assert.Contains("correction required", r.Reason, StringComparison.OrdinalIgnoreCase));
        Assert.True(tally.HasExceptions);

        // Nothing rewritten: still won, one decision fact; the run carried on to 006C.
        Assert.Equal("Won", api.Opportunities.Single(o => o.Notes == "006A").Stage);
        Assert.Single(api.Facts, f => f.Kind == "OpportunityDecided");
        Assert.Equal(3, api.Opportunities.Count);
        Assert.DoesNotContain(api.Writes, w => w.Path.EndsWith("/decide") && w.Body.Contains("Lost"));

        var printed = new StringWriter();
        SeedReport.Print(printed, "opps.json", tally);
        Assert.Contains("opportunity          updated   0, unchanged   0, exceptions   2", printed.ToString());
        Assert.Contains("EXCEPTION opportunity Acme opened 2025-01-13 [006A]", printed.ToString());
    }

    [Fact]
    public async Task An_opportunity_resolves_its_firm_by_firmSource_when_the_name_is_ambiguous()
    {
        var api = new FakeSourceApi();
        var ohio = api.AddFirm("Delta");
        api.AddFirm("Delta"); // an indistinguishable second firm created by hand
        api.Bind(Ref("Account", "001D"), "Firm", ohio.Id);

        var byName = await SeedLoader.PlanAsync(api.Client, File(d => d.Opportunities = [Opp("006A", "Delta", "2025-01-13")]),
            CancellationToken.None);
        Assert.Contains(byName.Problems, p => p.Contains("Delta") && p.Contains("firmSource"));

        var opportunity = Opp("006A", "", "2025-01-13");
        opportunity.FirmSource = Src("Account", "001D");
        await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities = [opportunity]), CancellationToken.None);

        Assert.Equal(ohio.Id, Assert.Single(api.Opportunities).FirmId);
    }

    // ---------- facts ----------

    [Fact]
    public async Task Two_same_segment_same_day_churns_with_source_ids_both_survive_with_customer_provenance()
    {
        var api = new FakeSourceApi();
        var acme = api.AddFirm("Acme");
        api.Bind(Ref("Account", "001A"), "Firm", acme.Id);
        var seed = File(d => d.ChurnEvents =
        [
            new SeedChurnEvent
            {
                SegmentName = "SMB", EffectiveDate = "2025-06-30", Source = Src("Churn", "CH-1"),
                FirmSource = Src("Account", "001A"), ProductName = "Acme WMS", SourceRelationshipRef = "800A"
            },
            new SeedChurnEvent { SegmentName = "SMB", EffectiveDate = "2025-06-30", Source = Src("Churn", "CH-2") }
        ]);

        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);
        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        var churns = api.Facts.Where(f => f.Kind == "Churn").ToList();
        Assert.Equal(2, churns.Count);
        var withCustomer = JsonDocument.Parse(churns[0].Payload!).RootElement;
        Assert.Equal(acme.Id, withCustomer.GetProperty("firmId").GetGuid());
        Assert.Equal(FakeSourceApi.OurProductId, withCustomer.GetProperty("productId").GetGuid());
        Assert.Equal("001A", withCustomer.GetProperty("sourceCustomerRef").GetString());
        Assert.Equal("800A", withCustomer.GetProperty("sourceRelationshipRef").GetString());
    }

    [Fact]
    public async Task Legacy_rows_without_source_keep_their_natural_key_matching()
    {
        var api = new FakeSourceApi();
        var seed = new SeedFile
        {
            MarketDynamics = new SeedMarketDynamics
            {
                MarketName = "Warehouse Software",
                Firms = [new SeedFirm { Name = "Acme", SegmentName = "SMB", Relationships = [new SeedFirmRelationship { ProductName = "Acme WMS" }] }],
                ChurnEvents =
                [
                    new SeedChurnEvent { SegmentName = "SMB", EffectiveDate = "2025-06-30" },
                    new SeedChurnEvent { SegmentName = "SMB", EffectiveDate = "2025-06-30" }
                ]
            }
        };

        var tally = await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        Assert.Single(api.Facts, f => f.Kind == "Churn"); // (segment, day) still collapses without a source
        Assert.Equal((1, 1), tally.Snapshot["churnEvent"]);
        Assert.Equal((1, 0), tally.Snapshot["firm"]);
        Assert.Equal(1, tally.AppliedSnapshot["firmRelationship"]);
        var item = JsonDocument.Parse(api.Writes.Single(w => w.Path == "api/firms/bulk").Body).RootElement.GetProperty("firms")[0];
        Assert.True(!item.TryGetProperty("source", out var source) || source.ValueKind == JsonValueKind.Null);
        Assert.Equal("{\"clientSegmentId\":\"" + FakeSourceApi.SmbSegmentId + "\"}", api.Facts.Single().Payload);

        var rerun = await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);
        Assert.Equal((0, 1), rerun.Snapshot["firm"]);
        Assert.Equal((0, 2), rerun.Snapshot["churnEvent"]);
    }

    [Fact]
    public async Task A_record_loaded_before_source_identity_is_an_exception_not_a_duplicate_when_its_row_gains_a_source()
    {
        var api = new FakeSourceApi();
        api.AddFirm("Acme");
        var legacy = new SeedFile
        {
            MarketDynamics = new SeedMarketDynamics
            {
                MarketName = "Warehouse Software",
                Opportunities = [new SeedOpportunity { FirmName = "Acme", OpenedDate = "2025-01-13", ScopeProcessNames = ["Order Picking"] }],
                ChurnEvents = [new SeedChurnEvent { SegmentName = "SMB", EffectiveDate = "2025-06-30" }]
            }
        };
        await SeedLoader.RunAsync(api.Client, legacy, CancellationToken.None);

        var tally = await SeedLoader.RunAsync(api.Client, File(d =>
        {
            d.Opportunities = [Opp("006A", "Acme", "2025-01-13")];
            d.ChurnEvents = [new SeedChurnEvent { SegmentName = "SMB", EffectiveDate = "2025-06-30", Source = Src("Churn", "CH-1") }];
        }), CancellationToken.None);

        Assert.Single(api.Opportunities);
        Assert.Single(api.Facts);
        Assert.Equal(2, tally.Records.Count(r => r.Kind is "opportunity" or "churnEvent" && r.Outcome == SeedAction.Exception
                                                 && r.Reason!.Contains("without source identity")));
    }

    // ---------- pagination (req 9) ----------

    [Fact]
    public async Task A_legacy_opportunity_is_matched_beyond_the_first_listing_page()
    {
        var api = new FakeSourceApi();
        var acme = api.AddFirm("Acme");
        // The listing is newest first, so the oldest deal lands on page 2 of 200-row pages.
        api.Opportunities.Add(new FakeOpportunity { Id = Guid.NewGuid(), FirmId = acme.Id, OpenedAt = Day("2020-01-01"), Scope = [FakeSourceApi.PickingProcessId] });
        for (var i = 0; i < 250; i++)
            api.Opportunities.Add(new FakeOpportunity { Id = Guid.NewGuid(), FirmId = acme.Id, OpenedAt = Day("2024-01-01").AddDays(i) });

        await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities =
            [new SeedOpportunity { FirmName = "Acme", OpenedDate = "2020-01-01", ScopeProcessNames = ["Order Picking"] }]), CancellationToken.None);

        Assert.Equal(251, api.Opportunities.Count);
        Assert.DoesNotContain(api.Writes, w => w.Path == "api/opportunities");
    }

    [Fact]
    public async Task A_legacy_arrival_count_is_matched_beyond_the_first_listing_page()
    {
        var api = new FakeSourceApi();
        for (var i = 0; i < 250; i++)
            api.Facts.Add(new FakeFact
            {
                Id = Guid.NewGuid(), Kind = "SegmentArrivalObserved", EffectiveDate = "2025-06-01", SubjectType = "ClientSegment",
                SubjectId = FakeSourceApi.SmbSegmentId, Payload = "{\"flowKind\": \"ConversionToMarket\"}"
            });
        api.Facts.Add(new FakeFact
        {
            Id = Guid.NewGuid(), Kind = "SegmentArrivalObserved", EffectiveDate = "2025-06-01", SubjectType = "ClientSegment",
            SubjectId = FakeSourceApi.SmbSegmentId, Payload = "{\"flowKind\": \"NewFirmArrival\"}"
        });

        await SeedLoader.RunAsync(api.Client, File(d => d.ArrivalCounts =
            [new SeedArrivalCount { SegmentName = "SMB", FlowKind = "NewFirmArrival", Month = "2025-06-15", Count = 2 }]),
            CancellationToken.None);

        Assert.DoesNotContain(api.Writes, w => w.Path == "api/facts");
    }

    // ---------- preflight, plan, report, resume (req 8) ----------

    [Fact]
    public async Task Preflight_lists_every_problem_and_nothing_is_written()
    {
        var api = new FakeSourceApi();
        api.AddFirm("Acme");
        var seed = File(d =>
        {
            d.Opportunities =
            [
                Opp("006A", "Nobody Inc", "2025-01-13"),                                   // unresolvable firm
                Opp("006B", "Acme", "2025-01-13", decided: "2025-02-01"),                  // decided without outcome
                Opp("006C", "Acme", "2025-01-13", decided: "2025-02-01", outcome: "Wonn"), // bad enum
                Opp("006D", "Acme", "2025-03-01", qualified: "2025-02-01"),               // qualified before opened
                Opp("006D", "Acme", "2025-04-01")                                          // duplicate source id
            ];
            d.ChurnEvents = [new SeedChurnEvent { SegmentName = "SMB", EffectiveDate = "2025-13-45", Source = Src("Churn", "CH-1") }];
        });

        var plan = await SeedLoader.PlanAsync(api.Client, seed, CancellationToken.None);

        Assert.Contains(plan.Problems, p => p.Contains("Nobody Inc"));
        Assert.Contains(plan.Problems, p => p.Contains("006B") && p.Contains("outcome"));
        Assert.Contains(plan.Problems, p => p.Contains("Wonn"));
        Assert.Contains(plan.Problems, p => p.Contains("006D") && p.Contains("qualified"));
        Assert.Contains(plan.Problems, p => p.Contains("006D") && p.Contains("more than once"));
        Assert.Contains(plan.Problems, p => p.Contains("2025-13-45"));

        var ex = await Assert.ThrowsAsync<SeedReferenceException>(() => SeedLoader.RunAsync(api.Client, seed, CancellationToken.None));
        Assert.Contains("Nobody Inc", ex.Message);
        Assert.Contains("Wonn", ex.Message);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task A_dry_run_writes_nothing_and_plans_creates_updates_no_ops_and_exceptions()
    {
        var api = new FakeSourceApi();
        var existing = api.AddFirm("Acme");
        api.Bind(Ref("Account", "001A"), "Firm", existing.Id);
        await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities =
        [
            Opp("006A", "Acme", "2025-01-13"),
            Opp("006B", "Acme", "2025-01-20", decided: "2025-04-28", outcome: "Won", winner: "Acme WMS")
        ]), CancellationToken.None);
        var writesBefore = api.Writes.Count;

        var plan = await SeedLoader.PlanAsync(api.Client, File(d =>
        {
            d.Firms = [Firm("Acme", "001A"), Firm("Beta", "001B")];
            d.Opportunities =
            [
                Opp("006A", "Acme", "2025-01-13", decided: "2025-05-01", outcome: "Lost", winner: "Rival WMS"),
                Opp("006B", "Acme", "2025-01-20", decided: "2025-04-30", outcome: "Won", winner: "Acme WMS"),
                Opp("006C", "Beta", "2025-02-01")
            ];
        }), CancellationToken.None);

        Assert.Equal(writesBefore, api.Writes.Count);
        Assert.Empty(plan.Problems);
        Assert.All(plan.Records, r => Assert.Null(r.Outcome));
        string Planned(string kind, string id) => plan.Records.Single(r => r.Kind == kind && r.Source?.SourceRecordId == id).Planned;
        Assert.Equal(SeedAction.NoOp, Planned("firm", "001A"));
        Assert.Equal(SeedAction.Create, Planned("firm", "001B"));
        Assert.Equal(SeedAction.Update, Planned("opportunity", "006A"));
        Assert.Equal(SeedAction.Exception, Planned("opportunity", "006B"));
        Assert.Equal(SeedAction.Create, Planned("opportunity", "006C"));

        var printed = new StringWriter();
        SeedReport.Print(printed, "plan.json", plan);
        Assert.StartsWith("Plan for plan.json (dry run, nothing written):", printed.ToString());
    }

    [Fact]
    public async Task The_report_file_carries_the_boundary_note_and_every_record_with_planned_action_outcome_and_source()
    {
        var api = new FakeSourceApi();
        api.AddFirm("Acme");
        var tally = await SeedLoader.RunAsync(api.Client, File(d => d.Opportunities = [Opp("006A", "Acme", "2025-01-13")]),
            CancellationToken.None);
        var path = Path.Combine(Path.GetTempPath(), $"seed-report-{Guid.NewGuid():N}.json");

        try
        {
            SeedReport.Write(path, "opps.json", tally);
            var report = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(path)).RootElement;

            Assert.Equal("opps.json", report.GetProperty("file").GetString());
            Assert.False(report.GetProperty("dryRun").GetBoolean());
            Assert.Contains("no transaction", report.GetProperty("boundary").GetString());
            var record = report.GetProperty("records").EnumerateArray().Single(r => r.GetProperty("kind").GetString() == "opportunity");
            Assert.Equal("Create", record.GetProperty("planned").GetString());
            Assert.Equal("Created", record.GetProperty("outcome").GetString());
            Assert.Equal("006A", record.GetProperty("source").GetProperty("sourceRecordId").GetString());
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_fails_midway_then_reruns_ends_in_the_same_state_as_one_clean_run(bool loseResponse)
    {
        var clean = new FakeSourceApi();
        await SeedLoader.RunAsync(clean.Client, WholeHistory(), CancellationToken.None);

        for (var failAt = 1; failAt <= clean.Writes.Count; failAt++)
        {
            var api = new FakeSourceApi();
            api.FailWrite(failAt, loseResponse);
            await Assert.ThrowsAnyAsync<ApiClientException>(() => SeedLoader.RunAsync(api.Client, WholeHistory(), CancellationToken.None));
            api.ClearFault();

            await SeedLoader.RunAsync(api.Client, WholeHistory(), CancellationToken.None);

            Assert.Equal(clean.StateFingerprint(), api.StateFingerprint());
        }
    }

    private static SeedFile WholeHistory() => File(d =>
    {
        var acme = Firm("Acme", "001A");
        acme.Relationships = [new SeedFirmRelationship { ProductName = "Acme WMS", AcquisitionDate = "2024-01-01", Sources = [Src("Contract", "800A")] }];
        d.Firms = [acme, Firm("Beta", "001B")];
        d.Opportunities =
        [
            Opp("006A", "Acme", "2025-01-13", qualified: "2025-02-10", decided: "2025-04-28", outcome: "Won", winner: "Acme WMS"),
            Opp("006B", "Beta", "2025-01-13", qualified: "2025-02-10")
        ];
        d.ChurnEvents =
        [
            new SeedChurnEvent { SegmentName = "SMB", EffectiveDate = "2025-06-30", Source = Src("Churn", "CH-1") },
            new SeedChurnEvent { SegmentName = "SMB", EffectiveDate = "2025-06-30", Source = Src("Churn", "CH-2") }
        ];
        d.ArrivalCounts = [new SeedArrivalCount { SegmentName = "SMB", Month = "2025-06-01", Count = 3, Source = Src("Arrivals", "AR-2025-06") }];
    });

    // ---------- helpers ----------

    private static SeedFile File(Action<SeedMarketDynamics> fill)
    {
        var dynamics = new SeedMarketDynamics
        {
            MarketName = "Warehouse Software",
            SourceDefaults = new SeedSourceDefaults { SourceSystem = SourceSystem, SourceInstance = Instance }
        };
        fill(dynamics);
        return new SeedFile { MarketDynamics = dynamics };
    }

    private static SeedSourceRef Src(string kind, string id) => new() { ObjectKind = kind, SourceRecordId = id };

    private static SourceRef Ref(string kind, string id) => new(SourceSystem, Instance, kind, id);

    private static SeedFirm Firm(string name, string accountId) =>
        new() { Name = name, SegmentName = "SMB", Source = Src("Account", accountId) };

    /// <summary>A sourced opportunity; its notes carry the source id so assertions can find it.</summary>
    private static SeedOpportunity Opp(string id, string firmName, string opened, string? qualified = null,
        string? decided = null, string? outcome = null, string? winner = null) => new()
    {
        FirmName = firmName, OpenedDate = opened, QualifiedDate = qualified, DecidedDate = decided, Outcome = outcome,
        WinningProductName = winner, ScopeProcessNames = ["Order Picking"], Notes = id, Source = Src("Opportunity", id)
    };

    private static DateTimeOffset Day(string date) => new(DateOnly.Parse(date).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
