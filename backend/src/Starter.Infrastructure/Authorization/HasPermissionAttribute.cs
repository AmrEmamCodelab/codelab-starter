using Microsoft.AspNetCore.Authorization;

namespace Starter.Infrastructure.Authorization;

/// <summary>
/// Marks an endpoint that states who may call it: <see cref="HasPermissionAttribute"/> (a permission) or
/// <see cref="AuthenticatedUserAttribute"/> (any signed-in user, allowlisted). The contract tests require one of the
/// two on every non-anonymous endpoint.
/// </summary>
public interface IEndpointAccessDeclaration;

/// <summary>
/// Guards an endpoint on one permission key, for example <c>[HasPermission(DashboardPermissionKeys.View)]</c>
/// (always a constant of a <c>PermissionKeys</c> class, never a literal). The attribute is its own authorization
/// requirement: the Access module's permission handler checks the caller's live permissions on every request (the
/// token carries identity only). It must not set <see cref="AuthorizeAttribute.Policy"/>. Stacked attributes AND.
/// An unauthenticated caller gets 401, an authenticated caller without the key 403.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute, IAuthorizationRequirement, IAuthorizationRequirementData, IEndpointAccessDeclaration
{
    /// <summary>Creates the guard for <paramref name="permission"/>.</summary>
    public HasPermissionAttribute(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }

    /// <summary>The permission key this endpoint requires.</summary>
    public string Permission { get; }

    /// <inheritdoc />
    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return this;
    }

    /// <inheritdoc />
    public override string ToString() => $"{nameof(HasPermissionAttribute)}({Permission})";
}

/// <summary>
/// Marks an endpoint that any authenticated user may call and that needs no permission (logout, the caller's own
/// permissions, the signing key). Every such endpoint is listed in the contract tests' endpoint allowlist.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuthenticatedUserAttribute : AuthorizeAttribute, IEndpointAccessDeclaration;
