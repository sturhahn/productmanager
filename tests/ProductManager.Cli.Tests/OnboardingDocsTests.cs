using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;
using ProductManager.Cli.Seed;

namespace ProductManager.Cli.Tests;

/// <summary>
/// Guards the agentic onboarding documentation (docs/onboarding/**) the way
/// <see cref="SeedFileTests"/> guards the shipped seed files: the docs tell an onboarding agent
/// exactly what JSON to write, so a stale heading, a broken cross-link, an invented seed key, a
/// misspelled enum member or a mistyped date in an example is a defect that will be copied
/// verbatim into a real user's instance. Every example here is parsed with unknown members
/// disallowed, so "documented field that does not exist" fails loudly.
/// </summary>
public class OnboardingDocsTests
{
    private const string RootReadmePath = "README.md";
    private const string GuidePath = "docs/onboarding/README.md";
    private const string ExampleSeedPath = "docs/onboarding/example-seed.json";
    private const string DatasetDirectory = "docs/onboarding/datasets";

    /// <summary>Today is 2026-09-01 (see docs/onboarding house rules) - a fact cannot be recorded
    /// in the future, and the API refuses a future recordedAt.</summary>
    private static readonly DateOnly LatestAllowedRecordedDate = new(2026, 8, 31);

    /// <summary>
    /// The 12 dataset files, listed explicitly rather than enumerated from disk so a missing file
    /// fails a test instead of silently shrinking the theory to zero cases.
    /// </summary>
    private static readonly string[] DatasetFileNames =
    [
        "01-workspace-and-teams.md",
        "02-market-structure.md",
        "03-initiatives-projects-features.md",
        "04-roadmap.md",
        "05-coverage-and-needs.md",
        "06-segment-economics.md",
        "07-covariates.md",
        "08-assumption-sets.md",
        "09-market-events.md",
        "10-channels-and-integrations.md",
        "11-firms-and-relationships.md",
        "12-historical-facts.md"
    ];

    /// <summary>The dataset-file skeleton from the onboarding spec - all eight, in this order.</summary>
    private static readonly string[] RequiredHeadings =
    [
        "## Tier and prerequisites",
        "## Questions to ask the user",
        "## Data requirements",
        "## Units and conversions",
        "## Seed JSON shape",
        "## Loading and idempotency",
        "## Verification",
        "## Common mistakes"
    ];

    /// <summary>
    /// The CLI's own wire options plus unknown-member rejection: a documented key that is not on
    /// <see cref="SeedFile"/> (a typo, or a field someone invented) must not parse silently.
    /// </summary>
    private static readonly JsonSerializerOptions StrictSeedJsonOptions =
        new(ProductManagerApiClient.JsonOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

    private static readonly Regex JsonFenceRegex = new(
        @"^[ \t]*```json[ \t]*\r?\n(?<body>.*?)^[ \t]*```",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

    private static readonly Regex MarkdownLinkRegex = new(@"\[[^\]]*\]\(([^)\s]+)\)");

    public static IEnumerable<object[]> DatasetFiles =>
        DatasetFileNames.Select(name => new object[] { name });

    /// <summary>Every markdown file the docs own: both READMEs plus the 12 dataset files.</summary>
    public static IEnumerable<object[]> MarkdownFiles =>
        new[] { RootReadmePath, GuidePath }
            .Concat(DatasetFileNames.Select(name => $"{DatasetDirectory}/{name}"))
            .Select(path => new object[] { path });

    // ---------- Repo-file helpers (same walk-up pattern as SeedFileTests) ----------

    /// <summary>
    /// Walks up from the test binary to the repo root. Anchored on ProductManager.sln rather than
    /// on the file being read, because these docs do not exist yet: we still need their expected
    /// absolute path so the failure reads "expected docs/onboarding/README.md to exist" instead of
    /// a bare FileNotFoundException from the walk itself.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "ProductManager.sln"))) return dir;
        }

        throw new FileNotFoundException(
            $"Could not locate the repo root (ProductManager.sln) by walking up from {AppContext.BaseDirectory}.");
    }

    private static string RepoPath(string relativePath) =>
        Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ReadRepoFile(string relativePath)
    {
        var path = RepoPath(relativePath);
        Assert.True(File.Exists(path), $"Expected '{relativePath}' to exist at '{path}'.");
        return File.ReadAllText(path);
    }

