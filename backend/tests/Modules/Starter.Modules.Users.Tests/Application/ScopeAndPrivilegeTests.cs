using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Domain.Users;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Application.Import;
using Starter.Modules.Users.Application.Organizations;
using Starter.Modules.Users.Domain;
using Starter.Modules.Users.Tests.Fakes;

namespace Starter.Modules.Users.Tests.Application;

/// <summary>Organization scope (own-organization grants) across the user and organization admin use cases.</summary>
public sealed class OrganizationScopeTests
{
    private readonly UsersHarness _harness = new();
    private readonly Organization _own;
    private readonly Organization _other;
    private readonly AccountSummary _ownMember;
    private readonly AccountSummary _otherMember;

    public OrganizationScopeTests()
    {
        _own = _harness.Organizations.Add("OWN", OrganizationKind.External);
        _other = _harness.Organizations.Add("OTHER", OrganizationKind.External);
        _ownMember = _harness.Accounts.Add("own.member", organizationId: _own.Id);
        _otherMember = _harness.Accounts.Add("other.member", organizationId: _other.Id);
        _harness.Scope.Decision = FakeOrganizationScope.Own(_own.Id);
    }

    [Fact]
    public async Task The_list_narrows_to_the_callers_organization_and_a_foreign_filter_matches_nothing()
    {
        var all = await _harness.ListUsers().ExecuteAsync(Query(organizationId: null), CancellationToken.None);
        var foreign = await _harness.ListUsers().ExecuteAsync(Query(PublicId<OrganizationIds>.From(_other.Id)), CancellationToken.None);

        Assert.Equal([_ownMember.UserId], all.Value.Items.Select(view => view.Account.UserId));
        Assert.Equal(0, foreign.Value.Total);
        Assert.Equal([UsersPermissionKeys.View, UsersPermissionKeys.View], _harness.Scope.Keys);
        Assert.Single(_harness.Accounts.Queries);
    }

    [Fact]
    public async Task An_own_organization_holder_without_an_organization_lists_nothing()
    {
        _harness.Scope.Decision = FakeOrganizationScope.Own(organizationId: null);

        var page = await _harness.ListUsers().ExecuteAsync(Query(organizationId: null), CancellationToken.None);

        Assert.Equal(0, page.Value.Total);
        Assert.Empty(_harness.Accounts.Queries);
    }

    [Fact]
    public async Task Every_single_account_use_case_answers_404_outside_the_scope()
    {
        var id = PublicId<UsersIds>.From(_otherMember.UserId);

        var get = await _harness.GetUser().ExecuteAsync(id, CancellationToken.None);
        var update = await _harness.UpdateUser().ExecuteAsync(id, Update(_other), CancellationToken.None);
        var deactivate = await _harness.SetUserActive().ExecuteAsync(id, isActive: false, CancellationToken.None);
        var unlock = await _harness.UnlockUser().ExecuteAsync(id, CancellationToken.None);
        var activation = await _harness.IssueUserActivation().ExecuteAsync(id, CancellationToken.None);

        Assert.All([get.Refusal, update.Refusal, deactivate.Refusal, unlock.Refusal, activation.Refusal], refusal =>
            Assert.Equal((RefusalCategory.NotFound, UsersErrorCodes.UserNotFound), (refusal!.Category, refusal.Code)));
        Assert.Empty(_harness.Accounts.Calls);
    }

