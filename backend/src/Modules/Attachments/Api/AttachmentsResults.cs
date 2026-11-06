using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Starter.Application.Results;
using Starter.Infrastructure.Results;
using Starter.Modules.Attachments.Application;

namespace Starter.Modules.Attachments.Api;

/// <summary>The Attachments outcome mappings the kernel <c>OutcomeResults</c> does not have (201 and the file stream).</summary>
public static class AttachmentsResults
{
    /// <summary>201 with <paramref name="map"/> of the value, or the refusal's problem+json.</summary>
    public static ActionResult<TResponse> ToCreatedResult<T, TResponse>(this Outcome<T> outcome, Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(map);
        return outcome.IsSuccess
            ? new ObjectResult(map(outcome.Value)) { StatusCode = StatusCodes.Status201Created }
            : outcome.Refusal.ToActionResult();
    }

    /// <summary>
    /// The download (<see cref="AttachmentFileResult"/>: streamed, no <c>Content-Disposition</c>, nosniff, sandboxed), or
    /// the refusal's problem+json.
    /// </summary>
    public static ActionResult ToFileResult(this Outcome<AttachmentDownload> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.IsSuccess ? new AttachmentFileResult(outcome.Value) : outcome.Refusal.ToActionResult();
    }
}

/// <summary>
/// An attachment's bytes for the SPA: streamed from the attachment service without buffering, with the MIME type this
/// database recorded, <c>X-Content-Type-Options: nosniff</c> (the browser keeps the recorded type) and
/// <c>Content-Security-Policy: sandbox; default-src 'none'</c> (rendered on its own, the bytes can run nothing and load
/// nothing). The stream is disposed after the write.
/// <para>
/// It deliberately sends NO <c>Content-Disposition</c>. The endpoint answers only a signed <c>fetch</c> (a navigation or
/// a link can never carry the bearer token and the request proof), and the SPA saves the Blob itself under the name
/// the attachment list gave it. A <c>Content-Disposition</c> header is what browser download managers (Internet Download
/// Manager and similar) key on: they swallow such a response mid-<c>fetch</c>, hand the page a bare 204 without
/// <c>Access-Control-Allow-Origin</c> (<c>MissingAllowOriginHeader</c>), and replay the URL themselves without the
/// request proof (401 <c>signature_missing</c>). That was the phase 6 browser download blocker.
/// </para>
/// </summary>
public sealed class AttachmentFileResult : FileStreamResult
{
    /// <summary>The <c>X-Content-Type-Options</c> value that stops MIME sniffing.</summary>
    public const string NoSniff = "nosniff";

    /// <summary>The <c>Content-Security-Policy</c> of a download: an opaque origin that may run and load nothing.</summary>
    public const string SandboxPolicy = "sandbox; default-src 'none'";

    /// <summary>Creates the result for <paramref name="download"/>.</summary>
    public AttachmentFileResult(AttachmentDownload download)
        : base(Guard(download).Content, download.ContentType)
    {
        EnableRangeProcessing = false;
    }

    /// <inheritdoc />
    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var headers = context.HttpContext.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = NoSniff;
        headers[HeaderNames.ContentSecurityPolicy] = SandboxPolicy;
        return base.ExecuteResultAsync(context);
    }

    private static AttachmentDownload Guard(AttachmentDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);
        return download;
    }
}
