using Codelab.Testing.Checks;
using Codelab.Testing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Starter.Infrastructure.Authorization;

namespace Starter.Contract.Tests.Support;

/// <summary>Who may call an allowlisted endpoint.</summary>
public enum EndpointAccess
{
    /// <summary>Anyone, signed in or not.</summary>
    Anonymous = 1,

    /// <summary>Any signed-in user; no permission key.</summary>
    Authenticated = 2,
}

/// <summary>One endpoint that carries no permission key, and why.</summary>
/// <param name="Key">The endpoint key (<c>METHOD /route</c>, see <see cref="EndpointDescriptions.KeyOf"/>).</param>
/// <param name="Access">Anonymous or authenticated-only.</param>
/// <param name="Owner">The module name, or <see cref="EndpointOwners.Kernel"/> / <see cref="EndpointOwners.Platform"/>.</param>
/// <param name="Reason">Why this endpoint needs no permission.</param>
public sealed record AllowlistEntry(string Key, EndpointAccess Access, string Owner, string Reason);

/// <summary>Owners of endpoints that do not belong to a module.</summary>
public static class EndpointOwners
{
    /// <summary>The kernel (Starter.Infrastructure, Starter.Api).</summary>
    public const string Kernel = "Kernel";

    /// <summary>The Codelab packages (security, observability).</summary>
    public const string Platform = "Platform";
}

/// <summary>
/// Rule <c>base.endpoint-contract</c>, permission part: every endpoint carries <see cref="HasPermissionAttribute"/>, or
/// is in the closed allowlist with the access it really has (anonymous, or authenticated-only). An allowlisted entry
/// whose owner is present must still exist; entries of a module that was removed are ignored, so removing a module
/// needs no edit here.
/// </summary>
public static class EndpointAccessContract
{
    /// <summary>Checks <paramref name="endpoints"/> against <paramref name="allowlist"/>.</summary>
    /// <param name="endpoints">The host's endpoints.</param>
    /// <param name="allowlist">The endpoints that carry no permission key.</param>
    /// <param name="presentOwners">The owners present in this host (module names plus kernel and platform).</param>
    public static CheckResult Check(IEnumerable<Endpoint> endpoints, IReadOnlyCollection<AllowlistEntry> allowlist, IReadOnlyCollection<string> presentOwners)
    {
        ArgumentNullException.ThrowIfNull(allowlist);
        ArgumentNullException.ThrowIfNull(presentOwners);
        var routable = EndpointDescriptions.Routable(endpoints);
        var entries = allowlist.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var violations = routable.Select(endpoint => Violation(endpoint, entries)).OfType<string>().ToList();
        var keys = routable.Select(EndpointDescriptions.KeyOf).ToHashSet(StringComparer.Ordinal);
        violations.AddRange(allowlist
            .Where(entry => presentOwners.Contains(entry.Owner, StringComparer.Ordinal) && !keys.Contains(entry.Key))
            .Select(entry => $"{entry.Key}: allowlisted for {entry.Owner} but no such endpoint exists (remove the stale entry)"));
        return new CheckResult("Every endpoint has a permission or an allowlist entry", violations, routable.Count);
    }

    /// <summary>The access an endpoint actually has, or null when it requires a permission key.</summary>
    public static EndpointAccess? AccessOf(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return EndpointAccess.Anonymous;
        }

        return endpoint.Metadata.GetMetadata<HasPermissionAttribute>() is null ? EndpointAccess.Authenticated : null;
    }

    private static string? Violation(RouteEndpoint endpoint, Dictionary<string, AllowlistEntry> entries)
    {
        var key = EndpointDescriptions.KeyOf(endpoint);
        var access = AccessOf(endpoint);
        var entry = entries.GetValueOrDefault(key);
        if (access is null)
        {
            return entry is null ? null : $"{key}: has a permission but is allowlisted as {entry.Access} (remove the entry)";
        }

        if (entry is null)
        {
            return $"{key}: {access} without a {nameof(HasPermissionAttribute)} and not in EndpointAllowlist";
        }

        return entry.Access == access ? null : $"{key}: allowlisted as {entry.Access} but is {access}";
    }
}
