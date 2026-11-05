using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Starter.Api.Tests.Support;
using Starter.Infrastructure.Features;
using Starter.Infrastructure.Modularity;

namespace Starter.Api.Tests;

[Collection(HostTestGroup.Name)]
public sealed class HostTests
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task Features_endpoint_is_anonymous_and_returns_flags_only()
    {
        await using var app = await KernelTestHost.StartAsync(ModuleCatalog.FromModules([new ProbeModule()]));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/" + KernelRoutes.Features, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(["flags"], root.EnumerateObject().Select(property => property.Name));
        var flags = root.GetProperty("flags").EnumerateObject().ToList();
        var flag = Assert.Single(flags);
        Assert.Equal(Probe.Flag, flag.Name);
        Assert.Equal(JsonValueKind.False, flag.Value.ValueKind);
    }

    [Fact]
    public async Task Features_endpoint_reports_the_configured_value()
    {
        await using var app = await StartProbeAsync(flagOn: true);
        using var client = app.GetTestClient();

        var response = await client.GetFromJsonAsync<FeaturesResponse>(new Uri("/" + KernelRoutes.Features, UriKind.Relative));

        Assert.True(response!.Flags[Probe.Flag]);
    }

    [Fact]
    public async Task An_unknown_route_is_a_404_problem()
    {
        await using var app = await KernelTestHost.StartAsync(ModuleCatalog.Empty);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Theory]
    [InlineData("/api/probe/secure")]
    [InlineData("/api/probe/unmarked")]
    public async Task A_protected_endpoint_is_a_401_problem_for_anonymous_callers(string path)
    {
        await using var app = await StartProbeAsync(flagOn: false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Theory]
    [InlineData("/api/probe/missing", HttpStatusCode.NotFound, "NOT_FOUND")]
    [InlineData("/api/probe/denied", HttpStatusCode.Forbidden, "FORBIDDEN")]
    [InlineData("/api/probe/rule", HttpStatusCode.UnprocessableEntity, Probe.RuleCode)]
    [InlineData("/api/probe/refused", HttpStatusCode.Conflict, Probe.ConflictCode)]
    public async Task Domain_exceptions_and_refusals_are_problem_json(string path, HttpStatusCode status, string code)
    {
        await using var app = await StartProbeAsync(flagOn: false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        await AssertProblemAsync(response, status, code);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.NotFound)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task The_feature_gate_removes_the_action_while_the_flag_is_off(bool flagOn, HttpStatusCode expected)
    {
        await using var app = await StartProbeAsync(flagOn);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/api/probe/gated", UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_configured_switch_that_no_module_declares_refuses_the_start()
    {
        var overrides = new Dictionary<string, string?> { ["Features:misspelledFlag"] = "true" };

        var error = await Assert.ThrowsAnyAsync<Exception>(() => KernelTestHost.StartAsync(ModuleCatalog.Empty, overrides));

        Assert.Contains("misspelledFlag", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Modules_are_added_and_their_extras_mapped()
    {
        var module = new ProbeModule();
        await using var app = await KernelTestHost.StartAsync(ModuleCatalog.FromModules([module]));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri(Probe.ExtraRoute, UriKind.Relative));

        Assert.True(module.Added);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Same(module, app.Services.GetRequiredService<ModuleCatalog>().Modules.Single());
    }

    [Fact]
    public async Task Without_modules_no_module_controller_is_mapped()
    {
        await using var app = await KernelTestHost.StartAsync(ModuleCatalog.Empty);
        using var client = app.GetTestClient();

        using var features = await client.GetAsync(new Uri("/" + KernelRoutes.Features, UriKind.Relative));
        using var probe = await client.GetAsync(new Uri("/api/probe/missing", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, features.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, probe.StatusCode);
    }

    [Fact]
    public async Task Cors_preflight_allows_the_configured_origin_only()
    {
        await using var app = await KernelTestHost.StartAsync(ModuleCatalog.Empty);
        using var client = app.GetTestClient();

        using var allowed = await client.SendAsync(Preflight(KernelTestHost.AllowedOrigin));
        using var other = await client.SendAsync(Preflight("http://evil.example.test"));

        Assert.Equal([KernelTestHost.AllowedOrigin], allowed.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.False(other.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static Task<WebApplication> StartProbeAsync(bool flagOn) =>
        KernelTestHost.StartAsync(
            ModuleCatalog.FromModules([new ProbeModule()]),
            new Dictionary<string, string?> { ["Features:" + Probe.Flag] = flagOn ? "true" : "false" });

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, new Uri("/" + KernelRoutes.Features, UriKind.Relative));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
    }
}
