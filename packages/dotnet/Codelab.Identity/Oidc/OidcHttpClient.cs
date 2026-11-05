namespace Codelab.Identity.Oidc;

/// <summary>
/// The named <see cref="HttpClient"/> every back-channel call (token, JWKS) goes through. Registered by
/// <c>AddCodelabOidc</c> with the configured timeout, a response size cap and the platform's normal certificate
/// validation (this package never sets a certificate callback). A test host or the phase 8 gateway relay replaces
/// the primary handler: <c>services.AddHttpClient(OidcHttpClient.Name).ConfigurePrimaryHttpMessageHandler(...)</c>.
/// </summary>
public static class OidcHttpClient
{
    /// <summary>The client name.</summary>
    public const string Name = "codelab-identity-oidc";

    /// <summary>The largest response body buffered from the provider (token response or JWKS).</summary>
    public const int MaxResponseBytes = 256 * 1024;

    /// <summary>
    /// The provider could not be talked to: a network failure, an oversized body, or the client's own timeout (a
    /// <see cref="TaskCanceledException"/> while <paramref name="cancellationToken"/> was NOT cancelled). Cancellation of
    /// <paramref name="cancellationToken"/> itself is not a transport failure and propagates.
    /// </summary>
    internal static bool IsTransportFailure(Exception failure, CancellationToken cancellationToken) =>
        failure is HttpRequestException || (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
