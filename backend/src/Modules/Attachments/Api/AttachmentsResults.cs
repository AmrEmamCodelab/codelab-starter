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
    /// The download (<see cref="AttachmentFileResult"/>: streamed, <c>Content-Disposition: attachment</c>, nosniff), or the
    /// refusal's problem+json.
    /// </summary>
    public static ActionResult ToFileResult(this Outcome<AttachmentDownload> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.IsSuccess ? new AttachmentFileResult(outcome.Value) : outcome.Refusal.ToActionResult();
    }
}

/// <summary>
/// An attachment's bytes as a download: streamed from the attachment service without buffering, with the name and MIME
/// type this database recorded, <c>Content-Disposition: attachment</c> (never rendered inline) and
/// <c>X-Content-Type-Options: nosniff</c> (the browser keeps the recorded type). The stream is disposed after the write.
/// </summary>
public sealed class AttachmentFileResult : FileStreamResult
{
    /// <summary>The <c>X-Content-Type-Options</c> value that stops MIME sniffing.</summary>
    public const string NoSniff = "nosniff";

    /// <summary>Creates the result for <paramref name="download"/>.</summary>
    public AttachmentFileResult(AttachmentDownload download)
        : base(Guard(download).Content, download.ContentType)
    {
        FileDownloadName = download.FileName;
        EnableRangeProcessing = false;
    }

    /// <inheritdoc />
    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.HttpContext.Response.Headers[HeaderNames.XContentTypeOptions] = NoSniff;
        return base.ExecuteResultAsync(context);
    }

    private static AttachmentDownload Guard(AttachmentDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);
        return download;
    }
}
