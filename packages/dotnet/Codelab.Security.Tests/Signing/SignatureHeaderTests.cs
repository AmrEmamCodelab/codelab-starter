using System.Globalization;
using Codelab.Security.Signing;

namespace Codelab.Security.Tests.Signing;

public sealed class SignatureHeaderTests
{
    private static readonly string Nonce = new('N', SignatureHeader.NonceLength);
    private static readonly string Mac = new('A', HmacSignature.MacCharacters);

    [Theory]
    [InlineData("1.946684800.{N}.{M}", true)]
    [InlineData("1.0.{N}.{M}", true)]
    [InlineData("2.946684800.{N}.{M}", false)]
    [InlineData("1.0946684800.{N}.{M}", false)]
    [InlineData("1.-5.{N}.{M}", false)]
    [InlineData("1.+5.{N}.{M}", false)]
    [InlineData("1.94668480000.{N}.{M}", false)]
    [InlineData("1.946684800.{N}.{M}.extra", false)]
    [InlineData("1.946684800.{N}", false)]
    [InlineData("1.946684800.{N}=.{M}", false)]
    [InlineData("1.946684800.{N}.{M}=", false)]
    [InlineData("1. 946684800.{N}.{M}", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_grammar_accepts_only_four_exactly_shaped_fields(string? template, bool expected)
    {
        var value = template?.Replace("{N}", Nonce, StringComparison.Ordinal).Replace("{M}", Mac, StringComparison.Ordinal);

        Assert.Equal(expected, SignatureHeader.TryParse(value, out _));
    }

    [Fact]
    public void A_parsed_header_round_trips_and_never_prints_the_mac()
    {
        var value = $"1.946684800.{Nonce}.{Mac}";

        Assert.True(SignatureHeader.TryParse(value, out var header));
        Assert.Equal(946684800, header.TimestampUnixSeconds);
        Assert.Equal(value, header.Format());
        Assert.DoesNotContain(Mac, header.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_twenty_thousand_character_value_is_refused_on_length()
    {
        Assert.False(SignatureHeader.TryParse(new string('1', 20_000), out _));
    }

    [Theory]
    [InlineData(-1L, false)]
    [InlineData(0L, true)]
    [InlineData(SignatureHeader.MaxTimestamp, true)]
    [InlineData(SignatureHeader.MaxTimestamp + 1, false)]
    public void A_numeric_timestamp_must_fit_ten_digits(long timestamp, bool expected)
    {
        Assert.Equal(expected, SignatureHeader.TryCreate(timestamp, Nonce, Mac, out _));
    }

    [Fact]
    public void The_grammar_is_culture_invariant()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
        try
        {
            Assert.True(SignatureHeader.TryParse($"1.946684800.{Nonce}.{Mac}", out var header));
            Assert.Equal("946684800", header.Timestamp);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void The_response_header_is_version_dot_mac()
    {
        var formatted = ResponseSignatureHeader.Format(Mac);

        Assert.Equal("1." + Mac, formatted);
        Assert.True(ResponseSignatureHeader.TryParse(formatted, out var mac));
        Assert.Equal(Mac, mac);
        Assert.False(ResponseSignatureHeader.TryParse("2." + Mac, out _));
        Assert.False(ResponseSignatureHeader.TryParse("1." + Mac[1..] + "+", out _));
        Assert.Throws<ArgumentException>(() => ResponseSignatureHeader.Format("short"));
    }
}
