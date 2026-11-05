using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Users;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Application.Activation;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Accounts;

public sealed class ActivationIssueTests
{
    [Fact]
    public async Task Issuing_clears_the_password_ends_sessions_and_records_it()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", "old password");

        var outcome = await harness.Administration().IssueActivationAsync(user.Id, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var issued = Assert.Single(harness.Tickets.Issued);
        Assert.Equal((issued.Ticket, issued.TicketId, issued.ExpiresAt), (outcome.Value.Ticket, outcome.Value.TicketId, outcome.Value.ExpiresAt));
        Assert.False(user.HasPassword);
        Assert.Equal(issued.TicketId, user.ActivationTicketId);
        Assert.Equal(1, harness.Versions.Versions[user.SubjectId]);
        Assert.Equal(AuthEventType.ActivationIssued, Assert.Single(harness.Audit.Events).EventType);
    }

    [Fact]
    public async Task Issuing_is_refused_for_directory_bound_break_glass_inactive_and_unknown_accounts()
    {
        var harness = new IdentityHarness();
        var directory = harness.AddAdministered("dir", AccountBinding.ActiveDirectory);
        var admin = harness.Store.AddAccount("admin", "pw", isBreakGlass: true);
        var inactive = harness.AddAdministered("gone", AccountBinding.Password);
        inactive.Deactivate();

        Assert.Equal((RefusalCategory.Unprocessable, IdentityErrorCodes.ActivationNotApplicable), await RefusalAsync(harness, directory.Id));
        Assert.Equal((RefusalCategory.Conflict, IdentityErrorCodes.BreakGlassProtected), await RefusalAsync(harness, admin.Id));
        Assert.Equal((RefusalCategory.Unprocessable, IdentityErrorCodes.AccountInactive), await RefusalAsync(harness, inactive.Id));
        Assert.Equal((RefusalCategory.NotFound, IdentityErrorCodes.AccountNotFound), await RefusalAsync(harness, 404));
        Assert.Empty(harness.Tickets.Issued);
        Assert.Empty(harness.Versions.Versions);
    }

    [Fact]
    public async Task Issuing_is_refused_while_sealed_tickets_are_off()
    {
        var harness = new IdentityHarness();
        var user = harness.AddAdministered("new", AccountBinding.Password);
        harness.Tickets.IsAvailable = false;

        Assert.Equal((RefusalCategory.Unprocessable, IdentityErrorCodes.ActivationUnavailable), await RefusalAsync(harness, user.Id));
    }

    private static async Task<(RefusalCategory, string)> RefusalAsync(IdentityHarness harness, long userId)
    {
        var outcome = await harness.Administration().IssueActivationAsync(userId, CancellationToken.None);
        return (outcome.Refusal!.Category, outcome.Refusal.Code);
    }
}