    private static string DatasetPath(string fileName) => $"{DatasetDirectory}/{fileName}";

    // ---------- 1-2: the entry points ----------

    [Fact]
    public void Root_readme_exists_and_links_to_the_onboarding_guide()
    {
        var content = ReadRepoFile(RootReadmePath);
        var guidePath = Path.GetFullPath(RepoPath(GuidePath));

        var linked = RelativeLinkTargets(content, RootReadmePath)
            .Any(target => string.Equals(target.ResolvedPath, guidePath, StringComparison.OrdinalIgnoreCase));

        Assert.True(linked,
            $"Root README.md must contain a relative markdown link resolving to '{GuidePath}'.");
    }

    [Fact]
    public void The_onboarding_guide_exists_and_links_to_every_dataset_file()
    {
        var content = ReadRepoFile(GuidePath);
        var linkedPaths = RelativeLinkTargets(content, GuidePath)
            .Select(target => target.ResolvedPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in DatasetFileNames)
        {
            var expected = Path.GetFullPath(RepoPath(DatasetPath(fileName)));
            Assert.True(linkedPaths.Contains(expected),
                $"{GuidePath} must link to '{DatasetPath(fileName)}' - the orchestrator guide is the only index of the dataset files.");
        }
    }

    // ---------- 3: dataset skeleton ----------

    [Theory]
    [MemberData(nameof(DatasetFiles))]
    public void Each_dataset_file_exists_and_carries_the_eight_H2_headings_in_order(string fileName)
    {
        var content = ReadRepoFile(DatasetPath(fileName));

        var headings = content
            .Split('\n')
            .Select(line => line.TrimEnd('\r', ' ', '\t'))
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToList();

        var previousIndex = -1;
        foreach (var required in RequiredHeadings)
        {
            var index = headings.IndexOf(required);
            Assert.True(index >= 0,
                $"{DatasetPath(fileName)} is missing the required heading '{required}'. Found: {string.Join(" | ", headings)}");
            Assert.True(index > previousIndex,
                $"{DatasetPath(fileName)}: heading '{required}' is out of order. Required order: {string.Join(" -> ", RequiredHeadings)}. Found: {string.Join(" | ", headings)}");
            previousIndex = index;
        }
    }

    // ---------- 4: cross-links ----------

    [Theory]
    [MemberData(nameof(MarkdownFiles))]
    public void Every_relative_markdown_link_resolves_to_an_existing_file(string relativePath)
    {
        var content = ReadRepoFile(relativePath);

        foreach (var (raw, resolved) in RelativeLinkTargets(content, relativePath))
        {
            Assert.True(File.Exists(resolved) || Directory.Exists(resolved),
                $"{relativePath}: link '{raw}' resolves to '{resolved}', which does not exist.");
        }
    }

    /// <summary>
    /// Relative markdown link targets in <paramref name="content"/>, resolved against the directory
    /// of <paramref name="relativePath"/>. External (http/https/mailto) links and pure in-page
    /// anchors are skipped; a "#fragment" suffix is stripped from the rest.
    /// </summary>
    private static List<(string Raw, string ResolvedPath)> RelativeLinkTargets(string content, string relativePath)
    {
        var containingDirectory = Path.GetDirectoryName(RepoPath(relativePath))!;
        var results = new List<(string, string)>();

        foreach (Match match in MarkdownLinkRegex.Matches(content))
        {
            var raw = match.Groups[1].Value;
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                continue;

            var withoutFragment = raw.Split('#')[0];
            if (withoutFragment.Length == 0) continue;

            var resolved = Path.GetFullPath(Path.Combine(
                containingDirectory, withoutFragment.Replace('/', Path.DirectorySeparatorChar)));
            results.Add((raw, resolved));
        }

        return results;
    }

    // ---------- 5-7: the fenced JSON examples ----------

    [Theory]
    [MemberData(nameof(DatasetFiles))]
    public void Every_json_block_in_a_dataset_file_deserializes_into_a_non_empty_seed_file(string fileName)
    {
        var blocks = JsonBlocks(fileName);

        Assert.True(blocks.Count > 0,
            $"{DatasetPath(fileName)} must contain at least one fenced ```json block (the 'Seed JSON shape' section).");

        for (var i = 0; i < blocks.Count; i++)
        {
            var seed = ParseSeedFile(blocks[i], $"{DatasetPath(fileName)} json block #{i + 1}");
            Assert.True(HasAnyTopLevelKey(seed),
                $"{DatasetPath(fileName)} json block #{i + 1} parsed to an empty SeedFile - no top-level seed key was set.");
        }
    }

