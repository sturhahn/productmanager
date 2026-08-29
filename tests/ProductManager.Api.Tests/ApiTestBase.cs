using System.Net.Http.Json;
using ProductManager.Api.Contracts;

namespace ProductManager.Api.Tests;

/// <summary>
/// One fresh API + database per test. xUnit creates a new instance of the test class for every
/// test, so no test can see another's writes.
/// </summary>
public abstract class ApiTestBase : IAsyncLifetime
{
    protected ApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Factory = new ApiFactory();
        Client = await Factory.CreateAuthenticatedClientAsync();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>The workspace the API creates on first run.</summary>
    protected async Task<WorkspaceDto> DefaultWorkspaceAsync()
    {
        var response = await Client.GetAsync("/api/workspaces/default");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceDto>(ApiFactory.Json))!;
    }

    protected async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Client.PostAsJsonAsync(url, body, ApiFactory.Json);
        await AssertSuccess(response, url);
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }

    protected async Task<T> PutAsync<T>(string url, object body)
    {
        var response = await Client.PutAsJsonAsync(url, body, ApiFactory.Json);
        await AssertSuccess(response, url);
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }

    protected async Task<T> GetAsync<T>(string url)
    {
        var response = await Client.GetAsync(url);
        await AssertSuccess(response, url);
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }

    private static async Task AssertSuccess(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"{response.StatusCode} from {url}: {body}");
    }

    // ---------- Fixture builders ----------

    protected async Task<InitiativeDto> CreateInitiativeAsync(Guid workspaceId, string name) =>
        await PostAsync<InitiativeDto>("/api/initiatives",
            new CreateInitiativeRequest(workspaceId, name, null, null, null, null));

    protected async Task<ProjectDto> CreateProjectAsync(Guid initiativeId, string name,
        DateOnly? start = null, DateOnly? end = null) =>
        await PostAsync<ProjectDto>("/api/projects",
            new CreateProjectRequest(initiativeId, name, null, null, start, end));

    protected async Task<TeamDto> CreateTeamAsync(Guid workspaceId, string name) =>
        await PostAsync<TeamDto>("/api/teams", new CreateTeamRequest(workspaceId, name, null));

    protected async Task<RoadmapDto> CreateRoadmapAsync(Guid workspaceId, string name, bool isLive = false) =>
        await PostAsync<RoadmapDto>("/api/roadmaps", new CreateRoadmapRequest(workspaceId, name, null, isLive));
}
