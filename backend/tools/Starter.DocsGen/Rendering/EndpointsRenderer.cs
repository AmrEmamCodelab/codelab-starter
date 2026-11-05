using Starter.DocsGen.Collection;

namespace Starter.DocsGen.Rendering;

/// <summary>Renders <c>endpoints.md</c>: every routed endpoint and who may call it.</summary>
internal static class EndpointsRenderer
{
    /// <summary>The access text of an anonymous endpoint.</summary>
    public const string AnonymousAccess = "anonymous (allowlisted)";

    /// <summary>The access text of an authenticated-only endpoint.</summary>
    public const string AuthenticatedAccess = "authenticated (allowlisted)";

    private const string Title = "Endpoints";
    private const string Intro =
        "Every routed endpoint of the API host in Development (the OpenAPI document is mapped in Development only). " +
        "An endpoint either requires permissions (`[HasPermission]`, all listed keys) or is allowlisted as anonymous " +
        "or authenticated in `backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`.";

    private const string NoEndpoints = "The host maps no endpoint.";
    private const string PermissionPrefix = "permission ";
    private const string PermissionSeparator = " + ";

    private static readonly string[] Headers = ["Method", "Route", "Access", "Owner", "Summary"];

    /// <summary>The table, sorted by route, then method.</summary>
    public static string Render(IReadOnlyList<EndpointDoc> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var rows = endpoints
            .OrderBy(endpoint => endpoint.Route, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.Method, StringComparer.Ordinal)
            .Select(endpoint => (IReadOnlyList<string>)
            [
                endpoint.Method,
                MarkdownText.Code(endpoint.Route),
                DescribeAccess(endpoint),
                endpoint.Owner,
                endpoint.Summary,
            ]);
        return MarkdownText.Document(Title).Line(Intro).Line().Table(Headers, rows, NoEndpoints).ToString();
    }

    /// <summary>The access column text.</summary>
    public static string DescribeAccess(EndpointDoc endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.Access switch
        {
            EndpointAccessKind.Anonymous => AnonymousAccess,
            EndpointAccessKind.Authenticated => AuthenticatedAccess,
            EndpointAccessKind.Permission => PermissionPrefix + string.Join(PermissionSeparator, endpoint.Permissions.Select(MarkdownText.Code)),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Access, "Unknown endpoint access kind."),
        };
    }
}
