using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Codelab.Testing.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Codelab.Testing.Contracts;

/// <summary>One request the problem+json contract sends.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path relative to the client's base address.</param>
/// <param name="JsonBody">A JSON body to send, or null for none.</param>
public sealed record ProblemJsonProbe(HttpMethod Method, string Path, string? JsonBody = null);

/// <summary>What the problem+json contract requires and how it builds probes from endpoints.</summary>
public sealed class ProblemJsonOptions
{
    /// <summary>Members every problem body must carry. Add the host's own (Codelab.Errors: <c>code</c>, <c>correlationId</c>, <c>referenceCode</c>).</summary>
    public IList<string> RequiredMembers { get; init; } = ["type", "title", "status"];

    /// <summary>The value put into every route parameter of a probe (an id no store holds, never a valid token).</summary>
    public string RouteValuePlaceholder { get; init; } = "probe";

    /// <summary>The JSON body sent to endpoints that accept one (empty, so validation must refuse it).</summary>
    public string EmptyJsonBody { get; init; } = "{}";

    /// <summary>Endpoint keys (see <see cref="EndpointDescriptions.KeyOf"/>) not probed, such as file downloads.</summary>
    public ISet<string> ExcludedEndpoints { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// Rule <c>base.endpoint-contract</c>, error part: every error answer is <c>application/problem+json</c> with the
/// required members and a <c>status</c> equal to the HTTP status. Probes run over a real host (a
/// <c>WebApplicationFactory</c> or test server client); an unauthenticated client probes the 401 path too.
/// </summary>
public static class ProblemJsonContract
{
    /// <summary>The problem details media type (RFC 9457).</summary>
    public const string ProblemJsonMediaType = "application/problem+json";

    private const string JsonMediaType = "application/json";
    private const string StatusMember = "status";
    private const int ErrorStatusFloor = 400;
    private static readonly HashSet<string> BodyMethods = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH" };

    /// <summary>One probe per routable endpoint (first method; route values replaced by the placeholder).</summary>
    public static IReadOnlyList<ProblemJsonProbe> ProbesFor(IEnumerable<Endpoint> endpoints, ProblemJsonOptions? options = null)
    {
        var settings = options ?? new ProblemJsonOptions();
        return [.. EndpointDescriptions.Routable(endpoints)
            .Where(endpoint => !settings.ExcludedEndpoints.Contains(EndpointDescriptions.KeyOf(endpoint)))
            .Select(endpoint => ProbeFor(endpoint, settings))];
    }

    /// <summary>Probes every routable endpoint and checks each error answer.</summary>
    public static Task<CheckResult> AllErrorsAreProblemJsonAsync(HttpClient client, IEnumerable<Endpoint> endpoints, ProblemJsonOptions? options = null, CancellationToken cancellationToken = default) =>
        CheckProbesAsync(client, ProbesFor(endpoints, options), options, cancellationToken);

    /// <summary>Sends <paramref name="probes"/> and checks each error answer. Success answers are counted but not checked.</summary>
    public static async Task<CheckResult> CheckProbesAsync(HttpClient client, IEnumerable<ProblemJsonProbe> probes, ProblemJsonOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(probes);
        var settings = options ?? new ProblemJsonOptions();
        var violations = new List<string>();
        var examined = 0;
        foreach (var probe in probes)
        {
            examined++;
            using var request = new HttpRequestMessage(probe.Method, probe.Path);
            if (probe.JsonBody is not null)
            {
                request.Content = new StringContent(probe.JsonBody, Encoding.UTF8, JsonMediaType);
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            violations.AddRange(ProblemViolations((int)response.StatusCode, response.Content.Headers.ContentType, body, settings)
                .Select(violation => $"{probe.Method} {probe.Path}: {violation}"));
        }

        return new CheckResult("Every error is problem+json", violations, examined);
    }

    /// <summary>The violations of one answer (none for a success status).</summary>
    public static IReadOnlyList<string> ProblemViolations(int status, MediaTypeHeaderValue? contentType, string body, ProblemJsonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (status < ErrorStatusFloor)
        {
            return [];
        }

        if (!string.Equals(contentType?.MediaType, ProblemJsonMediaType, StringComparison.OrdinalIgnoreCase))
        {
            return [$"{status} answered as '{contentType?.MediaType ?? "<no content type>"}', not {ProblemJsonMediaType}"];
        }

        return MemberViolations(status, body, options);
    }

    private static List<string> MemberViolations(int status, string body, ProblemJsonOptions options)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            return [$"{status} body is not JSON ({exception.Message})"];
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return [$"{status} body is not a JSON object"];
        }

        var violations = options.RequiredMembers.Where(member => !root.TryGetProperty(member, out _)).Select(member => $"{status} body has no '{member}'").ToList();
        if (root.TryGetProperty(StatusMember, out var declared) && (declared.ValueKind != JsonValueKind.Number || declared.GetInt32() != status))
        {
            violations.Add($"{status} body declares status {declared}");
        }

        return violations;
    }

    private static ProblemJsonProbe ProbeFor(RouteEndpoint endpoint, ProblemJsonOptions options)
    {
        var methods = EndpointDescriptions.MethodsOf(endpoint);
        var method = methods.Count > 0 ? methods[0] : HttpMethods.Get;
        var path = PathOf(endpoint.RoutePattern, options.RouteValuePlaceholder);
        var body = BodyMethods.Contains(method) ? options.EmptyJsonBody : null;
        return new ProblemJsonProbe(new HttpMethod(method), path, body);
    }

    private static string PathOf(RoutePattern pattern, string placeholder)
    {
        var segments = pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            _ => placeholder,
        })));
        return "/" + string.Join('/', segments);
    }
}
