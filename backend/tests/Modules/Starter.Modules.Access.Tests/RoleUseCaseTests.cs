using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class RoleUseCaseTests
{
    private const long Manager = 1;
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly string[] ManagerKeys = [AccessPermissionKeys.RolesManage, AccessPermissionKeys.GrantsManage];

    private readonly AccessStore _store = new AccessStore().WithPermissions(TestCatalogs.FullKeys);
    private readonly FakeAccounts _accounts = new();
    private readonly PermissionCatalog _catalog = TestCatalogs.Full();

    [Fact]
    public async Task CreateRole_saves_an_active_role_with_its_permissions()
    {
        var outcome = await Create().ExecuteAsync(new CreateRoleCommand("reviewers", " Reviewers ", "المراجعون", [AccessPermissionKeys.DashboardView, AccessPermissionKeys.DashboardView]), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var role = outcome.Value.Role;
        Assert.True(role.Id > 0);
        Assert.Equal(("reviewers", "Reviewers", true, (VirtualRoleKind?)null), (role.Code, role.NameEn, role.IsActive, role.VirtualKind));
        Assert.Equal([AccessPermissionKeys.DashboardView], role.Permissions);
        Assert.Empty(outcome.Value.Warnings);
        Assert.Equal(1, _store.Saves);
        Assert.Equal(Now, Assert.Single(_store.Roles).CreatedAt);
    }

    [Fact]
    public async Task CreateRole_refuses_invalid_input_with_every_field_error()
    {
        var outcome = await Create().ExecuteAsync(new CreateRoleCommand("Bad Code", " ", new string('x', AccessLimits.RoleNameMaxLength + 1), null!), CancellationToken.None);

        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal!.Category);
        Assert.Equal(["code", "nameAr", "nameEn", "permissions"], outcome.Refusal.FieldErrors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task CreateRole_refuses_a_code_or_a_name_already_in_use()
    {
        _store.AddRole("reviewers");

        var code = await Create().ExecuteAsync(new CreateRoleCommand("reviewers", "Other", "أخرى", []), CancellationToken.None);
        var name = await Create().ExecuteAsync(new CreateRoleCommand("others", "REVIEWERS EN", "أخرى", []), CancellationToken.None);

        Assert.Equal((RefusalCategory.Conflict, AccessErrorCodes.RoleCodeTaken), (code.Refusal!.Category, code.Refusal.Code));
        Assert.Equal((RefusalCategory.Conflict, AccessErrorCodes.RoleNameTaken), (name.Refusal!.Category, name.Refusal.Code));
    }

    [Theory]
    [InlineData("unknown.key")]
    [InlineData("retired.key")]
    public async Task CreateRole_refuses_a_key_outside_the_live_catalog(string key)
    {
        _store.WithPermissions("retired.key");

        var outcome = await Create().ExecuteAsync(new CreateRoleCommand("reviewers", "Reviewers", "المراجعون", [key]), CancellationToken.None);

        Assert.Equal((RefusalCategory.Invalid, AccessErrorCodes.UnknownPermission), (outcome.Refusal!.Category, outcome.Refusal.Code));
        Assert.Empty(_store.Roles);
    }

    [Fact]
    public async Task ListRoles_and_GetRole_read_the_store()
    {
        var role = _store.AddRole("reviewers", AccessPermissionKeys.DashboardView);
        _store.AddVirtualRole(VirtualRoleKind.AllInternalUsers);

        var list = await new ListRoles(_store).ExecuteAsync(CancellationToken.None);
        var found = await new GetRole(_store).ExecuteAsync(role.Id, CancellationToken.None);
        var missing = await new GetRole(_store).ExecuteAsync(role.Id + 1000, CancellationToken.None);

        Assert.Equal([VirtualRoleKind.AllInternalUsers, null], list.Value.Select(row => row.VirtualKind));
        Assert.Equal(1, list.Value[1].PermissionCount);
        Assert.Equal([AccessPermissionKeys.DashboardView], found.Value.Permissions);
        Assert.Equal((RefusalCategory.NotFound, AccessErrorCodes.RoleNotFound), (missing.Refusal!.Category, missing.Refusal.Code));
    }

    [Fact]
    public async Task UpdateRole_renames_replaces_permissions_and_deactivates()
    {
        var role = _store.AddRole("reviewers", AccessPermissionKeys.DashboardView);

        var outcome = await Update().ExecuteAsync(role.Id, new UpdateRoleCommand("Checkers", "المدققون", [TestCatalogs.ReportsView], IsActive: false), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(("reviewers", "Checkers", "المدققون", false), (role.Code, role.NameEn, role.NameAr, role.IsActive));
        Assert.Equal([TestCatalogs.ReportsView], outcome.Value.Role.Permissions);
        Assert.Equal(Now, role.UpdatedAt);
        Assert.Equal(1, _store.Saves);
    }

    [Fact]
    public async Task UpdateRole_edits_a_virtual_roles_permissions()
    {
        var everyone = _store.AddVirtualRole(VirtualRoleKind.AllAuthenticatedUsers);

        var outcome = await Update().ExecuteAsync(everyone.Id, new UpdateRoleCommand(everyone.NameEn, everyone.NameAr, [AccessPermissionKeys.DashboardView], IsActive: true), CancellationToken.None);

        Assert.Equal([AccessPermissionKeys.DashboardView], outcome.Value.Role.Permissions);
        Assert.Equal(VirtualRoleKind.AllAuthenticatedUsers, outcome.Value.Role.VirtualKind);
    }

    [Fact]
    public async Task UpdateRole_refuses_an_unknown_role_a_taken_name_and_invalid_input()
    {
        var role = _store.AddRole("reviewers");
        _store.AddRole("checkers");

        var missing = await Update().ExecuteAsync(role.Id + 1000, new UpdateRoleCommand("X", "Y", [], IsActive: true), CancellationToken.None);
        var taken = await Update().ExecuteAsync(role.Id, new UpdateRoleCommand("checkers en", "Y", [], IsActive: true), CancellationToken.None);
        var ownName = await Update().ExecuteAsync(role.Id, new UpdateRoleCommand("reviewers en", "reviewers ar", [], IsActive: true), CancellationToken.None);
        var invalid = await Update().ExecuteAsync(role.Id, new UpdateRoleCommand("X", "Y", ["Not A Key"], IsActive: true), CancellationToken.None);

        Assert.Equal(AccessErrorCodes.RoleNotFound, missing.Refusal!.Code);
        Assert.Equal(AccessErrorCodes.RoleNameTaken, taken.Refusal!.Code);
        Assert.True(ownName.IsSuccess);
        Assert.Equal(PlatformErrorCodes.Validation, invalid.Refusal!.Code);
    }

    [Fact]
    public async Task UpdateRole_refuses_to_strip_the_last_manager_role_unless_confirmed()
    {
        _accounts.Add(Manager);
        _accounts.Add(99, isBreakGlass: true);
        var admins = _store.AddRole("admins", ManagerKeys);
        _store.Assign(Manager, admins);

        var refused = await Update().ExecuteAsync(admins.Id, new UpdateRoleCommand(admins.NameEn, admins.NameAr, ManagerKeys, IsActive: false), CancellationToken.None);
        var confirmed = await Update().ExecuteAsync(admins.Id, new UpdateRoleCommand(admins.NameEn, admins.NameAr, ManagerKeys, IsActive: false, ConfirmBreakGlassOnly: true), CancellationToken.None);

        Assert.Equal((RefusalCategory.Conflict, AccessErrorCodes.LastManager), (refused.Refusal!.Category, refused.Refusal.Code));
        Assert.Equal([AccessErrorCodes.OnlyBreakGlassManages], confirmed.Value.Warnings);
        Assert.False(admins.IsActive);
    }

    [Fact]
    public void The_role_validators_check_every_field()
    {
        var create = new CreateRoleValidator().Validate(new CreateRoleCommand(new string('a', AccessLimits.RoleCodeMaxLength + 1), "A", "B", ["valid.key"]));
        var tooMany = new UpdateRoleValidator().Validate(new UpdateRoleCommand("A", "B", [.. Enumerable.Repeat("valid.key", AccessLimits.PermissionsPerWriteMax + 1)], IsActive: true));
        var valid = new UpdateRoleValidator().Validate(new UpdateRoleCommand("A", "B", ["valid.key"], IsActive: true));

        Assert.Equal([ValidationMessages.TooLong], create.ToDictionary()["code"]);
        Assert.Equal([ValidationMessages.TooLong], tooMany.ToDictionary()["permissions"]);
        Assert.True(valid.IsValid);
    }

    private AccessChanges Changes() => new(_store, new FakeTimeProvider(Now), NullLogger<AccessChanges>.Instance);

    private GrantablePermissions Grantable() => new(_catalog, _store);

    private CreateRole Create() => new(new CreateRoleValidator(), _store, Grantable(), Changes());

    private UpdateRole Update() => new(new UpdateRoleValidator(), _store, Grantable(), new ManagerContinuityGuard(_store, _accounts), Changes());
}
