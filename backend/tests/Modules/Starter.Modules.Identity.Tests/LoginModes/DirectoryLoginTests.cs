using Starter.Application.Results;
using Starter.Domain.Users;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.LoginModes;

public sealed class DirectoryLoginTests
{
    private readonly IdentityHarness _harness = new();
    private readonly FakeDirectory _directory = new();

    [Fact]
    public async Task An_accepted_directory_password_starts_an_active_directory_session_for_the_matched_account()
    {
        var user = _harness.AddAdministered("erin", AccountBinding.ActiveDirectory, loginName: "erin");
        _directory.Verdict = new DirectoryVerdict(DirectoryVerdictStatus.Accepted, MatchValue: "ERIN");

        var outcome = await LoginAsync("CORP\\erin", "directory-secret");

        Assert.True(outcome.IsSuccess);
        Assert.Equal(user.Id, outcome.Value.UserId);
        var session = Assert.Single(_harness.Store.Sessions);
        Assert.Equal(SignInMode.ActiveDirectory, session.SignInMode);
        Assert.Null(session.IdTokenHint);
        Assert.Equal(("CORP\\erin", "directory-secret"), Assert.Single(_directory.Calls));
    }

    [Theory]
    [InlineData(DirectoryVerdictStatus.Refused, AuthFailureReason.WrongPassword)]
    [InlineData(DirectoryVerdictStatus.AccountDisabled, AuthFailureReason.AccountInactive)]
    public async Task A_directory_refusal_is_the_one_invalid_credentials_code(DirectoryVerdictStatus status, AuthFailureReason reason)
    {
        _harness.AddAdministered("erin", AccountBinding.ActiveDirectory, loginName: "erin");
        _directory.Verdict = new DirectoryVerdict(status, MatchValue: null);

        var outcome = await LoginAsync("erin", "wrong");

        AssertRefused(outcome, RefusalCategory.Invalid, IdentityErrorCodes.InvalidCredentials);
        var recorded = Assert.Single(_harness.Audit.Events);
        Assert.Equal((AuthEventType.LoginFailed, SignInMode.ActiveDirectory, reason), (recorded.EventType, recorded.Mode!.Value, recorded.Reason!.Value));
        Assert.Empty(_harness.Store.Sessions);
    }

    [Fact]
    public async Task An_unreachable_directory_is_its_own_code_and_no_credential_verdict()
    {
        _directory.Verdict = new DirectoryVerdict(DirectoryVerdictStatus.Unavailable, MatchValue: null);

        var outcome = await LoginAsync("erin", "secret");

        AssertRefused(outcome, RefusalCategory.Unprocessable, LoginModeErrorCodes.DirectoryUnavailable);
        Assert.Null(Assert.Single(_harness.Audit.Events).Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task An_accepted_identity_without_the_match_value_is_refused(string? matchValue)
    {
        _directory.Verdict = new DirectoryVerdict(DirectoryVerdictStatus.Accepted, matchValue);

        var outcome = await LoginAsync("erin", "secret");

        AssertRefused(outcome, RefusalCategory.Invalid, IdentityErrorCodes.InvalidCredentials);
        Assert.Equal(AuthFailureReason.UnknownAccount, Assert.Single(_harness.Audit.Events).Reason);
    }

    [Theory]
    [InlineData(AccountBinding.Password)]
    [InlineData(AccountBinding.Sso)]
    public async Task A_matched_account_bound_to_another_mode_is_refused(AccountBinding binding)
    {
        _harness.AddAdministered("erin", binding, loginName: "erin");
        _directory.Verdict = new DirectoryVerdict(DirectoryVerdictStatus.Accepted, MatchValue: "erin");

        var outcome = await LoginAsync("erin", "secret");

        AssertRefused(outcome, RefusalCategory.Invalid, IdentityErrorCodes.InvalidCredentials);
        Assert.Equal(AuthFailureReason.BindingMismatch, Assert.Single(_harness.Audit.Events).Reason);
    }

    [Fact]
    public async Task Missing_fields_are_refused_before_the_directory_is_asked()
    {
        var outcome = await LoginAsync(userName: null, password: "");

        AssertRefused(outcome, RefusalCategory.Invalid, PlatformErrorCodes.Validation);
        Assert.Equal(["password", "userName"], outcome.Refusal!.FieldErrors.Keys.Order());
        Assert.Empty(_directory.Calls);
    }

    [Fact]
    public void The_command_never_prints_the_password() =>
        Assert.DoesNotContain("secret", new DirectoryLoginCommand("erin", "secret").ToString(), StringComparison.Ordinal);

    private Task<Outcome<LoginResult>> LoginAsync(string? userName, string? password) =>
        new DirectoryLoginUseCase(new DirectoryLoginCommandValidator(), _directory, _harness.ExternalSignIn(), _harness.Audit)
            .ExecuteAsync(new DirectoryLoginCommand(userName, password), CancellationToken.None);

    private static void AssertRefused<T>(Outcome<T> outcome, RefusalCategory category, string code)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal((category, code), (outcome.Refusal.Category, outcome.Refusal.Code));
    }
}
