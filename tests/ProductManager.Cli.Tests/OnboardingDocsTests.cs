using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const string DiscoveryDirectory = "docs/onboarding/discovery";
    private const string DiscoveryReadmePath = DiscoveryDirectory + "/README.md";
    private const string ManifestPath = DiscoveryDirectory + "/source-manifest.example.json";
    private const string AssessmentPath = DiscoveryDirectory + "/source-assessment.example.md";
    private const string FixtureReadmePath = DiscoveryDirectory + "/fixture/README.md";
    private const string PortfolioDirectory = "docs/onboarding/portfolio";
    private const string PortfolioReadmePath = PortfolioDirectory + "/README.md";
    private const string PortfolioMappingPath = PortfolioDirectory + "/portfolio-mapping.example.json";
    private const string PortfolioFixtureReadmePath = PortfolioDirectory + "/fixture/README.md";
    private const string ReviewDirectory = "docs/onboarding/review";
    private const string ReviewReadmePath = ReviewDirectory + "/README.md";
    private const string ReviewExamplePath = ReviewDirectory + "/review.example.md";
    private const string RegisterTemplatePath = ReviewDirectory + "/decision-register.template.json";
    private const string RegisterExamplePath = ReviewDirectory + "/decision-register.example.json";
    private const string ReviewSeedDirectory = ReviewDirectory + "/fixture/seed";

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

    /// <summary>Every markdown file the docs own: the READMEs, the discovery docs, and the 12 dataset files.</summary>
    public static IEnumerable<object[]> MarkdownFiles =>
        new[]
        {
            RootReadmePath, GuidePath, DiscoveryReadmePath, AssessmentPath, FixtureReadmePath,
            PortfolioReadmePath, PortfolioFixtureReadmePath, ReviewReadmePath, ReviewExamplePath,
            CommercialReadmePath, CommercialFixtureReadmePath, ReadinessReadmePath, ReadinessFixtureReadmePath
        }
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

    // ---------- 9: source discovery (PRD3) ----------

    private static readonly string[] AllowedReadiness =
    [
        "ready", "requires a mapping decision", "requires another source", "intentionally deferred"
    ];

    private static readonly string[] RequiredSourceKeys =
    [
        "system", "instance", "extractedAt", "filters", "observationPeriod", "objects",
        "availableHistory", "completeness", "absentData", "accessLimitations"
    ];

    private static readonly string[] RequiredObjectKeys =
        ["objectType", "fields", "sourceIds", "sourceLinks", "rowCount", "pagesFetched"];

    private static readonly Regex CredentialKeyRegex = new(
        "token|password|passwd|secret|api[_-]?key|authorization|bearer|credential|client[_-]?secret",
        RegexOptions.IgnoreCase);

    private static readonly Regex CredentialValueRegex = new(
        @"bearer\s+\S+|password\s*[:=]|secret\s*[:=]|api[_-]?key\s*[:=]|(access|refresh)[_-]?token|\bsk-[A-Za-z0-9]{8,}|00D\w{12,15}![A-Za-z0-9._]{20,}",
        RegexOptions.IgnoreCase);

    private static JsonNode ReadJson(string relativePath) =>
        JsonNode.Parse(ReadRepoFile(relativePath))
        ?? throw new Xunit.Sdk.XunitException($"{relativePath} parsed to null.");

    private static JsonArray Sources(JsonNode manifest) => manifest["sources"]!.AsArray();

    [Fact]
    public void The_guide_links_to_the_discovery_procedure()
    {
        var linked = RelativeLinkTargets(ReadRepoFile(GuidePath), GuidePath)
            .Select(target => target.ResolvedPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(Path.GetFullPath(RepoPath(DiscoveryReadmePath)), linked);
        Assert.Contains(Path.GetFullPath(RepoPath(ManifestPath)), linked);
    }

    [Fact]
    public void The_source_manifest_carries_the_required_keys_for_every_source_and_object()
    {
        var manifest = ReadJson(ManifestPath);

        foreach (var key in new[] { "manifestVersion", "discovery", "scope", "sources", "authorityByConcept", "datasetReadiness", "openQuestions" })
            Assert.True(manifest[key] is not null, $"{ManifestPath} is missing top-level key '{key}'.");

        var scope = manifest["scope"]!;
        foreach (var key in new[] { "requestedOutcome", "planningHorizon", "commercialObservationPeriod", "containers" })
            Assert.True(scope[key] is not null, $"{ManifestPath} scope is missing '{key}'.");

        var systems = Sources(manifest).Select(s => s!["system"]!.GetValue<string>()).ToList();
        Assert.Contains("Salesforce", systems);
        Assert.Contains("Aha!", systems);
        Assert.Contains("Jira", systems);

        foreach (var source in Sources(manifest))
        {
            var system = source!["system"]!.GetValue<string>();
            foreach (var key in RequiredSourceKeys)
                Assert.True(source[key] is not null, $"{ManifestPath}: source '{system}' is missing '{key}'.");

            Assert.True(source["instance"]!.AsObject().Count > 0, $"{system}: instance identity is empty.");
            Assert.True(DateTimeOffset.TryParse(source["extractedAt"]!.GetValue<string>(), out _),
                $"{system}: extractedAt is not a timestamp.");
            Assert.NotEmpty(source["filters"]!.AsArray());

            foreach (var obj in source["objects"]!.AsArray())
            foreach (var key in RequiredObjectKeys)
                Assert.True(obj![key] is not null,
                    $"{ManifestPath}: {system} object '{obj["objectType"]}' is missing '{key}'.");
        }
    }

    /// <summary>
    /// Acceptance criterion 3: counts include all pages. Every object's rowCount, ID list and page
    /// count must agree with the raw response files in the fixture it was derived from.
    /// </summary>
    [Fact]
    public void Manifest_row_counts_equal_the_records_summed_across_the_fixture_page_files()
    {
        var manifest = ReadJson(ManifestPath);
        var paginatedSeen = false;

        foreach (var source in Sources(manifest))
        foreach (var obj in source!["objects"]!.AsArray())
        {
            var label = $"{source["system"]} {obj!["objectType"]}";
            var files = obj["rawResponseFiles"]!.AsArray().Select(f => f!.GetValue<string>()).ToList();
            var total = files.Sum(file => CountRecords(ReadJson($"{DiscoveryDirectory}/{file}")["response"]!));

            Assert.Equal(total, obj["rowCount"]!.GetValue<int>());
            Assert.Equal(total, obj["sourceIds"]!.AsArray().Count);
            Assert.Equal(total, obj["sourceLinks"]!.AsArray().Count);
            Assert.True(files.Count == obj["pagesFetched"]!.GetValue<int>(), $"{label}: pagesFetched != page files.");
            if (files.Count > 1) paginatedSeen = true;

            if (obj["extractionMode"]!.GetValue<string>() == "full")
                Assert.True(obj["populationSize"]!.GetValue<int>() == total,
                    $"{label}: a full extraction must equal the population size.");
        }

        Assert.True(paginatedSeen, "The fixture must include a multi-page source response.");

        var opportunity = Sources(manifest)
            .SelectMany(s => s!["objects"]!.AsArray())
            .Single(o => o!["objectType"]!.GetValue<string>() == "Opportunity")!;
        Assert.True(opportunity["pagesFetched"]!.GetValue<int>() >= 2);
        Assert.True(opportunity["rawResponseFiles"]!.AsArray().Count >= 2);
    }

    private static int CountRecords(JsonNode response)
    {
        foreach (var name in new[] { "records", "features", "issues" })
            if (response is JsonObject o && o[name] is JsonArray array)
                return array.Count;

        throw new Xunit.Sdk.XunitException("A fixture response has no records/features/issues array.");
    }

    [Fact]
    public void The_source_manifest_and_fixture_contain_no_credentials()
    {
        var files = new[] { ManifestPath }
            .Concat(Directory.GetFiles(RepoPath($"{DiscoveryDirectory}/fixture"), "*.json", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(FindRepoRoot(), f).Replace('\\', '/')));

        foreach (var file in files)
            AssertNoCredentials(ReadJson(file), file);

        void AssertNoCredentials(JsonNode? node, string where)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, child) in obj)
                    {
                        Assert.False(CredentialKeyRegex.IsMatch(key), $"{where}: key '{key}' looks like a credential.");
                        AssertNoCredentials(child, where);
                    }
                    break;
                case JsonArray array:
                    foreach (var child in array) AssertNoCredentials(child, where);
                    break;
                case JsonValue value when value.TryGetValue<string>(out var text):
                    Assert.False(CredentialValueRegex.IsMatch(text), $"{where}: value '{text}' looks like a credential.");
                    break;
            }
        }
    }

    [Fact]
    public void Discovery_declares_it_created_no_entities_and_changed_no_source_records()
    {
        var discovery = ReadJson(ManifestPath)["discovery"]!;

        Assert.True(discovery["readOnly"]!.GetValue<bool>());
        Assert.Equal(0, discovery["productManagerEntitiesCreated"]!.GetValue<int>());
        Assert.Equal(0, discovery["sourceRecordsChanged"]!.GetValue<int>());
    }

    [Fact]
    public void Every_dataset_is_classified_with_one_of_the_four_readiness_values()
    {
        var readiness = ReadJson(ManifestPath)["datasetReadiness"]!.AsArray();

        Assert.Equal(
            Enumerable.Range(1, 12).Select(i => i.ToString("00")),
            readiness.Select(r => r!["datasetId"]!.GetValue<string>()));

        foreach (var entry in readiness)
        {
            var id = entry!["datasetId"]!.GetValue<string>();
            Assert.Contains(entry["classification"]!.GetValue<string>(), AllowedReadiness);
            var fileName = DatasetFileNames.Single(n => n.StartsWith(id + "-", StringComparison.Ordinal));
            Assert.Equal($"datasets/{fileName}", entry["file"]!.GetValue<string>());
            foreach (var key in new[] { "observed", "proposedAssumptions", "gaps" })
                Assert.True(entry[key] is JsonArray, $"dataset {id}: '{key}' must be an array.");
        }

        // The fixture exercises every classification, so a doc edit cannot silently drop one.
        Assert.Equal(AllowedReadiness.Order(),
            readiness.Select(r => r!["classification"]!.GetValue<string>()).Distinct().Order());
    }

    /// <summary>Acceptance criterion 2: unreadable history is an access limitation, never absent data.</summary>
    [Fact]
    public void Inaccessible_contract_history_is_an_access_limitation_with_affected_fields_and_not_absent_data()
    {
        var salesforce = Sources(ReadJson(ManifestPath))
            .Single(s => s!["system"]!.GetValue<string>() == "Salesforce")!;

        var limitation = salesforce["accessLimitations"]!.AsArray()
            .Single(l => l!["object"]!.GetValue<string>() == "ContractHistory")!;
        Assert.Equal("accessLimitation", limitation["kind"]!.GetValue<string>());
        Assert.Equal(403, limitation["httpStatus"]!.GetValue<int>());
        Assert.NotEmpty(limitation["affectedFields"]!.AsArray());
        Assert.Contains("acquisitionDate", limitation["affectedFields"]!.ToJsonString());
        Assert.Contains("churnEvents", limitation["affectedFields"]!.ToJsonString());

        Assert.DoesNotContain(salesforce["absentData"]!.AsArray(),
            a => a!["object"]!.GetValue<string>() == "ContractHistory");

        var history = salesforce["availableHistory"]!.AsArray()
            .Single(h => h!["object"]!.GetValue<string>() == "ContractHistory")!;
        Assert.Equal("not readable", history["status"]!.GetValue<string>());

        var denied = ReadJson($"{DiscoveryDirectory}/fixture/salesforce/contract-history-denied.json");
        Assert.Equal(403, denied["httpStatus"]!.GetValue<int>());
    }

    /// <summary>Acceptance criterion 4: commercial gaps must not block a roadmap-only (tier A) onboarding.</summary>
    [Fact]
    public void Tier_A_is_completable_and_no_commercial_gap_touches_a_tier_A_dataset()
    {
        var manifest = ReadJson(ManifestPath);
        var tierA = manifest["tierReadiness"]!["A"]!;
        var tierADatasets = tierA["datasets"]!.AsArray().Select(d => d!.GetValue<string>()).ToList();

        Assert.Equal(new[] { "01", "03", "04" }, tierADatasets);
        Assert.Equal("completable", tierA["status"]!.GetValue<string>());
        Assert.False(tierA["blockedByCommercialGaps"]!.GetValue<bool>());

        foreach (var gap in manifest["gaps"]!.AsArray())
        {
            Assert.Equal("gap", gap!["status"]!.GetValue<string>());
            Assert.Empty(gap["affectsDatasets"]!.AsArray().Select(d => d!.GetValue<string>()).Intersect(tierADatasets));
        }

        var gapConcepts = manifest["gaps"]!.ToJsonString();
        Assert.Contains("finance costs", gapConcepts);
        Assert.Contains("market-wide totals", gapConcepts);

        foreach (var entry in manifest["datasetReadiness"]!.AsArray()
                     .Where(r => tierADatasets.Contains(r!["datasetId"]!.GetValue<string>())))
            Assert.NotEqual("requires another source", entry!["classification"]!.GetValue<string>());
    }

    /// <summary>Acceptance criterion 1: segmentation candidates are proposed, not a manual export request.</summary>
    [Fact]
    public void Segmentation_candidates_are_proposed_and_the_top_ranked_question_asks_which_is_authoritative()
    {
        var manifest = ReadJson(ManifestPath);
        var account = Sources(manifest)
            .SelectMany(s => s!["objects"]!.AsArray())
            .Single(o => o!["objectType"]!.GetValue<string>() == "Account")!;

        Assert.True(account["segmentationCandidates"]!.AsArray().Count >= 2);

        var q1 = manifest["openQuestions"]!.AsArray().Single(q => q!["rank"]!.GetValue<int>() == 1)!;
        Assert.Contains("authoritative", q1["question"]!.GetValue<string>());
        Assert.Contains("Segment__c", q1["candidateAnswers"]!.ToJsonString());
        Assert.DoesNotContain("export", q1["candidateAnswers"]!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_questions_are_ranked_contiguously_carry_candidates_and_appear_in_the_assessment()
    {
        var questions = ReadJson(ManifestPath)["openQuestions"]!.AsArray();
        var assessment = ReadRepoFile(AssessmentPath);

        Assert.Equal(Enumerable.Range(1, questions.Count), questions.Select(q => q!["rank"]!.GetValue<int>()));

        foreach (var q in questions)
        {
            var id = q!["id"]!.GetValue<string>();
            Assert.NotEmpty(q["candidateAnswers"]!.AsArray());
            Assert.NotEmpty(q["affectsDatasets"]!.AsArray());
            Assert.Contains(id, assessment);
            Assert.Contains(q["question"]!.GetValue<string>(), assessment);
        }
    }

    [Fact]
    public void The_brief_template_carries_the_manifest_and_agreed_answers_slot()
    {
        var guide = ReadRepoFile(GuidePath);

        Assert.Contains("Source manifest:", guide);
        Assert.Contains("Answers already agreed", guide);
    }

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

    // ---------- PRD4: portfolio reconciliation ----------
    // The mapping example is the shared artifact every dataset agent consumes; the fixture is the
    // Aha!/Jira/Salesforce extract it was derived from; the seed fragments are generated from it.

    private static readonly string[] PortfolioSeedFiles =
        ["01-workspace-and-teams.json", "03-initiatives-projects-features.json", "04-roadmap.json"];

    private static readonly string[] DateMeanings =
        ["productTarget", "engineeringForecast", "actualCompletion", "customerAvailability", "salesCommitment"];

    private static readonly string[] MappingConcepts =
        ["initiative", "project", "feature", "team", "businessProcess"];

    private static JsonNode Mapping() => ReadJson(PortfolioMappingPath);

    private static IEnumerable<JsonNode> Nodes(JsonNode? array) => array!.AsArray().Select(n => n!);

    private static IEnumerable<JsonNode> MappingRecords(JsonNode mapping, string concept) =>
        Nodes(mapping["records"]).Where(r => r["concept"]!.GetValue<string>() == concept);

    private static JsonNode RecordByKey(JsonNode mapping, string key) =>
        Nodes(mapping["records"]).Single(r => r["key"]!.GetValue<string>() == key);

    private static JsonNode RecordByName(JsonNode mapping, string concept, string name) =>
        MappingRecords(mapping, concept).Single(r => r["name"]!.GetValue<string>() == name);

    private static List<string> SourceIds(JsonNode? sources) =>
        Nodes(sources).Select(s => s["id"]!.GetValue<string>()).ToList();

    private static List<string> Strings(JsonNode? array) =>
        Nodes(array).Select(n => n.GetValue<string>()).ToList();

    private static JsonNode PortfolioFixture(string relativePath) =>
        ReadJson($"{PortfolioDirectory}/fixture/{relativePath}")["response"]!;

    /// <summary>The portfolio candidates: every Aha! feature and Jira epic in the fixture extracts.
    /// Salesforce rows are date evidence, not candidates.</summary>
    private static List<string> PortfolioCandidateIds() =>
        Nodes(PortfolioFixture("aha/features.json")["features"]).Select(f => f["reference_num"]!.GetValue<string>())
            .Concat(Nodes(PortfolioFixture("jira/epics.json")["issues"]).Select(i => i["key"]!.GetValue<string>()))
            .ToList();

    private static HashSet<string> FixtureStrings()
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(RepoPath($"{PortfolioDirectory}/fixture"), "*.json", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}seed{Path.DirectorySeparatorChar}")))
            Collect(JsonNode.Parse(File.ReadAllText(file)));
        return found;

        void Collect(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj: foreach (var (_, child) in obj) Collect(child); break;
                case JsonArray array: foreach (var child in array) Collect(child); break;
                case JsonValue value when value.TryGetValue<string>(out var text): found.Add(text); break;
            }
        }
    }

    /// <summary>Which disposition(s) each source ID ended up in: selected (a mapping record),
    /// excluded, or exception (a held exception; flagged exceptions keep their record selected).</summary>
    private static Dictionary<string, HashSet<string>> Dispositions(JsonNode mapping)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(JsonNode? sources, string kind)
        {
            foreach (var id in SourceIds(sources))
            {
                if (!map.TryGetValue(id, out var kinds)) map[id] = kinds = [];
                kinds.Add(kind);
            }
        }

        foreach (var record in Nodes(mapping["records"])) Add(record["sources"], "selected");
        foreach (var exclusion in Nodes(mapping["exclusions"])) Add(exclusion["sources"], "excluded");
        foreach (var exception in Nodes(mapping["exceptions"]).Where(e => e["disposition"]!.GetValue<string>() == "held"))
            Add(exception["sources"], "exception");
        return map;
    }

    private static SeedFile PortfolioSeedFile(string fileName) =>
        ParseSeedFile(ReadRepoFile($"{PortfolioDirectory}/fixture/seed/{fileName}"), fileName);

    private static SeedFile MergedPortfolioSeed()
    {
        var merged = new SeedFile();
        foreach (var fileName in PortfolioSeedFiles)
        {
            var part = PortfolioSeedFile(fileName);
            merged.Teams.AddRange(part.Teams);
            merged.Initiatives.AddRange(part.Initiatives);
            merged.Projects.AddRange(part.Projects);
            merged.Features.AddRange(part.Features);
            merged.Roadmap ??= part.Roadmap;
        }

        return merged;
    }

    [Fact]
    public void The_mapping_references_the_discovery_manifest_instead_of_repeating_instance_identity()
    {
        var mapping = Mapping();
        var manifestRef = mapping["manifest"]!;
        var manifestFile = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(RepoPath(PortfolioMappingPath))!, manifestRef["file"]!.GetValue<string>()));
        Assert.Equal(Path.GetFullPath(RepoPath(ManifestPath)), manifestFile);

        var manifest = ReadJson(ManifestPath);
        Assert.Equal(manifest["discovery"]!["runId"]!.GetValue<string>(), manifestRef["runId"]!.GetValue<string>());

        var manifestSourceIds = Sources(manifest).Select(s => s!["sourceId"]!.GetValue<string>()).ToHashSet();
        var all = Nodes(mapping["records"]).Concat(Nodes(mapping["exclusions"]))
            .Concat(Nodes(mapping["exceptions"]).Where(e => e["sources"] is not null));
        foreach (var source in all.SelectMany(n => n["sources"] is JsonArray a ? Nodes(a) : Enumerable.Empty<JsonNode>()))
        {
            Assert.Contains(source["system"]!.GetValue<string>(), manifestSourceIds);
            Assert.False(source["link"]!.GetValue<string>().StartsWith("http", StringComparison.OrdinalIgnoreCase),
                $"link '{source["link"]}' repeats instance identity; links are relative to the manifest instance.");
        }
    }

    [Fact]
    public void Every_mapping_record_has_key_sources_concept_decision_and_rationale_and_every_exclusion_and_exception_a_reason()
    {
        var mapping = Mapping();
        var records = Nodes(mapping["records"]).ToList();

        Assert.Equal(records.Count, records.Select(r => r["key"]!.GetValue<string>()).Distinct().Count());
        foreach (var record in records)
        {
            var key = record["key"]!.GetValue<string>();
            Assert.Contains(record["concept"]!.GetValue<string>(), MappingConcepts);
            Assert.False(string.IsNullOrWhiteSpace(record["name"]!.GetValue<string>()), $"{key}: approved name.");
            Assert.Equal("selected", record["decision"]!.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(record["rationale"]!.GetValue<string>()), $"{key}: rationale.");
            Assert.NotEmpty(record["sources"]!.AsArray());
            foreach (var source in Nodes(record["sources"]))
            {
                Assert.False(string.IsNullOrWhiteSpace(source["id"]!.GetValue<string>()), $"{key}: source id.");
                Assert.False(string.IsNullOrWhiteSpace(source["link"]!.GetValue<string>()), $"{key}: source link.");
            }
        }

        Assert.Equal(MappingConcepts.Order(), records.Select(r => r["concept"]!.GetValue<string>()).Distinct().Order());
        Assert.NotEmpty(mapping["exclusions"]!.AsArray());
        Assert.NotEmpty(mapping["exceptions"]!.AsArray());
        foreach (var node in Nodes(mapping["exclusions"]).Concat(Nodes(mapping["exceptions"])))
        {
            Assert.False(string.IsNullOrWhiteSpace(node["reason"]!.GetValue<string>()), $"{node["key"]}: reason.");
            Assert.NotEmpty(node["key"]!.GetValue<string>());
        }

        foreach (var exception in Nodes(mapping["exceptions"]))
        {
            var held = exception["disposition"]!.GetValue<string>() == "held";
            Assert.Contains(exception["disposition"]!.GetValue<string>(), new[] { "held", "flagged" });
            Assert.Contains(exception["reviewPriority"]!.GetValue<string>(), new[] { "high", "medium", "low" });
            Assert.False(held && exception["sources"] is null, $"{exception["key"]}: a held exception names its sources.");
            Assert.False(!held && exception["recordKeys"] is null, $"{exception["key"]}: a flagged exception names its records.");
            foreach (var recordKey in exception["recordKeys"] is JsonArray keys ? Strings(keys) : [])
                Assert.Equal("selected", RecordByKey(mapping, recordKey)["decision"]!.GetValue<string>());
        }
    }

    [Fact]
    public void Every_mapping_source_exists_in_the_fixture_and_every_candidate_is_selected_excluded_or_an_exception_exactly_once()
    {
        var mapping = Mapping();
        var fixtureStrings = FixtureStrings();
        var dispositions = Dispositions(mapping);

        foreach (var id in dispositions.Keys)
            Assert.True(fixtureStrings.Contains(id), $"Mapping source '{id}' does not appear in the fixture extracts.");

        var candidates = PortfolioCandidateIds();
        Assert.True(candidates.Count >= 10, "The fixture must hold a realistic set of Aha! features and Jira epics.");
        foreach (var candidate in candidates)
        {
            Assert.True(dispositions.TryGetValue(candidate, out var kinds), $"{candidate} was silently dropped.");
            Assert.True(kinds!.Count == 1, $"{candidate} has conflicting dispositions: {string.Join(", ", kinds)}.");
        }

        // Date evidence names its source as system:id; every one must exist in the fixture too.
        foreach (var evidence in Nodes(mapping["records"]).Where(r => r["dates"] is not null)
                     .SelectMany(r => DateMeanings.Select(m => r["dates"]![m])).Where(d => d is not null))
            Assert.Contains(evidence!["source"]!.GetValue<string>().Split(':')[1], fixtureStrings);
    }

    [Fact]
    public void Seed_fragments_parse_strictly_own_only_their_keys_and_use_exactly_the_approved_names()
    {
        var mapping = Mapping();
        foreach (var fileName in PortfolioSeedFiles)
        {
            var seed = PortfolioSeedFile(fileName);
            var source = $"{PortfolioDirectory}/fixture/seed/{fileName}";
            Assert.True(HasAnyTopLevelKey(seed), $"{source} is empty.");
            AssertEnumsAreExactMemberNames(seed, source);
            AssertDatesAreIsoAndRecordedDatesArePast(seed, source);
        }

        var teams = PortfolioSeedFile(PortfolioSeedFiles[0]);
        Assert.Empty(teams.Initiatives.Concat<object>(teams.Projects).Concat(teams.Features));
        var execution = PortfolioSeedFile(PortfolioSeedFiles[1]);
        Assert.Empty(execution.Teams);
        Assert.Null(execution.Roadmap);
        var roadmap = PortfolioSeedFile(PortfolioSeedFiles[2]);
        Assert.Empty(roadmap.Projects.Concat<object>(roadmap.Features).Concat(roadmap.Initiatives).Concat(roadmap.Teams));

        var seedAll = MergedPortfolioSeed();
        void AssertSameNames(string concept, IEnumerable<string> seedNames)
        {
            var approved = MappingRecords(mapping, concept).Select(r => r["name"]!.GetValue<string>()).Order().ToList();
            Assert.Equal(approved, seedNames.Order().ToList());
        }

        AssertSameNames("initiative", seedAll.Initiatives.Select(i => i.Name));
        AssertSameNames("project", seedAll.Projects.Select(p => p.Name));
        AssertSameNames("feature", seedAll.Features.Select(f => f.Name));
        AssertSameNames("team", seedAll.Teams.Select(t => t.Name));

        var projectNames = NameSet(seedAll.Projects.Select(p => p.Name));
        var featureNames = NameSet(seedAll.Features.Select(f => f.Name));
        var initiativeNames = NameSet(seedAll.Initiatives.Select(i => i.Name));
        var teamNames = NameSet(seedAll.Teams.Select(t => t.Name));
        foreach (var project in seedAll.Projects)
        {
            AssertResolves(project.InitiativeName, initiativeNames, $"project '{project.Name}' initiativeName");
            foreach (var name in project.DependsOnProjectNames) AssertResolves(name, projectNames, $"project '{project.Name}' dependency");
            foreach (var name in project.DeliversFeatureNames) AssertResolves(name, featureNames, $"project '{project.Name}' feature");
            foreach (var demand in project.EffortDemands) AssertResolves(demand.TeamName, teamNames, $"project '{project.Name}' demand team");
        }

        Assert.NotNull(seedAll.Roadmap);
        foreach (var item in seedAll.Roadmap!.Items) AssertResolves(item.ProjectName, projectNames, "roadmap item project");
    }

    /// <summary>Acceptance criterion 1: one Aha! capability, two Jira epics, one Feature, two Projects.</summary>
    [Fact]
    public void One_capability_linked_to_two_epics_becomes_one_feature_delivered_by_two_traceable_projects()
    {
        var mapping = Mapping();
        var seed = MergedPortfolioSeed();

        var features = MappingRecords(mapping, "feature").Where(r => SourceIds(r["sources"]).Contains("HWMS-1")).ToList();
        var feature = Assert.Single(features);
        var name = feature["name"]!.GetValue<string>();
        Assert.NotEqual("Carrier Connect v1", name); // an Aha! feature is not automatically a domain Feature
        Assert.Single(seed.Features, f => f.Name == name);

        var projects = Strings(feature["relations"]!["deliveredBy"]).Select(k => RecordByKey(mapping, k)).ToList();
        Assert.Equal(new[] { "HINT-12", "HWEB-9" }, projects.SelectMany(p => SourceIds(p["sources"])).Order());
        Assert.Equal(2, seed.Projects.Count(p => p.DeliversFeatureNames.Contains(name)));
        Assert.DoesNotContain(MappingRecords(mapping, "feature"), r => SourceIds(r["sources"]).Intersect(["HINT-12", "HWEB-9"]).Any());
        Assert.DoesNotContain(seed.Features, f => f.Name != name && f.Name.Contains("Carrier", StringComparison.OrdinalIgnoreCase));

        // The explicit links exist on the Jira side of the fixture (evidence of association).
        var epics = Nodes(PortfolioFixture("jira/epics.json")["issues"]).ToList();
        foreach (var key in new[] { "HINT-12", "HWEB-9" })
            Assert.Equal("HWMS-1", epics.Single(e => e["key"]!.GetValue<string>() == key)["fields"]!["customfield_10200"]!.GetValue<string>());
    }

    /// <summary>Acceptance criterion 2: similarly named, unlinked epics stay separate and go to review.</summary>
    [Fact]
    public void Two_similarly_named_unlinked_epics_are_not_merged_and_are_held_for_review()
    {
        var mapping = Mapping();
        var epics = Nodes(PortfolioFixture("jira/epics.json")["issues"])
            .Where(e => new[] { "HCORE-7", "HINT-16" }.Contains(e["key"]!.GetValue<string>())).ToList();
        Assert.Equal(2, epics.Count);
        Assert.All(epics, e => Assert.Contains("wave picking", e["fields"]!["summary"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase));
        Assert.All(epics, e => Assert.Null(e["fields"]!["customfield_10200"]));

        var held = Nodes(mapping["exceptions"]).Single(e => SourceIds(e["sources"] ?? new JsonArray()).Contains("HCORE-7"));
        Assert.Equal("held", held["disposition"]!.GetValue<string>());
        Assert.Contains("HINT-16", SourceIds(held["sources"]));
        Assert.False(held["merge"]!.GetValue<bool>());
        Assert.NotEmpty(held["proposedMatches"]!.AsArray());

        Assert.DoesNotContain(Nodes(mapping["records"]), r => SourceIds(r["sources"]).Intersect(["HCORE-7", "HINT-16"]).Any());
        Assert.DoesNotContain(MergedPortfolioSeed().Projects, p => p.Name.Contains("Wave picking", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Acceptance criterion 3: Jira Done with an unreleased capability produces no ship date.</summary>
    [Fact]
    public void A_done_epic_with_an_unreleased_capability_produces_no_shipped_date()
    {
        var mapping = Mapping();
        var seed = MergedPortfolioSeed();
        var feature = RecordByName(mapping, "feature", "Cycle counting");

        Assert.Equal("Done", Nodes(PortfolioFixture("jira/epics.json")["issues"])
            .Single(e => e["key"]!.GetValue<string>() == "HWEB-3")["fields"]!["status"]!["name"]!.GetValue<string>());
        Assert.NotNull(feature["dates"]!["actualCompletion"]);
        Assert.Null(feature["dates"]!["customerAvailability"]);

        var seeded = seed.Features.Single(f => f.Name == "Cycle counting");
        Assert.Null(seeded.ShippedDate);
        Assert.NotEqual("Shipped", seeded.Status);
        Assert.Contains(Nodes(mapping["exceptions"]), e => e["kind"]!.GetValue<string>() == "doneNotReleased"
            && Strings(e["recordKeys"]).Contains(feature["key"]!.GetValue<string>()));

        // A shipped feature is only ever shipped on customer-availability evidence from the authority.
        foreach (var shipped in seed.Features.Where(f => f.ShippedDate is not null))
        {
            var availability = RecordByName(mapping, "feature", shipped.Name)["dates"]!["customerAvailability"]!;
            Assert.Equal(shipped.ShippedDate, availability["value"]!.GetValue<string>());
            Assert.StartsWith("aha:", availability["source"]!.GetValue<string>());
        }
    }

    /// <summary>Acceptance criterion 4 and requirement 5: five date meanings, an authority-chosen planning date.</summary>
    [Fact]
    public void A_sales_promise_conflicting_with_the_plan_is_an_exception_and_does_not_overwrite_the_planning_date()
    {
        var mapping = Mapping();
        var seed = MergedPortfolioSeed();
        var exceptionKeys = Nodes(mapping["exceptions"]).Select(e => e["key"]!.GetValue<string>()).ToHashSet();

        foreach (var feature in MappingRecords(mapping, "feature"))
        {
            var dates = feature["dates"]!;
            foreach (var meaning in DateMeanings) Assert.True(dates.AsObject().ContainsKey(meaning), $"{feature["key"]}: {meaning}.");
            var planning = dates["proposedPlanningDate"]!;
            Assert.Contains(planning["meaning"]!.GetValue<string>(), new[] { "productTarget", "customerAvailability" });
            Assert.Equal(dates[planning["meaning"]!.GetValue<string>()]!["value"]!.GetValue<string>(), planning["value"]!.GetValue<string>());
            foreach (var conflict in Strings(planning["conflicts"])) Assert.Contains(conflict, exceptionKeys);
        }

        var carrier = RecordByName(mapping, "feature", "Multi-carrier rate shopping")["dates"]!;
        var promised = carrier["salesCommitment"]!["value"]!.GetValue<string>();
        var planned = carrier["proposedPlanningDate"]!["value"]!.GetValue<string>();
        Assert.NotEqual(promised, planned);
        Assert.NotEmpty(carrier["proposedPlanningDate"]!["conflicts"]!.AsArray());
        Assert.Contains(Nodes(mapping["exceptions"]), e => e["kind"]!.GetValue<string>() == "dateConflict"
            && e["disposition"]!.GetValue<string>() == "flagged");

        var last = seed.Roadmap!.Items.Single(i => i.ProjectName == "Carrier Connect rate-shopping UI");
        Assert.Equal(planned, last.End);
        Assert.DoesNotContain(seed.Roadmap.Items, i => i.End == promised);
    }

    /// <summary>Acceptance criterion 5: stale but explicitly in the approved roadmap is flagged, not discarded.</summary>
    [Fact]
    public void A_stale_item_in_the_approved_roadmap_is_selected_and_flagged_not_discarded()
    {
        var mapping = Mapping();
        var seed = MergedPortfolioSeed();
        var staleAfter = mapping["conventions"]!["staleAfterDays"]!.GetValue<int>();
        var extracted = DateOnly.FromDateTime(DateTime.Parse(mapping["portfolio"]!["extractedAt"]!.GetValue<string>(),
            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));

        var aha = Nodes(PortfolioFixture("aha/features.json")["features"]).Single(f => f["reference_num"]!.GetValue<string>() == "HWMS-4");
        var updated = DateOnly.ParseExact(aha["updated_at"]!.GetValue<string>()[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.True(extracted.DayNumber - updated.DayNumber > staleAfter, "HWMS-4 must be stale by the mapping's own threshold.");
        Assert.Contains(aha["release"]!["reference_num"]!.GetValue<string>(), Strings(mapping["portfolio"]!["currentReleases"]));

        var feature = MappingRecords(mapping, "feature").Single(r => SourceIds(r["sources"]).Contains("HWMS-4"));
        Assert.Contains("stale", Strings(feature["flags"]));
        Assert.Contains(seed.Features, f => f.Name == feature["name"]!.GetValue<string>());
        Assert.Contains(Nodes(mapping["exceptions"]), e => e["kind"]!.GetValue<string>() == "staleInApprovedRoadmap"
            && Strings(e["recordKeys"]).Contains(feature["key"]!.GetValue<string>()));
        Assert.Contains(seed.Roadmap!.Items, i => i.ProjectName == "Returns portal");

        // Recency informs review priority, never approval: a fresh tentative idea is held, not selected.
        var tentative = Nodes(mapping["exceptions"]).Single(e => e["kind"]!.GetValue<string>() == "tentative");
        Assert.Equal("held", tentative["disposition"]!.GetValue<string>());
        Assert.DoesNotContain(Nodes(mapping["records"]), r => SourceIds(r["sources"]).Contains("HWMS-5"));
    }

    /// <summary>Acceptance criterion 6 and requirement 7: every dependency is mapped or explicitly unresolved.</summary>
    [Fact]
    public void Every_dependency_is_resolved_to_a_selected_project_or_listed_as_external_or_unresolved()
    {
        var mapping = Mapping();
        var fixtureStrings = FixtureStrings();
        var projectKeys = MappingRecords(mapping, "project").Select(r => r["key"]!.GetValue<string>()).ToHashSet();
        var exceptionKeys = Nodes(mapping["exceptions"]).Select(e => e["key"]!.GetValue<string>()).ToHashSet();
        var dependencies = Nodes(mapping["dependencies"]).ToList();
        var statuses = dependencies.Select(d => d["status"]!.GetValue<string>()).ToHashSet();
        Assert.Superset(new HashSet<string> { "resolved", "external", "unresolved" }, statuses);

        foreach (var dependency in dependencies)
        {
            var key = dependency["key"]!.GetValue<string>();
            switch (dependency["status"]!.GetValue<string>())
            {
                case "resolved":
                    Assert.Contains(dependency["from"]!.GetValue<string>(), projectKeys);
                    Assert.Contains(dependency["to"]!.GetValue<string>(), projectKeys);
                    Assert.NotEqual(dependency["from"]!.GetValue<string>(), dependency["to"]!.GetValue<string>());
                    break;
                case "external":
                    Assert.Contains(dependency["from"]!.GetValue<string>(), projectKeys);
                    Assert.Null(dependency["to"]);
                    Assert.Contains(dependency["external"]!["id"]!.GetValue<string>(), fixtureStrings);
                    Assert.True(dependency["external"]!.AsObject().ContainsKey("owner"), $"{key}: an external prerequisite names its owner or null.");
                    Assert.Null(dependency["startDate"]); // no invented dates
                    break;
                default:
                    Assert.Contains(dependency["exception"]!.GetValue<string>(), exceptionKeys);
                    break;
            }

            Assert.False(string.IsNullOrWhiteSpace(dependency["reason"]!.GetValue<string>()), $"{key}: reason.");
        }

        Assert.Contains(dependencies, d => d["status"]!.GetValue<string>() == "external" && d["external"]!["owner"] is null);
        Assert.Contains(dependencies, d => d["kind"]?.GetValue<string>() == "cycle");

        // Resolved edges appear as project dependencies in the fragment, nowhere else.
        var seed = MergedPortfolioSeed();
        var resolved = dependencies.Where(d => d["status"]!.GetValue<string>() == "resolved")
            .Select(d => (RecordByKey(mapping, d["from"]!.GetValue<string>())["name"]!.GetValue<string>(),
                          RecordByKey(mapping, d["to"]!.GetValue<string>())["name"]!.GetValue<string>())).Order().ToList();
        var seeded = seed.Projects.SelectMany(p => p.DependsOnProjectNames.Select(d => (p.Name, d))).Order().ToList();
        Assert.Equal(resolved, seeded);
    }

    /// <summary>Requirement 6: units are explicit, gaps stay gaps, nothing is converted without a convention.</summary>
    [Fact]
    public void Effort_and_capacity_units_are_explicit_and_gaps_are_never_filled_or_converted()
    {
        var mapping = Mapping();
        var seed = MergedPortfolioSeed();
        var conventions = mapping["conventions"]!;
        var effortUnit = conventions["effortUnit"]!.GetValue<string>();
        var capacityUnit = conventions["capacityUnit"]!.GetValue<string>();
        Assert.Equal(effortUnit + " per month", capacityUnit);
        Assert.Contains("no reviewed convention", conventions["unitConversion"]!.GetValue<string>());

        foreach (var team in MappingRecords(mapping, "team"))
        {
            var capacity = team["capacity"]!;
            Assert.Equal(capacityUnit, capacity["unit"]!.GetValue<string>());
            var rows = seed.Teams.Single(t => t.Name == team["name"]!.GetValue<string>()).Capacities;
            Assert.Equal(Strings(capacity["months"]), rows.Select(r => $"{r.Year:0000}-{r.Month:00}"));
            Assert.All(rows, r => Assert.Equal(capacity["perMonth"]!.GetValue<decimal>(), r.Points));
        }

        var statuses = new List<string>();
        foreach (var project in MappingRecords(mapping, "project"))
        {
            var effort = project["effort"]!;
            var status = effort["status"]!.GetValue<string>();
            statuses.Add(status);
            var demands = seed.Projects.Single(p => p.Name == project["name"]!.GetValue<string>()).EffortDemands;
            if (status == "estimated")
            {
                Assert.Equal(effortUnit, effort["unit"]!.GetValue<string>());
                Assert.Equal(effort["total"]!.GetValue<decimal>(), demands.Sum(d => d.Points));
                var team = RecordByKey(mapping, effort["team"]!.GetValue<string>())["name"]!.GetValue<string>();
                Assert.All(demands, d => Assert.Equal(team, d.TeamName));
            }
            else
            {
                Assert.Contains(status, new[] { "gap", "complete" });
                Assert.Empty(demands);
                Assert.False(string.IsNullOrWhiteSpace(effort["reason"]!.GetValue<string>()));
                Assert.Null(effort["total"]);
            }
        }

        Assert.Contains("estimated", statuses);
        var incompatible = MappingRecords(mapping, "project").Select(p => p["effort"]!)
            .Single(e => e["reportedAs"] is not null);
        Assert.NotEqual(effortUnit, incompatible["reportedAs"]!["unit"]!.GetValue<string>());
        Assert.Equal("gap", incompatible["status"]!.GetValue<string>());
        Assert.Contains(MappingRecords(mapping, "project"), p => p["effort"]!["status"]!.GetValue<string>() == "gap"
            && p["effort"]!["reportedAs"] is null); // a missing estimate
    }

    [Fact]
    public void The_guide_places_portfolio_selection_between_the_interview_and_loading_and_the_brief_carries_the_mapping_slot()
    {
        var guide = ReadRepoFile(GuidePath);
        var interview = guide.IndexOf("## 7. Interviewing the human", StringComparison.Ordinal);
        var portfolio = guide.IndexOf("## 8. Portfolio selection", StringComparison.Ordinal);
        var loading = guide.IndexOf("## 11. Assembling and loading", StringComparison.Ordinal);
        Assert.True(interview >= 0 && portfolio > interview && loading > portfolio,
            "Portfolio selection must be section 8, after the interview and before assembling and loading (section 11).");

        var linked = RelativeLinkTargets(guide, GuidePath).Select(t => t.ResolvedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(RepoPath(PortfolioReadmePath)), linked);
        Assert.Contains(Path.GetFullPath(RepoPath(PortfolioMappingPath)), linked);

        Assert.Contains("Portfolio mapping:", guide);
        Assert.Contains("never coin", guide);
        Assert.DoesNotContain("<!-- PRD5", guide); // the hook is now guide section 10 (PRD5 tests below)

        var readme = ReadRepoFile(PortfolioReadmePath);
        foreach (var meaning in DateMeanings) Assert.Contains(meaning, readme);
    }

    /// <summary>PRD4 requirement 4: a name match only proposes; discovery must not call it ready.</summary>
    [Fact]
    public void The_manifest_does_not_classify_a_name_match_as_ready()
    {
        var manifest = ReadJson(ManifestPath);
        var dataset03 = manifest["datasetReadiness"]!.AsArray().Single(r => r!["datasetId"]!.GetValue<string>() == "03")!;
        Assert.Equal("requires a mapping decision", dataset03["classification"]!.GetValue<string>());
        Assert.Contains("proposal", dataset03["proposedAssumptions"]!.ToJsonString());
        Assert.Contains(manifest["openQuestions"]!.AsArray(),
            q => q!["question"]!.GetValue<string>().Contains("HCORE-7", StringComparison.Ordinal));
    }

    // ---------- PRD4: append new tests above this line ----------

    // ---------- PRD5: evidence and consequential-decision review ----------
    // docs/onboarding/review/: the decision register template and example, the review walkthrough and
    // the seed fragments generated from the register's accepted decisions only.

    private static readonly string[] ReviewStates = ["proposed", "accepted", "rejected", "deferred"];

    private static readonly string[] DecisionClasses = ["sourceFact", "inferredInterpretation", "default", "assumption"];

    private static readonly string[] DecisionTopics =
    [
        "segmentDefinition", "identityMerge", "capabilityScope", "coverageTier", "coverageDelta", "need",
        "planningDate", "commercialClassification", "simulationAssumption"
    ];

    private static readonly string[] OutsideEvidenceQuantities =
    [
        "marketPopulation", "competitorInstalledBase", "channelMarketReach", "operatingCosts",
        "priceSensitivity", "switchingCosts", "covariateElasticities", "eventEffects"
    ];

    /// <summary>Every decision carries all of these keys; optional facts are explicit nulls.</summary>
    private static readonly string[] RequiredDecisionKeys =
    [
        "key", "entryType", "topic", "class", "tier", "datasets", "requiredForTier", "affects", "seedTarget",
        "proposedValue", "evidence", "sourceDate", "effectiveDate", "independentObservations",
        "extractionConfidence", "modelUncertainty", "alternatives", "rationale", "missingEvidence",
        "outsideEvidenceQuantity", "domainMapping", "question", "reviewState", "exclusionReason", "appliedRule",
        "accepted"
    ];

    private static readonly string[] ReviewSeedFiles =
        ["03-feature-deltas.json", "04-roadmap.json", "05-coverage-and-needs.json", "11-firms.json"];

    /// <summary>The domain vocabulary a register label may map onto: the CLI's mirrors of the Domain enums.</summary>
    private static readonly Dictionary<string, Type> DomainEnums = new()
    {
        ["ParameterProvenance"] = typeof(ParameterProvenance),
        ["EpistemicStatus"] = typeof(EpistemicStatus),
        ["CoverageTier"] = typeof(CoverageTier),
        ["FeatureStatus"] = typeof(FeatureStatus),
        ["ProjectStatus"] = typeof(ProjectStatus)
    };

    private static JsonNode Register() => ReadJson(RegisterExamplePath);

    private static List<JsonNode> Decisions(JsonNode register) => Nodes(register["decisions"]).ToList();

    private static JsonNode Decision(JsonNode register, string key) =>
        Decisions(register).Single(d => d["key"]!.GetValue<string>() == key);

    private static string Text(JsonNode? node) => node?.GetValue<string>() ?? "";

    /// <summary>
    /// Canonical form of a JSON value for the material-fields comparison: object keys sorted, strings
    /// trimmed with whitespace runs collapsed, arrays compared as sets. Formatting, key order and
    /// array order therefore never change it; a different value, scope or evidence always does.
    /// </summary>
    private static string Canon(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}:{Canon(p.Value)}")) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canon).Order(StringComparer.Ordinal)) + "]",
        JsonValue v when v.TryGetValue<string>(out var s) => "\"" + Regex.Replace(s.Trim(), @"\s+", " ") + "\"",
        JsonValue v when v.TryGetValue<double>(out var n) => n.ToString("R", CultureInfo.InvariantCulture),
        _ => node.ToJsonString()
    };

    /// <summary>The material fields of the current proposal: selected value, scope (affects) and evidence references.</summary>
    private static string MaterialOf(JsonNode decision) => Canon(new JsonObject
    {
        ["value"] = decision["proposedValue"]!.DeepClone(),
        ["affects"] = decision["affects"]!.DeepClone(),
        ["evidence"] = new JsonArray(Nodes(decision["evidence"]).Select(e => (JsonNode)JsonValue.Create(Text(e["ref"]))!).ToArray())
    });

    /// <summary>
    /// Acceptance is valid only while the accepted snapshot equals the current material fields. A
    /// decision whose snapshot no longer matches is reopened: treated like a proposal, excluded until re-accepted.
    /// </summary>
    private static string EffectiveState(JsonNode decision)
    {
        var state = Text(decision["reviewState"]);
        if (state != "accepted") return state;
        var snapshot = decision["accepted"];
        return snapshot is not null && Canon(snapshot["material"]) == MaterialOf(decision) ? "accepted" : "reopened";
    }

    private static HashSet<string> KnownSourceIds()
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in Sources(ReadJson(ManifestPath)))
            foreach (var obj in Nodes(source!["objects"]))
                foreach (var id in Strings(obj["sourceIds"])) known.Add(id);

        foreach (var dir in new[] { DiscoveryDirectory, PortfolioDirectory })
            foreach (var file in Directory.GetFiles(RepoPath($"{dir}/fixture"), "*.json", SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}seed{Path.DirectorySeparatorChar}")))
                CollectStrings(JsonNode.Parse(File.ReadAllText(file)), known);
        return known;
    }

    private static void CollectStrings(JsonNode? node, HashSet<string> into)
    {
        switch (node)
        {
            case JsonObject obj: foreach (var (_, child) in obj) CollectStrings(child, into); break;
            case JsonArray array: foreach (var child in array) CollectStrings(child, into); break;
            case JsonValue value when value.TryGetValue<string>(out var text): into.Add(text); break;
        }
    }

    /// <summary>Resolves "kind:id" against the discovery manifest, the portfolio mapping, the fixtures and the register itself.</summary>
    private static bool RefResolves(string reference, JsonNode register, JsonNode manifest, JsonNode mapping, HashSet<string> sourceIds)
    {
        var colon = reference.IndexOf(':');
        if (colon <= 0) return false;
        var (kind, id) = (reference[..colon], reference[(colon + 1)..]);
        return kind switch
        {
            "salesforce" or "aha" or "jira" => sourceIds.Contains(id),
            "mapping" => new[] { "records", "exceptions", "dependencies", "exclusions" }
                .Any(list => Nodes(mapping[list]).Any(n => Text(n["key"]) == id)),
            "manifest" when id.StartsWith("dataset:") => Nodes(manifest["datasetReadiness"]).Any(r => Text(r["datasetId"]) == id[8..]),
            "manifest" when id.StartsWith("gap:") => Nodes(manifest["gaps"]).Any(g => Text(g["concept"]).StartsWith(id[4..], StringComparison.Ordinal)),
            "manifest" => Nodes(manifest["openQuestions"]).Any(q => Text(q["id"]) == id),
            "human" => Nodes(register["humanEvidence"]).Any(h => Text(h["id"]) == id),
            "register" => Nodes(register["decisions"]).Any(d => Text(d["key"]) == id),
            "seed" => id.Length > 0,
            _ => false
        };
    }

    private static JsonNode ReviewFragment(string fileName) =>
        JsonNode.Parse(ReadRepoFile($"{ReviewSeedDirectory}/{fileName}"))!;

    /// <summary>The fragment rows a decision's <c>seedTarget</c> points at (collection path plus match keys).</summary>
    private static List<JsonNode> SeedRows(JsonNode target)
    {
        JsonNode? node = ReviewFragment(Text(target["fragment"]));
        foreach (var part in Text(target["collection"]).Split('.')) node = node?[part];
        return node is JsonArray rows
            ? rows.Select(r => r!).Where(r => target["match"]!.AsObject().All(m => Text(r[m.Key]) == Text(m.Value))).ToList()
            : [];
    }

    private static bool RowHoldsValue(JsonNode row, JsonNode value) =>
        value.AsObject().All(p => row[p.Key] is not null && JsonNode.DeepEquals(row[p.Key], p.Value));

    [Fact]
    public void The_register_template_and_example_parse_and_every_decision_has_the_required_fields_and_an_allowed_state()
    {
        foreach (var path in new[] { RegisterTemplatePath, RegisterExamplePath })
        {
            var register = ReadJson(path);
            Assert.Equal(1, register["registerVersion"]!.GetValue<int>());
            Assert.NotEmpty(register["decisions"]!.AsArray());
            foreach (var decision in Decisions(register))
            {
                foreach (var key in RequiredDecisionKeys)
                    Assert.True(decision.AsObject().ContainsKey(key), $"{path}: decision '{Text(decision["key"])}' lacks '{key}'.");
                Assert.NotEmpty(Text(decision["key"]));
            }
        }

        var example = Register();
        var keys = Decisions(example).Select(d => Text(d["key"])).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var d in Decisions(example))
        {
            var key = Text(d["key"]);
            Assert.Contains(Text(d["reviewState"]), ReviewStates);
            Assert.Contains(Text(d["class"]), DecisionClasses);
            Assert.Contains(Text(d["topic"]), DecisionTopics);
            Assert.Contains(Text(d["entryType"]), new[] { "decision", "rule" });
            Assert.Contains(Text(d["tier"]), new[] { "A", "B", "C", "D" });
            Assert.NotEmpty(d["datasets"]!.AsArray());
            Assert.NotEmpty(d["affects"]!.AsArray());
            Assert.False(string.IsNullOrWhiteSpace(Text(d["rationale"])), $"{key}: rationale.");
            Assert.False(string.IsNullOrWhiteSpace(Text(d["missingEvidence"])), $"{key}: missingEvidence (write 'none' if none).");
            Assert.True(d["alternatives"] is JsonArray, $"{key}: alternatives.");
        }

        // Every state the example exercises: accepted and in force, reopened, proposed, deferred.
        var effective = Decisions(example).Select(EffectiveState).Distinct().ToList();
        foreach (var state in new[] { "accepted", "reopened", "proposed", "deferred" })
            Assert.Contains(state, effective);
    }

    [Fact]
    public void Every_label_the_register_uses_maps_to_a_real_domain_enum_member()
    {
        var register = Register();
        var labelMap = Nodes(register["labelMap"]).ToList();
        Assert.NotEmpty(labelMap);

        foreach (var row in labelMap)
        {
            Assert.Contains(Text(row["label"]), DecisionClasses.Append("unknown"));
            var enumName = Text(row["enum"]);
            if (enumName.Length == 0) continue; // "unknown" lands in isUnknown, which is a flag, not an enum member
            Assert.True(DomainEnums.TryGetValue(enumName, out var type), $"labelMap: '{enumName}' is not a mirrored domain enum.");
            Assert.True(Enum.IsDefined(type!, Text(row["member"])), $"labelMap: {enumName}.{Text(row["member"])} does not exist.");
        }

        // Every descriptive class the register uses is mapped somewhere, and a decision's own domainMapping agrees.
        var mappedLabels = labelMap.Select(r => Text(r["label"])).ToHashSet();
        foreach (var d in Decisions(register))
        {
            Assert.Contains(Text(d["class"]), mappedLabels);
            if (d["domainMapping"] is not { } mapping) continue;
            Assert.Contains(labelMap, r => Text(r["label"]) == Text(d["class"])
                && Text(r["enum"]) == Text(mapping["enum"]) && Text(r["member"]) == Text(mapping["member"]));
        }

        // Coverage tiers proposed in seed-shaped values use the exact CoverageTier spellings.
        foreach (var d in Decisions(register))
        {
            var value = d["proposedValue"]!;
            if (value["tier"] is { } tier) Assert.True(Enum.IsDefined(typeof(CoverageTier), Text(tier)), $"{Text(d["key"])}: tier '{Text(tier)}'.");
            foreach (var delta in value["deltas"] is JsonArray deltas ? deltas.Select(x => x!) : [])
            {
                Assert.True(Enum.IsDefined(typeof(CoverageTier), Text(delta["fromTier"])), $"{Text(d["key"])}: fromTier.");
                Assert.True(Enum.IsDefined(typeof(CoverageTier), Text(delta["toTier"])), $"{Text(d["key"])}: toTier.");
            }
        }
    }

    [Fact]
    public void Every_evidence_reference_in_the_register_resolves_to_a_real_key_or_id()
    {
        var register = Register();
        var manifest = ReadJson(ManifestPath);
        var mapping = Mapping();
        var sourceIds = KnownSourceIds();

        Assert.Equal(Text(manifest["discovery"]!["runId"]), Text(register["manifest"]!["runId"]));
        Assert.Equal(Text(manifest["discovery"]!["runId"]), Text(mapping["manifest"]!["runId"]));
        foreach (var human in Nodes(register["humanEvidence"]))
        {
            Assert.NotEmpty(Text(human["statement"]));
            AssertIsoDate(Text(human["date"]), RegisterExamplePath, $"humanEvidence {Text(human["id"])}", required: true);
        }

        foreach (var d in Decisions(register))
        {
            var key = Text(d["key"]);
            foreach (var evidence in Nodes(d["evidence"]))
                Assert.True(RefResolves(Text(evidence["ref"]), register, manifest, mapping, sourceIds),
                    $"{key}: evidence '{Text(evidence["ref"])}' does not resolve.");
            foreach (var affected in Nodes(d["affects"]))
            {
                Assert.True(RefResolves(Text(affected["ref"]), register, manifest, mapping, sourceIds),
                    $"{key}: affects '{Text(affected["ref"])}' does not resolve.");
                Assert.NotEmpty(affected["fields"]!.AsArray());
            }

            if (Text(d["appliedRule"]).Length > 0) Assert.Contains(Text(d["appliedRule"]), Decisions(register).Select(x => Text(x["key"])));
            AssertIsoDate(d["sourceDate"] is null ? null : Text(d["sourceDate"]), RegisterExamplePath, $"{key} sourceDate");
            AssertIsoDate(d["effectiveDate"] is null ? null : Text(d["effectiveDate"]), RegisterExamplePath, $"{key} effectiveDate");
        }
    }

    /// <summary>Acceptance criterion 2 and requirement 3: only decisions in force reach the fragments; the rest are excluded.</summary>
    [Fact]
    public void A_decision_that_is_not_in_force_leaves_no_trace_of_its_proposed_value_in_any_seed_fragment()
    {
        var register = Register();
        var typed = new List<SeedFile>();
        foreach (var fileName in ReviewSeedFiles)
        {
            var seed = ParseSeedFile(ReadRepoFile($"{ReviewSeedDirectory}/{fileName}"), fileName);
            Assert.True(HasAnyTopLevelKey(seed), $"{fileName} is empty.");
            AssertEnumsAreExactMemberNames(seed, fileName);
            AssertDatesAreIsoAndRecordedDatesArePast(seed, fileName);
            typed.Add(seed);
        }

        foreach (var d in Decisions(register).Where(d => d["seedTarget"] is not null))
        {
            var key = Text(d["key"]);
            var holding = SeedRows(d["seedTarget"]!).Where(row => RowHoldsValue(row, d["proposedValue"]!)).ToList();
            if (EffectiveState(d) == "accepted")
                Assert.True(holding.Count == 1, $"{key} is accepted and in force, so its value must appear exactly once in the fragments.");
            else
                Assert.True(holding.Count == 0, $"{key} is {EffectiveState(d)}; its proposed value leaked into the fragments.");
        }

        // The concrete case: the Bronze-to-Gold delta for rate shopping stays proposed and the feature loads without it.
        var delta = Decision(register, "DEC-delta-rate-shopping-shipping");
        Assert.Equal("proposed", EffectiveState(delta));
        var features = typed.SelectMany(s => s.Features).ToList();
        Assert.Single(features, f => f.Name == "Multi-carrier rate shopping");
        Assert.DoesNotContain(features.SelectMany(f => f.Deltas), x => x.FromTier == "Bronze" && x.ToTier == "Gold");
        Assert.Empty(features.Single(f => f.Name == "Multi-carrier rate shopping").Deltas);

        // Nothing outside the register's scope names an item the human did not accept.
        var mapping = Mapping();
        var approvedProjects = MappingRecords(mapping, "project").Select(r => Text(r["name"])).ToHashSet();
        foreach (var item in typed.Select(s => s.Roadmap).Where(r => r is not null).SelectMany(r => r!.Items))
            Assert.Contains(item.ProjectName, approvedProjects);
        Assert.DoesNotContain(typed.Select(s => s.Roadmap).Where(r => r is not null).SelectMany(r => r!.Items),
            i => i.ProjectName == "Wave picking engine");
    }

    /// <summary>Acceptance criterion 3: unknown competitor cells stay unknown; extraction confidence does not make them Gold.</summary>
    [Fact]
    public void Unknown_competitor_cells_stay_unknown_and_extraction_confidence_never_turns_them_into_a_tier()
    {
        var register = Register();
        var coverage = ReviewFragment("05-coverage-and-needs.json")["market"]!["coverages"]!.AsArray().Select(c => c!).ToList();
        var competitors = coverage.Where(c => !Text(c["productName"]).StartsWith("Harborline", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(competitors);

        foreach (var cell in competitors)
        {
            Assert.True(cell["isUnknown"]!.GetValue<bool>(), $"{Text(cell["productName"])} / {Text(cell["businessProcessName"])} must stay unknown.");
            Assert.Null(cell["tier"]);       // an unknown cell carries no tier ...
            Assert.Null(cell["confidence"]); // ... and no confidence: the API ignores both, and a copied score must not sneak in.
        }

        var stackline = Decision(register, "DEC-cov-stackline-cycle-counting");
        Assert.True(stackline["extractionConfidence"]!.GetValue<double>() > 0.5);       // recorded, and high ...
        Assert.True(stackline["proposedValue"]!["isUnknown"]!.GetValue<bool>());        // ... yet the outcome is unknown.
        Assert.Null(stackline["proposedValue"]!["tier"]);

        var palletto = Decision(register, "DEC-cov-palletto-shipping-ecommerce");
        Assert.Equal("Gold", Text(palletto["proposedValue"]!["tier"]));
        Assert.Equal("proposed", EffectiveState(palletto));
        Assert.Contains(competitors, c => Text(c["productName"]) == "Palletto Cloud" && c["isUnknown"]!.GetValue<bool>());
    }

    /// <summary>Acceptance criterion 5 and requirement 7: a material change reopens that decision only; formatting does not.</summary>
    [Fact]
    public void Changing_a_material_accepted_date_reopens_that_decision_only_and_formatting_changes_do_not()
    {
        var register = Register();
        var mapping = Mapping();

        var reopened = Decisions(register)
            .Where(d => Text(d["reviewState"]) == "accepted" && EffectiveState(d) == "reopened")
            .Select(d => Text(d["key"])).ToList();
        Assert.Equal(new[] { "DEC-date-rate-shopping-planning" }, reopened);
        Assert.All(Decisions(register).Where(d => Text(d["reviewState"]) == "accepted" && Text(d["key"]) != reopened[0]),
            d => Assert.Equal("accepted", EffectiveState(d)));

        // The current proposal follows the current mapping; the accepted snapshot holds the older date.
        var moved = Decision(register, reopened[0]);
        var mappedEnd = Text(RecordByName(mapping, "project", "Carrier Connect rate-shopping UI")["roadmapItem"]!["end"]);
        Assert.Equal(mappedEnd, Text(moved["proposedValue"]!["end"]));
        Assert.NotEqual(mappedEnd, Text(moved["accepted"]!["material"]!["value"]!["end"]));

        // Sibling decisions on the same feature keep their acceptance (the sales promise did not change).
        var promise = Decision(register, "DEC-date-sales-commitment-rate-shopping");
        Assert.Equal("accepted", EffectiveState(promise));
        Assert.Equal(Text(RecordByName(mapping, "feature", "Multi-carrier rate shopping")["dates"]!["salesCommitment"]!["value"]),
            Text(promise["proposedValue"]!["salesCommitment"]));

        // Formatting-only changes: whitespace, key and array order, prose. Still valid.
        var settled = Decision(register, "DEC-date-cycle-counting-plan");
        Assert.Equal("accepted", EffectiveState(settled));
        var reformatted = JsonNode.Parse(settled.ToJsonString(new JsonSerializerOptions { WriteIndented = true }))!;
        reformatted["proposedValue"]!["end"] = " 2026-11-30  ";
        reformatted["rationale"] = "Reworded   rationale.";
        reformatted["evidence"] = new JsonArray(Nodes(settled["evidence"]).Reverse().Select(e => e.DeepClone()).ToArray());
        reformatted["affects"] = new JsonArray(Nodes(settled["affects"]).Reverse().Select(a => a.DeepClone()).ToArray());
        Assert.Equal("accepted", EffectiveState(reformatted));

        // Material changes: value, scope, evidence. Each one reopens.
        var changedValue = settled.DeepClone();
        changedValue["proposedValue"]!["end"] = "2026-12-15";
        Assert.Equal("reopened", EffectiveState(changedValue));

        var changedScope = settled.DeepClone();
        changedScope["affects"]!.AsArray().Add(JsonNode.Parse("""{ "ref": "mapping:P-returns-portal", "fields": ["roadmapItem.end"] }"""));
        Assert.Equal("reopened", EffectiveState(changedScope));

        var changedEvidence = settled.DeepClone();
        changedEvidence["evidence"]!.AsArray().Add(JsonNode.Parse("""{ "ref": "jira:HWEB-3", "note": "added" }"""));
        Assert.Equal("reopened", EffectiveState(changedEvidence));
    }

    /// <summary>Acceptance criterion 1 and requirement 2: a clean mapping under an accepted rule is loaded without a repeated question.</summary>
    [Fact]
    public void Records_covered_by_an_accepted_rule_carry_no_open_question_and_are_loaded()
    {
        var register = Register();
        var decisions = Decisions(register);
        var rules = decisions.Where(d => Text(d["entryType"]) == "rule").ToList();
        Assert.NotEmpty(rules);
        foreach (var rule in rules)
        {
            Assert.Equal("accepted", EffectiveState(rule));
            Assert.NotEmpty(rule["covers"]!.AsArray());
            foreach (var covered in Strings(rule["covers"]))
            {
                var touching = decisions.Where(d => Text(d["key"]) != Text(rule["key"])
                    && Nodes(d["affects"]).Any(a => Text(a["ref"]) == covered)).ToList();
                Assert.All(touching, d =>
                {
                    Assert.Equal(Text(rule["key"]), Text(d["appliedRule"]));
                    Assert.Equal("accepted", EffectiveState(d));
                    Assert.Null(d["question"]);
                });
            }
        }

        foreach (var applied in decisions.Where(d => Text(d["appliedRule"]).Length > 0))
        {
            Assert.Null(applied["question"]);
            Assert.StartsWith("rule:", Text(applied["accepted"]!["by"]));
        }

        // The account-to-segment rule: every covered account has a populated Segment__c that the value map knows,
        // and the fragment holds exactly one firm per covered account, in the mapped segment.
        var segmentRule = Decision(register, "RULE-segment-from-segment-c");
        var valueMap = segmentRule["proposedValue"]!["valueMap"]!.AsObject();
        var accounts = Nodes(ReadJson($"{DiscoveryDirectory}/fixture/salesforce/accounts-sample.json")["response"]!["records"]).ToList();
        var firms = ReviewFragment("11-firms.json")["marketDynamics"]!["firms"]!.AsArray().Select(f => f!).ToList();
        foreach (var covered in Strings(segmentRule["covers"]))
        {
            var id = covered["salesforce:".Length..];
            var account = accounts.Single(a => Text(a["Id"]) == id);
            Assert.True(valueMap.ContainsKey(Text(account["Segment__c"])), $"{id}: Segment__c is not in the rule's value map.");
            var firm = firms.Single(f => f["attributesJson"]!.GetValue<string>().Contains(id, StringComparison.Ordinal));
            Assert.Equal(Text(valueMap[Text(account["Segment__c"])]), Text(firm["segmentName"]));
        }

        // The account the rule does not cover (null Segment__c) is an open decision and is not loaded.
        var meridian = accounts.Single(a => Text(a["Name"]) == "Meridian Fulfillment");
        Assert.Null(meridian["Segment__c"]);
        Assert.DoesNotContain(Strings(segmentRule["covers"]), c => c == $"salesforce:{Text(meridian["Id"])}");
        Assert.Equal("proposed", EffectiveState(Decision(register, "DEC-account-meridian-segment")));
        Assert.DoesNotContain(firms, f => f["attributesJson"]!.GetValue<string>().Contains(Text(meridian["Id"]), StringComparison.Ordinal));
    }

    /// <summary>Acceptance criterion 6 and requirement 3: Tier A proceeds, unsupported simulation assumptions are deferred, all traceable.</summary>
    [Fact]
    public void Tier_A_proceeds_while_unsupported_simulation_assumptions_are_deferred_and_every_accepted_choice_is_traceable()
    {
        var register = Register();
        var decisions = Decisions(register);

        // A tier is deferred when a decision it requires is not in force; otherwise it proceeds (with exclusions).
        var outcome = register["tierOutcome"]!.AsObject().ToDictionary(p => p.Key, p => Text(p.Value));
        foreach (var tier in new[] { "A", "B", "C", "D" })
        {
            var blocked = decisions.Any(d => Text(d["tier"]) == tier && d["requiredForTier"]!.GetValue<bool>() && EffectiveState(d) != "accepted");
            Assert.Equal(blocked ? "deferred" : "proceed", outcome[tier]);
        }

        Assert.Equal("proceed", outcome["A"]);
        Assert.Equal("deferred", outcome["C"]);
        var deferred = decisions.Where(d => Text(d["reviewState"]) == "deferred").ToList();
        Assert.NotEmpty(deferred);
        Assert.All(deferred, d =>
        {
            Assert.Equal("simulationAssumption", Text(d["topic"]));
            Assert.Contains(Text(d["tier"]), new[] { "C", "D" });
        });

        // Tier A fragments still exist and hold the accepted roadmap items.
        var roadmap = ParseSeedFile(ReadRepoFile($"{ReviewSeedDirectory}/04-roadmap.json"), "04-roadmap.json").Roadmap!;
        Assert.NotEmpty(roadmap.Items);
        Assert.DoesNotContain(roadmap.Items, i => i.ProjectName == "Carrier Connect rate-shopping UI"); // reopened date

        foreach (var d in decisions)
        {
            var key = Text(d["key"]);
            var state = EffectiveState(d);
            if (state == "accepted")
            {
                // Traceable: evidence, or an explicit assumption with its basis in the rationale.
                Assert.True(d["evidence"]!.AsArray().Count > 0 || Text(d["class"]) == "assumption", $"{key}: accepted without evidence or an explicit assumption.");
                Assert.Null(d["exclusionReason"]);
                Assert.NotNull(d["accepted"]);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(Text(d["exclusionReason"])), $"{key}: {state} but no exclusion reason.");
                Assert.False(string.IsNullOrWhiteSpace(Text(d["question"])), $"{key}: {state} but no question for the reviewer.");
            }
        }
    }

    /// <summary>Requirement 5: extraction confidence, model uncertainty and independence are separate facts.</summary>
    [Fact]
    public void Extraction_confidence_model_uncertainty_and_independence_are_recorded_separately()
    {
        var register = Register();
        foreach (var d in Decisions(register))
        {
            var key = Text(d["key"]);
            if (d["extractionConfidence"] is { } confidence)
                Assert.InRange(confidence.GetValue<double>(), 0.0, 1.0);
            if (d["modelUncertainty"] is { } uncertainty)
            {
                Assert.Contains(Text(uncertainty["level"]), new[] { "low", "medium", "high", "unquantified" });
                Assert.NotEmpty(Text(uncertainty["statement"]));
                // No distribution width is derived from an agent's confidence.
                Assert.Null(uncertainty["width"]);
            }

            if (d["independentObservations"] is { } independent)
                Assert.InRange(independent.GetValue<int>(), 1, Math.Max(1, d["evidence"]!.AsArray().Count));
        }

        // Repeated sales notes: three references, one independent observation.
        var stackline = Decision(register, "DEC-cov-stackline-cycle-counting");
        Assert.True(stackline["evidence"]!.AsArray().Count >= 3);
        Assert.Equal(1, stackline["independentObservations"]!.GetValue<int>());

        // A customer request evidences a need, not a coverage tier or a win-probability effect.
        var need = Decision(register, "DEC-need-ecommerce-shipping");
        Assert.Equal("need", Text(need["topic"]));
        Assert.Contains(Nodes(need["evidence"]), e => Text(e["ref"]) == "salesforce:006EX000000A002AAA");
        Assert.DoesNotContain(Decisions(register), d => Text(d["topic"]) == "coverageTier" && Nodes(d["evidence"]).Any(e => Text(e["ref"]) == "salesforce:006EX000000A002AAA")
            && EffectiveState(d) == "accepted");
        Assert.DoesNotContain(Decisions(register), d => Text(d["proposedValue"]!["flowKind"]) == "WinRate");
    }

    /// <summary>Requirement 6: quantities with no source in the three systems are reviewed assumptions, never observed or calibrated.</summary>
    [Fact]
    public void Quantities_needing_outside_evidence_are_reviewed_assumptions_and_never_relabelled_observed_or_calibrated()
    {
        var register = Register();
        var outside = Decisions(register).Where(d => d["outsideEvidenceQuantity"] is not null).ToList();
        Assert.Contains(outside, d => Text(d["outsideEvidenceQuantity"]) == "marketPopulation");
        Assert.Contains(outside, d => Text(d["outsideEvidenceQuantity"]) == "priceSensitivity");
        foreach (var d in outside)
        {
            var key = Text(d["key"]);
            Assert.Contains(Text(d["outsideEvidenceQuantity"]), OutsideEvidenceQuantities);
            Assert.Contains(Text(d["class"]), new[] { "assumption", "default" });
            var mapping = d["domainMapping"];
            Assert.True(mapping is not null, $"{key}: an outside-evidence quantity names the provenance it will carry.");
            Assert.Equal("ParameterProvenance", Text(mapping!["enum"]));
            Assert.Contains(Text(mapping["member"]), new[] { "Asserted", "Default" });
            Assert.NotEqual("Calibrated", Text(mapping["member"]));
            Assert.False(string.IsNullOrWhiteSpace(Text(d["rationale"])), $"{key}: reviewed assumptions carry a rationale.");
        }

        Assert.DoesNotContain(Nodes(register["labelMap"]), r => Text(r["member"]) == "Calibrated");
    }

    [Fact]
    public void The_guide_links_the_review_procedure_between_portfolio_selection_and_loading_and_the_brief_carries_the_register_slot()
    {
        var guide = ReadRepoFile(GuidePath);
        var portfolio = guide.IndexOf("## 8. Portfolio selection", StringComparison.Ordinal);
        var review = guide.IndexOf("## 10. Consequential-decision review", StringComparison.Ordinal);
        var loading = guide.IndexOf("## 11. Assembling and loading", StringComparison.Ordinal);
        Assert.True(portfolio >= 0 && review > portfolio && loading > review,
            "The review must be section 10, after portfolio selection and commercial normalization and before assembling and loading.");

        var linked = RelativeLinkTargets(guide, GuidePath).Select(t => t.ResolvedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { ReviewReadmePath, RegisterTemplatePath, RegisterExamplePath })
            Assert.Contains(Path.GetFullPath(RepoPath(path)), linked);

        Assert.DoesNotContain("<!-- PRD5", guide);
        Assert.Contains("Decision register:", guide);
        Assert.Contains("never defaulted into the fragment", guide);
        Assert.DoesNotContain("Default: the subagent picks a documented default", guide); // old interview question 8
        Assert.Contains("Register and source references", guide);                          // kept with the import package
    }

    [Theory]
    [MemberData(nameof(DatasetFiles))]
    public void Each_dataset_file_points_at_the_review_procedure_and_names_its_consequential_choices(string fileName)
    {
        var content = ReadRepoFile(DatasetPath(fileName));
        Assert.Contains("](../review/README.md)", content);
        Assert.Contains("proposed register entry", content);
        Assert.Contains("Consequential choices", content);
    }

    [Fact]
    public void The_review_procedure_documents_every_register_field_state_class_and_outside_evidence_quantity()
    {
        var readme = ReadRepoFile(ReviewReadmePath);
        foreach (var name in RequiredDecisionKeys.Concat(ReviewStates).Concat(DecisionClasses).Concat(DecisionTopics)
                     .Concat(OutsideEvidenceQuantities).Concat(new[] { "reopened", "valueMap", "labelMap", "humanEvidence", "tierOutcome" }))
            Assert.Contains($"`{name}`", readme);

        // The enum members the mapping table promises exist (the register test checks the JSON; this checks the prose).
        foreach (var member in new[] { "Observed", "Inferred", "Asserted", "Default", "Gold", "NotPlaying" })
            Assert.Contains(member, readme);
        Assert.Contains("silence is not acceptance", readme, StringComparison.OrdinalIgnoreCase);

        // The consolidated review lists every decision key exactly as the register does.
        var review = ReadRepoFile(ReviewExamplePath);
        foreach (var d in Decisions(Register()))
            Assert.Contains(Text(d["key"]), review);

        var dataset05 = ReadRepoFile(DatasetPath("05-coverage-and-needs.md"));
        Assert.Contains("unknown", dataset05, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is evidence of a need", dataset05);
        var dataset08 = ReadRepoFile(DatasetPath("08-assumption-sets.md"));
        Assert.Contains("never relabel", dataset08, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- PRD5: append new tests above this line ----------

    // ---------- PRD7: commercial normalization ----------
    // docs/onboarding/commercial/: the normalization artifact for Harborline, the register excerpt it
    // cites, the Salesforce extract it was derived from and the seed fragments generated from its
    // normalized rows. The arithmetic is recomputed here from the RAW fixture records with the
    // documented formulas, so a wrong formula or a row that does not reconcile fails the build.

    private const string CommercialDirectory = "docs/onboarding/commercial";
    private const string CommercialReadmePath = CommercialDirectory + "/README.md";
    private const string CommercialArtifactPath = CommercialDirectory + "/commercial-normalization.example.json";
    private const string CommercialRegisterPath = CommercialDirectory + "/commercial-register-excerpt.example.json";
    private const string CommercialFixtureReadmePath = CommercialDirectory + "/fixture/README.md";
    private const string CommercialSeedDirectory = CommercialDirectory + "/fixture/seed";
    private static readonly DateOnly CommercialAsOf = new(2026, 9, 30);

    private static readonly Dictionary<string, string> CommercialRawFiles = new()
    {
        ["Account"] = "accounts.json",
        ["Opportunity"] = "opportunities.json",
        ["Contract"] = "contracts.json",
        ["Subscription__c"] = "subscription-lines.json"
    };

    private static readonly (string List, string ObjectType)[] CommercialLists =
    [
        ("firms", "Account"), ("deals", "Opportunity"), ("contracts", "Contract"), ("lines", "Subscription__c")
    ];

    private static readonly string[] CommercialDealTypes =
        ["newLogo", "renewal", "crossSell", "sameProductExpansion", "services", "unknown"];

    private static readonly string[] CommercialFormulaKeys =
    [
        "F1-term-months", "F2-recurring-monthly", "F3-annual", "F4-currency", "F5-base-arpu",
        "F6-module-arpu", "F7-penetration", "F8-implementation-fee", "F9-price-level", "F10-reconcile"
    ];

    private static readonly Regex DecisionKeyRegex = new(@"^(RULE|DEC)-[a-z0-9-]+$");

    private static JsonNode Commercial() => ReadJson(CommercialArtifactPath);

    private static JsonNode CommercialRegister() => ReadJson(CommercialRegisterPath);

    private static Dictionary<string, JsonNode> RawRecords(string objectType) =>
        Nodes(ReadJson($"{CommercialDirectory}/fixture/salesforce/{CommercialRawFiles[objectType]}")["response"]!["records"])
            .ToDictionary(r => Text(r["Id"]), r => r, StringComparer.Ordinal);

    private static List<JsonNode> Rows(JsonNode artifact, string list) => Nodes(artifact["normalized"]![list]).ToList();

    private static Dictionary<string, JsonNode> RowsById(JsonNode artifact, string list) =>
        Rows(artifact, list).ToDictionary(r => Text(r["sourceId"]), r => r, StringComparer.Ordinal);

    private static decimal Dec(JsonNode? node) =>
        node is null ? throw new Xunit.Sdk.XunitException("Expected a number, found null.") : node.GetValue<decimal>();

    private static void AssertMoney(decimal expected, JsonNode? actual, string where) =>
        Assert.True(actual is not null && Math.Abs(expected - Dec(actual)) <= 0.005m,
            $"{where}: recomputed {expected}, artifact says {actual?.ToJsonString() ?? "null"}.");

    private static DateOnly Day(JsonNode? node) =>
        DateOnly.ParseExact(Text(node), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>F1: whole calendar months from the start date to the day after the (inclusive) end date.</summary>
    private static int WholeMonths(DateOnly start, DateOnly endInclusive)
    {
        var end = endInclusive.AddDays(1);
        return (end.Year - start.Year) * 12 + end.Month - start.Month;
    }

    private static List<(string ObjectType, string Id, string Disposition)> CommercialDispositions(JsonNode artifact)
    {
        var all = new List<(string, string, string)>();
        foreach (var (list, type) in CommercialLists)
            foreach (var row in Rows(artifact, list))
            {
                Assert.Equal(type, Text(row["objectType"]));
                all.Add((type, Text(row["sourceId"]), "imported"));
            }

        foreach (var row in Nodes(artifact["excluded"])) all.Add((Text(row["objectType"]), Text(row["sourceId"]), "excluded"));
        foreach (var row in Nodes(artifact["unresolved"])) all.Add((Text(row["objectType"]), Text(row["sourceId"]), "unresolved"));
        return all;
    }

    private static SeedFile CommercialSeed(string fileName) =>
        ParseSeedFile(ReadRepoFile($"{CommercialSeedDirectory}/{fileName}"), fileName);

    private static SeedMarketDynamics CommercialDynamics(string fileName) =>
        CommercialSeed(fileName).MarketDynamics ?? throw new Xunit.Sdk.XunitException($"{fileName} has no marketDynamics.");

    private static void CollectDecisionKeys(JsonNode? node, HashSet<string> into)
    {
        switch (node)
        {
            case JsonObject obj: foreach (var (_, child) in obj) CollectDecisionKeys(child, into); break;
            case JsonArray array: foreach (var child in array) CollectDecisionKeys(child, into); break;
            case JsonValue value when value.TryGetValue<string>(out var text) && DecisionKeyRegex.IsMatch(text): into.Add(text); break;
        }
    }

    private static HashSet<string> KnownDecisionKeys() =>
        Decisions(Register()).Concat(Decisions(CommercialRegister())).Select(d => Text(d["key"])).ToHashSet(StringComparer.Ordinal);

    private static JsonNode CommercialRule(string key) => Decision(CommercialRegister(), key);

    /// <summary>The opportunity history rows of one deal, oldest first (empty when the extract has none).</summary>
    private static List<JsonNode> DealHistory(string opportunityId) =>
        Nodes(ReadJson($"{CommercialDirectory}/fixture/salesforce/opportunity-history.json")["response"]!["records"])
            .Where(h => Text(h["OpportunityId"]) == opportunityId)
            .OrderBy(h => Text(h["CreatedDate"]), StringComparer.Ordinal).ToList();

    private static string DatePart(string timestamp) => timestamp[..10];

    [Fact]
    public void The_commercial_artifact_references_the_manifest_and_register_and_carries_the_stable_keys()
    {
        var artifact = Commercial();
        Assert.Equal(1, artifact["normalizationVersion"]!.GetValue<int>());
        foreach (var key in new[]
                 {
                     "manifest", "register", "extract", "mappingRules", "currency", "formulas", "normalized", "excluded",
                     "unresolved", "sourceCounts", "calibrationPopulation", "segmentTotals", "gaps"
                 })
            Assert.NotNull(artifact[key]);
        foreach (var list in new[] { "firms", "deals", "contracts", "lines", "relationships", "churnEvents" })
            Assert.NotNull(artifact["normalized"]![list]);

        var manifest = ReadJson(ManifestPath);
        Assert.Equal(Text(manifest["discovery"]!["runId"]), Text(artifact["manifest"]!["runId"]));
        Assert.Contains(Sources(manifest), s => Text(s!["sourceId"]) == Text(artifact["manifest"]!["sourceId"]));
        foreach (var file in new[] { Text(artifact["manifest"]!["file"]), Text(artifact["register"]!["file"]) })
            Assert.True(File.Exists(Path.GetFullPath(Path.Combine(RepoPath(CommercialDirectory), file))), $"{file} does not exist.");

        // Instance identity lives in the manifest; the artifact refers to it by key and never copies it.
        var text = ReadRepoFile(CommercialArtifactPath);
        Assert.DoesNotContain("00DEX000000HRBLAAA", text);
        Assert.DoesNotContain("my.salesforce.com", text);

        var keys = Nodes(artifact["formulas"]).Select(f => Text(f["key"])).ToList();
        foreach (var key in CommercialFormulaKeys) Assert.Contains(key, keys);
        foreach (var formula in Nodes(artifact["formulas"])) Assert.False(string.IsNullOrWhiteSpace(Text(formula["expression"])));
    }

    [Fact]
    public void Every_normalized_excluded_and_unresolved_record_exists_in_the_extract_and_carries_a_classification_or_reason()
    {
        var artifact = Commercial();
        var raw = CommercialRawFiles.Keys.ToDictionary(k => k, RawRecords);

        foreach (var (list, type) in CommercialLists)
            foreach (var row in Rows(artifact, list))
            {
                Assert.True(raw[type].ContainsKey(Text(row["sourceId"])), $"{list}: {Text(row["sourceId"])} is not in the {type} extract.");
                Assert.False(string.IsNullOrWhiteSpace(Text(row["classification"])), $"{list}: {Text(row["sourceId"])} has no classification.");
            }

        foreach (var row in Nodes(artifact["excluded"]).Concat(Nodes(artifact["unresolved"])))
        {
            var id = Text(row["sourceId"]);
            Assert.True(raw[Text(row["objectType"])].ContainsKey(id), $"{id} is not in the {Text(row["objectType"])} extract.");
            Assert.False(string.IsNullOrWhiteSpace(Text(row["reason"])), $"{id} has no reason.");
        }

        foreach (var row in Nodes(artifact["excluded"]))
            Assert.False(string.IsNullOrWhiteSpace(Text(row["classification"])), $"excluded {Text(row["sourceId"])} has no classification.");
        foreach (var row in Nodes(artifact["unresolved"]))
            Assert.False(string.IsNullOrWhiteSpace(Text(row["question"])), $"unresolved {Text(row["sourceId"])} asks no question.");
    }

    [Fact]
    public void Every_extract_record_is_imported_excluded_or_unresolved_exactly_once_and_the_source_counts_reconcile()
    {
        var artifact = Commercial();
        var dispositions = CommercialDispositions(artifact);

        foreach (var (type, file) in CommercialRawFiles)
        {
            var extract = RawRecords(type);
            var mine = dispositions.Where(d => d.ObjectType == type).ToList();
            Assert.Equal(extract.Count, mine.Count);
            Assert.Equal(extract.Keys.Order(StringComparer.Ordinal), mine.Select(d => d.Id).Order(StringComparer.Ordinal));
            Assert.Equal(mine.Count, mine.Select(d => d.Id).Distinct().Count());

            var count = Nodes(artifact["sourceCounts"]).Single(c => Text(c["objectType"]) == type);
            Assert.Equal(extract.Count, count["source"]!.GetValue<int>());
            Assert.Equal(mine.Count(d => d.Disposition == "imported"), count["imported"]!.GetValue<int>());
            Assert.Equal(mine.Count(d => d.Disposition == "excluded"), count["excluded"]!.GetValue<int>());
            Assert.Equal(mine.Count(d => d.Disposition == "unresolved"), count["unresolved"]!.GetValue<int>());
            Assert.Equal(count["source"]!.GetValue<int>(),
                count["imported"]!.GetValue<int>() + count["excluded"]!.GetValue<int>() + count["unresolved"]!.GetValue<int>());
            var declared = Nodes(artifact["extract"]!["objects"]).Single(o => Text(o["objectType"]) == type);
            Assert.Equal(extract.Count, declared["rowCount"]!.GetValue<int>());
        }
    }

    [Fact]
    public void Every_decision_key_the_artifact_names_exists_in_the_review_register_or_the_commercial_excerpt()
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        CollectDecisionKeys(Commercial(), referenced);
        var known = KnownDecisionKeys();
        Assert.NotEmpty(referenced);
        foreach (var key in referenced) Assert.True(known.Contains(key), $"The artifact names {key}, which is in no register.");

        var excerpt = CommercialRegister();
        var review = Decisions(Register()).Select(d => Text(d["key"])).ToHashSet(StringComparer.Ordinal);
        var raw = CommercialRawFiles.Keys.SelectMany(t => RawRecords(t).Keys).ToHashSet(StringComparer.Ordinal);
        var manifest = ReadJson(ManifestPath);
        foreach (var decision in Decisions(excerpt))
        {
            Assert.DoesNotContain(Text(decision["key"]), review); // an excerpt adds entries, never redefines PRD5's
            foreach (var key in RequiredDecisionKeys) Assert.True(decision.AsObject().ContainsKey(key), $"{Text(decision["key"])} lacks '{key}'.");
            Assert.Contains(Text(decision["reviewState"]), ReviewStates);
            Assert.Contains(Text(decision["topic"]), DecisionTopics);
            Assert.Contains(Text(decision["class"]), DecisionClasses);
            if (Text(decision["reviewState"]) == "accepted")
                Assert.Equal("accepted", EffectiveState(decision));
            foreach (var evidence in Nodes(decision["evidence"]))
            {
                var reference = Text(evidence["ref"]);
                var (kind, id) = (reference[..reference.IndexOf(':')], reference[(reference.IndexOf(':') + 1)..]);
                Assert.True(kind switch
                {
                    "salesforce" => raw.Contains(id),
                    "manifest" => id.StartsWith("gap:") || Nodes(manifest["openQuestions"]).Any(q => Text(q["id"]) == id),
                    "human" => Nodes(excerpt["humanEvidence"]).Any(h => Text(h["id"]) == id),
                    "register" => known.Contains(id),
                    _ => false
                }, $"{Text(decision["key"])}: evidence '{reference}' does not resolve.");
            }
        }
    }

    [Fact]
    public void Recurring_amounts_are_normalized_by_actual_term_and_quantity_with_components_kept_apart()
    {
        var artifact = Commercial();
        var contracts = RawRecords("Contract");
        var rawLines = RawRecords("Subscription__c");
        var componentMap = CommercialRule("RULE-commercial-recurring")["proposedValue"]!["componentMap"]!.AsObject();
        var rates = Nodes(artifact["currency"]!["rates"]).ToDictionary(r => Text(r["from"]), r => Dec(r["rate"]));

        foreach (var line in Rows(artifact, "lines"))
        {
            var id = Text(line["sourceId"]);
            var source = rawLines[id];
            var contract = contracts[Text(source["Contract__c"])];
            Assert.Equal(Text(source["Contract__c"]), Text(line["contractId"]));
            Assert.Equal(Text(componentMap[Text(source["Component__c"])]), Text(line["classification"]));

            var quantity = Dec(source["Quantity__c"]);
            Assert.Equal(quantity * Dec(source["UnitPrice__c"]), Dec(source["TotalPrice__c"]));   // F10 at line level
            AssertMoney(Dec(source["TotalPrice__c"]), line["totalPrice"], id);

            var rate = Text(source["CurrencyIsoCode"]) == "USD" ? 1m : rates[Text(source["CurrencyIsoCode"])];
            AssertMoney(rate, line["rate"], $"{id} rate");
            var reporting = line["reporting"]!;
            if (Text(line["classification"]).StartsWith("recurring"))
            {
                // F1 from the contract dates, F2 and F3 from the line, F4 for the reporting currency.
                var months = WholeMonths(Day(contract["StartDate"]), Day(contract["EndDate"]));
                Assert.Equal(months, source["TermMonths__c"]!.GetValue<int>());
                Assert.Equal(months, line["termMonths"]!.GetValue<int>());
                var monthly = Dec(source["TotalPrice__c"]) / months;
                AssertMoney(monthly, line["monthly"], $"{id} monthly");
                AssertMoney(monthly * 12, line["annual"], $"{id} annual");
                AssertMoney(Math.Round(monthly * rate, 2), reporting["monthly"], $"{id} reporting monthly");
                Assert.Null(reporting["oneTime"]);
            }
            else
            {
                Assert.Null(reporting["monthly"]);
                Assert.Null(line["monthly"]);
                AssertMoney(Math.Round(Dec(source["TotalPrice__c"]) * rate, 2), reporting["oneTime"], $"{id} one-time");
            }
        }

        // The three-year contract: 36 actual months, not a 12-month year, and the fee and services stay out of ARR.
        var bluewater = contracts.Values.Single(c => Text(c["Id"]).EndsWith("C001AAA"));
        Assert.Equal(36, WholeMonths(Day(bluewater["StartDate"]), Day(bluewater["EndDate"])));
        var base1 = Rows(artifact, "lines").Where(l => Text(l["contractId"]) == Text(bluewater["Id"]) && Text(l["classification"]) == "recurringBase").Single();
        AssertMoney(4500m, base1["monthly"], "three-year base monthly");
        AssertMoney(54000m, base1["annual"], "three-year base ARR");
        var naive = Dec(RawRecords("Opportunity")[Text(bluewater["Opportunity__c"])]["Amount"]) / 12m;
        Assert.NotEqual(naive, Dec(base1["monthly"]));
        var otherKinds = Rows(artifact, "lines").Where(l => Text(l["contractId"]) == Text(bluewater["Id"]) && l != base1)
            .Select(l => Text(l["classification"]))
            .Concat(Nodes(artifact["excluded"]).Where(e => Text(e["objectType"]) == "Subscription__c" && Text(rawLines[Text(e["sourceId"])]["Contract__c"]) == Text(bluewater["Id"]))
                .Select(e => Text(e["classification"]))).ToHashSet();
        Assert.True(otherKinds.SetEquals(["implementation", "services", "usageEstimate"]), string.Join(",", otherKinds));

        // F10: the line totals of a contract add up to its opportunity's Amount (no line lost, none counted twice).
        foreach (var contract in contracts.Values.Where(c => Text(c["Status"]) != "Draft"))
        {
            var total = rawLines.Values.Where(l => Text(l["Contract__c"]) == Text(contract["Id"])).Sum(l => Dec(l["TotalPrice__c"]));
            Assert.Equal(Dec(RawRecords("Opportunity")[Text(contract["Opportunity__c"])]["Amount"]), total);
        }
    }

    [Fact]
    public void Currency_conversion_records_its_rate_source_and_date_and_raw_amounts_are_never_summed_across_currencies()
    {
        var artifact = Commercial();
        var currency = artifact["currency"]!;
        Assert.Equal("USD", Text(currency["reporting"]));
        var rate = Nodes(currency["rates"]).Single(r => Text(r["from"]) == "EUR");
        Assert.Equal("USD", Text(rate["to"]));
        Assert.Equal(1.08m, Dec(rate["rate"]));
        Assert.Equal("2026-09-30", Text(rate["rateDate"]));
        Assert.False(string.IsNullOrWhiteSpace(Text(rate["source"])));
        Assert.Equal("accepted", EffectiveState(CommercialRule(Text(rate["decisionKey"]))));

        // One record is EUR; every total is a sum of reporting-currency values, never of raw source amounts.
        var eur = Rows(artifact, "lines").Where(l => Text(l["currency"]) == "EUR").ToList();
        Assert.Equal(2, eur.Count);   // the EUR base and implementation lines of the one converted contract
        var totals = Nodes(artifact["segmentTotals"]).Single(t => Text(t["segment"]) == "Regional 3PLs");
        var lineRows = RowsById(artifact, "lines");
        var rawSum = Rows(artifact, "relationships").Where(r => Text(r["kind"]) == "base" && r["active"]!.GetValue<bool>())
            .Sum(r => Dec(lineRows[Text(r["currentLineId"])]["monthly"]));
        Assert.NotEqual(rawSum, Dec(totals["baseMonthly"]));   // 4500 + 3600 raw would be wrong; 4500 + 3888 is right

        // The deferred alternative: without the rule the EUR population is unresolved and reported apart.
        var deferred = currency["deferredAlternative"]!;
        var alpenhaus = RowsById(artifact, "firms").Values.Single(f => Text(f["name"]).StartsWith("Alpenhaus"));
        Assert.Contains(Text(alpenhaus["sourceId"]), Strings(deferred["movesToUnresolved"]));
        var usd = deferred["segmentTotalsReportingOnly"]!["Regional 3PLs"]!;
        Assert.Equal(1, usd["activeCustomers"]!.GetValue<int>());
        AssertMoney(54000m, usd["baseArr"], "deferred USD-only ARR");
        AssertMoney(18500m, usd["implementationFee"], "deferred USD-only fee");
        var separate = deferred["segmentTotalsReportingOnly"]!["Regional 3PLs"]!["separatePopulations"]!["EUR"]!;
        Assert.Equal(1, separate["activeCustomers"]!.GetValue<int>());
        AssertMoney(43200m, separate["baseArr"], "deferred EUR-only ARR (source currency)");
    }

    /// <summary>Which firms hold an active edge and what it pays, recomputed from the raw contracts and lines.</summary>
    private static List<(string FirmId, string Kind, string Product, decimal Monthly, string LineId)> ActiveEdgesFromRaw(JsonNode artifact)
    {
        var contracts = RawRecords("Contract");
        var lines = RawRecords("Subscription__c");
        var rate = Nodes(artifact["currency"]!["rates"]).ToDictionary(r => Text(r["from"]), r => Dec(r["rate"]));
        var edges = new List<(string, string, string, decimal, string)>();
        foreach (var contract in contracts.Values)
        {
            var terminated = contract["Terminated_Date__c"] is null ? (DateOnly?)null : Day(contract["Terminated_Date__c"]);
            var active = Text(contract["Status"]) == "Activated" && Day(contract["StartDate"]) <= CommercialAsOf
                         && Day(contract["EndDate"]) >= CommercialAsOf && (terminated is null || terminated > CommercialAsOf);
            if (!active) continue;
            foreach (var line in lines.Values.Where(l => Text(l["Contract__c"]) == Text(contract["Id"]) && Text(l["Component__c"]).StartsWith("Recurring")))
            {
                var currency = Text(line["CurrencyIsoCode"]);
                var monthly = Math.Round(Dec(line["TotalPrice__c"]) / line["TermMonths__c"]!.GetValue<int>() * (currency == "USD" ? 1m : rate[currency]), 2);
                edges.Add((Text(contract["AccountId"]), Text(line["Component__c"]) == "Recurring - Base" ? "base" : "module",
                    Text(line["Product__c"]), monthly, Text(line["Id"])));
            }
        }

        return edges;
    }

    [Fact]
    public void Segment_totals_are_reproducible_and_a_churned_account_is_not_an_active_customer()
    {
        var artifact = Commercial();
        var firms = RowsById(artifact, "firms");
        var lines = RowsById(artifact, "lines");
        var fromRaw = ActiveEdgesFromRaw(artifact);

        var total = Nodes(artifact["segmentTotals"]).Single(t => Text(t["segment"]) == "Regional 3PLs");
        Assert.Equal("resolved", Text(total["status"]));
        var segmentEdges = fromRaw.Where(e => Text(firms[e.FirmId]["segment"]!["name"]) == "Regional 3PLs").ToList();
        var baseEdges = segmentEdges.Where(e => e.Kind == "base").ToList();
        Assert.Equal(baseEdges.Select(e => e.FirmId).Distinct().Order().ToList(), Strings(total["activeFirmSourceIds"]).Order().ToList());
        Assert.Equal(baseEdges.Count, total["activeCustomers"]!.GetValue<int>());
        AssertMoney(baseEdges.Sum(e => e.Monthly), total["baseMonthly"], "base monthly");
        AssertMoney(baseEdges.Sum(e => e.Monthly) * 12, total["baseArr"], "base ARR");
        AssertMoney(baseEdges.Sum(e => e.Monthly) / baseEdges.Count, total["baseArpuPerMonth"], "base ARPU");

        // No double counting: base ARPU excludes module revenue; the naive figure is recorded and differs.
        var moduleMonthly = segmentEdges.Where(e => e.Kind == "module").Sum(e => e.Monthly);
        Assert.True(moduleMonthly > 0);
        AssertMoney((baseEdges.Sum(e => e.Monthly) + moduleMonthly) / baseEdges.Count, total["doubleCountCheck"]!["naiveArpuIfModulesIncluded"], "naive ARPU");
        Assert.NotEqual(Dec(total["baseArpuPerMonth"]), Dec(total["doubleCountCheck"]!["naiveArpuIfModulesIncluded"]));

        var module = Nodes(total["modules"]).Single();
        var holders = segmentEdges.Where(e => e.Kind == "module" && e.Product == Text(module["product"])).Select(e => e.FirmId).Distinct().ToList();
        Assert.Equal(holders.Count, module["holders"]!.GetValue<int>());
        AssertMoney((decimal)holders.Count / baseEdges.Count, module["penetration"], "penetration (unique firms / active firms)");
        AssertMoney(moduleMonthly / holders.Count, module["arpuPerHolderPerMonth"], "module ARPU per holder");
        AssertMoney(moduleMonthly * 12, module["arr"], "module ARR");

        // F8: mean one-time implementation over the segment's imported implementation lines, reporting currency.
        var fees = lines.Values.Where(l => Text(l["classification"]) == "implementation"
                                           && Text(firms[Text(RawRecords("Contract")[Text(l["contractId"])]["AccountId"])]["segment"]!["name"]) == "Regional 3PLs")
            .Select(l => Dec(l["reporting"]!["oneTime"])).ToList();
        AssertMoney(fees.Sum() / fees.Count, total["implementationFee"]!["value"], "implementation fee");

        // The churned account: CRM still says Customer, contract evidence says ended; history retained.
        var tidewater = firms.Values.Single(f => Text(f["name"]).StartsWith("Tidewater"));
        Assert.Equal("Customer", Text(RawRecords("Account")[Text(tidewater["sourceId"])]["Type"]));
        Assert.Equal("former-customer", Text(tidewater["status"]));
        Assert.DoesNotContain(Text(tidewater["sourceId"]), Strings(total["activeFirmSourceIds"]));
        Assert.Contains(Text(tidewater["sourceId"]), Strings(total["endedInObservationPeriod"]));

        var firmsFragment = CommercialDynamics("11-firms-and-relationships.json").Firms.Single(f => f.Name.StartsWith("Tidewater"));
        var edge = Assert.Single(firmsFragment.Relationships);
        Assert.Equal("2026-06-30", edge.EndDate);
        Assert.Equal("2025-03-01", edge.AcquisitionDate);
        var opportunities = CommercialDynamics("12-historical-facts.json").Opportunities;
        Assert.Contains(opportunities, o => o.FirmSource!.SourceRecordId == Text(tidewater["sourceId"]) && o.Outcome == "Won");

        // The unresolved segment keeps its total unresolved instead of loading a zero.
        var pending = Nodes(artifact["segmentTotals"]).Single(t => Text(t["segment"]) == "E-commerce Fulfillment");
        Assert.Equal("unresolved", Text(pending["status"]));
        Assert.Null(pending["activeCustomers"]);
    }

    [Fact]
    public void Renewals_and_services_never_become_calibration_wins_and_the_win_population_matches_the_fragment()
    {
        var artifact = Commercial();
        var opportunities = RawRecords("Opportunity");
        var facts = CommercialDynamics("12-historical-facts.json").Opportunities;
        var factIds = facts.Select(o => o.Source!.SourceRecordId).ToHashSet();

        foreach (var row in Nodes(artifact["excluded"]).Where(e => Text(e["objectType"]) == "Opportunity"))
        {
            Assert.DoesNotContain(Text(row["sourceId"]), factIds);
            Assert.Contains(Text(row["classification"]), CommercialDealTypes);
            Assert.NotEqual("newLogo", Text(row["classification"]));
        }

        Assert.Contains(Nodes(artifact["excluded"]), e => Text(e["classification"]) == "renewal" && Text(opportunities[Text(e["sourceId"])]["Type"]) == "Renewal");
        Assert.Contains(Nodes(artifact["excluded"]), e => Text(e["classification"]) == "services" && Text(opportunities[Text(e["sourceId"])]["Type"]) == "Services");

        // Wins in the fragment are exactly the new-logo Closed Won deals; none is a cross-sell (that decision is still open).
        var wonInFragment = facts.Where(o => o.Outcome == "Won").Select(o => o.Source!.SourceRecordId).Order().ToList();
        var newLogoWon = Rows(artifact, "deals").Where(d => Text(d["classification"]) == "newLogo" && Text(d["outcome"]) == "Won")
            .Select(d => Text(d["sourceId"])).Order().ToList();
        Assert.Equal(newLogoWon, wonInFragment);
        Assert.All(facts, o => Assert.Null(o.CrossSellModuleProductName));
        var population = artifact["calibrationPopulation"]!["winRate"]!;
        Assert.Equal(wonInFragment.Count, Nodes(population["Regional 3PLs"]!["won"]).Count() + Nodes(population["E-commerce Fulfillment"]!["won"]).Count());
    }

    [Fact]
    public void Open_deals_stay_open_losses_stay_lost_and_a_blank_loss_reason_is_unresolved_never_no_decision()
    {
        var artifact = Commercial();
        var opportunities = RawRecords("Opportunity");
        var map = CommercialRule("RULE-commercial-outcome")["proposedValue"]!["competitorProductMap"]!.AsObject();
        var facts = CommercialDynamics("12-historical-facts.json").Opportunities.ToDictionary(o => o.Source!.SourceRecordId);

        foreach (var deal in Rows(artifact, "deals"))
        {
            var id = Text(deal["sourceId"]);
            var raw = opportunities[id];
            var fact = facts[id];
            switch (Text(deal["outcome"]))
            {
                case "Open":
                    Assert.False(raw["IsClosed"]!.GetValue<bool>());
                    Assert.Null(fact.DecidedDate);
                    Assert.Null(fact.Outcome);
                    Assert.Null(fact.RecordedDate);
                    Assert.Equal(Text(raw["CloseDate"]), Text(deal["dates"]!["expectedClose"]!["value"]));
                    Assert.True(Day(raw["CloseDate"]) > CommercialAsOf, "the open deal's CloseDate is a forecast in the future");
                    break;
                case "Lost":
                    Assert.Equal("Lost", fact.Outcome);
                    Assert.Equal("Lost to Competitor", Text(raw["Loss_Reason__c"]));
                    Assert.Equal(Text(map[Text(raw["Competitor__c"])]), fact.WinningProductName);
                    break;
                case "NoDecision":
                    Assert.Equal("NoDecision", fact.Outcome);
                    Assert.Equal("No Decision", Text(raw["Loss_Reason__c"]));
                    Assert.False(string.IsNullOrWhiteSpace(Text(raw["Loss_Notes__c"])), "NoDecision needs evidence of a non-purchase");
                    Assert.False(string.IsNullOrWhiteSpace(Text(deal["noDecisionEvidence"])));
                    Assert.Null(fact.WinningProductName);
                    break;
                case "Won":
                    Assert.Equal("Won", fact.Outcome);
                    Assert.False(string.IsNullOrWhiteSpace(fact.WinningProductName));
                    break;
                default:
                    Assert.Fail($"{id}: unexpected outcome {Text(deal["outcome"])}.");
                    break;
            }

            // A competitor is never invented: no competitor value in the source means no winner on a lost deal.
            if (fact.Outcome == "Lost" && raw["Competitor__c"] is null) Assert.Null(fact.WinningProductName);
        }

        var blank = opportunities.Values.Where(o => o["IsClosed"]!.GetValue<bool>() && !o["IsWon"]!.GetValue<bool>() && o["Loss_Reason__c"] is null).ToList();
        Assert.NotEmpty(blank);
        foreach (var raw in blank)
        {
            var id = Text(raw["Id"]);
            Assert.DoesNotContain(id, facts.Keys);
            var unresolved = Nodes(artifact["unresolved"]).Single(u => Text(u["sourceId"]) == id);
            Assert.Contains("loss reason", Text(unresolved["reason"]), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Deal_dates_follow_their_documented_meaning_and_no_recorded_date_is_invented()
    {
        var artifact = Commercial();
        var opportunities = RawRecords("Opportunity");
        var facts = CommercialDynamics("12-historical-facts.json").Opportunities.ToDictionary(o => o.Source!.SourceRecordId);

        foreach (var deal in Rows(artifact, "deals"))
        {
            var id = Text(deal["sourceId"]);
            var raw = opportunities[id];
            var fact = facts[id];
            var history = DealHistory(id);
            var opened = history.FirstOrDefault(h => Text(h["StageName"]) == "Prospecting");
            var qualified = history.FirstOrDefault(h => Text(h["StageName"]) == "Qualification");
            var terminal = history.FirstOrDefault(h => Text(h["StageName"]).StartsWith("Closed"));

            Assert.Equal(DatePart(opened is null ? Text(raw["CreatedDate"]) : Text(opened["CreatedDate"])), fact.OpenedDate);
            Assert.Equal(qualified is null ? null : DatePart(Text(qualified["CreatedDate"])), fact.QualifiedDate);
            if (Text(deal["outcome"]) != "Open")
            {
                Assert.Equal(terminal is null ? Text(raw["CloseDate"]) : DatePart(Text(terminal["CreatedDate"])), fact.DecidedDate);
                // Recorded date: the transition date when history proves it, otherwise omitted (import time).
                Assert.Equal(terminal is null ? null : DatePart(Text(terminal["CreatedDate"])), fact.RecordedDate);
                // Neither the last modification nor the creation of the record is a decision date.
                Assert.NotEqual(DatePart(Text(raw["LastModifiedDate"])), fact.DecidedDate);
                Assert.NotEqual(DatePart(Text(raw["CreatedDate"])), fact.DecidedDate);
            }

            Assert.Equal(fact.OpenedDate, Text(deal["dates"]!["opened"]!["value"]));
            Assert.False(string.IsNullOrWhiteSpace(Text(deal["dates"]!["opened"]!["basis"])));
        }

        Assert.Contains(Rows(artifact, "deals"), d => DealHistory(Text(d["sourceId"])).Count > 0);
        Assert.Contains(Rows(artifact, "deals"), d => Text(d["outcome"]) != "Open" && DealHistory(Text(d["sourceId"])).Count == 0);
    }

    [Fact]
    public void Seed_fragments_parse_strictly_bind_only_extract_records_and_never_turn_an_unresolved_value_into_a_zero()
    {
        var artifact = Commercial();
        var manifest = ReadJson(ManifestPath);
        var salesforce = Sources(manifest).Single(s => Text(s!["sourceId"]) == "salesforce")!;
        var raw = CommercialRawFiles.Keys.ToDictionary(k => k, RawRecords);
        var unresolvedIds = Nodes(artifact["unresolved"]).Select(u => Text(u["sourceId"])).ToHashSet();

        foreach (var file in new[] { "06-segment-economics.json", "11-firms-and-relationships.json", "12-historical-facts.json" })
        {
            var seed = CommercialSeed(file);
            Assert.True(HasAnyTopLevelKey(seed));
            AssertEnumsAreExactMemberNames(seed, file);
            AssertDatesAreIsoAndRecordedDatesArePast(seed, file);
            if (file.StartsWith("06")) continue;   // stock states carry no source identity
            Assert.Equal(Text(salesforce["sourceId"]), seed.MarketDynamics!.SourceDefaults?.SourceSystem ?? "");
            Assert.Equal(Text(salesforce["instance"]!["orgId"]), seed.MarketDynamics.SourceDefaults?.SourceInstance ?? "");
        }

        var sources = new List<SeedSourceRef?>();
        var dynamics11 = CommercialDynamics("11-firms-and-relationships.json");
        foreach (var firm in dynamics11.Firms)
        {
            sources.Add(firm.Source);
            sources.AddRange(firm.Relationships.SelectMany(r => r.Sources));
        }

        var dynamics12 = CommercialDynamics("12-historical-facts.json");
        foreach (var opportunity in dynamics12.Opportunities)
        {
            sources.Add(opportunity.Source);
            sources.Add(opportunity.FirmSource);
        }

        foreach (var churn in dynamics12.ChurnEvents) { sources.Add(churn.Source); sources.Add(churn.FirmSource); }
        Assert.NotEmpty(sources);
        foreach (var source in sources)
        {
            Assert.NotNull(source);
            Assert.True(raw.TryGetValue(source!.ObjectKind, out var records), $"objectKind {source.ObjectKind} is not an extract object.");
            Assert.True(records.ContainsKey(source.SourceRecordId), $"{source.ObjectKind} {source.SourceRecordId} is not in the extract.");
            Assert.DoesNotContain(source.SourceRecordId, unresolvedIds);
        }

        // A source record is bound by exactly one row per (object kind, id) of a file, and a churn never reuses an edge's record.
        var bound = dynamics11.Firms.Select(f => f.Source!).Concat(dynamics11.Firms.SelectMany(f => f.Relationships.SelectMany(r => r.Sources)))
            .Select(s => (s.ObjectKind, s.SourceRecordId)).ToList();
        Assert.Equal(bound.Count, bound.Distinct().Count());
        var churnRecords = dynamics12.ChurnEvents.Select(c => (c.Source!.ObjectKind, c.Source.SourceRecordId));
        Assert.Empty(churnRecords.Intersect(bound));

        // Unresolved values are left out, never written as observed zeros.
        var stocks = CommercialDynamics("06-segment-economics.json").SegmentStockStates;
        Assert.DoesNotContain(stocks, s => s.SegmentName == "E-commerce Fulfillment");
        foreach (var stock in stocks)
        {
            foreach (var value in new[] { stock.InitialProspectPool, stock.InitialClientCount, stock.ArpuPerMonth, stock.ImplementationFee,
                         stock.ServicingCostPerClient, stock.InfraFixedCostPerMonth, stock.InfraCostPerClient, stock.RdDragPerMonth })
                Assert.True(value > 0, $"{stock.SegmentName}: a stock-state number is 0; an unresolved value must leave the whole row out.");
            Assert.All(stock.ModulePenetrations, m => Assert.True(m.Penetration > 0 && m.ArpuPerMonth > 0));
        }

        Assert.Empty(dynamics12.ArrivalCounts);   // CRM record creation is not a firm birth (requirement 8)
        Assert.DoesNotContain(dynamics11.Firms, f => f.Source!.SourceRecordId == "001EX000000B003AAA");   // unresolved segment
        Assert.DoesNotContain(dynamics11.Firms, f => f.Source!.SourceRecordId == "001EX000000B007AAA");   // merged duplicate
    }

    [Fact]
    public void Fragments_carry_exactly_the_normalized_rows_and_relationships_reconcile_with_churn()
    {
        var artifact = Commercial();
        var firms = CommercialDynamics("11-firms-and-relationships.json").Firms.ToDictionary(f => f.Source!.SourceRecordId);
        var facts = CommercialDynamics("12-historical-facts.json");
        var normalizedFirms = Rows(artifact, "firms");
        Assert.Equal(normalizedFirms.Count, firms.Count);

        foreach (var row in normalizedFirms)
        {
            var fragment = firms[Text(row["sourceId"])];
            Assert.Equal(Text(row["name"]), fragment.Name);
            Assert.Equal(Text(row["segment"]!["name"]), fragment.SegmentName);
            var edges = Rows(artifact, "relationships").Where(r => Text(r["firmSourceId"]) == Text(row["sourceId"])).ToList();
            Assert.Equal(edges.Count, fragment.Relationships.Count);
            foreach (var edge in edges)
            {
                var seed = fragment.Relationships.Single(r => r.ProductName == Text(edge["product"]));
                Assert.Equal(Text(edge["acquisitionDate"]), seed.AcquisitionDate);
                Assert.Equal(edge["endDate"] is null ? null : Text(edge["endDate"]), seed.EndDate);
                Assert.Equal(Strings(edge["sourceIds"]).Order(), seed.Sources.Select(s => s.SourceRecordId).Order());
                Assert.True(Math.Abs((decimal)seed.PriceLevel!.Value - Dec(edge["priceLevel"])) <= 0.0001m);
                Assert.InRange(seed.PriceLevel!.Value, 0.5, 1.2);   // the dataset 11 sanity band
                // F9: contracted monthly (reporting currency) over the list price row.
                AssertMoney(Math.Round(Dec(edge["contractedMonthlyReporting"]) / Dec(edge["listMonthly"]), 4), edge["priceLevel"], "price level");
            }
        }

        // Ended relationships and churn events reconcile in both directions, and one evidence level is used throughout.
        var churnRows = Rows(artifact, "churnEvents");
        Assert.NotEmpty(churnRows);
        Assert.Single(churnRows.Select(c => Text(c["level"])).Distinct());
        Assert.Contains(Text(churnRows[0]["level"]), new[] { "logo", "product" });
        var ended = Rows(artifact, "relationships").Where(r => r["endDate"] is not null).ToList();
        Assert.Equal(ended.Count, churnRows.Count);
        foreach (var churn in churnRows)
        {
            var edge = ended.Single(r => Text(r["firmSourceId"]) == Text(churn["firmSourceId"]));
            Assert.Equal(Text(edge["endDate"]), Text(churn["effectiveDate"]));
            var seed = facts.ChurnEvents.Single(c => c.FirmSource!.SourceRecordId == Text(churn["firmSourceId"]));
            Assert.Equal(Text(churn["effectiveDate"]), seed.EffectiveDate);
            Assert.Equal(Text(churn["segment"]), seed.SegmentName);
            Assert.Equal(Text(churn["sourceId"]), seed.Source!.SourceRecordId);
            Assert.Null(seed.RecordedDate);   // ContractHistory is unreadable: nothing honest to backdate to
        }

        foreach (var deal in Rows(artifact, "deals"))
            Assert.Equal(Text(deal["accountId"]), facts.Opportunities.Single(o => o.Source!.SourceRecordId == Text(deal["sourceId"])).FirmSource!.SourceRecordId);
    }

    [Fact]
    public void CRM_population_growth_and_missing_market_exposure_stay_labelled_gaps_or_reviewed_assumptions()
    {
        var artifact = Commercial();
        var gaps = Nodes(artifact["gaps"]).ToDictionary(g => Text(g["key"]));
        foreach (var key in new[]
                 {
                     "firmBirthsFromCrmCreation", "prospectPoolIsNotCrmList", "marketWideConversionExposure",
                     "channelReachAndAttribution", "competitorAffinity", "contractHistory"
                 })
        {
            Assert.True(gaps.ContainsKey(key), $"gaps lacks '{key}'.");
            Assert.Contains(Text(gaps[key]["status"]), new[] { "gap", "assumption" });
            Assert.False(string.IsNullOrWhiteSpace(Text(gaps[key]["reason"])));
        }

        Assert.False(artifact["calibrationPopulation"]!["arrivals"]!["loaded"]!.GetValue<bool>());
        Assert.Empty(CommercialDynamics("12-historical-facts.json").ArrivalCounts);

        // The prospect pool in the stock state is a reviewed assumption, not the CRM's account count.
        var pool = CommercialRule("DEC-assumption-prospect-pool-regional-3pl");
        Assert.Equal("assumption", Text(pool["class"]));
        Assert.Equal("marketPopulation", Text(pool["outsideEvidenceQuantity"]));
        Assert.Equal("accepted", EffectiveState(pool));
        var stock = Assert.Single(CommercialDynamics("06-segment-economics.json").SegmentStockStates);
        Assert.Equal(pool["proposedValue"]!["initialProspectPool"]!.GetValue<double>(), stock.InitialProspectPool);
        var costs = CommercialRule("DEC-assumption-operating-costs-regional-3pl");
        Assert.Equal("operatingCosts", Text(costs["outsideEvidenceQuantity"]));
        Assert.Equal(costs["proposedValue"]!["servicingCostPerClient"]!.GetValue<double>(), stock.ServicingCostPerClient);
        Assert.Equal(costs["proposedValue"]!["rdDragPerMonth"]!.GetValue<double>(), stock.RdDragPerMonth);
        var crmProspects = RawRecords("Account").Values.Count(a => Text(a["Type"]) == "Prospect");
        Assert.NotEqual((double)crmProspects, stock.InitialProspectPool);

        // Every quantity with no source in the connected systems is one of the PRD5 outside-evidence quantities.
        foreach (var gap in gaps.Values.Where(g => g["quantity"] is not null))
            Assert.Contains(Text(gap["quantity"]), OutsideEvidenceQuantities);
    }

    [Fact]
    public void The_guide_places_commercial_normalization_between_portfolio_selection_and_the_review_and_the_brief_has_the_slot()
    {
        var guide = ReadRepoFile(GuidePath);
        var portfolio = guide.IndexOf("## 8. Portfolio selection", StringComparison.Ordinal);
        var commercial = guide.IndexOf("## 9. Commercial normalization", StringComparison.Ordinal);
        var review = guide.IndexOf("## 10. Consequential-decision review", StringComparison.Ordinal);
        var loading = guide.IndexOf("## 11. Assembling and loading", StringComparison.Ordinal);
        Assert.True(portfolio >= 0 && commercial > portfolio && review > commercial && loading > review,
            "Commercial normalization must be section 9: after portfolio selection, before the review (10) and loading (11).");

        var linked = RelativeLinkTargets(guide, GuidePath).Select(t => t.ResolvedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { CommercialReadmePath, CommercialArtifactPath })
            Assert.Contains(Path.GetFullPath(RepoPath(path)), linked);

        var section = guide[commercial..review];
        Assert.Contains("tier C or D", section);
        Assert.Contains("Skip it", section);
        Assert.Contains("register entries", section);
        Assert.Contains("Commercial normalization:", guide[..portfolio]);           // the brief slot
        Assert.Contains("commercial classifications and conversion rules", guide[review..loading]);
    }

    [Theory]
    [InlineData("06-segment-economics.md")]
    [InlineData("11-firms-and-relationships.md")]
    [InlineData("12-historical-facts.md")]
    public void The_commercial_datasets_point_at_the_normalization_procedure(string fileName)
    {
        var content = ReadRepoFile(DatasetPath(fileName));
        Assert.Contains("](../commercial/README.md)", content);
        Assert.Contains("normalized", content);
    }

    [Fact]
    public void The_commercial_procedure_documents_every_deal_type_formula_output_and_stable_key()
    {
        var readme = ReadRepoFile(CommercialReadmePath);
        foreach (var name in CommercialDealTypes.Concat(CommercialFormulaKeys).Concat(new[]
                 {
                     "NoDecision", "unresolved", "sourceCounts", "segmentTotals", "calibrationPopulation", "normalized",
                     "excluded", "gaps", "currency", "crossSellModuleProductName", "recordedDate", "logo", "product",
                     "priceLevel", "sourceRecordId", "Subscription__c", "expectedClose"
                 }))
            Assert.Contains(name, readme);
        foreach (var key in CommercialRegister()["decisions"]!.AsArray().Select(d => Text(d!["key"])).Where(k => k.StartsWith("RULE-")))
            Assert.Contains(key, readme);
        Assert.Contains("stable", readme, StringComparison.OrdinalIgnoreCase);

        var fixture = ReadRepoFile(CommercialFixtureReadmePath);
        foreach (var name in new[] { "ContractHistory", "renewal", "services", "three-year", "EUR", "Tidewater", "sourceCounts", "duplicate" })
            Assert.Contains(name, fixture, StringComparison.OrdinalIgnoreCase);
        for (var i = 1; i <= 7; i++) Assert.Contains($"| {i}.", fixture);
    }

    // ---------- PRD7: append new tests above this line ----------

    // ---------- PRD8: calibration readiness ----------
    // docs/onboarding/readiness/: the procedure, an agent-side readiness artifact for Harborline (the API
    // report plus source reconciliation, verdict and sensitivity comparisons) and the walkthrough. The
    // artifact's report is parsed into the CLI's own readiness DTO with unknown members disallowed, and its
    // numbers are recomputed from the PRD7 normalization artifact and fragments it reconciles against.

    private const string ReadinessDirectory = "docs/onboarding/readiness";
    private const string ReadinessReadmePath = ReadinessDirectory + "/README.md";
    private const string ReadinessArtifactPath = ReadinessDirectory + "/calibration-readiness.example.json";
    private const string ReadinessFixtureReadmePath = ReadinessDirectory + "/fixture/README.md";
    private const string ReadinessApiTestsPath = "tests/ProductManager.Api.Tests/CalibrationReadinessTests.cs";
    private const double DaysPerMonth = 30.4375;

    private static readonly string[] ReadinessStatuses = ["Ready", "NotReady", "Unfitted", "Asserted"];
    private static readonly string[] ReadinessOveralls = ["Ready", "Exploratory"];
    private static readonly string[] HoldoutStatuses = ["Compared", "Unsupported"];
    private static readonly string[] PriorEvidenceBases = ["Independent", "Observations"];

    private static readonly string[] ReadinessIssueCodes =
    [
        "PriorEvidenceUnknown", "MissingAcquisitionDate", "ChurnWithoutEndDate", "LogoLevelChurn",
        "UnsegmentedEvidence", "ExposureApproximated", "ArrivalMonthsMissing", "DuplicateArrivalMonth",
        "LedgerChangedSinceRun"
    ];

    private static readonly string[] ReconciliationFigures =
    [
        "importedFirms", "activeCustomers", "moduleHolders", "closedNewLogoDeals", "churnEvents", "clientMonths",
        "baseArr", "moduleArr", "implementationFee", "monthlyObservations"
    ];

    private static readonly string[] ReconciliationReasons =
    [
        "syntheticFill", "unresolved", "excludedByRule", "excludedByAsOfCut", "open", "notLoaded",
        "edgeVersusLogoExposure", "rounding"
    ];

    private static JsonNode Readiness() => ReadJson(ReadinessArtifactPath);

    private static CalibrationReadinessDto ReadinessReport(JsonNode artifact)
    {
        try
        {
            return JsonSerializer.Deserialize<CalibrationReadinessDto>(artifact["report"]!.ToJsonString(),
                StrictSeedJsonOptions)!;
        }
        catch (JsonException ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"{ReadinessArtifactPath} report does not deserialize into the CLI's CalibrationReadinessDto " +
                $"with unknown members disallowed: {ex.Message}");
        }
    }

    private static HashSet<string> RegisterKeys() =>
        Decisions(Register()).Concat(Decisions(CommercialRegister())).Select(d => Text(d["key"])).ToHashSet();

    [Fact]
    public void The_readiness_vocabulary_the_docs_teach_is_exactly_the_closed_sets_the_CLI_mirrors()
    {
        Assert.Equal(ReadinessStatuses.Order(), Enum.GetNames<CalibrationReadinessStatus>().Order());
        Assert.Equal(ReadinessOveralls.Order(), Enum.GetNames<CalibrationReadinessOverall>().Order());
        Assert.Equal(ReadinessIssueCodes.Order(), Enum.GetNames<ReadinessIssueCode>().Order());
        Assert.Equal(HoldoutStatuses.Order(), Enum.GetNames<HoldoutComparisonStatus>().Order());
        Assert.Equal(PriorEvidenceBases.Order(), Enum.GetNames<PriorEvidenceBasis>().Order());
    }

    [Fact]
    public void The_readiness_procedure_documents_every_status_issue_code_and_requirement()
    {
        var readme = ReadRepoFile(ReadinessReadmePath);
        foreach (var name in ReadinessStatuses.Concat(ReadinessOveralls).Concat(HoldoutStatuses)
                     .Concat(PriorEvidenceBases).Concat(ReadinessIssueCodes).Concat(new[]
                     {
                         "priorEvidence", "observedFrom", "observedThrough", "recordedThrough", "sourceCalibrationRunId",
                         "observationsRecordedAfter", "--observations-recorded-after", "prior-evidence-reuse",
                         "exit code 6", "409", "pm calibrate readiness", "--holdout-through", "--output", "--json",
                         "populations", "exclusions", "arrivalObservations", "asserted", "notes", "overallReasons",
                         "sourceCounts", "calibrationPopulation", "segmentTotals", "normalized", "tolerance",
                         "synthetic", "unresolved", "reconciliation", "churn.level", "logo", "product",
                         "count: 0", "missing", "sensitivity", "pm assumptions branch", "pm sim compare",
                         "smoke", "exploratory", "roadmap-only", "validated forecast", "forecast accuracy",
                         "GET /api/calibration-runs/{id}/input", "units", "denominator"
                     }))
            Assert.Contains(name, readme, StringComparison.OrdinalIgnoreCase);

        // Requirement sections are named so a reader can find them.
        for (var requirement = 1; requirement <= 8; requirement++)
            Assert.Contains($"requirement {requirement}", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_readiness_example_report_parses_into_the_CLI_DTO_and_uses_only_defined_exact_members()
    {
        var artifact = Readiness();
        Assert.Equal(1, artifact["readinessArtifactVersion"]!.GetValue<int>());
        var report = ReadinessReport(artifact);

        // The DTO parse is case-insensitive for enum strings; the docs must spell members exactly.
        var raw = artifact["report"]!;
        Assert.Contains(Text(raw["overall"]), ReadinessOveralls);
        foreach (var p in Nodes(raw["parameters"]))
        {
            Assert.Contains(Text(p["status"]), ReadinessStatuses.Where(s => s != "Asserted"));
            foreach (var issue in Nodes(p["issues"])) Assert.Contains(Text(issue["code"]), ReadinessIssueCodes);
            if (p["priorEvidence"] is { } evidence) Assert.Contains(Text(evidence["basis"]), PriorEvidenceBases);
        }

        foreach (var a in Nodes(raw["asserted"])) Assert.Equal("Asserted", Text(a["status"]));
        foreach (var c in Nodes(raw["holdout"]!["comparisons"])) Assert.Contains(Text(c["status"]), HoldoutStatuses);
        foreach (var e in Nodes(raw["exclusions"])) Assert.Contains(Text(e["kind"]), Enum.GetNames<FactEventKind>());
        Assert.NotEmpty(report.Parameters);
        Assert.NotEmpty(report.Notes);
    }

    [Fact]
    public void The_report_statuses_follow_its_own_rules_and_its_posteriors_follow_from_the_fit()
    {
        var report = ReadinessReport(Readiness());

        foreach (var p in report.Parameters)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Units), $"{p.Key}: every rate carries units.");
            Assert.False(string.IsNullOrWhiteSpace(p.Denominator), $"{p.Key}: every rate carries its denominator.");
            var codes = p.Issues.Select(i => i.Code).ToList();
            Assert.Equal(p.Status != CalibrationReadinessStatus.Unfitted && p.PriorEvidence is null,
                codes.Contains(ReadinessIssueCode.PriorEvidenceUnknown));

            switch (p.Status)
            {
                case CalibrationReadinessStatus.Ready:
                    Assert.Empty(p.Issues);
                    Assert.NotNull(p.Posterior);
                    Assert.Null(p.UnfittedReason);
                    break;
                case CalibrationReadinessStatus.NotReady:
                    Assert.NotEmpty(p.Issues);
                    Assert.NotNull(p.Posterior);
                    break;
                case CalibrationReadinessStatus.Unfitted:
                    Assert.Null(p.Posterior);
                    Assert.Null(p.PosteriorMean);
                    Assert.False(string.IsNullOrWhiteSpace(p.UnfittedReason), $"{p.Key}: an unfitted parameter says why.");
                    break;
                default:
                    Assert.Fail($"{p.Key}: a fitted or unfitted parameter is never '{p.Status}'.");
                    break;
            }

            if (p.Posterior is not { Type: DistributionType.Beta } posterior) continue;

            // Beta-Binomial (WinRate) and the client-month updates the engine documents; a Point prior counts as 50.
            var (a, b) = p.Prior!.Type == DistributionType.Point
                ? (p.Prior.P1 * 50, (1 - p.Prior.P1) * 50)
                : (p.Prior.P1, p.Prior.P2);
            var (expectedA, expectedB) = p.FlowKind == SimulationFlowKind.WinRate
                ? (a + p.Successes!.Value, b + p.Failures!.Value)
                : (a + p.Successes!.Value, b + p.Exposure!.Value - p.Successes.Value);
            Assert.Equal(expectedA, posterior.P1, 3);
            Assert.Equal(expectedB, posterior.P2, 3);
            Assert.Equal(posterior.P1 / (posterior.P1 + posterior.P2), p.PosteriorMean!.Value, 4);
        }

        bool allReady = report.Parameters.Count > 0
                        && report.Parameters.All(p => p.Status == CalibrationReadinessStatus.Ready)
                        && report.Asserted.Count == 0;
        Assert.Equal(allReady ? CalibrationReadinessOverall.Ready : CalibrationReadinessOverall.Exploratory, report.Overall);
        Assert.Equal(allReady, report.OverallReasons.Count == 0);
        Assert.Contains(report.Notes, n => n.Contains("not reconstruct", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Notes, n => n.Contains("current state", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Notes, n => n.Contains("forecast accuracy", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Client-months of one segment's relationships as the engine cuts them (30.4375 days a month, spans clipped at as-of).</summary>
    private static double ClientMonths(JsonNode commercial, string segment, DateOnly asOf, bool baseOnly = false)
    {
        var firmSegment = Rows(commercial, "firms").ToDictionary(f => Text(f["sourceId"]), f => Text(f["segment"]!["name"]));
        double months = 0;
        foreach (var edge in Rows(commercial, "relationships"))
        {
            if (firmSegment[Text(edge["firmSourceId"])] != segment) continue;
            if (baseOnly && Text(edge["kind"]) != "base") continue;
            var start = Day(edge["acquisitionDate"]);
            var end = edge["endDate"] is { } e && Day(e) < asOf ? Day(e) : asOf;
            months += (end.DayNumber - start.DayNumber) / DaysPerMonth;
        }

        return months;
    }

    [Fact]
    public void Report_populations_equal_the_PRD7_stable_keys_they_are_reconciled_against()
    {
        var commercial = Commercial();
        var report = ReadinessReport(Readiness());
        Assert.Equal(CommercialAsOf, report.AsOfDate);
        var totals = Nodes(commercial["segmentTotals"]).ToDictionary(t => Text(t["segment"]));
        var winRate = commercial["calibrationPopulation"]!["winRate"]!;
        Assert.Equal(Nodes(commercial["segmentTotals"]).Count(), report.Populations.Count);

        foreach (var pop in report.Populations)
        {
            var segment = pop.SegmentName!;
            var firms = Rows(commercial, "firms").Where(f => Text(f["segment"]!["name"]) == segment).ToList();
            Assert.Equal(firms.Count, pop.ObservedFirms + pop.InferredFirms + pop.SyntheticFirms);
            Assert.Equal(0, pop.SyntheticFirms);

            var total = totals[segment];
            if (Text(total["status"]) == "resolved")
            {
                Assert.Equal(total["activeCustomers"]!.GetValue<int>(), pop.ActiveCustomers);
                foreach (var module in Nodes(total["modules"]))
                    Assert.Equal(module["holders"]!.GetValue<int>(),
                        pop.ModuleHolders.Single(m => m.Name == Text(module["product"])).Holders);
            }

            var outcomes = winRate[segment]!;
            Assert.Equal(outcomes["won"]!.AsArray().Count, pop.Decisions.Won);
            Assert.Equal(outcomes["lost"]!.AsArray().Count, pop.Decisions.Lost);
            Assert.Equal(outcomes["noDecision"]!.AsArray().Count, pop.Decisions.NoDecision);
            Assert.Equal(0, pop.Decisions.CrossSellWon + pop.Decisions.CrossSellLost + pop.Decisions.CrossSellNoDecision);

            Assert.Equal(Rows(commercial, "churnEvents").Count(c => Text(c["segment"]) == segment), pop.ChurnEvents);
            Assert.Equal(0, pop.ProductlessChurnEvents);
            var edges = Rows(commercial, "relationships")
                .Where(r => firms.Any(f => Text(f["sourceId"]) == Text(r["firmSourceId"]))).ToList();
            Assert.Equal(edges.Count, pop.Relationships);
            Assert.Equal(edges.Count(r => r["endDate"] is not null), pop.EndedRelationships);
            Assert.Equal(edges.Count(r => r["acquisitionDate"] is null), pop.RelationshipsWithoutAcquisitionDate);
            Assert.Equal(edges.GroupBy(r => Text(r["firmSourceId"])).Count(g => g.Count() > 1), pop.MultiProductFirms);
            Assert.Equal(ClientMonths(commercial, segment, report.AsOfDate), pop.ClientMonths, 3);
        }

        // Nothing was cut by the as-of date or added since; every decision, churn and arrival fact was read.
        Assert.All(report.Exclusions, e =>
            Assert.Equal((0, 0, 0, 0), (e.RecordedAfterAsOf, e.RecordedOnOrBeforeWindowStart, e.Unattributed, e.AddedAfterRun)));
        Assert.Empty(report.ArrivalObservations);   // calibrationPopulation.arrivals.loaded is false
    }

    [Fact]
    public void The_readiness_example_parameters_match_the_loaded_PRD7_fragments()
    {
        var commercial = Commercial();
        var report = ReadinessReport(Readiness());
        var facts = CommercialDynamics("12-historical-facts.json");
        var firmSegment = Rows(commercial, "firms").ToDictionary(f => Text(f["sourceId"]), f => Text(f["segment"]!["name"]));
        var bySegment = report.Populations.ToDictionary(p => p.SegmentName!, p => p.ClientSegmentId);

        foreach (var (segment, id) in bySegment)
        {
            var decided = facts.Opportunities
                .Where(o => o.Outcome is "Won" or "Lost" && firmSegment[o.FirmSource!.SourceRecordId] == segment).ToList();
            var parameter = report.Parameters.SingleOrDefault(p => p.FlowKind == SimulationFlowKind.WinRate && p.ClientSegmentId == id);
            if (parameter is null)
            {
                Assert.Empty(decided);
                continue;
            }

            Assert.Equal(decided.Count, parameter.Observations);
            Assert.Equal(decided.Count(o => o.Outcome == "Won"), parameter.Successes);
            Assert.Equal(decided.Count(o => o.Outcome == "Lost"), parameter.Failures);
            Assert.Equal(facts.Opportunities.Count(o => o.Outcome == "NoDecision" && firmSegment[o.FirmSource!.SourceRecordId] == segment),
                parameter.Excluded);
            Assert.Equal(decided.Min(o => o.DecidedDate), parameter.ObservedFrom?.ToString("yyyy-MM-dd"));
            Assert.Equal(decided.Max(o => o.DecidedDate), parameter.ObservedThrough?.ToString("yyyy-MM-dd"));
        }

        // The churn hazard's exposure is the population's client-months; its numerator is the one churn fact.
        var churn = report.Parameters.Single(p => p.FlowKind == SimulationFlowKind.ChurnHazard);
        Assert.Equal(facts.ChurnEvents.Count, churn.Successes);
        Assert.Equal(report.Populations.Single(p => p.ClientSegmentId == churn.ClientSegmentId).ClientMonths, churn.Exposure!.Value, 6);

        // The PRD7 fixture has no cross-sell evidence and no arrival counts: honest result, no fit, no fabricated zero.
        Assert.Empty(facts.ArrivalCounts);
        var crossSell = report.Parameters.Single(p => p.FlowKind == SimulationFlowKind.CrossSellRate);
        Assert.Equal(CalibrationReadinessStatus.Unfitted, crossSell.Status);
        Assert.Equal(0, crossSell.Observations);
        Assert.DoesNotContain(facts.Opportunities, o => o.CrossSellModuleProductName is not null);
    }

    /// <summary>The system side of a reconciliation figure, read from the report (or, for money, the loaded stock state).</summary>
    private static double? SystemFigure(string figure, string segment, string? module, CalibrationReadinessDto report)
    {
        var pop = report.Populations.Single(p => p.SegmentName == segment);
        var stock = CommercialDynamics("06-segment-economics.json").SegmentStockStates.SingleOrDefault(s => s.SegmentName == segment);
        return figure switch
        {
            "importedFirms" => pop.ObservedFirms + pop.InferredFirms + pop.SyntheticFirms,
            "activeCustomers" => pop.ActiveCustomers,
            "moduleHolders" => pop.ModuleHolders.Single(m => m.Name == module).Holders,
            "closedNewLogoDeals" => pop.Decisions.Won + pop.Decisions.Lost + pop.Decisions.NoDecision,
            "churnEvents" => pop.ChurnEvents,
            "clientMonths" => pop.ClientMonths,
            "baseArr" => stock is null ? 0 : stock.ArpuPerMonth * stock.InitialClientCount * 12,
            "moduleArr" => stock?.ModulePenetrations.Where(m => m.ModuleProductName == module)
                .Sum(m => m.Penetration * stock.InitialClientCount * m.ArpuPerMonth * 12) ?? 0,
            "implementationFee" => stock?.ImplementationFee ?? 0,
            "monthlyObservations" => report.ArrivalObservations.Where(a => a.ClientSegmentId == pop.ClientSegmentId).Sum(a => a.ObservedMonths),
            _ => throw new Xunit.Sdk.XunitException($"Unknown reconciliation figure '{figure}'.")
        };
    }

    /// <summary>The source side of a reconciliation figure, read from the PRD7 artifact's stable keys (null = unresolved there).</summary>
    private static double? SourceFigure(string figure, string segment, string? module, JsonNode commercial, DateOnly asOf)
    {
        var total = Nodes(commercial["segmentTotals"]).Single(t => Text(t["segment"]) == segment);
        bool resolved = Text(total["status"]) == "resolved";
        var outcomes = commercial["calibrationPopulation"]!["winRate"]![segment]!;
        int Count(string key) => outcomes[key]!.AsArray().Count;
        return figure switch
        {
            "importedFirms" => Rows(commercial, "firms").Count(f => Text(f["segment"]!["name"]) == segment),
            "activeCustomers" => resolved ? total["activeCustomers"]!.GetValue<int>() : null,
            "moduleHolders" => resolved ? Nodes(total["modules"]).Single(m => Text(m["product"]) == module)["holders"]!.GetValue<int>() : null,
            "closedNewLogoDeals" => Count("won") + Count("lost") + Count("noDecision") + Count("unresolved"),
            "churnEvents" => Rows(commercial, "churnEvents").Count(c => Text(c["segment"]) == segment),
            // Logo level (PRD7 churn.level): a firm counts once per month however many products it holds.
            "clientMonths" => ClientMonths(commercial, segment, asOf, baseOnly: true),
            "baseArr" => resolved ? total["baseArr"]!.GetValue<double>() : null,
            "moduleArr" => resolved ? Nodes(total["modules"]).Single(m => Text(m["product"]) == module)["arr"]!.GetValue<double>() : null,
            "implementationFee" => resolved
                ? Nodes(total["implementationFee"]!["inputs"]).Average(i => i["oneTimeReporting"]!.GetValue<double>())
                : null,
            "monthlyObservations" => commercial["calibrationPopulation"]!["arrivals"]!["loaded"]!.GetValue<bool>() ? null : 0,
            _ => throw new Xunit.Sdk.XunitException($"Unknown reconciliation figure '{figure}'.")
        };
    }

    [Fact]
    public void Every_reconciliation_figure_equals_its_system_and_source_values_and_every_difference_has_a_named_reason()
    {
        var artifact = Readiness();
        var commercial = Commercial();
        var report = ReadinessReport(artifact);
        var figures = Nodes(artifact["reconciliation"]!["figures"]).ToList();
        Assert.NotEmpty(Text(artifact["reconciliation"]!["tolerancePolicy"]));
        Assert.Equal(figures.Count, figures.Select(f => Text(f["id"])).Distinct().Count());
        Assert.Subset(ReconciliationFigures.ToHashSet(), figures.Select(f => Text(f["figure"])).ToHashSet());
        foreach (var required in ReconciliationFigures)
            Assert.Contains(required, figures.Select(f => Text(f["figure"])));
        var keys = RegisterKeys();

        foreach (var f in figures)
        {
            var id = Text(f["id"]);
            var segment = Text(f["segment"]);
            var module = f["module"] is { } m ? Text(m) : null;
            var system = SystemFigure(Text(f["figure"]), segment, module, report);
            var source = SourceFigure(Text(f["figure"]), segment, module, commercial, CommercialAsOf);
            Assert.Equal(system!.Value, f["system"]!.GetValue<double>(), 3);
            Assert.Equal(source is null, f["source"] is null);
            if (source is { } s) Assert.Equal(s, f["source"]!.GetValue<double>(), 3);

            var tolerance = f["tolerance"]!;
            var toleranceValue = tolerance["value"]!.GetValue<double>();
            Assert.False(string.IsNullOrWhiteSpace(Text(tolerance["basis"])), $"{id}: a tolerance states its basis.");
            Assert.Contains(Text(tolerance["kind"]), new[] { "exact", "absolute" });
            if (Text(tolerance["kind"]) == "exact") Assert.Equal(0, toleranceValue);
            else Assert.Matches(@"F\d+|rounding", Text(tolerance["basis"]));

            var reasons = Nodes(f["reasons"]).ToList();
            foreach (var reason in reasons)
            {
                Assert.Contains(Text(reason["reason"]), ReconciliationReasons);
                Assert.False(string.IsNullOrWhiteSpace(Text(reason["ref"])), $"{id}: every reason names what it refers to.");
                if (Text(reason["reason"]) == "unresolved") Assert.Contains(Text(reason["decisionKey"]), keys);
            }

            if (source is null)
            {
                Assert.Equal("unresolvedInSource", Text(f["status"]));
                Assert.Null(f["difference"]);
                continue;
            }

            var difference = system!.Value - source.Value;
            Assert.Equal(difference, f["difference"]!.GetValue<double>(), 3);
            if (Math.Abs(difference) <= toleranceValue)
            {
                Assert.Equal("matched", Text(f["status"]));
                Assert.Empty(reasons);
            }
            else
            {
                // Beyond tolerance: explained only when the named reasons add up to the difference.
                var unexplained = difference - reasons.Sum(r => r["count"]!.GetValue<double>());
                Assert.Equal(unexplained, f["unexplained"]!.GetValue<double>(), 3);
                Assert.Equal(Math.Abs(unexplained) <= toleranceValue ? "explained" : "unexplained", Text(f["status"]));
                Assert.NotEmpty(reasons);
            }
        }
    }

    [Fact]
    public void A_mismatch_that_touches_a_parameter_keeps_it_from_being_presented_as_Ready_however_the_API_rates_it()
    {
        var artifact = Readiness();
        var report = ReadinessReport(artifact);
        var figures = Nodes(artifact["reconciliation"]!["figures"]).ToList();
        var figureIds = figures.Select(f => Text(f["id"])).ToHashSet();
        var verdict = artifact["verdict"]!;
        var entries = Nodes(verdict["parameters"]).ToDictionary(e => Text(e["key"]));
        Assert.Equal(report.Parameters.Select(p => p.Key).Order(), entries.Keys.Order());

        // The deliberate mismatch: a figure the system cannot match exactly, named, explained and blocking.
        var blocking = figures.Where(f => Text(f["status"]) != "matched" && Text(f["effect"]) == "notReady").ToList();
        Assert.NotEmpty(blocking);
        var blocked = new HashSet<string>();
        foreach (var f in blocking)
        {
            Assert.NotEmpty(Nodes(f["reasons"]));
            var affected = Strings(f["affectsParameters"]);
            Assert.NotEmpty(affected);
            foreach (var key in affected) { Assert.Contains(report.Parameters, p => p.Key == key); blocked.Add(key); }
        }

        // A Ready parameter blocked by a figure is the point of the mechanism.
        Assert.Contains(report.Parameters, p => p.Status == CalibrationReadinessStatus.Ready && blocked.Contains(p.Key));

        foreach (var p in report.Parameters)
        {
            var entry = entries[p.Key];
            Assert.Equal(p.Status.ToString(), Text(entry["apiStatus"]));
            var presented = Text(entry["presentedStatus"]);
            Assert.Contains(presented, new[] { "Ready", "NotReady", "Unfitted" });
            if (p.Status != CalibrationReadinessStatus.Ready || blocked.Contains(p.Key))
                Assert.NotEqual("Ready", presented);
            if (p.Status == CalibrationReadinessStatus.Unfitted) Assert.Equal("Unfitted", presented);
            foreach (var cause in Strings(entry["because"]))
                Assert.True(figureIds.Contains(cause) || ReadinessIssueCodes.Contains(cause),
                    $"{p.Key}: '{cause}' is neither a reconciliation figure nor a readiness issue code.");
        }

        Assert.Equal(report.Overall.ToString(), Text(verdict["overall"]));
        Assert.Equal("exploratory", Text(verdict["presentableAs"]));
        Assert.Contains("validated forecast", Text(verdict["statement"]), StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(Nodes(verdict["unsupportedAssumptions"]));

        // Decisions the agent needs are proposals with new keys: it never invents an accepted one.
        var existing = RegisterKeys();
        foreach (var entry in Nodes(artifact["proposedRegisterEntries"]))
        {
            Assert.Matches(DecisionKeyRegex, Text(entry["key"]));
            Assert.DoesNotContain(Text(entry["key"]), existing);
            Assert.Equal("proposed", Text(entry["state"]));
            Assert.All(Strings(entry["blocks"]), key => Assert.Contains(report.Parameters, p => p.Key == key));
        }
    }

    [Fact]
    public void An_evidence_reuse_refusal_names_the_overlap_and_the_resolution_and_matches_the_loaded_deals()
    {
        var artifact = Readiness();
        var report = ReadinessReport(artifact);
        var attempt = Nodes(artifact["refusedAttempts"]).Single();
        Assert.Equal(409, attempt["httpStatus"]!.GetValue<int>());
        Assert.Equal("urn:productmanager:problem:prior-evidence-reuse", Text(attempt["problemType"]));
        Assert.Equal(6, attempt["exitCode"]!.GetValue<int>());
        Assert.False(attempt["stored"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(Text(attempt["resolution"])));

        var overlap = Nodes(attempt["overlaps"]).Single();
        var winRate = report.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate && p.Observations > 1);
        Assert.Equal(winRate.Key, Text(overlap["key"]));
        Assert.Equal("Observations", Text(overlap["priorEvidence"]!["basis"]));
        var declaredFrom = Day(overlap["priorEvidence"]!["observedFrom"]);
        var declaredThrough = Day(overlap["priorEvidence"]!["observedThrough"]);
        Assert.True(declaredFrom <= Day(overlap["overlapFrom"]) && Day(overlap["overlapThrough"]) <= declaredThrough);
        Assert.Equal(winRate.ObservedFrom!.Value, Day(overlap["overlapFrom"]));
        Assert.Equal(winRate.ObservedThrough!.Value, Day(overlap["overlapThrough"]));
        Assert.Equal(winRate.Observations, overlap["overlappingObservations"]!.GetValue<int>());
        Assert.Equal(winRate.Observations, overlap["fitObservations"]!.GetValue<int>());
    }

    [Fact]
    public void Holdout_comparisons_are_unsupported_without_held_out_history_and_never_a_false_pass()
    {
        var report = ReadinessReport(Readiness());
        var holdout = report.Holdout!;
        Assert.Equal(report.AsOfDate.AddDays(1), holdout.HeldOutFrom);
        Assert.True(holdout.HeldOutThrough > report.AsOfDate);
        var fitted = report.Parameters.Where(p => p.Status != CalibrationReadinessStatus.Unfitted).Select(p => p.Key).Order();
        Assert.Equal(fitted, holdout.Comparisons.Select(c => c.Key).Order());

        foreach (var c in holdout.Comparisons)
        {
            if (c.Status == HoldoutComparisonStatus.Unsupported)
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Reason), $"{c.Key}: an unsupported comparison says why.");
                Assert.Null(c.ObservedInsideInterval);
                Assert.Null(c.IntervalLower);
                Assert.Null(c.ObservedRate);
            }
            else
            {
                Assert.True(c.SampleSize > 0, $"{c.Key}: a comparison needs held-out observations.");
            }
        }

        // The fixture's extract ends on the as-of date: nothing is held out, so nothing may compare.
        Assert.All(holdout.Comparisons, c => Assert.Equal(HoldoutComparisonStatus.Unsupported, c.Status));
        Assert.DoesNotContain("pass", holdout.Note.Replace("not a pass", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sensitivity_comparisons_vary_one_asserted_quantity_with_real_commands_and_invent_no_results()
    {
        var artifact = Readiness();
        var report = ReadinessReport(artifact);
        var comparisons = Nodes(artifact["sensitivityComparisons"]).ToList();
        Assert.InRange(comparisons.Count, 3, 5);
        Assert.Equal(comparisons.Count, comparisons.Select(c => Text(c["id"])).Distinct().Count());

        foreach (var c in comparisons)
        {
            var id = Text(c["id"]);
            Assert.False(string.IsNullOrWhiteSpace(Text(c["output"])), $"{id}: names the output compared.");
            Assert.NotEmpty(Strings(c["commands"]));
            Assert.All(Strings(c["commands"]), command => Assert.StartsWith("pm ", command));
            if (c["assertedKey"] is { } key)
            {
                var asserted = report.Asserted.Single(a => a.Key == Text(key));
                var baseline = c["baseline"]!;
                Assert.Equal(asserted.Distribution!.Type.ToString(), Text(baseline["type"]));
                Assert.Equal(asserted.Distribution.P1, baseline["p1"]!.GetValue<double>(), 6);
                Assert.NotEqual(asserted.Distribution.P1, c["variant"]!["p1"]!.GetValue<double>());
            }
            else
            {
                Assert.True(c["variesRoadmap"]!.GetValue<bool>(), $"{id}: varies an asserted key or the roadmap.");
            }

            Assert.Contains(Text(c["status"]), new[] { "planned", "run" });
            if (Text(c["status"]) == "planned")
            {
                Assert.Null(c["simRunIds"]);
                Assert.Null(c["result"]);
            }
            else
            {
                Assert.Equal(2, c["simRunIds"]!.AsArray().Count);
                Assert.NotNull(c["result"]);
            }
        }
    }

    [Fact]
    public void The_guide_links_the_readiness_procedure_and_keeps_model_readiness_apart_from_the_smoke_test()
    {
        var guide = ReadRepoFile(GuidePath);
        var linked = RelativeLinkTargets(guide, GuidePath).Select(t => t.ResolvedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { ReadinessReadmePath, ReadinessArtifactPath })
            Assert.Contains(Path.GetFullPath(RepoPath(path)), linked);

        var smoke = guide.IndexOf("### Smoke simulation (tiers C and D)", StringComparison.Ordinal);
        var readiness = guide.IndexOf("### Model readiness (tiers C and D)", StringComparison.Ordinal);
        var troubleshooting = guide.IndexOf("## 13. Troubleshooting", StringComparison.Ordinal);
        Assert.True(smoke >= 0 && readiness > smoke && troubleshooting > readiness,
            "Model readiness must be its own subsection after the smoke simulation and before troubleshooting.");

        var smokeText = guide[smoke..readiness];
        Assert.DoesNotContain("pm calibrate readiness", smokeText);   // the smoke test never claims readiness
        Assert.Contains("execute", smokeText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Exit 6", smokeText);

        var readinessText = guide[readiness..troubleshooting];
        foreach (var token in new[] { "pm calibrate readiness", "not a smoke test", "evidence sufficiency", "Exploratory", "roadmap-only" })
            Assert.Contains(token, readinessText, StringComparison.OrdinalIgnoreCase);
        AssertCliNamesExist(guide[smoke..troubleshooting], "guide smoke simulation and model readiness");

        var troubleshootingText = guide[troubleshooting..];
        Assert.Contains("Exit 6", troubleshootingText);
        Assert.Contains("prior-evidence-reuse", troubleshootingText);

        var loading = guide.IndexOf("## 11. Assembling and loading", StringComparison.Ordinal);
        var verification = guide.IndexOf("## 12. Verification checklist", StringComparison.Ordinal);
        Assert.Contains("readiness", guide[loading..verification], StringComparison.OrdinalIgnoreCase);   // import package

        var brief = guide[..guide.IndexOf("## 2. Prerequisites", StringComparison.Ordinal)];
        Assert.Contains("Prior evidence:", brief);
        Assert.Contains("priorEvidence", brief);
    }

    private static readonly Regex PmVerbRegex = new(@"\bpm ([a-z][a-z-]*)(?: ([a-z][a-z-]*))?");
    private static readonly Regex OptionRegex = new(@"(?<![\w-])(--[a-z][a-z0-9-]*)");

    /// <summary>The text of every command definition, so a documented verb or option can be checked without invoking the CLI.</summary>
    private static string CliCommandSources() =>
        string.Join("\n", Directory.GetFiles(RepoPath("src/ProductManager.Cli/Commands"), "*.cs").Select(File.ReadAllText));

    [Theory]
    [InlineData(ReadinessReadmePath)]
    [InlineData(ReadinessFixtureReadmePath)]
    [InlineData(ReadinessArtifactPath)]
    [InlineData(DatasetDirectory + "/08-assumption-sets.md")]
    [InlineData(DatasetDirectory + "/12-historical-facts.md")]
    public void Every_pm_verb_and_option_the_readiness_docs_name_exists_in_the_CLI_sources(string path) =>
        AssertCliNamesExist(ReadRepoFile(path), path);

    private static void AssertCliNamesExist(string content, string where)
    {
        var sources = CliCommandSources();
        string[] leaves = ["seed"];
        string[] notCliOptions = ["--help", "--project", "--filter"];   // dotnet's own: "dotnet run --project ... --", "dotnet test --filter"

        foreach (Match m in PmVerbRegex.Matches(content))
        {
            var group = m.Groups[1].Value;
            Assert.True(sources.Contains($"new Command(\"{group}\"", StringComparison.Ordinal),
                $"{where}: 'pm {group}' names a command group that does not exist.");
            if (m.Groups[2].Success && !leaves.Contains(group))
                Assert.True(sources.Contains($"new Command(\"{m.Groups[2].Value}\"", StringComparison.Ordinal),
                    $"{where}: 'pm {group} {m.Groups[2].Value}' names a verb that does not exist under '{group}'.");
        }

        foreach (Match m in OptionRegex.Matches(content))
            Assert.True(sources.Contains($"\"{m.Groups[1].Value}\"", StringComparison.Ordinal) || notCliOptions.Contains(m.Groups[1].Value),
                $"{where}: option '{m.Groups[1].Value}' is not defined by any command.");
    }

    private static void AssertPriorEvidenceIsValid(SeedPriorEvidence e, string source, string where)
    {
        Assert.Contains(e.Basis, PriorEvidenceBases);
        var from = AssertIsoDate(e.ObservedFrom, source, $"{where} observedFrom");
        var through = AssertIsoDate(e.ObservedThrough, source, $"{where} observedThrough");
        var recorded = AssertIsoDate(e.RecordedThrough, source, $"{where} recordedThrough");

        if (e.Basis == "Independent")
        {
            Assert.True(from is null && through is null && recorded is null,
                $"{source}: {where} is Independent, so it has no observation period or recorded cut-off.");
            return;
        }

        Assert.Equal(from is null, through is null);
        Assert.True(from is not null || recorded is not null,
            $"{source}: {where} is derived from observations, so it needs observedFrom/observedThrough or recordedThrough.");
        if (from is not null)
        {
            Assert.True(from <= through, $"{source}: {where} observedFrom is after observedThrough.");
            Assert.True(through <= LatestAllowedRecordedDate, $"{source}: {where} observation period ends in the future.");
        }
    }

    private static IEnumerable<(string Source, SeedParameter Parameter)> EngineParameters(SeedFile seed, string source) =>
        (seed.MarketDynamics?.AssumptionSets ?? []).SelectMany(s => s.Parameters)
            .Where(p => p.FlowKind is not null).Select(p => (source, p));

    [Fact]
    public void Dataset_08_and_the_example_seed_declare_prior_evidence_on_every_engine_parameter()
    {
        var examples = JsonBlocks("08-assumption-sets.md")
            .Select((json, i) => (Source: $"{DatasetPath("08-assumption-sets.md")} json block #{i + 1}", Seed: ParseSeedFile(json, "08")))
            .Append((Source: ExampleSeedPath, Seed: LoadExampleSeed())).ToList();

        var engine = examples.SelectMany(e => EngineParameters(e.Seed, e.Source)).ToList();
        Assert.NotEmpty(engine);
        foreach (var (source, parameter) in engine)
        {
            var where = $"{parameter.FlowKind} parameter of '{parameter.SegmentName}'";
            Assert.True(parameter.PriorEvidence is not null,
                $"{source}: {where} must declare priorEvidence (Independent, or Observations with its period).");
        }

        foreach (var (source, seed) in examples)
            foreach (var parameter in (seed.MarketDynamics?.AssumptionSets ?? []).SelectMany(s => s.Parameters)
                         .Where(p => p.PriorEvidence is not null))
                AssertPriorEvidenceIsValid(parameter.PriorEvidence!, source, $"{parameter.FlowKind ?? parameter.ChoiceKind ?? parameter.Key} priorEvidence");

        // Both forms are taught: an independent prior, and a win-count prior with the period it counted.
        var all = engine.Select(e => e.Parameter.PriorEvidence!).ToList();
        Assert.Contains(all, e => e.Basis == "Independent");
        Assert.Contains(all, e => e.Basis == "Observations" && e.ObservedFrom is not null);

        var text = ReadRepoFile(DatasetPath("08-assumption-sets.md"));
        foreach (var token in new[]
                 {
                     "priorEvidence", "Independent", "Observations", "--observations-recorded-after", "prior-evidence-reuse",
                     "counts the same", "](../readiness/README.md)", "disjoint"
                 })
            Assert.Contains(token, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("from last year's decided deals", text);
    }

    [Fact]
    public void Datasets_11_and_12_explain_zero_versus_missing_months_churn_level_and_the_history_approximation()
    {
        var dataset12 = ReadRepoFile(DatasetPath("12-historical-facts.md"));
        foreach (var token in new[]
                 {
                     "observed zero", "missing month", "never fabricat", "calibrationPopulation.churn.level",
                     "knowledge", "](../readiness/README.md)", "ArrivalMonthsMissing", "ChurnWithoutEndDate"
                 })
            Assert.Contains(token, dataset12, StringComparison.OrdinalIgnoreCase);

        var dataset11 = ReadRepoFile(DatasetPath("11-firms-and-relationships.md"));
        foreach (var token in new[] { "](../readiness/README.md)", "MissingAcquisitionDate", "ChurnWithoutEndDate" })
            Assert.Contains(token, dataset11, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_citations_into_CalibrationInputBuilder_point_at_the_code_they_describe()
    {
        var builder = File.ReadAllLines(RepoPath("src/ProductManager.Api/Simulation/CalibrationInputBuilder.cs"));
        string[] expected = ["spans", "RelationshipSpan", "arrival", "Arrival", "Exposure", "Remaining approximation", "AcquisitionDate"];
        var citation = new Regex(@"CalibrationInputBuilder\.cs:(\d+)(?:-(\d+))?");
        var checkedCitations = 0;

        foreach (var path in DatasetFileNames.Select(DatasetPath).Append(ReadinessReadmePath))
            foreach (Match m in citation.Matches(ReadRepoFile(path)))
            {
                int from = int.Parse(m.Groups[1].Value), to = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : from;
                Assert.True(from >= 1 && to <= builder.Length && from <= to, $"{path}: {m.Value} is outside the file.");
                var window = string.Join("\n", builder.Skip(from - 1).Take(to - from + 1));
                Assert.True(expected.Any(window.Contains), $"{path}: {m.Value} no longer points at spans, arrivals or exposure code.");
                checkedCitations++;
            }

        Assert.True(checkedCitations >= 6, "The datasets cite CalibrationInputBuilder for spans, arrivals and exposure.");
    }

    [Fact]
    public void The_readiness_walkthrough_maps_every_PRD8_acceptance_criterion_to_an_API_test_or_the_artifact()
    {
        var fixture = ReadRepoFile(ReadinessFixtureReadmePath);
        for (var i = 1; i <= 6; i++) Assert.Contains($"| {i}.", fixture);

        var apiTests = ReadRepoFile(ReadinessApiTestsPath);
        var named = Regex.Matches(fixture, @"`((?:AC\d|R\d(?:_R\d)*)_\w+)`").Select(m => m.Groups[1].Value).Distinct().ToList();
        foreach (var name in named)
            Assert.Contains($"Task {name}(", apiTests);
        foreach (var prefix in new[] { "AC1_", "AC2_", "AC3_", "AC4_" })
            Assert.Contains(named, n => n.StartsWith(prefix, StringComparison.Ordinal));

        foreach (var token in new[] { "reconciliation", "verdict", "Exploratory", "Unsupported", "refusedAttempts" })
            Assert.Contains(token, fixture);
    }

    [Theory]
    [InlineData(ReadinessReadmePath)]
    [InlineData(ReadinessFixtureReadmePath)]
    public void The_new_readiness_prose_uses_plain_ascii_hyphens_and_no_em_dashes(string path)
    {
        var content = ReadRepoFile(path);
        Assert.DoesNotContain('\u2014', content);
        Assert.DoesNotContain('\u2013', content);
    }

    // ---------- PRD8: append new tests above this line ----------
}
