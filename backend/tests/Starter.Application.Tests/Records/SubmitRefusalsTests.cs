using Starter.Application.Attachments;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Validation;

namespace Starter.Application.Tests.Records;

/// <summary>The one central mapping of a submit refusal to its category (HTTP status) and error code.</summary>
public sealed class SubmitRefusalsTests
{
    private const string TitleField = "title";
    private const string PhoneField = "contactPhone";
    private const int UndefinedRefusalValue = 99;

    /// <summary>
    /// The mapping table: refusal, category, the HTTP status the host gives that category (OutcomeResults), code.
    /// </summary>
    public static TheoryData<SubmitRefusal, RefusalCategory, int, string> MappingTable => new()
    {
        { SubmitRefusal.NotFound, RefusalCategory.NotFound, 404, RecordsErrorCodes.NotFound },
        { SubmitRefusal.NotOwner, RefusalCategory.NotFound, 404, RecordsErrorCodes.NotFound },
        { SubmitRefusal.NotSubmittable, RefusalCategory.Conflict, 409, RecordsErrorCodes.NotSubmittable },
        { SubmitRefusal.AttachmentsMissing, RefusalCategory.Conflict, 409, AttachmentErrorCodes.RequiredMissing },
        { SubmitRefusal.Concurrency, RefusalCategory.Conflict, 409, RecordsErrorCodes.Concurrency },
    };

    [Theory]
    [MemberData(nameof(MappingTable))]
    public void Each_refusal_maps_to_one_status_and_one_code(SubmitRefusal refusal, RefusalCategory category, int status, string code)
    {
        var mapped = SubmitRefusals.ToRefusal(refusal, fieldErrors: null);

        Assert.Equal((category, code), (mapped.Category, mapped.Code));
        Assert.Equal(status, HttpStatusOf(mapped.Category));
        Assert.Empty(mapped.FieldErrors);
    }

    [Fact]
    public void A_validation_refusal_is_400_validation_failed_with_every_field_error_grouped_by_field()
    {
        IReadOnlyList<FieldError> errors =
        [
            new(TitleField, ValidationMessages.Required),
            new(PhoneField, ValidationMessages.InvalidFormat),
            new(TitleField, ValidationMessages.TooLong),
        ];

        var mapped = SubmitRefusals.ToRefusal(SubmitRefusal.ValidationFailed, errors);

        Assert.Equal((RefusalCategory.Invalid, PlatformErrorCodes.Validation), (mapped.Category, mapped.Code));
        Assert.Equal(400, HttpStatusOf(mapped.Category));
        Assert.Equal([ValidationMessages.Required, ValidationMessages.TooLong], mapped.FieldErrors[TitleField]);
        Assert.Equal([ValidationMessages.InvalidFormat], mapped.FieldErrors[PhoneField]);
    }

    [Fact]
    public void Every_defined_refusal_has_a_mapping()
    {
        foreach (var refusal in Enum.GetValues<SubmitRefusal>())
        {
            var errors = refusal == SubmitRefusal.ValidationFailed ? [new FieldError(TitleField, ValidationMessages.Required)] : (IReadOnlyList<FieldError>?)null;

            var mapped = SubmitRefusals.ToRefusal(refusal, errors);

            Assert.False(string.IsNullOrWhiteSpace(mapped.Code));
        }
    }

    [Fact]
    public void The_refusal_values_are_a_wire_contract() =>
        Assert.Equal(
            [1, 2, 3, 4, 5, 6],
            Enum.GetValues<SubmitRefusal>().Select(value => (int)value));

    [Fact]
    public void A_validation_refusal_without_field_errors_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() => SubmitRefusals.ToRefusal(SubmitRefusal.ValidationFailed, fieldErrors: null));
        Assert.Throws<ArgumentException>(() => SubmitRefusals.ToRefusal(SubmitRefusal.ValidationFailed, fieldErrors: []));
    }

    [Fact]
    public void Field_errors_on_any_other_refusal_are_a_programming_error() =>
        Assert.Throws<ArgumentException>(() =>
            SubmitRefusals.ToRefusal(SubmitRefusal.NotSubmittable, [new FieldError(TitleField, ValidationMessages.Required)]));

    [Fact]
    public void An_undefined_refusal_value_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SubmitRefusals.ToRefusal((SubmitRefusal)UndefinedRefusalValue, fieldErrors: null));

    [Fact]
    public void The_records_error_codes_are_a_wire_contract()
    {
        Assert.Equal("RECORDS_NOT_FOUND", RecordsErrorCodes.NotFound);
        Assert.Equal("RECORDS_NOT_SUBMITTABLE", RecordsErrorCodes.NotSubmittable);
        Assert.Equal("RECORDS_CONCURRENCY", RecordsErrorCodes.Concurrency);
    }

    /// <summary>The host's category-to-status map (Starter.Infrastructure.Results.OutcomeResults via Codelab.Errors).</summary>
    private static int HttpStatusOf(RefusalCategory category) => category switch
    {
        RefusalCategory.Invalid => 400,
        RefusalCategory.Forbidden => 403,
        RefusalCategory.NotFound => 404,
        RefusalCategory.Conflict => 409,
        RefusalCategory.Unprocessable => 422,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };
}
