using Starter.Application.Settings;
using Starter.Application.Users;
using Starter.Modules.Settings.Application;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Tests.Fakes;

/// <summary>Sample settings, one per value type.</summary>
internal static class SampleSettings
{
    public const string TextKey = "portal.banner-text";
    public const string IntegerKey = "notifications.page-size";
    public const string BooleanKey = "portal.contact-enabled";
    public const string DurationKey = "notifications.retry-delay";

    public static Setting Text() => Setting.Define(new SettingDefinition(
        TextKey, "portal", SettingValueType.Text, "Welcome", "نص", "Text", SettingRange.None));

    public static Setting Integer() => Setting.Define(new SettingDefinition(
        IntegerKey, "notifications", SettingValueType.WholeNumber, "20", "حجم", "Size", new SettingRange(5, 100)));

    public static Setting Boolean() => Setting.Define(new SettingDefinition(
        BooleanKey, "portal", SettingValueType.Boolean, "true", "تفعيل", "Enabled", SettingRange.None));

    public static Setting Duration() => Setting.Define(new SettingDefinition(
        DurationKey, "notifications", SettingValueType.DurationSeconds, "300", "مدة", "Delay", new SettingRange(30, 86400)));

    public static IReadOnlyList<Setting> All() => [Text(), Integer(), Boolean(), Duration()];
}

/// <summary>
/// One shared "database" for the reader tests: the settings, the stamp, and how many queries each node ran. Two
/// readers with two caches over one store behave like two hosts over one database.
/// </summary>
internal sealed class FakeSettingsStore : ISettingsSnapshotSource
{
    private readonly Dictionary<string, StoredSetting> _values = new(StringComparer.Ordinal);

    public FakeSettingsStore()
    {
        foreach (var setting in SampleSettings.All())
        {
            _values[setting.Key] = new StoredSetting(setting.ValueType, setting.Value);
        }
    }

    public long Version { get; private set; } = SettingsStamp.InitialVersion;

    public int VersionReads { get; private set; }

    public int Loads { get; private set; }

    /// <summary>A change committed by "another node": new value and a new stamp version.</summary>
    public void Change(string key, string value)
    {
        _values[key] = _values[key] with { Value = value };
        Version++;
    }

    /// <summary>Writes a value WITHOUT advancing the stamp (only the TTL can surface it).</summary>
    public void ChangeWithoutStamp(string key, string value) => _values[key] = _values[key] with { Value = value };

    public long ReadVersion()
    {
        VersionReads++;
        return Version;
    }

    public StoredSettings Load()
    {
        Loads++;
        return new StoredSettings(Version, new Dictionary<string, StoredSetting>(_values, StringComparer.Ordinal));
    }
}

internal sealed class FakeSettingsRepository(params Setting[] settings) : ISettingsRepository
{
    public List<Setting> Settings { get; } = [.. settings];

    public Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Setting>>([.. Settings.OrderBy(setting => setting.Group, StringComparer.Ordinal).ThenBy(setting => setting.Key, StringComparer.Ordinal)]);

    public Task<Setting?> FindAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Settings.FirstOrDefault(setting => string.Equals(setting.Key, key, StringComparison.Ordinal)));
}

internal sealed class FakeSettingsUnitOfWork : ISettingsUnitOfWork
{
    public int StampAdvances { get; private set; }

    public Task SaveChangesAndAdvanceStampAsync(CancellationToken cancellationToken)
    {
        StampAdvances++;
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}

internal sealed class FakeCurrentUser(Guid? subjectId) : ICurrentUserContext
{
    public bool IsAuthenticated => SubjectId is not null;

    public Guid? SubjectId { get; } = subjectId;

    public string? SessionId => null;
}

internal sealed class FakeUserDirectory : IUserDirectory
{
    private readonly Dictionary<Guid, DirectoryUser> _bySubject = [];

    public FakeUserDirectory Add(Guid subjectId, long userId)
    {
        _bySubject[subjectId] = new DirectoryUser(userId, IsActive: true, HoldsAllPermissions: false);
        return this;
    }

    public Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken) =>
        Task.FromResult(_bySubject.GetValueOrDefault(subjectId));
}
