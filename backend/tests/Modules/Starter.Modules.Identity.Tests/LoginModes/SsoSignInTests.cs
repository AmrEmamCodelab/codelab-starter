using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Users;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.LoginModes;

public sealed class SsoSignInTests
{
    private const string IdToken = "header.payload.signature";
    private const string Proof = "a-binding-secret-of-at-least-32-characters";
    private static readonly string Hash = SsoBinding.HashOf(Proof);

    private readonly IdentityHarness _harness = new();
    private readonly FakeSsoProvider _provider = new();
    private readonly InMemorySsoTickets _tickets = new();

    [Fact]
    public void Begin_keeps_a_safe_return_path_and_seals_the_pending_values()
    {
        var started = Begin().Execute(returnUrl: "/admin/users?page=2", bindingHash: Hash);

        Assert.True(started.IsSuccess);
        Assert.StartsWith(FakeSsoProvider.AuthorizeBase, started.Value.AuthorizationUrl.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("state-1", started.Value.State);
        Assert.Equal("/admin/users?page=2", Pending(started.Value).ReturnUrl);
    }

    [Theory]
    [InlineData("https://evil.example.test/")]
    [InlineData("//evil.example.test")]
    [InlineData(null)]
    public void Begin_turns_an_unsafe_return_url_into_the_root(string? returnUrl)
    {
        var started = Begin().Execute(returnUrl, bindingHash: Hash);

        Assert.Equal(ReturnUrlPolicy.Root, Pending(started.Value).ReturnUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Begin_refuses_a_sign_in_without_a_browser_binding(string? bindingHash)
    {
        var started = Begin().Execute(returnUrl: "/", bindingHash);

        Assert.Equal(PlatformErrorCodes.Validation, started.Refusal!.Code);
        Assert.Equal([ValidationMessages.Required], started.Refusal.FieldErrors[BeginSsoSignIn.BindingField]);
        Assert.Equal(0, _provider.Begins);
    }

    [Fact]
    public async Task A_forwarded_ticket_is_rejected_in_a_browser_without_the_secret()
    {
        var ticket = await SignedInTicketAsync(Hash);

        var withoutProof = await Exchange().ExecuteAsync(new SsoExchangeCommand(ticket, BindingProof: null), CancellationToken.None);

        Assert.Equal((RefusalCategory.Invalid, LoginModeErrorCodes.SsoTicketRejected), (withoutProof.Refusal!.Category, withoutProof.Refusal.Code));
    }

    [Fact]
    public void Begin_refuses_a_binding_that_is_not_a_sha256_hash()
    {
        var started = Begin().Execute(returnUrl: "/", bindingHash: "not-a-hash");

        Assert.False(started.IsSuccess);
        Assert.Equal(PlatformErrorCodes.Validation, started.Refusal.Code);
        Assert.Contains(BeginSsoSignIn.BindingField, started.Refusal.FieldErrors.Keys);
    }

    [Fact]
    public async Task A_valid_callback_starts_an_sso_session_with_the_id_token_and_redirects_with_a_ticket()
    {
        var user = _harness.AddAdministered("carol", AccountBinding.Sso, email: "carol@example.test");
        var started = Begin().Execute(returnUrl: "/admin", bindingHash: Hash).Value;
        _provider.Result = new SsoIdentityResult(SsoIdentityStatus.Succeeded, MatchValue: "carol@example.test", IdToken: IdToken);

        var redirect = await CompleteAsync(code: "the-code", state: started.State, started.SealedPending);

        Assert.NotNull(redirect.Result.Ticket);
        Assert.Null(redirect.Result.ErrorCode);
        Assert.Equal(FakeSpaCallback.Url + "#ticket=" + Uri.EscapeDataString(redirect.Result.Ticket!) + "&returnUrl=%2Fadmin", redirect.Location.AbsoluteUri);
        var session = Assert.Single(_harness.Store.Sessions);
        Assert.Equal((user.Id, SignInMode.Sso, IdToken), (session.UserId, session.SignInMode, session.IdTokenHint));
        Assert.Equal(("the-code", started.State), (_provider.Completions[0].Code, _provider.Completions[0].State));
    }

    [Fact]
    public async Task The_sign_in_cookie_opens_once()
    {
        _harness.AddAdministered("carol", AccountBinding.Sso, email: "carol@example.test");
        var started = Begin().Execute(returnUrl: "/", bindingHash: Hash).Value;
        _provider.Result = new SsoIdentityResult(SsoIdentityStatus.Succeeded, MatchValue: "carol@example.test", IdToken: IdToken);
        await CompleteAsync(code: "c", state: started.State, started.SealedPending);

        var replay = await CompleteAsync(code: "c", state: started.State, started.SealedPending);

        Assert.Equal(LoginModeErrorCodes.SsoRejected, replay.Result.ErrorCode);
        Assert.Single(_provider.Completions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pending:999")]
    public async Task A_callback_without_a_usable_sign_in_cookie_is_rejected_without_a_trail_row(string? cookie)
    {
        var redirect = await CompleteAsync(code: "c", state: "state-1", cookie);

        Assert.Equal(LoginModeErrorCodes.SsoRejected, redirect.Result.ErrorCode);
        Assert.Equal(FakeSpaCallback.Url + "#error=" + LoginModeErrorCodes.SsoRejected, redirect.Location.AbsoluteUri);
        Assert.Empty(_harness.Audit.Events);
        Assert.Empty(_provider.Completions);
    }

    [Fact]
    public async Task An_unavailable_ticket_store_answers_sso_unavailable()
    {
        var started = Begin().Execute(returnUrl: "/", bindingHash: Hash).Value;
        _tickets.Unavailable = true;

        var redirect = await CompleteAsync(code: "c", state: started.State, started.SealedPending);

        Assert.Equal(LoginModeErrorCodes.SsoUnavailable, redirect.Result.ErrorCode);
    }

    [Theory]
    [InlineData("access_denied", "c", null)]
    [InlineData(null, null, null)]
    [InlineData(null, "c", "state-other")]
    public async Task A_provider_error_a_missing_code_or_another_state_is_rejected_and_recorded(string? error, string? code, string? state)
    {
        var started = Begin().Execute(returnUrl: "/admin", bindingHash: Hash).Value;

        var redirect = await new CompleteSsoSignIn(_provider, _tickets, _harness.ExternalSignIn(), _harness.Audit, new FakeSpaCallback())
            .ExecuteAsync(new SsoCallback(Code: code, State: state ?? started.State, ProviderError: error, SealedPending: started.SealedPending), CancellationToken.None);

        Assert.Equal(LoginModeErrorCodes.SsoRejected, redirect.Result.ErrorCode);
        Assert.Equal("/admin", redirect.Result.ReturnUrl);
        Assert.Empty(_provider.Completions);
        var recorded = Assert.Single(_harness.Audit.Events);
        Assert.Equal((AuthEventType.LoginFailed, SignInMode.Sso), (recorded.EventType, recorded.Mode!.Value));
    }

    [Theory]
    [InlineData(SsoIdentityStatus.Rejected, LoginModeErrorCodes.SsoRejected)]
    [InlineData(SsoIdentityStatus.Unavailable, LoginModeErrorCodes.SsoUnavailable)]
    public async Task A_provider_verdict_other_than_success_maps_to_its_code(SsoIdentityStatus status, string expected)
    {
        var started = Begin().Execute(returnUrl: "/", bindingHash: Hash).Value;
        _provider.Result = new SsoIdentityResult(status, MatchValue: null, IdToken: null);

        var redirect = await CompleteAsync(code: "c", state: started.State, started.SealedPending);

        Assert.Equal(expected, redirect.Result.ErrorCode);
        Assert.Empty(_harness.Store.Sessions);
    }

    [Theory]
    [InlineData("nobody@example.test")]
    [InlineData(null)]
    public async Task An_identity_with_no_matching_sso_account_gets_invalid_credentials(string? matchValue)
    {
        _harness.AddAdministered("pat", AccountBinding.Password, email: "pat@example.test");
        var started = Begin().Execute(returnUrl: "/", bindingHash: Hash).Value;
        _provider.Result = new SsoIdentityResult(SsoIdentityStatus.Succeeded, matchValue, IdToken: IdToken);

        var redirect = await CompleteAsync(code: "c", state: started.State, started.SealedPending);

        Assert.Equal(IdentityErrorCodes.InvalidCredentials, redirect.Result.ErrorCode);
        Assert.Empty(_harness.Store.Sessions);
    }

    [Fact]
    public async Task The_exchange_rotates_the_sessions_refresh_token_and_opens_once()
    {
        var ticket = await SignedInTicketAsync(Hash);

        var exchanged = await Exchange().ExecuteAsync(new SsoExchangeCommand(ticket, Proof), CancellationToken.None);
        var replayed = await Exchange().ExecuteAsync(new SsoExchangeCommand(ticket, Proof), CancellationToken.None);

        Assert.True(exchanged.IsSuccess);
        Assert.Equal(Assert.Single(_harness.Store.Sessions).UserId, exchanged.Value.UserId);
        Assert.Equal((RefusalCategory.Invalid, LoginModeErrorCodes.SsoTicketRejected), (replayed.Refusal!.Category, replayed.Refusal.Code));
    }

    [Fact]
    public async Task A_bound_ticket_needs_the_binding_secret()
    {
        var ticket = await SignedInTicketAsync(SsoBinding.HashOf(Proof));
        var other = await SignedInTicketAsync(SsoBinding.HashOf(Proof));

        var withoutProof = await Exchange().ExecuteAsync(new SsoExchangeCommand(ticket, BindingProof: null), CancellationToken.None);
        var withProof = await Exchange().ExecuteAsync(new SsoExchangeCommand(other, Proof), CancellationToken.None);

        Assert.Equal(LoginModeErrorCodes.SsoTicketRejected, withoutProof.Refusal!.Code);
        Assert.True(withProof.IsSuccess);
    }

    [Fact]
    public async Task An_exchange_after_the_session_ended_is_rejected()
    {
        var ticket = await SignedInTicketAsync(Hash);
        await _harness.Logout(_harness.Store.Sessions.Single().SessionId).ExecuteAsync(CancellationToken.None);

        var exchanged = await Exchange().ExecuteAsync(new SsoExchangeCommand(ticket, Proof), CancellationToken.None);

        Assert.Equal(LoginModeErrorCodes.SsoTicketRejected, exchanged.Refusal!.Code);
    }

    [Fact]
    public async Task The_exchange_validates_its_body_and_maps_an_unavailable_store()
    {
        var invalid = await Exchange().ExecuteAsync(new SsoExchangeCommand(Ticket: " ", BindingProof: "short"), CancellationToken.None);
        _tickets.Unavailable = true;
        var unavailable = await Exchange().ExecuteAsync(new SsoExchangeCommand(Ticket: "exchange:1", BindingProof: null), CancellationToken.None);

        Assert.Equal(PlatformErrorCodes.Validation, invalid.Refusal!.Code);
        Assert.Equal(["bindingProof", "ticket"], invalid.Refusal.FieldErrors.Keys.Order());
        Assert.Equal((RefusalCategory.Unprocessable, LoginModeErrorCodes.SsoUnavailable), (unavailable.Refusal!.Category, unavailable.Refusal.Code));
    }

    [Fact]
    public async Task Sso_logout_ends_the_session_and_returns_the_end_session_url_with_the_hint()
    {
        await SignedInTicketAsync(Hash);
        var sid = _harness.Store.Sessions.Single().SessionId;

        var outcome = await Logout(sid).ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(FakeSsoProvider.EndSessionBase + "?id_token_hint=" + Uri.EscapeDataString(IdToken), outcome.Value.EndSessionUrl!.AbsoluteUri);
        Assert.NotNull(_harness.Store.Sessions.Single().RevokedAt);
        Assert.Contains(sid, _harness.Revocation.Revoked.Keys);
    }

    [Fact]
    public async Task Logout_of_a_basic_session_returns_no_end_session_url()
    {
        _harness.Store.Add(UserAccount.Register(new AccountRegistration(
            SubjectId: Guid.NewGuid(),
            UserName: "basic",
            Profile: new AccountProfile("basic", Email: null, LoginName: null, Upn: null, UserKind.External, AccountBinding.Password, OrganizationId: null),
            CreatedAt: _harness.Clock.GetUtcNow())));
        await _harness.Store.SaveChangesAsync(CancellationToken.None);
        await _harness.Issuer.StartAsync(_harness.Store.Accounts.Single(), SessionStart.Basic, CancellationToken.None);

        var outcome = await Logout(_harness.Store.Sessions.Single().SessionId).ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Null(outcome.Value.EndSessionUrl);
    }

    [Fact]
    public async Task Logout_without_a_session_id_is_the_logout_refusal()
    {
        var outcome = await Logout(sessionId: null).ExecuteAsync(CancellationToken.None);

        Assert.Equal(IdentityErrorCodes.SessionRequired, outcome.Refusal!.Code);
    }

    [Fact]
    public void Records_print_no_secret()
    {
        var pending = new SsoPendingSignIn("state", "verifier", "nonce", "/", BindingHash: null);

        Assert.DoesNotContain("verifier", pending.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("refresh", new SsoExchangeGrant("refresh", BindingHash: null).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("tok", new SsoIdentityResult(SsoIdentityStatus.Succeeded, "who", "tok").ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ticket-value", new SsoCallbackResult("ticket-value", ErrorCode: null, "/").ToString(), StringComparison.Ordinal);
    }

    private BeginSsoSignIn Begin() => new(_provider, _tickets);

    private ExchangeSsoTicket Exchange() => new(new SsoExchangeCommandValidator(), _tickets, _harness.Refresh());

    private SsoLogoutUseCase Logout(string? sessionId) => new(_harness.CurrentSession(sessionId), _harness.Logout(sessionId), _provider);

    private SsoPendingSignIn Pending(SsoSignInStart started) =>
        _tickets.OpenPendingAsync(started.SealedPending, CancellationToken.None).GetAwaiter().GetResult().Value!;

    private Task<SsoCallbackRedirect> CompleteAsync(string? code, string? state, string? cookie) =>
        new CompleteSsoSignIn(_provider, _tickets, _harness.ExternalSignIn(), _harness.Audit, new FakeSpaCallback())
            .ExecuteAsync(new SsoCallback(code, state, ProviderError: null, SealedPending: cookie), CancellationToken.None);

    private async Task<string> SignedInTicketAsync(string? bindingHash)
    {
        if (_harness.Store.Accounts.Count == 0)
        {
            _harness.AddAdministered("carol", AccountBinding.Sso, email: "carol@example.test");
        }

        var started = Begin().Execute(returnUrl: "/", bindingHash).Value;
        _provider.Result = new SsoIdentityResult(SsoIdentityStatus.Succeeded, MatchValue: "carol@example.test", IdToken: IdToken);
        var redirect = await CompleteAsync(code: "c", state: started.State, started.SealedPending);
        return redirect.Result.Ticket!;
    }
}
