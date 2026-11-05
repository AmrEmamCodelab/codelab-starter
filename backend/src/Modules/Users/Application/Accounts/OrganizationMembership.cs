using Starter.Application.Results;
using Starter.Domain.Users;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>Whether an account of a given kind may belong to an organization.</summary>
public enum MembershipCheck
{
    /// <summary>The membership is allowed (or the account has no organization and needs none).</summary>
    Allowed = 1,

    /// <summary>An external account needs an organization.</summary>
    OrganizationRequired = 2,

    /// <summary>The organization does not exist.</summary>
    OrganizationNotFound = 3,

    /// <summary>The organization is deactivated and accepts no new member.</summary>
    OrganizationInactive = 4,

    /// <summary>The account's kind does not match the organization's kind.</summary>
    KindMismatch = 5,
}

/// <summary>
/// The one owner of the membership rule (admin form and import alike): an external account belongs to an active
/// external organization; an internal account belongs to an active internal organization or to none. An account that
/// keeps the organization it already has may stay in it after the organization was deactivated.
/// </summary>
public static class OrganizationMembership
{
    /// <summary>The check of an account of <paramref name="kind"/> in the requested organization.</summary>
    /// <param name="kind">The account's kind.</param>
    /// <param name="organizationRequested">True when an organization was named (by id or code); false for none.</param>
    /// <param name="organization">The named organization, or null when it does not exist (or none was named).</param>
    /// <param name="alreadyMember">True when the account already belongs to that organization (an edit).</param>
    public static MembershipCheck Check(UserKind kind, bool organizationRequested, Organization? organization, bool alreadyMember)
    {
        if (!organizationRequested)
        {
            return kind == UserKind.External ? MembershipCheck.OrganizationRequired : MembershipCheck.Allowed;
        }

        if (organization is null)
        {
            return MembershipCheck.OrganizationNotFound;
        }

        if (!organization.IsActive && !alreadyMember)
        {
            return MembershipCheck.OrganizationInactive;
        }

        var externalUser = kind == UserKind.External;
        var externalOrganization = organization.Kind == OrganizationKind.External;
        return externalUser == externalOrganization ? MembershipCheck.Allowed : MembershipCheck.KindMismatch;
    }

    /// <summary>The admin endpoints' refusal of a failed check, or null when allowed.</summary>
    public static Refusal? RefusalOf(MembershipCheck check) => check switch
    {
        MembershipCheck.Allowed => null,
        MembershipCheck.OrganizationNotFound => Refusal.Unprocessable(UsersErrorCodes.OrganizationNotFound),
        MembershipCheck.OrganizationInactive => Refusal.Unprocessable(UsersErrorCodes.OrganizationInactive),
        MembershipCheck.KindMismatch or MembershipCheck.OrganizationRequired => Refusal.Unprocessable(UsersErrorCodes.OrganizationKindMismatch),
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown membership check."),
    };
}
