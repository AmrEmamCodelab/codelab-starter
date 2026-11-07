using System.Net;
using System.Text.Json;
using Codelab.Errors;
using Codelab.OpsConsole.Api.Diagnostics;
using Codelab.OpsConsole.Application.Certificates;
using Codelab.OpsConsole.Application.Ports;
using Codelab.OpsConsole.Application.ReferenceCodes;

namespace Codelab.OpsConsole.Api.Upstream;

/// <summary>
/// The typed client of the starter API's <c>/api/ops</c> (registered by <c>AddOpsApiClient</c>: base address and
/// timeout from <c>Codelab:OpsConsole:Api</c>, the correlation id forwarded, and a fresh ES256 service assertion on
/// every request from <c>AddCodelabServiceAssertion</c>). This class adds the operator header and maps every failure
/// to <see cref="UpstreamUnavailableException"/> (502 <c>UPSTREAM_UNAVAILABLE</c>); the status the API answered stays
/// in the log.
/// </summary>
internal sealed class OpsApiClient(HttpClient http, ILogger<OpsApiClient> logger) : IOpsApiClient
{
    /// <summary>The name of the HTTP client and of the dependency in the log.</summary>
    public const string HttpClientName = "ops-api";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<JsonElement> GetHealthAsync(OperatorName operatorName, CancellationToken cancellationToken) =>
        GetJsonAsync(OpsApiRoutes.Health, operatorName, cancellationToken);

    /// <inheritdoc />
    public Task<JsonElement> GetJobsAsync(OperatorName operatorName, CancellationToken cancellationToken) =>
        GetJsonAsync(OpsApiRoutes.Jobs, operatorName, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CertificateExpiry>> GetCertificatesAsync(OperatorName operatorName, CancellationToken cancellationToken)
    {
        var document = await GetJsonAsync(OpsApiRoutes.Certificates, operatorName, cancellationToken);
        return Deserialize<List<CertificateExpiry>>(document, OpsApiRoutes.Certificates);
    }

    /// <inheritdoc />
    public async Task<ApiReferenceCodeFacts?> FindReferenceCodeAsync(string referenceCode, OperatorName operatorName, CancellationToken cancellationToken)
    {
        var route = OpsApiRoutes.ReferenceCode(referenceCode);
        using var response = await SendAsync(route, operatorName, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var document = await ReadSuccessAsync(response, OpsApiRoutes.ReferenceCodes, cancellationToken);
        return Deserialize<ApiReferenceCodeDocument>(document, OpsApiRoutes.ReferenceCodes).ToFacts();
    }

    private async Task<JsonElement> GetJsonAsync(string route, OperatorName operatorName, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(route, operatorName, cancellationToken);
        return await ReadSuccessAsync(response, route, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(string route, OperatorName operatorName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(route, UriKind.Relative));
        request.Headers.TryAddWithoutValidation(OperatorHeader.Name, OperatorHeader.ValueFor(operatorName));
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            OpsConsoleLog.OpsApiUnreachable(logger, route, ex.HttpRequestError.ToString());
            throw new UpstreamUnavailableException(HttpClientName, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            OpsConsoleLog.OpsApiUnreachable(logger, route, nameof(TimeoutException));
            throw new UpstreamUnavailableException(HttpClientName, ex);
        }
    }

    private async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response, string route, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            OpsConsoleLog.OpsApiRefused(logger, route, (int)response.StatusCode);
            throw new UpstreamUnavailableException(HttpClientName);
        }

        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            OpsConsoleLog.OpsApiUnreachable(logger, route, nameof(JsonException));
            throw new UpstreamUnavailableException(HttpClientName, ex);
        }
    }

    private T Deserialize<T>(JsonElement document, string route)
    {
        try
        {
            return document.Deserialize<T>(Json) ?? throw new JsonException("The body is null.");
        }
        catch (JsonException ex)
        {
            OpsConsoleLog.OpsApiUnreachable(logger, route, nameof(JsonException));
            throw new UpstreamUnavailableException(HttpClientName, ex);
        }
    }

    /// <summary>The body of GET api/ops/reference-codes/{code}; every member optional.</summary>
    private sealed record ApiReferenceCodeDocument(string? CorrelationId, string? TraceId, DateTimeOffset? FirstSeen, ApiRequestDocument? Request)
    {
        public ApiReferenceCodeFacts ToFacts() => new()
        {
            CorrelationId = CorrelationId,
            TraceId = TraceId,
            FirstSeen = FirstSeen,
            Request = Request is { Method: { } method, Path: { } path, Status: { } status } ? new TracedRequest(method, path, status) : null,
        };
    }

    private sealed record ApiRequestDocument(string? Method, string? Path, int? Status);
}
