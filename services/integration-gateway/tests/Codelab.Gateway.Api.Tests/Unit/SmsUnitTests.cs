using System.Net;
using System.Text;
using System.Text.Json;
using Codelab.Gateway.Api.Relay;
using Codelab.Gateway.Api.Sms;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Tests.Unit;

public sealed class SmsTemplateTests
{
    private static readonly SmsTemplateValues Values = new("+966 50", "a&b \"c\" {sender}", "ar", "STARTER");

    [Fact]
    public void A_url_template_percent_encodes_every_value()
    {
        var url = SmsTemplate.RenderUrl("https://sms.example.test/send?to={to}&msg={text}&lang={language}&from={sender}", Values);

        Assert.Equal("https://sms.example.test/send?to=%2B966%2050&msg=a%26b%20%22c%22%20%7Bsender%7D&lang=ar&from=STARTER", url);
    }

    [Fact]
    public void A_value_holding_a_placeholder_is_never_substituted_again()
    {
        var body = SmsTemplate.RenderBody("""{"text":"{text}","from":"{sender}"}""", Values, SmsBodyFormat.Json);

        using var document = JsonDocument.Parse(body);
        Assert.Equal("a&b \"c\" {sender}", document.RootElement.GetProperty("text").GetString());
        Assert.Equal("STARTER", document.RootElement.GetProperty("from").GetString());
    }

