using Microsoft.EntityFrameworkCore;

namespace Starter.Infrastructure.Persistence;

/// <summary>One module database (a DbContext and its migrations), as the startup runner sees it.</summary>
public interface IModuleDatabase
{
    /// <summary>The DbContext type name, for logs.</summary>
    string Name { get; }

    /// <summary>Applies every pending migration of the module.</summary>
    Task MigrateAsync(CancellationToken cancellationToken);
}

/// <summary>The <see cref="IModuleDatabase"/> of <typeparamref name="TContext"/>.</summary>
public sealed class ModuleDatabase<TContext>(TContext context) : IModuleDatabase
    where TContext : ModuleDbContext
{
    /// <inheritdoc />
    public string Name => typeof(TContext).Name;

    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken cancellationToken) => context.Database.MigrateAsync(cancellationToken);
}
