using Starter.Domain.Auditing;

namespace Starter.Modules.ServiceRequests.Domain;

/// <summary>
/// The sample's own fields of one record (table <c>servicerequests.ServiceRequests</c>), keyed by the Records kernel's
/// record id. The status, owner, number and dates live in the Records module; this row holds only what the wizard
/// collects. Every field is optional while the record is a draft: each wizard step writes its own fields, and the
/// submit validator decides whether the whole is complete.
/// </summary>
public sealed class ServiceRequest
{
    private ServiceRequest()
    {
    }

    /// <summary>The Records kernel's record id (by value only: no foreign key across schemas).</summary>
    public long RecordId { get; private set; }

    /// <summary>The request category (a <c>ServiceRequestCategoryIds</c> lookup id), or null until step 1 is saved.</summary>
    public int? CategoryId { get; private set; }

    /// <summary>The title, or null until step 1 is saved.</summary>
    public string? Title { get; private set; }

    /// <summary>The description, or null until step 2 is saved.</summary>
    [PersonalData(PersonalDataKind.FreeText)]
    public string? Description { get; private set; }

    /// <summary>The contact phone (<see cref="ServiceRequestsLimits.ContactPhonePattern"/>), or null until step 2 is saved.</summary>
    [PersonalData(PersonalDataKind.Phone)]
    public string? ContactPhone { get; private set; }

    /// <summary>The preferred date (optional, never before the day it was saved).</summary>
    public DateOnly? PreferredDate { get; private set; }

    /// <summary>The empty row of a new draft record.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The record id is below 1.</exception>
    public static ServiceRequest StartFor(long recordId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordId, 1);
        return new ServiceRequest { RecordId = recordId };
    }

    /// <summary>Writes the fields of wizard step 1 (already validated by the caller).</summary>
    public void ApplyRequestStep(RequestStepFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        CategoryId = fields.CategoryId;
        Title = fields.Title;
    }

    /// <summary>Writes the fields of wizard step 2 (already validated by the caller).</summary>
    public void ApplyDetailsStep(DetailsStepFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Description = fields.Description;
        ContactPhone = fields.ContactPhone;
        PreferredDate = fields.PreferredDate;
    }
}

/// <summary>The validated fields of wizard step 1.</summary>
/// <param name="CategoryId">The active category's lookup id.</param>
/// <param name="Title">The title.</param>
public sealed record RequestStepFields(int CategoryId, string Title);

/// <summary>The validated fields of wizard step 2.</summary>
/// <param name="Description">The description.</param>
/// <param name="ContactPhone">The contact phone.</param>
/// <param name="PreferredDate">The preferred date, or null.</param>
public sealed record DetailsStepFields(string Description, string ContactPhone, DateOnly? PreferredDate);
