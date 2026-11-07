using Codelab.Integration.Gateway;

namespace Codelab.Integration.Tests.Gateway;

/// <summary>SmsRelayRequestValidator and the SsoTokenRelayRequest guards.</summary>
public sealed class GatewayRequestValidationTests
{
    [Fact]
    public void A_complete_sms_is_valid() =>
        Assert.Empty(SmsRelayRequestValidator.Validate(new SmsRelayRequest { To = "+9665XXXXXXXX", Text = "x", Language = GatewayLanguages.English }));

    [Fact]
    public void A_missing_sms_is_one_problem() =>
        Assert.Single(SmsRelayRequestValidator.Validate(null));

    [Fact]
    public void Every_problem_is_reported_at_once() =>
        Assert.Equal(3, SmsRelayRequestValidator.Validate(new SmsRelayRequest()).Count);

    [Theory]
    [InlineData(" ", "x", GatewayLanguages.Arabic, "to")]
    [InlineData("0500\n01", "x", GatewayLanguages.Arabic, "to")]
    [InlineData("050000000105000000010500000001055", "x", GatewayLanguages.Arabic, "to")]
    [InlineData("05XXXXXXXX", "", GatewayLanguages.Arabic, "text")]
    [InlineData("05XXXXXXXX", "x", 0, "language")]
    [InlineData("05XXXXXXXX", "x", 3, "language")]
    public void A_bad_field_is_named(string to, string text, int language, string field)
    {
        var problem = Assert.Single(SmsRelayRequestValidator.Validate(new SmsRelayRequest { To = to, Text = text, Language = language }));

        Assert.StartsWith(field, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_over_the_limit_is_refused()
    {
        var request = new SmsRelayRequest { To = "05XXXXXXXX", Text = new string('x', GatewayRelayLimits.MaxSmsTextLength + 1), Language = GatewayLanguages.Arabic };

        Assert.StartsWith("text", Assert.Single(SmsRelayRequestValidator.Validate(request)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_request_keeps_its_fields_in_order()
    {
        var request = new SsoTokenRelayRequest([new("b", "2"), new("a", "1")]);

        Assert.Equal(["b", "a"], request.Fields.Select(field => field.Key));
    }

    [Fact]
    public void A_token_request_needs_at_least_one_field_and_at_most_the_limit()
    {
        Assert.Throws<ArgumentException>(() => new SsoTokenRelayRequest([]));
        var tooMany = Enumerable.Range(0, GatewayRelayLimits.MaxSsoTokenFormFields + 1).Select(index => new KeyValuePair<string, string>($"f{index}", "v"));
        Assert.Throws<ArgumentException>(() => new SsoTokenRelayRequest(tooMany));
    }

    [Fact]
    public void A_token_request_field_needs_a_name() =>
        Assert.Throws<ArgumentException>(() => new SsoTokenRelayRequest([new(" ", "v")]));
}
