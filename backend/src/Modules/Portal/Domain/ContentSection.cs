namespace Starter.Modules.Portal.Domain;

/// <summary>
/// One content page's header row: when an administrator last changed it (null while it still holds the seeded text).
/// Seeded by migration with one row per <see cref="PortalPage"/>.
/// </summary>
public sealed class ContentPage
{
    private ContentPage()
    {
    }

    /// <summary>The page (the primary key).</summary>
    public PortalPage Page { get; private set; }

    /// <summary>When an administrator last changed the page's sections, or null for the seeded text.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>The header of <paramref name="page"/> before any edit (the seed and tests).</summary>
    public static ContentPage Seeded(PortalPage page) => new() { Page = page };

    /// <summary>Records that the page's sections changed at <paramref name="at"/>.</summary>
    public void Touch(DateTimeOffset at) => UpdatedAt = at;
}

/// <summary>
/// One bilingual section of a content page: a title and a plain-text body (paragraphs separated by a blank line) in
/// Arabic and English, and its place in the page. Changed only through <see cref="Edit"/>, so the audit trail records
/// a modification only when a value really changed.
/// </summary>
public sealed class ContentSection
{
    private ContentSection()
    {
    }

    /// <summary>The internal id (the wire carries its PublicId).</summary>
    public long Id { get; private set; }

    /// <summary>The page the section belongs to (a section never moves to another page).</summary>
    public PortalPage Page { get; private set; }

    /// <summary>The Arabic title.</summary>
    public string TitleAr { get; private set; } = string.Empty;

    /// <summary>The English title.</summary>
    public string TitleEn { get; private set; } = string.Empty;

    /// <summary>The Arabic body.</summary>
    public string BodyAr { get; private set; } = string.Empty;

    /// <summary>The English body.</summary>
    public string BodyEn { get; private set; } = string.Empty;

    /// <summary>The display order within the page (ascending; ties fall back to <see cref="Id"/>).</summary>
    public int SortOrder { get; private set; }

    /// <summary>The current texts and order.</summary>
    public SectionText Text => new(TitleAr, TitleEn, BodyAr, BodyEn, SortOrder);

    /// <summary>A new section of <paramref name="page"/>.</summary>
    public static ContentSection Create(PortalPage page, SectionText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var section = new ContentSection { Page = page };
        section.Edit(text);
        return section;
    }

    /// <summary>Replaces the texts and the order; true when any value changed.</summary>
    public bool Edit(SectionText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text == Text)
        {
            return false;
        }

        TitleAr = text.TitleAr;
        TitleEn = text.TitleEn;
        BodyAr = text.BodyAr;
        BodyEn = text.BodyEn;
        SortOrder = text.SortOrder;
        return true;
    }
}

/// <summary>The editable values of a <see cref="ContentSection"/> (validated and trimmed by the caller).</summary>
/// <param name="TitleAr">The Arabic title.</param>
/// <param name="TitleEn">The English title.</param>
/// <param name="BodyAr">The Arabic body.</param>
/// <param name="BodyEn">The English body.</param>
/// <param name="SortOrder">The display order.</param>
public sealed record SectionText(string TitleAr, string TitleEn, string BodyAr, string BodyEn, int SortOrder);
