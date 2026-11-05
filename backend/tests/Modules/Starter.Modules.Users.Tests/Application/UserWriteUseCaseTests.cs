using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Domain.Users;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Domain;
using Starter.Modules.Users.Tests.Fakes;

namespace Starter.Modules.Users.Tests.Application;

public sealed class CreateUserTests
{
    [Fact]
    public async Task A_password_bound_user_is_created_without_a_password_and_gets_a_link_and_an_outbox_row()
    {
        var harness = new UsersHarness();
        var organization = harness.Organizations.Add("FIN", OrganizationKind.External);

        var outcome = await harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(organizationId: Id(organization)), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.Value.User.Account.HasPassword);
        Assert.Equal("FIN en", outcome.Value.User.OrganizationName);
        Assert.StartsWith(UsersHarness.LinkBase + "/activate#ticket=", outcome.Value.Activation!.ActivationLink, StringComparison.Ordinal);
        Assert.Single(harness.Outbox.Messages);
        Assert.Equal(["create:amal", $"activation:{outcome.Value.User.Account.UserId}"], harness.Accounts.Calls);
    }

    [Theory]
    [InlineData(AccountBinding.ActiveDirectory)]
    [InlineData(AccountBinding.Sso)]
    public async Task A_directory_or_sso_user_gets_no_activation(AccountBinding binding)
    {
        var harness = new UsersHarness();

        var outcome = await harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(kind: UserKind.@Internal, binding: binding), CancellationToken.None);

        Assert.Null(outcome.Value.Activation);
        Assert.Empty(harness.Outbox.Messages);
    }

    [Fact]
    public async Task An_invalid_body_creates_nothing()
    {
        var harness = new UsersHarness();

        var outcome = await harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(userName: " "), CancellationToken.None);

        Assert.Equal(PlatformErrorCodes.Validation, outcome.Refusal!.Code);
        Assert.Empty(harness.Accounts.Calls);
    }

    [Fact]
    public async Task The_organization_must_exist_be_active_and_match_the_kind()
    {
        var harness = new UsersHarness();
        var inactive = harness.Organizations.Add("OLD", OrganizationKind.External, isActive: false);
        var internalOrganization = harness.Organizations.Add("HQ", OrganizationKind.@Internal);

        var missing = await harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(organizationId: PublicId<OrganizationIds>.From(404)), CancellationToken.None);
        var deactivated = await harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(organizationId: Id(inactive)), CancellationToken.None);
        var mismatch = await harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(organizationId: Id(internalOrganization)), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.OrganizationNotFound, missing.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.OrganizationInactive, deactivated.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.OrganizationKindMismatch, mismatch.Refusal!.Code);
        Assert.Empty(harness.Accounts.Calls);
    }

    [Fact]
    public async Task Without_a_link_base_a_password_bound_user_is_refused_before_anything_is_created()
    {
        var harness = new UsersHarness { Links = new ActivationLinkSettings(LinkBase: null) };

        var outcome = await harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(kind: UserKind.@Internal), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ActivationLinkUnconfigured, outcome.Refusal!.Code);
        Assert.Empty(harness.Accounts.Calls);
    }

    [Fact]
    public async Task Identitys_duplicate_refusal_passes_through()
    {
        var harness = new UsersHarness();
        harness.Accounts.Add("amal");

        var outcome = await harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(kind: UserKind.@Internal), CancellationToken.None);

        Assert.Equal(FakeAccounts.DuplicateCode, outcome.Refusal!.Code);
        Assert.Empty(harness.Outbox.Messages);
    }

    [Fact]
    public async Task A_refused_activation_after_the_create_is_returned_as_the_refusal()
    {
        var harness = new UsersHarness();
        harness.Accounts.IssueRefusal = Refusal.Conflict("IDENTITY_SOMETHING");

        var outcome = await harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(kind: UserKind.@Internal), CancellationToken.None);

        Assert.Equal("IDENTITY_SOMETHING", outcome.Refusal!.Code);
        Assert.Single(harness.Accounts.Accounts);
        Assert.Empty(harness.Outbox.Messages);
    }

    private static PublicId<OrganizationIds> Id(Organization organization) => PublicId<OrganizationIds>.From(organization.Id);
}

public sealed class UpdateUserTests
{
    [Fact]
    public async Task Changes_the_fields()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.Add("amal", kind: UserKind.@Internal);

        var outcome = await harness.UpdateUser().ExecuteAsync(
            PublicId<UsersIds>.From(account.UserId),
            new UpdateUserCommand(" Amal B ", "amal.b@example.test", null, null, UserKind.@Internal, AccountBinding.ActiveDirectory, null),
            CancellationToken.None);

