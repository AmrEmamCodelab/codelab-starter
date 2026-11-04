using System.Reflection;
using Codelab.Testing.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Codelab.Testing.Contracts;

/// <summary>
/// Endpoint contracts of rule <c>base.endpoint-contract</c>, over the routing endpoints of a host (an
/// <c>EndpointDataSource</c> from a test server or <c>WebApplicationFactory</c>): every endpoint has a permission and
/// every request body has a validator.
/// </summary>
public static class EndpointContracts
{
    /// <summary>
    /// Every endpoint carries <paramref name="permissionAttributeType"/> metadata, except the anonymous ones, and the
    /// anonymous ones are a closed set: each must be in <paramref name="anonymousAllowlist"/> (endpoint keys, see
    /// <see cref="EndpointDescriptions.KeyOf"/>), each allowlist entry must still exist and must still be anonymous.
    /// A bare "any signed-in user" authorization is not enough: it is the silent default this contract refuses.
    /// </summary>
    public static CheckResult AllEndpointsHavePermission(IEnumerable<Endpoint> endpoints, Type permissionAttributeType, IReadOnlyCollection<string> anonymousAllowlist)
    {
        ArgumentNullException.ThrowIfNull(permissionAttributeType);
        ArgumentNullException.ThrowIfNull(anonymousAllowlist);
        var routable = EndpointDescriptions.Routable(endpoints);
        var allowlist = new HashSet<string>(anonymousAllowlist, StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var endpoint in routable)
        {
            var violation = PermissionViolation(endpoint, permissionAttributeType, allowlist);
            if (violation is not null)
            {
                violations.Add(violation);
            }
        }

        var keys = routable.Select(EndpointDescriptions.KeyOf).ToHashSet(StringComparer.Ordinal);
        violations.AddRange(allowlist.Where(entry => !keys.Contains(entry)).Order(StringComparer.Ordinal)
            .Select(entry => $"{entry}: allowlisted as anonymous but no such endpoint exists (remove the stale entry)"));
        return new CheckResult($"Every endpoint has a {permissionAttributeType.Name}", violations, routable.Count);
    }

    /// <summary>
    /// Every request body type of every endpoint has a validator according to <paramref name="hasValidator"/>.
    /// Endpoints in <paramref name="exemptEndpoints"/> (keys) are skipped. Endpoints without a body pass.
    /// </summary>
    public static CheckResult AllEndpointsHaveValidator(IEnumerable<Endpoint> endpoints, Func<Type, bool> hasValidator, IReadOnlyCollection<string>? exemptEndpoints = null)
    {
        ArgumentNullException.ThrowIfNull(hasValidator);
        var exempt = new HashSet<string>(exemptEndpoints ?? [], StringComparer.Ordinal);
        var routable = EndpointDescriptions.Routable(endpoints).Where(endpoint => !exempt.Contains(EndpointDescriptions.KeyOf(endpoint))).ToList();
        var violations = routable
            .SelectMany(endpoint => EndpointDescriptions.RequestBodyTypes(endpoint)
                .Where(type => !hasValidator(type))
                .Select(type => $"{EndpointDescriptions.KeyOf(endpoint)}: request body {type.Name} has no validator"));
        return new CheckResult("Every request body has a validator", violations, routable.Count);
    }

    /// <summary>
    /// A validator lookup for <see cref="AllEndpointsHaveValidator"/>: a type has a validator when a concrete class in
    /// <paramref name="assemblies"/> implements <paramref name="openValidatorInterface"/> closed over it (for example
    /// <c>typeof(IValidator&lt;&gt;)</c>).
    /// </summary>
    public static Func<Type, bool> ValidatorsIn(Type openValidatorInterface, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(openValidatorInterface);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (!openValidatorInterface.IsGenericTypeDefinition || openValidatorInterface.GetGenericArguments().Length != 1)
        {
            throw new ArgumentException("Pass an open generic interface with one type parameter, such as IValidator<>.", nameof(openValidatorInterface));
        }

        var validated = assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == openValidatorInterface)
            .Select(contract => contract.GetGenericArguments()[0])
            .ToHashSet();
        return validated.Contains;
    }

    private static string? PermissionViolation(RouteEndpoint endpoint, Type permissionAttributeType, HashSet<string> allowlist)
    {
        var key = EndpointDescriptions.KeyOf(endpoint);
        var isAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        var hasPermission = endpoint.Metadata.Any(permissionAttributeType.IsInstanceOfType);
        if (isAnonymous)
        {
            return allowlist.Contains(key) ? null : $"{key}: anonymous but not in the anonymous allowlist";
        }

        if (allowlist.Contains(key))
        {
            return $"{key}: in the anonymous allowlist but not anonymous (remove the entry)";
        }

        return hasPermission ? null : $"{key}: no {permissionAttributeType.Name} (and not allowlisted as anonymous)";
    }
}