    [Fact]
    public async Task Accounts_of_the_callers_organization_are_reachable()
    {
        var id = PublicId<UsersIds>.From(_ownMember.UserId);

        Assert.True((await _harness.GetUser().ExecuteAsync(id, CancellationToken.None)).IsSuccess);
        Assert.True((await _harness.UpdateUser().ExecuteAsync(id, Update(_own), CancellationToken.None)).IsSuccess);
        Assert.True((await _harness.IssueUserActivation().ExecuteAsync(id, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Creating_or_moving_an_account_outside_the_scope_is_forbidden()
    {
        var create = await _harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(organizationId: PublicId<OrganizationIds>.From(_other.Id)), CancellationToken.None);
        var withoutOrganization = await _harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(kind: UserKind.@Internal), CancellationToken.None);
        var move = await _harness.UpdateUser().ExecuteAsync(PublicId<UsersIds>.From(_ownMember.UserId), Update(_other), CancellationToken.None);

        Assert.All([create.Refusal, withoutOrganization.Refusal, move.Refusal], refusal =>
            Assert.Equal((RefusalCategory.Forbidden, UsersErrorCodes.OrganizationOutOfScope), (refusal!.Category, refusal.Code)));
        Assert.Empty(_harness.Accounts.Calls);
    }

    [Fact]
    public async Task Creating_an_account_in_the_callers_organization_is_allowed()
    {
        var outcome = await _harness.CreateUser().ExecuteAsync(UserValidatorTests.Body(organizationId: PublicId<OrganizationIds>.From(_own.Id)), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public async Task Import_rows_outside_the_scope_are_rejected()
    {
        var rows = await _harness.RowValidator.ValidateAsync(
            [
                ImportRowValidatorTests.Row(2, "in.scope", kind: "external", organizationCode: "OWN"),
                ImportRowValidatorTests.Row(3, "foreign", kind: "external", organizationCode: "OTHER"),
                ImportRowValidatorTests.Row(4, "no.organization", kind: "internal"),
            ],
            CancellationToken.None);

        Assert.True(rows[0].IsValid);
        Assert.Equal([UsersImportRowErrorCodes.OrganizationOutOfScope], rows[1].Errors);
        Assert.Equal([UsersImportRowErrorCodes.OrganizationOutOfScope], rows[2].Errors);
        Assert.All(_harness.Scope.Keys, key => Assert.Equal(UsersPermissionKeys.Import, key));
    }

    [Fact]
    public async Task Organizations_narrow_to_the_callers_own_and_creating_one_needs_a_global_grant()
    {
        var list = await _harness.ListOrganizations().ExecuteAsync(new OrganizationListQuery(Search: null, Kind: null, IsActive: null, Page: 1, PageSize: 20), CancellationToken.None);
        var foreign = await _harness.GetOrganization().ExecuteAsync(PublicId<OrganizationIds>.From(_other.Id), CancellationToken.None);
        var deactivateForeign = await _harness.SetOrganizationActive().ExecuteAsync(PublicId<OrganizationIds>.From(_other.Id), isActive: false, CancellationToken.None);
        var updateForeign = await _harness.UpdateOrganization().ExecuteAsync(
            PublicId<OrganizationIds>.From(_other.Id), new UpdateOrganizationCommand("OTHER", "Other", "Other ar", OrganizationKind.External), CancellationToken.None);
        var create = await _harness.CreateOrganization().ExecuteAsync(new CreateOrganizationCommand("NEW", "New", "New ar", OrganizationKind.External), CancellationToken.None);

        Assert.Equal([_own.Id], list.Value.Items.Select(organization => organization.Id));
        Assert.Equal(UsersErrorCodes.OrganizationNotFound, foreign.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.OrganizationNotFound, deactivateForeign.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.OrganizationNotFound, updateForeign.Refusal!.Code);
        Assert.Equal((RefusalCategory.Forbidden, UsersErrorCodes.OrganizationOutOfScope), (create.Refusal!.Category, create.Refusal.Code));
        Assert.True(_other.IsActive);
        Assert.Equal(0, _harness.UnitOfWork.Saves);
    }

    private static UserListQuery Query(PublicId<OrganizationIds>? organizationId) =>
        new(Search: null, OrganizationId: organizationId, Kind: null, IsActive: null, Page: 1, PageSize: 20);

    private static UpdateUserCommand Update(Organization organization) =>
        new("Name", null, null, null, UserKind.External, AccountBinding.Password, PublicId<OrganizationIds>.From(organization.Id));
}

/// <summary>The privilege rule (no change to an account that outranks the caller) and the self and last-manager rules.</summary>
public sealed class AccountPrivilegeTests
{
    private readonly UsersHarness _harness = new();
    private readonly Organization _organization;
    private readonly AccountSummary _admin;

    public AccountPrivilegeTests()
    {
        _organization = _harness.Organizations.Add("FIN", OrganizationKind.External);
        _admin = _harness.Accounts.Add("grants.admin", organizationId: _organization.Id);
        _harness.Guard.Outranking.Add(_admin.SubjectId);
    }

    [Fact]
    public async Task An_account_that_outranks_the_caller_cannot_be_reset_edited_or_switched_off()
    {
        var id = PublicId<UsersIds>.From(_admin.UserId);

        var activation = await _harness.IssueUserActivation().ExecuteAsync(id, CancellationToken.None);
        var update = await _harness.UpdateUser().ExecuteAsync(id, Rebind(AccountBinding.Sso), CancellationToken.None);
        var deactivate = await _harness.SetUserActive().ExecuteAsync(id, isActive: false, CancellationToken.None);
        var reactivate = await _harness.SetUserActive().ExecuteAsync(id, isActive: true, CancellationToken.None);

        Assert.All([activation.Refusal, update.Refusal, deactivate.Refusal, reactivate.Refusal], refusal =>
            Assert.Equal((RefusalCategory.Forbidden, UsersErrorCodes.TargetOutranksCaller), (refusal!.Category, refusal.Code)));
        Assert.Empty(_harness.Accounts.Calls);
        Assert.Empty(_harness.Outbox.Messages);
    }

    [Fact]
    public async Task An_outranking_account_can_still_be_read_and_unlocked()
    {
        var id = PublicId<UsersIds>.From(_admin.UserId);

        Assert.True((await _harness.GetUser().ExecuteAsync(id, CancellationToken.None)).IsSuccess);
        Assert.True((await _harness.UnlockUser().ExecuteAsync(id, CancellationToken.None)).IsSuccess);
    }

    [Theory]
    [InlineData(AccountBinding.Sso, false, false)]
    [InlineData(AccountBinding.Password, true, false)]
    [InlineData(AccountBinding.Password, false, true)]
    public async Task An_administrator_cannot_change_their_own_binding_kind_or_organization(AccountBinding binding, bool changeKind, bool moveOrganization)
    {
        var me = _harness.Accounts.Add("me", organizationId: _organization.Id);
        var elsewhere = _harness.Organizations.Add("ELSEWHERE", OrganizationKind.External);
        _harness.CurrentUser.SubjectId = me.SubjectId;
        var kind = changeKind ? UserKind.@Internal : UserKind.External;
        var organizationId = PublicId<OrganizationIds>.From(moveOrganization ? elsewhere.Id : _organization.Id);

        var outcome = await _harness.UpdateUser().ExecuteAsync(
            PublicId<UsersIds>.From(me.UserId), new UpdateUserCommand("Me", null, null, null, kind, binding, organizationId), CancellationToken.None);

        Assert.Equal((RefusalCategory.Conflict, UsersErrorCodes.CannotChangeOwnAccess), (outcome.Refusal!.Category, outcome.Refusal.Code));
        Assert.Empty(_harness.Accounts.Calls);
    }

    [Fact]
    public async Task An_administrator_can_still_change_their_own_display_name_and_e_mail()
    {
        var me = _harness.Accounts.Add("me", organizationId: _organization.Id);
        _harness.CurrentUser.SubjectId = me.SubjectId;

        var outcome = await _harness.UpdateUser().ExecuteAsync(
            PublicId<UsersIds>.From(me.UserId),
            new UpdateUserCommand("Me Too", "me@example.test", null, null, UserKind.External, AccountBinding.Password, PublicId<OrganizationIds>.From(_organization.Id)),
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public async Task The_last_access_manager_cannot_be_deactivated_but_can_be_reactivated()
    {
        var manager = _harness.Accounts.Add("manager", organizationId: _organization.Id);
        _harness.Guard.LastManagers.Add(manager.UserId);
        var id = PublicId<UsersIds>.From(manager.UserId);

        var off = await _harness.SetUserActive().ExecuteAsync(id, isActive: false, CancellationToken.None);
        var on = await _harness.SetUserActive().ExecuteAsync(id, isActive: true, CancellationToken.None);

        Assert.Equal((RefusalCategory.Conflict, UsersErrorCodes.LastAccessManager), (off.Refusal!.Category, off.Refusal.Code));
        Assert.True(on.IsSuccess);
        Assert.Equal([$"active:{manager.UserId}:True"], _harness.Accounts.Calls);
    }

    private UpdateUserCommand Rebind(AccountBinding binding) =>
        new("Admin", "attacker@example.test", null, null, UserKind.External, binding, PublicId<OrganizationIds>.From(_organization.Id));
}
