using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Tests;

/// <summary>
/// Drift guard for the hand-mirrored CLI contract (src/ProductManager.Cli/Client/{Dtos,Enums}.cs).
/// Nothing here talks to a live API: it loads the checked-in openapi/productmanager.json plus the
/// raw source of ProductManagerApiClient.cs and checks three things a rename on the API side could
/// silently break for the CLI (extra JSON properties are ignored by System.Text.Json, not errored):
///
///  (a) every route the client actually calls — extracted from the client's own source text, not
///      hand-copied, so this can't itself drift from the real call sites — exists in the spec;
///  (b) every DTO used as a request/response body has its JsonPropertyName-camelCase properties as
///      a subset of the matching schema's properties, and the schema's required properties (if any)
///      are all present on the DTO;
///  (c) every enum used by a registered DTO has exactly the schema's member names.
///
/// DTOs and enums are matched to schemas via the explicit <see cref="DtoSchemaMap"/> /
/// <see cref="EnumSchemaMap"/> dictionaries below (by design — see task spec item 2): a new
/// endpoint or a newly-referenced nested DTO/enum that isn't registered there fails loudly instead
/// of silently skipping the check.
/// </summary>
public class ApiContractDriftTests
{
    private const string ContractRelativePath = "openapi/productmanager.json";
    private const string ClientSourceRelativePath = "src/ProductManager.Cli/Client/ProductManagerApiClient.cs";

