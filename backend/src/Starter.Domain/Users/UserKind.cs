namespace Starter.Domain.Users;

/// <summary>
/// Who a user is to the organization running the portal. Stored and sent as the integer value; labels come from the
/// client's i18n bundles, never from the member names.
/// </summary>
public enum UserKind
{
    /// <summary>Staff of the organization that runs the portal (the default).</summary>
    Internal = 1,

    /// <summary>A user from an outside organization (a partner entity or a member of the public).</summary>
    External = 2,
}
