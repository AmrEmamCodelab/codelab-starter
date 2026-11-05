using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.LoginModes;

/// <summary>The values one SSO sign-in keeps between the redirect to the provider and its callback. All secret.</summary>
/// <param name="State">The state sent to the provider (also names the browser's sign-in cookie).</param>
/// <param name="CodeVerifier">The PKCE verifier.</param>
/// <param name="Nonce">The id_token nonce.</param>
/// <param name="ReturnUrl">The root-relative path the SPA opens after the sign-in.</param>
/// <param name="BindingHash">
/// Optional: the base64url SHA-256 of a secret the SPA keeps in this browser tab; the exchange then requires the
/// secret, so a ticket link is useless in any other browser (login CSRF). Null when the SPA sent none.
/// </param>
public sealed record SsoPendingSignIn(string State, string CodeVerifier, string Nonce, string ReturnUrl, string? BindingHash)
{
    /// <summary>A log-safe description: no value of the sign-in is printed.</summary>
    public override string ToString() => nameof(SsoPendingSignIn);
}

/// <summary>A started SSO sign-in: where to send the browser, and what to keep sealed until the callback.</summary>
/// <param name="AuthorizationUrl">The provider's authorization URL (code, PKCE S256, state, nonce).</param>
/// <param name="Pending">The values the callback needs.</param>
public sealed record SsoAuthorization(Uri AuthorizationUrl, SsoPendingSignIn Pending)
{
    /// <summary>A log-safe description: the sign-in's values are not printed.</summary>
    public override string ToString() => nameof(SsoAuthorization) + "(" + AuthorizationUrl.GetLeftPart(UriPartial.Path) + ")";
}

/// <summary>What a completed callback proved. Logged as the integer value.</summary>
public enum SsoIdentityStatus
{
    /// <summary>The code was redeemed and the id_token is valid.</summary>
    Succeeded = 1,

    /// <summary>Something presented was wrong (state; code; token response or id_token).</summary>
    Rejected = 2,

    /// <summary>The provider or its signing keys could not be reached.</summary>
    Unavailable = 3,
}

/// <summary>The provider's verdict on a callback and, on success, the matched value and the id_token.</summary>
/// <param name="Status">The verdict.</param>
/// <param name="MatchValue">The identity's value of <see cref="ISsoProvider.MatchField"/>, or null.</param>
/// <param name="IdToken">The raw id_token (kept server-side as the end-session hint), or null.</param>
public sealed record SsoIdentityResult(SsoIdentityStatus Status, string? MatchValue, string? IdToken)
{
    /// <summary>A log-safe description: neither the match value nor the token is printed.</summary>
    public override string ToString() => nameof(SsoIdentityResult) + "(" + Status + ")";
}

/// <summary>The organization's OpenID Connect provider, as the SSO use cases need it (implemented over Codelab.Identity).</summary>
public interface ISsoProvider
{
    /// <summary>The local account field an SSO identity is matched on (<c>Codelab:Identity:Sso:MatchBy</c>).</summary>
    AccountMatchField MatchField { get; }

    /// <summary>A fresh authorization request for <paramref name="returnUrl"/> (already a safe root-relative path).</summary>
    SsoAuthorization Begin(string returnUrl, string? bindingHash);

    /// <summary>Redeems <paramref name="code"/> with <paramref name="pending"/>'s verifier and validates the id_token.</summary>
    Task<SsoIdentityResult> CompleteAsync(string code, string state, SsoPendingSignIn pending, CancellationToken cancellationToken);

    /// <summary>The provider's end_session URL for <paramref name="idTokenHint"/> (opened by a top-level browser GET).</summary>
    Uri EndSessionUrl(string idTokenHint);
}

/// <summary>Why a sealed value did or did not open. Logged as the integer value.</summary>
public enum SealedValueStatus
{
    /// <summary>Opened and consumed: this was its one use.</summary>
    Opened = 1,

    /// <summary>Malformed; forged; expired or already used.</summary>
    Rejected = 2,