    // DTO record type name (in ProductManager.Cli.Client) -> OpenAPI component schema name.
    // Every DTO that ProductManagerApiClient sends or receives (directly, as a call-site generic
    // argument, or nested inside another registered DTO) must appear here.
    private static readonly Dictionary<string, string> DtoSchemaMap = new()
    {
        ["TokenRequest"] = "TokenRequest",
        ["TokenResponse"] = "TokenResponse",

        ["WorkspaceDto"] = "WorkspaceDto",
        ["CreateWorkspaceRequest"] = "CreateWorkspaceRequest",
        ["UpdateWorkspaceRequest"] = "UpdateWorkspaceRequest",

        ["TeamDto"] = "TeamDto",
        ["TeamCapacityDto"] = "TeamCapacityDto",
        ["CreateTeamRequest"] = "CreateTeamRequest",
        ["UpdateTeamRequest"] = "UpdateTeamRequest",
        ["SetTeamCapacityRequest"] = "SetTeamCapacityRequest",

        ["InitiativeDto"] = "InitiativeDto",
        ["CreateInitiativeRequest"] = "CreateInitiativeRequest",
        ["UpdateInitiativeRequest"] = "UpdateInitiativeRequest",

        ["ProjectDto"] = "ProjectDto",
        ["EffortDemandDto"] = "EffortDemandDto",
        ["CreateProjectRequest"] = "CreateProjectRequest",
        ["UpdateProjectRequest"] = "UpdateProjectRequest",
        ["AddProjectDependencyRequest"] = "AddProjectDependencyRequest",
        ["SetEffortDemandRequest"] = "SetEffortDemandRequest",
        ["LinkFeatureRequest"] = "LinkFeatureRequest",

        ["FeatureDto"] = "FeatureDto",
        ["FeatureDeltaDto"] = "FeatureDeltaDto",
        ["CreateFeatureRequest"] = "CreateFeatureRequest",
        ["UpdateFeatureRequest"] = "UpdateFeatureRequest",
        ["AddFeatureDeltaRequest"] = "AddFeatureDeltaRequest",

        ["RoadmapDto"] = "RoadmapDto",
        ["RoadmapItemDto"] = "RoadmapItemDto",
        ["CreateRoadmapRequest"] = "CreateRoadmapRequest",
        ["UpdateRoadmapRequest"] = "UpdateRoadmapRequest",
        ["PlanProjectRequest"] = "PlanProjectRequest",
        ["RescheduleItemRequest"] = "RescheduleItemRequest",
        ["ReorderRoadmapRequest"] = "ReorderRoadmapRequest",
        ["BranchRoadmapRequest"] = "BranchRoadmapRequest",
        ["AdoptBranchRequest"] = "AdoptBranchRequest",
        ["RoadmapFeasibilityDto"] = "RoadmapFeasibilityDto",
        ["CapacityWarningDto"] = "CapacityWarningDto",
        ["SequencingViolationDto"] = "SequencingViolationDto",

        ["MarketDto"] = "MarketDto",
        ["CreateMarketRequest"] = "CreateMarketRequest",
        ["ClientSegmentDto"] = "ClientSegmentDto",
        ["CreateClientSegmentRequest"] = "CreateClientSegmentRequest",
        ["SegmentStockStateDto"] = "SegmentStockStateDto",
        ["SetSegmentStockStateRequest"] = "SetSegmentStockStateRequest",
        ["SegmentModulePenetrationDto"] = "SegmentModulePenetrationDto",
        ["SetSegmentModulePenetrationRequest"] = "SetSegmentModulePenetrationRequest",
        ["SegmentApproachAdoptionDto"] = "SegmentApproachAdoptionDto",
        ["SetSegmentApproachAdoptionRequest"] = "SetSegmentApproachAdoptionRequest",
        ["BusinessProcessDto"] = "BusinessProcessDto",
        ["CreateBusinessProcessRequest"] = "CreateBusinessProcessRequest",
        ["ApproachDto"] = "ApproachDto",
        ["CreateApproachRequest"] = "CreateApproachRequest",
        ["CompetitorDto"] = "CompetitorDto",
        ["CreateCompetitorRequest"] = "CreateCompetitorRequest",
        ["ProductDto"] = "ProductDto",
        ["ProductPriceDto"] = "ProductPriceDto",
        ["CreateProductRequest"] = "CreateProductRequest",
        ["SetProductPriceRequest"] = "SetProductPriceRequest",
        ["MoneyDto"] = "MoneyDto",
        ["CoverageDto"] = "CoverageDto",
        ["CreateCoverageRequest"] = "CreateCoverageRequest",
        ["UpdateCoverageRequest"] = "UpdateCoverageRequest",

        ["FirmDto"] = "FirmDto",
        ["CreateFirmRequest"] = "CreateFirmRequest",
        ["UpdateFirmRequest"] = "UpdateFirmRequest",

        ["NeedDto"] = "NeedDto",
        ["CreateNeedRequest"] = "CreateNeedRequest",
        ["UpdateNeedRequest"] = "UpdateNeedRequest",

        ["EventRateMultiplierDto"] = "EventRateMultiplierDto",
        ["MarketEventDto"] = "MarketEventDto",
        ["CreateMarketEventRequest"] = "CreateMarketEventRequest",
        ["UpdateMarketEventRequest"] = "UpdateMarketEventRequest",

        ["CovariateDto"] = "CovariateDto",
        ["CreateCovariateRequest"] = "CreateCovariateRequest",
        ["UpdateCovariateRequest"] = "UpdateCovariateRequest",
        ["CovariateObservationDto"] = "CovariateObservationDto",
        ["CovariateHistoryDto"] = "CovariateHistoryDto",

        ["FactEventDto"] = "FactEventDto",
        ["CreateFactEventRequest"] = "CreateFactEventRequest",

        ["DistributionSpecDto"] = "DistributionSpecDto",
        ["ParameterDto"] = "ParameterDto",
        ["AssumptionSetDto"] = "AssumptionSetDto",
        ["CreateAssumptionSetRequest"] = "CreateAssumptionSetRequest",
        ["UpdateAssumptionSetRequest"] = "UpdateAssumptionSetRequest",
        ["BranchAssumptionSetRequest"] = "BranchAssumptionSetRequest",
        ["UpsertParameterRequest"] = "UpsertParameterRequest",

        ["SimulationRunDto"] = "SimulationRunDto",
        ["CreateSimulationRunRequest"] = "CreateSimulationRunRequest",

        // ---------- Phase 3: channels, integrations, firm relationships, opportunities, calibration ----------

        ["ChannelSegmentReachDto"] = "ChannelSegmentReachDto",
        ["ChannelProductAffinityDto"] = "ChannelProductAffinityDto",
        ["ChannelDto"] = "ChannelDto",
        ["CreateChannelRequest"] = "CreateChannelRequest",
        ["UpdateChannelRequest"] = "UpdateChannelRequest",
        ["SetChannelSegmentReachRequest"] = "SetChannelSegmentReachRequest",
        ["SetChannelProductAffinityRequest"] = "SetChannelProductAffinityRequest",

        ["IntegrationDto"] = "IntegrationDto",
        ["CreateIntegrationRequest"] = "CreateIntegrationRequest",
        ["UpdateIntegrationRequest"] = "UpdateIntegrationRequest",

        ["FirmProductRelationshipDto"] = "FirmProductRelationshipDto",
        ["CreateFirmProductRelationshipRequest"] = "CreateFirmProductRelationshipRequest",
        ["UpdateFirmProductRelationshipRequest"] = "UpdateFirmProductRelationshipRequest",

        ["FirmImportItem"] = "FirmImportItem",
        ["FirmImportRelationship"] = "FirmImportRelationship",
        ["BulkImportFirmsRequest"] = "BulkImportFirmsRequest",
        ["BulkImportFirmsResponse"] = "BulkImportFirmsResponse",
        ["FirmImportOutcomeDto"] = "FirmImportOutcomeDto",
        ["RelationshipImportOutcomeDto"] = "RelationshipImportOutcomeDto",
        ["FieldConflictDto"] = "FieldConflictDto",
        ["SourceRef"] = "SourceRef",
        ["SourceBindingDto"] = "SourceBindingDto",
        ["GenerateSyntheticFirmsRequest"] = "GenerateSyntheticFirmsRequest",
        ["GenerateSyntheticFirmsResponse"] = "GenerateSyntheticFirmsResponse",
        ["SyntheticSegmentFillDto"] = "SyntheticSegmentFillDto",

        ["OpportunityScopeItemDto"] = "OpportunityScopeItemDto",
        ["OpportunityDto"] = "OpportunityDto",
        ["CreateOpportunityRequest"] = "CreateOpportunityRequest",
        ["UpdateOpportunityRequest"] = "UpdateOpportunityRequest",
        ["AddOpportunityScopeRequest"] = "AddOpportunityScopeRequest",
        ["QualifyOpportunityRequest"] = "QualifyOpportunityRequest",
        ["DecideOpportunityRequest"] = "DecideOpportunityRequest",

        ["CalibrationRunDto"] = "CalibrationRunDto",
        ["CreateCalibrationRunRequest"] = "CreateCalibrationRunRequest",
        ["CalibrationParameterDiffDto"] = "CalibrationParameterDiffDto",
        ["CalibrationUnfittedDto"] = "CalibrationUnfittedDto",
        ["CalibrationDiffDto"] = "CalibrationDiffDto",
        ["CalibrationRunResultDto"] = "CalibrationRunResultDto",

        ["EngineSegmentFunnel"] = "EngineSegmentFunnel",
        ["EngineChannelFunnel"] = "EngineChannelFunnel",
        ["EngineFirmLevelDiagnostics"] = "EngineFirmLevelDiagnostics",

        ["EngineCostRates"] = "EngineCostRates",
        ["EngineModulePenetration"] = "EngineModulePenetration",
        ["EngineSegmentInput"] = "EngineSegmentInput",
        ["EngineDistributionSpec"] = "EngineDistributionSpec",
        ["EngineCovariateLink"] = "EngineCovariateLink",
        ["EngineParameterSpec"] = "EngineParameterSpec",
        ["EngineGbmSpec"] = "EngineGbmSpec",
        ["EngineCovariateInput"] = "EngineCovariateInput",
        ["EngineEventRateMultiplier"] = "EngineEventRateMultiplier",
        ["EngineEventInput"] = "EngineEventInput",
        ["EngineAttractivenessEntry"] = "EngineAttractivenessEntry",
        ["EngineInput"] = "EngineInput",
        ["EngineDistributionSummary"] = "EngineDistributionSummary",
        ["EngineMetricTrajectory"] = "EngineMetricTrajectory",
        ["EngineSegmentTrajectories"] = "EngineSegmentTrajectories",
        ["EngineResult"] = "EngineResult",
    };

