using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.ServiceRequests.Application.Access;
using Starter.Modules.ServiceRequests.Application.Validation;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Application.Drafts;

/// <summary>The wizard step a draft save validates and writes (<c>step</c> on the wire; stored nowhere).</summary>
public enum ServiceRequestDraftStep
{
    /// <summary>Step 1, "request": category and title.</summary>
    Request = 1,

    /// <summary>Step 2, "details": description, contact phone and preferred date.</summary>
    Details = 2,

    /// <summary>Step 3, "attachments and review": no field of its own (its check is the submit's attachment interlock).</summary>
    Attachments = 3,
}

/// <summary>
/// The body of <c>PUT api/service-requests/{id}/draft</c>: the draft fields as the wizard holds them, plus the step
/// being saved. Only that step's fields are validated and written; the others are ignored (they keep what their own
/// step saved), so a stored draft never holds a field its step did not accept.
/// </summary>
/// <param name="CategoryId">The category value's PublicId token (family <c>lookup-items</c>).</param>
/// <param name="Title">The title.</param>
/// <param name="Description">The description.</param>
/// <param name="ContactPhone">The contact phone.</param>
/// <param name="PreferredDate">The preferred date, <c>YYYY-MM-DD</c>.</param>
/// <param name="Step">The wizard step (1, 2 or 3).</param>
public sealed record SaveServiceRequestDraftCommand(
    string? CategoryId,
    string? Title,
    string? Description,
    string? ContactPhone,
    string? PreferredDate,
    int? Step)
{
    /// <summary>The wizard fields of the body.</summary>
    public ServiceRequestInput Input => new(CategoryId, Title, Description, ContactPhone, PreferredDate);

    /// <summary>The step, or null when the body names none or an undefined one.</summary>
    public ServiceRequestDraftStep? DraftStep =>
        Step is { } step && Enum.IsDefined((ServiceRequestDraftStep)step) ? (ServiceRequestDraftStep)step : null;
}

/// <summary>
/// Validates a draft save: the step must be 1, 2 or 3, and the fields of that step follow
/// <see cref="ServiceRequestRules"/> (today from the app calendar). Fields of the other steps are not checked.
/// </summary>
public sealed class SaveServiceRequestDraftValidator(IServiceRequestCalendar calendar) : IValidator<SaveServiceRequestDraftCommand>
{
    /// <summary>The wire name of the step field.</summary>
    public static readonly string StepField = FieldNames.Of(nameof(SaveServiceRequestDraftCommand.Step));

    /// <inheritdoc />
    public ValidationErrors Validate(SaveServiceRequestDraftCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.DraftStep switch
        {
            null => new ValidationErrors().Add(StepField, ServiceRequestValidationCodes.StepInvalid),
            ServiceRequestDraftStep.Request => ServiceRequestRules.Validate(input.Input, ServiceRequestValidationScope.RequestStep, calendar.Today()),
            ServiceRequestDraftStep.Details => ServiceRequestRules.Validate(input.Input, ServiceRequestValidationScope.DetailsStep, calendar.Today()),
            _ => new ValidationErrors(),
        };
    }
}

/// <summary>
/// Saves one wizard step of the caller's own draft (or returned request): 204; 400 with field errors (including
/// <see cref="ServiceRequestValidationCodes.CategoryUnknown"/> for a category that is not an active value); 404
/// <see cref="RecordsErrorCodes.NotFound"/> when the record is missing or not the caller's; 409
/// <see cref="ServiceRequestsErrorCodes.NotEditable"/> once it left Draft and Returned.
/// </summary>
public sealed class SaveServiceRequestDraft(
    IValidator<SaveServiceRequestDraftCommand> validator,
    ServiceRequestAccess access,
    ServiceRequestCategories categories,
    IServiceRequestStore store)
{
    /// <summary>Runs the use case.</summary>
    public async Task<Outcome<Done>> ExecuteAsync(long recordId, SaveServiceRequestDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var record = await access.FindOwnedAsync(recordId, cancellationToken);
        if (record is null)
        {
            return Refusal.NotFound(RecordsErrorCodes.NotFound);
        }

        if (!ServiceRequestStatuses.Editable.Contains(record.Status))
        {
            return Refusal.Conflict(ServiceRequestsErrorCodes.NotEditable);
        }

        var row = await store.FindAsync(recordId, cancellationToken) ?? AddRow(recordId);
        var refusal = await ApplyAsync(row, command, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        await store.SaveChangesAsync(cancellationToken);
        return Done.Value;
    }

    private ServiceRequest AddRow(long recordId)
    {
        var row = ServiceRequest.StartFor(recordId);
        store.Add(row);
        return row;
    }

    private async Task<Refusal?> ApplyAsync(ServiceRequest row, SaveServiceRequestDraftCommand command, CancellationToken cancellationToken)
    {
        switch (command.DraftStep)
        {
            case ServiceRequestDraftStep.Request:
                var category = await categories.FindActiveByTokenAsync(command.CategoryId, cancellationToken);
                if (category is null)
                {
                    return Refusal.Invalid(new ValidationErrors().Add(ServiceRequestFields.CategoryId, ServiceRequestValidationCodes.CategoryUnknown));
                }

                row.ApplyRequestStep(new RequestStepFields(category.Id, command.Title!));
                return null;
            case ServiceRequestDraftStep.Details:
                row.ApplyDetailsStep(new DetailsStepFields(command.Description!, command.ContactPhone!, ParseDate(command.PreferredDate)));
                return null;
            default:
                return null;
        }
    }

    private static DateOnly? ParseDate(string? value) =>
        ServiceRequestRules.TryParseDate(value, out var date) ? date : null;
}
