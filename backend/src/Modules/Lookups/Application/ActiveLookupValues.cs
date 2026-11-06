using Starter.Application.Lookups;

namespace Starter.Modules.Lookups.Application;

/// <summary>
/// The kernel <see cref="ILookupValues"/> over the same cached active lists as <c>GET /api/lookups/{type}</c>
/// (<see cref="GetActiveLookupValues"/>): one owner of what "active" means and of the cache.
/// </summary>
public sealed class ActiveLookupValues(GetActiveLookupValues activeValues) : ILookupValues
{
    /// <inheritdoc />
    public async Task<ActiveLookupValue?> FindActiveAsync(string typeKey, int id, CancellationToken cancellationToken)
    {
        var outcome = await activeValues.ExecuteAsync(typeKey, cancellationToken);
        if (!outcome.IsSuccess)
        {
            return null;
        }

        return outcome.Value.FirstOrDefault(value => value.Id == id) is { } found
            ? new ActiveLookupValue(found.Id, found.Code, found.NameAr, found.NameEn)
            : null;
    }
}
