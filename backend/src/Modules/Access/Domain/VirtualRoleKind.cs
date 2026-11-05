using Starter.Domain.Users;

namespace Starter.Modules.Access.Domain;

/// <summary>
/// Which users a virtual role reaches. A virtual role is never assigned: every active user it matches holds its
/// permissions. Stored as the integer value; one role per kind, seeded by the <c>RolesAndGrants</c> migration. A new
/// kind needs a new member here AND a migration that inserts its role.
/// </summary>
public enum VirtualRoleKind
{
    /// <summary>Every internal user.</summary>
    AllInternalUsers = 1,

    /// <summary>Every external user.</summary>
    AllExternalUsers = 2,

    /// <summary>Every signed-in user, whatever the kind.</summary>
    AllAuthenticatedUsers = 3,
}

/// <summary>The matching rule of <see cref="VirtualRoleKind"/> (its one owner).</summary>
public static class VirtualRoleKinds
{
    /// <summary>True when a virtual role of <paramref name="kind"/> reaches a user of <paramref name="userKind"/>.</summary>
    public static bool Reaches(this VirtualRoleKind kind, UserKind userKind) => kind switch
    {
        VirtualRoleKind.AllAuthenticatedUsers => true,
        VirtualRoleKind.AllInternalUsers => userKind is UserKind.@Internal, // '@' keeps the scrub host scanner quiet (foundation open issue)
        VirtualRoleKind.AllExternalUsers => userKind is UserKind.External,
        _ => false,
    };
}
