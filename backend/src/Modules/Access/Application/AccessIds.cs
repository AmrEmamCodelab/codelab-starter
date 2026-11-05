using Codelab.Security.PublicIds;

namespace Starter.Modules.Access.Application;

/// <summary>
/// The family of role ids on the wire (<c>PublicId&lt;RoleIds&gt;</c>). It lives in Application because request bodies
/// (the grant write) carry role ids and are validated here; the controllers and responses use it too.
/// </summary>
public sealed class RoleIds : IIdDomain
{
    /// <summary>The domain label bound into every role PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "roles";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>
/// The family of user ids on the wire (<c>PublicId&lt;UserIds&gt;</c>), label <c>users</c>: the same label as the
/// Identity module's user ids, so a user's PublicId from the sign-in response or the users list opens their grants.
/// </summary>
public sealed class UserIds : IIdDomain
{
    /// <summary>The domain label bound into every user PublicId (shared with Identity and Users; never rename).</summary>
    public const string DomainLabel = "users";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}
