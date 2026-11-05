using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Domain.Users;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Application.Accounts;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Accounts;

public sealed class AccountAdministrationTests
{
    [Fact]
    public async Task Create_registers_an_active_account_without_a_password()
    {
        var harness = new IdentityHarness();

        var outcome = await harness.Administration().CreateAsync(NewAccount("erin", email: "erin@example.test"), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var summary = outcome.Value;
        Assert.Equal(("erin", "erin@example.test", AccountBinding.Password, 12L), (summary.UserName, summary.Email, summary.Binding, summary.OrganizationId!.Value));
        Assert.False(summary.HasPassword);
        Assert.True(summary.IsActive);
        Assert.False(summary.IsLockedOut);
        Assert.False(summary.IsBreakGlass);
        Assert.Null(Assert.Single(harness.Store.Accounts).PasswordHash);
        Assert.Empty(harness.Versions.Versions);
    }

    [Theory]
    [InlineData(" ERIN ", null, null, null, IdentityErrorCodes.UserNameTaken)]
    [InlineData("other", "ERIN@example.test", null, null, IdentityErrorCodes.EmailTaken)]
    [InlineData("other", null, "Erin", null, IdentityErrorCodes.LoginNameTaken)]
    [InlineData("other", null, null, "erin@EXAMPLE.test", IdentityErrorCodes.UpnTaken)]
    public async Task Create_refuses_an_identifier_another_account_holds(string userName, string? email, string? loginName, string? upn, string code)
    {
        var harness = new IdentityHarness();
        await harness.Administration().CreateAsync(
            NewAccount("erin", email: "erin@example.test", loginName: "erin", upn: "erin@example.test"), CancellationToken.None);

        var outcome = await harness.Administration().CreateAsync(NewAccount(userName, email, loginName, upn), CancellationToken.None);

        Assert.Equal((RefusalCategory.Conflict, code), (outcome.Refusal!.Category, outcome.Refusal.Code));
        Assert.Single(harness.Store.Accounts);
    }

    [Fact]
    public async Task Create_returns_every_field_error_at_once()
    {
        var harness = new IdentityHarness();
        var input = new NewAccount(
            UserName: " ",
            DisplayName: new string('d', IdentityLimits.DisplayNameMaxLength + 1),
            Email: "not-an-address",
            LoginName: "aliçe",
            Upn: "a@b",
            Kind: (UserKind)99,
            Binding: (AccountBinding)99,
            OrganizationId: 0);

        var outcome = await harness.Administration().CreateAsync(input, CancellationToken.None);

        Assert.Equal(PlatformErrorCodes.Validation, outcome.Refusal!.Code);
        var errors = outcome.Refusal.FieldErrors;
        Assert.Equal([ValidationMessages.Required], errors["userName"]);
        Assert.Equal([ValidationMessages.TooLong], errors["displayName"]);
        Assert.Equal([ValidationMessages.InvalidFormat], errors["email"]);
        Assert.Equal([ValidationMessages.InvalidFormat], errors["loginName"]);
        Assert.Equal([ValidationMessages.InvalidFormat], errors["upn"]);
        Assert.Equal([IdentityValidationMessages.UnknownValue], errors["kind"]);
        Assert.Equal([IdentityValidationMessages.UnknownValue], errors["binding"]);
        Assert.Equal([ValidationMessages.InvalidFormat], errors["organizationId"]);
        Assert.Empty(harness.Store.Accounts);
    }

    [Fact]
    public async Task Update_changes_the_profile_and_ends_sessions_when_the_binding_changes()
    {
        var harness = new IdentityHarness();
        var created = (await harness.Administration().CreateAsync(NewAccount("erin", email: "erin@example.test"), CancellationToken.None)).Value;

        var renamed = await harness.Administration().UpdateAsync(created.UserId, Changes("Erin Renamed", AccountBinding.Password, email: "erin@example.test"), CancellationToken.None);
        Assert.Equal("Erin Renamed", renamed.Value.DisplayName);
        Assert.Empty(harness.Versions.Versions);

        var rebound = await harness.Administration().UpdateAsync(created.UserId, Changes("Erin", AccountBinding.Sso), CancellationToken.None);
        Assert.Equal(AccountBinding.Sso, rebound.Value.Binding);
        Assert.Null(rebound.Value.Email);
        Assert.Equal(1, harness.Versions.Versions[created.SubjectId]);
    }

    [Fact]
    public async Task Update_ends_sessions_when_a_sign_in_identifier_changes()
    {
        var harness = new IdentityHarness();
        var created = (await harness.Administration().CreateAsync(NewAccount("erin", email: "erin@example.test"), CancellationToken.None)).Value;

        var sameAddress = await harness.Administration().UpdateAsync(created.UserId, Changes("Erin", AccountBinding.Password, email: "ERIN@example.test"), CancellationToken.None);
        Assert.True(sameAddress.IsSuccess);
        Assert.Empty(harness.Versions.Versions);

        var rekeyed = await harness.Administration().UpdateAsync(created.UserId, Changes("Erin", AccountBinding.Password, email: "attacker@example.test"), CancellationToken.None);
        Assert.Equal("attacker@example.test", rekeyed.Value.Email);
        Assert.Equal(1, harness.Versions.Versions[created.SubjectId]);
    }

    [Fact]
    public async Task Update_refuses_an_unknown_account_the_break_glass_and_another_accounts_email()
    {
        var harness = new IdentityHarness();
        var admin = harness.Store.AddAccount("admin", "pw", isBreakGlass: true);
        await harness.Administration().CreateAsync(NewAccount("erin", email: "erin@example.test"), CancellationToken.None);
        var frank = (await harness.Administration().CreateAsync(NewAccount("frank"), CancellationToken.None)).Value;

        var unknown = await harness.Administration().UpdateAsync(999, Changes("x", AccountBinding.Password), CancellationToken.None);
        var protectedAccount = await harness.Administration().UpdateAsync(admin.Id, Changes("x", AccountBinding.Password), CancellationToken.None);
        var taken = await harness.Administration().UpdateAsync(frank.UserId, Changes("x", AccountBinding.Password, email: "Erin@Example.Test"), CancellationToken.None);

        Assert.Equal((RefusalCategory.NotFound, IdentityErrorCodes.AccountNotFound), (unknown.Refusal!.Category, unknown.Refusal.Code));
        Assert.Equal((RefusalCategory.Conflict, IdentityErrorCodes.BreakGlassProtected), (protectedAccount.Refusal!.Category, protectedAccount.Refusal.Code));
        Assert.Equal(IdentityErrorCodes.EmailTaken, taken.Refusal!.Code);
    }

    [Fact]
    public async Task Deactivation_bumps_the_session_version_and_records_it_reactivation_does_not_bump()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", "pw");

        var deactivated = await harness.Administration().SetActiveAsync(user.Id, isActive: false, CancellationToken.None);
        var again = await harness.Administration().SetActiveAsync(user.Id, isActive: false, CancellationToken.None);

        Assert.True(deactivated.IsSuccess && again.IsSuccess);
        Assert.False(user.IsActive);
        Assert.Equal(1, harness.Versions.Versions[user.SubjectId]);

        await harness.Administration().SetActiveAsync(user.Id, isActive: true, CancellationToken.None);
        Assert.True(user.IsActive);
        Assert.Equal(1, harness.Versions.Versions[user.SubjectId]);
        Assert.Equal([AuthEventType.AccountDeactivated, AuthEventType.AccountReactivated], harness.Audit.Events.Select(e => e.EventType));
    }