    // CLI enum type name -> OpenAPI component schema name.
    private static readonly Dictionary<string, string> EnumSchemaMap = new()
    {
        ["ProjectStatus"] = "ProjectStatus",
        ["FeatureStatus"] = "FeatureStatus",
        ["CoverageTier"] = "CoverageTier",
        ["FeatureDeltaKind"] = "FeatureDeltaKind",
        ["SequencingViolationKind"] = "SequencingViolationKind",
        ["StrategicIntent"] = "StrategicIntent",

        ["SimulationFlowKind"] = "SimulationFlowKind",
        ["DistributionType"] = "DistributionType",
        ["EngineDistributionType"] = "EngineDistributionType",
        ["ParameterProvenance"] = "ParameterProvenance",
        ["FactEventKind"] = "FactEventKind",
        ["EpistemicStatus"] = "EpistemicStatus",
        ["MarketEventType"] = "MarketEventType",

        ["OpportunityStage"] = "OpportunityStage",
        ["ImportOutcome"] = "ImportOutcome",
        ["SimulationEngineMode"] = "SimulationEngineMode",
    };

    private static readonly Assembly ClientAssembly = typeof(ProductManagerApiClient).Assembly;

    // ---------- (a) every call site's route exists in the contract ----------

    [Fact]
    public void Every_route_the_client_calls_exists_in_the_openapi_contract()
    {
        var callSites = ExtractCallSites(LoadClientSource());
        Assert.True(callSites.Count >= 60, $"Only found {callSites.Count} call sites — the source scanner probably broke.");

        var specRoutes = LoadSpecRoutes();
        var errors = new List<string>();

        foreach (var site in callSites.DistinctBy(s => (s.Verb, s.UrlTemplate)))
        {
            var normalized = NormalizeRoute(site.Verb, site.UrlTemplate);
            if (!specRoutes.Contains(normalized))
            {
                errors.Add(
                    $"{site.Verb} \"{site.UrlTemplate}\" (normalized: {normalized.Verb} {normalized.Path}) has no " +
                    "matching entry in openapi/productmanager.json — the API route was likely renamed, removed, or " +
                    "the CLI call site has a typo.");
            }
        }

        Assert.True(errors.Count == 0, "CLI call sites with no matching route in the OpenAPI contract:\n" + string.Join("\n", errors));
    }

