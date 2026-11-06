using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Results;
using Starter.Infrastructure.Results;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Application.Contact;
using Starter.Modules.Portal.Application.Content;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Api;

/// <summary>The contact receipt: <c>{referenceNumber}</c>.</summary>
/// <param name="ReferenceNumber">The reference number, <c>CU-YYYY-NNN</c>.</param>
public sealed record ContactReceiptResponse(string ReferenceNumber)
{
    /// <summary>The wire shape of <paramref name="receipt"/>.</summary>
    public static ContactReceiptResponse From(ContactReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new ContactReceiptResponse(receipt.ReferenceNumber);
    }
}

/// <summary>One page's content: <c>{page, updatedAt, sections}</c>.</summary>
/// <param name="Page">The page key (<c>faq</c>, <c>service-guide</c>, <c>privacy</c>, <c>terms</c>).</param>
/// <param name="UpdatedAt">When an administrator last changed it (ISO 8601, UTC), or null for the seeded text.</param>
/// <param name="Sections">The sections, ordered by sort order.</param>
public sealed record PortalContentResponse(string Page, DateTimeOffset? UpdatedAt, IReadOnlyList<PortalSectionResponse> Sections)
{
    /// <summary>The wire shape of <paramref name="content"/>.</summary>
    public static PortalContentResponse From(PortalContentView content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new PortalContentResponse(
            PortalPages.KeyOf(content.Page),
            content.UpdatedAt?.ToUniversalTime(),
            content.Sections.Select(PortalSectionResponse.From).ToList());
    }
}

/// <summary>One bilingual section on the wire (plain text; body paragraphs separated by a blank line).</summary>
/// <param name="Id">The section's PublicId.</param>
/// <param name="TitleAr">The Arabic title.</param>
/// <param name="TitleEn">The English title.</param>
/// <param name="BodyAr">The Arabic body.</param>
/// <param name="BodyEn">The English body.</param>
/// <param name="SortOrder">The display order.</param>
public sealed record PortalSectionResponse(
    PublicId<PortalSectionIds> Id,
    string TitleAr,
    string TitleEn,
    string BodyAr,
    string BodyEn,
    int SortOrder)
{
    /// <summary>The wire shape of <paramref name="section"/>.</summary>
    public static PortalSectionResponse From(SectionView section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return new PortalSectionResponse(
            PublicId<PortalSectionIds>.From(section.Id),
            section.TitleAr,
            section.TitleEn,
            section.BodyAr,
            section.BodyEn,
            section.SortOrder);
    }
}

/// <summary>The 201 mapping the kernel <c>OutcomeResults</c> does not have yet (one owner in this module).</summary>
public static class PortalResults
{
    /// <summary>201 with the mapped body, or the refusal's problem+json.</summary>
    public static ActionResult<TResponse> ToCreatedResult<T, TResponse>(this Outcome<T> outcome, Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(map);
        return outcome.IsSuccess
            ? new ObjectResult(map(outcome.Value)) { StatusCode = StatusCodes.Status201Created }
            : outcome.Refusal.ToActionResult();
    }
}
