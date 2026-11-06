namespace Starter.Modules.Notifications.Domain;

/// <summary>The delivery state of one outbox channel (e-mail or SMS) of a notification. Stored as the integer value.</summary>
public enum ChannelDeliveryState
{
    /// <summary>The channel was not asked for.</summary>
    NotRequested = 1,

    /// <summary>Queued in the outbox; not delivered yet.</summary>
    Pending = 2,

    /// <summary>Handed to the sender.</summary>
    Sent = 3,

    /// <summary>Skipped at delivery: the recipient is unknown or has no address for this channel.</summary>
    NoAddress = 4,

    /// <summary>Skipped when sent: the channel is switched off (SMS while <c>Notifications:Sms:Enabled</c> is false).</summary>
    Disabled = 5,
}
