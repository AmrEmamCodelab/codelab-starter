using System.Text.Json;
using Starter.Application.Messaging;
using Starter.Application.Notifications;

namespace Starter.Modules.Notifications.Application.Delivery;

/// <summary>
/// The payload of a <see cref="OutboxMessageTypes.NotificationEmail"/> or <see cref="OutboxMessageTypes.NotificationSms"/>
/// outbox message: which notification and which channel. Never an address, a placeholder value or a secret; the
/// handler reads the rest from the notification row.
/// </summary>
/// <param name="NotificationId">The notification's internal id.</param>
/// <param name="Channel">The channel to deliver (<see cref="NotificationChannels.Email"/> or <see cref="NotificationChannels.Sms"/>).</param>
public sealed record DeliveryPayload(long NotificationId, NotificationChannels Channel)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The outbox message type of <paramref name="channel"/>.</summary>
    public static string MessageTypeOf(NotificationChannels channel) => channel switch
    {
        NotificationChannels.Email => OutboxMessageTypes.NotificationEmail,
        NotificationChannels.Sms => OutboxMessageTypes.NotificationSms,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Only e-mail and SMS are delivered through the outbox."),
    };

    /// <summary>The payload as JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The payload of <paramref name="json"/>, or null when it is not one.</summary>
    public static DeliveryPayload? TryRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DeliveryPayload>(json, Json);
        }
        catch (JsonException)
        {
            return null; // load-bearing: a malformed payload is reported by the handler, never retried
        }
    }
}

/// <summary>The placeholder values as stored on a notification while a delivery waits.</summary>
public static class PlaceholderJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The values as JSON.</summary>
    public static string Write(IReadOnlyDictionary<string, string> values) => JsonSerializer.Serialize(values, Json);

    /// <summary>The values of <paramref name="json"/>; empty when null.</summary>
    public static IReadOnlyDictionary<string, string> Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return None;
        }

        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json);
        return values is null ? None : new Dictionary<string, string>(values, StringComparer.Ordinal);
    }
}
