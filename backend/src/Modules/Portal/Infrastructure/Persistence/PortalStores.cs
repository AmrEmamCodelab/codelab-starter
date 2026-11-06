using Microsoft.EntityFrameworkCore;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Infrastructure.Persistence;

/// <summary>The <see cref="IContactMessageStore"/> over <see cref="PortalDbContext"/>.</summary>
public sealed class EfContactMessageStore(PortalDbContext context) : IContactMessageStore
{
    /// <inheritdoc />
    public void Add(ContactMessage message) => context.ContactMessages.Add(message);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}

/// <summary>The <see cref="IPortalContentStore"/> over <see cref="PortalDbContext"/>.</summary>
public sealed class EfPortalContentStore(PortalDbContext context) : IPortalContentStore
{
    /// <inheritdoc />
    public Task<ContentPage?> FindPageAsync(PortalPage page, CancellationToken cancellationToken) =>
        context.ContentPages.FirstOrDefaultAsync(header => header.Page == page, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContentSection>> ListSectionsAsync(PortalPage page, bool forUpdate, CancellationToken cancellationToken)
    {
        var sections = context.ContentSections.Where(section => section.Page == page);
        if (!forUpdate)
        {
            sections = sections.AsNoTracking();
        }

        return await sections.OrderBy(section => section.SortOrder).ThenBy(section => section.Id).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(ContentSection section) => context.ContentSections.Add(section);

    /// <inheritdoc />
    public void Remove(ContentSection section) => context.ContentSections.Remove(section);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
