using Starter.Modules.ServiceRequests.Application.Validation;
using Starter.Modules.ServiceRequests.Domain;
using Starter.Modules.ServiceRequests.Tests.Fakes;

namespace Starter.Modules.ServiceRequests.Tests.Validation;

public sealed class ServiceRequestRulesTests
{
    private const string ValidTitle = "Passport renewal";
    private const string ValidDescription = "I need to renew my passport before travelling.";
    private const string ValidPhone = TestValues.ValidPhone;

    private static readonly ServiceRequestInput Complete = new(
        CategoryId: FakeCodec.CategoryToken(1),
        Title: ValidTitle,
        Description: ValidDescription,
        ContactPhone: ValidPhone,
        PreferredDate: "2026-11-20");

    [Theory]
    [InlineData(null, ServiceRequestValidationCodes.CategoryRequired)]
    [InlineData("", ServiceRequestValidationCodes.CategoryRequired)]
    [InlineData("  ", ServiceRequestValidationCodes.CategoryRequired)]
    [InlineData("any-token", null)]
    public void The_category_is_only_checked_for_presence(string? value, string? expected)
    {
        Assert.Equal(expected, ServiceRequestRules.CategoryProblem(value));
    }

    [Theory]
    [InlineData(null, ServiceRequestValidationCodes.TitleRequired)]
    [InlineData("\t ", ServiceRequestValidationCodes.TitleRequired)]
    [InlineData("Abcd", ServiceRequestValidationCodes.TitleTooShort)]
    [InlineData("Abcde", null)]
    public void The_title_is_required_and_at_least_five_characters(string? value, string? expected)
    {
        Assert.Equal(expected, ServiceRequestRules.TitleProblem(value));
    }

    [Fact]
    public void The_title_is_at_most_two_hundred_characters()
    {
        Assert.Null(ServiceRequestRules.TitleProblem(new string('a', ServiceRequestsLimits.TitleMaxLength)));
        Assert.Equal(ServiceRequestValidationCodes.TitleTooLong, ServiceRequestRules.TitleProblem(new string('a', ServiceRequestsLimits.TitleMaxLength + 1)));
    }

    [Fact]
    public void The_description_is_between_twenty_and_two_thousand_characters()
    {
        Assert.Equal(ServiceRequestValidationCodes.DescriptionRequired, ServiceRequestRules.DescriptionProblem(null));
        Assert.Equal(ServiceRequestValidationCodes.DescriptionTooShort, ServiceRequestRules.DescriptionProblem(new string('a', ServiceRequestsLimits.DescriptionMinLength - 1)));
        Assert.Null(ServiceRequestRules.DescriptionProblem(new string('a', ServiceRequestsLimits.DescriptionMinLength)));
        Assert.Null(ServiceRequestRules.DescriptionProblem(new string('a', ServiceRequestsLimits.DescriptionMaxLength)));
        Assert.Equal(ServiceRequestValidationCodes.DescriptionTooLong, ServiceRequestRules.DescriptionProblem(new string('a', ServiceRequestsLimits.DescriptionMaxLength + 1)));
    }

    [Theory]
    [InlineData(null, ServiceRequestValidationCodes.ContactPhoneRequired)]
    [InlineData(ValidPhone, null)]
    [InlineData("0612345678", ServiceRequestValidationCodes.ContactPhoneInvalid)]
    [InlineData("051234567", ServiceRequestValidationCodes.ContactPhoneInvalid)]
    [InlineData("05123456789", ServiceRequestValidationCodes.ContactPhoneInvalid)]
    [InlineData("05123456ab", ServiceRequestValidationCodes.ContactPhoneInvalid)]
    [InlineData("+966" + "5" + "12345678", ServiceRequestValidationCodes.ContactPhoneInvalid)]
    [InlineData(TestValues.ValidPhone + "\n", ServiceRequestValidationCodes.ContactPhoneInvalid)]
    [InlineData("٠٥١٢٣٤٥٦٧٨", ServiceRequestValidationCodes.ContactPhoneInvalid)]
    public void The_contact_phone_is_a_saudi_mobile_number(string? value, string? expected)
    {
        Assert.Equal(expected, ServiceRequestRules.ContactPhoneProblem(value));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("2026-11-06", null)]
    [InlineData("2027-01-31", null)]
    [InlineData("2026-11-05", ServiceRequestValidationCodes.PreferredDateInPast)]
    [InlineData("20/11/2026", ServiceRequestValidationCodes.PreferredDateInvalid)]
    [InlineData("2026-02-30", ServiceRequestValidationCodes.PreferredDateInvalid)]
    [InlineData("2026-11-20T10:00:00", ServiceRequestValidationCodes.PreferredDateInvalid)]
    [InlineData("2026-1-20", ServiceRequestValidationCodes.PreferredDateInvalid)]
    public void The_preferred_date_is_optional_a_real_iso_date_and_not_before_today(string? value, string? expected)
    {
        Assert.Equal(expected, ServiceRequestRules.PreferredDateProblem(value, TestValues.Today));
    }

    [Fact]
    public void A_complete_request_is_valid_in_every_scope()
    {
        foreach (var scope in Enum.GetValues<ServiceRequestValidationScope>())
        {
            Assert.True(ServiceRequestRules.Validate(Complete, scope, TestValues.Today).IsValid, scope.ToString());
        }
    }

    [Fact]
    public void Each_step_checks_only_its_own_fields()
    {
        var onlyStepOne = Complete with { Description = "short", ContactPhone = "123", PreferredDate = "bad" };
        var onlyStepTwo = Complete with { CategoryId = null, Title = "x" };

        Assert.True(ServiceRequestRules.Validate(onlyStepOne, ServiceRequestValidationScope.RequestStep, TestValues.Today).IsValid);
        Assert.True(ServiceRequestRules.Validate(onlyStepTwo, ServiceRequestValidationScope.DetailsStep, TestValues.Today).IsValid);
    }

    [Fact]
    public void A_submit_that_skipped_step_two_fails_on_its_required_fields()
    {
        var skipped = Complete with { Description = null, ContactPhone = null, PreferredDate = null };

        var errors = ServiceRequestRules.Validate(skipped, ServiceRequestValidationScope.Submit, TestValues.Today).ToDictionary();

        Assert.Equal([ServiceRequestFields.ContactPhone, ServiceRequestFields.Description], errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([ServiceRequestValidationCodes.DescriptionRequired], errors[ServiceRequestFields.Description]);
    }

    [Fact]
    public void Each_field_reports_one_code()
    {
        var errors = ServiceRequestRules.Validate(new ServiceRequestInput(null, null, null, null, "bad"), ServiceRequestValidationScope.Submit, TestValues.Today).ToDictionary();

        Assert.Equal(5, errors.Count);
        Assert.All(errors.Values, codes => Assert.Single(codes));
    }

    [Fact]
    public void The_field_names_are_the_camel_case_wire_names()
    {
        Assert.Equal(
            ["categoryId", "title", "description", "contactPhone", "preferredDate"],
            ServiceRequestRules.FieldsOf(ServiceRequestValidationScope.Submit));
    }

    [Fact]
    public void An_undefined_scope_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceRequestRules.FieldsOf((ServiceRequestValidationScope)0));
    }

    [Fact]
    public void Dates_round_trip_through_the_wire_format()
    {
        Assert.True(ServiceRequestRules.TryParseDate("2026-11-20", out var date));
        Assert.Equal("2026-11-20", ServiceRequestRules.FormatDate(date));
    }
}
