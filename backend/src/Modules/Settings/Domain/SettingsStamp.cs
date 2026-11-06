namespace Starter.Modules.Settings.Domain;

/// <summary>
/// The single-row version stamp of all settings (<c>settings.SettingsStamp</c>). Every settings change increments
/// <see cref="Version"/> in the same transaction; each node compares it with the version of its cached snapshot.
/// </summary>
public sealed class SettingsStamp
{
    /// <summary>The id of the only row (seeded by the InitialCreate migration).</summary>
    public const int SingletonId = 1;

    /// <summary>The version of the seeded row.</summary>
    public const long InitialVersion = 1;

    private SettingsStamp()
    {
    }

    /// <summary>Always <see cref="SingletonId"/>.</summary>
    public int Id { get; private set; }

    /// <summary>Increases by one with every settings change.</summary>
    public long Version { get; private set; }
}
