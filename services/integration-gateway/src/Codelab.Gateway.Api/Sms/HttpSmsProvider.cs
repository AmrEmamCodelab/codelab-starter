using System.Net.Http.Headers;
using System.Text;
using Codelab.Gateway.Api.Relay;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Sms;

/// <summary>What the SMS provider made of one message: accepted or not and its id, or why there is no answer.</summary>
/// <param name="Accepted">True when the provider accepted the message for delivery.</param>
/// <param name="ProviderMessageId">The provider's message id, when it returns one.</param>
/// <param name="Failure">Why the provider gave no usable answer; null when it answered.</param>
internal sealed record SmsProviderOutcome(bool Accepted, string? ProviderMessageId, UpstreamFailure? Failure);

/// <summary>
/// The SMS provider adapter the SMS relay profile sends through. The shipped one is <see cref="HttpSmsProvider"/>; a
/// project whose provider needs more than templates registers its own implementation instead.
/// </summary>
internal interface ISmsProvider
{
    /// <summary>Sends one validated message.</summary>
    Task<SmsProviderOutcome> SendAsync(SmsRelayRequest message, CancellationToken cancellationToken);
}

/// <summary>
/// The generic HTTP SMS provider: the request is rendered from <see cref="SmsProviderOptions"/> templates and sent on
/// the Codelab.Integration outbound client <see cref="ClientName"/> (validated TLS, timeouts; a POST is never
/// retried, so a message is never sent twice by the gateway).
/// </summary>
internal sealed class HttpSmsProvider(HttpClient http, IOptions<SmsProviderOptions> options) : ISmsProvider
{
    /// <summary>The name of the outbound client and of its options.</summary>
    public const string ClientName = "gateway-sms-provider";

    public async Task<SmsProviderOutcome> SendAsync(SmsRelayRequest message, CancellationToken cancellationToken)
    {
        using var request = SmsProviderRequest.Build(options.Value, message);
        var result = await UpstreamExchange.SendAsync(http, request, SmsProviderDefaults.MaxResponseBytes, cancellationToken);
        return result.Succeeded
            ? SmsProviderAnswer.Read(options.Value, result)
            : new SmsProviderOutcome(Accepted: false, ProviderMessageId: null, result.Failure);
    }
}

/// <summary>Builds the provider request of one message from the templates.</summary>
internal static class SmsProviderRequest
{
    private const string FormMediaType = GatewayMediaTypes.FormUrlEncoded;

    public static HttpRequestMessage Build(SmsProviderOptions options, SmsRelayRequest message)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(message);
        var values = ValuesOf(options, message);
        var method = options.Method == SmsProviderMethod.Get ? HttpMethod.Get : HttpMethod.Post;
        var request = new HttpRequestMessage(method, SmsTemplate.RenderUrl(options.UrlTemplate!, values));
        if (options.Method == SmsProviderMethod.Post)
        {
            var mediaType = options.BodyFormat == SmsBodyFormat.Json ? GatewayMediaTypes.Json : FormMediaType;
            request.Content = new StringContent(SmsTemplate.RenderBody(options.BodyTemplate!, values, options.BodyFormat), Encoding.UTF8, mediaType);
        }

        if (!string.IsNullOrWhiteSpace(options.AuthHeaderName))
        {
            request.Headers.TryAddWithoutValidation(options.AuthHeaderName, options.AuthHeaderValue);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GatewayMediaTypes.Json));
        return request;
    }

    /// <summary>The placeholder values of <paramref name="message"/> in the provider's terms.</summary>
    internal static SmsTemplateValues ValuesOf(SmsProviderOptions options, SmsRelayRequest message)
    {
        var language = message.Language == GatewayLanguages.Arabic ? options.LanguageCodes.Arabic : options.LanguageCodes.English;
        return new SmsTemplateValues(message.To, message.Text, language, options.Sender ?? string.Empty);
    }
}