    [Theory]
    [MemberData(nameof(DatasetFiles))]
    public void Every_enum_valued_string_in_a_dataset_file_example_is_an_exact_enum_member_name(string fileName)
    {
        var blocks = JsonBlocks(fileName);
        Assert.True(blocks.Count > 0, $"{DatasetPath(fileName)} must contain at least one fenced ```json block.");

        for (var i = 0; i < blocks.Count; i++)
        {
            var source = $"{DatasetPath(fileName)} json block #{i + 1}";
            AssertEnumsAreExactMemberNames(ParseSeedFile(blocks[i], source), source);
        }
    }

    [Theory]
    [MemberData(nameof(DatasetFiles))]
    public void Every_date_in_a_dataset_file_example_is_iso_and_no_recorded_date_is_in_the_future(string fileName)
    {
        var blocks = JsonBlocks(fileName);
        Assert.True(blocks.Count > 0, $"{DatasetPath(fileName)} must contain at least one fenced ```json block.");

        for (var i = 0; i < blocks.Count; i++)
        {
            var source = $"{DatasetPath(fileName)} json block #{i + 1}";
            AssertDatesAreIsoAndRecordedDatesArePast(ParseSeedFile(blocks[i], source), source);
        }
    }

    private static List<string> JsonBlocks(string fileName)
    {
        var content = ReadRepoFile(DatasetPath(fileName));
        return JsonFenceRegex.Matches(content).Select(m => m.Groups["body"].Value).ToList();
    }

    private static SeedFile ParseSeedFile(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<SeedFile>(json, StrictSeedJsonOptions)
                ?? throw new InvalidOperationException($"{source} deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"{source} does not deserialize into ProductManager.Cli.Seed.SeedFile with unknown members disallowed: {ex.Message}");
        }
    }

    private static bool HasAnyTopLevelKey(SeedFile seed) =>
        seed.Workspace is not null ||
        seed.Teams.Count > 0 ||
        seed.Initiatives.Count > 0 ||
        seed.Projects.Count > 0 ||
        seed.Features.Count > 0 ||
        seed.Roadmap is not null ||
        seed.Market is not null ||
        seed.MarketDynamics is not null;

    // ---------- 8: the shared example seed file ----------

    [Fact]
    public void The_example_seed_file_parses_strictly_into_a_non_empty_seed_file()
    {
        var seed = LoadExampleSeed();

        Assert.True(HasAnyTopLevelKey(seed), $"{ExampleSeedPath} parsed to an empty SeedFile.");
        Assert.NotNull(seed.Workspace);
        Assert.NotNull(seed.Market);
        Assert.NotNull(seed.MarketDynamics);
        Assert.NotNull(seed.Roadmap);
        Assert.NotEmpty(seed.Teams);
        Assert.NotEmpty(seed.Initiatives);
        Assert.NotEmpty(seed.Projects);
        Assert.NotEmpty(seed.Features);
    }

    [Fact]
    public void The_example_seed_file_uses_exact_enum_member_names_and_iso_dates()
    {
        var seed = LoadExampleSeed();

        AssertEnumsAreExactMemberNames(seed, ExampleSeedPath);
        AssertDatesAreIsoAndRecordedDatesArePast(seed, ExampleSeedPath);
    }