public sealed class ActivationRedemptionTests
{
    private static readonly string ChosenPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)) + "-7";

    [Fact]
    public async Task Redeeming_sets_the_password_bumps_the_version_and_the_user_can_sign_in()
    {
        var harness = new IdentityHarness();
        var user = harness.AddAdministered("new", AccountBinding.Password);
        var ticket = await IssueAsync(harness, user);

        var outcome = await RedeemAsync(harness, ticket);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(PlainTextHasher.HashOf(ChosenPassword), user.PasswordHash);
        Assert.Equal(harness.Clock.GetUtcNow(), user.ActivatedAt);
        Assert.Equal(2, harness.Versions.Versions[user.SubjectId]);
        Assert.Equal(AuthEventType.AccountActivated, harness.Audit.Events[^1].EventType);
        Assert.True((await harness.Login().ExecuteAsync(new LoginCommand("new", ChosenPassword), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task A_link_opens_once()
    {
        var harness = new IdentityHarness();
        var user = harness.AddAdministered("new", AccountBinding.Password);
        var ticket = await IssueAsync(harness, user);
        await RedeemAsync(harness, ticket);

        var replay = await RedeemAsync(harness, ticket);

        AssertRejected(replay);
        Assert.Equal(AuthFailureReason.ActivationTicketRejected, harness.Audit.Events[^1].Reason);
    }

    [Fact]
    public async Task A_newer_link_supersedes_the_older_one()
    {
        var harness = new IdentityHarness();
        var user = harness.AddAdministered("new", AccountBinding.Password);
        var first = await IssueAsync(harness, user);
        var second = await IssueAsync(harness, user);

        AssertRejected(await RedeemAsync(harness, first));
        Assert.True((await RedeemAsync(harness, second)).IsSuccess);
    }

    [Fact]
    public async Task An_expired_link_or_a_deactivated_account_is_rejected()
    {
        var harness = new IdentityHarness();
        var expiring = harness.AddAdministered("late", AccountBinding.Password);
        var deactivated = harness.AddAdministered("gone", AccountBinding.Password);
        var expiringTicket = await IssueAsync(harness, expiring);
        var deactivatedTicket = await IssueAsync(harness, deactivated);
        deactivated.Deactivate();

        AssertRejected(await RedeemAsync(harness, deactivatedTicket));
        harness.Clock.Advance(FakeActivationTickets.Lifetime);
        AssertRejected(await RedeemAsync(harness, expiringTicket));
        Assert.False(expiring.HasPassword);
        Assert.False(deactivated.HasPassword);
    }

    [Fact]
    public async Task A_weak_password_is_refused_before_the_link_is_spent()
    {
        var harness = new IdentityHarness();
        var user = harness.AddAdministered("new", AccountBinding.Password);
        var ticket = await IssueAsync(harness, user);

        var weak = await harness.Activate().ExecuteAsync(new ActivateAccountCommand(ticket, "aaaaaaaaaaaaaaa"), CancellationToken.None);
        var strong = await RedeemAsync(harness, ticket);

        Assert.Equal(PlatformErrorCodes.Validation, weak.Refusal!.Code);
        Assert.True(strong.IsSuccess);
    }

    private static async Task<string> IssueAsync(IdentityHarness harness, UserAccount user) =>
        (await harness.Administration().IssueActivationAsync(user.Id, CancellationToken.None)).Value.Ticket;

    private static Task<Outcome<Done>> RedeemAsync(IdentityHarness harness, string ticket) =>
        harness.Activate().ExecuteAsync(new ActivateAccountCommand(ticket, ChosenPassword), CancellationToken.None);

    private static void AssertRejected(Outcome<Done> outcome)
    {
        Assert.Equal((RefusalCategory.Invalid, IdentityErrorCodes.ActivationRejected), (outcome.Refusal!.Category, outcome.Refusal.Code));
        Assert.Empty(outcome.Refusal.FieldErrors);
    }
}

public sealed class ActivationValidatorTests
{
    private readonly ActivateAccountCommandValidator _validator = new(IdentityHarness.Passwords);

    [Fact]
    public void Missing_fields_are_required()
    {
        var errors = _validator.Validate(new ActivateAccountCommand(Ticket: null, Password: " ")).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["ticket"]);
        Assert.Equal([ValidationMessages.Required], errors["password"]);
    }

    [Fact]
    public void A_short_and_simple_password_gets_both_codes()
    {
        var errors = _validator.Validate(new ActivateAccountCommand("t", "abab")).ToDictionary();

        Assert.Equal([IdentityValidationMessages.PasswordTooShort, IdentityValidationMessages.PasswordTooSimple], errors["password"]);
    }

    [Fact]
    public void Over_long_values_are_too_long_and_a_good_password_passes()
    {
        var tooLong = _validator.Validate(new ActivateAccountCommand(
            new string('t', IdentityLimits.ActivationTicketMaxLength + 1),
            new string('p', IdentityLimits.PasswordMaxLength + 1))).ToDictionary();

        Assert.Equal([ValidationMessages.TooLong], tooLong["ticket"]);
        Assert.Equal([ValidationMessages.TooLong], tooLong["password"]);
        Assert.True(_validator.Validate(new ActivateAccountCommand("t", "a-long passphrase 7")).IsValid);
    }

    [Fact]
    public void The_command_never_prints_its_secrets()
    {
        var text = new ActivateAccountCommand("sealed-ticket", "pass-phrase").ToString();

        Assert.DoesNotContain("sealed-ticket", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pass-phrase", text, StringComparison.Ordinal);
    }
}
