using Microsoft.Extensions.Time.Testing;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class UserPermissionGrantsTests
{
    private const long UserId = 7;
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private readonly PermissionCatalog _catalog = TestCatalogs.Default();
    private readonly FakePermissionRepository _rows = new();
    private readonly FakeGrantRepository _grants = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Grants_the_missing_keys_with_the_current_time()
    {
        await SynchronizeAsync();

        await Grants().EnsureGrantedAsync(UserId, [AccessPermissionKeys.DashboardView], CancellationToken.None);

        var grant = Assert.Single(_grants.Grants);
        Assert.Equal(UserId, grant.UserId);
        Assert.Equal(AccessPermissionKeys.DashboardView, grant.Permission.Key);
        Assert.Equal(Now, grant.GrantedAt);
        Assert.Equal(2, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Is_idempotent()
    {
        await SynchronizeAsync();
        var grants = Grants();
        await grants.EnsureGrantedAsync(UserId, [AccessPermissionKeys.DashboardView], CancellationToken.None);

        await grants.EnsureGrantedAsync(UserId, [AccessPermissionKeys.DashboardView, AccessPermissionKeys.DashboardView], CancellationToken.None);

        Assert.Single(_grants.Grants);
        Assert.Equal(2, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Adds_only_what_is_missing()
    {
        await SynchronizeAsync();
        var grants = Grants();
        await grants.EnsureGrantedAsync(UserId, [AccessPermissionKeys.DashboardView], CancellationToken.None);

        await grants.EnsureGrantedAsync(UserId, [AccessPermissionKeys.DashboardView, AccessPermissionKeys.AccessAdminView], CancellationToken.None);

        Assert.Equal(
            [AccessPermissionKeys.AccessAdminView, AccessPermissionKeys.DashboardView],
            _grants.Grants.Select(grant => grant.Permission.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_key_outside_the_catalog_without_writing()
    {
        await SynchronizeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Grants().EnsureGrantedAsync(UserId, ["unknown.view"], CancellationToken.None));

        Assert.Empty(_grants.Grants);
    }

    [Fact]
    public async Task Refuses_when_the_catalog_was_never_synchronized()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Grants().EnsureGrantedAsync(UserId, [AccessPermissionKeys.DashboardView], CancellationToken.None));

        Assert.Empty(_grants.Grants);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    private Task<PermissionSyncReport> SynchronizeAsync() =>
        new PermissionSynchronizer(_catalog, _rows, _unitOfWork).SynchronizeAsync(CancellationToken.None);

    private UserPermissionGrants Grants() => new(_catalog, _rows, _grants, _unitOfWork, new FakeTimeProvider(Now));
}
