using Codelab.Security.PublicIds;

namespace Starter.Modules.Users.Application;

/// <summary>
/// The family of user ids on the wire (<c>PublicId&lt;UsersIds&gt;</c>). The label is the one the Identity module uses
/// for the signed-in user, so a user id from the session and one from the admin list are the same token family.
/// </summary>
public sealed class UsersIds : IIdDomain
{
    /// <summary>The domain label bound into every user PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "users";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>The family of organization ids on the wire (<c>PublicId&lt;OrganizationIds&gt;</c>).</summary>
public sealed class OrganizationIds : IIdDomain
{
    /// <summary>The domain label bound into every organization PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "organizations";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>The family of user import batch ids on the wire (<c>PublicId&lt;UserImportIds&gt;</c>).</summary>
public sealed class UserImportIds : IIdDomain
{
    /// <summary>The domain label bound into every import batch PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "user-imports";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}
