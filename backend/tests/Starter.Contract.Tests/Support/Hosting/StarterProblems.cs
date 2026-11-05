using System.Net;
using System.Text.Json;
using Codelab.Testing.Contracts;

namespace Starter.Tests.Hosting;

/// <summary>
/// What every error of the starter host looks like (the Codelab.Errors envelope): problem+json with title, status,
/// code, correlationId and referenceCode. The envelope omits <c>type</c>, which RFC 9457 then reads as
/// <c>about:blank</c>, so <c>type</c> is not required here.
/// </summary>
public static class StarterProblems
{
    /// <summary>The member that carries the machine-readable error code.</summary>
    public const string CodeMember = "code";

    /// <summary>The OpenAPI document endpoint: a framework endpoint, Development only, that answers an unknown document name in plain text.</summary>
    public const string OpenApiDocumentEndpoint = "GET /openapi/{documentName}.json";

    /// <summary>The problem+json contract options for the starter host.</summary>
    public static ProblemJsonOptions Options { get; } = new()
    {
        RequiredMembers = ["title", "status", CodeMember, "correlationId", "referenceCode"],
        ExcludedEndpoints = new HashSet<string>(StringComparer.Ordinal) { OpenApiDocumentEndpoint },
    };

    /// <summary>Asserts that <paramref name="response"/> is a problem with <paramref name="expected"/> status; returns its <c>code</c>.</summary>
    public static async Task<string?> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        var violations = ProblemJsonContract.ProblemViolations((int)response.StatusCode, response.Content.Headers.ContentType, body, Options);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty(CodeMember, out var code) ? code.GetString() : null;
    }
}
