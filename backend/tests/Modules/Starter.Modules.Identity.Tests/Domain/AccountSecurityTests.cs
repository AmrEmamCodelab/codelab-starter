using Starter.Domain.Users;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Domain;

public sealed class LockoutRuleTests
{
    private static readonly LockoutPolicy Policy = new(MaxFailedAttempts: 3, Duration: TimeSpan.FromMinutes(15));

    [Fact]
    public void The_attempt_that_reaches_the_threshold_locks_for_the_duration_and_resets_the_counter()
    {
        var account = NewAccount();

        Assert.False(account.RecordFailedSignIn(TestClock.Start, Policy));
        Assert.False(account.RecordFailedSignIn(TestClock.Start, Policy));
        Assert.True(account.RecordFailedSignIn(TestClock.Start, Policy));

        Assert.Equal(TestClock.Start + Policy.Duration, account.LockoutEndsAt);
        Assert.Equal(0, account.FailedAccessCount);
        Assert.True(account.IsLockedOut(TestClock.Start.AddMinutes(14)));
        Assert.False(account.IsLockedOut(TestClock.Start + Policy.Duration));
    }

    [Fact]
    public void An_attempt_during_a_lockout_is_not_counted_and_never_extends_it()
    {
        var account = NewAccount();
        LockOut(account, TestClock.Start);

        Assert.False(account.RecordFailedSignIn(TestClock.Start.AddMinutes(5), Policy));

        Assert.Equal(0, account.FailedAccessCount);
        Assert.Equal(TestClock.Start + Policy.Duration, account.LockoutEndsAt);
    }

    [Fact]
    public void After_the_window_a_new_series_of_failures_is_needed_to_lock_again()
    {
        var account = NewAccount();
        LockOut(account, TestClock.Start);
        var later = TestClock.Start + Policy.Duration;

        Assert.False(account.RecordFailedSignIn(later, Policy));

        Assert.Equal(1, account.FailedAccessCount);
        Assert.False(account.IsLockedOut(later));
    }

    [Fact]
    public void A_success_resets_the_counter_and_unlock_ends_the_lockout()
    {
        var account = NewAccount();
        account.RecordFailedSignIn(TestClock.Start, Policy);
        account.RecordFailedSignIn(TestClock.Start, Policy);

        account.RecordSuccessfulSignIn();
        Assert.Equal(0, account.FailedAccessCount);

        LockOut(account, TestClock.Start);
        account.Unlock();
        Assert.False(account.IsLockedOut(TestClock.Start));
        Assert.Null(account.LockoutEndsAt);
    }

    [Fact]
    public void Restoring_the_break_glass_ends_its_lockout()
    {
        var account = UserAccount.Create(new NewUserAccount(Guid.NewGuid(), "admin", "Admin", "hash", IsBreakGlass: true, TestClock.Start));
        LockOut(account, TestClock.Start);

        account.RestoreBreakGlass();

        Assert.False(account.IsLockedOut(TestClock.Start));
    }

    private static void LockOut(UserAccount account, DateTimeOffset now)
    {
        for (var attempt = 0; attempt < Policy.MaxFailedAttempts; attempt++)
        {
            account.RecordFailedSignIn(now, Policy);
        }
    }

    private static UserAccount NewAccount() =>
        UserAccount.Create(new NewUserAccount(Guid.NewGuid(), "alice", "Alice", "hash", IsBreakGlass: false, TestClock.Start));
}

public sealed class ActivationRuleTests
{
    [Fact]
    public void A_registered_password_account_has_no_password_and_is_eligible()
    {
        var account = Registered(AccountBinding.Password);

        Assert.False(account.HasPassword);
        Assert.Null(account.ActivatedAt);
        Assert.Equal(ActivationEligibility.Eligible, account.CheckActivationEligibility());
    }

    [Fact]
    public void Break_glass_directory_bound_and_inactive_accounts_are_not_eligible()
    {
        var breakGlass = UserAccount.Create(new NewUserAccount(Guid.NewGuid(), "admin", "Admin", "hash", IsBreakGlass: true, TestClock.Start));
        var inactive = Registered(AccountBinding.Password);
        inactive.Deactivate();

        Assert.Equal(ActivationEligibility.BreakGlass, breakGlass.CheckActivationEligibility());
        Assert.Equal(ActivationEligibility.NotPasswordBound, Registered(AccountBinding.ActiveDirectory).CheckActivationEligibility());
        Assert.Equal(ActivationEligibility.NotPasswordBound, Registered(AccountBinding.Sso).CheckActivationEligibility());
        Assert.Equal(ActivationEligibility.Inactive, inactive.CheckActivationEligibility());
        Assert.Throws<InvalidOperationException>(() => breakGlass.BeginActivation("id-1"));
    }

