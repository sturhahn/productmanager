using System.Text.Json;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Tests;

/// <summary>
/// The API serializes camelCase property names and enum members by their declared C# name (see
/// Program.cs: <c>AddJsonOptions</c> with a bare <c>JsonStringEnumConverter()</c>, no naming
/// policy). These tests pin the client's <see cref="ProductManagerApiClient.JsonOptions"/> against
/// hand-written samples of that exact wire format, so a mismatch fails here instead of only at
/// the first real request.
/// </summary>
public class WireFormatTests
{
    [Fact]
    public void Deserializes_a_ProjectDto_with_PascalCase_enum_and_ISO_dates()
    {
        const string json = """
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "initiativeId": "22222222-2222-2222-2222-222222222222",
              "name": "Smart Order Router",
              "description": null,
              "status": "InProgress",
              "startDate": "2026-10-01",
              "endDate": "2026-11-15",
              "dependsOnProjectIds": [ "33333333-3333-3333-3333-333333333333" ],
              "deliveredFeatureIds": [],
              "effortDemands": [
                { "teamId": "44444444-4444-4444-4444-444444444444", "year": 2026, "month": 11, "points": 15.5 }
              ]
            }
            """;

        var dto = JsonSerializer.Deserialize<ProjectDto>(json, ProductManagerApiClient.JsonOptions)!;

        Assert.Equal("Smart Order Router", dto.Name);
        Assert.Equal(ProjectStatus.InProgress, dto.Status);
        Assert.Equal(new DateOnly(2026, 10, 1), dto.StartDate);
        Assert.Single(dto.DependsOnProjectIds);
        Assert.Equal(15.5m, dto.EffortDemands[0].Points);
    }

    [Fact]
    public void Deserializes_a_PagedResponse_and_ignores_the_computed_totalPages_field()
    {
        const string json = """
            {
              "items": [ { "id": "11111111-1111-1111-1111-111111111111", "name": "X", "createdAt": "2026-08-01T00:00:00Z" } ],
              "page": 1,
              "pageSize": 50,
              "totalCount": 1,
              "totalPages": 1
            }
            """;

        var page = JsonSerializer.Deserialize<PagedResponse<WorkspaceDto>>(json, ProductManagerApiClient.JsonOptions)!;

        Assert.Single(page.Items);
        Assert.Equal("X", page.Items[0].Name);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public void Deserializes_a_RoadmapFeasibilityDto_with_nested_enums()
    {
        const string json = """
            {
              "roadmapId": "11111111-1111-1111-1111-111111111111",
              "isFeasible": false,
              "capacityWarnings": [
                { "teamId": "22222222-2222-2222-2222-222222222222", "teamName": "Platform Engineering",
                  "year": 2026, "month": 11, "capacityPoints": 40, "demandPoints": 50, "overallocationPoints": 10, "capacityUndefined": false }
              ],
              "sequencingViolations": [
                { "kind": "DependencyCycle", "projectId": "33333333-3333-3333-3333-333333333333",
                  "dependsOnProjectId": "44444444-4444-4444-4444-444444444444", "description": "cycle" }
              ]
            }
            """;

        var feasibility = JsonSerializer.Deserialize<RoadmapFeasibilityDto>(json, ProductManagerApiClient.JsonOptions)!;

        Assert.False(feasibility.IsFeasible);
        Assert.Equal(10m, feasibility.CapacityWarnings[0].OverallocationPoints);
        Assert.Equal(SequencingViolationKind.DependencyCycle, feasibility.SequencingViolations[0].Kind);
    }

    [Fact]
    public void Serializes_a_request_enum_by_its_declared_name_not_camelCase()
    {
        var request = new CreateCoverageRequest(Guid.NewGuid(), Guid.NewGuid(), CoverageTier.NotPlaying);

        var json = JsonSerializer.Serialize(request, ProductManagerApiClient.JsonOptions);

        Assert.Contains("\"tier\":\"NotPlaying\"", json);
    }

    [Fact]
    public void Deserializes_a_ProblemDetails_error_body()
    {
        const string json = """{"type":"https://httpstatuses.io/409","title":"Conflict","status":409,"detail":"Something is already planned."}""";

        var problem = JsonSerializer.Deserialize<ProblemDetailsDtoForTests>(json, ProductManagerApiClient.JsonOptions)!;

        Assert.Equal("Conflict", problem.Title);
        Assert.Equal("Something is already planned.", problem.Detail);
    }
}

// ApiException.ProblemDetailsDto is internal to the Client namespace; this local mirror keeps the
// wire-format assertion above without changing that type's visibility just for a test.
internal sealed record ProblemDetailsDtoForTests(string? Title, string? Detail, int? Status, string? Type);
