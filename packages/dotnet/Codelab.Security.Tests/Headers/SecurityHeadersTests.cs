using System.Net;
using Codelab.Security.Headers;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Codelab.Security.Tests.Headers;

[Collection(SharedCodecGroup.Name)]
public sealed class SecurityHeadersTests
{
    private static readonly TestSession Session = new("7", "s1", "m1");

    [Theory]
    [InlineData("/json", HttpStatusCode.OK)]
    [InlineData("/text", HttpStatusCode.OK)]
    [InlineData("/missing", HttpStatusCode.NotFound)]
    [InlineData("/boom", HttpStatusCode.InternalServerError)]
    public async Task Every_response_carries_the_baseline(string path, HttpStatusCode expected)
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
        foreach (var (name, value) in SecurityHeadersMiddleware.Baseline)
        {
            Assert.Equal(value, Header(response, name));
        }

        Assert.Null(Header(response, "X-XSS-Protection"));
        Assert.Null(Header(response, "Server"));
        Assert.Null(Header(response, "X-Powered-By"));
    }

    [Fact]
    public async Task A_pre_existing_header_value_is_overwritten_and_server_headers_removed()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("/overwrite");

        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Null(Header(response, "X-Powered-By"));
    }

    [Fact]
    public async Task The_strict_csp_applies_to_json_and_problem_json_outside_relaxed_prefixes()
    {
        await using var host = await StartAsync();

        Assert.Equal(SecurityHeadersMiddleware.JsonContentSecurityPolicy, Header(await host.Client.GetAsync("/json"), "Content-Security-Policy"));
        Assert.Equal(SecurityHeadersMiddleware.JsonContentSecurityPolicy, Header(await host.Client.GetAsync("/problem"), "Content-Security-Policy"));
        Assert.Null(Header(await host.Client.GetAsync("/text"), "Content-Security-Policy"));
        Assert.Null(Header(await host.Client.GetAsync("/swagger/json"), "Content-Security-Policy"));
        Assert.Equal(SecurityHeadersMiddleware.JsonContentSecurityPolicy, Header(await host.Client.GetAsync("/swaggerish/json"), "Content-Security-Policy"));
    }

    [Fact]
    public async Task Authenticated_json_is_no_store_and_anonymous_json_is_not()
    {
        await using var host = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/json");
        Session.ApplyTo(request);

        Assert.Equal("no-store", (await host.Client.SendAsync(request)).Headers.CacheControl?.ToString());
        Assert.Null((await host.Client.GetAsync("/json")).Headers.CacheControl);
    }

    [Fact]
    public async Task Hsts_is_sent_over_https_to_a_real_host_only()
    {
        await using var host = await StartAsync();

        Assert.Null(Header(await host.Client.GetAsync("http://app.example.test/json"), "Strict-Transport-Security"));
        Assert.Equal("max-age=31536000; includeSubDomains", Header(await host.Client.GetAsync("https://app.example.test/json"), "Strict-Transport-Security"));
        Assert.Null(Header(await host.Client.GetAsync("https://localhost/json"), "Strict-Transport-Security"));
    }

    [Fact]
    public async Task Disabling_the_section_writes_nothing()
    {
        await using var host = await StartAsync(settings => settings[$"{SecuritySections.Headers}:Enabled"] = "false");

        Assert.Null(Header(await host.Client.GetAsync("/json"), "X-Content-Type-Options"));
    }

    [Fact]
    public void The_hsts_policy_reflects_its_options_and_preload_must_be_coherent()
    {
        Assert.Equal("max-age=63072000; includeSubDomains; preload", new HstsOptions { MaxAgeDays = 730, Preload = true }.ToHeaderValue());
        Assert.Null(new HstsOptions().Incoherence());
        Assert.False(new HstsOptions().Preload);
        Assert.NotNull(new HstsOptions { Preload = true, IncludeSubDomains = false }.Incoherence());
        Assert.NotNull(new HstsOptions { Preload = true, MaxAgeDays = 30 }.Incoherence());
    }

    [Fact]
    public async Task An_incoherent_hsts_configuration_fails_the_boot()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.Headers}:Hsts:Preload"] = "true";
        options.Settings[$"{SecuritySections.Headers}:Hsts:MaxAgeDays"] = "30";

        await BootAssert.FailsAsync(options, "Preload");
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values) ? string.Join(",", values) : null;

    private static Task<SecurityTestHost> StartAsync(Action<Dictionary<string, string?>>? configure = null)
    {
        var options = SecurityTestHost.BaseOptions();
        configure?.Invoke(options.Settings);
        var withEndpoints = new TestHostOptions
        {
            MapEndpoints = endpoints =>
            {
                endpoints.MapGet("/json", () => TypedResults.Ok(new { ok = true }));
                endpoints.MapGet("/text", () => TypedResults.Text("plain"));
                endpoints.MapGet("/problem", () => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict));
                endpoints.MapGet("/swagger/json", () => TypedResults.Ok(new { ok = true }));
                endpoints.MapGet("/swaggerish/json", () => TypedResults.Ok(new { ok = true }));
                endpoints.MapGet("/boom", ThrowingEndpoint);
                endpoints.MapGet("/overwrite", (HttpContext context) =>
                {
                    context.Response.Headers["X-Frame-Options"] = "ALLOWALL";
                    context.Response.Headers["X-Powered-By"] = "test";
                    return TypedResults.Ok();
                });
            },
        };
        foreach (var (key, value) in options.Settings)
        {
            withEndpoints.Settings[key] = value;
        }

        return SecurityTestHost.StartAsync(withEndpoints);
    }

    private static StatusCodeHttpResult ThrowingEndpoint() => TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
}
