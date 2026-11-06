using System.Globalization;
using System.Text.RegularExpressions;
using Starter.Application.Validation;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Application.Validation;

/// <summary>
/// The field codes the wizard validation sends (the message of a field error). The client mirrors them by hand
/// (<c>ServiceRequestValidationCodes</c> in <c>service-request-rules.ts</c>) and renders
/// <c>serviceRequests.errors.&lt;CODE&gt;</c>.
/// </summary>
public static class ServiceRequestValidationCodes
{
    /// <summary>No category was chosen.</summary>
    public const string CategoryRequired = "SERVICE_REQUESTS_CATEGORY_REQUIRED";

    /// <summary>The category token is unreadable, unknown or retired (server only: no client can mirror it).</summary>
    public const string CategoryUnknown = "SERVICE_REQUESTS_CATEGORY_UNKNOWN";

    /// <summary>The title is missing.</summary>
    public const string TitleRequired = "SERVICE_REQUESTS_TITLE_REQUIRED";

    /// <summary>The title is shorter than <see cref="ServiceRequestsLimits.TitleMinLength"/>.</summary>
    public const string TitleTooShort = "SERVICE_REQUESTS_TITLE_TOO_SHORT";

    /// <summary>The title is longer than <see cref="ServiceRequestsLimits.TitleMaxLength"/>.</summary>
    public const string TitleTooLong = "SERVICE_REQUESTS_TITLE_TOO_LONG";

    /// <summary>The description is missing.</summary>
    public const string DescriptionRequired = "SERVICE_REQUESTS_DESCRIPTION_REQUIRED";

    /// <summary>The description is shorter than <see cref="ServiceRequestsLimits.DescriptionMinLength"/>.</summary>
    public const string DescriptionTooShort = "SERVICE_REQUESTS_DESCRIPTION_TOO_SHORT";

    /// <summary>The description is longer than <see cref="ServiceRequestsLimits.DescriptionMaxLength"/>.</summary>
    public const string DescriptionTooLong = "SERVICE_REQUESTS_DESCRIPTION_TOO_LONG";

    /// <summary>The contact phone is missing.</summary>
    public const string ContactPhoneRequired = "SERVICE_REQUESTS_CONTACT_PHONE_REQUIRED";

    /// <summary>The contact phone does not match <see cref="ServiceRequestsLimits.ContactPhonePattern"/>.</summary>
    public const string ContactPhoneInvalid = "SERVICE_REQUESTS_CONTACT_PHONE_INVALID";

    /// <summary>The preferred date is not a real <c>YYYY-MM-DD</c> date.</summary>
    public const string PreferredDateInvalid = "SERVICE_REQUESTS_PREFERRED_DATE_INVALID";

    /// <summary>The preferred date is before today (in the app time zone).</summary>
    public const string PreferredDateInPast = "SERVICE_REQUESTS_PREFERRED_DATE_IN_PAST";

    /// <summary>The draft save names no wizard step (1, 2 or 3).</summary>
    public const string StepInvalid = "SERVICE_REQUESTS_STEP_INVALID";
}

/// <summary>The validated fields by their camelCase wire names (the keys of the <c>errors</c> dictionary).</summary>
public static class ServiceRequestFields
{
    /// <summary>The category.</summary>
    public static readonly string CategoryId = FieldNames.Of(nameof(ServiceRequestInput.CategoryId));

    /// <summary>The title.</summary>
    public static readonly string Title = FieldNames.Of(nameof(ServiceRequestInput.Title));

    /// <summary>The description.</summary>
    public static readonly string Description = FieldNames.Of(nameof(ServiceRequestInput.Description));

    /// <summary>The contact phone.</summary>
    public static readonly string ContactPhone = FieldNames.Of(nameof(ServiceRequestInput.ContactPhone));

    /// <summary>The preferred date.</summary>
    public static readonly string PreferredDate = FieldNames.Of(nameof(ServiceRequestInput.PreferredDate));
}

/// <summary>What one validation checks: a wizard step (a draft save) or the whole submit.</summary>
public enum ServiceRequestValidationScope
{
    /// <summary>Step 1, "request": category and title.</summary>
    RequestStep = 1,

    /// <summary>Step 2, "details": description, contact phone and preferred date.</summary>
    DetailsStep = 2,

    /// <summary>The submit: every field (a submit that skipped a step fails on that step's fields).</summary>
    Submit = 3,
}

/// <summary>
/// The wizard fields as text, exactly as the client sends them (every one may be null or absent). The category is the
/// lookup value's PublicId token; the preferred date is <c>YYYY-MM-DD</c>.
/// </summary>
public sealed record ServiceRequestInput(
    string? CategoryId,
    string? Title,
    string? Description,
    string? ContactPhone,
    string? PreferredDate);