    // ---------- (b)/(c) DTOs and enums used by the client match their schemas ----------

    [Fact]
    public void Every_dto_the_client_uses_is_registered_and_matches_its_schema()
    {
        using var spec = LoadSpec();
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

        var callSites = ExtractCallSites(LoadClientSource());
        var errors = new List<string>();

        // Every leaf DTO name reached from a call site's generic type arguments (unwrapping
        // PagedResponse<T> / IReadOnlyList<T>) must be registered.
        var usedLeafNames = new HashSet<string>();
        var pagedLeafNames = new HashSet<string>();
        foreach (var site in callSites)
        foreach (var rawArg in site.TypeArgs)
        {
            var (container, leaf) = ParseTypeArg(rawArg);
            usedLeafNames.Add(leaf);
            if (container == "PagedResponse") pagedLeafNames.Add(leaf);
        }

        foreach (var leaf in usedLeafNames)
        {
            if (!DtoSchemaMap.ContainsKey(leaf))
            {
                errors.Add($"CLI DTO '{leaf}' is used by a ProductManagerApiClient call site but has no entry in " +
                           "ApiContractDriftTests.DtoSchemaMap — add a mapping to the corresponding OpenAPI schema.");
            }
        }

        // Registered DTOs must themselves be internally consistent: every Client-namespace type
        // reachable from a registered DTO's properties must also be registered (catches a nested
        // DTO/enum that was added but never wired into the maps above).
        foreach (var dtoName in DtoSchemaMap.Keys)
        {
            var type = ClientAssembly.GetType($"ProductManager.Cli.Client.{dtoName}");
            if (type is null)
            {
                errors.Add($"DtoSchemaMap references '{dtoName}', but no such type exists in ProductManager.Cli.Client.");
                continue;
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var element = UnwrapClientElementType(prop.PropertyType);
                if (element.Namespace != "ProductManager.Cli.Client") continue;

                if (element.IsEnum)
                {
                    if (!EnumSchemaMap.ContainsKey(element.Name))
                        errors.Add($"Enum '{element.Name}' (used by {dtoName}.{prop.Name}) is not registered in EnumSchemaMap.");
                }
                else if (!DtoSchemaMap.ContainsKey(element.Name))
                {
                    errors.Add($"DTO '{element.Name}' (used by {dtoName}.{prop.Name}) is not registered in DtoSchemaMap.");
                }
            }
        }

        // Property-name subset/superset check against the matched schema, for every registered DTO.
        foreach (var (dtoName, schemaName) in DtoSchemaMap)
        {
            if (!schemas.TryGetProperty(schemaName, out var schema))
            {
                errors.Add($"DtoSchemaMap maps CLI DTO '{dtoName}' to schema '{schemaName}', but no such schema exists " +
                           "in openapi/productmanager.json.");
                continue;
            }

            var type = ClientAssembly.GetType($"ProductManager.Cli.Client.{dtoName}");
            if (type is null) continue; // already reported above

            CompareDtoToSchema(dtoName, type, schemaName, schema, errors);
        }

        // Wrapper check for every PagedResponse<T> actually returned by the client.
        foreach (var leaf in pagedLeafNames)
        {
            var wrapperSchemaName = $"PagedResponseOf{leaf}";
            if (!schemas.TryGetProperty(wrapperSchemaName, out var wrapperSchema))
            {
                errors.Add($"CLI returns PagedResponse<{leaf}> but openapi/productmanager.json has no " +
                           $"'{wrapperSchemaName}' schema.");
                continue;
            }

            var elementType = ClientAssembly.GetType($"ProductManager.Cli.Client.{leaf}");
            if (elementType is null) continue; // already reported above

            var pagedType = typeof(PagedResponse<>).MakeGenericType(elementType);
            CompareDtoToSchema($"PagedResponse<{leaf}>", pagedType, wrapperSchemaName, wrapperSchema, errors);
        }

        Assert.True(errors.Count == 0, "CLI/OpenAPI DTO drift:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void Every_enum_used_by_a_registered_dto_matches_its_schema_member_list_exactly()
    {
        using var spec = LoadSpec();
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");
        var errors = new List<string>();

        foreach (var (enumName, schemaName) in EnumSchemaMap)
        {
            var type = ClientAssembly.GetType($"ProductManager.Cli.Client.{enumName}");
            if (type is null || !type.IsEnum)
            {
                errors.Add($"EnumSchemaMap references '{enumName}', but no such enum exists in ProductManager.Cli.Client.");
                continue;
            }

            if (!schemas.TryGetProperty(schemaName, out var schema))
            {
                errors.Add($"EnumSchemaMap maps CLI enum '{enumName}' to schema '{schemaName}', but no such schema " +
                           "exists in openapi/productmanager.json.");
                continue;
            }

            var clientMembers = Enum.GetNames(type).ToHashSet();
            var schemaMembers = EnumValuesOf(schema);

            var onlyInClient = clientMembers.Except(schemaMembers).ToList();
            var onlyInSchema = schemaMembers.Except(clientMembers).ToList();

            if (onlyInClient.Count > 0)
                errors.Add($"CLI enum '{enumName}' has member(s) {string.Join(", ", onlyInClient)} that schema '{schemaName}' does not.");
            if (onlyInSchema.Count > 0)
                errors.Add($"Schema '{schemaName}' has value(s) {string.Join(", ", onlyInSchema)} that CLI enum '{enumName}' is missing.");
        }

        Assert.True(errors.Count == 0, "CLI/OpenAPI enum drift:\n" + string.Join("\n", errors));
    }

    // ---------- comparison helper ----------

    private static void CompareDtoToSchema(string dtoLabel, Type dtoType, string schemaName, JsonElement schema, List<string> errors)
    {
        var schemaProps = PropertyNamesOf(schema);
        var required = RequiredOf(schema);
        var dtoProps = CamelCasePropertyNames(dtoType);

        var extra = dtoProps.Except(schemaProps).ToList();
        if (extra.Count > 0)
        {
            errors.Add($"CLI DTO '{dtoLabel}' has propert{(extra.Count == 1 ? "y" : "ies")} " +
                       $"{string.Join(", ", extra)} not present on schema '{schemaName}' " +
                       "— likely renamed or removed on the API side.");
        }

        var missingRequired = required.Except(dtoProps).ToList();
        if (missingRequired.Count > 0)
        {
            errors.Add($"CLI DTO '{dtoLabel}' is missing required propert{(missingRequired.Count == 1 ? "y" : "ies")} " +
                       $"{string.Join(", ", missingRequired)} from schema '{schemaName}'.");
        }
    }

    private static Type UnwrapClientElementType(Type type)
    {
        while (true)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                type = type.GetGenericArguments()[0];
                continue;
            }

            if (type.IsGenericType && type != typeof(string) &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            {
                type = type.GetGenericArguments()[0];
                continue;
            }

            return type;
        }
    }

    private static HashSet<string> CamelCasePropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToHashSet();

    // ---------- OpenAPI document helpers ----------

    private static JsonDocument LoadSpec() => JsonDocument.Parse(File.ReadAllText(FindRepoFile(ContractRelativePath)));

    private static HashSet<(string Verb, string Path)> LoadSpecRoutes()
    {
        using var spec = LoadSpec();
        var routes = new HashSet<(string, string)>();

        foreach (var pathEntry in spec.RootElement.GetProperty("paths").EnumerateObject())
        {
            var normalizedPath = Regex.Replace(pathEntry.Name, @"\{[^}]+\}", "{}");
            foreach (var methodEntry in pathEntry.Value.EnumerateObject())
            {
                if (methodEntry.Name is "get" or "post" or "put" or "delete" or "patch")
                    routes.Add((methodEntry.Name.ToUpperInvariant(), normalizedPath));
            }
        }

        return routes;
    }

    private static (string Verb, string Path) NormalizeRoute(string verb, string urlTemplate)
    {
        var path = urlTemplate.StartsWith('/') ? urlTemplate : "/" + urlTemplate;
        path = Regex.Replace(path, @"\{[^}]+\}", "{}");
        return (verb, path);
    }

    private static HashSet<string> PropertyNamesOf(JsonElement schema) =>
        schema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().Select(p => p.Name).ToHashSet()
            : new HashSet<string>();

    private static HashSet<string> RequiredOf(JsonElement schema) =>
        schema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(e => e.GetString()!).ToHashSet()
            : new HashSet<string>();

    private static HashSet<string> EnumValuesOf(JsonElement schema) =>
        schema.TryGetProperty("enum", out var e)
            ? e.EnumerateArray().Select(x => x.GetString()!).ToHashSet()
            : new HashSet<string>();

    // ---------- client source scanning ----------

    private sealed record CallSite(string Verb, string UrlTemplate, IReadOnlyList<string> TypeArgs);

    private static readonly Regex CallSiteIdentifier =
        new(@"\b(GetAsync|PostAsync|PutAsync|DeleteReturningAsync|DeleteAsync)\b", RegexOptions.Compiled);

    /// <summary>
    /// Scans the raw source of ProductManagerApiClient.cs for every call to its five HTTP-verb
    /// plumbing methods, skipping the methods' own declarations (which look almost identical
    /// syntactically — a declaration's first "argument" is a type keyword like "string", never a
    /// quote or "Query(").
    /// </summary>
    private static List<CallSite> ExtractCallSites(string source)
    {
        var verbByMethod = new Dictionary<string, string>
        {
            ["GetAsync"] = "GET",
            ["PostAsync"] = "POST",
            ["PutAsync"] = "PUT",
            ["DeleteReturningAsync"] = "DELETE",
            ["DeleteAsync"] = "DELETE",
        };

        var sites = new List<CallSite>();

        foreach (Match m in CallSiteIdentifier.Matches(source))
        {
            var name = m.Value;
            var pos = m.Index + m.Length;

            var typeArgs = new List<string>();
            if (pos < source.Length && source[pos] == '<')
            {
                var (content, end) = ExtractAngleBrackets(source, pos);
                typeArgs = SplitTopLevel(content);
                pos = end;
            }

            while (pos < source.Length && char.IsWhiteSpace(source[pos])) pos++;
            if (pos >= source.Length || source[pos] != '(') continue;

            var argStart = pos + 1;
            while (argStart < source.Length && char.IsWhiteSpace(source[argStart])) argStart++;
            if (argStart >= source.Length) continue;

            var isCall =
                source[argStart] == '"' ||
                (source[argStart] == '$' && argStart + 1 < source.Length && source[argStart + 1] == '"') ||
                source.AsSpan(argStart).StartsWith("Query(");

            if (!isCall) continue; // the plumbing method's own declaration, not a call site

            var q1 = source.IndexOf('"', argStart);
            var q2 = source.IndexOf('"', q1 + 1);
            var url = source.Substring(q1 + 1, q2 - q1 - 1);

            sites.Add(new CallSite(verbByMethod[name], url, typeArgs));
        }

        return sites;
    }

    private static (string Content, int EndExclusive) ExtractAngleBrackets(string src, int ltIndex)
    {
        var depth = 0;
        for (var i = ltIndex; i < src.Length; i++)
        {
            if (src[i] == '<') depth++;
            else if (src[i] == '>')
            {
                depth--;
                if (depth == 0) return (src.Substring(ltIndex + 1, i - ltIndex - 1), i + 1);
            }
        }

        throw new InvalidOperationException("Unbalanced angle brackets while scanning ProductManagerApiClient.cs.");
    }

    private static List<string> SplitTopLevel(string s)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '<') depth++;
            else if (s[i] == '>') depth--;
            else if (s[i] == ',' && depth == 0)
            {
                parts.Add(s.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        parts.Add(s.Substring(start).Trim());
        return parts;
    }

    private static (string? Container, string Leaf) ParseTypeArg(string raw)
    {
        var m = Regex.Match(raw.Trim(), @"^([A-Za-z_][A-Za-z0-9_]*)<(.+)>$");
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value.Trim()) : (null, raw.Trim());
    }

    private static string LoadClientSource() => File.ReadAllText(FindRepoFile(ClientSourceRelativePath));

    private static string FindRepoFile(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' by walking up from {AppContext.BaseDirectory}.");
    }
}
