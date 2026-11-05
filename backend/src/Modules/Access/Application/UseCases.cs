using Starter.Application.Results;
using Starter.Application.Users;

namespace Starter.Modules.Access.Application;

/// <summary>The caller's own permissions.</summary>
/// <param name="Permissions">The permission keys the caller holds now, sorted (ordinal).</param>
public sealed record MyPermissions(IReadOnlyList<string> Permissions);

/// <summary>Lists the permissions the signed-in caller holds now (the frontend gates routes and buttons with them).</summary>
public sealed class GetMyPermissions(ICurrentUserContext currentUser, IUserPermissionResolver resolver)
{
    /// <summary>The caller's permissions, or a 403 refusal when the token carries no readable subject.</summary>
    public async Task<Outcome<MyPermissions>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (currentUser.SubjectId is not { } subjectId)
        {
            return Refusal.Forbidden(AccessErrorCodes.SubjectRequired);
        }

        var keys = await resolver.ResolveAsync(subjectId, cancellationToken);
        return new MyPermissions([.. keys.Order(StringComparer.Ordinal)]);
    }
}

/// <summary>The dashboard's data. Empty in phase 2: the endpoint proves the permission gate end to end.</summary>
public sealed record DashboardSummary;

/// <summary>The data port of <see cref="GetDashboard"/>, implemented in Infrastructure.</summary>
public interface IDashboardSummaryStore
{
    /// <summary>The dashboard's data.</summary>
    Task<DashboardSummary> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Reads the dashboard. The permission is enforced by the endpoint's <c>[HasPermission]</c>.</summary>
public sealed class GetDashboard(IDashboardSummaryStore store)
{
    /// <summary>The dashboard's data.</summary>
    public async Task<Outcome<DashboardSummary>> ExecuteAsync(CancellationToken cancellationToken) =>
        await store.ReadAsync(cancellationToken);
}

/// <summary>The access administration overview. An empty placeholder until the phase 3 role and grant editor.</summary>
public sealed record AccessAdminOverview;

/// <summary>The data port of <see cref="GetAccessAdminOverview"/>, implemented in Infrastructure.</summary>
public interface IAccessAdminOverviewStore
{
    /// <summary>The overview.</summary>
    Task<AccessAdminOverview> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Reads the access administration overview. The permission is enforced by the endpoint's <c>[HasPermission]</c>.</summary>
public sealed class GetAccessAdminOverview(IAccessAdminOverviewStore store)
{
    /// <summary>The overview.</summary>
    public async Task<Outcome<AccessAdminOverview>> ExecuteAsync(CancellationToken cancellationToken) =>
        await store.ReadAsync(cancellationToken);
}
