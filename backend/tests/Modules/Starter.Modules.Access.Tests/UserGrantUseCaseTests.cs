using Codelab.Security.PublicIds;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Authorization;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class UserGrantUseCaseTests
{
    private const long UserId = 7;
    private const long Manager = 1;
    private const long OtherOrganizationUser = 40;
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] ManagerKeys = [AccessPermissionKeys.RolesManage, AccessPermissionKeys.GrantsManage];

    private readonly AccessStore _store = new AccessStore().WithPermissions(TestCatalogs.FullKeys);
    private readonly FakeAccounts _accounts = new();
    private readonly PermissionCatalog _catalog = TestCatalogs.Full();
    private readonly FakeOrganizationScope _scope = new();
    private static readonly Guid CallerSubject = Guid.Parse("5c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e09");
    private IUserPermissionResolver _callerAccess = new FakeResolver(TestCatalogs.FullKeys);

    public UserGrantUseCaseTests()
    {
        _accounts.Add(UserId, kind: FakeAccounts.SecondKind);
        _accounts.Add(99, isBreakGlass: true);
    }

    [Fact]
    public async Task GetUserGrants_shows_rows_matching_virtual_roles_and_the_effective_set()
    {
        var reviewers = _store.AddRole("reviewers", AccessPermissionKeys.RolesView);
        _store.Assign(UserId, reviewers, PermissionScope.OwnOrganization);
        _store.GrantDirect(UserId, AccessPermissionKeys.DashboardView);
        var external = _store.AddVirtualRole(VirtualRoleKind.AllExternalUsers, TestCatalogs.ReportsView);
        _store.AddVirtualRole(VirtualRoleKind.AllInternalUsers, AccessPermissionKeys.GrantsView);

        var view = (await Get().ExecuteAsync(UserId, CancellationToken.None)).Value;

        Assert.Equal(UserId, view.Account.UserId);
        Assert.Equal((reviewers.Id, PermissionScope.OwnOrganization), (Assert.Single(view.Roles).Role.RoleId, view.Roles[0].Scope));
        Assert.Equal(AccessPermissionKeys.DashboardView, Assert.Single(view.Permissions).Key);
        Assert.Equal(external.Id, Assert.Single(view.VirtualRoles).RoleId);
        Assert.Equal([AccessPermissionKeys.RolesView, AccessPermissionKeys.DashboardView, TestCatalogs.ReportsView], view.Effective.Select(grant => grant.Key));
        Assert.Empty(view.Warnings);
    }

    [Fact]
    public async Task GetUserGrants_refuses_an_unknown_user()
    {
        var outcome = await Get().ExecuteAsync(UserId + 1000, CancellationToken.None);

        Assert.Equal((RefusalCategory.NotFound, AccessErrorCodes.UserNotFound), (outcome.Refusal!.Category, outcome.Refusal.Code));
    }

    [Fact]
    public async Task An_own_organization_holder_cannot_read_or_edit_a_user_of_another_organization()
    {
        _accounts.Add(OtherOrganizationUser, organizationId: 6);
        _scope.Decision = FakeOrganizationScope.Own(5);

        var read = await Get().ExecuteAsync(OtherOrganizationUser, CancellationToken.None);
        var write = await Put().ExecuteAsync(OtherOrganizationUser, new PutUserGrantsCommand([], []), CancellationToken.None);

        Assert.Equal((RefusalCategory.NotFound, AccessErrorCodes.UserNotFound), (read.Refusal!.Category, read.Refusal.Code));
        Assert.Equal((RefusalCategory.NotFound, AccessErrorCodes.UserNotFound), (write.Refusal!.Category, write.Refusal.Code));
        Assert.Equal([AccessPermissionKeys.GrantsView, AccessPermissionKeys.GrantsManage], _scope.Keys);
        Assert.Empty(_store.Grants.Grants);
    }

    [Fact]
    public async Task An_own_organization_holder_reads_a_user_of_their_organization()
    {
        _accounts.Add(OtherOrganizationUser, organizationId: 6);
        _scope.Decision = FakeOrganizationScope.Own(6);

        var read = await Get().ExecuteAsync(OtherOrganizationUser, CancellationToken.None);

        Assert.True(read.IsSuccess);
    }

    [Fact]
    public async Task PutUserGrants_adds_rescopes_and_removes_rows_in_one_commit()
    {
        var keep = _store.AddRole("keep", AccessPermissionKeys.RolesView);
        var drop = _store.AddRole("drop", AccessPermissionKeys.GrantsView);
        var add = _store.AddRole("add", TestCatalogs.ReportsView);
        _store.Assign(UserId, keep, PermissionScope.Global);
        _store.Assign(UserId, drop);
        _store.GrantDirect(UserId, AccessPermissionKeys.DashboardView, PermissionScope.Global);
        _store.GrantDirect(UserId, AccessPermissionKeys.AccessAdminView);

        var command = new PutUserGrantsCommand(
            Roles: [Role(keep, PermissionScope.OwnOrganization), Role(add, PermissionScope.Global)],
            Permissions: [new PermissionGrantInput(AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization), new PermissionGrantInput(AccessPermissionKeys.RolesView, PermissionScope.Global)]);
        var outcome = await Put().ExecuteAsync(UserId, command, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal([(keep.Id, PermissionScope.OwnOrganization), (add.Id, PermissionScope.Global)], _store.Assignments.OrderBy(row => row.RoleId).Select(row => (row.RoleId, row.Scope)));
        Assert.Equal(
            [(AccessPermissionKeys.RolesView, PermissionScope.Global), (AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization)],
            _store.Grants.Grants.OrderBy(row => row.Permission.Key, StringComparer.Ordinal).Select(row => (row.Permission.Key, row.Scope)));
        Assert.Equal(Now, _store.Assignments.Single(row => row.RoleId == add.Id).AssignedAt);
        Assert.Equal(1, _store.Saves);
        Assert.Contains(outcome.Value.Effective, grant => grant.Key == TestCatalogs.ReportsView);
    }

    [Fact]
    public async Task PutUserGrants_merges_duplicates_to_their_widest_scope()
    {
        var role = _store.AddRole("team", AccessPermissionKeys.RolesView);
        var command = new PutUserGrantsCommand(
            Roles: [Role(role, PermissionScope.OwnOrganization), Role(role, PermissionScope.Global)],
            Permissions: [new PermissionGrantInput(AccessPermissionKeys.DashboardView, PermissionScope.Global), new PermissionGrantInput(AccessPermissionKeys.DashboardView, PermissionScope.OwnOrganization)]);

        Assert.True((await Put().ExecuteAsync(UserId, command, CancellationToken.None)).IsSuccess);

        Assert.Equal(PermissionScope.Global, Assert.Single(_store.Assignments).Scope);
        Assert.Equal(PermissionScope.Global, Assert.Single(_store.Grants.Grants).Scope);
    }

    [Fact]
    public async Task PutUserGrants_refuses_unknown_and_virtual_roles_and_unknown_keys()
    {
        var everyone = _store.AddVirtualRole(VirtualRoleKind.AllAuthenticatedUsers);

        var unknownRole = await Put().ExecuteAsync(UserId, new PutUserGrantsCommand([Role(5555, PermissionScope.Global)], []), CancellationToken.None);
        var virtualRole = await Put().ExecuteAsync(UserId, new PutUserGrantsCommand([Role(everyone, PermissionScope.Global)], []), CancellationToken.None);
        var unknownKey = await Put().ExecuteAsync(UserId, new PutUserGrantsCommand([], [new PermissionGrantInput("unknown.key", PermissionScope.Global)]), CancellationToken.None);

        Assert.Equal(AccessErrorCodes.UnknownRole, unknownRole.Refusal!.Code);
        Assert.Equal(AccessErrorCodes.VirtualRoleNotAssignable, virtualRole.Refusal!.Code);
        Assert.Equal(AccessErrorCodes.UnknownPermission, unknownKey.Refusal!.Code);
        Assert.All([unknownRole, virtualRole, unknownKey], outcome => Assert.Equal(RefusalCategory.Invalid, outcome.Refusal!.Category));
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task PutUserGrants_refuses_an_unknown_user_and_the_break_glass_account()
    {
        var unknown = await Put().ExecuteAsync(UserId + 1000, new PutUserGrantsCommand([], []), CancellationToken.None);
        var breakGlass = await Put().ExecuteAsync(99, new PutUserGrantsCommand([], []), CancellationToken.None);

        Assert.Equal((RefusalCategory.NotFound, AccessErrorCodes.UserNotFound), (unknown.Refusal!.Category, unknown.Refusal.Code));
        Assert.Equal((RefusalCategory.Conflict, AccessErrorCodes.BreakGlassNotEditable), (breakGlass.Refusal!.Category, breakGlass.Refusal.Code));
    }

    [Fact]
    public async Task PutUserGrants_refuses_removing_the_last_manager_and_allows_it_when_confirmed()
    {
        _accounts.Add(Manager);
        foreach (var key in ManagerKeys)
        {
            _store.GrantDirect(Manager, key);
        }

        var refused = await Put().ExecuteAsync(Manager, new PutUserGrantsCommand([], []), CancellationToken.None);
        var confirmed = await Put().ExecuteAsync(Manager, new PutUserGrantsCommand([], [], ConfirmBreakGlassOnly: true), CancellationToken.None);

        Assert.Equal((RefusalCategory.Conflict, AccessErrorCodes.LastManager), (refused.Refusal!.Category, refused.Refusal.Code));
        Assert.Equal([AccessErrorCodes.OnlyBreakGlassManages], confirmed.Value.Warnings);
        Assert.Empty(_store.Grants.Grants);
    }

    [Fact]
    public async Task An_own_organization_grants_manager_gets_403_granting_global_scope_to_a_colleague()
    {
        _accounts.Add(OtherOrganizationUser, organizationId: 5);
        _scope.Decision = FakeOrganizationScope.Own(5);
        _callerAccess = OwnOrganizationCaller(organizationId: 5);
        var role = _store.AddRole("everything", AccessPermissionKeys.GrantsManage);

        var directGrant = await Put().ExecuteAsync(OtherOrganizationUser, new PutUserGrantsCommand([], [new PermissionGrantInput(AccessPermissionKeys.GrantsManage, PermissionScope.Global)]), CancellationToken.None);
        var globalRole = await Put().ExecuteAsync(OtherOrganizationUser, new PutUserGrantsCommand([Role(role, PermissionScope.Global)], []), CancellationToken.None);
        var ownScope = await Put().ExecuteAsync(OtherOrganizationUser, new PutUserGrantsCommand([Role(role, PermissionScope.OwnOrganization)], [new PermissionGrantInput(AccessPermissionKeys.GrantsManage, PermissionScope.OwnOrganization)]), CancellationToken.None);

        Assert.All([directGrant, globalRole], outcome => Assert.Equal((RefusalCategory.Forbidden, AccessErrorCodes.ScopeExceedsCaller), (outcome.Refusal!.Category, outcome.Refusal.Code)));
        Assert.True(ownScope.IsSuccess);
        Assert.Equal(1, _store.Saves);
    }

    [Fact]
    public async Task PutUserGrants_refuses_raising_the_callers_own_grants_with_403()
    {
        var self = _accounts.Add(Manager) with { SubjectId = CallerSubject };
        _accounts.Accounts[^1] = self;
        _store.GrantDirect(Manager, AccessPermissionKeys.GrantsManage, PermissionScope.OwnOrganization);

        var outcome = await Put().ExecuteAsync(Manager, new PutUserGrantsCommand([], [new PermissionGrantInput(AccessPermissionKeys.GrantsManage, PermissionScope.Global)]), CancellationToken.None);

        Assert.Equal((RefusalCategory.Forbidden, AccessErrorCodes.ScopeExceedsCaller), (outcome.Refusal!.Category, outcome.Refusal.Code));
        Assert.Equal(PermissionScope.OwnOrganization, Assert.Single(_store.Grants.Grants).Scope);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public void The_grant_validator_checks_lists_ids_scopes_and_keys()
    {
        var validator = new PutUserGrantsValidator();

        var missing = validator.Validate(new PutUserGrantsCommand(null!, null!));
        var malformed = validator.Validate(new PutUserGrantsCommand(
            [new RoleAssignmentInput(PublicId<RoleIds>.From(1), (PermissionScope)9)],
            [new PermissionGrantInput("Not A Key", PermissionScope.Global)]));
        var noId = validator.Validate(new PutUserGrantsCommand([new RoleAssignmentInput(default, PermissionScope.Global)], []));
        var tooMany = validator.Validate(new PutUserGrantsCommand(
            [.. Enumerable.Range(1, AccessLimits.RolesPerUserMax + 1).Select(id => new RoleAssignmentInput(PublicId<RoleIds>.From(id), PermissionScope.Global))],
            [.. Enumerable.Repeat(new PermissionGrantInput("valid.key", PermissionScope.Global), AccessLimits.PermissionsPerWriteMax + 1)]));

        Assert.Equal([ValidationMessages.Required], missing.ToDictionary()["roles"]);
        Assert.Equal([ValidationMessages.Required], missing.ToDictionary()["permissions"]);
        Assert.Equal([ValidationMessages.InvalidFormat], malformed.ToDictionary()["roles"]);
        Assert.Equal([ValidationMessages.InvalidFormat], malformed.ToDictionary()["permissions"]);
        Assert.Equal([ValidationMessages.Required], noId.ToDictionary()["roles"]);
        Assert.Equal([ValidationMessages.TooLong], tooMany.ToDictionary()["roles"]);
        Assert.Equal([ValidationMessages.TooLong], tooMany.ToDictionary()["permissions"]);
        Assert.True(validator.Validate(new PutUserGrantsCommand([], [])).IsValid);
    }

    private static RoleAssignmentInput Role(Role role, PermissionScope scope) => Role(role.Id, scope);

    private static RoleAssignmentInput Role(long roleId, PermissionScope scope) => new(PublicId<RoleIds>.From(roleId), scope);

    private static FakeResolver OwnOrganizationCaller(long organizationId) => new(TestCatalogs.FullKeys)
    {
        Scope = PermissionScope.OwnOrganization,
        User = new DirectoryUser(UserId: Manager, IsActive: true, HoldsAllPermissions: false, OrganizationId: organizationId),
    };

    private UserGrantsViewBuilder Views() => new(_store, _catalog);

    private GetUserGrants Get() => new(new ScopedAccounts(_accounts, _scope), Views());

    private PutUserGrants Put()
    {
        var changes = new AccessChanges(_store, new FakeTimeProvider(Now), NullLogger<AccessChanges>.Instance);
        return new PutUserGrants(
            new ScopedAccounts(_accounts, _scope),
            new GrantRequestReader(new PutUserGrantsValidator(), _store, new GrantablePermissions(_catalog, _store)),
            new UserGrantWriteGuards(new GrantCeiling(new FakeCurrentUser(CallerSubject), _callerAccess, _store), new ManagerContinuityGuard(_store, _accounts)),
            new UserGrantWriter(_store, _store.Grants, changes),
            Views());
    }
}