    [Fact]
    public void Beginning_activation_clears_the_password_and_a_newer_link_supersedes_the_older()
    {
        var account = UserAccount.Create(new NewUserAccount(Guid.NewGuid(), "alice", "Alice", "hash", IsBreakGlass: false, TestClock.Start));

        account.BeginActivation("id-1");
        Assert.False(account.HasPassword);
        Assert.True(account.CanRedeemActivation("id-1"));

        account.BeginActivation("id-2");
        Assert.False(account.CanRedeemActivation("id-1"));
        Assert.True(account.CanRedeemActivation("id-2"));
    }

    [Fact]
    public void Completing_activation_sets_the_password_spends_the_link_and_unlocks()
    {
        var account = Registered(AccountBinding.Password);
        account.BeginActivation("id-1");
        var now = TestClock.Start.AddHours(1);

        account.CompleteActivation("hash:new", now);

        Assert.Equal("hash:new", account.PasswordHash);
        Assert.Equal(now, account.ActivatedAt);
        Assert.Null(account.ActivationTicketId);
        Assert.False(account.CanRedeemActivation("id-1"));
        Assert.Throws<InvalidOperationException>(() => account.CompleteActivation("hash:again", now));
    }

    [Fact]
    public void A_deactivated_account_cannot_redeem_its_pending_link()
    {
        var account = Registered(AccountBinding.Password);
        account.BeginActivation("id-1");

        account.Deactivate();

        Assert.False(account.CanRedeemActivation("id-1"));
    }

    [Fact]
    public void Moving_to_another_binding_drops_the_password_and_the_pending_link()
    {
        var account = Registered(AccountBinding.Password);
        account.BeginActivation("id-1");

        var sameBinding = account.ChangeProfile(Profile(AccountBinding.Password, "Alice B"));
        Assert.False(sameBinding);
        Assert.True(account.CanRedeemActivation("id-1"));

        var changed = account.ChangeProfile(Profile(AccountBinding.Sso, "Alice B"));
        Assert.True(changed);
        Assert.False(account.HasPassword);
        Assert.Null(account.ActivationTicketId);
        Assert.Equal("Alice B", account.DisplayName);
    }

    [Fact]
    public void A_new_sign_in_identifier_is_a_credential_change_but_keeps_the_password()
    {
        var account = Registered(AccountBinding.Password);
        account.BeginActivation("id-1");

        var sameIdentifiers = account.ChangeProfile(Profile(AccountBinding.Password) with { Email = "ALICE@example.test", DisplayName = "Alice C" });
        var newEmail = account.ChangeProfile(Profile(AccountBinding.Password) with { Email = "attacker@example.test" });
        var newLoginName = account.ChangeProfile(Profile(AccountBinding.Password) with { Email = "attacker@example.test", LoginName = "other" });
        var newUpn = account.ChangeProfile(Profile(AccountBinding.Password) with { Email = "attacker@example.test", LoginName = "other", Upn = "other@corp.test" });

        Assert.False(sameIdentifiers);
        Assert.True(newEmail);
        Assert.True(newLoginName);
        Assert.True(newUpn);
        Assert.True(account.CanRedeemActivation("id-1"));
    }

    internal static AccountProfile Profile(AccountBinding binding, string displayName = "Alice") => new(
        DisplayName: displayName,
        Email: " Alice@Example.Test ",
        LoginName: "alice",
        Upn: null,
        Kind: UserKind.External,
        Binding: binding,
        OrganizationId: 9);

    private static UserAccount Registered(AccountBinding binding) =>
        UserAccount.Register(new AccountRegistration(Guid.NewGuid(), " alice ", Profile(binding), TestClock.Start));
}

public sealed class AccountIdentifierTests
{
    [Fact]
    public void Registration_keeps_identifiers_as_entered_and_normalized_and_blank_as_null()
    {
        var account = UserAccount.Register(new AccountRegistration(
            Guid.NewGuid(),
            " bob ",
            new AccountProfile(" Bob ", " Bob@Example.Test ", "  ", null, UserKind.External, AccountBinding.Sso, OrganizationId: 4),
            TestClock.Start));

        Assert.Equal("bob", account.UserName);
        Assert.Equal("BOB", account.NormalizedUserName);
        Assert.Equal("Bob", account.DisplayName);
        Assert.Equal("Bob@Example.Test", account.Email);
        Assert.Equal("BOB@EXAMPLE.TEST", account.NormalizedEmail);
        Assert.Null(account.LoginName);
        Assert.Null(account.NormalizedLoginName);
        Assert.Null(account.NormalizedUpn);
        Assert.Equal(UserKind.External, account.Kind);
        Assert.Equal(AccountBinding.Sso, account.Binding);
        Assert.Equal(4, account.OrganizationId);
        Assert.True(account.IsActive);
    }

