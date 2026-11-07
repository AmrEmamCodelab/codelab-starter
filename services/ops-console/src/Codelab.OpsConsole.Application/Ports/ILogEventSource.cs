using Codelab.OpsConsole.Application.ReferenceCodes;

namespace Codelab.OpsConsole.Application.Ports;

/// <summary>
/// The central log store (Seq). Best effort: a store that is not configured, down, slow or refusing the key answers
/// <see cref="LogSearchResult.Unavailable"/> instead of throwing; cancellation propagates.
/// </summary>
public interface ILogEventSource
{
    /// <summary>The events that carry <paramref name="referenceCode"/> (normalized), in any order.</summary>
    Task<LogSearchResult> FindByReferenceCodeAsync(string referenceCode, CancellationToken cancellationToken);
}
