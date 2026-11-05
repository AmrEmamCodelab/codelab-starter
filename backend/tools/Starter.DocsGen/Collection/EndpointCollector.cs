using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Starter.Infrastructure.Authorization;

namespace Starter.DocsGen.Collection;

/// <summary>
/// Turns the host's routed endpoints into <see cref="EndpointDoc"/> rows: one per HTTP method, with who may call it
/// (anonymous, authenticated or the permissions), the owner and a one-line summary.
/// </summary>
internal static class EndpointCollector
{
    /// <summary>The method label of an endpoint that matches every HTTP method.</summary>
    public const string AnyMethod = "ANY";

    private const string RouteSeparator = "/";

    /// <summary>Collects the rows, sorted by route, then method.</summary>
    public static IReadOnlyList<EndpointDoc> Collect(IEnumerable<Endpoint> endpoints, OwnerNaming owners, XmlDocumentation documentation)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(documentation);
        return [.. endpoints.OfType<RouteEndpoint>()
            .SelectMany(endpoint => Describe(endpoint, owners, documentation))
            .Distinct(EndpointDocComparer.Instance)
            .OrderBy(row => row.Route, StringComparer.Ordinal)
            .ThenBy(row => row.Method, StringComparer.Ordinal)];
    }

    /// <summary>Who may call an endpoint with <paramref name="metadata"/>: anonymous wins, then permissions, else authenticated (the fallback policy).</summary>
    public static (EndpointAccessKind Access, IReadOnlyList<string> Permissions) ClassifyAccess(IReadOnlyCollection<object> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return (EndpointAccessKind.Anonymous, []);
        }

        var permissions = metadata.OfType<HasPermissionAttribute>()
            .Select(attribute => attribute.Permission)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return permissions.Count > 0
            ? (EndpointAccessKind.Permission, permissions)
            : (EndpointAccessKind.Authenticated, []);
    }

    /// <summary>The route with exactly one leading slash.</summary>
    public static string NormalizeRoute(string? rawRoute) =>
        RouteSeparator + (rawRoute ?? string.Empty).TrimStart(RouteSeparator[0]);

    private static IEnumerable<EndpointDoc> Describe(RouteEndpoint endpoint, OwnerNaming owners, XmlDocumentation documentation)
    {
        var metadata = endpoint.Metadata.ToList();
        var (access, permissions) = ClassifyAccess(metadata);
        var handler = HandlerOf(metadata);
        var owner = handler?.DeclaringType is { } declaringType ? owners.OwnerOf(declaringType) : OwnerNaming.UnknownOwner;
        var summary = metadata.OfType<IEndpointSummaryMetadata>().LastOrDefault()?.Summary
            ?? (handler is null ? string.Empty : documentation.SummaryOf(handler));
        var route = NormalizeRoute(endpoint.RoutePattern.RawText);
        var methods = metadata.OfType<IHttpMethodMetadata>().LastOrDefault()?.HttpMethods ?? [];
        var labels = methods.Count == 0 ? [AnyMethod] : methods;
        return labels.Select(method => new EndpointDoc(method, route, access, permissions, owner, summary));
    }

    private static MethodInfo? HandlerOf(IReadOnlyCollection<object> metadata) =>
        metadata.OfType<ControllerActionDescriptor>().LastOrDefault()?.MethodInfo
        ?? metadata.OfType<MethodInfo>().LastOrDefault();

    private sealed class EndpointDocComparer : IEqualityComparer<EndpointDoc>
    {
        public static readonly EndpointDocComparer Instance = new();

        public bool Equals(EndpointDoc? x, EndpointDoc? y) =>
            x is not null && y is not null
            && string.Equals(x.Method, y.Method, StringComparison.Ordinal)
            && string.Equals(x.Route, y.Route, StringComparison.Ordinal);

        public int GetHashCode(EndpointDoc obj) => HashCode.Combine(obj.Method, obj.Route);
    }
}
