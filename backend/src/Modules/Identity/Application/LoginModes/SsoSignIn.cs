using System.Security.Cryptography;
using System.Text;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.LoginModes;

/// <summary>A started SSO sign-in: the browser goes to <see cref="AuthorizationUrl"/> holding <see cref="SealedPending"/> in a cookie.</summary>
/// <param name="AuthorizationUrl">The provider's authorization URL.</param>
/// <param name="State">The sign-in's state (it names the cookie, so parallel sign-ins in two tabs never collide).</param>
/// <param name="SealedPending">The sealed pending sign-in, the cookie's value.</param>
public sealed record SsoSignInStart(Uri AuthorizationUrl, string State, string SealedPending)
{
    /// <summary>A log-safe description: the state and the sealed value are not printed.</summary>
    public override string ToString() => nameof(SsoSignInStart);
}

/// <summary>
/// Starts an SSO sign-in: a safe return path, a fresh authorization request bound to the browser tab that asked for it,
/// and its pending values sealed. The browser binding is required (login-CSRF defence, see <see cref="SsoBinding"/>).
/// </summary>
public sealed class BeginSsoSignIn(ISsoProvider provider, ISsoTickets tickets)
{
    /// <summary>The wire name of the required binding hash (query <c>binding</c>).</summary>
    public const string BindingField = "binding";

    /// <summary>
    /// The start, or <see cref="PlatformErrorCodes.Validation"/> when the binding hash is missing
    /// (<see cref="ValidationMessages.Required"/>) or not SHA-256 shaped (<see cref="ValidationMessages.InvalidFormat"/>).
    /// </summary>
    /// <param name="returnUrl">Where the SPA goes after the sign-in; anything but a root-relative path becomes <c>/</c>.</param>
    /// <param name="bindingHash">The base64url SHA-256 of the binding secret the SPA keeps in this tab.</param>
    public Outcome<SsoSignInStart> Execute(string? returnUrl, string? bindingHash)
    {
        if (string.IsNullOrEmpty(bindingHash))
        {
            return Refusal.Invalid(new ValidationErrors().Add(BindingField, ValidationMessages.Required));
        }

        if (!SsoBinding.IsHashShaped(bindingHash))
        {
            return Refusal.Invalid(new ValidationErrors().Add(BindingField, ValidationMessages.InvalidFormat));
        }

        var authorization = provider.Begin(returnUrl: ReturnUrlPolicy.Sanitize(returnUrl), bindingHash: bindingHash);
        return new SsoSignInStart(
            AuthorizationUrl: authorization.AuthorizationUrl,
            State: authorization.Pending.State,
            SealedPending: tickets.SealPending(authorization.Pending));
    }
}

/// <summary>What the provider's redirect brought back, plus the browser's sign-in cookie.</summary>
/// <param name="Code">The authorization code, or null.</param>
/// <param name="State">The state, or null.</param>
/// <param name="ProviderError">The provider's <c>error</c> parameter (the user cancelled, the client is refused), or null.</param>
/// <param name="SealedPending">The sign-in cookie's value, or null when the browser sent none.</param>
public sealed record SsoCallback(string? Code, string? State, string? ProviderError, string? SealedPending)
{
    /// <summary>A log-safe description: nothing of the callback is printed.</summary>
    public override string ToString() => nameof(SsoCallback);
}

/// <summary>How the callback ends: a one-time exchange ticket, or an error code, and the SPA path to continue on.</summary>
/// <param name="Ticket">The sealed exchange ticket (for the URL fragment), or null.</param>
/// <param name="ErrorCode">The error code (for the URL fragment), or null.</param>
/// <param name="ReturnUrl">The safe root-relative path the SPA opens next.</param>
public sealed record SsoCallbackResult(string? Ticket, string? ErrorCode, string ReturnUrl)
{
    /// <summary>A refused callback.</summary>
    public static SsoCallbackResult Failed(string errorCode, string returnUrl) => new(Ticket: null, ErrorCode: errorCode, ReturnUrl: returnUrl);

    /// <summary>A log-safe description: the ticket is never printed.</summary>
    public override string ToString() => nameof(SsoCallbackResult) + "(" + (ErrorCode ?? "ticket") + ")";
}

/// <summary>Where the callback sends the browser: the SPA's callback route with the outcome in the URL fragment.</summary>
/// <param name="Location">The absolute redirect target.</param>
/// <param name="Result">The outcome the fragment carries.</param>
public sealed record SsoCallbackRedirect(Uri Location, SsoCallbackResult Result)
{
    /// <summary>A log-safe description: the location carries the ticket and is never printed.</summary>
    public override string ToString() => nameof(SsoCallbackRedirect) + "(" + Result + ")";
}

