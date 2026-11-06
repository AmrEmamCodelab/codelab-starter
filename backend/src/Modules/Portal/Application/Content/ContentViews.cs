using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Application.Content;

/// <summary>One content page as the reads and the replace answer it: immutable, safe to cache.</summary>
/// <param name="Page">The page.</param>
/// <param name="UpdatedAt">When an administrator last changed it, or null for the seeded text.</param>
/// <param name="Sections">The sections, ordered by sort order, then id.</param>
public sealed record PortalContentView(PortalPage Page, DateTimeOffset? UpdatedAt, IReadOnlyList<SectionView> Sections)
{
    /// <summary>The view of <paramref name="page"/> with <paramref name="sections"/> (ordered here).</summary>
    public static PortalContentView Of(PortalPage page, ContentPage? header, IEnumerable<ContentSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var ordered = sections
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Id)
            .Select(SectionView.Of)
            .ToList();
        return new PortalContentView(page, header?.UpdatedAt, ordered);
    }
}

/// <summary>One section in both languages.</summary>
/// <param name="Id">The internal id (the wire carries its PublicId).</param>
/// <param name="TitleAr">The Arabic title.</param>
/// <param name="TitleEn">The English title.</param>
/// <param name="BodyAr">The Arabic body (plain text, paragraphs separated by a blank line).</param>
/// <param name="BodyEn">The English body.</param>
/// <param name="SortOrder">The display order.</param>
public sealed record SectionView(long Id, string TitleAr, string TitleEn, string BodyAr, string BodyEn, int SortOrder)
{
    /// <summary>The view of <paramref name="section"/>.</summary>
    public static SectionView Of(ContentSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return new SectionView(section.Id, section.TitleAr, section.TitleEn, section.BodyAr, section.BodyEn, section.SortOrder);
    }
}
