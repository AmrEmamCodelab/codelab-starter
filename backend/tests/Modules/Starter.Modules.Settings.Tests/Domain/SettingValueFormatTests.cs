using Starter.Application.Settings;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Tests.Domain;

public sealed class SettingValueFormatTests
{
    private static readonly SettingRange OneToHundred = new(1, 100);

    [Theory]
    [InlineData("", "")]
    [InlineData("  keep my spaces ", "  keep my spaces ")]
    [InlineData("نص عربي", "نص عربي")]
    public void Text_is_kept_as_given(string value, string stored) =>
        Assert.Equal(SettingValueCheck.Accepted(stored), SettingValueFormat.Check(SettingValueType.Text, value, SettingRange.None));

    [Theory]
    [InlineData("20", "20")]
    [InlineData(" 7 ", "7")]
    [InlineData("+5", "5")]
    [InlineData("1", "1")]
    [InlineData("100", "100")]
    public void Integers_in_range_are_stored_in_invariant_form(string value, string stored) =>
        Assert.Equal(SettingValueCheck.Accepted(stored), SettingValueFormat.Check(SettingValueType.WholeNumber, value, OneToHundred));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("1,000")]
    [InlineData("99999999999")]
    [InlineData("٢٠")]
    public void Anything_but_an_int32_is_an_invalid_integer(string value) =>
        Assert.Equal(SettingValueProblem.InvalidFormat, SettingValueFormat.Check(SettingValueType.WholeNumber, value, SettingRange.None).Problem);

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-3")]
    public void Integers_outside_the_range_are_refused(string value) =>
        Assert.Equal(SettingValueProblem.OutOfRange, SettingValueFormat.Check(SettingValueType.WholeNumber, value, OneToHundred).Problem);

    [Fact]
    public void A_negative_integer_is_fine_without_a_range() =>
        Assert.Equal(SettingValueCheck.Accepted("-3"), SettingValueFormat.Check(SettingValueType.WholeNumber, "-3", SettingRange.None));

    [Theory]
    [InlineData("true", "true")]
    [InlineData("TRUE", "true")]
    [InlineData(" False ", "false")]
    public void Booleans_are_stored_lower_case(string value, string stored) =>
        Assert.Equal(SettingValueCheck.Accepted(stored), SettingValueFormat.Check(SettingValueType.Boolean, value, SettingRange.None));

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("on")]
    public void Anything_but_true_or_false_is_an_invalid_boolean(string value) =>
        Assert.Equal(SettingValueProblem.InvalidFormat, SettingValueFormat.Check(SettingValueType.Boolean, value, SettingRange.None).Problem);

    [Theory]
    [InlineData("0", "0")]
    [InlineData(" 300 ", "300")]
    public void Durations_are_whole_non_negative_seconds(string value, string stored) =>
        Assert.Equal(SettingValueCheck.Accepted(stored), SettingValueFormat.Check(SettingValueType.DurationSeconds, value, SettingRange.None));

    [Theory]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("00:05:00")]
    [InlineData("5m")]
    public void Signed_fractional_or_timespan_text_is_an_invalid_duration(string value) =>
        Assert.Equal(SettingValueProblem.InvalidFormat, SettingValueFormat.Check(SettingValueType.DurationSeconds, value, SettingRange.None).Problem);

    [Fact]
    public void A_duration_outside_the_range_is_refused() =>
        Assert.Equal(SettingValueProblem.OutOfRange, SettingValueFormat.Check(SettingValueType.DurationSeconds, "10", new SettingRange(30, null)).Problem);

    [Theory]
    [InlineData(SettingValueType.Text)]
    [InlineData(SettingValueType.WholeNumber)]
    public void A_value_longer_than_the_column_is_too_long(SettingValueType valueType) =>
        Assert.Equal(
            SettingValueProblem.TooLong,
            SettingValueFormat.Check(valueType, new string('1', SettingFieldLimits.ValueMaxLength + 1), SettingRange.None).Problem);

    [Fact]
    public void A_value_of_exactly_the_column_length_is_accepted() =>
        Assert.True(SettingValueFormat.Check(SettingValueType.Text, new string('x', SettingFieldLimits.ValueMaxLength), SettingRange.None).IsValid);

    [Fact]
    public void An_undefined_type_is_a_programming_error() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SettingValueFormat.Check((SettingValueType)99, "x", SettingRange.None));

    [Fact]
    public void The_readers_parse_what_the_writers_write()
    {
        Assert.True(SettingValueFormat.TryReadInt32(SettingValueFormat.Write(-42), out var number));
        Assert.Equal(-42, number);
        Assert.True(SettingValueFormat.TryReadBoolean(SettingValueFormat.Write(true), out var flag));
        Assert.True(flag);
        Assert.True(SettingValueFormat.TryReadBoolean(SettingValueFormat.Write(false), out flag));
        Assert.False(flag);
        Assert.True(SettingValueFormat.TryReadDuration(SettingValueFormat.Write(90), out var duration));
        Assert.Equal(TimeSpan.FromSeconds(90), duration);
    }

    [Fact]
    public void The_readers_refuse_garbage()
    {
        Assert.False(SettingValueFormat.TryReadInt32("x", out _));
        Assert.False(SettingValueFormat.TryReadBoolean("x", out var flag));
        Assert.False(flag);
        Assert.False(SettingValueFormat.TryReadDuration("-1", out var duration));
        Assert.Equal(TimeSpan.Zero, duration);
    }

    [Theory]
    [InlineData(null, null, int.MinValue, true)]
    [InlineData(1, null, 0, false)]
    [InlineData(1, null, 1, true)]
    [InlineData(null, 10, 10, true)]
    [InlineData(null, 10, 11, false)]
    public void Range_bounds_are_inclusive_and_optional(int? min, int? max, int value, bool contained) =>
        Assert.Equal(contained, new SettingRange(min, max).Contains(value));
}