/// <summary>
/// Completes an SSO callback (a top-level browser navigation, so every outcome is a redirect, never an error page):
/// opens and consumes the browser's sign-in cookie, checks the state against it, lets the provider redeem the code and
/// validate the id_token, matches the identity to an existing SSO-bound account and starts its session with the
/// id_token as the logout hint. The session's refresh token goes sealed into a two-minute, one-time exchange ticket.
/// A callback without a usable sign-in cookie names no sign-in and writes no trail row; every later refusal does.
/// </summary>
public sealed class CompleteSsoSignIn(
    ISsoProvider provider,
    ISsoTickets tickets,
    SignInExternalAccount signIn,
    IAuthEventSink audit,
    ISsoSpaCallback spa)
{
    /// <summary>The redirect to the SPA's callback route, carrying the ticket or the error code in the fragment.</summary>
    public async Task<SsoCallbackRedirect> ExecuteAsync(SsoCallback callback, CancellationToken cancellationToken)
    {
        var result = await CompleteAsync(callback, cancellationToken);
        return new SsoCallbackRedirect(SsoSpaFragment.Location(spa.CallbackUrl, result), result);
    }

    private async Task<SsoCallbackResult> CompleteAsync(SsoCallback callback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!IsBounded(callback.SealedPending, SsoLimits.SealedValueMaxLength))
        {
            return SsoCallbackResult.Failed(errorCode: LoginModeErrorCodes.SsoRejected, returnUrl: ReturnUrlPolicy.Root);
        }

        var opened = await tickets.OpenPendingAsync(callback.SealedPending!, cancellationToken);
        if (opened is not { Status: SealedValueStatus.Opened, Value: { } pending })
        {
            var code = opened.Status == SealedValueStatus.Unavailable ? LoginModeErrorCodes.SsoUnavailable : LoginModeErrorCodes.SsoRejected;
            return SsoCallbackResult.Failed(errorCode: code, returnUrl: ReturnUrlPolicy.Root);
        }

        var returnUrl = ReturnUrlPolicy.Sanitize(pending.ReturnUrl);
        if (!CallbackIsUsable(callback, pending))
        {
            return await FailAsync(code: LoginModeErrorCodes.SsoRejected, returnUrl: returnUrl, cancellationToken);
        }

        var identity = await provider.CompleteAsync(code: callback.Code!, state: callback.State!, pending, cancellationToken);
        if (identity.Status != SsoIdentityStatus.Succeeded)
        {
            var code = identity.Status == SsoIdentityStatus.Unavailable ? LoginModeErrorCodes.SsoUnavailable : LoginModeErrorCodes.SsoRejected;
            return await FailAsync(code: code, returnUrl: returnUrl, cancellationToken);
        }

        var session = await signIn.ExecuteAsync(
            provider.MatchField, value: identity.MatchValue ?? string.Empty, SignInMode.Sso, idTokenHint: identity.IdToken, cancellationToken);
        if (!session.IsSuccess)
        {
            return SsoCallbackResult.Failed(errorCode: session.Refusal.Code, returnUrl: returnUrl);
        }

        var grant = new SsoExchangeGrant(RefreshToken: session.Value.RefreshToken, BindingHash: pending.BindingHash);
        return new SsoCallbackResult(Ticket: tickets.SealExchange(grant), ErrorCode: null, ReturnUrl: returnUrl);
    }

    private static bool CallbackIsUsable(SsoCallback callback, SsoPendingSignIn pending) =>
        string.IsNullOrEmpty(callback.ProviderError)
        && IsBounded(callback.Code, SsoLimits.CallbackValueMaxLength)
        && IsBounded(callback.State, SsoLimits.CallbackValueMaxLength)
        && CryptographicOperations.FixedTimeEquals(left: Encoding.UTF8.GetBytes(callback.State!), right: Encoding.UTF8.GetBytes(pending.State));

    private static bool IsBounded(string? value, int maxLength) => !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;

    private async Task<SsoCallbackResult> FailAsync(string code, string returnUrl, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(new AuthEventRecord(AuthEventType.LoginFailed, Mode: SignInMode.Sso), cancellationToken);
        return SsoCallbackResult.Failed(errorCode: code, returnUrl: returnUrl);
    }
}

