namespace Starter.Modules.Lookups.Application;

/// <summary>The Lookups module's run-time limits.</summary>
public static class LookupsLimits
{
    /// <summary>
    /// How long, in seconds, a node serves a type's cached active list (<see cref="CacheTtl"/>). It is also the upper
    /// bound for another node to see an activation change: only the node that made the change drops its entry at once.
    /// </summary>
    public const int CacheTtlSeconds = 60;

    /// <summary>The cache lifetime of a type's active list.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(CacheTtlSeconds);
}
