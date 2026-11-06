using System.Globalization;
using Starter.Application.Notifications;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Application;

/// <summary>
/// Sends one escalation notification: the definition's <see cref="SlaDefinition.EscalationTemplateKey"/> to one user,
/// on <see cref="DeadlinesNotifications.EscalationChannels"/>, with the placeholders of
/// <see cref="DeadlinesNotifications"/>.
/// </summary>
public sealed class SlaEscalationNotifier(INotificationService notifications, IRecordWorkflowCatalogs workflows)
{
    /// <summary>Notifies <paramref name="userId"/> of the breach of <paramref name="deadline"/>.</summary>
    /// <remarks>
    /// The Notifications module throws <c>BusinessRuleException</c> when the template or one of its placeholders is
    /// missing (nothing is written).
    /// </remarks>
    public Task NotifyAsync(SlaDefinition definition, RecordDeadline deadline, RecordSummary record, long userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return notifications.NotifyUserAsync(
            new UserNotification(
                UserId: userId,
                TemplateKey: definition.EscalationTemplateKey,
                Placeholders: PlaceholdersFor(deadline, record),
                Channels: DeadlinesNotifications.EscalationChannels,
                Link: null),
            cancellationToken);
    }

    /// <summary>The placeholder values of the escalation of <paramref name="deadline"/>.</summary>
    public IReadOnlyDictionary<string, string> PlaceholdersFor(RecordDeadline deadline, RecordSummary record)
    {
        ArgumentNullException.ThrowIfNull(deadline);
        ArgumentNullException.ThrowIfNull(record);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DeadlinesNotifications.RecordNumber] = record.Number ?? string.Empty,
            [DeadlinesNotifications.StatusKey] = StatusKeyOf(deadline),
            [DeadlinesNotifications.DueAt] = deadline.DueAt.ToUniversalTime().ToString(DeadlinesNotifications.DueAtFormat, CultureInfo.InvariantCulture),
        };
    }

    private string StatusKeyOf(RecordDeadline deadline) =>
        workflows.Find(deadline.RecordType)?.FindStatus(deadline.Status)?.Key
        ?? deadline.Status.ToString(CultureInfo.InvariantCulture);
}
