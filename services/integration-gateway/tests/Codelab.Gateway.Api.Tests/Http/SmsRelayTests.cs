using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codelab.Errors;
using Codelab.Gateway.Api.Tests.TestSupport;
using Codelab.Integration.Gateway;

namespace Codelab.Gateway.Api.Tests.Http;

/// <summary>
/// The SMS profile renders the provider request from the configured templates, sends it with the provider credential
/// and answers the relay contract; a bad request never reaches the provider and a provider failure is a coded refusal.
/// </summary>
public sealed class SmsRelayTests : IClassFixture<GatewayFixture>
{
    private const string ArabicText = "رمز التحقق 1234 \"{sender}\"";

    private readonly GatewayFixture _fixture;

    public SmsRelayTests(GatewayFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _fixture = fixture;
        fixture.Upstream.SmsStatus = 200;
        fixture.Upstream.SmsBody = FakeUpstream.SmsJson;
    }

    [Fact]
    public async Task A_message_reaches_the_provider_from_the_templates_with_the_credential()
    {
        var json = JsonSerializer.Serialize(new SmsRelayRequest { To = "+05XXXXXX02", Text = ArabicText, Language = GatewayLanguages.Arabic }, GatewayJson.Options);

        using var response = await _fixture.Client.SendAsync(_fixture.Host.SmsRequest(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SmsRelayResult>(GatewayJson.Options);
        Assert.True(result!.Accepted);
        Assert.Equal("msg-1", result.ProviderMessageId);
        Assert.False(string.IsNullOrWhiteSpace(result.ReferenceCode));
        var sent = _fixture.Upstream.Messages.Last(message => message.Body.Contains("+05XXXXXX02", StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Post.Method, sent.Method);
        Assert.Equal(FakeUpstream.SmsPath + "?sender=" + GatewayHost.Sender, sent.PathAndQuery);
        Assert.Equal(_fixture.Host.ProviderCredential, sent.Credential);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal(ArabicText, body.RootElement.GetProperty("text").GetString());
        Assert.Equal("ar", body.RootElement.GetProperty("lang").GetString());
    }

    [Fact]
    public async Task A_provider_that_declines_is_answered_as_not_accepted()
    {
        _fixture.Upstream.SmsBody = """{"ok":false}""";

        using var response = await _fixture.Client.SendAsync(_fixture.Host.SmsRequest(Message("+05XXXXXX03")));

        var result = await response.Content.ReadFromJsonAsync<SmsRelayResult>(GatewayJson.Options);
        Assert.False(result!.Accepted);
        Assert.Null(result.ProviderMessageId);
    }

    [Theory]
    [InlineData(500, HttpStatusCode.ServiceUnavailable, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(429, HttpStatusCode.ServiceUnavailable, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(401, HttpStatusCode.BadGateway, GatewayFailureCodes.UpstreamRejected)]
    [InlineData(422, HttpStatusCode.BadGateway, GatewayFailureCodes.UpstreamRejected)]
    public async Task A_provider_failure_is_a_coded_refusal(int providerStatus, HttpStatusCode status, string code)
    {
        _fixture.Upstream.SmsStatus = providerStatus;

        using var response = await _fixture.Client.SendAsync(_fixture.Host.SmsRequest(Message("+05XXXXXX04")));

        await Problems.AssertAsync(response, status, code);
    }

    [Fact]
    public async Task An_invalid_message_is_400_and_never_sent()
    {
        using var response = await _fixture.Client.SendAsync(_fixture.Host.SmsRequest("""{"to":"+05XXXXXX05","text":"hello","language":3}"""));

        await Problems.AssertAsync(response, HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.DoesNotContain(_fixture.Upstream.Messages, message => message.Body.Contains("+05XXXXXX05", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"to":"+05XXXXXX06","text":"hello","language":true}""")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task A_malformed_body_is_400(string json)
    {
        using var response = await _fixture.Client.SendAsync(_fixture.Host.SmsRequest(json));

        await Problems.AssertAsync(response, HttpStatusCode.BadRequest, ErrorCodes.Malformed);
    }

    private static string Message(string to) =>
        JsonSerializer.Serialize(new SmsRelayRequest { To = to, Text = "Your code is 1234", Language = GatewayLanguages.English }, GatewayJson.Options);
}
