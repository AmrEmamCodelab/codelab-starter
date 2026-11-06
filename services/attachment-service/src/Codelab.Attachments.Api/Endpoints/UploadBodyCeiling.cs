using Codelab.Attachments.Api.Diagnostics;
using Codelab.Security.Uploads;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Api.Endpoints;

/// <summary>
/// The upload request's body ceiling: <c>MaxTotalBytes</c> plus room for the multipart framing of
/// <c>MaxFileCount</c> parts. The upload pipeline enforces the real caps while it streams; this ceiling raises the
/// server's default body limit (30 MB on Kestrel and IIS) to exactly what a legal request can need, and refuses a
/// declared <c>Content-Length</c> above it before a byte is read. Keep the IIS <c>maxAllowedContentLength</c>
/// (web.config) at least this value.
/// </summary>
public static class UploadBodyCeiling
{
    /// <summary>Framing allowance per file part: its boundary line and headers (the multipart reader's header cap).</summary>
    public const long PartFramingBytes = 16 * 1024;

    /// <summary>The ceiling for <paramref name="options"/>: total bytes plus one framing allowance per part and one for the closing boundary.</summary>
    public static long For(UploadSafetyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.MaxTotalBytes + ((options.MaxFileCount + 1L) * PartFramingBytes);
    }
}

/// <summary>Applies <see cref="UploadBodyCeiling"/> to the upload endpoint.</summary>
internal sealed class UploadBodyCeilingFilter(IOptions<UploadSafetyOptions> options, ILogger<UploadBodyCeilingFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var httpContext = context.HttpContext;
        var ceiling = UploadBodyCeiling.For(options.Value);
        if (httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = ceiling;
        }

        if (httpContext.Request.ContentLength is { } declared && declared > ceiling)
        {
            AttachmentsApiLog.BodyAboveCeiling(logger, declared, ceiling);
            return AttachmentProblems.BodyTooLarge();
        }

        return await next(context);
    }
}
