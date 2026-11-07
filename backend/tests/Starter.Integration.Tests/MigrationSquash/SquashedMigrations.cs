using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Starter.Integration.Tests.MigrationSquash;

/// <summary>
/// The migration squash policy (ADR-010) as data, and the reflection that finds every DbContext and its migrations in
/// the assemblies the host ships (every <c>Starter.*</c> assembly next to the tests, test assemblies excluded).
/// </summary>
public static class SquashedMigrations
{
    /// <summary>The class name of the one migration each context keeps after the squash.</summary>
    public const string InitialCreateName = "InitialCreate";

    /// <summary>
    /// The id of the last migration that shipped in the v1.0 release, or null before v1.0. Null means every context
    /// has exactly its <see cref="InitialCreateName"/> migration. Set once when v1.0 is tagged: later migrations
    /// (incremental, from <c>/new-lookup</c> or an upstream release) must then sort after it; the chain is never
    /// re-squashed.
    /// </summary>
    public static string? ReleasedThroughMigrationId => null;

    private const string ShippedAssemblyPattern = "Starter.*.dll";
    private const string TestAssemblySuffix = ".Tests";

    /// <summary>Every concrete DbContext in the shipped <c>Starter.*</c> assemblies, by full name.</summary>
    public static IReadOnlyList<Type> DbContexts() =>
        [.. ShippedAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(DbContext).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    /// <summary>The migrations of <paramref name="context"/> (<see cref="DbContextAttribute"/> + <see cref="MigrationAttribute"/>), ordered by id.</summary>
    public static IReadOnlyList<MigrationInfo> MigrationsOf(Type context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. ShippedAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(Migration).IsAssignableFrom(type) && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == context)
            .Select(type => new MigrationInfo(Id: type.GetCustomAttribute<MigrationAttribute>()?.Id ?? string.Empty, Type: type))
            .OrderBy(migration => migration.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// What is wrong with <paramref name="migrations"/> under the squash policy, or null when nothing is: exactly one
    /// <see cref="InitialCreateName"/> class with an id, first in the chain, and any other migration only after
    /// <paramref name="releasedThrough"/> (none at all while it is null).
    /// </summary>
    public static string? Violation(IReadOnlyList<MigrationInfo> migrations, string? releasedThrough)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        if (migrations.Any(migration => migration.Id.Length == 0))
        {
            return "a migration class has no [Migration] id";
        }

        var initial = migrations.Where(migration => migration.Type.Name == InitialCreateName).ToList();
        if (initial.Count != 1)
        {
            return $"{initial.Count} {InitialCreateName} migrations (expected exactly one)";
        }

        if (migrations[0] != initial[0])
        {
            return $"{migrations[0].Id} precedes {InitialCreateName}";
        }

        var extra = migrations.Skip(1)
            .Where(migration => releasedThrough is null || string.CompareOrdinal(migration.Id, releasedThrough) <= 0)
            .Select(migration => migration.Id)
            .ToList();
        return extra.Count == 0 ? null : $"not folded into {InitialCreateName}: {string.Join(", ", extra)}";
    }

    private static IEnumerable<Assembly> ShippedAssemblies() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, ShippedAssemblyPattern)
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(TestAssemblySuffix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path)));
}

/// <summary>One migration class and its id.</summary>
/// <param name="Id">The <see cref="MigrationAttribute"/> id (timestamp and name).</param>
/// <param name="Type">The migration class.</param>
public sealed record MigrationInfo(string Id, Type Type);
