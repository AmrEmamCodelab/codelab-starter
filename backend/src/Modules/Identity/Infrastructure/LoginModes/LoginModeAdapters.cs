using Codelab.Identity;
using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.LoginModes;

/// <summary>The one mapping from the package's <see cref="LoginMatchField"/> to the account's <see cref="AccountMatchField"/>.</summary>
public static class MatchFields
{
    /// <summary>The account field matching <paramref name="field"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="field"/> is not a defined member (the deployment guards refuse it at start).</exception>
    public static AccountMatchField From(LoginMatchField field) => field switch
    {
        LoginMatchField.Email => AccountMatchField.Email,
        LoginMatchField.LoginName => AccountMatchField.LoginName,
        LoginMatchField.Upn => AccountMatchField.Upn,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a login match field."),
    };
}

/// <summary>
/// <see cref="IDirectorySignIn"/> over the Codelab.Identity Active Directory provider. The provider is resolved on
/// first use: it is registered only when <c>Auth:Modes</c> lists <c>ad</c>, and the login-mode gate keeps every
/// directory endpoint closed otherwise.
/// </summary>
public sealed class ActiveDirectorySignIn(IOptions<ActiveDirectoryOptions> options, IServiceProvider services) : IDirectorySignIn
{
    /// <inheritdoc />
    public AccountMatchField MatchField => MatchFields.From(options.Value.MatchBy);

    /// <inheritdoc />
    public async Task<DirectoryVerdict> VerifyAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<IActiveDirectoryAuthenticator>().AuthenticateAsync(userName, password, cancellationToken);
        return result switch
        {
            { Status: DirectoryAuthStatus.Succeeded, Identity: { } identity } =>
                new DirectoryVerdict(DirectoryVerdictStatus.Accepted, LoginMatch.ValueOf(identity, options.Value.MatchBy)),
            { Status: DirectoryAuthStatus.AccountDisabled } => new DirectoryVerdict(DirectoryVerdictStatus.AccountDisabled, MatchValue: null),
            { Status: DirectoryAuthStatus.Unavailable } => new DirectoryVerdict(DirectoryVerdictStatus.Unavailable, MatchValue: null),
            _ => new DirectoryVerdict(DirectoryVerdictStatus.Refused, MatchValue: null),
        };
    }
}

/// <summary>
/// <see cref="ISsoProvider"/> over the Codelab.Identity OpenID Connect provider, resolved on first use (registered only
/// when <c>Auth:Modes</c> lists <c>sso</c>). The end_session URL uses the configured
/// <c>Codelab:Identity:Sso:PostLogoutRedirectUri</c>.
/// </summary>
public sealed class OidcSsoProvider(IOptions<OidcOptions> options, IServiceProvider services) : ISsoProvider
{
    /// <inheritdoc />
    public AccountMatchField MatchField => MatchFields.From(options.Value.MatchBy);

    /// <inheritdoc />
    public SsoAuthorization Begin(string returnUrl, string? bindingHash)
    {
        var request = Provider().BeginAuthorization(returnUrl);
        return new SsoAuthorization(
            request.AuthorizationUrl,
            new SsoPendingSignIn(
                State: request.State,
                CodeVerifier: request.CodeVerifier,
                Nonce: request.Nonce,
                ReturnUrl: request.ReturnUrl,
                BindingHash: bindingHash));
    }

    /// <inheritdoc />
    public async Task<SsoIdentityResult> CompleteAsync(string code, string state, SsoPendingSignIn pending, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var result = await Provider().CompleteAsync(
            new OidcCallback(Code: code, State: state),
            new OidcPendingAuthorization(State: pending.State, CodeVerifier: pending.CodeVerifier, Nonce: pending.Nonce),
            cancellationToken);
        return result switch
        {
            { Status: OidcSignInStatus.Succeeded, Identity: { } identity } =>
                new SsoIdentityResult(SsoIdentityStatus.Succeeded, MatchValue: LoginMatch.ValueOf(identity, options.Value.MatchBy), IdToken: identity.IdToken),
            { Status: OidcSignInStatus.Unavailable } => new SsoIdentityResult(SsoIdentityStatus.Unavailable, MatchValue: null, IdToken: null),
            _ => new SsoIdentityResult(SsoIdentityStatus.Rejected, MatchValue: null, IdToken: null),
        };
    }

    /// <inheritdoc />
    public Uri EndSessionUrl(string idTokenHint) => Provider().BuildEndSessionUrl(idTokenHint, postLogoutRedirectUri: null);

    private IOidcIdentityProvider Provider() => services.GetRequiredService<IOidcIdentityProvider>();
}
