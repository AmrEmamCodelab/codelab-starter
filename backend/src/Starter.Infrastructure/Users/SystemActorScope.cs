using Starter.Application.Jobs;

namespace Starter.Infrastructure.Users;

/// <summary>
/// <see cref="ISystemActorScope"/> on an <see cref="AsyncLocal{T}"/>: a scope covers everything awaited inside it and
/// nothing outside, like <c>Codelab.Observability.CorrelationScope</c>. The HTTP adapters of the current user and the
/// audit context read <see cref="IsActiveOnCurrentFlow"/> directly, so the scope works without being injected.
/// </summary>
public sealed class SystemActorScope : ISystemActorScope
{
    private static readonly AsyncLocal<bool> Ambient = new();

    /// <summary>True while a system scope is open on the current async flow.</summary>
    public static bool IsActiveOnCurrentFlow => Ambient.Value;

    /// <inheritdoc />
    public bool IsActive => IsActiveOnCurrentFlow;

    /// <summary>Opens a system scope (see <see cref="ISystemActorScope.BeginSystemScope"/>).</summary>
    public static IDisposable Begin()
    {
        var previous = Ambient.Value;
        Ambient.Value = true;
        return new Restore(previous);
    }

    /// <inheritdoc />
    public IDisposable BeginSystemScope() => Begin();

    private sealed class Restore(bool previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
