using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Codelab.OpsConsole.Api.Upstream;
using Codelab.Security;
using Codelab.Security.Replay;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Signing;

namespace Codelab.OpsConsole.Api.Tests.TestSupport;

/// <summary>One request the fake API received, with the verdict its own validator gave the assertion.</summary>
internal sealed record ReceivedApiCall(string Method, string PathAndQuery, string? Assertion, string? Operator, ServiceAssertionOutcome Outcome);

/// <summary>
/// The starter API's /api/ops as the console sees it: every request's assertion is checked by a real
/// <see cref="ServiceAssertionValidator"/> (the server side of Codelab.Security, its own replay guard), exactly as the
/// API would; a refused assertion answers 401. Routes answer the JSON configured per path (404 when none).
/// </summary>
internal sealed class FakeOpsApi : IDisposable
{
    public const string Audience = "starter-api";
    public const int MemoryEntryLimit = 1000;

    private readonly ConcurrentDictionary<string, (HttpStatusCode Status, string Json)> _answers = new(StringComparer.Ordinal);
    private readonly ServiceAssertionValidator _validator;

    public FakeOpsApi(SecurityProtocol protocol, string serviceName, string keyId, string publicKeyPem)
    {
        Protocol = protocol;
        var options = new ServiceAssertionServerOptions { Audience = Audience };
        options.TrustedServices.Add(new TrustedServiceOptions { ServiceName = serviceName, KeyId = keyId, PublicKeyPem = publicKeyPem });
        _validator = new ServiceAssertionValidator(options, new MemoryReplayGuard(TimeProvider.System, MemoryEntryLimit), TimeProvider.System, protocol);
    }

    public SecurityProtocol Protocol { get; }

    public ConcurrentQueue<ReceivedApiCall> Calls { get; } = new();

    public bool Unreachable { get; set; }

    public void Answer(string route, string json, HttpStatusCode status = HttpStatusCode.OK) => _answers["/" + route] = (status, json);

    /// <summary>Validates <paramref name="assertion"/> for a request; consumes its jti like the API does.</summary>
    public Task<ServiceAssertionResult> ValidateAsync(string? assertion, string method, Uri uri) =>
        _validator.ValidateAsync(
            new ServiceAssertionRequest(assertion, method, ServiceAssertionToken.Target(uri.AbsolutePath, uri.Query), SignatureCanonicalForm.HashBody([])),
            CancellationToken.None);

    public HttpMessageHandler CreateHandler() => new Handler(this);

    public void Dispose() => _validator.Dispose();

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        if (Unreachable)
        {
            throw new HttpRequestException(HttpRequestError.ConnectionError);
        }

        var uri = request.RequestUri!;
        var assertion = HeaderValue(request, Protocol.ServiceAssertionHeaderName);
        var verdict = await ValidateAsync(assertion, request.Method.Method, uri);
        Calls.Enqueue(new ReceivedApiCall(request.Method.Method, uri.PathAndQuery, assertion, HeaderValue(request, OperatorHeader.Name), verdict.Outcome));
        if (verdict.Outcome is not ServiceAssertionOutcome.Valid)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        return _answers.TryGetValue(uri.AbsolutePath, out var answer)
            ? new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Json, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string? HeaderValue(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private sealed class Handler(FakeOpsApi api) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            api.RespondAsync(request);
    }
}
