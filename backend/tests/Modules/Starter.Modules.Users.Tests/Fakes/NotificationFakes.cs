using Microsoft.Extensions.Logging;
using Starter.Application.Notifications;
using Starter.Domain.Exceptions;

namespace Starter.Modules.Users.Tests.Fakes;

/// <summary>Records the immediate e-mails; queued notifications are not expected from the Users module.</summary>
internal sealed class FakeNotifications : INotificationService
{
    public const string SendFailureCode = "NOTIFICATIONS_SEND_FAILED";

    public List<UserNotification> ImmediateEmails { get; } = [];

    public bool FailNextSend { get; set; }

    public Task NotifyUserAsync(UserNotification notification, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Users module never queues a user notification.");

    public Task SendToAddressAsync(AddressedNotification notification, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Users module never sends to an address.");

    public Task SendImmediateEmailAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        if (FailNextSend)
        {
            FailNextSend = false;
            throw new BusinessRuleException(SendFailureCode, "The relay is down.");
        }

        ImmediateEmails.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>Keeps every formatted log line, so tests can prove no secret is logged.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Lines.Add((logLevel, formatter(state, exception)));
    }
}
