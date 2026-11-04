using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Codelab.Observability;

/// <summary>
/// What a failure fingerprint is computed from: the exception's type and the SITE that failed, never a message or
/// an id. A record, so the two cannot be passed in the wrong order.
/// </summary>
/// <param name="ExceptionType">The thrown exception's type; its full name enters the hash.</param>
/// <param name="Site">Where it failed, built by one of the <see cref="FailureFingerprint"/> site methods.</param>
public sealed record FingerprintInput(Type ExceptionType, string Site);

/// <summary>
/// The single owner of <see cref="LogPropertyNames.ErrorFingerprint"/>: the first 12 hex characters of
/// SHA-256(<c>"{ExceptionType.FullName}|{site}"</c>). Seq groups failures by it, so one bug is one group across
/// ids, messages, releases and nodes. It never appears in a response body. A site names WHERE code failed in terms
/// that do not change between two failures of the same bug (a route template, a job's type and method).
/// </summary>
public static class FailureFingerprint
{
    /// <summary>The length of a fingerprint, in hex characters.</summary>
    public const int Length = 12;

    /// <summary>The request site when no endpoint was matched.</summary>
    public const string UnroutedSite = "unrouted";

    /// <summary>The job site when the job's type or method could not be loaded.</summary>
    public const string UnknownJobSite = "job:unknown";

    private const string JobSitePrefix = "job:";
    private const string StartupSitePrefix = "startup:";

    /// <summary>The fingerprint of <paramref name="input"/>: 12 lowercase hex characters, stable across processes.</summary>
    public static string For(FingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.ExceptionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Site);

        var text = $"{input.ExceptionType.FullName}|{input.Site}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..Length];
    }

    /// <summary>
    /// The site of a failed request: <c>"{METHOD} {route template}"</c>, or <see cref="UnroutedSite"/>. The method is
    /// upper-cased so a client's casing cannot split a group. Never throws: it runs while a failure is logged.
    /// </summary>
    public static string RequestSite(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return RouteTemplateOf(httpContext) is { } template
            ? $"{httpContext.Request.Method.ToUpperInvariant()} {template}"
            : UnroutedSite;
    }

    /// <summary>
    /// The matched route pattern, or null. Read from the exception-handler feature first, because the exception
    /// handler middleware clears the request's endpoint before it calls a handler.
    /// </summary>
    public static string? RouteTemplateOf(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint();
        return endpoint is RouteEndpoint { RoutePattern.RawText: { } template } ? template : null;
    }

    /// <summary>The site of a failed background job: <c>job:{Type}.{Method}</c> with the type's short name; arguments never enter it.</summary>
    public static string JobSite(Type jobType, string method)
    {
        ArgumentNullException.ThrowIfNull(jobType);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        return $"{JobSitePrefix}{jobType.Name}.{method}";
    }

    /// <summary>The site of a host that failed to build or start: <c>startup:{ServiceName}</c>.</summary>
    public static string StartupSite(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        return StartupSitePrefix + serviceName;
    }
}
