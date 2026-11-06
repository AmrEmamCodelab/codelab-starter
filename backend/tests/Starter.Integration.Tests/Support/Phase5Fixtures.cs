using System.Collections.Concurrent;
using System.Text.Json;
using Codelab.Testing.Logging;
using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Domain.Localization;

namespace Starter.Integration.Tests.Support;

/// <summary>
/// The planted personal data of the PII-leak canary: unique values a test types into the inputs of the host, which
/// must never reach a retained log sink or an unmasked audit row.
/// </summary>
/// <param name="NationalId">A fake national id.</param>
/// <param name="EmailLocalPart">The unique local part of <see cref="Email"/> (what masking removes).</param>
/// <param name="Phone">A fake mobile number.</param>
/// <param name="Name">A fake person's name.</param>
public sealed record PiiCanaries(string NationalId, string EmailLocalPart, string Phone, string Name)
{
    /// <summary>The domain of the canary e-mail (reserved, never delivered).</summary>
    public const string EmailDomain = "example.test";

    /// <summary>The canary e-mail address.</summary>
    public string Email => $"{EmailLocalPart}@{EmailDomain}";

    /// <summary>Every canary value to look for.</summary>
    public IReadOnlyList<string> All => [NationalId, EmailLocalPart, Phone, Name];

    /// <summary>A fresh set of canaries.</summary>
    public static PiiCanaries Generate() => new(
        NationalId: CanaryLogSink.NewCanary("nid"),
        EmailLocalPart: CanaryLogSink.NewCanary("email"),
        Phone: CanaryLogSink.NewCanary("phone"),
        Name: CanaryLogSink.NewCanary("name"));
}

/// <summary>What a test makes the notification recipients directory answer for every known user.</summary>
/// <param name="Email">The e-mail address.</param>
/// <param name="Mobile">The mobile number.</param>
/// <param name="Language">The preferred language.</param>
public sealed record RecipientOverride(string? Email, string? Mobile, AppLanguage Language);

/// <summary>
/// The test switch over the recipients directory. Accounts store no mobile number or preferred language yet, so a test
/// that needs an SMS, or a recipient in English, sets an override for the duration of the test (null: the host's own
/// answer).
/// </summary>
public sealed class RecipientOverrides
{
    private RecipientOverride? _current;

    /// <summary>The override in force, or null.</summary>
    public RecipientOverride? Current => Volatile.Read(ref _current);

    /// <summary>Puts <paramref name="value"/> in force until the result is disposed.</summary>
    public IDisposable Use(RecipientOverride value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Volatile.Write(ref _current, value);
        return new Clear(this);
    }

    private sealed class Clear(RecipientOverrides owner) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref owner._current, null);
    }
}

/// <summary>The host's recipients directory with <see cref="RecipientOverrides"/> applied to every user it knows.</summary>
public sealed class OverridingRecipients(INotificationRecipients inner, RecipientOverrides overrides) : INotificationRecipients
{
    /// <inheritdoc />
    public async Task<NotificationRecipient?> FindAsync(long userId, CancellationToken cancellationToken)
    {
        var recipient = await inner.FindAsync(userId, cancellationToken);
        return recipient is null || overrides.Current is not { } value
            ? recipient
            : recipient with { Email = value.Email, Mobile = value.Mobile, Language = value.Language };
    }
}

/// <summary>The outbox message types of the probe handlers (test-only types; no product code sends them).</summary>
public static class ProbeMessageTypes
{
    /// <summary>Handled by <see cref="IdempotentProbeHandler"/>.</summary>
    public const string Idempotent = "it-probe-idempotent";

    /// <summary>Handled by <see cref="FailingProbeHandler"/>, which always throws.</summary>
    public const string Failing = "it-probe-failing";
}

/// <summary>The payload of a probe message: a run-unique marker, so a test sees only its own deliveries.</summary>
/// <param name="Probe">The marker.</param>
public sealed record ProbePayload(string Probe)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The JSON form stored in the outbox.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads a stored payload.</summary>
    public static ProbePayload FromJson(string json) => JsonSerializer.Deserialize<ProbePayload>(json, Json) ?? throw new JsonException("An empty probe payload.");
}

/// <summary>What the probe handlers saw: every delivery (attempts) and the effects applied once per message id.</summary>
public sealed class ProbeLedger
{
    private readonly ConcurrentQueue<OutboxDelivery> _deliveries = new();
    private readonly ConcurrentDictionary<long, string> _effects = new();

    /// <summary>Records a delivery; applies its effect only the first time its message id is seen (the idempotency rule).</summary>
    public void Deliver(OutboxDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        _deliveries.Enqueue(delivery);
        _effects.TryAdd(delivery.MessageId, ProbePayload.FromJson(delivery.PayloadJson).Probe);
    }

    /// <summary>Records a delivery attempt without an effect.</summary>
    public void Attempt(OutboxDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        _deliveries.Enqueue(delivery);
    }

    /// <summary>Every delivery of the probe <paramref name="probe"/>, in order.</summary>
    public IReadOnlyList<OutboxDelivery> DeliveriesOf(string probe) =>
        _deliveries.Where(delivery => ProbePayload.FromJson(delivery.PayloadJson).Probe == probe).ToList();

    /// <summary>How many effects the probe <paramref name="probe"/> produced.</summary>
    public int EffectsOf(string probe) => _effects.Values.Count(value => value == probe);
}

/// <summary>An idempotent outbox handler: its effect happens once per message id, however often it is delivered.</summary>
public sealed class IdempotentProbeHandler(ProbeLedger ledger) : IOutboxHandler
{
    /// <inheritdoc />
    public string MessageType => ProbeMessageTypes.Idempotent;

    /// <inheritdoc />
    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ledger.Deliver(delivery);
        return Task.CompletedTask;
    }
}

/// <summary>An outbox handler that always fails, with a message that must never be stored.</summary>
public sealed class FailingProbeHandler(ProbeLedger ledger) : IOutboxHandler
{
    /// <summary>The text of every failure (the store keeps only a classified code, never this).</summary>
    public const string FailureText = "probe handler failure: internal detail that must not be stored";

    /// <inheritdoc />
    public string MessageType => ProbeMessageTypes.Failing;

    /// <inheritdoc />
    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ledger.Attempt(delivery);
        throw new InvalidOperationException(FailureText);
    }
}
