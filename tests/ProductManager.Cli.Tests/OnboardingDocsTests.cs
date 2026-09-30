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
            PortfolioReadmePath, PortfolioFixtureReadmePath, ReviewReadmePath, ReviewExamplePath
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
        var loading = guide.IndexOf("## 10. Assembling and loading", StringComparison.Ordinal);
        Assert.True(interview >= 0 && portfolio > interview && loading > portfolio,
            "Portfolio selection must be section 8, after the interview and before assembling and loading (section 10).");

        var linked = RelativeLinkTargets(guide, GuidePath).Select(t => t.ResolvedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(RepoPath(PortfolioReadmePath)), linked);
        Assert.Contains(Path.GetFullPath(RepoPath(PortfolioMappingPath)), linked);

        Assert.Contains("Portfolio mapping:", guide);
        Assert.Contains("never coin", guide);
        Assert.DoesNotContain("<!-- PRD5", guide); // the hook is now guide section 9 (PRD5 tests below)

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
        var review = guide.IndexOf("## 9. Consequential-decision review", StringComparison.Ordinal);
        var loading = guide.IndexOf("## 10. Assembling and loading", StringComparison.Ordinal);
        Assert.True(portfolio >= 0 && review > portfolio && loading > review,
            "The review must be section 9, after portfolio selection and before assembling and loading.");

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
}
