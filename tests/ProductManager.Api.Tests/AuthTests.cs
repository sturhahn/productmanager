using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductManager.Api.Contracts;

namespace ProductManager.Api.Tests;

public sealed class AuthTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Correct_password_issues_a_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new TokenRequest(ApiFactory.Password), ApiFactory.Json);

        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ApiFactory.Json);
        Assert.NotNull(token);
        Assert.Equal("Bearer", token!.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new TokenRequest("not-the-password"), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_password_is_a_validation_error()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new { }, ApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_require_a_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_garbage_token_is_rejected()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync("/api/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_token_opens_the_api()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/workspaces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_is_anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_default_workspace_exists_on_first_run()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync();

        var workspace = await client.GetFromJsonAsync<WorkspaceDto>("/api/workspaces/default", ApiFactory.Json);

        Assert.NotNull(workspace);
        Assert.NotEqual(Guid.Empty, workspace!.Id);
    }
}
