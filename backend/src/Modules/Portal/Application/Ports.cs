using Starter.Domain.Localization;
using Starter.Modules.Portal.Application.Content;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Application;

/// <summary>The contact messages (implemented by the module's EF Core context in Infrastructure).</summary>
public interface IContactMessageStore
{
    /// <summary>Stages a new message (written by <see cref="SaveChangesAsync"/>).</summary>
    void Add(ContactMessage message);

    /// <summary>Writes every staged change.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>The content pages and their sections (implemented by the module's EF Core context in Infrastructure).</summary>
public interface IPortalContentStore
{
    /// <summary>The page's header row, tracked for a change written by <see cref="SaveChangesAsync"/>, or null.</summary>
    Task<ContentPage?> FindPageAsync(PortalPage page, CancellationToken cancellationToken);

    /// <summary>
    /// The page's sections, ordered by sort order then id; tracked for a change when <paramref name="forUpdate"/>,
    /// read-only otherwise.
    /// </summary>
    Task<IReadOnlyList<ContentSection>> ListSectionsAsync(PortalPage page, bool forUpdate, CancellationToken cancellationToken);

    /// <summary>Stages a new section.</summary>
    void Add(ContentSection section);

    /// <summary>Stages the removal of a tracked section.</summary>
    void Remove(ContentSection section);

    /// <summary>Writes every staged change.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The per-node cache of each page's public content. An entry lives at most <see cref="PortalLimits.ContentCacheTtl"/>,
/// timed by the injected <see cref="TimeProvider"/>.
/// </summary>
public interface IPortalContentCache
{
    /// <summary>The cached content of <paramref name="page"/>, or null when there is none or it expired.</summary>
    PortalContentView? Find(PortalPage page);

    /// <summary>Caches <paramref name="content"/> for <see cref="PortalLimits.ContentCacheTtl"/>.</summary>
    void Store(PortalContentView content);

    /// <summary>Drops the cached content of <paramref name="page"/> (after it changed).</summary>
    void Invalidate(PortalPage page);
}

/// <summary>
/// The contact topics a message may be filed under: the active values of the <c>contact-topics</c> lookup
/// (<c>Starter.Domain.Lookups.LookupTypes.ContactTopics</c>, ids from <c>ContactTopicIds</c>).
/// </summary>
public interface IContactTopics
{
    /// <summary>The active topic with <paramref name="topicId"/>, or null when it is unknown or retired.</summary>
    Task<ContactTopic?> FindActiveAsync(int topicId, CancellationToken cancellationToken);
}

/// <summary>One contact topic with its names.</summary>
/// <param name="Id">The lookup id.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
public sealed record ContactTopic(int Id, string NameAr, string NameEn)
{
    /// <summary>The name in <paramref name="language"/>.</summary>
    public string NameIn(AppLanguage language) => language == AppLanguage.English ? NameEn : NameAr;
}

/// <summary>Where a new contact message is announced (from the <c>Portal:Contact</c> options, validated on start).</summary>
/// <param name="EmailAddress">The inbox address.</param>
/// <param name="Language">The language of the announcement e-mail.</param>
public sealed record ContactInbox(string EmailAddress, AppLanguage Language);
