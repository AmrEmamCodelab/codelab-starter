namespace Starter.Modules.Users.Domain;

/// <summary>
/// Whose organization it is: the one running the portal (its departments) or an outside one (a partner entity).
/// Stored and sent as the integer value; labels come from the client's i18n bundles, never from the member names.
/// </summary>
public enum OrganizationKind
{
    /// <summary>A unit of the organization that runs the portal; its members are internal users.</summary>
    Internal = 1,

    /// <summary>An outside organization; its members are external users.</summary>
    External = 2,
}
