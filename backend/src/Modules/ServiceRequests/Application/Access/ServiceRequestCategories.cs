using Codelab.Security.PublicIds;
using Starter.Application.Lookups;
using Starter.Domain.Lookups;

namespace Starter.Modules.ServiceRequests.Application.Access;

/// <summary>
/// The request categories (lookup <see cref="LookupTypes.ServiceRequestCategories"/>, values
/// <see cref="ServiceRequestCategoryIds"/>) through the kernel port <see cref="ILookupValues"/>: reading a wire token,
/// finding an active value and writing a value's token.
/// </summary>
public sealed class ServiceRequestCategories(ILookupValues lookups, IPublicIdCodec codec)
{
    /// <summary>The active category the token names, or null when the token is unreadable or the value unknown or retired.</summary>
    public async Task<ActiveLookupValue?> FindActiveByTokenAsync(string? token, CancellationToken cancellationToken)
    {
        if (!codec.TryUnprotect(ServiceRequestCategoryTokens.DomainLabel, token, out var id) || id > int.MaxValue)
        {
            return null;
        }

        return await FindActiveAsync((int)id, cancellationToken);
    }

    /// <summary>The active category <paramref name="categoryId"/>, or null when unknown or retired.</summary>
    public Task<ActiveLookupValue?> FindActiveAsync(int categoryId, CancellationToken cancellationToken) =>
        lookups.FindActiveAsync(LookupTypes.ServiceRequestCategories, categoryId, cancellationToken);

    /// <summary>The wire token of a stored category id.</summary>
    public string TokenOf(int categoryId) => codec.Protect(ServiceRequestCategoryTokens.DomainLabel, categoryId);
}