    /// <summary>
    /// example-seed.json is a single self-contained file loaded in one pass, so every by-name
    /// reference SeedLoader resolves through NameIndex must resolve inside the file itself -
    /// the same cross-check <see cref="SeedFileTests"/> performs across the three phase files.
    /// </summary>
    [Fact]
    public void The_example_seed_file_reference_names_all_resolve_within_the_file()
    {
        var seed = LoadExampleSeed();

        Assert.NotNull(seed.Market);
        Assert.NotNull(seed.MarketDynamics);
        var market = seed.Market!;
        var dynamics = seed.MarketDynamics!;

        var teamNames = NameSet(seed.Teams.Select(t => t.Name));
        var initiativeNames = NameSet(seed.Initiatives.Select(i => i.Name));
        var projectNames = NameSet(seed.Projects.Select(p => p.Name));
        var featureNames = NameSet(seed.Features.Select(f => f.Name));
        var segmentNames = NameSet(market.Segments.Select(s => s.Name));
        var processNames = NameSet(market.BusinessProcesses.Select(p => p.Name));
        var productNames = NameSet(market.Products.Select(p => p.Name));
        var competitorNames = NameSet(market.Competitors.Select(c => c.Name));
        var approachKeys = NameSet(market.Approaches.Select(a => ApproachKey(a.BusinessProcessName, a.Name)));
        var covariateNames = NameSet(dynamics.Covariates.Select(c => c.Name));
        var channelNames = NameSet(dynamics.Channels.Select(c => c.Name));
        var firmNames = NameSet(dynamics.Firms.Select(f => f.Name));

        // marketDynamics attaches to the market defined in the same file.
        Assert.Equal(market.Name, dynamics.MarketName);

        // ----- execution side -----
        foreach (var initiative in seed.Initiatives)
        foreach (var name in initiative.TargetSegmentNames)
            AssertResolves(name, segmentNames, $"initiative '{initiative.Name}' targetSegmentNames");

        foreach (var project in seed.Projects)
        {
            AssertResolves(project.InitiativeName, initiativeNames, $"project '{project.Name}' initiativeName");
            foreach (var dep in project.DependsOnProjectNames)
                AssertResolves(dep, projectNames, $"project '{project.Name}' dependsOnProjectNames");
            foreach (var featureName in project.DeliversFeatureNames)
                AssertResolves(featureName, featureNames, $"project '{project.Name}' deliversFeatureNames");
            foreach (var effort in project.EffortDemands)
                AssertResolves(effort.TeamName, teamNames, $"project '{project.Name}' effortDemands.teamName");
        }

        foreach (var feature in seed.Features)
        {
            AssertResolves(feature.ProductName, productNames, $"feature '{feature.Name}' productName");
            foreach (var delta in feature.Deltas)
            {
                AssertResolves(delta.BusinessProcessName, processNames, $"feature '{feature.Name}' delta businessProcessName");
                AssertResolves(delta.SegmentName, segmentNames, $"feature '{feature.Name}' delta segmentName");
                if (delta.ApproachName is not null && delta.BusinessProcessName is not null)
                    AssertResolves(ApproachKey(delta.BusinessProcessName, delta.ApproachName), approachKeys,
                        $"feature '{feature.Name}' delta approachName");
            }
        }

        foreach (var item in seed.Roadmap?.Items ?? [])
            AssertResolves(item.ProjectName, projectNames, "roadmap item projectName");

        // ----- market -----
        foreach (var process in market.BusinessProcesses)
            AssertResolves(process.ParentProcessName, processNames, $"business process '{process.Name}' parentProcessName");

        foreach (var approach in market.Approaches)
            AssertResolves(approach.BusinessProcessName, processNames, $"approach '{approach.Name}' businessProcessName");

        foreach (var product in market.Products)
        {
            AssertResolves(product.CompetitorName, competitorNames, $"product '{product.Name}' competitorName");
            AssertResolves(product.ParentProductName, productNames, $"product '{product.Name}' parentProductName");
            foreach (var price in product.Prices)
                AssertResolves(price.SegmentName, segmentNames, $"product '{product.Name}' price segmentName");
        }

        foreach (var coverage in market.Coverages)
        {
            AssertResolves(coverage.ProductName, productNames, "coverage productName");
            AssertResolves(coverage.BusinessProcessName, processNames, "coverage businessProcessName");
            AssertResolves(coverage.SegmentName, segmentNames, "coverage segmentName");
            if (coverage.ApproachName is not null)
                AssertResolves(ApproachKey(coverage.BusinessProcessName, coverage.ApproachName), approachKeys,
                    "coverage approachName");
        }

        foreach (var need in market.Needs)
        {
            AssertResolves(need.SegmentName, segmentNames, "need segmentName");
            AssertResolves(need.BusinessProcessName, processNames, "need businessProcessName");
        }

        // ----- market dynamics -----
        foreach (var stockState in dynamics.SegmentStockStates)
        {
            AssertResolves(stockState.SegmentName, segmentNames, "segmentStockState segmentName");
            foreach (var penetration in stockState.ModulePenetrations)
                AssertResolves(penetration.ModuleProductName, productNames,
                    $"segmentStockState '{stockState.SegmentName}' modulePenetrations.moduleProductName");
        }

        foreach (var assumptionSet in dynamics.AssumptionSets)
        foreach (var parameter in assumptionSet.Parameters)
        {
            var where = $"assumption set '{assumptionSet.Name}' parameter";
            AssertResolves(parameter.SegmentName, segmentNames, $"{where} segmentName");
            AssertResolves(parameter.BusinessProcessName, processNames, $"{where} businessProcessName");
            AssertResolves(parameter.ModuleProductName, productNames, $"{where} moduleProductName");
            AssertResolves(parameter.CovariateName, covariateNames, $"{where} covariateName");
        }

        foreach (var marketEvent in dynamics.MarketEvents)
        {
            AssertResolves(marketEvent.CompetitorName, competitorNames, $"market event '{marketEvent.Name}' competitorName");
            foreach (var multiplier in marketEvent.RateMultipliers)
                AssertResolves(multiplier.SegmentName, segmentNames,
                    $"market event '{marketEvent.Name}' rateMultipliers.segmentName");
        }

        foreach (var channel in dynamics.Channels)
        {
            foreach (var reach in channel.SegmentReaches)
                AssertResolves(reach.SegmentName, segmentNames, $"channel '{channel.Name}' segmentReaches.segmentName");
            foreach (var affinity in channel.ProductAffinities)
                AssertResolves(affinity.ProductName, productNames, $"channel '{channel.Name}' productAffinities.productName");
        }

        foreach (var integration in dynamics.Integrations)
        {
            AssertResolves(integration.ProductAName, productNames, "integration productAName");
            AssertResolves(integration.ProductBName, productNames, "integration productBName");
        }

        foreach (var firm in dynamics.Firms)
        {
            AssertResolves(firm.SegmentName, segmentNames, $"firm '{firm.Name}' segmentName");
            foreach (var relationship in firm.Relationships)
            {
                AssertResolves(relationship.ProductName, productNames, $"firm '{firm.Name}' relationships.productName");
                foreach (var processName in relationship.ServedProcessNames)
                    AssertResolves(processName, processNames, $"firm '{firm.Name}' relationships.servedProcessNames");
            }
        }

        foreach (var opportunity in dynamics.Opportunities)
        {
            var where = $"opportunity for '{opportunity.FirmName}'";
            AssertResolves(opportunity.FirmName, firmNames, $"{where} firmName");
            AssertResolves(opportunity.ChannelName, channelNames, $"{where} channelName");
            AssertResolves(opportunity.WinningProductName, productNames, $"{where} winningProductName");
            AssertResolves(opportunity.CrossSellModuleProductName, productNames, $"{where} crossSellModuleProductName");
            foreach (var processName in opportunity.ScopeProcessNames)
                AssertResolves(processName, processNames, $"{where} scopeProcessNames");
        }

        foreach (var churn in dynamics.ChurnEvents)
            AssertResolves(churn.SegmentName, segmentNames, "churn event segmentName");

        foreach (var arrival in dynamics.ArrivalCounts)
            AssertResolves(arrival.SegmentName, segmentNames, "arrival count segmentName");
    }

