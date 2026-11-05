using Starter.Application.Authorization;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class PermissionSynchronizerTests
{
    private readonly FakePermissionRepository _rows = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task The_first_run_inserts_the_catalog()
    {
        var report = await Synchronizer(TestCatalogs.Default()).SynchronizeAsync(CancellationToken.None);

        Assert.Equal(new PermissionSyncReport(Added: 3, Refreshed: 0, Retired: 0), report);
        Assert.Equal(1, _unitOfWork.Saves);
        var dashboard = Assert.Single(_rows.Rows, row => row.Key == AccessPermissionKeys.DashboardView);
        Assert.Equal("dashboard", dashboard.Owner);
        Assert.False(dashboard.IsObsolete);
    }

    [Fact]
    public async Task A_second_run_with_the_same_catalog_writes_nothing()
    {
        var catalog = TestCatalogs.Default();
        await Synchronizer(catalog).SynchronizeAsync(CancellationToken.None);

        var report = await Synchronizer(catalog).SynchronizeAsync(CancellationToken.None);

        Assert.True(report.IsUnchanged);
        Assert.Equal(3, _rows.Rows.Count);
        Assert.Equal(3, _rows.Adds);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task A_key_that_left_the_catalog_is_retired_not_deleted()
    {
        await Synchronizer(TestCatalogs.Default()).SynchronizeAsync(CancellationToken.None);

        var report = await Synchronizer(TestCatalogs.Of(AccessPermissionKeys.DashboardView, AccessPermissionKeys.AccessAdminView))
            .SynchronizeAsync(CancellationToken.None);

        Assert.Equal(new PermissionSyncReport(Added: 0, Refreshed: 0, Retired: 1), report);
        Assert.True(Assert.Single(_rows.Rows, row => row.Key == TestCatalogs.ReportsView).IsObsolete);
        Assert.Equal(3, _rows.Rows.Count);
    }

    [Fact]
    public async Task A_returning_key_is_un_retired_and_a_changed_description_refreshed()
    {
        await Synchronizer(TestCatalogs.Default()).SynchronizeAsync(CancellationToken.None);
        await Synchronizer(TestCatalogs.Of(AccessPermissionKeys.DashboardView)).SynchronizeAsync(CancellationToken.None);
        var changed = new PermissionCatalog(
        [
            new StaticCatalogSource(
                new PermissionDefinition(Key: AccessPermissionKeys.DashboardView, Description: "A new description."),
                new PermissionDefinition(Key: AccessPermissionKeys.AccessAdminView, Description: "Test permission " + AccessPermissionKeys.AccessAdminView)),
        ]);

        var report = await Synchronizer(changed).SynchronizeAsync(CancellationToken.None);

        Assert.Equal(new PermissionSyncReport(Added: 0, Refreshed: 2, Retired: 0), report);
        Assert.Equal("A new description.", _rows.Rows.Single(row => row.Key == AccessPermissionKeys.DashboardView).Description);
        Assert.False(_rows.Rows.Single(row => row.Key == AccessPermissionKeys.AccessAdminView).IsObsolete);
    }

    [Fact]
    public void Retire_and_refresh_report_whether_anything_changed()
    {
        var row = PermissionEntry.Create(key: TestCatalogs.ReportsView, owner: "reports", description: "View reports.");

        Assert.False(row.Refresh(owner: "reports", description: "View reports."));
        Assert.True(row.Retire());
        Assert.False(row.Retire());
        Assert.True(row.Refresh(owner: "reports", description: "View reports."));
        Assert.False(row.IsObsolete);
    }

    private PermissionSynchronizer Synchronizer(PermissionCatalog catalog) => new(catalog, _rows, _unitOfWork);
}
