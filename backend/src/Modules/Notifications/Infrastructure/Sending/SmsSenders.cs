using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
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

/// <summary>
/// The production SMS adapter (<see cref="SmsDeliveryMode.Gateway"/>): a documented STUB until phase 8. Then it calls the
/// integration gateway's SMS relay profile through Codelab.Integration with the ES256 service-to-service assertion (the
/// backend zone has no internet egress, decision D2). Start-up validation refuses this mode while SMS is enabled, so it
/// is never reached; if it is, it throws.
/// </summary>
public sealed class GatewaySmsSender : ISmsSender
{
    /// <inheritdoc />
    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The integration gateway SMS relay arrives in phase 8; use Notifications:Sms:Mode 1 (File).");
}
