using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.LoginModes;

/// <summary>
/// The login modes this deployment has switched on (<c>Auth:Modes</c>), fixed at start: changing them is a
/// configuration change plus a restart, and the start fails when the list or a mode's provider section is incoherent.
/// </summary>
public interface ILoginModes
{
    /// <summary>The switched-on modes, in <see cref="SignInMode"/> order.</summary>
    IReadOnlyList<SignInMode> Enabled { get; }

    /// <summary>True when <paramref name="mode"/> is switched on.</summary>
    bool IsEnabled(SignInMode mode);
}

/// <summary>Which login methods the login page offers.</summary>
/// <param name="Basic">User name and password checked by this module.</param>
/// <param name="ActiveDirectory">User name and password checked by the organization's Active Directory.</param>
/// <param name="Sso">The organization's OpenID Connect identity provider.</param>
public sealed record EnabledLoginModes(bool Basic, bool ActiveDirectory, bool Sso);

/// <summary>Answers which login methods the login page offers (anonymous: modes only, never an identity).</summary>
public sealed class LoginModesQuery(ILoginModes modes)
{
    /// <summary>The switched-on modes.</summary>
    public EnabledLoginModes Execute() =>
        new(
            Basic: modes.IsEnabled(SignInMode.Basic),
            ActiveDirectory: modes.IsEnabled(SignInMode.ActiveDirectory),
            Sso: modes.IsEnabled(SignInMode.Sso));
}
