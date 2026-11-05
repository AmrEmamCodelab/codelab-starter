using Starter.Application.Persistence;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>The permission catalog rows.</summary>
public interface IPermissionRepository
{
    /// <summary>Every row, current and retired (tracked, so changes are written by the unit of work).</summary>
    Task<IReadOnlyList<PermissionEntry>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>The rows with these keys (tracked); keys without a row are absent from the result.</summary>
    Task<IReadOnlyList<PermissionEntry>> FindByKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);

    /// <summary>Stages a new row.</summary>
    void Add(PermissionEntry permission);
}

/// <summary>The direct user grants.</summary>
public interface IUserPermissionGrantRepository
{
    /// <summary>The keys of every permission granted to <paramref name="userId"/>, retired ones included.</summary>
    Task<IReadOnlyList<string>> GrantedKeysAsync(long userId, CancellationToken cancellationToken);

    /// <summary>Stages a new grant.</summary>
    void Add(UserPermissionGrant grant);
}

/// <summary>The Access module's unit of work (implemented by its persistence context in Infrastructure).</summary>
public interface IAccessUnitOfWork : IUnitOfWork;
