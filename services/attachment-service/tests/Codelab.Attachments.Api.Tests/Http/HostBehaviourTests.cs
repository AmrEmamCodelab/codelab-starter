using System.Net;
using Codelab.Attachments.Api.Authentication;
using Codelab.Attachments.Api.Tests.TestSupport;
using Codelab.Attachments.Core.Errors;
using Codelab.Attachments.Core.Storage;
using Codelab.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Attachments.Api.Tests.Http;

/// <summary>Health, the upload-only CORS policy, ticket expiry and the authorization net over every endpoint.</summary>
public sealed class HostBehaviourTests : IClassFixture<AttachmentsServiceHostFixture>
{
    private const string PreflightMethod = "Access-Control-Request-Method";
    private const string PreflightHeaders = "Access-Control-Request-Headers";
    private const string AllowOrigin = "Access-Control-Allow-Origin";

    private readonly AttachmentsServiceHost _host;
    private readonly HttpClient _client;

    public HostBehaviourTests(AttachmentsServiceHostFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _host = fixture.Host;
        _client = fixture.Client;
    }

    [Theory]
    [InlineData(HealthEndpoints.LivePath)]
    [InlineData(HealthEndpoints.ReadyPath)]
    public async Task Health_is_anonymous_and_green(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_fails_while_the_storage_root_is_unreachable()
    {
        using var host = new AttachmentsServiceHost(settings => settings[$"{AttachmentStorageOptions.SectionName}:RootPath"] = Path.Combine(Path.GetTempPath(), "codelab-attachments-missing-" + AttachmentFileIds.New()));
        using var client = host.CreateClient();

        using var live = await client.GetAsync(HealthEndpoints.LivePath);
        using var ready = await client.GetAsync(HealthEndpoints.ReadyPath);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task The_upload_preflight_grants_the_configured_origin_the_ticket_header()
    {
        using var response = await _client.SendAsync(Preflight("/api/uploads", AttachmentsServiceHost.AllowedOrigin));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AttachmentsServiceHost.AllowedOrigin, Assert.Single(response.Headers.GetValues(AllowOrigin)));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("/api/uploads", "https://elsewhere.example.test")]
    [InlineData("/api/files/some-file", AttachmentsServiceHost.AllowedOrigin)]
    public async Task No_other_origin_or_endpoint_is_granted_cors(string path, string origin)
    {
        using var response = await _client.SendAsync(Preflight(path, origin));

        Assert.False(response.Headers.Contains(AllowOrigin));
    }

    [Fact]
    public async Task An_expired_ticket_is_refused()
    {
        using var host = new AttachmentsServiceHost();
        using var client = host.CreateClient();
        var ticket = host.SealTicket(lifetime: TimeSpan.FromMinutes(1));
        host.Clock.Advance(TimeSpan.FromMinutes(2));

        using var response = await client.SendAsync(host.UploadRequest(ticket, ("late.pdf", Samples.Pdf)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
    }

    [Fact]
    public void Every_endpoint_names_one_credential_and_only_health_is_anonymous()
    {
        var endpoints = _host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var policies = endpoints.ToDictionary(
            endpoint => $"{string.Join(',', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {endpoint.RoutePattern.RawText}",
            endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                ? "anonymous"
                : string.Join(',', endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy)));

        Assert.Equal(AttachmentsAuthentication.UploadTicketPolicy, policies["POST /api/uploads"]);
        Assert.Equal(AttachmentsAuthentication.ServiceCallerPolicy, policies["GET /api/files/{fileId}"]);
        Assert.Equal(AttachmentsAuthentication.ServiceCallerPolicy, policies["DELETE /api/files/{fileId}"]);
        Assert.All(
            policies.Where(entry => entry.Value == "anonymous"),
            entry => Assert.Contains(HealthEndpoints.BasePath, entry.Key, StringComparison.Ordinal));
        Assert.Equal(5, policies.Count);
    }

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add(PreflightMethod, "POST");
        request.Headers.Add(PreflightHeaders, "content-type,x-codelabtest-upload-ticket");
        return request;
    }
}
