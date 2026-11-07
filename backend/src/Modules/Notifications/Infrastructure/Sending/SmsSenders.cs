using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Codelab.Integration.Gateway; // codelab:module Sms
using Microsoft.Extensions.Logging; // codelab:module Sms
using Microsoft.Extensions.Options;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Infrastructure.Options;

namespace Starter.Modules.Notifications.Infrastructure.Sending;

/// <summary>One line of the SMS file.</summary>
/// <param name="At">When it was written (UTC).</param>
/// <param name="To">The mobile number.</param>
/// <param name="Language">The language of the text (the integer value: 1 Arabic, 2 English).</param>
/// <param name="Text">The text.</param>
public sealed record SmsFileRecord(DateTimeOffset At, string To, int Language, string Text);

/// <summary>
/// The development and CI SMS sink (<see cref="SmsDeliveryMode.File"/>): appends each message as one JSON line (UTF-8,
/// Arabic kept readable) to the SMS file. Writes are serialized within the process. Never used in production: the file
/// holds mobile numbers and texts in clear.
/// </summary>
public sealed class FileSmsSender(IOptions<SmsDeliveryOptions> options, TimeProvider timeProvider) : ISmsSender, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    /// <inheritdoc />
    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var path = options.Value.ResolvedFilePath;
        var record = new SmsFileRecord(timeProvider.GetUtcNow(), message.To, (int)message.Language, message.Text);
        var line = JsonSerializer.Serialize(record, Json) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.AppendAllTextAsync(path, line, Utf8NoBom, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
// codelab:module-begin Sms

/// <summary>
/// The production SMS adapter (<see cref="SmsDeliveryMode.Gateway"/>): relays each message to the integration gateway's
/// SMS relay profile (POST <c>/relay/sms</c>) through <see cref="GatewayRelayClient"/>, signed with this API's ES256
/// service assertion; the gateway in the DMZ calls the client's SMS provider (the backend zone has no internet egress,
/// decision D2). Returns only when the provider accepted the message. Every failure is a
/// <see cref="SmsDeliveryException"/> whose <see cref="SmsDeliveryException.IsRetryable"/> says whether repeating the
/// same message can succeed (<see cref="SmsRelayFailures"/>); the outbox retries it with its back-off. The relay is
/// never retried inside one attempt: the provider may not de-duplicate.
/// </summary>
/// <remarks>Scoped, like the typed client it holds. Logs carry the gateway's reference code and the provider's id, never the number or the text.</remarks>
public sealed partial class GatewaySmsSender(GatewayRelayClient client, ILogger<GatewaySmsSender> logger) : ISmsSender
{
    /// <inheritdoc />
    /// <exception cref="SmsDeliveryException">The relay failed or the provider did not accept the message.</exception>
    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = SmsRelayFailures.RequestOf(message);
        if (SmsRelayRequestValidator.Validate(request).Count > 0)
        {
            throw SmsRelayFailures.Invalid();
        }

        SmsRelayResult result;
        try
        {
            result = await client.RelaySmsAsync(request, cancellationToken);
        }
        catch (GatewayRelayException failure)
        {
            throw SmsRelayFailures.From(failure);
        }

        if (!result.Accepted)
        {
            throw SmsRelayFailures.NotAccepted(result.ReferenceCode);
        }

        LogRelayed(logger, result.ReferenceCode, result.ProviderMessageId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SMS relayed through the integration gateway (gateway reference {ReferenceCode}, provider message {ProviderMessageId}).")]
    private static partial void LogRelayed(ILogger logger, string? referenceCode, string? providerMessageId);
}
// codelab:module-end Sms
