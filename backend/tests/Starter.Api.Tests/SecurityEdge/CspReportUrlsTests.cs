using Starter.Infrastructure.Security;

namespace Starter.Api.Tests.SecurityEdge;

public sealed class CspReportUrlsTests
{
    [Theory]
    [InlineData("https://portal.example.test/requests/7?nationalId=1#tab", "https://portal.example.test/requests/7")]
    [InlineData("https://user@portal.example.test:8443/a?b", "https://portal.example.test:8443/a")]
    [InlineData("HTTP://Portal.Example.Test", "http://portal.example.test/")]
    [InlineData("wss://live.example.test/hub?access_token=t", "wss://live.example.test/hub")]
    [InlineData("data:image/png;base64,AAAA", "data")]
    [InlineData("blob:https://portal.example.test/0b1c", "blob")]
    [InlineData("inline", "inline")]
    [InlineData("eval", "eval")]
    [InlineData("/relative/path?q=1", "/relative/path")]
    [InlineData("self#frag", "self")]
    [InlineData("  https://portal.example.test/x  ", "https://portal.example.test/x")]
    public void Redact_keeps_scheme_host_port_and_path_only(string value, string expected) =>
        Assert.Equal(expected, CspReportUrls.Redact(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Redact_of_a_missing_value_is_empty(string? value) => Assert.Equal(string.Empty, CspReportUrls.Redact(value));

    [Fact]
    public void Redact_clips_long_values()
    {
        var value = "https://portal.example.test/" + new string('a', CspReportContract.MaxFieldLength * 2);

        Assert.Equal(CspReportContract.MaxFieldLength, CspReportUrls.Redact(value).Length);
    }

    [Fact]
    public void Clip_drops_control_characters() =>
        Assert.Equal("ab", CspReportUrls.Clip("a\r\n\u0000b", CspReportContract.MaxFieldLength));
}
