using Starter.Application.Results;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Application.Content;

/// <summary>Reads one page's content from the store (one owner of the read for both endpoints).</summary>
public sealed class PortalContentReader(IPortalContentStore store)
{
    /// <summary>The current content of <paramref name="page"/>, read-only.</summary>
    public async Task<PortalContentView> ReadAsync(PortalPage page, CancellationToken cancellationToken)
    {
        var header = await store.FindPageAsync(page, cancellationToken);
        var sections = await store.ListSectionsAsync(page, forUpdate: false, cancellationToken);
        return PortalContentView.Of(page, header, sections);
    }
}

/// <summary>
/// <c>GET /api/portal/content/{page}</c> (anonymous): the page's content for the public portal, served from the
/// per-node cache (<see cref="PortalLimits.ContentCacheTtl"/>).
/// </summary>
public sealed class GetPublishedPortalContent(PortalContentReader reader, IPortalContentCache cache)
{
    /// <summary>The content, or <see cref="PortalErrorCodes.PageNotFound"/> (404) for an unknown page key.</summary>
    public async Task<Outcome<PortalContentView>> ExecuteAsync(string? pageKey, CancellationToken cancellationToken)
    {
        if (PortalPages.Find(pageKey) is not { } page)
        {
            return Refusal.NotFound(PortalErrorCodes.PageNotFound);
        }

        if (cache.Find(page) is { } cached)
        {
            return cached;
        }

        var content = await reader.ReadAsync(page, cancellationToken);
        cache.Store(content);
        return content;
    }
}

/// <summary><c>GET /api/admin/portal/content/{page}</c>: the page's current content for the editor, never cached.</summary>
public sealed class GetPortalContent(PortalContentReader reader)
{
    /// <summary>The content, or <see cref="PortalErrorCodes.PageNotFound"/> (404) for an unknown page key.</summary>
    public async Task<Outcome<PortalContentView>> ExecuteAsync(string? pageKey, CancellationToken cancellationToken) =>
        PortalPages.Find(pageKey) is { } page
            ? await reader.ReadAsync(page, cancellationToken)
            : Refusal.NotFound(PortalErrorCodes.PageNotFound);
}
