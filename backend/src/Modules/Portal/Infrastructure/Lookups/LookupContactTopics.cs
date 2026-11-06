using Starter.Application.Lookups;
using Starter.Domain.Lookups;
using Starter.Modules.Portal.Application;

namespace Starter.Modules.Portal.Infrastructure.Lookups;

/// <summary>
/// The <see cref="IContactTopics"/> over the <see cref="LookupTypes.ContactTopics"/> lookup, read through the kernel
/// <see cref="ILookupValues"/> port (Lookups module), so the Portal module never reads the Lookups tables or references
/// the Lookups project.
/// </summary>
public sealed class LookupContactTopics(ILookupValues lookups) : IContactTopics
{
    /// <inheritdoc />
    public async Task<ContactTopic?> FindActiveAsync(int topicId, CancellationToken cancellationToken) =>
        await lookups.FindActiveAsync(LookupTypes.ContactTopics, topicId, cancellationToken) is { } value
            ? new ContactTopic(value.Id, value.NameAr, value.NameEn)
            : null;
}
