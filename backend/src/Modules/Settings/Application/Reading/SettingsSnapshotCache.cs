namespace Starter.Modules.Settings.Application.Reading;

/// <summary>
/// The node's in-memory settings snapshot (registered as a singleton; a test host may register its own instance). It
/// only holds the snapshot: when to reload is the <see cref="RuntimeSettingsReader"/>'s decision. Thread-safe.
/// </summary>
public sealed class SettingsSnapshotCache
{
    private SettingsSnapshot? _current;

    /// <summary>The snapshot, or null before the first load or after <see cref="Invalidate"/>.</summary>
    public SettingsSnapshot? Current => Volatile.Read(ref _current);

    /// <summary>
    /// Offers a freshly loaded snapshot and returns the one to use: <paramref name="loaded"/>, unless another thread
    /// meanwhile stored one with a HIGHER version (a slow load never replaces a newer snapshot).
    /// </summary>
    public SettingsSnapshot Offer(SettingsSnapshot loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        while (true)
        {
            var current = Volatile.Read(ref _current);
            if (current is not null && current.Version > loaded.Version)
            {
                return current;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, loaded, current), current))
            {
                return loaded;
            }
        }
    }

    /// <summary>Drops the snapshot, so the next read on this node reloads (used after a local change).</summary>
    public void Invalidate() => Volatile.Write(ref _current, null);
}
