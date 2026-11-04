namespace Codelab.Observability.Tests;

/// <summary>Masked forms of contact details for log lines; never reversible, never the whole value.</summary>
public sealed class PiiMaskTests
{
    [Theory]
    [InlineData("person@example.test", "p***@example.test")]
    [InlineData("  person@example.test ", "p***@example.test")]
    [InlineData("@example.test", "***@example.test")]
    [InlineData("no-at-sign", "***")]
    public void Email_keeps_the_first_character_and_the_domain(string input, string expected)
    {
        Assert.Equal(expected, PiiMask.Email(input));
    }

    [Theory]
    [InlineData("+44 20 7946 0958", "***58")]
    [InlineData("12", "***")]
    [InlineData("no digits", "***")]
    public void Phone_keeps_the_last_two_digits_only(string input, string expected)
    {
        Assert.Equal(expected, PiiMask.Phone(input));
    }

    [Theory]
    [InlineData(@"CORP\jane.doe", @"CORP\j***")]
    [InlineData("jane.doe", "j***")]
    [InlineData(@"CORP\", @"CORP\***")]
    [InlineData("jane@example.test", "j***@example.test")]
    public void LoginName_keeps_the_domain_prefix_and_the_first_character(string input, string expected)
    {
        Assert.Equal(expected, PiiMask.LoginName(input));
    }

    [Theory]
    [InlineData("https://portal.example.test/admin/records?search=jane", "https://portal.example.test/admin/records")]
    [InlineData("https://portal.example.test/page#section", "https://portal.example.test/page")]
    [InlineData("/plain/path", "/plain/path")]
    public void UrlWithoutQuery_drops_query_and_fragment(string input, string expected)
    {
        Assert.Equal(expected, PiiMask.UrlWithoutQuery(input));
    }

    [Theory]
    [InlineData("jane@example.test", "j***@example.test")]
    [InlineData("+44 20 7946 0958", "***58")]
    public void Recipient_masks_by_shape(string input, string expected)
    {
        Assert.Equal(expected, PiiMask.Recipient(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_is_null_for_every_mask(string? input)
    {
        Assert.Null(PiiMask.Email(input));
        Assert.Null(PiiMask.Phone(input));
        Assert.Null(PiiMask.LoginName(input));
        Assert.Null(PiiMask.UrlWithoutQuery(input));
        Assert.Null(PiiMask.Recipient(input));
        Assert.Null(PiiMask.For(PiiKind.Hidden, input));
    }

    [Theory]
    [InlineData(PiiKind.Hidden, "Jane Doe", "***")]
    [InlineData(PiiKind.Email, "jane@example.test", "j***@example.test")]
    [InlineData(PiiKind.Phone, "+44 20 7946 0958", "***58")]
    [InlineData(PiiKind.LoginName, @"CORP\jane", @"CORP\j***")]
    [InlineData(PiiKind.Url, "https://x.example.test/a?b=c", "https://x.example.test/a")]
    [InlineData(PiiKind.Recipient, "jane@example.test", "j***@example.test")]
    public void For_dispatches_by_kind(PiiKind kind, string input, string expected)
    {
        Assert.Equal(expected, PiiMask.For(kind, input));
    }

    [Fact]
    public void Every_kind_has_an_explicit_stable_value()
    {
        Assert.Equal(1, (int)PiiKind.Hidden);
        Assert.Equal(2, (int)PiiKind.Email);
        Assert.Equal(3, (int)PiiKind.Phone);
        Assert.Equal(4, (int)PiiKind.LoginName);
        Assert.Equal(5, (int)PiiKind.Url);
        Assert.Equal(6, (int)PiiKind.Recipient);
    }
}