    private static SeedFile LoadExampleSeed() =>
        ParseSeedFile(ReadRepoFile(ExampleSeedPath), ExampleSeedPath);

    private static HashSet<string> NameSet(IEnumerable<string> names) =>
        names.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string ApproachKey(string businessProcessName, string approachName) =>
        $"{businessProcessName}\u0000{approachName}";

    private static void AssertResolves(string? name, HashSet<string> known, string where)
    {
        if (name is null) return;

        Assert.True(known.Contains(name),
            $"{ExampleSeedPath}: {where} references '{name.Replace("\u0000", " / ")}', which is not defined in the file. " +
            $"Known: {string.Join(", ", known.Select(k => k.Replace("\u0000", " / ")).OrderBy(k => k, StringComparer.Ordinal))}");
    }

    // ---------- enum / date walkers ----------

    /// <summary>
    /// Walks every string field on <see cref="SeedFile"/> that SeedLoader parses into one of the
    /// enums in src/ProductManager.Cli/Client/Enums.cs (plus choiceKind, whose vocabulary lives in
    /// <see cref="ChoiceParameterKeys.Kinds"/>) and asserts the exact member spelling.
    /// </summary>
    private static void AssertEnumsAreExactMemberNames(SeedFile seed, string source)
    {
        foreach (var initiative in seed.Initiatives)
            AssertEnumMember<StrategicIntent>(initiative.StrategicIntent, source, $"initiative '{initiative.Name}' strategicIntent");

        foreach (var project in seed.Projects)
            AssertEnumMember<ProjectStatus>(project.Status, source, $"project '{project.Name}' status");

        foreach (var feature in seed.Features)
        {
            AssertEnumMember<FeatureStatus>(feature.Status, source, $"feature '{feature.Name}' status");
            foreach (var delta in feature.Deltas)
            {
                AssertEnumMember<FeatureDeltaKind>(delta.Kind, source, $"feature '{feature.Name}' delta kind");
                AssertEnumMember<CoverageTier>(delta.FromTier, source, $"feature '{feature.Name}' delta fromTier");
                AssertEnumMember<CoverageTier>(delta.ToTier, source, $"feature '{feature.Name}' delta toTier");
            }
        }

        foreach (var coverage in seed.Market?.Coverages ?? [])
            AssertEnumMember<CoverageTier>(coverage.Tier, source,
                $"coverage '{coverage.ProductName}' / '{coverage.BusinessProcessName}' tier");

        var dynamics = seed.MarketDynamics;
        if (dynamics is null) return;

        foreach (var assumptionSet in dynamics.AssumptionSets)
        foreach (var parameter in assumptionSet.Parameters)
        {
            var where = $"assumption set '{assumptionSet.Name}' parameter";
            AssertEnumMember<DistributionType>(parameter.DistType, source, $"{where} distType");
            AssertEnumMember<ParameterProvenance>(parameter.Provenance, source, $"{where} provenance");
            AssertEnumMember<SimulationFlowKind>(parameter.FlowKind, source, $"{where} flowKind");

            if (parameter.ChoiceKind is not null)
                Assert.True(ChoiceParameterKeys.Kinds.Contains(parameter.ChoiceKind, StringComparer.Ordinal),
                    $"{source}: {where} choiceKind '{parameter.ChoiceKind}' is not in the choice-parameter vocabulary. " +
                    $"Allowed: {string.Join(", ", ChoiceParameterKeys.Kinds)}");
        }

        foreach (var marketEvent in dynamics.MarketEvents)
        {
            AssertEnumMember<MarketEventType>(marketEvent.Type, source, $"market event '{marketEvent.Name}' type");
            foreach (var multiplier in marketEvent.RateMultipliers)
                AssertEnumMember<SimulationFlowKind>(multiplier.FlowKind, source,
                    $"market event '{marketEvent.Name}' rateMultipliers.flowKind");
        }

        foreach (var firm in dynamics.Firms)
            AssertEnumMember<EpistemicStatus>(firm.EpistemicStatus, source, $"firm '{firm.Name}' epistemicStatus");

        string[] decidedOutcomes = ["Won", "Lost", "NoDecision"];
        foreach (var opportunity in dynamics.Opportunities)
        {
            if (opportunity.Outcome is null) continue;

            AssertEnumMember<OpportunityStage>(opportunity.Outcome, source,
                $"opportunity for '{opportunity.FirmName}' outcome");
            Assert.True(decidedOutcomes.Contains(opportunity.Outcome, StringComparer.Ordinal),
                $"{source}: opportunity for '{opportunity.FirmName}' has outcome '{opportunity.Outcome}'; " +
                $"a decided opportunity must be one of {string.Join(", ", decidedOutcomes)}.");
        }

        foreach (var arrival in dynamics.ArrivalCounts)
            AssertEnumMember<SimulationFlowKind>(arrival.FlowKind, source,
                $"arrival count '{arrival.SegmentName}' / '{arrival.Month}' flowKind");
    }

