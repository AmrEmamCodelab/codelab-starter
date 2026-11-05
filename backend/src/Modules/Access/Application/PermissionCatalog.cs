using Starter.Application.Authorization;

namespace Starter.Modules.Access.Application;

/// <summary>
/// The whole permission catalog: the union of every registered <see cref="IPermissionCatalogSource"/>, sorted by key.
/// Built once (singleton) and validated on construction, so a duplicate key, a key that breaks
/// <see cref="PermissionKeyFormat"/> or a missing description stops the host at start instead of surfacing later.
/// </summary>
public sealed class PermissionCatalog
{
    private const string ListSeparator = ", ";

    private readonly HashSet<string> _keys;

    /// <summary>Aggregates and validates <paramref name="sources"/>.</summary>
    /// <exception cref="InvalidOperationException">A key is malformed, declared twice, or has no description (a configuration fault).</exception>
    public PermissionCatalog(IEnumerable<IPermissionCatalogSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var definitions = sources.SelectMany(source => source.Permissions).ToList();
        Validate(definitions);
        Definitions = [.. definitions.OrderBy(definition => definition.Key, StringComparer.Ordinal)];
        _keys = new HashSet<string>(definitions.Select(definition => definition.Key), StringComparer.Ordinal);
    }

    /// <summary>Every permission, sorted by key (ordinal).</summary>
    public IReadOnlyList<PermissionDefinition> Definitions { get; }

    /// <summary>Every key.</summary>
    public IReadOnlySet<string> Keys => _keys;

    /// <summary>True when <paramref name="key"/> is in the catalog (ordinal).</summary>
    public bool Contains(string key) => _keys.Contains(key);

    private static void Validate(List<PermissionDefinition> definitions)
    {
        var malformed = definitions.Where(definition => !PermissionKeyFormat.IsValid(definition.Key)).Select(definition => definition.Key).ToList();
        if (malformed.Count > 0)
        {
            throw new InvalidOperationException($"Permission keys break the key format: {string.Join(ListSeparator, malformed)}.");
        }

        var duplicates = definitions.GroupBy(definition => definition.Key, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Permission keys are declared more than once: {string.Join(ListSeparator, duplicates)}.");
        }

        var undescribed = definitions.Where(definition => string.IsNullOrWhiteSpace(definition.Description)).Select(definition => definition.Key).ToList();
        if (undescribed.Count > 0)
        {
            throw new InvalidOperationException($"Permission keys have no description: {string.Join(ListSeparator, undescribed)}.");
        }
    }
}