    [Fact]
    public void Configured_accounts_are_internal_password_bound_and_activated()
    {
        var account = UserAccount.Create(new NewUserAccount(Guid.NewGuid(), "dev", "Dev", "hash", IsBreakGlass: false, TestClock.Start));

        Assert.NotEqual(UserKind.External, account.Kind);
        Assert.Equal(AccountBinding.Password, account.Binding);
        Assert.Equal(TestClock.Start, account.ActivatedAt);
        Assert.True(account.HasPassword);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" Mixed.Case ", "MIXED.CASE")]
    public void Optional_identifiers_normalize_to_upper_case_or_null(string? value, string? expected) =>
        Assert.Equal(expected, UserNames.NormalizeOptional(value));

    [Fact]
    public void Trimming_an_optional_identifier_keeps_its_case()
    {
        Assert.Equal("Mixed", UserNames.TrimOptional("  Mixed "));
        Assert.Null(UserNames.TrimOptional(" "));
    }
}

public sealed class SignInModeRuleTests
{
    [Theory]
    [InlineData(AccountBinding.Password, SignInMode.Basic, true)]
    [InlineData(AccountBinding.Password, SignInMode.ActiveDirectory, false)]
    [InlineData(AccountBinding.Password, SignInMode.Sso, false)]
    [InlineData(AccountBinding.ActiveDirectory, SignInMode.ActiveDirectory, true)]
    [InlineData(AccountBinding.ActiveDirectory, SignInMode.Basic, false)]
    [InlineData(AccountBinding.Sso, SignInMode.Sso, true)]
    [InlineData(AccountBinding.Sso, SignInMode.ActiveDirectory, false)]
    public void Each_binding_accepts_exactly_its_own_mode(AccountBinding binding, SignInMode mode, bool accepted) =>
        Assert.Equal(accepted, AccountBindings.Accepts(binding, mode));

    [Fact]
    public void A_session_keeps_its_mode_and_hint_and_drops_an_over_long_hint()
    {
        var withHint = Session(SignInMode.Sso, "header.payload.signature");
        var overLong = Session(SignInMode.Sso, new string('a', IdentityLimits.IdTokenHintMaxLength + 1));

        Assert.Equal(SignInMode.Sso, withHint.SignInMode);
        Assert.Equal("header.payload.signature", withHint.IdTokenHint);
        Assert.Null(overLong.IdTokenHint);
    }

    [Fact]
    public void Enum_values_are_stable()
    {
        Assert.Equal(1, (int)SignInMode.Basic);
        Assert.Equal(2, (int)SignInMode.ActiveDirectory);
        Assert.Equal(3, (int)SignInMode.Sso);
        Assert.Equal(1, (int)AccountMatchField.Email);
        Assert.Equal(2, (int)AccountMatchField.LoginName);
        Assert.Equal(3, (int)AccountMatchField.Upn);
        Assert.Equal(11, (int)AuthEventType.AccountUnlocked);
        Assert.Equal(13, (int)AuthFailureReason.ActivationTicketRejected);
    }

    private static UserSession Session(SignInMode mode, string hint) => UserSession.Start(new NewUserSession(
        SessionId: "5f0e2c9a7b3d4e1f8a6c0b2d9e7f1a3c",
        UserId: 7,
        RefreshSecret: "first",
        SessionVersion: 0,
        StartedAt: TestClock.Start,
        Lifetime: TimeSpan.FromDays(7),
        Mode: mode,
        IdTokenHint: hint));
}

public sealed class AuthEventTests
{
    [Fact]
    public void A_row_cuts_over_long_text_instead_of_refusing_it()
    {
        var row = AuthEvent.Record(new NewAuthEvent(
            EventType: AuthEventType.LoginFailed,
            FailureReason: AuthFailureReason.UnknownAccount,
            UserId: null,
            UserNameHash: new string('h', IdentityLimits.UserNameHashMaxLength + 5),
            SignInMode: SignInMode.Basic,
            OccurredAt: TestClock.Start,
            CorrelationId: new string('c', IdentityLimits.CorrelationIdMaxLength + 5)));

        Assert.Equal(IdentityLimits.UserNameHashMaxLength, row.UserNameHash!.Length);
        Assert.Equal(IdentityLimits.CorrelationIdMaxLength, row.CorrelationId!.Length);
        Assert.Equal(AuthEventType.LoginFailed, row.EventType);
        Assert.Equal(AuthFailureReason.UnknownAccount, row.FailureReason);
        Assert.Equal(TestClock.Start, row.OccurredAt);
    }
}
