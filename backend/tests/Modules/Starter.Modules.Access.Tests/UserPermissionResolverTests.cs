using Starter.Application.Users;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class UserPermissionResolverTests
{
    private const long UserId = 42;
    private static readonly Guid Subject = Guid.Parse("7f1e4c1a-3b0d-4a43-9f1e-2c6b1d1a0001");

    private readonly FakeUserDirectory _directory = new();
    private readonly FakeGrantRepository _grants = new();
    private readonly PermissionCatalog _catalog = TestCatalogs.Default();

    [Fact]
    public async Task An_unknown_user_holds_nothing()
    {
        var permissions = await Resolver().ResolveAsync(Subject, CancellationToken.None);

        Assert.Empty(permissions);
    }

    [Fact]
    public async Task An_inactive_user_holds_nothing_even_with_grants_or_all_permissions()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId, IsActive: false, HoldsAllPermissions: true));
        Grant(AccessPermissionKeys.DashboardView);

        var permissions = await Resolver().ResolveAsync(Subject, CancellationToken.None);

        Assert.Empty(permissions);
    }

    [Fact]
    public async Task A_user_holding_all_permissions_gets_the_whole_catalog()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId, IsActive: true, HoldsAllPermissions: true));

        var permissions = await Resolver().ResolveAsync(Subject, CancellationToken.None);

        Assert.Equal(_catalog.Keys.Order(StringComparer.Ordinal), permissions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_active_user_gets_the_grants_that_are_still_in_the_catalog()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId, IsActive: true, HoldsAllPermissions: false));
        Grant(AccessPermissionKeys.DashboardView);
        Grant("retired.key");

        var permissions = await Resolver().ResolveAsync(Subject, CancellationToken.None);

        Assert.Equal([AccessPermissionKeys.DashboardView], permissions);
    }

    [Fact]
    public async Task Another_users_grants_do_not_leak()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId + 1, IsActive: true, HoldsAllPermissions: false));
        Grant(AccessPermissionKeys.DashboardView);

        Assert.Empty(await Resolver().ResolveAsync(Subject, CancellationToken.None));
    }

    [Fact]
    public async Task Every_call_resolves_live()
    {
        _directory.Add(Subject, new DirectoryUser(UserId: UserId, IsActive: true, HoldsAllPermissions: false));
        var resolver = Resolver();
        Assert.Empty(await resolver.ResolveAsync(Subject, CancellationToken.None));

        Grant(AccessPermissionKeys.DashboardView);

        Assert.Contains(AccessPermissionKeys.DashboardView, await resolver.ResolveAsync(Subject, CancellationToken.None));
        Assert.Equal(2, _directory.Lookups);
    }

    private UserPermissionResolver Resolver() => new(_directory, _grants, _catalog);

    private void Grant(string key) =>
        _grants.Add(UserPermissionGrant.Create(
            userId: UserId,
            permission: PermissionEntry.Create(key: key, owner: key.Split('.')[0], description: "Test."),
            grantedAt: DateTimeOffset.UnixEpoch));
}
