using Starter.Domain.Users;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Application;

public sealed class ExternalSignInTests
{
    private const string IdToken = "header.payload.signature";

    [Fact]
    public async Task An_sso_identity_matched_by_email_starts_an_sso_session_with_the_logout_hint()
    {
        var harness = new IdentityHarness();
        var user = harness.AddAdministered("carol", AccountBinding.Sso, email: "carol@example.test");

        var outcome = await harness.ExternalSignIn().ExecuteAsync(
            AccountMatchField.Email, " CAROL@Example.Test ", SignInMode.Sso, IdToken, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(user.Id, outcome.Value.UserId);
        var session = Assert.Single(harness.Store.Sessions);
        Assert.Equal(SignInMode.Sso, session.SignInMode);
        Assert.Equal(IdToken, session.IdTokenHint);
        var recorded = Assert.Single(harness.Audit.Events);
        Assert.Equal((AuthEventType.LoginSucceeded, SignInMode.Sso), (recorded.EventType, recorded.Mode!.Value));
    }

    [Theory]
    [InlineData(AccountMatchField.LoginName, "DAVE")]
    [InlineData(AccountMatchField.Upn, "dave@example.test")]
    public async Task A_directory_identity_matches_on_the_configured_field(AccountMatchField field, string value)
    {
        var harness = new IdentityHarness();
        harness.AddAdministered("dave", AccountBinding.ActiveDirectory, loginName: "dave", upn: "Dave@Example.Test");

        var outcome = await harness.ExternalSignIn().ExecuteAsync(field, value, SignInMode.ActiveDirectory, idTokenHint: null, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(SignInMode.ActiveDirectory, Assert.Single(harness.Store.Sessions).SignInMode);
    }

    [Fact]
    public async Task Unknown_inactive_and_wrongly_bound_accounts_get_the_same_refusal()
    {
        var harness = new IdentityHarness();
        harness.AddAdministered("inactive", AccountBinding.Sso, email: "inactive@example.test").Deactivate();
        harness.AddAdministered("password", AccountBinding.Password, email: "password@example.test");

        var unknown = await SsoAsync(harness, "nobody@example.test");
        var inactive = await SsoAsync(harness, "inactive@example.test");
        var mismatch = await SsoAsync(harness, "password@example.test");
        var blank = await SsoAsync(harness, "  ");

        Assert.All([unknown, inactive, mismatch, blank], code => Assert.Equal(IdentityErrorCodes.InvalidCredentials, code));
        Assert.Equal(
            [AuthFailureReason.UnknownAccount, AuthFailureReason.AccountInactive, AuthFailureReason.BindingMismatch, AuthFailureReason.UnknownAccount],
            harness.Audit.Events.Select(e => e.Reason!.Value));
        Assert.Empty(harness.Store.Sessions);
    }

    [Fact]
    public async Task External_sign_in_is_never_basic()
    {
        var harness = new IdentityHarness();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            harness.ExternalSignIn().ExecuteAsync(AccountMatchField.Email, "a@example.test", SignInMode.Basic, idTokenHint: null, CancellationToken.None));
    }

    [Fact]
    public async Task The_current_session_query_returns_the_mode_and_hint_of_the_callers_sid()
    {
        var harness = new IdentityHarness();
        harness.AddAdministered("carol", AccountBinding.Sso, email: "carol@example.test");
        await harness.ExternalSignIn().ExecuteAsync(AccountMatchField.Email, "carol@example.test", SignInMode.Sso, IdToken, CancellationToken.None);
        var sid = Assert.Single(harness.Store.Sessions).SessionId;

        var current = await harness.CurrentSession(sid).FindAsync(CancellationToken.None);
        await harness.Logout(sid).ExecuteAsync(CancellationToken.None);
        var ended = await harness.CurrentSession(sid).FindAsync(CancellationToken.None);

        Assert.Equal((SignInMode.Sso, IdToken, false), (current!.Mode, current.IdTokenHint, current.IsEnded));
        Assert.True(ended!.IsEnded);
        Assert.Null(await harness.CurrentSession(sessionId: null).FindAsync(CancellationToken.None));
        Assert.Null(await harness.CurrentSession(new string('0', IdentityLimits.SessionIdLength)).FindAsync(CancellationToken.None));
        Assert.DoesNotContain(IdToken, current.ToString(), StringComparison.Ordinal);
    }

    private static async Task<string> SsoAsync(IdentityHarness harness, string email)
    {
        var outcome = await harness.ExternalSignIn().ExecuteAsync(AccountMatchField.Email, email, SignInMode.Sso, IdToken, CancellationToken.None);
        return outcome.Refusal!.Code;
    }
}
