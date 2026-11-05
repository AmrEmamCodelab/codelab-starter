using Starter.Application.Authorization;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class DomainTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset Later = Created.AddHours(1);

    [Theory]
    [InlineData("reviewers", true)]
    [InlineData("request-reviewers", true)]
    [InlineData("team2-leads", true)]
    [InlineData("Reviewers", false)]
    [InlineData("2nd-line", false)]
    [InlineData("double--dash", false)]
    [InlineData("trailing-", false)]
    [InlineData("with space", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Role_codes_are_lower_case_kebab(string? code, bool valid) =>
        Assert.Equal(valid, RoleCodeFormat.IsValid(code));

    [Fact]
    public void A_role_code_longer_than_the_limit_is_invalid() =>
        Assert.False(RoleCodeFormat.IsValid(new string('a', AccessLimits.RoleCodeMaxLength + 1)));

    [Fact]
    public void A_new_role_is_active_ordinary_and_trims_its_names()
    {
        var role = Role.Create(code: "reviewers", names: new RoleNames(En: "  Reviewers ", Ar: " المراجعون "), createdAt: Created);

        Assert.True(role.IsActive);
        Assert.False(role.IsVirtual);
        Assert.Null(role.VirtualKind);
        Assert.Equal("Reviewers", role.NameEn);
        Assert.Equal("المراجعون", role.NameAr);
        Assert.Equal(Created, role.CreatedAt);
        Assert.Empty(role.Permissions);
    }

    [Fact]
    public void A_malformed_code_or_a_blank_name_is_refused_by_the_entity()
    {
        Assert.Throws<ArgumentException>(() => Role.Create(code: "Bad Code", names: new RoleNames(En: "x", Ar: "y"), createdAt: Created));
        Assert.ThrowsAny<ArgumentException>(() => Role.Create(code: "good", names: new RoleNames(En: " ", Ar: "y"), createdAt: Created));
    }

    [Fact]
    public void ReplacePermissions_adds_and_removes_and_reports_a_change()
    {
        var store = new AccessStore().WithPermissions("a.view", "b.view", "c.view");
        var role = store.AddRole("team", "a.view", "b.view");

        var changed = role.ReplacePermissions([store.Permission("b.view"), store.Permission("c.view")], Later);

        Assert.True(changed);
        Assert.Equal(["b.view", "c.view"], role.Permissions.Select(granted => granted.Permission.Key).Order(StringComparer.Ordinal));
        Assert.Equal(Later, role.UpdatedAt);
    }

    [Fact]
    public void ReplacePermissions_with_the_same_set_changes_nothing()
    {
        var store = new AccessStore().WithPermissions("a.view");
        var role = store.AddRole("team", "a.view");

        Assert.False(role.ReplacePermissions([store.Permission("a.view"), store.Permission("a.view")], Later));
        Assert.Single(role.Permissions);
        Assert.Equal(Created, role.UpdatedAt);
    }

    [Fact]
    public void SetActive_toggles_and_stamps()
    {
        var role = Role.Create(code: "team", names: new RoleNames(En: "Team", Ar: "الفريق"), createdAt: Created);

        role.SetActive(false, Later);

        Assert.False(role.IsActive);
        Assert.Equal(Later, role.UpdatedAt);
    }

    [Fact]
    public void A_virtual_role_is_never_assigned()
    {
        var role = new AccessStore().AddRole("everyone").AsVirtual(VirtualRoleKind.AllAuthenticatedUsers);

        Assert.True(role.IsVirtual);
        Assert.Throws<ArgumentException>(() => UserRoleAssignment.Create(userId: 1, role: role, scope: PermissionScope.Global, assignedAt: Created));
    }

    [Fact]
    public void Assignment_and_grant_scopes_change_only_when_different()
    {
        var store = new AccessStore().WithPermissions("a.view");
        var assignment = UserRoleAssignment.Create(userId: 1, role: store.AddRole("team"), scope: PermissionScope.Global, assignedAt: Created);
        var grant = UserPermissionGrant.Create(userId: 1, permission: store.Permission("a.view"), grantedAt: Created);

        Assert.Equal(PermissionScope.Global, grant.Scope);
        Assert.False(assignment.ChangeScope(PermissionScope.Global, Later));
        Assert.True(assignment.ChangeScope(PermissionScope.OwnOrganization, Later));
        Assert.Equal(Later, assignment.AssignedAt);
        Assert.False(grant.ChangeScope(PermissionScope.Global, Later));
        Assert.True(grant.ChangeScope(PermissionScope.OwnOrganization, Later));
        Assert.Equal(PermissionScope.OwnOrganization, grant.Scope);
    }

    [Fact]
    public void Virtual_role_kinds_reach_the_matching_users()
    {
        Assert.True(VirtualRoleKind.AllInternalUsers.Reaches(FakeAccounts.FirstKind));
        Assert.False(VirtualRoleKind.AllInternalUsers.Reaches(FakeAccounts.SecondKind));
        Assert.True(VirtualRoleKind.AllExternalUsers.Reaches(FakeAccounts.SecondKind));
        Assert.False(VirtualRoleKind.AllExternalUsers.Reaches(FakeAccounts.FirstKind));
        Assert.True(VirtualRoleKind.AllAuthenticatedUsers.Reaches(FakeAccounts.FirstKind));
        Assert.True(VirtualRoleKind.AllAuthenticatedUsers.Reaches(FakeAccounts.SecondKind));
        Assert.False(((VirtualRoleKind)99).Reaches(FakeAccounts.FirstKind));
    }

    [Fact]
    public void Enums_have_their_stored_values()
    {
        Assert.Equal([1, 2, 3], Enum.GetValues<VirtualRoleKind>().Select(kind => (int)kind));
        Assert.Equal([1, 2, 3, 4], Enum.GetValues<Application.PermissionSourceKind>().Select(kind => (int)kind));
        Assert.Equal([1, 2, 3], Enum.GetValues<Application.GuardVerdict>().Select(kind => (int)kind));
        Assert.Equal([1, 2, 3], Enum.GetValues<Application.AccessChangeKind>().Select(kind => (int)kind));
    }
}
