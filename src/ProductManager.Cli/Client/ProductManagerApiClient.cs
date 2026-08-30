using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProductManager.Cli.Client;

/// <summary>
/// Hand-written typed client for the ProductManager API (see openapi/productmanager.json). One
/// class, no codegen: every method corresponds to a single documented endpoint. Callers own the
/// underlying <see cref="HttpClient"/> (base address + bearer token already configured).
/// </summary>
public sealed class ProductManagerApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // No naming policy: the API's JsonStringEnumConverter() (Program.cs) serializes enum
        // members by their declared C# name (e.g. "InProgress", "NotPlaying"), not camelCase.
        Converters = { new JsonStringEnumConverter() }
    };

    // ---------- Auth ----------

    public Task<TokenResponse> IssueTokenAsync(string password, CancellationToken ct) =>
        PostAsync<TokenRequest, TokenResponse>("api/auth/token", new TokenRequest(password), ct);

    // ---------- Workspaces ----------

    public Task<PagedResponse<WorkspaceDto>> ListWorkspacesAsync(int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<WorkspaceDto>>(Query("api/workspaces", ("page", page), ("pageSize", pageSize)), ct);

    public Task<WorkspaceDto> GetWorkspaceAsync(Guid id, CancellationToken ct) =>
        GetAsync<WorkspaceDto>($"api/workspaces/{id}", ct);

    public Task<WorkspaceDto> GetDefaultWorkspaceAsync(CancellationToken ct) =>
        GetAsync<WorkspaceDto>("api/workspaces/default", ct);

    public Task<WorkspaceDto> CreateWorkspaceAsync(CreateWorkspaceRequest request, CancellationToken ct) =>
        PostAsync<CreateWorkspaceRequest, WorkspaceDto>("api/workspaces", request, ct);

    public Task<WorkspaceDto> UpdateWorkspaceAsync(Guid id, UpdateWorkspaceRequest request, CancellationToken ct) =>
        PutAsync<UpdateWorkspaceRequest, WorkspaceDto>($"api/workspaces/{id}", request, ct);

    public Task DeleteWorkspaceAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/workspaces/{id}", ct);

    // ---------- Teams ----------

    public Task<PagedResponse<TeamDto>> ListTeamsAsync(Guid? workspaceId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<TeamDto>>(
            Query("api/teams", ("workspaceId", workspaceId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<TeamDto> GetTeamAsync(Guid id, CancellationToken ct) => GetAsync<TeamDto>($"api/teams/{id}", ct);

    public Task<TeamDto> CreateTeamAsync(CreateTeamRequest request, CancellationToken ct) =>
        PostAsync<CreateTeamRequest, TeamDto>("api/teams", request, ct);

    public Task<TeamDto> UpdateTeamAsync(Guid id, UpdateTeamRequest request, CancellationToken ct) =>
        PutAsync<UpdateTeamRequest, TeamDto>($"api/teams/{id}", request, ct);

    public Task DeleteTeamAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/teams/{id}", ct);

    public Task<TeamDto> SetTeamCapacityAsync(Guid id, SetTeamCapacityRequest request, CancellationToken ct) =>
        PutAsync<SetTeamCapacityRequest, TeamDto>($"api/teams/{id}/capacities", request, ct);

    // ---------- Initiatives ----------

    public Task<PagedResponse<InitiativeDto>> ListInitiativesAsync(Guid? workspaceId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<InitiativeDto>>(
            Query("api/initiatives", ("workspaceId", workspaceId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<InitiativeDto> GetInitiativeAsync(Guid id, CancellationToken ct) =>
        GetAsync<InitiativeDto>($"api/initiatives/{id}", ct);

    public Task<InitiativeDto> CreateInitiativeAsync(CreateInitiativeRequest request, CancellationToken ct) =>
        PostAsync<CreateInitiativeRequest, InitiativeDto>("api/initiatives", request, ct);

    public Task<InitiativeDto> UpdateInitiativeAsync(Guid id, UpdateInitiativeRequest request, CancellationToken ct) =>
        PutAsync<UpdateInitiativeRequest, InitiativeDto>($"api/initiatives/{id}", request, ct);

    public Task DeleteInitiativeAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/initiatives/{id}", ct);

    // ---------- Projects ----------

    public Task<PagedResponse<ProjectDto>> ListProjectsAsync(Guid? workspaceId, Guid? initiativeId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<ProjectDto>>(
            Query("api/projects", ("workspaceId", workspaceId), ("initiativeId", initiativeId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<ProjectDto> GetProjectAsync(Guid id, CancellationToken ct) => GetAsync<ProjectDto>($"api/projects/{id}", ct);

    public Task<ProjectDto> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct) =>
        PostAsync<CreateProjectRequest, ProjectDto>("api/projects", request, ct);

    public Task<ProjectDto> UpdateProjectAsync(Guid id, UpdateProjectRequest request, CancellationToken ct) =>
        PutAsync<UpdateProjectRequest, ProjectDto>($"api/projects/{id}", request, ct);

    public Task DeleteProjectAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/projects/{id}", ct);

    public Task<ProjectDto> AddProjectDependencyAsync(Guid id, Guid dependsOnProjectId, CancellationToken ct) =>
        PostAsync<AddProjectDependencyRequest, ProjectDto>($"api/projects/{id}/dependencies",
            new AddProjectDependencyRequest(dependsOnProjectId), ct);

    public Task<ProjectDto> RemoveProjectDependencyAsync(Guid id, Guid dependsOnProjectId, CancellationToken ct) =>
        DeleteReturningAsync<ProjectDto>($"api/projects/{id}/dependencies/{dependsOnProjectId}", ct);

    public Task<ProjectDto> SetEffortDemandAsync(Guid id, SetEffortDemandRequest request, CancellationToken ct) =>
        PutAsync<SetEffortDemandRequest, ProjectDto>($"api/projects/{id}/effort-demands", request, ct);

    public Task<ProjectDto> LinkFeatureAsync(Guid id, Guid featureId, CancellationToken ct) =>
        PostAsync<LinkFeatureRequest, ProjectDto>($"api/projects/{id}/features", new LinkFeatureRequest(featureId), ct);

    public Task<ProjectDto> UnlinkFeatureAsync(Guid id, Guid featureId, CancellationToken ct) =>
        DeleteReturningAsync<ProjectDto>($"api/projects/{id}/features/{featureId}", ct);

    // ---------- Features ----------

    public Task<PagedResponse<FeatureDto>> ListFeaturesAsync(Guid? workspaceId, Guid? productId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<FeatureDto>>(
            Query("api/features", ("workspaceId", workspaceId), ("productId", productId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<FeatureDto> GetFeatureAsync(Guid id, CancellationToken ct) => GetAsync<FeatureDto>($"api/features/{id}", ct);

    public Task<FeatureDto> CreateFeatureAsync(CreateFeatureRequest request, CancellationToken ct) =>
        PostAsync<CreateFeatureRequest, FeatureDto>("api/features", request, ct);

    public Task<FeatureDto> UpdateFeatureAsync(Guid id, UpdateFeatureRequest request, CancellationToken ct) =>
        PutAsync<UpdateFeatureRequest, FeatureDto>($"api/features/{id}", request, ct);

    public Task DeleteFeatureAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/features/{id}", ct);

    public Task<FeatureDto> AddFeatureDeltaAsync(Guid id, AddFeatureDeltaRequest request, CancellationToken ct) =>
        PostAsync<AddFeatureDeltaRequest, FeatureDto>($"api/features/{id}/deltas", request, ct);

    public Task<FeatureDto> ReplaceFeatureDeltasAsync(Guid id, IReadOnlyList<AddFeatureDeltaRequest> deltas, CancellationToken ct) =>
        PutAsync<IReadOnlyList<AddFeatureDeltaRequest>, FeatureDto>($"api/features/{id}/deltas", deltas, ct);

    // ---------- Roadmaps ----------

    public Task<PagedResponse<RoadmapDto>> ListRoadmapsAsync(Guid? workspaceId, bool? isLive, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<RoadmapDto>>(
            Query("api/roadmaps", ("workspaceId", workspaceId), ("isLive", isLive), ("page", page), ("pageSize", pageSize)), ct);

    public Task<RoadmapDto> GetRoadmapAsync(Guid id, CancellationToken ct) => GetAsync<RoadmapDto>($"api/roadmaps/{id}", ct);

    public Task<RoadmapDto> CreateRoadmapAsync(CreateRoadmapRequest request, CancellationToken ct) =>
        PostAsync<CreateRoadmapRequest, RoadmapDto>("api/roadmaps", request, ct);

    public Task<RoadmapDto> UpdateRoadmapAsync(Guid id, UpdateRoadmapRequest request, CancellationToken ct) =>
        PutAsync<UpdateRoadmapRequest, RoadmapDto>($"api/roadmaps/{id}", request, ct);

    public Task DeleteRoadmapAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/roadmaps/{id}", ct);

    public Task<RoadmapDto> MakeRoadmapLiveAsync(Guid id, CancellationToken ct) =>
        PostAsync<RoadmapDto>($"api/roadmaps/{id}/make-live", ct);

    public Task<RoadmapDto> PlanProjectAsync(Guid roadmapId, PlanProjectRequest request, CancellationToken ct) =>
        PostAsync<PlanProjectRequest, RoadmapDto>($"api/roadmaps/{roadmapId}/items", request, ct);

    public Task<RoadmapDto> RescheduleItemAsync(Guid roadmapId, Guid projectId, RescheduleItemRequest request, CancellationToken ct) =>
        PutAsync<RescheduleItemRequest, RoadmapDto>($"api/roadmaps/{roadmapId}/items/{projectId}", request, ct);

    public Task<RoadmapDto> UnplanProjectAsync(Guid roadmapId, Guid projectId, CancellationToken ct) =>
        DeleteReturningAsync<RoadmapDto>($"api/roadmaps/{roadmapId}/items/{projectId}", ct);

    public Task<RoadmapDto> ReorderRoadmapAsync(Guid roadmapId, IReadOnlyList<Guid> projectIds, CancellationToken ct) =>
        PutAsync<ReorderRoadmapRequest, RoadmapDto>($"api/roadmaps/{roadmapId}/order", new ReorderRoadmapRequest(projectIds), ct);

    public Task<RoadmapDto> BranchRoadmapAsync(Guid roadmapId, string name, CancellationToken ct) =>
        PostAsync<BranchRoadmapRequest, RoadmapDto>($"api/roadmaps/{roadmapId}/branch", new BranchRoadmapRequest(name), ct);

    public Task<RoadmapDto> AdoptBranchAsync(Guid liveRoadmapId, Guid branchRoadmapId, CancellationToken ct) =>
        PostAsync<AdoptBranchRequest, RoadmapDto>($"api/roadmaps/{liveRoadmapId}/adopt", new AdoptBranchRequest(branchRoadmapId), ct);

    public Task<RoadmapFeasibilityDto> GetRoadmapFeasibilityAsync(Guid roadmapId, CancellationToken ct) =>
        GetAsync<RoadmapFeasibilityDto>($"api/roadmaps/{roadmapId}/feasibility", ct);

    // ---------- Market ----------

    public Task<PagedResponse<MarketDto>> ListMarketsAsync(Guid? workspaceId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<MarketDto>>(Query("api/markets", ("workspaceId", workspaceId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<MarketDto> GetMarketAsync(Guid id, CancellationToken ct) => GetAsync<MarketDto>($"api/markets/{id}", ct);

    public Task<MarketDto> CreateMarketAsync(CreateMarketRequest request, CancellationToken ct) =>
        PostAsync<CreateMarketRequest, MarketDto>("api/markets", request, ct);

    public Task<PagedResponse<ClientSegmentDto>> ListClientSegmentsAsync(Guid? marketId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<ClientSegmentDto>>(Query("api/client-segments", ("marketId", marketId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<ClientSegmentDto> GetClientSegmentAsync(Guid id, CancellationToken ct) =>
        GetAsync<ClientSegmentDto>($"api/client-segments/{id}", ct);

    public Task<ClientSegmentDto> CreateClientSegmentAsync(CreateClientSegmentRequest request, CancellationToken ct) =>
        PostAsync<CreateClientSegmentRequest, ClientSegmentDto>("api/client-segments", request, ct);

    public Task<ClientSegmentDto> SetSegmentStockStateAsync(Guid id, SetSegmentStockStateRequest request, CancellationToken ct) =>
        PutAsync<SetSegmentStockStateRequest, ClientSegmentDto>($"api/client-segments/{id}/stock-state", request, ct);

    public Task<ClientSegmentDto> SetSegmentModulePenetrationAsync(Guid id, SetSegmentModulePenetrationRequest request, CancellationToken ct) =>
        PutAsync<SetSegmentModulePenetrationRequest, ClientSegmentDto>($"api/client-segments/{id}/module-penetrations", request, ct);

    public Task<ClientSegmentDto> RemoveSegmentModulePenetrationAsync(Guid id, Guid moduleProductId, CancellationToken ct) =>
        DeleteReturningAsync<ClientSegmentDto>($"api/client-segments/{id}/module-penetrations/{moduleProductId}", ct);

    public Task<ClientSegmentDto> SetSegmentApproachAdoptionAsync(Guid id, SetSegmentApproachAdoptionRequest request, CancellationToken ct) =>
        PutAsync<SetSegmentApproachAdoptionRequest, ClientSegmentDto>($"api/client-segments/{id}/approach-mix", request, ct);

    public Task<ClientSegmentDto> RemoveSegmentApproachAdoptionAsync(Guid id, Guid approachId, CancellationToken ct) =>
        DeleteReturningAsync<ClientSegmentDto>($"api/client-segments/{id}/approach-mix/{approachId}", ct);

    public Task<PagedResponse<BusinessProcessDto>> ListBusinessProcessesAsync(Guid? marketId, Guid? parentProcessId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<BusinessProcessDto>>(
            Query("api/business-processes", ("marketId", marketId), ("parentProcessId", parentProcessId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<BusinessProcessDto> CreateBusinessProcessAsync(CreateBusinessProcessRequest request, CancellationToken ct) =>
        PostAsync<CreateBusinessProcessRequest, BusinessProcessDto>("api/business-processes", request, ct);

    public Task<PagedResponse<ApproachDto>> ListApproachesAsync(Guid? businessProcessId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<ApproachDto>>(Query("api/approaches", ("businessProcessId", businessProcessId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<ApproachDto> CreateApproachAsync(CreateApproachRequest request, CancellationToken ct) =>
        PostAsync<CreateApproachRequest, ApproachDto>("api/approaches", request, ct);

    public Task<PagedResponse<CompetitorDto>> ListCompetitorsAsync(Guid? marketId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<CompetitorDto>>(Query("api/competitors", ("marketId", marketId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<CompetitorDto> CreateCompetitorAsync(CreateCompetitorRequest request, CancellationToken ct) =>
        PostAsync<CreateCompetitorRequest, CompetitorDto>("api/competitors", request, ct);

    public Task<PagedResponse<ProductDto>> ListProductsAsync(Guid? marketId, Guid? competitorId, Guid? parentProductId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<ProductDto>>(
            Query("api/products", ("marketId", marketId), ("competitorId", competitorId), ("parentProductId", parentProductId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken ct) =>
        PostAsync<CreateProductRequest, ProductDto>("api/products", request, ct);

    public Task<ProductDto> SetProductPriceAsync(Guid id, SetProductPriceRequest request, CancellationToken ct) =>
        PutAsync<SetProductPriceRequest, ProductDto>($"api/products/{id}/prices", request, ct);

    public Task<PagedResponse<CoverageDto>> ListCoveragesAsync(Guid? productId, Guid? businessProcessId, Guid? clientSegmentId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<CoverageDto>>(
            Query("api/coverages", ("productId", productId), ("businessProcessId", businessProcessId), ("clientSegmentId", clientSegmentId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<CoverageDto> GetCoverageAsync(Guid id, CancellationToken ct) => GetAsync<CoverageDto>($"api/coverages/{id}", ct);

    /// <summary>Upserts on the (product, process, approach?, segment?) key — posting the same tuple
    /// twice re-assesses the existing row rather than duplicating it.</summary>
    public Task<CoverageDto> CreateCoverageAsync(CreateCoverageRequest request, CancellationToken ct) =>
        PostAsync<CreateCoverageRequest, CoverageDto>("api/coverages", request, ct);

    public Task<CoverageDto> UpdateCoverageAsync(Guid id, UpdateCoverageRequest request, CancellationToken ct) =>
        PutAsync<UpdateCoverageRequest, CoverageDto>($"api/coverages/{id}", request, ct);

    public Task DeleteCoverageAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/coverages/{id}", ct);

    // ---------- Firms ----------

    public Task<PagedResponse<FirmDto>> ListFirmsAsync(Guid? marketId, Guid? clientSegmentId, EpistemicStatus? epistemicStatus, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<FirmDto>>(
            Query("api/firms", ("marketId", marketId), ("clientSegmentId", clientSegmentId), ("epistemicStatus", epistemicStatus), ("page", page), ("pageSize", pageSize)), ct);

    public Task<FirmDto> GetFirmAsync(Guid id, CancellationToken ct) => GetAsync<FirmDto>($"api/firms/{id}", ct);

    public Task<FirmDto> CreateFirmAsync(CreateFirmRequest request, CancellationToken ct) =>
        PostAsync<CreateFirmRequest, FirmDto>("api/firms", request, ct);

    public Task<FirmDto> UpdateFirmAsync(Guid id, UpdateFirmRequest request, CancellationToken ct) =>
        PutAsync<UpdateFirmRequest, FirmDto>($"api/firms/{id}", request, ct);

    public Task DeleteFirmAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/firms/{id}", ct);

    /// <summary>Imports a whole roster into one market, relationships included. Matching is by
    /// natural key (market, name), so re-importing the same roster updates rather than duplicates it.</summary>
    public Task<BulkImportFirmsResponse> BulkImportFirmsAsync(BulkImportFirmsRequest request, CancellationToken ct) =>
        PostAsync<BulkImportFirmsRequest, BulkImportFirmsResponse>("api/firms/bulk", request, ct);

    /// <summary>Fills a market's segment stocks with deterministically generated synthetic firms.
    /// Regeneration replaces the previous synthetic population rather than adding a second one.</summary>
    public Task<GenerateSyntheticFirmsResponse> GenerateSyntheticFirmsAsync(Guid marketId, CancellationToken ct) =>
        PostAsync<GenerateSyntheticFirmsRequest, GenerateSyntheticFirmsResponse>(
            "api/firms/generate-synthetic", new GenerateSyntheticFirmsRequest(marketId), ct);

    // ---------- Channels ----------

    public Task<PagedResponse<ChannelDto>> ListChannelsAsync(Guid? marketId, bool? originatesOpportunities, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<ChannelDto>>(
            Query("api/channels", ("marketId", marketId), ("originatesOpportunities", originatesOpportunities), ("page", page), ("pageSize", pageSize)), ct);

    public Task<ChannelDto> GetChannelAsync(Guid id, CancellationToken ct) => GetAsync<ChannelDto>($"api/channels/{id}", ct);

    public Task<ChannelDto> CreateChannelAsync(CreateChannelRequest request, CancellationToken ct) =>
        PostAsync<CreateChannelRequest, ChannelDto>("api/channels", request, ct);

    public Task<ChannelDto> UpdateChannelAsync(Guid id, UpdateChannelRequest request, CancellationToken ct) =>
        PutAsync<UpdateChannelRequest, ChannelDto>($"api/channels/{id}", request, ct);

    public Task DeleteChannelAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/channels/{id}", ct);

    public Task<ChannelDto> SetChannelSegmentReachAsync(Guid id, SetChannelSegmentReachRequest request, CancellationToken ct) =>
        PutAsync<SetChannelSegmentReachRequest, ChannelDto>($"api/channels/{id}/segment-reaches", request, ct);

    public Task<ChannelDto> RemoveChannelSegmentReachAsync(Guid id, Guid clientSegmentId, CancellationToken ct) =>
        DeleteReturningAsync<ChannelDto>($"api/channels/{id}/segment-reaches/{clientSegmentId}", ct);

    public Task<ChannelDto> SetChannelProductAffinityAsync(Guid id, SetChannelProductAffinityRequest request, CancellationToken ct) =>
        PutAsync<SetChannelProductAffinityRequest, ChannelDto>($"api/channels/{id}/product-affinities", request, ct);

    public Task<ChannelDto> RemoveChannelProductAffinityAsync(Guid id, Guid productId, CancellationToken ct) =>
        DeleteReturningAsync<ChannelDto>($"api/channels/{id}/product-affinities/{productId}", ct);

    // ---------- Integrations ----------

    public Task<PagedResponse<IntegrationDto>> ListIntegrationsAsync(Guid? productId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<IntegrationDto>>(Query("api/integrations", ("productId", productId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<IntegrationDto> GetIntegrationAsync(Guid id, CancellationToken ct) => GetAsync<IntegrationDto>($"api/integrations/{id}", ct);

    public Task<IntegrationDto> CreateIntegrationAsync(CreateIntegrationRequest request, CancellationToken ct) =>
        PostAsync<CreateIntegrationRequest, IntegrationDto>("api/integrations", request, ct);

    public Task<IntegrationDto> UpdateIntegrationAsync(Guid id, UpdateIntegrationRequest request, CancellationToken ct) =>
        PutAsync<UpdateIntegrationRequest, IntegrationDto>($"api/integrations/{id}", request, ct);

    public Task DeleteIntegrationAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/integrations/{id}", ct);

    // ---------- Firm↔Product relationships ----------

    public Task<PagedResponse<FirmProductRelationshipDto>> ListFirmRelationshipsAsync(Guid? firmId, Guid? productId, bool? activeOnly, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<FirmProductRelationshipDto>>(
            Query("api/firm-relationships", ("firmId", firmId), ("productId", productId), ("activeOnly", activeOnly), ("page", page), ("pageSize", pageSize)), ct);

    public Task<FirmProductRelationshipDto> GetFirmRelationshipAsync(Guid id, CancellationToken ct) =>
        GetAsync<FirmProductRelationshipDto>($"api/firm-relationships/{id}", ct);

    public Task<FirmProductRelationshipDto> CreateFirmRelationshipAsync(CreateFirmProductRelationshipRequest request, CancellationToken ct) =>
        PostAsync<CreateFirmProductRelationshipRequest, FirmProductRelationshipDto>("api/firm-relationships", request, ct);

    public Task<FirmProductRelationshipDto> UpdateFirmRelationshipAsync(Guid id, UpdateFirmProductRelationshipRequest request, CancellationToken ct) =>
        PutAsync<UpdateFirmProductRelationshipRequest, FirmProductRelationshipDto>($"api/firm-relationships/{id}", request, ct);

    public Task DeleteFirmRelationshipAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/firm-relationships/{id}", ct);

    // ---------- Opportunities ----------

    public Task<PagedResponse<OpportunityDto>> ListOpportunitiesAsync(Guid? firmId, Guid? channelId, OpportunityStage? stage, bool? isCrossSell, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<OpportunityDto>>(
            Query("api/opportunities", ("firmId", firmId), ("channelId", channelId), ("stage", stage), ("isCrossSell", isCrossSell), ("page", page), ("pageSize", pageSize)), ct);

    public Task<OpportunityDto> GetOpportunityAsync(Guid id, CancellationToken ct) => GetAsync<OpportunityDto>($"api/opportunities/{id}", ct);

    public Task<OpportunityDto> CreateOpportunityAsync(CreateOpportunityRequest request, CancellationToken ct) =>
        PostAsync<CreateOpportunityRequest, OpportunityDto>("api/opportunities", request, ct);

    public Task<OpportunityDto> UpdateOpportunityAsync(Guid id, UpdateOpportunityRequest request, CancellationToken ct) =>
        PutAsync<UpdateOpportunityRequest, OpportunityDto>($"api/opportunities/{id}", request, ct);

    public Task DeleteOpportunityAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/opportunities/{id}", ct);

    public Task<OpportunityDto> AddOpportunityScopeAsync(Guid id, AddOpportunityScopeRequest request, CancellationToken ct) =>
        PostAsync<AddOpportunityScopeRequest, OpportunityDto>($"api/opportunities/{id}/scope", request, ct);

    public Task<OpportunityDto> RemoveOpportunityScopeAsync(Guid id, Guid businessProcessId, CancellationToken ct) =>
        DeleteReturningAsync<OpportunityDto>($"api/opportunities/{id}/scope/{businessProcessId}", ct);

    public Task<OpportunityDto> QualifyOpportunityAsync(Guid id, QualifyOpportunityRequest request, CancellationToken ct) =>
        PostAsync<QualifyOpportunityRequest, OpportunityDto>($"api/opportunities/{id}/qualify", request, ct);

    public Task<OpportunityDto> DecideOpportunityAsync(Guid id, DecideOpportunityRequest request, CancellationToken ct) =>
        PostAsync<DecideOpportunityRequest, OpportunityDto>($"api/opportunities/{id}/decide", request, ct);

    // ---------- Calibration runs ----------

    public Task<PagedResponse<CalibrationRunDto>> ListCalibrationRunsAsync(Guid? workspaceId, Guid? priorAssumptionSetId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<CalibrationRunDto>>(
            Query("api/calibration-runs", ("workspaceId", workspaceId), ("priorAssumptionSetId", priorAssumptionSetId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<CalibrationRunDto> GetCalibrationRunAsync(Guid id, CancellationToken ct) => GetAsync<CalibrationRunDto>($"api/calibration-runs/{id}", ct);

    public Task<CalibrationDiffDto> GetCalibrationRunDiffAsync(Guid id, CancellationToken ct) =>
        GetAsync<CalibrationDiffDto>($"api/calibration-runs/{id}/diff", ct);

    public Task<CalibrationRunResultDto> CreateCalibrationRunAsync(CreateCalibrationRunRequest request, CancellationToken ct) =>
        PostAsync<CreateCalibrationRunRequest, CalibrationRunResultDto>("api/calibration-runs", request, ct);

    public Task DeleteCalibrationRunAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/calibration-runs/{id}", ct);

    // ---------- Needs ----------

    public Task<PagedResponse<NeedDto>> ListNeedsAsync(Guid? firmId, Guid? clientSegmentId, Guid? businessProcessId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<NeedDto>>(
            Query("api/needs", ("firmId", firmId), ("clientSegmentId", clientSegmentId), ("businessProcessId", businessProcessId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<NeedDto> GetNeedAsync(Guid id, CancellationToken ct) => GetAsync<NeedDto>($"api/needs/{id}", ct);

    public Task<NeedDto> CreateNeedAsync(CreateNeedRequest request, CancellationToken ct) =>
        PostAsync<CreateNeedRequest, NeedDto>("api/needs", request, ct);

    public Task<NeedDto> UpdateNeedAsync(Guid id, UpdateNeedRequest request, CancellationToken ct) =>
        PutAsync<UpdateNeedRequest, NeedDto>($"api/needs/{id}", request, ct);

    public Task DeleteNeedAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/needs/{id}", ct);

    // ---------- Market events ----------

    public Task<PagedResponse<MarketEventDto>> ListMarketEventsAsync(Guid? marketId, bool? isHypothetical, Guid? competitorId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<MarketEventDto>>(
            Query("api/market-events", ("marketId", marketId), ("isHypothetical", isHypothetical), ("competitorId", competitorId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<MarketEventDto> GetMarketEventAsync(Guid id, CancellationToken ct) => GetAsync<MarketEventDto>($"api/market-events/{id}", ct);

    public Task<MarketEventDto> CreateMarketEventAsync(CreateMarketEventRequest request, CancellationToken ct) =>
        PostAsync<CreateMarketEventRequest, MarketEventDto>("api/market-events", request, ct);

    public Task<MarketEventDto> UpdateMarketEventAsync(Guid id, UpdateMarketEventRequest request, CancellationToken ct) =>
        PutAsync<UpdateMarketEventRequest, MarketEventDto>($"api/market-events/{id}", request, ct);

    public Task DeleteMarketEventAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/market-events/{id}", ct);

    // ---------- Covariates ----------

    public Task<PagedResponse<CovariateDto>> ListCovariatesAsync(Guid? workspaceId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<CovariateDto>>(Query("api/covariates", ("workspaceId", workspaceId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<CovariateDto> GetCovariateAsync(Guid id, CancellationToken ct) => GetAsync<CovariateDto>($"api/covariates/{id}", ct);

    public Task<CovariateDto> CreateCovariateAsync(CreateCovariateRequest request, CancellationToken ct) =>
        PostAsync<CreateCovariateRequest, CovariateDto>("api/covariates", request, ct);

    public Task<CovariateDto> UpdateCovariateAsync(Guid id, UpdateCovariateRequest request, CancellationToken ct) =>
        PutAsync<UpdateCovariateRequest, CovariateDto>($"api/covariates/{id}", request, ct);

    public Task DeleteCovariateAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/covariates/{id}", ct);

    public Task<CovariateHistoryDto> GetCovariateHistoryAsync(Guid id, DateOnly? effectiveFrom, DateOnly? effectiveTo, DateTimeOffset? knownAsOf, CancellationToken ct) =>
        GetAsync<CovariateHistoryDto>(
            Query($"api/covariates/{id}/history", ("effectiveFrom", effectiveFrom), ("effectiveTo", effectiveTo), ("knownAsOf", knownAsOf)), ct);

    // ---------- Facts ----------

    public Task<PagedResponse<FactEventDto>> ListFactsAsync(
        Guid? workspaceId, FactEventKind? kind, string? subjectType, Guid? subjectId,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, DateTimeOffset? knownAsOf, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<FactEventDto>>(
            Query("api/facts",
                ("workspaceId", workspaceId), ("kind", kind), ("subjectType", subjectType), ("subjectId", subjectId),
                ("effectiveFrom", effectiveFrom), ("effectiveTo", effectiveTo), ("knownAsOf", knownAsOf),
                ("page", page), ("pageSize", pageSize)), ct);

    public Task<FactEventDto> GetFactAsync(Guid id, CancellationToken ct) => GetAsync<FactEventDto>($"api/facts/{id}", ct);

    public Task<FactEventDto> CreateFactAsync(CreateFactEventRequest request, CancellationToken ct) =>
        PostAsync<CreateFactEventRequest, FactEventDto>("api/facts", request, ct);

    // ---------- Assumption sets ----------

    public Task<PagedResponse<AssumptionSetDto>> ListAssumptionSetsAsync(Guid? workspaceId, Guid? parentAssumptionSetId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<AssumptionSetDto>>(
            Query("api/assumption-sets", ("workspaceId", workspaceId), ("parentAssumptionSetId", parentAssumptionSetId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<AssumptionSetDto> GetAssumptionSetAsync(Guid id, CancellationToken ct) => GetAsync<AssumptionSetDto>($"api/assumption-sets/{id}", ct);

    public Task<AssumptionSetDto> CreateAssumptionSetAsync(CreateAssumptionSetRequest request, CancellationToken ct) =>
        PostAsync<CreateAssumptionSetRequest, AssumptionSetDto>("api/assumption-sets", request, ct);

    public Task<AssumptionSetDto> UpdateAssumptionSetAsync(Guid id, UpdateAssumptionSetRequest request, CancellationToken ct) =>
        PutAsync<UpdateAssumptionSetRequest, AssumptionSetDto>($"api/assumption-sets/{id}", request, ct);

    public Task DeleteAssumptionSetAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/assumption-sets/{id}", ct);

    public Task<AssumptionSetDto> BranchAssumptionSetAsync(Guid id, BranchAssumptionSetRequest request, CancellationToken ct) =>
        PostAsync<BranchAssumptionSetRequest, AssumptionSetDto>($"api/assumption-sets/{id}/branch", request, ct);

    public Task<AssumptionSetDto> UpsertParameterAsync(Guid id, UpsertParameterRequest request, CancellationToken ct) =>
        PutAsync<UpsertParameterRequest, AssumptionSetDto>($"api/assumption-sets/{id}/parameters", request, ct);

    public Task<AssumptionSetDto> RemoveParameterAsync(Guid id, Guid parameterId, CancellationToken ct) =>
        DeleteReturningAsync<AssumptionSetDto>($"api/assumption-sets/{id}/parameters/{parameterId}", ct);

    // ---------- Simulation runs ----------

    public Task<PagedResponse<SimulationRunDto>> ListSimulationRunsAsync(Guid? workspaceId, Guid? roadmapId, Guid? assumptionSetId, int? page, int? pageSize, CancellationToken ct) =>
        GetAsync<PagedResponse<SimulationRunDto>>(
            Query("api/simulation-runs", ("workspaceId", workspaceId), ("roadmapId", roadmapId), ("assumptionSetId", assumptionSetId), ("page", page), ("pageSize", pageSize)), ct);

    public Task<SimulationRunDto> GetSimulationRunAsync(Guid id, CancellationToken ct) => GetAsync<SimulationRunDto>($"api/simulation-runs/{id}", ct);

    public Task<EngineResult> GetSimulationRunResultAsync(Guid id, CancellationToken ct) => GetAsync<EngineResult>($"api/simulation-runs/{id}/result", ct);

    public Task<EngineInput> GetSimulationRunInputAsync(Guid id, CancellationToken ct) => GetAsync<EngineInput>($"api/simulation-runs/{id}/input", ct);

    /// <summary>Firm-level diagnostics: opportunity funnels, sales-cycle length, expansions, and the
    /// mean utility gap on lost deals. 404s on an aggregate run.</summary>
    public Task<EngineFirmLevelDiagnostics> GetSimulationRunDiagnosticsAsync(Guid id, CancellationToken ct) =>
        GetAsync<EngineFirmLevelDiagnostics>($"api/simulation-runs/{id}/diagnostics", ct);

    public Task<SimulationRunDto> CreateSimulationRunAsync(CreateSimulationRunRequest request, CancellationToken ct) =>
        PostAsync<CreateSimulationRunRequest, SimulationRunDto>("api/simulation-runs", request, ct);

    public Task DeleteSimulationRunAsync(Guid id, CancellationToken ct) => DeleteAsync($"api/simulation-runs/{id}", ct);

    // ---------- Plumbing ----------

    private async Task<T> GetAsync<T>(string url, CancellationToken ct) => await SendAsync<T>(HttpMethod.Get, url, null, ct);

    private async Task<TRes> PostAsync<TReq, TRes>(string url, TReq body, CancellationToken ct) =>
        await SendAsync<TRes>(HttpMethod.Post, url, body, ct);

    private async Task<TRes> PostAsync<TRes>(string url, CancellationToken ct) =>
        await SendAsync<TRes>(HttpMethod.Post, url, null, ct);

    private async Task<TRes> PutAsync<TReq, TRes>(string url, TReq body, CancellationToken ct) =>
        await SendAsync<TRes>(HttpMethod.Put, url, body, ct);

    private async Task DeleteAsync(string url, CancellationToken ct) => await SendAsync<object?>(HttpMethod.Delete, url, null, ct, expectContent: false);

    private async Task<T> DeleteReturningAsync<T>(string url, CancellationToken ct) => await SendAsync<T>(HttpMethod.Delete, url, null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct, bool expectContent = true)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiConnectionException(http.BaseAddress?.ToString() ?? "(no base URL configured)", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // A timeout surfaces as TaskCanceledException when the caller's token was not the one cancelled.
            throw new ApiConnectionException(http.BaseAddress?.ToString() ?? "(no base URL configured)", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw await BuildRequestExceptionAsync(response, ct);

            if (!expectContent || response.StatusCode == HttpStatusCode.NoContent)
                return default!;

            var stream = await response.Content.ReadAsStreamAsync(ct);
            if (stream.Length == 0)
                return default!;

            var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
            return result!;
        }
    }

    private static async Task<ApiRequestException> BuildRequestExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? title = null;
        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(JsonOptions, ct);
            title = problem?.Title;
            detail = problem?.Detail;
        }
        catch (JsonException)
        {
            // Not every error response is ProblemDetails (e.g. a raw 401 with no body) — fall back below.
        }

        if (detail is null)
        {
            try
            {
                var raw = await response.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrWhiteSpace(raw)) detail = raw;
            }
            catch
            {
                // best effort only
            }
        }

        return new ApiRequestException(response.StatusCode, title, detail);
    }

    private static string Query(string path, params (string Key, object? Value)[] parameters)
    {
        var pairs = new List<string>();
        foreach (var (key, value) in parameters)
        {
            if (value is null) continue;
            var text = value switch
            {
                bool b => b ? "true" : "false",
                DateOnly d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
            };
            pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(text)}");
        }

        return pairs.Count == 0 ? path : $"{path}?{string.Join('&', pairs)}";
    }
}
