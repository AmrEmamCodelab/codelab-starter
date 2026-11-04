using Codelab.Observability;
using Microsoft.AspNetCore.Http;

namespace Codelab.Errors;

/// <summary>
/// The ONE error envelope: an RFC 9457 problem+json body whose extension members are <see cref="CodeMember"/>,
/// <see cref="CorrelationIdMember"/> and <see cref="ReferenceCodeMember"/> (plus <c>errors</c> on a validation
/// failure). No <c>type</c>, no <c>instance</c> and no framework <c>traceId</c>: <c>code</c> is the problem class a
/// client branches on, and the correlation id is the one id to quote. Every writer stamps the ids through
/// <see cref="StampIdentity"/>, so the envelope cannot drift between writers.
/// </summary>
public static class ErrorEnvelope
{
    /// <summary>The media type of every error body.</summary>
    public const string ProblemJsonMediaType = "application/problem+json";

    /// <summary>The <c>title</c> of every error body; the client reads <see cref="CodeMember"/>, never the title.</summary>
    public const string Title = "Request failed";

    /// <summary>The machine code member (<see cref="ErrorCodes"/> or a module's own code).</summary>
    public const string CodeMember = "code";

    /// <summary>The correlation id member (32 lowercase hex).</summary>
    public const string CorrelationIdMember = "correlationId";

    /// <summary>The reference code member (<see cref="ReferenceCode"/>), what a user reads to the helpdesk.</summary>
    public const string ReferenceCodeMember = "referenceCode";

    /// <summary>The per-field validation errors member.</summary>
    public const string ErrorsMember = "errors";

    /// <summary>The framework's own id member, removed from every body.</summary>
    public const string FrameworkTraceIdMember = "traceId";

    /// <summary>
    /// Writes <see cref="CorrelationIdMember"/> and <see cref="ReferenceCodeMember"/> with the indexer (never
    /// <c>Add</c>, which throws when a second writer runs over the same dictionary) and removes the framework's
    /// <see cref="FrameworkTraceIdMember"/>. Idempotent.
    /// </summary>
    public static void StampIdentity(IDictionary<string, object?> extensions, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(httpContext);

        var correlationId = CorrelationId.For(httpContext);
        extensions[CorrelationIdMember] = correlationId;
        extensions[ReferenceCodeMember] = ReferenceCode.From(correlationId);
        extensions.Remove(FrameworkTraceIdMember);
    }
}
