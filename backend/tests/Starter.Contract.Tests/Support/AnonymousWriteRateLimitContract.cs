using Codelab.Testing.Checks;
using Codelab.Testing.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Starter.Contract.Tests.Support;

/// <summary>
/// Rule <c>base.endpoint-contract</c>, rate-limit part: every anonymous write endpoint (POST, PUT, PATCH, DELETE that
/// <see cref="EndpointAccessContract.AccessOf"/> reports as <see cref="EndpointAccess.Anonymous"/>) carries
/// <see cref="EnableRateLimitingAttribute"/>, so a caller without a session (a password guesser) cannot call it
/// without limit. Anonymous reads (feature flags, health probes, the OpenAPI document) are not covered.
/// </summary>
public static class AnonymousWriteRateLimitContract
{
    private static readonly string[] WriteMethods = [HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete];

    /// <summary>Checks <paramref name="endpoints"/>.</summary>
    public static CheckResult Check(IEnumerable<Endpoint> endpoints)
    {
        var anonymousWrites = EndpointDescriptions.Routable(endpoints)
            .Where(endpoint => EndpointAccessContract.AccessOf(endpoint) == EndpointAccess.Anonymous && IsWrite(endpoint))
            .ToList();
        var violations = anonymousWrites
            .Where(endpoint => endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is null
                || endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null)
            .Select(endpoint => $"{EndpointDescriptions.KeyOf(endpoint)}: anonymous write without [{nameof(EnableRateLimitingAttribute)}]")
            .ToList();
        return new CheckResult("Every anonymous write endpoint is rate limited", violations, anonymousWrites.Count);
    }

    private static bool IsWrite(Endpoint endpoint) =>
        EndpointDescriptions.MethodsOf(endpoint).Any(method => WriteMethods.Contains(method, StringComparer.OrdinalIgnoreCase));
}
