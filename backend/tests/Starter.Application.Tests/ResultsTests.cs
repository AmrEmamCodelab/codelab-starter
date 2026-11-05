using Starter.Application.Results;
using Starter.Application.Validation;

namespace Starter.Application.Tests;

public sealed class OutcomeTests
{
    [Fact]
    public void Success_carries_the_value_and_no_refusal()
    {
        var outcome = Outcome.Success(value: 42);

        Assert.True(outcome.IsSuccess);
        Assert.Null(outcome.Refusal);
        Assert.Equal(42, outcome.Value);
    }

    [Fact]
    public void Refused_carries_the_refusal_and_has_no_value()
    {
        var refusal = Refusal.Conflict("SAMPLE_CONFLICT");

        var outcome = Outcome.Refused<int>(refusal);

        Assert.False(outcome.IsSuccess);
        Assert.Same(refusal, outcome.Refusal);
        var error = Assert.Throws<InvalidOperationException>(() => outcome.Value);
        Assert.Contains("SAMPLE_CONFLICT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Implicit_conversions_build_success_and_refusal()
    {
        Outcome<string> success = "done";
        Outcome<string> refused = Refusal.NotFound("SAMPLE_MISSING");

        Assert.Equal("done", success.Value);
        Assert.Equal(RefusalCategory.NotFound, refused.Refusal!.Category);
    }

    [Fact]
    public void Refused_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => Outcome.Refused<int>(refusal: null!));
    }

    [Fact]
    public void Done_is_a_single_value()
    {
        Assert.Same(Done.Value, Done.Value);
    }
}

public sealed class RefusalTests
{
    public static TheoryData<Func<string, Refusal>, RefusalCategory> Factories => new()
    {
        { code => Refusal.Invalid(code), RefusalCategory.Invalid },
        { Refusal.Forbidden, RefusalCategory.Forbidden },
        { Refusal.NotFound, RefusalCategory.NotFound },
        { Refusal.Conflict, RefusalCategory.Conflict },
        { Refusal.Unprocessable, RefusalCategory.Unprocessable },
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void Each_factory_sets_its_category_and_code_without_field_errors(Func<string, Refusal> factory, RefusalCategory category)
    {
        var refusal = factory("SAMPLE_CODE");

        Assert.Equal(category, refusal.Category);
        Assert.Equal("SAMPLE_CODE", refusal.Code);
        Assert.Empty(refusal.FieldErrors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_code_is_refused(string? code)
    {
        Assert.ThrowsAny<ArgumentException>(() => Refusal.Conflict(code!));
    }

    [Fact]
    public void Invalid_from_validation_errors_uses_the_platform_code_and_every_field()
    {
        var errors = new ValidationErrors().Add("userName", ValidationMessages.Required).Add("password", ValidationMessages.TooLong);

        var refusal = Refusal.Invalid(errors);

        Assert.Equal(RefusalCategory.Invalid, refusal.Category);
        Assert.Equal(PlatformErrorCodes.Validation, refusal.Code);
        Assert.Equal([ValidationMessages.Required], refusal.FieldErrors["userName"]);
        Assert.Equal([ValidationMessages.TooLong], refusal.FieldErrors["password"]);
    }

    [Fact]
    public void Invalid_with_field_errors_keeps_the_module_code()
    {
        var fields = new Dictionary<string, string[]> { ["name"] = [ValidationMessages.InvalidFormat] };

        var refusal = Refusal.Invalid(fields, "SAMPLE_INVALID");

        Assert.Equal("SAMPLE_INVALID", refusal.Code);
        Assert.Same(fields, refusal.FieldErrors);
    }

    [Fact]
    public void Platform_validation_code_matches_the_errors_package_vocabulary()
    {
        Assert.Equal("VALIDATION_FAILED", PlatformErrorCodes.Validation);
    }

    [Fact]
    public void Category_values_are_explicit_and_stable()
    {
        Assert.Equal(1, (int)RefusalCategory.Invalid);
        Assert.Equal(2, (int)RefusalCategory.Forbidden);
        Assert.Equal(3, (int)RefusalCategory.NotFound);
        Assert.Equal(4, (int)RefusalCategory.Conflict);
        Assert.Equal(5, (int)RefusalCategory.Unprocessable);
    }
}
