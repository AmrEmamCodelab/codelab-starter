using Starter.Modules.Access.Application;

namespace Starter.Modules.Access.Infrastructure;

/// <summary>
/// The phase 2 <see cref="IDashboardSummaryStore"/>: an empty summary (the endpoint proves the permission gate end to
/// end). Replace it with a store over real data when the dashboard gets content.
/// </summary>
public sealed class PlaceholderDashboardSummaryStore : IDashboardSummaryStore
{
    private static readonly Task<DashboardSummary> Empty = Task.FromResult(new DashboardSummary());

    /// <inheritdoc />
    public Task<DashboardSummary> ReadAsync(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? Task.FromCanceled<DashboardSummary>(cancellationToken) : Empty;
}

/// <summary>
/// The phase 2 <see cref="IAccessAdminOverviewStore"/>: an empty overview until the phase 3 role and grant editor.
/// </summary>
public sealed class PlaceholderAccessAdminOverviewStore : IAccessAdminOverviewStore
{
    private static readonly Task<AccessAdminOverview> Empty = Task.FromResult(new AccessAdminOverview());

    /// <inheritdoc />
    public Task<AccessAdminOverview> ReadAsync(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? Task.FromCanceled<AccessAdminOverview>(cancellationToken) : Empty;
}
