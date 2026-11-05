using Starter.Application.Messaging;

namespace Starter.Infrastructure.Messaging;

/// <summary>
/// The <see cref="IOutbox"/> on <see cref="OutboxDbContext"/>: each enqueue adds one Pending row and saves it at once,
/// in the outbox's own context (not in the caller's transaction; see the port's remarks).
/// </summary>
public sealed class EfOutbox(OutboxDbContext context, TimeProvider timeProvider) : IOutbox
{
    /// <inheritdoc />
    public async Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        Validate(message);
        context.Messages.Add(OutboxEntry.Pending(message, timeProvider.GetUtcNow()));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Refuses a message the table cannot hold or the drain could not route.</summary>
    /// <exception cref="ArgumentException">The type is blank or too long, or the payload is blank.</exception>
    public static void Validate(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Type, nameof(message));
        ArgumentException.ThrowIfNullOrWhiteSpace(message.PayloadJson, nameof(message));
        if (message.Type.Length > OutboxLimits.TypeMaxLength)
        {
            throw new ArgumentException($"The outbox message type is longer than {OutboxLimits.TypeMaxLength} characters.", nameof(message));
        }
    }
}
