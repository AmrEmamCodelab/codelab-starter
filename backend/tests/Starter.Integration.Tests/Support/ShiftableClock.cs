namespace Starter.Integration.Tests.Support;

/// <summary>
/// A host clock that runs in real time plus an offset the test controls. Unlike a frozen fake clock it keeps moving, so
/// tokens, sessions and timeouts behave normally while the offset is zero; a test that needs another moment (a year
/// rollover, an outbox back-off, a lease expiry) shifts it with <see cref="TravelTo"/> or <see cref="Advance"/> and
/// returns to real time with <see cref="Reset"/> (in a <c>finally</c>). Timers stay on the system clock.
/// </summary>
/// <param name="inner">The clock that supplies real time (the system clock outside tests of this class).</param>
public sealed class ShiftableClock(TimeProvider inner) : TimeProvider
{
    private readonly Lock _gate = new();
    private TimeSpan _offset;

    /// <summary>A clock over the system clock.</summary>
    public ShiftableClock()
        : this(System)
    {
    }

    /// <summary>The current shift from real time (zero unless a test shifted it).</summary>
    public TimeSpan Offset
    {
        get
        {
            lock (_gate)
            {
                return _offset;
            }
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + Offset;

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    /// <summary>Moves the clock so that "now" is <paramref name="moment"/> (it keeps running from there).</summary>
    public void TravelTo(DateTimeOffset moment)
    {
        lock (_gate)
        {
            _offset = moment - inner.GetUtcNow();
        }
    }

    /// <summary>Moves the clock forward by <paramref name="delta"/>, on top of any earlier shift.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative.</exception>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        lock (_gate)
        {
            _offset += delta;
        }
    }

    /// <summary>Returns to real time.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _offset = TimeSpan.Zero;
        }
    }
}
