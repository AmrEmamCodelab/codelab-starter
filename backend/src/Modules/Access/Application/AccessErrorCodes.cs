namespace Starter.Modules.Access.Application;

/// <summary>
/// The Access module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.
/// </summary>
public static class AccessErrorCodes
{
    /// <summary>The access token carries no readable subject, so there is no user to resolve permissions for (403).</summary>
    public const string SubjectRequired = "ACCESS_SUBJECT_REQUIRED";

    /// <summary>No role has this id (404).</summary>
    public const string RoleNotFound = "ACCESS_ROLE_NOT_FOUND";

    /// <summary>Another role already uses this code (409).</summary>
    public const string RoleCodeTaken = "ACCESS_ROLE_CODE_TAKEN";

    /// <summary>Another role already uses this English or Arabic name (409).</summary>
    public const string RoleNameTaken = "ACCESS_ROLE_NAME_TAKEN";

    /// <summary>A permission key is not in the live catalog: unknown, or retired from the code (400).</summary>
    public const string UnknownPermission = "ACCESS_UNKNOWN_PERMISSION";

    /// <summary>A role id in a grant write names no role (400).</summary>
    public const string UnknownRole = "ACCESS_UNKNOWN_ROLE";

    /// <summary>A virtual role reaches its users by kind and is never assigned (400).</summary>
    public const string VirtualRoleNotAssignable = "ACCESS_VIRTUAL_ROLE_NOT_ASSIGNABLE";

    /// <summary>No user has this id (404).</summary>
    public const string UserNotFound = "ACCESS_USER_NOT_FOUND";

    /// <summary>The break-glass administrator holds every permission; its grants are not edited (409).</summary>
    public const string BreakGlassNotEditable = "ACCESS_BREAK_GLASS_NOT_EDITABLE";

    /// <summary>
    /// A grant write raises the caller's own grants, assigns a role wider than the caller holds
    /// <c>access.grants.manage</c>, or grants a permission the caller does not hold at least that widely (403).
    /// </summary>
    public const string ScopeExceedsCaller = "ACCESS_SCOPE_EXCEEDS_CALLER";

    /// <summary>
    /// The change would leave no active user (other than break-glass) able to manage both roles and grants (409).
    /// Resend with <c>confirmBreakGlassOnly: true</c> to accept that only the break-glass administrator remains.
    /// </summary>
    public const string LastManager = "ACCESS_LAST_MANAGER";

    /// <summary>
    /// A WARNING, not an error: the confirmed change left only the break-glass administrator able to manage roles
    /// and grants. Returned in the <c>warnings</c> list of a successful write.
    /// </summary>
    public const string OnlyBreakGlassManages = "ACCESS_ONLY_BREAK_GLASS_MANAGES";
}
