using System.Net;
using Codelab.Security.PublicIds;
using Package.Sample.Api.Hosting;
using Package.Sample.Api.Records;
using Package.Sample.Tests.Support;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>Exit criterion: the security headers, on success and on error responses alike.</summary>
public sealed class SecurityHeadersTests : IDisposable
{
    private readonly SampleHost _host = new(SampleKeys.Create());

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_json_response_carries_the_security_headers_and_hsts()
    {
        using var client = _host.NewClient();

        var response = await client.GetAsync(SampleRoutes.RecordFor(PublicId<RecordIds>.From(RecordStore.SeedRecordId).ToToken()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
        Assert.StartsWith("max-age=31536000", Single(response, "Strict-Transport-Security"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_error_response_carries_them_too()
    {
        using var client = _host.NewClient();

        var response = await client.GetAsync("/api/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Single(response, "Referrer-Policy"));
        Assert.Contains("default-src 'none'", Single(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Server"));
        Assert.False(response.Headers.Contains("X-Powered-By"));
    }

    private static string Single(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? values.Single()
            : throw new Xunit.Sdk.XunitException($"Header {name} is missing.");
}
