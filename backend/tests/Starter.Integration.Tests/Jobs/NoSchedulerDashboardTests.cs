using System.Net;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Jobs;

/// <summary>
/// The scheduler's dashboard is never mapped (rule jobs.no-dashboard): on a node that runs the job server, its routes
/// answer 404 to an anonymous caller and to the break-glass administrator alike. This probe sees the default paths only;
/// a mapping at any other path, in any backend file, fails Starter.Architecture.Tests.DataBoundaryTests.
/// </summary>
[Collection(JobServerHostGroup.Name)]
public sealed class NoSchedulerDashboardTests(JobServerHost host)
{
    /// <summary>The scheduler library's default dashboard path, and two of its pages.</summary>
    public static TheoryData<string> DashboardPaths => new() { "/hangfire", "/hangfire/recurring", "/hangfire/jobs/enqueued" };

    [Theory]
    [MemberData(nameof(DashboardPaths))]
    public async Task The_dashboard_routes_are_not_found_even_for_the_administrator(string path)
    {
        using var api = host.CreateApi();
        var adminToken = await host.AdminTokenAsync();

        using var anonymous = await api.GetAsync(path, accessToken: null);
        using var administrator = await api.GetAsync(path, adminToken);

        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, administrator.StatusCode);
    }
}
