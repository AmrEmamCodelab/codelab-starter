namespace Codelab.Security.Tests.Protocol;

public sealed class SecurityProtocolTests
{
    [Fact]
    public void Every_wire_name_derives_from_the_prefix()
    {
        var protocol = SecurityProtocol.Create("CODELABTEST");

        Assert.Equal("CODELABTEST-CANON-v1", protocol.CanonicalVersion);
        Assert.Equal("CODELABTEST-SIG-v1", protocol.SignatureVersion);
        Assert.Equal("CODELABTEST-SIG-v1/REQ", protocol.RequestProfile);
        Assert.Equal("CODELABTEST-SIG-v1/RESP", protocol.ResponseProfile);
        Assert.Equal("CODELABTEST-SIG-v1/KDF", protocol.SessionKeyProfile);
        Assert.Equal("CODELABTEST-PUBLICID-v1", protocol.PublicIdSpec);
        Assert.Equal("CODELABTEST-TICKET-v1", protocol.TicketSpec);
        Assert.Equal("CODELABTEST-RECEIPT-v1", protocol.ReceiptSpec);
        Assert.Equal("CODELABTEST-S2S-v1", protocol.ServiceAssertionType);
        Assert.Equal("X-CODELABTEST-Signature", protocol.SignatureHeaderName);
        Assert.Equal("X-CODELABTEST-Response-Signature", protocol.ResponseSignatureHeaderName);
        Assert.Equal("X-CODELABTEST-Service-Assertion", protocol.ServiceAssertionHeaderName);
    }

    [Theory]
    [InlineData("AB", true)]
    [InlineData("MOHPERMITS", true)]
    [InlineData("A1B2", true)]
    [InlineData("ABCDEFGHIJKLMNOP", true)]
    [InlineData("ABCDEFGHIJKLMNOPQ", false)]
    [InlineData("A", false)]
    [InlineData("1AB", false)]
    [InlineData("ab", false)]
    [InlineData("AB-C", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_prefix_is_two_to_sixteen_upper_case_letters_and_digits_starting_with_a_letter(string? prefix, bool valid)
    {
        Assert.Equal(valid, SecurityProtocol.IsValidPrefix(prefix));
    }

    [Fact]
    public void Creating_a_protocol_from_an_invalid_prefix_throws()
    {
        Assert.Throws<ArgumentException>(() => SecurityProtocol.Create("bad prefix"));
    }

    [Fact]
    public void The_options_produce_the_protocol_and_refuse_a_missing_prefix()
    {
        Assert.Equal("ALTPFX-SIG-v1", new SecurityProtocolOptions { Prefix = "ALTPFX" }.ToProtocol().SignatureVersion);
        Assert.Throws<ArgumentException>(() => new SecurityProtocolOptions().ToProtocol());
    }
}
