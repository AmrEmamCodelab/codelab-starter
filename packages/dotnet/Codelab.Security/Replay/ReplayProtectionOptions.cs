using Codelab.Security.Storage;

namespace Codelab.Security.Replay;

/// <summary>Replay protection (<c>Codelab:Security:ReplayProtection</c>).</summary>
public sealed class ReplayProtectionOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.ReplayProtection;

    /// <summary>Memory (default, single process) or SqlServer (shared across replicas).</summary>
    public SecurityStoreKind Store { get; set; } = SecurityStoreKind.Memory;

    /// <summary>Extra seconds a consumed request nonce is kept beyond the proof window.</summary>
    public int RetentionMarginSeconds { get; set; } = 60;

    /// <summary>Most live entries the memory store holds before it fails closed.</summary>
    public int MemoryEntryLimit { get; set; } = 100_000;

    /// <summary>
    /// The single-instance acknowledgement for every per-process security store. Outside Development, the memory
    /// replay store (used by request signing in Enforce, the service-assertion server and sealed tickets) and the
    /// memory session store are refused unless this is true: a second replica would accept a replay, or a revoked
    /// session, that the first one refused.
    /// </summary>
    public bool AcknowledgeSingleInstance { get; set; }
}
