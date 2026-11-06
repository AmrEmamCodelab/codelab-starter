using Starter.Application.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Localization;
using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Application.Users;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Application.Delivery;
using Starter.Modules.Notifications.Application.Feed;
using Starter.Modules.Notifications.Application.Rendering;
using Starter.Modules.Notifications.Domain;
using Starter.Modules.Notifications.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.Notifications.Tests.Fakes;

public sealed class FakeTemplateStore(IEnumerable<TemplateText> texts) : ITemplateStore
{
    public FakeTemplateStore()
        : this(TemplateSeed.Rows)
    {
    }

    public List<NotificationTemplate> Texts { get; } = texts.Select(NotificationTemplate.Define).ToList();

    public Task<IReadOnlyList<NotificationTemplate>> ListAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationTemplate>>(Texts.Where(text => text.Key == key).ToList());
}

public sealed class FakeNotificationStore : INotificationStore
{
    private readonly List<Notification> _staged = [];
    private long _nextId = 1;

    public List<Notification> Saved { get; } = [];

    public int SaveCount { get; private set; }

    public void Add(Notification notification) => _staged.Add(notification);

    public Task<Notification?> FindAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.FirstOrDefault(notification => notification.Id == id));

    public Task<Notification?> FindInAppAsync(FeedOwner owner, long id, CancellationToken cancellationToken) =>
        Task.FromResult(InApp(owner).FirstOrDefault(notification => notification.Id == id));

    public Task<IReadOnlyList<Notification>> ListInAppAsync(FeedQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Notification>>(InApp(query.Owner)
            .Where(notification => query.BeforeId is not { } before || notification.Id < before)
            .OrderByDescending(notification => notification.Id)
            .Take(query.Take)
            .ToList());

    public Task<int> CountUnreadAsync(FeedOwner owner, CancellationToken cancellationToken) =>
        Task.FromResult(InApp(owner).Count(notification => notification.IsUnread));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var notification in _staged)
        {
            typeof(Notification).GetProperty(nameof(Notification.Id))!.SetValue(notification, _nextId++);
            Saved.Add(notification);
        }

        _staged.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }

    private IEnumerable<Notification> InApp(FeedOwner owner) =>
        Saved.Where(notification => notification.UserId == owner.UserId && notification.IsInApp);
}

public sealed class FakeOutbox : IOutbox
{
    public List<OutboxMessage> Messages { get; } = [];

    public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public Exception? FailWith { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class FakeSmsSender : ISmsSender
{
    public List<SmsMessage> Sent { get; } = [];

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>A fake of the Identity-owned INotificationRecipients port.</summary>
public sealed class FakeRecipients : INotificationRecipients
{
    public Dictionary<long, NotificationRecipient> Users { get; } = [];

    public Task<NotificationRecipient?> FindAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(Users.GetValueOrDefault(userId));
}

public sealed class FakeCurrentUser : ICurrentUserContext
{
    public Guid? SubjectId { get; set; }

    public bool IsAuthenticated => SubjectId is not null;

    public string? SessionId => null;
}

public sealed class FakeUserDirectory : IUserDirectory
{
    public Dictionary<Guid, long> Users { get; } = [];

    public Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken) =>
        Task.FromResult(Users.TryGetValue(subjectId, out var id) ? new DirectoryUser(id, IsActive: true, HoldsAllPermissions: false) : null);
}

public sealed class FakeLanguage : ICurrentLanguage
{
    public AppLanguage Language { get; set; } = AppLanguage.Arabic;
}

/// <summary>Everything a module test needs, wired the way the registration wires it.</summary>
/// <summary>The runtime settings the module reads; only the feed page size is set, every other key throws.</summary>
public sealed class FakeRuntimeSettings : IRuntimeSettings
{
    public int FeedPageSize { get; set; } = 20;

    public int GetInt32(string key) =>
        key == SettingKeys.NotificationsPageSize ? FeedPageSize : throw new InvalidOperationException($"No integer setting '{key}'.");

    public string GetString(string key) => throw new InvalidOperationException($"No text setting '{key}'.");

    public bool GetBoolean(string key) => throw new InvalidOperationException($"No boolean setting '{key}'.");

    public TimeSpan GetDuration(string key) => throw new InvalidOperationException($"No duration setting '{key}'.");
}

public sealed class NotificationsHarness
{
    public const long UserId = 42;
    public const string UserEmail = "user@example.test";
    public const string UserMobile = "+10000000001";

    public NotificationsHarness(bool smsEnabled = true)
    {
        Settings = new DeliverySettings(smsEnabled);
        Recipients.Users[UserId] = new NotificationRecipient(UserId, UserEmail, UserMobile, AppLanguage.English);
        Directory.Users[Subject] = UserId;
        CurrentUser.SubjectId = Subject;
        Composer = new NotificationComposer(Templates);
    }

    public Guid Subject { get; } = Guid.NewGuid();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 6, 9, 30, 0, TimeSpan.Zero));

    public FakeTemplateStore Templates { get; } = new();

    public FakeNotificationStore Store { get; } = new();

    public FakeOutbox Outbox { get; } = new();

    public FakeEmailSender Email { get; } = new();

    public FakeSmsSender Sms { get; } = new();

    public FakeRecipients Recipients { get; } = new();

    public FakeRuntimeSettings RuntimeSettings { get; } = new();

    public FakeCurrentUser CurrentUser { get; } = new();

    public FakeUserDirectory Directory { get; } = new();

    public FakeLanguage Language { get; } = new();

    public DeliverySettings Settings { get; }

    public NotificationComposer Composer { get; }

    public NotificationWriter Writer() => new(Composer, Store, Outbox, Settings, Clock);

    public NotificationService Service() => new(Writer(), new ImmediateEmails(Composer, Recipients, new EmailChannel(Email)));

    public NotificationDeliveries Deliveries() =>
        new(Store, Recipients, Composer, Clock, NullLogger<NotificationDeliveries>.Instance);

    public NotificationEmailHandler EmailHandler() => new(Deliveries(), new EmailChannel(Email));

    public NotificationSmsHandler SmsHandler() => new(Deliveries(), new SmsChannel(Sms));

    public CurrentFeedOwner Owners() => new(CurrentUser, Directory);

    public static Dictionary<string, string> SentAt(string value) =>
        new(StringComparer.Ordinal) { [NotificationPlaceholders.SentAt] = value };

    public static UserNotification Test(NotificationChannels channels, string sentAt = "2026-11-06 09:30 UTC") =>
        new(UserId, NotificationTemplateKeys.TestNotification, SentAt(sentAt), channels, Link: null);

    public static OutboxDelivery DeliveryOf(OutboxMessage message, long messageId = 1) =>
        new(messageId, message.Type, message.PayloadJson, Attempt: 1);
}