    [Fact]
    public void A_json_body_keeps_arabic_and_plus_readable_and_stays_valid_json()
    {
        var values = Values with { To = "+05XXXXXX01", Text = "رمز التحقق\n1234" };

        var body = SmsTemplate.RenderBody("""{"to":"{to}","text":"{text}"}""", values, SmsBodyFormat.Json);

        Assert.Contains("+05XXXXXX01", body, StringComparison.Ordinal);
        Assert.Contains("رمز التحقق", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(values.Text, document.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void A_form_body_percent_encodes_every_value()
    {
        var body = SmsTemplate.RenderBody("to={to}&text={text}", Values, SmsBodyFormat.Form);

        Assert.Equal("to=%2B966%2050&text=a%26b%20%22c%22%20%7Bsender%7D", body);
    }

    [Theory]
    [InlineData("https://x.test/{to}/{text}", new string[0])]
    [InlineData("https://x.test/{recipient}?m={message}", new[] { "{recipient}", "{message}" })]
    [InlineData("""{"json":{"nested":1}}""", new string[0])]
    [InlineData(null, new string[0])]
    public void Unknown_placeholders_are_reported(string? template, string[] unknown) =>
        Assert.Equal(unknown, SmsTemplate.UnknownPlaceholders(template));
}

public sealed class SmsProviderOptionsValidatorTests
{
    [Fact]
    public void A_complete_post_provider_is_valid() => Assert.True(Validate(Options()).Succeeded);

    [Fact]
    public void A_get_provider_without_a_body_is_valid()
    {
        var options = Options();
        options.Method = SmsProviderMethod.Get;
        options.BodyTemplate = null;

        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/relative/{to}")]
    [InlineData("http://sms.example.test/send")]
    public void The_url_template_must_render_an_https_address(string? template)
    {
        var options = Options();
        options.UrlTemplate = template;

        Assert.Contains("Gateway:Sms:UrlTemplate", Validate(options).FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_post_needs_a_body_and_a_get_must_not_have_one()
    {
        var post = Options();
        post.BodyTemplate = " ";
        var get = Options();
        get.Method = SmsProviderMethod.Get;

        Assert.Contains("BodyTemplate is required", Validate(post).FailureMessage, StringComparison.Ordinal);
        Assert.Contains("BodyTemplate must be empty", Validate(get).FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_placeholder_is_refused()
    {
        var options = Options();
        options.BodyTemplate = """{"msg":"{message}"}""";

        Assert.Contains("{message}", Validate(options).FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Undefined_enum_values_are_refused()
    {
        var options = Options();
        options.Method = (SmsProviderMethod)9;

        Assert.Contains("Gateway:Sms:Method", Validate(options).FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("X-Key", null)]
    [InlineData(null, "secret")]
    [InlineData("Bad Header", "secret")]
    [InlineData("X-Key", "line\r\nX-Injected: 1")]
    public void The_credential_header_is_a_valid_pair(string? name, string? value)
    {
        var options = Options();
        options.AuthHeaderName = name;
        options.AuthHeaderValue = value;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.DoesNotContain("secret", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("result..id")]
    [InlineData("result[0]")]
    public void Answer_paths_are_dotted_member_names(string path)
    {
        var options = Options();
        options.MessageIdMember = path;

        Assert.Contains("MessageIdMember", Validate(options).FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Language_codes_are_required()
    {
        var options = Options();
        options.LanguageCodes.English = "";

        Assert.Contains("LanguageCodes", Validate(options).FailureMessage, StringComparison.Ordinal);
    }

    internal static SmsProviderOptions Options() => new()
    {
        UrlTemplate = "https://sms.example.test/send?from={sender}",
        Method = SmsProviderMethod.Post,
        BodyTemplate = """{"to":"{to}","text":"{text}","lang":"{language}"}""",
        BodyFormat = SmsBodyFormat.Json,
        AuthHeaderName = "X-Key",
        AuthHeaderValue = "secret",
        Sender = "STARTER",
        AcceptedMember = "ok",
        MessageIdMember = "result.id",
    };

    private static ValidateOptionsResult Validate(SmsProviderOptions options) =>
        new SmsProviderOptionsValidator(SsoRelayOptionsValidatorTests.Environment(isDevelopment: false)).Validate(name: null, options);
}

public sealed class SmsProviderRequestTests
{
    private static readonly SmsRelayRequest Message = new() { To = "+05XXXXXX01", Text = "hello", Language = GatewayLanguages.English };

    [Fact]
    public async Task A_post_carries_the_rendered_body_the_credential_and_the_language_code()
    {
        using var request = SmsProviderRequest.Build(SmsProviderOptionsValidatorTests.Options(), Message);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://sms.example.test/send?from=STARTER", request.RequestUri!.ToString());
        Assert.Equal("secret", request.Headers.GetValues("X-Key").Single());
        Assert.Equal(GatewayMediaTypes.Json, request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("""{"to":"+05XXXXXX01","text":"hello","lang":"en"}""", await request.Content.ReadAsStringAsync());
    }

    [Fact]
    public void A_get_has_no_body_and_no_credential_when_none_is_configured()
    {
        var options = SmsProviderOptionsValidatorTests.Options();
        options.Method = SmsProviderMethod.Get;
        options.UrlTemplate = "https://sms.example.test/send?to={to}&lang={language}";
        options.AuthHeaderName = null;
        options.AuthHeaderValue = null;

        using var request = SmsProviderRequest.Build(options, Message);

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Null(request.Content);
        Assert.Equal("https://sms.example.test/send?to=%2B05XXXXXX01&lang=en", request.RequestUri!.AbsoluteUri);
        Assert.False(request.Headers.Contains("X-Key"));
    }

    [Fact]
    public async Task A_form_body_uses_the_form_media_type()
    {
        var options = SmsProviderOptionsValidatorTests.Options();
        options.BodyFormat = SmsBodyFormat.Form;
        options.BodyTemplate = "to={to}&lang={language}";

        using var request = SmsProviderRequest.Build(options, new SmsRelayRequest { To = "1", Text = "x", Language = GatewayLanguages.Arabic });

        Assert.Equal(GatewayMediaTypes.FormUrlEncoded, request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("to=1&lang=ar", await request.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_http_provider_sends_through_its_client_and_reads_the_answer()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""{"ok":true,"result":{"id":42}}""") });
        using var http = new HttpClient(handler);
        var provider = new HttpSmsProvider(http, Microsoft.Extensions.Options.Options.Create(SmsProviderOptionsValidatorTests.Options()));

        var outcome = await provider.SendAsync(Message, CancellationToken.None);

        Assert.Equal(new SmsProviderOutcome(Accepted: true, ProviderMessageId: "42", Failure: null), outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task The_http_provider_reports_an_upstream_failure()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var provider = new HttpSmsProvider(http, Microsoft.Extensions.Options.Options.Create(SmsProviderOptionsValidatorTests.Options()));

        var outcome = await provider.SendAsync(Message, CancellationToken.None);

        Assert.False(outcome.Accepted);
        Assert.Equal(GatewayFailureCodes.UpstreamUnavailable, outcome.Failure!.Code);
    }
}

public sealed class SmsProviderAnswerTests
{
    [Fact]
    public void Without_answer_paths_a_success_means_accepted()
    {
        var options = SmsProviderOptionsValidatorTests.Options();
        options.AcceptedMember = null;
        options.MessageIdMember = null;

        Assert.Equal(new SmsProviderOutcome(true, null, null), SmsProviderAnswer.Read(options, Success("not json at all")));
    }

    [Theory]
    [InlineData("""{"ok":true,"result":{"id":"m-7"}}""", true, "m-7")]
    [InlineData("""{"ok":false,"result":{"id":"m-8"}}""", false, "m-8")]
    [InlineData("""{"ok":true,"result":{"id":123}}""", true, "123")]
    [InlineData("""{"ok":true}""", true, null)]
    [InlineData("""{"ok":true,"result":{"id":null}}""", true, null)]
    public void The_configured_members_are_read(string body, bool accepted, string? messageId) =>
        Assert.Equal(new SmsProviderOutcome(accepted, messageId, null), SmsProviderAnswer.Read(SmsProviderOptionsValidatorTests.Options(), Success(body)));

    [Theory]
    [InlineData("""{"ok":"yes"}""")]
    [InlineData("""{"result":{"id":"m"}}""")]
    [InlineData("""{"ok":true,"result":{"id":{"a":1}}}""")]
    [InlineData("<html/>")]
    public void An_answer_that_does_not_fit_is_rejected(string body)
    {
        var outcome = SmsProviderAnswer.Read(SmsProviderOptionsValidatorTests.Options(), Success(body));

        Assert.False(outcome.Accepted);
        Assert.Equal(GatewayFailureCodes.UpstreamRejected, outcome.Failure!.Code);
    }

    [Fact]
    public void A_dotted_path_walks_objects_only()
    {
        using var document = JsonDocument.Parse("""{"a":{"b":[1]}}""");

        Assert.NotNull(SmsProviderAnswer.Find(document.RootElement, "a.b"));
        Assert.Null(SmsProviderAnswer.Find(document.RootElement, "a.b.c"));
        Assert.Null(SmsProviderAnswer.Find(document.RootElement, "x"));
    }

    private static UpstreamResult Success(string body) => UpstreamResult.Success(HttpStatusCode.OK, Encoding.UTF8.GetBytes(body));
}
