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

    public Task<ClientSegmentDto> CreateClientSegmentAsync(CreateClientSegmentRequest request, CancellationToken ct) =>
        PostAsync<CreateClientSegmentRequest, ClientSegmentDto>("api/client-segments", request, ct);

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

    public Task<CoverageDto> CreateCoverageAsync(CreateCoverageRequest request, CancellationToken ct) =>
        PostAsync<CreateCoverageRequest, CoverageDto>("api/coverages", request, ct);

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
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
            };
            pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(text)}");
        }

        return pairs.Count == 0 ? path : $"{path}?{string.Join('&', pairs)}";
    }
}
