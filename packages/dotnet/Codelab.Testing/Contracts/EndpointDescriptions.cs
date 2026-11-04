using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace Codelab.Testing.Contracts;

/// <summary>
/// Reads what the contract checks need from routing endpoints, for minimal APIs and MVC controllers alike: a stable key
/// (<c>GET /records/{id}</c>), the HTTP methods, the request body types and the handler parameters.
/// </summary>
public static class EndpointDescriptions
{
    private const string AnyMethod = "*";
    private const string RouteRoot = "/";
    private const char MethodSeparator = ',';

    /// <summary>The routable endpoints (those with a route pattern), ordered by key.</summary>
    public static IReadOnlyList<RouteEndpoint> Routable(IEnumerable<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return [.. endpoints.OfType<RouteEndpoint>().OrderBy(KeyOf, StringComparer.Ordinal)];
    }

    /// <summary>The routable endpoints of a data source.</summary>
    public static IReadOnlyList<RouteEndpoint> Routable(EndpointDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        return Routable(dataSource.Endpoints);
    }

    /// <summary>The key used in allowlists and messages: methods, a space, then the route (<c>GET,HEAD /x/{id}</c>).</summary>
    public static string KeyOf(RouteEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var methods = MethodsOf(endpoint);
        var methodText = methods.Count == 0 ? AnyMethod : string.Join(MethodSeparator, methods);
        return $"{methodText} {RouteOf(endpoint)}";
    }

    /// <summary>The route template with a leading slash.</summary>
    public static string RouteOf(RouteEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var raw = endpoint.RoutePattern.RawText ?? string.Empty;
        return raw.StartsWith(RouteRoot, StringComparison.Ordinal) ? raw : RouteRoot + raw;
    }

    /// <summary>The HTTP methods of the endpoint (empty means any method).</summary>
    public static IReadOnlyList<string> MethodsOf(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return [.. endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []];
    }

    /// <summary>The types the endpoint reads from the request body (minimal API accepts metadata and MVC body parameters).</summary>
    public static IReadOnlyList<Type> RequestBodyTypes(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var accepted = endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>().Select(metadata => metadata.RequestType).OfType<Type>();
        var controllerBodies = ControllerParameters(endpoint)
            .Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(parameter => parameter.ParameterType);
        return [.. accepted.Concat(controllerBodies).Distinct()];
    }

    /// <summary>The handler parameters by name (minimal API method or MVC action), for route-value type checks.</summary>
    public static IReadOnlyDictionary<string, Type> HandlerParameters(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var handler = endpoint.Metadata.GetMetadata<MethodInfo>();
        var fromHandler = handler?.GetParameters().Where(parameter => parameter.Name is not null)
            .Select(parameter => (parameter.Name!, parameter.ParameterType)) ?? [];
        var fromController = ControllerParameters(endpoint).Select(parameter => (parameter.Name, parameter.ParameterType));
        return fromHandler.Concat(fromController)
            .GroupBy(entry => entry.Item1, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Item2, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The declared response body types (produces metadata and the MVC action return type, unwrapped).</summary>
    public static IReadOnlyList<Type> ResponseTypes(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var produced = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(metadata => metadata.Type).OfType<Type>();
        var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo.ReturnType;
        var returned = action is null ? [] : new[] { WireTypes.UnwrapResult(action) }.OfType<Type>();
        return [.. produced.Concat(returned).Where(type => type != typeof(void)).Distinct()];
    }

    private static IEnumerable<Microsoft.AspNetCore.Mvc.Abstractions.ParameterDescriptor> ControllerParameters(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.Parameters ?? [];
}
