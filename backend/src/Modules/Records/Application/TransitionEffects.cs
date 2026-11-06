using Microsoft.Extensions.Logging;
using Starter.Application.Notifications;
using Starter.Application.Records;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Application;

/// <summary>A transition that was saved, with what its side effects need.</summary>
/// <param name="Record">The record after the transition.</param>
/// <param name="Rule">The rule that was applied.</param>
/// <param name="FromStatus">The status before.</param>
/// <param name="Reason">The recorded reason, or null.</param>
/// <param name="Actor">Who took it.</param>
/// <param name="Type">The record type.</param>
/// <param name="At">When it was saved.</param>
public sealed record CommittedTransition(
    Record Record, TransitionRule Rule, int FromStatus, string? Reason, RecordCaller Actor, RegisteredRecordType Type, DateTimeOffset At);

/// <summary>The placeholder names a record transition template may use (<c>{{name}}</c>; case-sensitive).</summary>
public static class RecordNotificationPlaceholders
{
    /// <summary>The record's reference number (absent while the record has none).</summary>
    public const string RecordNumber = "recordNumber";

    /// <summary>The kebab key of the status the record moved to.</summary>
    public const string StatusKey = "statusKey";

    /// <summary>The action key that was taken.</summary>
    public const string ActionKey = "actionKey";

    /// <summary>The reason the actor gave (absent when none).</summary>
    public const string Reason = "reason";
}

/// <summary>
/// What happens after a transition is saved, in the same request: every <see cref="IRecordTransitionObserver"/> (zero or
/// more), then the rule's notification to the requester and the current assignee as its
/// <see cref="NotificationDecision"/> says. Each observer and each notification is isolated (ERROR-HANDLING row 8): a
/// failure is logged with identifiers and never undoes or fails the committed transition.
/// </summary>
public sealed class TransitionEffects(
    IEnumerable<IRecordTransitionObserver> observers,
    IEnumerable<IRecordAssignmentReader> assignments,
    INotificationService notifications,
    ILogger<TransitionEffects> logger)
{
    /// <summary>The channels a transition notification goes to (each template has an in-app and an e-mail text).</summary>
    public const NotificationChannels Channels = NotificationChannels.InApp | NotificationChannels.Email;

    /// <summary>Runs the observers and sends the notification of <paramref name="transition"/>.</summary>
    public async Task AfterCommitAsync(CommittedTransition transition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var recipients = await RecipientsAsync(transition, cancellationToken);
        var happened = ToEvent(transition);
        foreach (var observer in observers)
        {
            await NotifyObserverAsync(observer, happened, cancellationToken);
        }

        foreach (var userId in recipients)
        {
            await NotifyUserAsync(transition, userId, cancellationToken);
        }
    }

    /// <summary>
    /// Who the rule's decision names: the requester and, when asked and assigned, the assignee (read BEFORE the observers
    /// run, so an observer that closes the assignment on a terminal status does not hide its holder). Distinct ids.
    /// </summary>
    public async Task<IReadOnlyList<long>> RecipientsAsync(CommittedTransition transition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var decision = transition.Rule.Notification;
        if (decision?.TemplateKey is null || !decision.NotifiesAnyone)
        {
            return [];
        }

        var recipients = new List<long>();
        if (decision.NotifyRequester)
        {
            recipients.Add(transition.Record.OwnerUserId);
        }

        if (decision.NotifyAssignee)
        {
            recipients.AddRange(await AssigneesAsync(transition.Record.Id, cancellationToken));
        }

        return [.. recipients.Distinct()];
    }

    /// <summary>The placeholder values of the notification of <paramref name="transition"/>.</summary>
    public static IReadOnlyDictionary<string, string> PlaceholdersOf(CommittedTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RecordNotificationPlaceholders.StatusKey] = transition.Type.StatusKeyOf(transition.Rule.ToStatus),
            [RecordNotificationPlaceholders.ActionKey] = transition.Rule.ActionKey,
        };
        if (transition.Record.Number is { } number)
        {
            values[RecordNotificationPlaceholders.RecordNumber] = number;
        }

        if (transition.Reason is { } reason)
        {
            values[RecordNotificationPlaceholders.Reason] = reason;
        }

        return values;
    }

    private static RecordTransitioned ToEvent(CommittedTransition transition) => new(
        transition.Record.Id,
        transition.Type.RecordType,
        FromStatus: transition.FromStatus,
        ToStatus: transition.Rule.ToStatus,
        transition.Rule.ActionKey,
        transition.Actor.UserId,
        ToTerminal: transition.Type.Catalog.IsTerminal(transition.Rule.ToStatus),
        transition.At);

    private async Task<IEnumerable<long>> AssigneesAsync(long recordId, CancellationToken cancellationToken)
    {
        var assignees = new List<long>();
        foreach (var reader in assignments)
        {
            var current = await reader.CurrentAsync([recordId], cancellationToken);
            if (current.TryGetValue(recordId, out var snapshot))
            {
                assignees.Add(snapshot.AssigneeUserId);
            }
        }

        return assignees;
    }

    private async Task NotifyObserverAsync(IRecordTransitionObserver observer, RecordTransitioned happened, CancellationToken cancellationToken)
    {
        try
        {
            await observer.OnTransitionedAsync(happened, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RecordsLog.ObserverFailed(logger, exception, observer.GetType().Name, happened.RecordId, happened.ActionKey);
        }
    }

    private async Task NotifyUserAsync(CommittedTransition transition, long userId, CancellationToken cancellationToken)
    {
        var templateKey = transition.Rule.Notification!.TemplateKey!;
        try
        {
            await notifications.NotifyUserAsync(
                new UserNotification(userId, templateKey, PlaceholdersOf(transition), Channels, Link: null),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RecordsLog.NotificationFailed(logger, exception, templateKey, transition.Record.Id, userId);
        }
    }
}

/// <summary>The module's log lines (event ids 7001-7099; internal ids and keys only, never names, numbers or reasons).</summary>
internal static partial class RecordsLog
{
    [LoggerMessage(EventId = 7001, Level = LogLevel.Information, Message = "Record {RecordId} ({RecordType}) moved {FromStatus} -> {ToStatus} by action {ActionKey} of user {UserId}")]
    public static partial void Transitioned(ILogger logger, long recordId, string recordType, int fromStatus, int toStatus, string actionKey, long userId);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Error, Message = "Transition observer {Observer} failed for record {RecordId} after action {ActionKey}; the transition stays saved")]
    public static partial void ObserverFailed(ILogger logger, Exception exception, string observer, long recordId, string actionKey);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Error, Message = "Notification {TemplateKey} for record {RecordId} to user {UserId} failed; the transition stays saved")]
    public static partial void NotificationFailed(ILogger logger, Exception exception, string templateKey, long recordId, long userId);
}
