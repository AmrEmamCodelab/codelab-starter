using Codelab.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Starter.Application.Messaging;

namespace Starter.Infrastructure.Messaging;

/// <summary>What happened to one delivery attempt. Stored nowhere; drives the drain's next state.</summary>
public enum OutboxDispatchStatus
{
    /// <summary>The handler returned: the message was delivered.</summary>
    Delivered = 1,

    /// <summary>No handler is registered for the message type.</summary>
    NoHandler = 2,

    /// <summary>The handler threw; <see cref="OutboxDispatchResult.ErrorCode"/> holds the classified code.</summary>
    HandlerFailed = 3,
}

/// <summary>The outcome of one attempt.</summary>
/// <param name="Status">What happened.</param>
/// <param name="ErrorCode">The classified failure code, or null when delivered.</param>
public readonly record struct OutboxDispatchResult(OutboxDispatchStatus Status, string? ErrorCode)
{
    /// <summary>Delivered.</summary>
    public static OutboxDispatchResult Delivered { get; } = new(OutboxDispatchStatus.Delivered, ErrorCode: null);

    /// <summary>No handler.</summary>
    public static OutboxDispatchResult NoHandler { get; } = new(OutboxDispatchStatus.NoHandler, OutboxErrorCodes.NoHandler);
}

/// <summary>
/// Routes one claimed message to the single <see cref="IOutboxHandler"/> registered for its type, in a DI scope of its
/// own (a failed delivery cannot leave a dirty DbContext behind for the next one), and turns the handler's exception
/// into a classified code. The exception is logged with the message id and type, never with the payload.
/// </summary>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopes, IFailureCodeResolver failureCodes, ILogger<OutboxDispatcher> logger)
{
    /// <summary>Delivers <paramref name="delivery"/> once.</summary>
    /// <exception cref="InvalidOperationException">Two handlers are registered for the type (a wiring bug).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<OutboxDispatchResult> DispatchAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        await using var scope = scopes.CreateAsyncScope();
        var handler = SingleHandlerFor(scope.ServiceProvider, delivery.Type);
        if (handler is null)
        {
            LogNoHandler(logger, delivery.MessageId, delivery.Type);
            return OutboxDispatchResult.NoHandler;
        }

        try
        {
            await handler.HandleAsync(delivery, cancellationToken);
            return OutboxDispatchResult.Delivered;
        }
        catch (Exception exception) when (!IsDrainCancellation(exception, cancellationToken))
        {
            var code = failureCodes.Resolve(exception).Code;
            LogHandlerFailed(logger, exception, delivery.MessageId, delivery.Type, delivery.Attempt, code);
            return new OutboxDispatchResult(OutboxDispatchStatus.HandlerFailed, code);
        }
    }

    private static IOutboxHandler? SingleHandlerFor(IServiceProvider services, string messageType)
    {
        var handlers = services.GetServices<IOutboxHandler>()
            .Where(handler => string.Equals(handler.MessageType, messageType, StringComparison.Ordinal))
            .ToList();
        if (handlers.Count > 1)
        {
            throw new InvalidOperationException(
                $"{handlers.Count} outbox handlers are registered for message type '{messageType}' "
                + $"({string.Join(", ", handlers.Select(handler => handler.GetType().Name))}); register exactly one.");
        }

        return handlers.SingleOrDefault();
    }

    /// <summary>The drain itself is stopping: let the cancellation propagate; the lease returns the message later.</summary>
    private static bool IsDrainCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "Outbox message {MessageId} has type {MessageType}, which no handler delivers; it is marked failed")]
    private static partial void LogNoHandler(ILogger logger, long messageId, string messageType);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({MessageType}) attempt {Attempt} failed with {ErrorCode}")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, long messageId, string messageType, int attempt, string errorCode);
}