    private static void AssertEnumMember<TEnum>(string? value, string source, string where) where TEnum : struct, Enum
    {
        if (value is null) return;

        var isMember = Enum.TryParse<TEnum>(value, ignoreCase: false, out _) &&
                       Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal);

        Assert.True(isMember,
            $"{source}: {where} is '{value}', which is not an exact member name of {typeof(TEnum).Name}. " +
            $"Allowed: {string.Join(", ", Enum.GetNames<TEnum>())}");
    }

    /// <summary>
    /// Walks every date-typed string on <see cref="SeedFile"/>. Dates travel as ISO yyyy-MM-dd on
    /// the wire; a recordedDate is a bi-temporal "known as of" stamp, so it can be backdated but
    /// never post-dated (the API refuses a future recordedAt).
    /// </summary>
    private static void AssertDatesAreIsoAndRecordedDatesArePast(SeedFile seed, string source)
    {
        foreach (var project in seed.Projects)
        {
            AssertIsoDate(project.StartDate, source, $"project '{project.Name}' startDate");
            AssertIsoDate(project.EndDate, source, $"project '{project.Name}' endDate");
        }

        foreach (var feature in seed.Features)
            AssertIsoDate(feature.ShippedDate, source, $"feature '{feature.Name}' shippedDate");

        foreach (var item in seed.Roadmap?.Items ?? [])
        {
            AssertIsoDate(item.Start, source, $"roadmap item '{item.ProjectName}' start", required: true);
            AssertIsoDate(item.End, source, $"roadmap item '{item.ProjectName}' end", required: true);
        }

        foreach (var coverage in seed.Market?.Coverages ?? [])
            AssertIsoDate(coverage.EffectiveDate, source,
                $"coverage '{coverage.ProductName}' / '{coverage.BusinessProcessName}' effectiveDate");

        var dynamics = seed.MarketDynamics;
        if (dynamics is null) return;

        foreach (var covariate in dynamics.Covariates)
        foreach (var observation in covariate.Observations)
        {
            AssertIsoDate(observation.EffectiveDate, source,
                $"covariate '{covariate.Name}' observations.effectiveDate", required: true);
            AssertRecordedDate(observation.RecordedDate, source,
                $"covariate '{covariate.Name}' observations.recordedDate");
        }

        foreach (var marketEvent in dynamics.MarketEvents)
        {
            AssertIsoDate(marketEvent.StartDate, source, $"market event '{marketEvent.Name}' startDate", required: true);
            AssertIsoDate(marketEvent.EndDate, source, $"market event '{marketEvent.Name}' endDate");
        }

        foreach (var firm in dynamics.Firms)
        foreach (var relationship in firm.Relationships)
            AssertIsoDate(relationship.AcquisitionDate, source,
                $"firm '{firm.Name}' relationships.acquisitionDate");

        foreach (var opportunity in dynamics.Opportunities)
        {
            var where = $"opportunity for '{opportunity.FirmName}'";
            AssertIsoDate(opportunity.OpenedDate, source, $"{where} openedDate", required: true);
            AssertIsoDate(opportunity.QualifiedDate, source, $"{where} qualifiedDate");
            AssertIsoDate(opportunity.DecidedDate, source, $"{where} decidedDate");
            AssertRecordedDate(opportunity.RecordedDate, source, $"{where} recordedDate");
        }

        foreach (var churn in dynamics.ChurnEvents)
        {
            AssertIsoDate(churn.EffectiveDate, source, $"churn event '{churn.SegmentName}' effectiveDate", required: true);
            AssertRecordedDate(churn.RecordedDate, source, $"churn event '{churn.SegmentName}' recordedDate");
        }

        foreach (var arrival in dynamics.ArrivalCounts)
        {
            AssertIsoDate(arrival.Month, source, $"arrival count '{arrival.SegmentName}' month", required: true);
            AssertRecordedDate(arrival.RecordedDate, source, $"arrival count '{arrival.SegmentName}' recordedDate");
        }
    }

    private static DateOnly? AssertIsoDate(string? value, string source, string where, bool required = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            Assert.False(required, $"{source}: {where} is required and must be an ISO yyyy-MM-dd date.");
            return null;
        }

        var parsed = DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date);

        Assert.True(parsed, $"{source}: {where} is '{value}', which is not an ISO yyyy-MM-dd date.");
        return date;
    }

    private static void AssertRecordedDate(string? value, string source, string where)
    {
        var date = AssertIsoDate(value, source, where);
        if (date is null) return;

        Assert.True(date.Value <= LatestAllowedRecordedDate,
            $"{source}: {where} is '{value}', which is later than {LatestAllowedRecordedDate:yyyy-MM-dd}. " +
            "A fact cannot be recorded in the future - the API refuses a future recordedAt.");
    }
}
