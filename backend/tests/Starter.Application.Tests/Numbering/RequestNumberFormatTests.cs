using Starter.Application.Numbering;

namespace Starter.Application.Tests.Numbering;

/// <summary>The <c>PREFIX-YYYY-NNN</c> format: padding, growth past 999, the prefix rule and the bounds.</summary>
public sealed class RequestNumberFormatTests
{
    [Theory]
    [InlineData("CU", 2026, 1, "CU-2026-001")]
    [InlineData("CU", 2026, 42, "CU-2026-042")]
    [InlineData("CU", 2026, 999, "CU-2026-999")]
    [InlineData("CU", 2026, 1000, "CU-2026-1000")]
    [InlineData("SR", 2027, 123456, "SR-2027-123456")]
    [InlineData("ABCDEF", 2026, 7, "ABCDEF-2026-007")]
    public void Formats_the_prefix_year_and_a_counter_padded_to_at_least_three_digits(string prefix, int year, long counter, string expected) =>
        Assert.Equal(expected, RequestNumberFormat.Format(prefix, year, counter));

    [Theory]
    [InlineData("CU", true)]
    [InlineData("ABCDEF", true)]
    [InlineData("C", false)]
    [InlineData("ABCDEFG", false)]
    [InlineData("cu", false)]
    [InlineData("C1", false)]
    [InlineData("C-U", false)]
    [InlineData("CÜ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_prefix_is_two_to_six_uppercase_ascii_letters(string? prefix, bool valid) =>
        Assert.Equal(valid, RequestNumberFormat.IsValidPrefix(prefix));

    [Theory]
    [InlineData("cu")]
    [InlineData("CU1")]
    [InlineData("C")]
    public void Formatting_refuses_an_invalid_prefix(string prefix)
    {
        var refused = Assert.Throws<ArgumentException>(() => RequestNumberFormat.Format(prefix, year: 2026, counter: 1));

        Assert.Equal("prefix", refused.ParamName);
    }

    [Fact]
    public void Ensuring_a_valid_prefix_passes_silently() => RequestNumberFormat.EnsureValidPrefix("CU");

    [Theory]
    [InlineData(999)]
    [InlineData(10000)]
    public void Formatting_refuses_a_year_that_is_not_four_digits(int year) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RequestNumberFormat.Format("CU", year, counter: 1));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Formatting_refuses_a_counter_below_one(long counter) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RequestNumberFormat.Format("CU", year: 2026, counter));

    [Fact]
    public void The_format_constants_are_the_documented_rule()
    {
        Assert.Equal(2, RequestNumberFormat.PrefixMinLength);
        Assert.Equal(6, RequestNumberFormat.PrefixMaxLength);
        Assert.Equal(3, RequestNumberFormat.MinimumCounterDigits);
        Assert.Equal('-', RequestNumberFormat.Separator);
    }
}
