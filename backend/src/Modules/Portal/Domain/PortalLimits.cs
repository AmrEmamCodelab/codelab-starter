namespace Starter.Modules.Portal.Domain;

/// <summary>
/// The Portal module's sizes: one owner for the columns, the validators and the cache. The contact limits mirror the
/// contact form's <c>ContactLimits</c> (frontend <c>contact-form.ts</c>), so the form and the server agree.
/// </summary>
public static class PortalLimits
{
    /// <summary>The longest sender name, after trimming.</summary>
    public const int FullNameMaxLength = 100;

    /// <summary>The longest e-mail address, after trimming (the RFC 5321 path limit).</summary>
    public const int EmailMaxLength = 254;

    /// <summary>The shortest message, after trimming.</summary>
    public const int MessageMinLength = 10;

    /// <summary>The longest message, after trimming.</summary>
    public const int MessageMaxLength = 2000;

    /// <summary>The longest reference number column (<c>CU-2026-001</c> grows past three digits, never past this).</summary>
    public const int ReferenceNumberMaxLength = 32;

    /// <summary>The longest section title (Arabic or English).</summary>
    public const int SectionTitleMaxLength = 200;

    /// <summary>The longest section body (Arabic or English, plain text, paragraphs separated by a blank line).</summary>
    public const int SectionBodyMaxLength = 8000;

    /// <summary>The most sections one page may hold.</summary>
    public const int MaxSectionsPerPage = 100;

    /// <summary>The smallest sort order.</summary>
    public const int SortOrderMin = 0;

    /// <summary>The largest sort order.</summary>
    public const int SortOrderMax = 100_000;

    /// <summary>How many seconds a node serves a page's public content from its cache (<see cref="ContentCacheTtl"/>).</summary>
    public const int ContentCacheSeconds = 60;

    /// <summary>
    /// How long a node serves a page's public content from its cache. A replace drops the entry on the node that made
    /// it at once; the other nodes see the change within this time.
    /// </summary>
    public static readonly TimeSpan ContentCacheTtl = TimeSpan.FromSeconds(ContentCacheSeconds);
}
