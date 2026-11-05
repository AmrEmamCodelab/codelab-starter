using Starter.Application.Results;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>
/// The one owner of "may this key be granted": it must be in the live code catalog (an unknown key and a retired key
/// are both refused with <see cref="AccessErrorCodes.UnknownPermission"/>) and have its synchronized database row.
/// </summary>
public sealed class GrantablePermissions(PermissionCatalog catalog, IPermissionRepository permissions)
{
    /// <summary>The tracked catalog rows of <paramref name="keys"/> by key, or a 400 refusal.</summary>
    public async Task<Outcome<IReadOnlyDictionary<string, PermissionEntry>>> LoadAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var wanted = keys.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Any(key => !catalog.Contains(key)))
        {
            return Refusal.Invalid(AccessErrorCodes.UnknownPermission);
        }

        var rows = wanted.Count == 0 ? [] : await permissions.FindByKeysAsync(wanted, cancellationToken);
        var current = rows.Where(row => !row.IsObsolete).ToDictionary(row => row.Key, StringComparer.Ordinal);
        if (current.Count != wanted.Count)
        {
            return Refusal.Invalid(AccessErrorCodes.UnknownPermission);
        }

        return Outcome.Success<IReadOnlyDictionary<string, PermissionEntry>>(current);
    }
}