    [Fact]
    public async Task The_break_glass_cannot_be_deactivated_and_an_unknown_account_is_not_found()
    {
        var harness = new IdentityHarness();
        var admin = harness.Store.AddAccount("admin", "pw", isBreakGlass: true);

        var refused = await harness.Administration().SetActiveAsync(admin.Id, isActive: false, CancellationToken.None);
        var unknown = await harness.Administration().SetActiveAsync(404, isActive: false, CancellationToken.None);

        Assert.Equal(IdentityErrorCodes.BreakGlassProtected, refused.Refusal!.Code);
        Assert.Equal(IdentityErrorCodes.AccountNotFound, unknown.Refusal!.Code);
        Assert.True(admin.IsActive);
        Assert.Empty(harness.Versions.Versions);
    }

    [Fact]
    public async Task Unlock_ends_a_lockout_also_for_the_break_glass()
    {
        var harness = new IdentityHarness();
        var admin = harness.Store.AddAccount("admin", "pw", isBreakGlass: true);
        for (var attempt = 0; attempt < IdentityHarness.Lockout.MaxFailedAttempts; attempt++)
        {
            admin.RecordFailedSignIn(harness.Clock.GetUtcNow(), IdentityHarness.Lockout);
        }

        Assert.True((await harness.Administration().FindAsync(admin.Id, CancellationToken.None))!.IsLockedOut);
        var unlocked = await harness.Administration().UnlockAsync(admin.Id, CancellationToken.None);

        Assert.True(unlocked.IsSuccess);
        Assert.False((await harness.Administration().FindAsync(admin.Id, CancellationToken.None))!.IsLockedOut);
        Assert.Equal(AuthEventType.AccountUnlocked, Assert.Single(harness.Audit.Events).EventType);
        Assert.Equal(IdentityErrorCodes.AccountNotFound, (await harness.Administration().UnlockAsync(404, CancellationToken.None)).Refusal!.Code);
    }

    [Fact]
    public async Task List_caps_the_page_and_find_answers_null_for_an_unknown_id()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("bravo", "pw");
        harness.Store.AddAccount("alpha", "pw");

        var page = await harness.Administration().ListAsync(new AccountQuery(Search: "  a ", OrganizationId: null, Kind: null, IsActive: true, Page: 0, PageSize: 1000), CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.Equal(["alpha", "bravo"], page.Items.Select(item => item.UserName));
        var request = harness.Store.LastListRequest!;
        Assert.Equal((0, AccountAdministration.MaxPageSize, "a"), (request.Skip, request.Take, request.Search));
        var second = await harness.Administration().ListAsync(new AccountQuery(null, null, null, null, Page: 2, PageSize: 1), CancellationToken.None);
        Assert.Equal(["bravo"], second.Items.Select(item => item.UserName));
        Assert.Null(await harness.Administration().FindAsync(404, CancellationToken.None));
    }

    internal static NewAccount NewAccount(string userName, string? email = null, string? loginName = null, string? upn = null) => new(
        UserName: userName,
        DisplayName: userName + " display",
        Email: email,
        LoginName: loginName,
        Upn: upn,
        Kind: UserKind.External,
        Binding: AccountBinding.Password,
        OrganizationId: 12);

    private static AccountChanges Changes(string displayName, AccountBinding binding, string? email = null) => new(
        DisplayName: displayName,
        Email: email,
        LoginName: null,
        Upn: null,
        Kind: UserKind.External,
        Binding: binding,
        OrganizationId: 12);
}
