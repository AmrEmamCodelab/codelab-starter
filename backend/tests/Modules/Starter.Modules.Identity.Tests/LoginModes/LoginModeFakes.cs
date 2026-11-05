using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Tests.LoginModes;

internal sealed class FakeLoginModes(params SignInMode[] enabled) : ILoginModes
{
    public IReadOnlyList<SignInMode> Enabled { get; } = enabled;

    public bool IsEnabled(SignInMode mode) => Enabled.Contains(mode);
}

internal sealed class FakeDirectory(AccountMatchField matchField = AccountMatchField.LoginName) : IDirectorySignIn
{
    public DirectoryVerdict Verdict { get; set; } = new(DirectoryVerdictStatus.Refused, MatchValue: null);

    public List<(string UserName, string Password)> Calls { get; } = [];

    public AccountMatchField MatchField => matchField;

    public Task<DirectoryVerdict> VerifyAsync(string userName, string password, CancellationToken cancellationToken)
    {
        Calls.Add((userName, password));
        return Task.FromResult(Verdict);
    }
}

/// <summary>An identity provider fake: state/verifier/nonce are numbered; the callback answers <see cref="Result"/>.</summary>
internal sealed class FakeSsoProvider(AccountMatchField matchField = AccountMatchField.Email) : ISsoProvider
{
    public const string AuthorizeBase = "https://idp.example.test/authorize";
    public const string EndSessionBase = "https://idp.example.test/end-session";

    private int _started;

    public int Begins => _started;

    public SsoIdentityResult Result { get; set; } = new(SsoIdentityStatus.Rejected, MatchValue: null, IdToken: null);

    public List<(string Code, string State, SsoPendingSignIn Pending)> Completions { get; } = [];

    public AccountMatchField MatchField => matchField;

    public SsoAuthorization Begin(string returnUrl, string? bindingHash)
    {
        _started++;
        var pending = new SsoPendingSignIn(
            State: "state-" + _started,
            CodeVerifier: "verifier-" + _started,
            Nonce: "nonce-" + _started,
            ReturnUrl: returnUrl,
            BindingHash: bindingHash);
        return new SsoAuthorization(new Uri(AuthorizeBase + "?state=" + pending.State), pending);
    }

    public Task<SsoIdentityResult> CompleteAsync(string code, string state, SsoPendingSignIn pending, CancellationToken cancellationToken)
    {
        Completions.Add((code, state, pending));
        return Task.FromResult(Result);
    }

    public Uri EndSessionUrl(string idTokenHint) => new(EndSessionBase + "?id_token_hint=" + Uri.EscapeDataString(idTokenHint));
}

/// <summary>Sealed values in memory: each opens once, for its own kind only; <see cref="Unavailable"/> fails closed.</summary>
internal sealed class InMemorySsoTickets : ISsoTickets
{
    private const string PendingPrefix = "pending:";
    private const string ExchangePrefix = "exchange:";

    private readonly Dictionary<string, object> _live = new(StringComparer.Ordinal);
    private int _next;

    public bool Unavailable { get; set; }

    public string SealPending(SsoPendingSignIn pending) => Add(PendingPrefix, pending);

    public Task<SealedValue<SsoPendingSignIn>> OpenPendingAsync(string sealedValue, CancellationToken cancellationToken) =>
        Task.FromResult(Open<SsoPendingSignIn>(sealedValue));

    public string SealExchange(SsoExchangeGrant grant) => Add(ExchangePrefix, grant);

    public Task<SealedValue<SsoExchangeGrant>> OpenExchangeAsync(string ticket, CancellationToken cancellationToken) =>
        Task.FromResult(Open<SsoExchangeGrant>(ticket));

    private string Add(string prefix, object value)
    {
        var key = prefix + ++_next;
        _live[key] = value;
        return key;
    }

    private SealedValue<T> Open<T>(string key)
        where T : class
    {
        if (Unavailable)
        {
            return SealedValue.Refused<T>(SealedValueStatus.Unavailable);
        }

        return _live.Remove(key, out var value) && value is T typed
            ? new SealedValue<T>(SealedValueStatus.Opened, typed)
            : SealedValue.Refused<T>(SealedValueStatus.Rejected);
    }
}

internal sealed class FakeSpaCallback : ISsoSpaCallback
{
    public const string Url = "https://portal.example.test/auth/sso/callback";

    public Uri CallbackUrl { get; } = new(Url);
}

internal sealed class FakeActiveDirectoryAuthenticator : IActiveDirectoryAuthenticator
{
    public DirectoryAuthResult Result { get; set; } = new(DirectoryAuthStatus.InvalidCredentials, Identity: null);

    public Task<DirectoryAuthResult> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken) =>
        Task.FromResult(Result);
}

internal sealed class FakeOidcIdentityProvider : IOidcIdentityProvider
{
    public OidcSignInResult Result { get; set; } = OidcSignInResult.Rejected(OidcFailureReason.IdTokenInvalid);

    public (OidcCallback Callback, OidcPendingAuthorization Pending)? LastCompletion { get; private set; }

    public (string Hint, Uri? PostLogout)? LastEndSession { get; private set; }

    public OidcAuthorizationRequest BeginAuthorization(string returnUrl) =>
        new(new Uri("https://idp.example.test/authorize?x=1"), State: "s", CodeVerifier: "v", Nonce: "n", ReturnUrl: returnUrl);

    public Task<OidcSignInResult> CompleteAsync(OidcCallback callback, OidcPendingAuthorization pending, CancellationToken cancellationToken)
    {
        LastCompletion = (callback, pending);
        return Task.FromResult(Result);
    }

    public Uri BuildEndSessionUrl(string idTokenHint, Uri? postLogoutRedirectUri)
    {
        LastEndSession = (idTokenHint, postLogoutRedirectUri);
        return new Uri("https://idp.example.test/end-session?id_token_hint=" + idTokenHint);
    }
}

internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "starter-tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
