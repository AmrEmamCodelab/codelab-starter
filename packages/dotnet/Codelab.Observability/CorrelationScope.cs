namespace Codelab.Observability;

/// <summary>
/// The correlation id for work that runs OUTSIDE an HTTP request (a background job, an outbox replay, a hosted
/// service iteration). It flows with the async context, so everything awaited inside <see cref="Begin"/> sees it
/// and nothing outside does; <see cref="CorrelationId.Current"/> reads it when there is no request.
/// </summary>
public static class CorrelationScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>The id of the innermost open scope on this async flow, or null.</summary>
    public static string? Current => Ambient.Value;

    /// <summary>
    /// Makes <paramref name="correlationId"/> current until the returned scope is disposed, which restores the
    /// previous one (scopes nest). Throws <see cref="ArgumentException"/> when the id fails
    /// <see cref="CorrelationId.IsWellFormed"/>: it reaches log lines, so it meets the bounds of a request's id.
    /// </summary>
    public static IDisposable Begin(string correlationId)
    {
        if (!CorrelationId.IsWellFormed(correlationId))
        {
            throw new ArgumentException(
                $"A correlation id must be {CorrelationId.MinLength}-{CorrelationId.MaxLength} characters of [A-Za-z0-9-].",
                nameof(correlationId));
        }

        var previous = Ambient.Value;
        Ambient.Value = correlationId;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
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
