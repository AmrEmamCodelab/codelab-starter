using System.Reflection;
using Starter.Application.Notifications;
using Starter.Application.Lookups;
using Starter.Application.Numbering;
using Starter.Application.Settings;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Application.Content;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Tests.Fakes;

/// <summary>The order in which the fakes were called (store-then-notify proofs).</summary>
public sealed class CallJournal
{
    public List<string> Calls { get; } = [];

    public void Record(string call) => Calls.Add(call);
}

public static class JournalCalls
{
    public const string Allocate = "allocate";
    public const string Save = "save";
    public const string Notify = "notify";
}

/// <summary>The runtime settings the Portal module reads; only the contact switch is set, every other key throws.</summary>
public sealed class FakeRuntimeSettings : IRuntimeSettings
{
    public bool ContactEnabled { get; set; } = true;

    public List<string> Read { get; } = [];

    public bool GetBoolean(string key)
    {
        Read.Add(key);
        return key == SettingKeys.PortalContactEnabled
            ? ContactEnabled
            : throw new InvalidOperationException($"No boolean setting '{key}'.");
    }

    public string GetString(string key) => throw new InvalidOperationException($"No text setting '{key}'.");

    public int GetInt32(string key) => throw new InvalidOperationException($"No integer setting '{key}'.");

    public TimeSpan GetDuration(string key) => throw new InvalidOperationException($"No duration setting '{key}'.");
}

/// <summary>The kernel lookup port: the values a test registers by (type, id); anything else is null.</summary>
public sealed class FakeLookupValues : ILookupValues
{
    public Dictionary<(string TypeKey, int Id), ActiveLookupValue> Values { get; } = [];

    public List<(string TypeKey, int Id)> Asked { get; } = [];

    public Task<ActiveLookupValue?> FindActiveAsync(string typeKey, int id, CancellationToken cancellationToken)
    {
        Asked.Add((typeKey, id));
        return Task.FromResult(Values.GetValueOrDefault((typeKey, id)));
    }
}

public sealed class FakeContactTopics : IContactTopics
{
    public Dictionary<int, ContactTopic> Active { get; } = new()
    {
        [1] = new ContactTopic(1, "استفسار عام", "General question"),
        [2] = new ContactTopic(2, "طلب قدّمته", "A request I submitted"),
    };

    public List<int> Asked { get; } = [];

    public Task<ContactTopic?> FindActiveAsync(int topicId, CancellationToken cancellationToken)
    {
        Asked.Add(topicId);
        return Task.FromResult(Active.GetValueOrDefault(topicId));
    }
}

public sealed class FakeNumberAllocator(CallJournal journal) : IRequestNumberAllocator
{
    private int _next = 1;

    public List<string> Prefixes { get; } = [];

    public Task<string> NextAsync(string prefix, CancellationToken cancellationToken)
    {
        journal.Record(JournalCalls.Allocate);
        Prefixes.Add(prefix);
        return Task.FromResult(RequestNumberFormat.Format(prefix, 2026, _next++));
    }
}

public sealed class FakeContactMessageStore(CallJournal journal) : IContactMessageStore
{
    private readonly List<ContactMessage> _staged = [];

    public List<ContactMessage> Saved { get; } = [];

    public Exception? FailSaveWith { get; set; }

    public void Add(ContactMessage message) => _staged.Add(message);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        journal.Record(JournalCalls.Save);
        if (FailSaveWith is not null)
        {
            throw FailSaveWith;
        }

        Saved.AddRange(_staged);
        _staged.Clear();
        return Task.CompletedTask;
    }
}

public sealed class FakeNotificationService(CallJournal journal) : INotificationService
{
    public List<AddressedNotification> Addressed { get; } = [];

    public Exception? FailWith { get; set; }

    public Task NotifyUserAsync(UserNotification notification, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Portal module notifies an address, never a user.");

    public Task SendToAddressAsync(AddressedNotification notification, CancellationToken cancellationToken)
    {
        journal.Record(JournalCalls.Notify);
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Addressed.Add(notification);
        return Task.CompletedTask;
    }

    public Task SendImmediateEmailAsync(UserNotification notification, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Portal module never sends an immediate e-mail.");
}

/// <summary>An in-memory content store that assigns ids on save, like the database.</summary>
public sealed class FakePortalContentStore : IPortalContentStore
{
    private long _nextId = 100;

    public Dictionary<PortalPage, ContentPage> Pages { get; } = PortalPages.All.ToDictionary(page => page, ContentPage.Seeded);

    public List<ContentSection> Sections { get; } = [];

    public List<ContentSection> Staged { get; } = [];

    public List<ContentSection> Removed { get; } = [];

    public int Saves { get; private set; }

    public int Reads { get; private set; }

    public ContentSection Seed(PortalPage page, SectionText text)
    {
        var section = ContentSection.Create(page, text);
        SetId(section, _nextId++);
        Sections.Add(section);
        return section;
    }

    public Task<ContentPage?> FindPageAsync(PortalPage page, CancellationToken cancellationToken) =>
        Task.FromResult(Pages.GetValueOrDefault(page));

    public Task<IReadOnlyList<ContentSection>> ListSectionsAsync(PortalPage page, bool forUpdate, CancellationToken cancellationToken)
    {
        Reads++;
        IReadOnlyList<ContentSection> sections = [.. Sections.Where(section => section.Page == page).OrderBy(section => section.SortOrder).ThenBy(section => section.Id)];
        return Task.FromResult(sections);
    }

    public void Add(ContentSection section) => Staged.Add(section);

    public void Remove(ContentSection section) => Removed.Add(section);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        foreach (var section in Staged)
        {
            SetId(section, _nextId++);
            Sections.Add(section);
        }

        Staged.Clear();
        Removed.ForEach(section => Sections.Remove(section));
        Removed.Clear();
        return Task.CompletedTask;
    }

    public static void SetId(ContentSection section, long id) =>
        typeof(ContentSection).GetProperty(nameof(ContentSection.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(section, id);
}

public sealed class FakeContentCache : IPortalContentCache
{
    public Dictionary<PortalPage, PortalContentView> Entries { get; } = [];

    public List<PortalPage> Invalidated { get; } = [];

    public PortalContentView? Find(PortalPage page) => Entries.GetValueOrDefault(page);

    public void Store(PortalContentView content) => Entries[content.Page] = content;

    public void Invalidate(PortalPage page)
    {
        Invalidated.Add(page);
        Entries.Remove(page);
    }
}

/// <summary>Collects log entries (level and message) without a logging framework.</summary>
public sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));
}
