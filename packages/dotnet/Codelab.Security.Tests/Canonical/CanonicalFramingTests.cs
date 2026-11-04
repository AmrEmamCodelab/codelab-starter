using System.Globalization;
using System.Text;
using Codelab.Security.Canonical;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.Canonical;

public sealed class CanonicalFramingTests
{
    private readonly CanonicalFraming _framing = new(TestProtocols.Test);

    [Fact]
    public void Every_element_is_written_as_its_utf8_byte_length_a_colon_the_value_and_a_newline()
    {
        var framed = _framing.Build("P", "ab", string.Empty);

        Assert.Equal("20:CODELABTEST-CANON-v1\n1:P\n1:2\n2:ab\n0:\n", framed);
    }

    [Fact]
    public void Lengths_count_utf8_bytes_not_utf16_characters()
    {
        var framed = _framing.Build("P", "سلام");

        Assert.Contains("8:سلام\n", framed, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(framed), _framing.BuildUtf8("P", "سلام"));
    }

    [Fact]
    public void Field_boundaries_cannot_be_shifted_by_the_field_contents()
    {
        var first = _framing.Build("P", "GET", "/a", "bc");
        var second = _framing.Build("P", "GET", "/ab", "c");
        var withDelimiters = _framing.Build("P", "a\n1:b", string.Empty);
        var split = _framing.Build("P", "a", "b");

        Assert.NotEqual(first, second);
        Assert.NotEqual(withDelimiters, split);
    }

    [Fact]
    public void The_same_fields_under_two_profiles_never_collide()
    {
        Assert.NotEqual(_framing.Build("RESP", "x"), _framing.Build("NONCE", "x"));
    }

    [Fact]
    public void The_protocol_prefix_is_bound_into_every_form()
    {
        var alternate = new CanonicalFraming(TestProtocols.Alternate);

        Assert.NotEqual(_framing.Build("P", "x"), alternate.Build("P", "x"));
        Assert.StartsWith("15:ALTPFX-CANON-v1\n", alternate.Build("P"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_field_or_profile_is_a_programming_error()
    {
        Assert.Throws<ArgumentNullException>(() => _framing.Build("P", "a", null!));
        Assert.Throws<ArgumentNullException>(() => _framing.Build(null!, "a"));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    [InlineData("fa-IR")]
    public void The_bytes_are_identical_under_every_culture(string culture)
    {
        var invariant = RunUnder(CultureInfo.InvariantCulture, () => _framing.Build("P", "istanbul", "123"));

        Assert.Equal(invariant, RunUnder(new CultureInfo(culture), () => _framing.Build("P", "istanbul", "123")));
    }

    private static T RunUnder<T>(CultureInfo culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

public sealed class CanonicalTextTests
{
    [Theory]
    [InlineData("get", "GET")]
    [InlineData("Post", "POST")]
    [InlineData("DELETE", "DELETE")]
    [InlineData("options", "OPTIONS")]
    public void A_method_maps_onto_its_allowlist_literal(string method, string expected)
    {
        Assert.True(CanonicalText.TryCanonicalMethod(method, out var canonical));
        Assert.Equal(expected, canonical);
        Assert.Equal(expected, CanonicalText.CanonicalMethod(method));
    }

    [Theory]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    [InlineData("")]
    [InlineData(null)]
    public void A_method_outside_the_allowlist_is_refused_with_an_empty_canonical(string? method)
    {
        Assert.False(CanonicalText.TryCanonicalMethod(method, out var canonical));
        Assert.Equal(string.Empty, canonical);
    }

    [Fact]
    public void The_throwing_lookup_refuses_an_unknown_method()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalText.CanonicalMethod("PROPFIND"));
    }

    [Theory]
    [InlineData("/api/records", "/api/records")]
    [InlineData("/a%7Eb", "/a~b")]
    [InlineData("/a%41", "/aA")]
    [InlineData("/a%2fb", "/a%2Fb")]
    [InlineData("/a%2Fb", "/a%2Fb")]
    [InlineData("/%d8%b3", "/%D8%B3")]
    [InlineData("/trailing%", "/trailing%")]
    [InlineData("/short%4", "/short%4")]
    [InlineData("/bad%zz", "/bad%zz")]
    public void Paths_normalise_unreserved_escapes_and_upper_case_the_rest(string path, string expected)
    {
        Assert.Equal(expected, CanonicalText.NormalizePath(path));
    }
}
