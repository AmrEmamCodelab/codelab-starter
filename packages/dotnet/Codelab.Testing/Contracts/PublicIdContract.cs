using Codelab.Testing.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Codelab.Testing.Contracts;

/// <summary>
/// Rule <c>base.endpoint-contract</c>, PublicId part: no raw database id crosses the wire. Route values named like ids
/// carry no numeric or Guid constraint and bind to a wire-id type, and every id-shaped member of a request or response
/// type (nested types included) is a wire-id type. The wire-id type is a predicate, so this package needs no
/// reference to the security package (for example <c>type =&gt; type.IsGenericType &amp;&amp;
/// type.GetGenericTypeDefinition() == typeof(PublicId&lt;&gt;)</c>).
/// </summary>
public static class PublicIdContract
{
    private const string IdSuffix = "id";
    private static readonly HashSet<string> RawIdConstraints = new(StringComparer.OrdinalIgnoreCase) { "int", "long", "guid", "decimal", "double", "float" };

    /// <summary>
    /// Checks every endpoint's id route values and its request and response types. <paramref name="allowlistedMembers"/>
    /// holds <c>Type.Member</c> entries that legitimately carry a number (lookup ids seeded by migrations).
    /// </summary>
    public static CheckResult AllIdsArePublicId(IEnumerable<Endpoint> endpoints, Func<Type, bool> isWireIdType, IReadOnlyCollection<string>? allowlistedMembers = null)
    {
        ArgumentNullException.ThrowIfNull(isWireIdType);
        var routable = EndpointDescriptions.Routable(endpoints);
        var routeViolations = routable.SelectMany(endpoint => RouteViolations(endpoint, isWireIdType));
        var wireTypes = routable.SelectMany(endpoint => EndpointDescriptions.RequestBodyTypes(endpoint).Concat(EndpointDescriptions.ResponseTypes(endpoint)));
        var typeResult = AllWireTypeIdsArePublicId(wireTypes, isWireIdType, allowlistedMembers);
        return new CheckResult("Every id on the wire is a PublicId", routeViolations.Concat(typeResult.Violations), routable.Count);
    }

    /// <summary>Checks the id-shaped members of <paramref name="wireTypes"/> and of the types nested in them.</summary>
    public static CheckResult AllWireTypeIdsArePublicId(IEnumerable<Type> wireTypes, Func<Type, bool> isWireIdType, IReadOnlyCollection<string>? allowlistedMembers = null)
    {
        ArgumentNullException.ThrowIfNull(wireTypes);
        ArgumentNullException.ThrowIfNull(isWireIdType);
        var allowlist = new HashSet<string>(allowlistedMembers ?? [], StringComparer.Ordinal);
        var walker = new WireTypeWalker(isWireIdType, allowlist);
        foreach (var type in wireTypes)
        {
            walker.Visit(type);
        }

        return new CheckResult("Every id member of a wire type is a PublicId", walker.Violations, walker.VisitedCount);
    }

    private static IEnumerable<string> RouteViolations(RouteEndpoint endpoint, Func<Type, bool> isWireIdType)
    {
        var key = EndpointDescriptions.KeyOf(endpoint);
        var handlerParameters = EndpointDescriptions.HandlerParameters(endpoint);
        foreach (var parameter in endpoint.RoutePattern.Parameters.Where(parameter => parameter.Name.EndsWith(IdSuffix, StringComparison.OrdinalIgnoreCase)))
        {
            var constraint = parameter.ParameterPolicies.Select(policy => policy.Content).FirstOrDefault(content => content is not null && RawIdConstraints.Contains(content));
            if (constraint is not null)
            {
                yield return $"{key}: route value {{{parameter.Name}}} is constrained to {constraint} (use a PublicId)";
            }

            if (handlerParameters.TryGetValue(parameter.Name, out var bound) && !isWireIdType(WireTypes.ElementOf(bound)))
            {
                yield return $"{key}: route value {{{parameter.Name}}} binds to {bound.Name}, not a PublicId";
            }
        }
    }

    private sealed class WireTypeWalker(Func<Type, bool> isWireIdType, HashSet<string> allowlist)
    {
        private readonly HashSet<Type> _visited = [];

        public List<string> Violations { get; } = [];

        public int VisitedCount => _visited.Count;

        public void Visit(Type type)
        {
            var element = WireTypes.ElementOf(type);
            if (!IsWalkable(element) || !_visited.Add(element))
            {
                return;
            }

            foreach (var property in WireTypes.WireProperties(element))
            {
                CheckMember(element, property.Name, property.PropertyType);
                Visit(property.PropertyType);
            }
        }

        private void CheckMember(Type owner, string member, Type memberType)
        {
            var qualified = $"{owner.Name}.{member}";
            if (WireTypes.IsIdName(member) && !allowlist.Contains(qualified) && !isWireIdType(WireTypes.ElementOf(memberType)))
            {
                Violations.Add($"{qualified} is {WireTypes.ElementOf(memberType).Name}, not a PublicId (or allowlist it as a lookup id)");
            }
        }

        // Framework and primitive types are leaves; wire-id types are leaves by definition.
        private bool IsWalkable(Type type) =>
            !type.IsPrimitive && !type.IsEnum && type != typeof(string) && !isWireIdType(type)
            && type.Namespace is not null && !type.Namespace.StartsWith(nameof(System), StringComparison.Ordinal)
            && !type.Namespace.StartsWith(nameof(Microsoft), StringComparison.Ordinal);
    }
}
