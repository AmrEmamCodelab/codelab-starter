using System.Net.Http.Headers;
using Codelab.Testing.Contracts;
using Codelab.Testing.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Codelab.Testing.Tests.Contracts;

public sealed class ProblemJsonContractTests
{
    private static readonly MediaTypeHeaderValue Problem = new(ProblemJsonContract.ProblemJsonMediaType);

    [Fact]
    public async Task Probes_over_a_running_host_find_every_non_problem_error()
    {
        await using var app = EndpointFixtureHost.Build(MapProblemEndpoints);
        var endpoints = EndpointFixtureHost.EndpointsOf(app);
        using var client = await EndpointFixtureHost.StartAsync(app);

        var result = await ProblemJsonContract.AllErrorsAreProblemJsonAsync(client, endpoints);

        Assert.Equal(
        [
            "GET /mismatch: 400 body declares status 500",
            "GET /plain: 400 answered as 'text/plain', not application/problem+json",
        ], result.Violations);
        Assert.Equal(5, result.Examined);
    }

    [Fact]
    public async Task Probes_put_the_placeholder_in_route_values_and_send_a_body_where_one_is_read()
    {
        await using var app = EndpointFixtureHost.Build(MapProblemEndpoints);
        var options = new ProblemJsonOptions { ExcludedEndpoints = new HashSet<string>(["GET /ok"], StringComparer.Ordinal) };

        var probes = ProblemJsonContract.ProbesFor(EndpointFixtureHost.EndpointsOf(app), options);

        Assert.Contains(new ProblemJsonProbe(HttpMethod.Post, "/items/probe", "{}"), probes);
        Assert.Contains(new ProblemJsonProbe(HttpMethod.Get, "/problem", null), probes);
        Assert.DoesNotContain(probes, probe => probe.Path == "/ok");
    }

    [Fact]
    public void Required_members_and_json_shape_are_checked()
    {
        var options = new ProblemJsonOptions { RequiredMembers = ["type", "title", "status", "referenceCode"] };

        Assert.Equal(["404 body has no 'referenceCode'"], ProblemJsonContract.ProblemViolations(404, Problem, """{"type":"t","title":"x","status":404}""", options));
        Assert.Equal(["500 body is not a JSON object"], ProblemJsonContract.ProblemViolations(500, Problem, "[]", options));
        Assert.StartsWith("500 body is not JSON", Assert.Single(ProblemJsonContract.ProblemViolations(500, Problem, "<html>", options)), StringComparison.Ordinal);
        Assert.Equal(["401 answered as '<no content type>', not application/problem+json"], ProblemJsonContract.ProblemViolations(401, contentType: null, string.Empty, options));
        Assert.Empty(ProblemJsonContract.ProblemViolations(204, contentType: null, string.Empty, options));
    }

    private static void MapProblemEndpoints(WebApplication app)
    {
        app.MapGet("/problem", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found"));
        app.MapGet("/plain", () => Results.Text("no", statusCode: StatusCodes.Status400BadRequest));
        app.MapGet("/mismatch", () => Results.Json(new { type = "t", title = "x", status = 500 }, statusCode: StatusCodes.Status400BadRequest, contentType: ProblemJsonContract.ProblemJsonMediaType));
        app.MapGet("/ok", () => Results.Ok());
        app.MapPost("/items/{id}", (string id, ItemInput input) => Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(input.Name)] = [id] }));
    }

    private sealed record ItemInput(string? Name);
}