        Assert.Equal("Amal B", outcome.Value.Account.DisplayName);
        Assert.Equal(AccountBinding.ActiveDirectory, outcome.Value.Account.Binding);
    }

    [Fact]
    public async Task A_member_may_keep_a_deactivated_organization_but_not_move_into_one()
    {
        var harness = new UsersHarness();
        var current = harness.Organizations.Add("OLD", OrganizationKind.External, isActive: false);
        var other = harness.Organizations.Add("OTHER", OrganizationKind.External, isActive: false);
        var account = harness.Accounts.Add("amal", organizationId: current.Id);

        var keep = await harness.UpdateUser().ExecuteAsync(PublicId<UsersIds>.From(account.UserId), Command(current), CancellationToken.None);
        var move = await harness.UpdateUser().ExecuteAsync(PublicId<UsersIds>.From(account.UserId), Command(other), CancellationToken.None);

        Assert.True(keep.IsSuccess);
        Assert.Equal(UsersErrorCodes.OrganizationInactive, move.Refusal!.Code);
    }

    [Fact]
    public async Task The_break_glass_account_is_not_edited_here()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.Add("admin", kind: UserKind.@Internal, isBreakGlass: true);

        var outcome = await harness.UpdateUser().ExecuteAsync(
            PublicId<UsersIds>.From(account.UserId), new UpdateUserCommand("x", null, null, null, UserKind.@Internal, AccountBinding.Password, null), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.BreakGlassProtected, outcome.Refusal!.Code);
        Assert.DoesNotContain(harness.Accounts.Calls, call => call.StartsWith("update", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_and_invalid()
    {
        var harness = new UsersHarness();

        var missing = await harness.UpdateUser().ExecuteAsync(
            PublicId<UsersIds>.From(1), new UpdateUserCommand("x", null, null, null, UserKind.@Internal, AccountBinding.Sso, null), CancellationToken.None);
        var invalid = await harness.UpdateUser().ExecuteAsync(
            PublicId<UsersIds>.From(1), new UpdateUserCommand(null, null, null, null, null, null, null), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.UserNotFound, missing.Refusal!.Code);
        Assert.Equal(PlatformErrorCodes.Validation, invalid.Refusal!.Code);
    }

    private static UpdateUserCommand Command(Organization organization) =>
        new("Amal", null, null, null, UserKind.External, AccountBinding.Password, PublicId<OrganizationIds>.From(organization.Id));
}

public sealed class SetUserActiveTests
{
    [Fact]
    public async Task Deactivates_and_reactivates_through_identity()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.Add("amal");
        var useCase = harness.SetUserActive();

        var off = await useCase.ExecuteAsync(PublicId<UsersIds>.From(account.UserId), isActive: false, CancellationToken.None);
        var on = await useCase.ExecuteAsync(PublicId<UsersIds>.From(account.UserId), isActive: true, CancellationToken.None);

        Assert.True(off.IsSuccess && on.IsSuccess);
        Assert.Equal([$"active:{account.UserId}:False", $"active:{account.UserId}:True"], harness.Accounts.Calls);
    }

    [Fact]
    public async Task An_administrator_cannot_deactivate_themselves()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.Add("me");
        harness.CurrentUser.SubjectId = account.SubjectId;

        var outcome = await harness.SetUserActive().ExecuteAsync(PublicId<UsersIds>.From(account.UserId), isActive: false, CancellationToken.None);

        Assert.Equal(UsersErrorCodes.CannotDeactivateSelf, outcome.Refusal!.Code);
        Assert.Empty(harness.Accounts.Calls);
    }

    [Fact]
    public async Task Break_glass_and_unknown_accounts_are_refused()
    {
        var harness = new UsersHarness();
        var breakGlass = harness.Accounts.Add("admin", isBreakGlass: true);
        var useCase = harness.SetUserActive();

        var protectedAccount = await useCase.ExecuteAsync(PublicId<UsersIds>.From(breakGlass.UserId), isActive: false, CancellationToken.None);
        var missing = await useCase.ExecuteAsync(PublicId<UsersIds>.From(12345), isActive: false, CancellationToken.None);

        Assert.Equal(UsersErrorCodes.BreakGlassProtected, protectedAccount.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.UserNotFound, missing.Refusal!.Code);
    }
}

public sealed class UnlockUserTests
{
    [Fact]
    public async Task Unlocks_a_known_account_and_refuses_an_unknown_one()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.Add("amal");
        var useCase = harness.UnlockUser();

        var unlocked = await useCase.ExecuteAsync(PublicId<UsersIds>.From(account.UserId), CancellationToken.None);
        var missing = await useCase.ExecuteAsync(PublicId<UsersIds>.From(1), CancellationToken.None);

        Assert.True(unlocked.IsSuccess);
        Assert.Equal([$"unlock:{account.UserId}"], harness.Accounts.Calls);
        Assert.Equal(UsersErrorCodes.UserNotFound, missing.Refusal!.Code);
    }
}

public sealed class IssueUserActivationTests
{
    [Fact]
    public async Task Reissues_for_a_password_bound_account()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.Add("amal");

        var outcome = await harness.IssueUserActivation().ExecuteAsync(PublicId<UsersIds>.From(account.UserId), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Single(harness.Outbox.Messages);
    }

    [Fact]
    public async Task Directory_sso_break_glass_and_unknown_accounts_are_refused()
    {
        var harness = new UsersHarness();
        var sso = harness.Accounts.Add("sso", binding: AccountBinding.Sso);
        var breakGlass = harness.Accounts.Add("admin", isBreakGlass: true);
        var useCase = harness.IssueUserActivation();

        var notApplicable = await useCase.ExecuteAsync(PublicId<UsersIds>.From(sso.UserId), CancellationToken.None);
        var protectedAccount = await useCase.ExecuteAsync(PublicId<UsersIds>.From(breakGlass.UserId), CancellationToken.None);
        var missing = await useCase.ExecuteAsync(PublicId<UsersIds>.From(1), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ActivationNotApplicable, notApplicable.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.BreakGlassProtected, protectedAccount.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.UserNotFound, missing.Refusal!.Code);
        Assert.Empty(harness.Outbox.Messages);
    }
}