/// <summary>
/// The wizard validation rules, hand-mirrored by the client (<c>service-request-rules.ts</c>) and pinned by the
/// parity fixture <c>validation-parity.fixtures.json</c>. Semantics both sides share: a text is missing when null,
/// absent or only white space; lengths count the value as sent; the category is only checked for presence here (the
/// use case checks it names an active category); the preferred date is optional, a real calendar date and not before
/// today; each field reports at most one code, the first rule it breaks (required, format, length, range). Pure.
/// </summary>
public static partial class ServiceRequestRules
{
    private static readonly IReadOnlyList<string> RequestStepFields = [ServiceRequestFields.CategoryId, ServiceRequestFields.Title];

    private static readonly IReadOnlyList<string> DetailsStepFields =
        [ServiceRequestFields.Description, ServiceRequestFields.ContactPhone, ServiceRequestFields.PreferredDate];

    /// <summary>Every field error of <paramref name="input"/> in <paramref name="scope"/>, one code per field.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The scope is not defined.</exception>
    public static ValidationErrors Validate(ServiceRequestInput input, ServiceRequestValidationScope scope, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        foreach (var field in FieldsOf(scope))
        {
            if (Problem(field, input, today) is { } code)
            {
                errors.Add(field, code);
            }
        }

        return errors;
    }

    /// <summary>The fields <paramref name="scope"/> validates.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The scope is not defined.</exception>
    public static IReadOnlyList<string> FieldsOf(ServiceRequestValidationScope scope) => scope switch
    {
        ServiceRequestValidationScope.RequestStep => RequestStepFields,
        ServiceRequestValidationScope.DetailsStep => DetailsStepFields,
        ServiceRequestValidationScope.Submit => [.. RequestStepFields, .. DetailsStepFields],
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Not a validation scope."),
    };

    /// <summary>The code the category breaks, or null.</summary>
    public static string? CategoryProblem(string? value) => IsMissing(value) ? ServiceRequestValidationCodes.CategoryRequired : null;

    /// <summary>The code the title breaks, or null.</summary>
    public static string? TitleProblem(string? value) => IsMissing(value)
        ? ServiceRequestValidationCodes.TitleRequired
        : LengthProblem(value, new LengthRule(ServiceRequestsLimits.TitleMinLength, ServiceRequestsLimits.TitleMaxLength, ServiceRequestValidationCodes.TitleTooShort, ServiceRequestValidationCodes.TitleTooLong));

    /// <summary>The code the description breaks, or null.</summary>
    public static string? DescriptionProblem(string? value) => IsMissing(value)
        ? ServiceRequestValidationCodes.DescriptionRequired
        : LengthProblem(value, new LengthRule(ServiceRequestsLimits.DescriptionMinLength, ServiceRequestsLimits.DescriptionMaxLength, ServiceRequestValidationCodes.DescriptionTooShort, ServiceRequestValidationCodes.DescriptionTooLong));

    /// <summary>The code the contact phone breaks, or null.</summary>
    public static string? ContactPhoneProblem(string? value)
    {
        if (IsMissing(value))
        {
            return ServiceRequestValidationCodes.ContactPhoneRequired;
        }

        return value.Length == ServiceRequestsLimits.ContactPhoneLength && ContactPhoneRegex().IsMatch(value) ? null : ServiceRequestValidationCodes.ContactPhoneInvalid;
    }

    /// <summary>The code the preferred date breaks, or null (missing is valid).</summary>
    public static string? PreferredDateProblem(string? value, DateOnly today)
    {
        if (IsMissing(value))
        {
            return null;
        }

        if (!TryParseDate(value, out var date))
        {
            return ServiceRequestValidationCodes.PreferredDateInvalid;
        }

        return date < today ? ServiceRequestValidationCodes.PreferredDateInPast : null;
    }

    /// <summary>Reads a strict <c>YYYY-MM-DD</c> calendar date (no 2026-02-30, no other format).</summary>
    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, ServiceRequestsLimits.PreferredDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>The wire form of a stored date.</summary>
    public static string FormatDate(DateOnly date) => date.ToString(ServiceRequestsLimits.PreferredDateFormat, CultureInfo.InvariantCulture);

    /// <summary>True when the text is null, empty or only white space.</summary>
    public static bool IsMissing([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] string? value) => string.IsNullOrWhiteSpace(value);

    private static string? Problem(string field, ServiceRequestInput input, DateOnly today)
    {
        if (field == ServiceRequestFields.CategoryId)
        {
            return CategoryProblem(input.CategoryId);
        }

        if (field == ServiceRequestFields.Title)
        {
            return TitleProblem(input.Title);
        }

        if (field == ServiceRequestFields.Description)
        {
            return DescriptionProblem(input.Description);
        }

        return field == ServiceRequestFields.ContactPhone ? ContactPhoneProblem(input.ContactPhone) : PreferredDateProblem(input.PreferredDate, today);
    }

    private static string? LengthProblem(string value, LengthRule rule)
    {
        if (value.Length < rule.Min)
        {
            return rule.TooShort;
        }

        return value.Length > rule.Max ? rule.TooLong : null;
    }

    [GeneratedRegex(ServiceRequestsLimits.ContactPhonePattern, RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ContactPhoneRegex();

    private sealed record LengthRule(int Min, int Max, string TooShort, string TooLong);
}
