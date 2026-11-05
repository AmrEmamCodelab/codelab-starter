using Starter.Application.Authorization;
using Starter.Application.Users;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Tests.Fakes;

internal sealed class StaticCatalogSource(params PermissionDefinition[] permissions) : IPermissionCatalogSource
{
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } = permissions;
}

internal sealed class FakeUserDirectory : IUserDirectory
{
    private readonly Dictionary<Guid, DirectoryUser> _users = [];

    public int Lookups { get; private set; }

    public void Add(Guid subjectId, DirectoryUser user) => _users[subjectId] = user;

    public Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult(_users.GetValueOrDefault(subjectId));
    }
}

internal sealed class FakePermissionRepository : IPermissionRepository
{
    public List<PermissionEntry> Rows { get; } = [];

    public int Adds { get; private set; }

    public Task<IReadOnlyList<PermissionEntry>> ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PermissionEntry>>([.. Rows]);

    public Task<IReadOnlyList<PermissionEntry>> FindByKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PermissionEntry>>([.. Rows.Where(row => keys.Contains(row.Key))]);

    public void Add(PermissionEntry permission)
    {
        Adds++;
        Rows.Add(permission);
    }
}

internal sealed class FakeGrantRepository : IUserPermissionGrantRepository
{
    public List<UserPermissionGrant> Grants { get; } = [];

    public Task<IReadOnlyList<string>> GrantedKeysAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. Grants.Where(grant => grant.UserId == userId).Select(grant => grant.Permission.Key)]);

    public Task<IReadOnlyList<UserPermissionGrant>> ForUserAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserPermissionGrant>>([.. Grants.Where(grant => grant.UserId == userId)]);

    public void Add(UserPermissionGrant grant) => Grants.Add(grant);

    public void Remove(UserPermissionGrant grant) => Grants.Remove(grant);
}

internal sealed class FakeUnitOfWork : IAccessUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(Saves);
    }
}

internal sealed class FakeCurrentUser(Guid? subjectId, bool isAuthenticated = true) : ICurrentUserContext
{
    public bool IsAuthenticated { get; } = isAuthenticated;

    public Guid? SubjectId { get; } = subjectId;

    public string? SessionId => null;
}

internal sealed class FakeResolver(params string[] keys) : IUserPermissionResolver
{
    public int Calls { get; private set; }

    public DirectoryUser? User { get; init; }

    public Starter.Application.Authorization.PermissionScope Scope { get; init; } = Starter.Application.Authorization.PermissionScope.Global;

    public Task<IReadOnlySet<string>> ResolveAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(keys, StringComparer.Ordinal));
    }

    public Task<ResolvedAccess> ResolveAccessAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new ResolvedAccess(User, [.. keys.Select(key => new EffectiveGrant(key, Scope, []))]));
    }

    public Task<ResolvedAccess> ResolvePotentialAccessAsync(Guid subjectId, CancellationToken cancellationToken) =>
        ResolveAccessAsync(subjectId, cancellationToken);
}

internal sealed class FakeOrganizationScope : IOrganizationScope
{
    public static readonly ScopeDecision Global = new(Allowed: true, Scope: PermissionScope.Global, OrganizationId: null);

    public ScopeDecision Decision { get; set; } = Global;

    public List<string> Keys { get; } = [];

    public static ScopeDecision Own(long? organizationId) => new(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: organizationId);

    public Task<ScopeDecision> ScopeForAsync(string permissionKey, CancellationToken cancellationToken)
    {
        Keys.Add(permissionKey);
        return Task.FromResult(Decision);
    }
}

internal static class TestCatalogs
{
    public const string ReportsView = "reports.view";

    public static PermissionCatalog Of(params string[] keys) =>
        new([new StaticCatalogSource([.. keys.Select(key => new PermissionDefinition(Key: key, Description: "Test permission " + key))])]);

    public static PermissionCatalog Default() => Of(AccessPermissionKeys.DashboardView, AccessPermissionKeys.AccessAdminView, ReportsView);

    /// <summary>Every key this module declares plus <see cref="ReportsView"/>.</summary>
    public static string[] FullKeys { get; } =
    [
        AccessPermissionKeys.DashboardView, AccessPermissionKeys.AccessAdminView, AccessPermissionKeys.RolesView,
        AccessPermissionKeys.RolesManage, AccessPermissionKeys.GrantsView, AccessPermissionKeys.GrantsManage, ReportsView,
    ];

    public static PermissionCatalog Full() => Of(FullKeys);
}
