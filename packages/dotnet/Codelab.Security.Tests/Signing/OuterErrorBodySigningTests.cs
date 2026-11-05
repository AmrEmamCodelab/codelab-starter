using System.Net;
using System.Text;
using Codelab.Security.Signing;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Codelab.Security.Tests.Signing;

/// <summary>
/// A host writes the problem body for an empty 4xx/5xx in middleware that runs BEFORE the security pipeline
/// (UseStatusCodePages, UseExceptionHandler). The response signature must cover that body, or a verifying client
/// refuses every 401, 403 and 500 as a signature mismatch.
/// </summary>
[Collection(SharedCodecGroup.Name)]
public sealed class OuterErrorBodySigningTests : IAsyncLifetime
{
    private const string ProblemContentType = "application/problem+json";
    private const string ForbiddenPath = "/api/forbidden";
    private const string UnauthorizedPath = "/api/unauthorized";
    private const string FailingPath = "/api/failing";
    private const string EmptyOkPath = "/api/empty";
    private const string PartialBody = "partial-endpoint-output";

    private readonly string _master = TestKeys.NewKey();
    private SecurityTestHost _host = null!;

    private RequestSigner Signer => SigningHost.Signer(_master);

    public async Task InitializeAsync()
    {
        var options = SigningHost.Options(SecurityEnforcementMode.Enforce, _master);
        var baseEndpoints = options.MapEndpoints;
        options.ConfigureOuterPipeline = UseHostErrorPages;
        options.MapEndpoints = endpoints =>
        {
            baseEndpoints?.Invoke(endpoints);
            MapErrorEndpoints(endpoints);
        };
        _host = await SecurityTestHost.StartAsync(options);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData(ForbiddenPath, HttpStatusCode.Forbidden)]
    [InlineData(UnauthorizedPath, HttpStatusCode.Unauthorized)]
    [InlineData(FailingPath, HttpStatusCode.InternalServerError)]
    public async Task An_error_body_written_by_an_outer_middleware_is_covered_by_the_response_signature(string path, HttpStatusCode status)
    {
        using var request = SigningHost.Request(HttpMethod.Get, path);
        var nonce = Signer.Sign(request, _host.Clock.GetUtcNow());

        var response = await _host.Client.SendAsync(request);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(await Signer.VerifyResponseAsync(response, nonce));
    }

    [Fact]
    public async Task An_empty_success_response_is_still_signed_over_the_empty_body()
    {
        using var request = SigningHost.Request(HttpMethod.Get, EmptyOkPath);
        var nonce = Signer.Sign(request, _host.Clock.GetUtcNow());

        var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await Signer.VerifyResponseAsync(response, nonce));
    }

    [Fact]
    public async Task A_body_the_endpoint_wrote_before_it_failed_is_not_part_of_the_signed_error_body()
    {
        using var request = SigningHost.Request(HttpMethod.Get, FailingPath + "?partial=true");
        var nonce = Signer.Sign(request, _host.Clock.GetUtcNow());

        var response = await _host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(PartialBody, body, StringComparison.Ordinal);
        Assert.True(await Signer.VerifyResponseAsync(response, nonce));
    }

    private static void UseHostErrorPages(WebApplication app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = WriteProblemAsync });
        app.UseStatusCodePages(context => WriteProblemAsync(context.HttpContext));
    }

    private static Task WriteProblemAsync(HttpContext context)
    {
        context.Response.ContentType = ProblemContentType;
        return context.Response.WriteAsync($"{{\"status\":{context.Response.StatusCode}}}", Encoding.UTF8);
    }

    private static void MapErrorEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ForbiddenPath, () => TypedResults.Ok()).RequireAuthorization(policy => policy.RequireAssertion(_ => false));
        endpoints.MapGet(UnauthorizedPath, () => TypedResults.Unauthorized());
        endpoints.MapGet(EmptyOkPath, () => TypedResults.NoContent());
        endpoints.MapGet(FailingPath, FailAsync);
    }

    private static async Task FailAsync(HttpContext context, bool partial = false)
    {
        if (partial)
        {
            await context.Response.WriteAsync(PartialBody);
        }

        throw new InvalidOperationException("The endpoint failed.");
    }
}
