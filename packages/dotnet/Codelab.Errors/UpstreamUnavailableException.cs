namespace Codelab.Errors;

/// <summary>
/// A dependency (another service, an identity provider, a mail relay) could not answer. Thrown by the adapter that
/// wraps ONLY the outbound call, with <c>catch (Exception ex) when (ex is not OperationCanceledException)</c>; the
/// <see cref="Classifiers.UpstreamUnavailableClassifier"/> answers 502 <see cref="ErrorCodes.Upstream"/>. The
/// dependency's name stays in the log and out of the body.
/// </summary>
public class UpstreamUnavailableException : Exception
{
    /// <summary>Creates the exception for the dependency named <paramref name="upstream"/>.</summary>
    public UpstreamUnavailableException(string upstream, Exception? innerException)
        : base($"Upstream '{upstream}' is unavailable.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upstream);
        Upstream = upstream;
    }

    /// <summary>Creates the exception for the dependency named <paramref name="upstream"/>.</summary>
    public UpstreamUnavailableException(string upstream)
        : this(upstream, innerException: null)
    {
    }

    /// <summary>The dependency's name, for the log only.</summary>
    public string Upstream { get; }
}
