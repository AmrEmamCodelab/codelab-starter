using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Results;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class CatalogUseCaseTests
{
    [Fact]
    public async Task ListPermissions_returns_every_row_sorted_with_its_retired_flag()
    {
        var store = new AccessStore().WithPermissions("reports.view", "access.roles.view", "old.key");
        store.Permission("old.key").Retire();

        var rows = (await new ListPermissions(store).ExecuteAsync(CancellationToken.None)).Value;

        Assert.Equal(["access.roles.view", "old.key", "reports.view"], rows.Select(row => row.Key));
        Assert.Equal(("access", false), (rows[0].Module, rows[0].IsRetired));
        Assert.True(rows[1].IsRetired);
        Assert.False(string.IsNullOrWhiteSpace(rows[2].Description));
    }

    [Fact]
    public async Task SearchAccessUsers_trims_cuts_and_reads_one_page_of_every_state()
    {
        var accounts = new FakeAccounts();
        accounts.Add(1);
        accounts.Add(12, isActive: false);
        var search = new SearchAccessUsers(accounts, new FakeOrganizationScope());

        var found = (await search.ExecuteAsync("  user1 ", CancellationToken.None)).Value;
        await search.ExecuteAsync(new string('u', SearchAccessUsers.SearchMaxLength + 10), CancellationToken.None);
        await search.ExecuteAsync("   ", CancellationToken.None);

        Assert.Equal([1L, 12L], found.Select(account => account.UserId));
        Assert.Equal(("user1", (bool?)null, 1, SearchAccessUsers.PageSize), (accounts.Queries[0].Search, accounts.Queries[0].IsActive, accounts.Queries[0].Page, accounts.Queries[0].PageSize));
        Assert.Equal(SearchAccessUsers.SearchMaxLength, accounts.Queries[1].Search!.Length);
        Assert.Null(accounts.Queries[2].Search);
    }

    [Fact]
    public async Task SearchAccessUsers_at_own_organization_scope_finds_only_that_organizations_accounts()
    {
        var accounts = new FakeAccounts();
        accounts.Add(1, organizationId: 5);
        accounts.Add(2, organizationId: 6);
        var scope = new FakeOrganizationScope { Decision = FakeOrganizationScope.Own(5) };

        var found = (await new SearchAccessUsers(accounts, scope).ExecuteAsync(search: null, CancellationToken.None)).Value;

        Assert.Equal([1L], found.Select(account => account.UserId));
        Assert.Equal([AccessPermissionKeys.GrantsView], scope.Keys);
    }

    [Fact]
    public async Task SearchAccessUsers_for_an_own_organization_holder_without_an_organization_finds_nothing()
    {
        var accounts = new FakeAccounts();
        accounts.Add(1, organizationId: 5);

        var found = (await new SearchAccessUsers(accounts, new FakeOrganizationScope { Decision = FakeOrganizationScope.Own(null) })
            .ExecuteAsync(search: null, CancellationToken.None)).Value;

        Assert.Empty(found);
        Assert.Empty(accounts.Queries);
    }

    [Fact]
    public async Task GrantablePermissions_needs_the_catalog_and_a_current_row()
    {
        var store = new AccessStore().WithPermissions("reports.view");
        var grantable = new GrantablePermissions(TestCatalogs.Of("reports.view", "dashboard.view"), store);

        var loaded = await grantable.LoadAsync(["reports.view", "reports.view"], CancellationToken.None);
        var unsynchronized = await grantable.LoadAsync(["dashboard.view"], CancellationToken.None);
        var empty = await grantable.LoadAsync([], CancellationToken.None);
        store.Permission("reports.view").Retire();
        var retiredRow = await grantable.LoadAsync(["reports.view"], CancellationToken.None);

        Assert.Equal(["reports.view"], loaded.Value.Keys);
        Assert.Equal(AccessErrorCodes.UnknownPermission, unsynchronized.Refusal!.Code);
        Assert.Empty(empty.Value);
        Assert.Equal(RefusalCategory.Invalid, retiredRow.Refusal!.Category);
    }

    [Fact]
    public async Task AccessChanges_saves_then_records_and_warns_for_break_glass_only()
    {
        var store = new AccessStore();
        var logger = new ListLogger();
        var changes = new AccessChanges(store, new FakeTimeProvider(DateTimeOffset.UnixEpoch), logger);
        var described = 0;

        await changes.CommitAsync(
            () =>
            {
                described = store.Saves;
                return new AccessAuditEvent(AccessChangeKind.UserGrantsReplaced, TargetId: 5, RoleCount: 1, PermissionCount: 2, GuardVerdict.AllowedBreakGlassOnly);
            },
            CancellationToken.None);

        Assert.Equal(1, described);
        Assert.Equal(DateTimeOffset.UnixEpoch, changes.Now);
        Assert.Equal([LogLevel.Information, LogLevel.Warning], logger.Levels);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("user5", StringComparison.Ordinal));
    }

    private sealed class ListLogger : ILogger<AccessChanges>
    {
        public List<LogLevel> Levels { get; } = [];

        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }
    }
}
