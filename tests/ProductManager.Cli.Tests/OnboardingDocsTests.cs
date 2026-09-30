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
            PortfolioReadmePath, PortfolioFixtureReadmePath
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
        var loading = guide.IndexOf("## 9. Assembling and loading", StringComparison.Ordinal);
        Assert.True(interview >= 0 && portfolio > interview && loading > portfolio,
            "Portfolio selection must be section 8, after the interview and before assembling and loading.");

        var linked = RelativeLinkTargets(guide, GuidePath).Select(t => t.ResolvedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(RepoPath(PortfolioReadmePath)), linked);
        Assert.Contains(Path.GetFullPath(RepoPath(PortfolioMappingPath)), linked);

        Assert.Contains("Portfolio mapping:", guide);
        Assert.Contains("never coin", guide);
        Assert.Contains("PRD5", guide); // the named hook for the consequential-decision review

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
}