    /// <summary>The one-time store could not answer (fail closed; try again).</summary>
    Unavailable = 3,
}

/// <summary>An opened sealed value; <see cref="Value"/> is set only when <see cref="Status"/> is <see cref="SealedValueStatus.Opened"/>.</summary>
/// <typeparam name="T">The sealed payload.</typeparam>
/// <param name="Status">The outcome.</param>
/// <param name="Value">The payload, or default.</param>
public sealed record SealedValue<T>(SealedValueStatus Status, T? Value)
    where T : class
{
    /// <summary>A log-safe description: the payload is never printed.</summary>
    public override string ToString() => "SealedValue(" + Status + ")";
}

/// <summary>Creates <see cref="SealedValue{T}"/> values.</summary>
public static class SealedValue
{
    /// <summary>A refusal with no payload.</summary>
    public static SealedValue<T> Refused<T>(SealedValueStatus status)
        where T : class => new(status, Value: null);
}

/// <summary>What the one-time exchange ticket carries: the new session's refresh token and the optional browser binding.</summary>
/// <param name="RefreshToken">The refresh token of the session the callback started (spent by the exchange).</param>
/// <param name="BindingHash">The binding hash of the sign-in, or null.</param>
public sealed record SsoExchangeGrant(string RefreshToken, string? BindingHash)
{
    /// <summary>A log-safe description: nothing is printed.</summary>
    public override string ToString() => nameof(SsoExchangeGrant);
}

/// <summary>
/// The sealed, one-time values of the SSO flow (implemented over Codelab.Security sealed tickets): the pending
/// sign-in kept in the browser's HttpOnly cookie, and the exchange ticket handed to the SPA in the URL fragment. Each
/// opens once, only for its own purpose, and only before it expires.
/// </summary>
public interface ISsoTickets
{
    /// <summary>Seals <paramref name="pending"/> for the sign-in cookie (lifetime <see cref="SsoLimits.PendingLifetime"/>).</summary>
    string SealPending(SsoPendingSignIn pending);

    /// <summary>Opens and consumes a sign-in cookie value.</summary>
    Task<SealedValue<SsoPendingSignIn>> OpenPendingAsync(string sealedValue, CancellationToken cancellationToken);

    /// <summary>Seals <paramref name="grant"/> as the exchange ticket (lifetime <see cref="SsoLimits.ExchangeLifetime"/>).</summary>
    string SealExchange(SsoExchangeGrant grant);

    /// <summary>Opens and consumes an exchange ticket.</summary>
    Task<SealedValue<SsoExchangeGrant>> OpenExchangeAsync(string ticket, CancellationToken cancellationToken);
}

/// <summary>The SSO flow's limits.</summary>
public static class SsoLimits
{
    /// <summary>How long a started sign-in may take at the provider before its cookie no longer opens.</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(PendingLifetimeMinutes);

    /// <summary>How long the SPA has to exchange the fragment ticket.</summary>
    public static readonly TimeSpan ExchangeLifetime = TimeSpan.FromMinutes(ExchangeLifetimeMinutes);

    /// <summary>The longest return path kept (a longer one becomes <c>/</c>).</summary>
    public const int ReturnUrlMaxLength = 2048;

    /// <summary>The longest exchange ticket or sign-in cookie value accepted.</summary>
    public const int SealedValueMaxLength = 4096;

    /// <summary>The longest authorization code or state accepted from the provider's callback.</summary>
    public const int CallbackValueMaxLength = 2048;

    /// <summary>The length of a binding hash: base64url of a 32-byte SHA-256, unpadded.</summary>
    public const int BindingHashLength = 43;

    /// <summary>The shortest binding secret accepted at the exchange.</summary>
    public const int BindingProofMinLength = 32;

    /// <summary>The longest binding secret accepted at the exchange.</summary>
    public const int BindingProofMaxLength = 256;

    private const int PendingLifetimeMinutes = 10;
    private const int ExchangeLifetimeMinutes = 2;
}
