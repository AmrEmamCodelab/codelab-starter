using System.Globalization;
using Starter.Application.Records;
using Starter.Modules.ServiceRequests.Application.Access;
using Starter.Modules.ServiceRequests.Application.Validation;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Application.Drafts;

/// <summary>
/// The whole-request check the Records kernel runs before a submit or a resubmit of a service request: every field of
/// every step (<see cref="ServiceRequestValidationScope.Submit"/>, today from the app calendar) over what was saved, so
/// a crafted submit that skipped a step fails on that step's fields; plus the stored category must still be an active
/// value (<see cref="ServiceRequestValidationCodes.CategoryUnknown"/>). A record without a row fails on every required
/// field. Field errors are ordered by field name.
/// </summary>
public sealed class ServiceRequestSubmitValidator(
    IServiceRequestStore store,
    ServiceRequestCategories categories,
    IServiceRequestCalendar calendar) : IRecordSubmitValidator
{
    /// <inheritdoc />
    public string RecordType => ServiceRequestRecord.RecordType;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FieldError>> ValidateForSubmitAsync(long recordId, CancellationToken cancellationToken)
    {
        var row = await store.FindAsync(recordId, cancellationToken);
        var errors = ServiceRequestRules.Validate(InputOf(row), ServiceRequestValidationScope.Submit, calendar.Today()).ToDictionary();
        var fieldErrors = errors
            .SelectMany(entry => entry.Value.Select(code => new FieldError(entry.Key, code)))
            .ToList();
        if (row?.CategoryId is { } categoryId && await categories.FindActiveAsync(categoryId, cancellationToken) is null)
        {
            fieldErrors.Add(new FieldError(ServiceRequestFields.CategoryId, ServiceRequestValidationCodes.CategoryUnknown));
        }

        return [.. fieldErrors.OrderBy(error => error.Field, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The stored row as validation input. The category only has to be present here (its value is checked against the
    /// active list separately), so its id stands in for the token.
    /// </summary>
    public static ServiceRequestInput InputOf(ServiceRequest? row) => new(
        CategoryId: row?.CategoryId?.ToString(CultureInfo.InvariantCulture),
        Title: row?.Title,
        Description: row?.Description,
        ContactPhone: row?.ContactPhone,
        PreferredDate: row?.PreferredDate is { } date ? ServiceRequestRules.FormatDate(date) : null);
}
