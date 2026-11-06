namespace Starter.Application.Settings;

/// <summary>The freshness bounds of the runtime settings cache.</summary>
public static class SettingsLimits
{
    /// <summary>The cache TTL in seconds (see <see cref="CacheTtl"/>).</summary>
    public const int CacheTtlSeconds = 30;

    /// <summary>
    /// The upper bound on how stale a node's settings snapshot can be: a snapshot this old (or older) is reloaded on
    /// the next read, even inside a scope that already checked the stamp. A change on one node is therefore visible on
    /// every other node within this time.
    /// </summary>
    public static TimeSpan CacheTtl { get; } = TimeSpan.FromSeconds(CacheTtlSeconds);
}
