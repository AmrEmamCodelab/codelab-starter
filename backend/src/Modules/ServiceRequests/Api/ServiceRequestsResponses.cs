using Codelab.Security.PublicIds;
using Starter.Modules.ServiceRequests.Application.Drafts;

namespace Starter.Modules.ServiceRequests.Api;

/// <summary>The answer of the create endpoint.</summary>
/// <param name="Id">The new record's PublicId.</param>
public sealed record CreatedServiceRequestResponse(PublicId<ServiceRequestRecordIds> Id)
{
    /// <summary>Maps the new record id.</summary>
    public static CreatedServiceRequestResponse From(long recordId) => new(PublicId<ServiceRequestRecordIds>.From(recordId));
}

/// <summary>The answer of the submit endpoint.</summary>
/// <param name="Number">The reference number, for example <c>SR-2026-001</c>.</param>
public sealed record SubmittedServiceRequestResponse(string Number)
{
    /// <summary>Maps the allocated number.</summary>
    public static SubmittedServiceRequestResponse From(string number) => new(number);
}

/// <summary>A draft as the wizard resumes it (every field null until its step is saved).</summary>
/// <param name="Id">The record's PublicId.</param>
/// <param name="CategoryId">The category value's PublicId token (family <c>lookup-items</c>), or null.</param>
/// <param name="Title">The title, or null.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="ContactPhone">The contact phone, or null.</param>
/// <param name="PreferredDate">The preferred date (<c>YYYY-MM-DD</c>), or null.</param>
public sealed record ServiceRequestDraftResponse(
    PublicId<ServiceRequestRecordIds> Id,
    string? CategoryId,
    string? Title,
    string? Description,
    string? ContactPhone,
    string? PreferredDate)
{
    /// <summary>Maps the draft view.</summary>
    public static ServiceRequestDraftResponse From(ServiceRequestDraftView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new(
            PublicId<ServiceRequestRecordIds>.From(view.RecordId),
            view.CategoryToken,
            view.Title,
            view.Description,
            view.ContactPhone,
            view.PreferredDate);
    }
}

/// <summary>
/// The fields of a service request for its detail cards. Every field but <see cref="Number"/> and
/// <see cref="PreferredDate"/> is set once submitted; the category names are null when the value was retired.
/// </summary>
/// <param name="Id">The record's PublicId.</param>
/// <param name="Number">The reference number, or null before the submit.</param>
/// <param name="CategoryId">The category value's PublicId token.</param>
/// <param name="CategoryNameAr">The category's Arabic name.</param>
/// <param name="CategoryNameEn">The category's English name.</param>
/// <param name="Title">The title.</param>
/// <param name="Description">The description.</param>
/// <param name="ContactPhone">The contact phone.</param>
/// <param name="PreferredDate">The preferred date (<c>YYYY-MM-DD</c>), or null.</param>
public sealed record ServiceRequestDetailsResponse(
    PublicId<ServiceRequestRecordIds> Id,
    string? Number,
    string? CategoryId,
    string? CategoryNameAr,
    string? CategoryNameEn,
    string? Title,
    string? Description,
    string? ContactPhone,
    string? PreferredDate)
{
    /// <summary>Maps the details view.</summary>
    public static ServiceRequestDetailsResponse From(ServiceRequestDetailsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var draft = view.Draft;
        return new(
            PublicId<ServiceRequestRecordIds>.From(draft.RecordId),
            view.Number,
            draft.CategoryToken,
            view.CategoryNameAr,
            view.CategoryNameEn,
            draft.Title,
            draft.Description,
            draft.ContactPhone,
            draft.PreferredDate);
    }
}