/// <summary>The body of <c>POST /api/auth/sso/exchange</c>.</summary>
/// <param name="Ticket">The one-time ticket from the callback's URL fragment.</param>
/// <param name="BindingProof">The binding secret whose hash started the sign-in (required: without it the ticket is rejected).</param>
public sealed record SsoExchangeCommand(string? Ticket, string? BindingProof)
{
    /// <summary>A log-safe description: nothing is printed.</summary>
    public override string ToString() => nameof(SsoExchangeCommand);
}

/// <summary>
/// Validates <see cref="SsoExchangeCommand"/>: the ticket present and bounded, a given proof within its limits. A missing
/// proof is not a validation error: the exchange rejects it as <see cref="LoginModeErrorCodes.SsoTicketRejected"/>, like
/// a wrong one.
/// </summary>
public sealed class SsoExchangeCommandValidator : IValidator<SsoExchangeCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(SsoExchangeCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors().RequireText(FieldNames.Of(nameof(SsoExchangeCommand.Ticket)), input.Ticket, SsoLimits.SealedValueMaxLength);
        if (input.BindingProof is { } proof && (proof.Length < SsoLimits.BindingProofMinLength || proof.Length > SsoLimits.BindingProofMaxLength))
        {
            errors.Add(FieldNames.Of(nameof(SsoExchangeCommand.BindingProof)), ValidationMessages.InvalidFormat);
        }

        return errors;
    }
}

/// <summary>
/// Exchanges the callback's one-time ticket for the session, in the same body as <c>/api/auth/login</c>: opens and
/// consumes the ticket, checks the browser binding (a missing or wrong proof is refused), then rotates the session's refresh token through
/// <see cref="RefreshSessionUseCase"/>, so the tokens the client receives were issued for it alone. Every refusal is
/// <see cref="LoginModeErrorCodes.SsoTicketRejected"/>; an unavailable ticket store is <see cref="LoginModeErrorCodes.SsoUnavailable"/>.
/// </summary>
public sealed class ExchangeSsoTicket(IValidator<SsoExchangeCommand> validator, ISsoTickets tickets, RefreshSessionUseCase refresh)
{
    /// <summary>The session, or a refusal.</summary>
    public async Task<Outcome<LoginResult>> ExecuteAsync(SsoExchangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var opened = await tickets.OpenExchangeAsync(command.Ticket!, cancellationToken);
        if (opened.Status == SealedValueStatus.Unavailable)
        {
            return Refusal.Unprocessable(LoginModeErrorCodes.SsoUnavailable);
        }

        if (opened is not { Status: SealedValueStatus.Opened, Value: { } grant } || !SsoBinding.Accepts(bindingHash: grant.BindingHash, proof: command.BindingProof))
        {
            return Refusal.Invalid(LoginModeErrorCodes.SsoTicketRejected);
        }

        var session = await refresh.ExecuteAsync(new RefreshCommand(grant.RefreshToken), cancellationToken);
        return session.IsSuccess ? session : Refusal.Invalid(LoginModeErrorCodes.SsoTicketRejected);
    }
}

/// <summary>The end of an SSO logout: the provider's end_session URL, or null when the session was not an SSO one.</summary>
/// <param name="EndSessionUrl">The URL the browser opens with a top-level GET, or null.</param>
public sealed record SsoLogoutResult(Uri? EndSessionUrl);

/// <summary>
/// Ends the caller's session exactly as <see cref="LogoutUseCase"/> does and, for an SSO session that kept the
/// provider's id_token, returns the provider's end_session URL with it as <c>id_token_hint</c>. The URL is for a
/// top-level browser GET: providers that redirect back to <c>post_logout_redirect_uri</c> do it only on a GET that
/// carries the hint.
/// </summary>
public sealed class SsoLogoutUseCase(CurrentSessionQuery currentSession, LogoutUseCase logout, ISsoProvider provider)
{
    /// <summary>The end_session URL (null for a non-SSO session), or the logout's refusal.</summary>
    public async Task<Outcome<SsoLogoutResult>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var session = await currentSession.FindAsync(cancellationToken);
        var ended = await logout.ExecuteAsync(cancellationToken);
        if (!ended.IsSuccess)
        {
            return ended.Refusal;
        }

        return session is { Mode: SignInMode.Sso, IdTokenHint: { Length: > 0 } hint }
            ? new SsoLogoutResult(provider.EndSessionUrl(hint))
            : new SsoLogoutResult(EndSessionUrl: null);
    }
}
