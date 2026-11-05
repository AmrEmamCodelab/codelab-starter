using Starter.Application.Results;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Infrastructure;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class UseCaseTests
{
    private static readonly Guid Subject = Guid.Parse("7f1e4c1a-3b0d-4a43-9f1e-2c6b1d1a0003");

    [Fact]
    public async Task GetMyPermissions_returns_the_live_keys_sorted()
    {
        var useCase = new GetMyPermissions(
            new FakeCurrentUser(Subject),
            new FakeResolver(AccessPermissionKeys.DashboardView, AccessPermissionKeys.AccessAdminView));

        var outcome = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal([AccessPermissionKeys.AccessAdminView, AccessPermissionKeys.DashboardView], outcome.Value.Permissions);
    }

    [Fact]
    public async Task GetMyPermissions_refuses_a_caller_without_a_subject()
    {
        var resolver = new FakeResolver(AccessPermissionKeys.DashboardView);

        var outcome = await new GetMyPermissions(new FakeCurrentUser(subjectId: null), resolver).ExecuteAsync(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(RefusalCategory.Forbidden, outcome.Refusal.Category);
        Assert.Equal(AccessErrorCodes.SubjectRequired, outcome.Refusal.Code);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task GetDashboard_returns_what_its_store_reads()
    {
        var summary = new DashboardSummary();

        var outcome = await new GetDashboard(new StubDashboardStore(summary)).ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Same(summary, outcome.Value);
    }

    [Fact]
    public async Task GetAccessAdminOverview_returns_what_its_store_reads()
    {
        var overview = new AccessAdminOverview();

        var outcome = await new GetAccessAdminOverview(new StubOverviewStore(overview)).ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Same(overview, outcome.Value);
    }

    [Fact]
    public async Task The_placeholder_stores_return_empty_data()
    {
        Assert.NotNull(await new PlaceholderDashboardSummaryStore().ReadAsync(CancellationToken.None));
        Assert.NotNull(await new PlaceholderAccessAdminOverviewStore().ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_placeholder_use_cases_honour_cancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GetDashboard(new PlaceholderDashboardSummaryStore()).ExecuteAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GetAccessAdminOverview(new PlaceholderAccessAdminOverviewStore()).ExecuteAsync(cancelled.Token));
    }

    private sealed class StubDashboardStore(DashboardSummary summary) : IDashboardSummaryStore
    {
        public Task<DashboardSummary> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(summary);
    }

    private sealed class StubOverviewStore(AccessAdminOverview overview) : IAccessAdminOverviewStore
    {
        public Task<AccessAdminOverview> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(overview);
    }
}
