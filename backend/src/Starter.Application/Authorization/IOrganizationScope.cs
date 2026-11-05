namespace Starter.Application.Authorization;

/// <summary>
/// How far a held permission reaches. Stored and sent as the integer value. When a user holds the same permission at
/// both scopes, <see cref="Global"/> wins.
/// </summary>
public enum PermissionScope
{
    /// <summary>Every organization's records.</summary>
    Global = 1,

    /// <summary>Only the records of the user's own organization.</summary>
    OwnOrganization = 2,
}

/// <summary>Whether the current user holds a permission, and how far it reaches.</summary>
/// <param name="Allowed">False when the user does not hold the permission (then the other members are null).</param>
/// <param name="Scope">The widest scope the user holds it at, or null when not allowed.</param>
/// <param name="OrganizationId">
/// The user's organization (internal id) when the scope is <see cref="PermissionScope.OwnOrganization"/>; otherwise
/// null.
/// </param>
public sealed record ScopeDecision(bool Allowed, PermissionScope? Scope, long? OrganizationId)
{
    /// <summary>The decision for a permission the user does not hold.</summary>
    public static ScopeDecision Denied { get; } = new(Allowed: false, Scope: null, OrganizationId: null);

    /// <summary>
    /// True when the decision reaches a record of organization <paramref name="organizationId"/> (null: a record with
    /// no organization). A global decision reaches every record; an own-organization decision only its own
    /// organization's records, and nothing when the user has no organization; a denied decision reaches nothing.
    /// </summary>
    public bool Reaches(long? organizationId) => Allowed && (Scope is PermissionScope.Global
        || (OrganizationId is { } own && organizationId == own));

    /// <summary>
    /// The organization filter of a list query that asked for <paramref name="requestedOrganizationId"/> (null: every
    /// organization), narrowed to this decision. <see cref="OrganizationFilter.MatchesNothing"/> is true when the
    /// decision reaches none of the requested records; the query must then return an empty page.
    /// </summary>
    public OrganizationFilter Narrow(long? requestedOrganizationId)
    {
        if (Allowed && Scope is PermissionScope.Global)
        {
            return new OrganizationFilter(MatchesNothing: false, OrganizationId: requestedOrganizationId);
        }

        return OrganizationId is { } own && Allowed && (requestedOrganizationId is null || requestedOrganizationId == own)
            ? new OrganizationFilter(MatchesNothing: false, OrganizationId: own)
            : OrganizationFilter.Nothing;
    }
}

/// <summary>A list query's organization filter after <see cref="ScopeDecision.Narrow"/>.</summary>
/// <param name="MatchesNothing">True when the query must return nothing (the caller reaches no requested record).</param>
/// <param name="OrganizationId">Only this organization's records, or null for every organization.</param>
public sealed record OrganizationFilter(bool MatchesNothing, long? OrganizationId)
{
    /// <summary>The filter that matches nothing.</summary>
    public static OrganizationFilter Nothing { get; } = new(MatchesNothing: true, OrganizationId: null);
}

/// <summary>
/// The organization scope of the current user's permissions (implemented by the Access module), for a module's
/// queries that must narrow to the user's own organization. Live: it reflects grant changes on the next request.
/// </summary>
public interface IOrganizationScope
{
    /// <summary>The current user's decision for <paramref name="permissionKey"/> (a <c>&lt;Name&gt;PermissionKeys</c> constant).</summary>
    Task<ScopeDecision> ScopeForAsync(string permissionKey, CancellationToken cancellationToken);
}
