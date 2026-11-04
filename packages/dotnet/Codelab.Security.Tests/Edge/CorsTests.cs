using System.Net;
using Codelab.Security.Cors;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;

namespace Codelab.Security.Tests.Edge;

[Collection(SharedCodecGroup.Name)]
public sealed class CorsTests
{
    private const string Origin = "https://app.example.test";

    [Theory]
    [InlineData("https://app.example.test", true, false, true)]
    [InlineData("https://app.example.test:8443", true, false, true)]
    [InlineData("http://app.example.test", true, false, false)]
    [InlineData("http://localhost:4200", true, true, true)]
    [InlineData("*", true, true, false)]
    [InlineData("*", false, true, true)]
    [InlineData("*", false, false, false)]
    [InlineData("https://app.example.test/", true, false, false)]
    [InlineData("https://app.example.test/path", true, false, false)]
    [InlineData("app.example.test", true, false, false)]
    public void The_validator_refuses_wildcards_with_credentials_and_plain_http_outside_development(string origin, bool credentials, bool development, bool valid)
    {
        var options = new CodelabCorsOptions { AllowedOrigins = [origin], AllowCredentials = credentials };

        Assert.Equal(valid, CorsOriginValidator.Validate(options, development).Count == 0);
    }

    [Fact]
    public async Task A_wildcard_with_credentials_fails_the_boot()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.Cors}:AllowedOrigins:0"] = CorsOriginValidator.Wildcard;

        await BootAssert.FailsAsync(options, "wildcard");
    }

    [Fact]
    public async Task A_preflight_is_granted_and_exposes_the_signature_headers()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.Cors}:AllowedOrigins:0"] = Origin;
        options.MapEndpoints = endpoints => endpoints.MapGet("/api/data", () => "ok").RequireCors(CodelabCorsOptions.PolicyName);
        options.ConfigureServices = _ => { };
        await using var host = await SecurityTestHost.StartAsync(options);
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/data");
        preflight.Headers.Add("Origin", Origin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "x-codelabtest-signature");

        var response = await host.Client.SendAsync(preflight);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task An_actual_cross_origin_response_exposes_the_response_signature_header()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.Cors}:AllowedOrigins:0"] = Origin;
        options.MapEndpoints = endpoints => endpoints.MapGet("/api/data", () => "ok").RequireCors(CodelabCorsOptions.PolicyName);
        await using var host = await SecurityTestHost.StartAsync(options);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");
        request.Headers.Add("Origin", Origin);

        var response = await host.Client.SendAsync(request);

        Assert.Contains("X-CODELABTEST-Response-Signature", response.Headers.GetValues("Access-Control-Expose-Headers").Single(), StringComparison.Ordinal);
    }
}
