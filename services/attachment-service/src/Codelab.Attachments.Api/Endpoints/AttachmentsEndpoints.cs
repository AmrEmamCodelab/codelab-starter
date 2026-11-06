using System.Net.Mime;
using System.Security.Claims;
using Codelab.Attachments.Api.Authentication;
using Codelab.Attachments.Api.Diagnostics;
using Codelab.Attachments.Api.Hosting;
using Codelab.Attachments.Core.Storage;
using Codelab.Attachments.Core.Uploads;
using Codelab.Security.Uploads;

namespace Codelab.Attachments.Api.Endpoints;

/// <summary>The routes of the service (phase 6 contract 1).</summary>
public static class AttachmentsRoutes
{
    /// <summary>POST: the browser's multipart upload (ticket only).</summary>
    public const string Uploads = "/api/uploads";

    /// <summary>GET and DELETE: one stored file (S2S only).</summary>
    public const string File = "/api/files/{fileId}";
}

/// <summary>The upload response: every stored file with its signed receipt, in request order.</summary>
/// <param name="Files">The stored files.</param>
public sealed record UploadResponse(IReadOnlyList<UploadedFile> Files);

/// <summary>
/// The three endpoints. Thin: each authorizes through its policy, calls one Core operation and maps the outcome. The
/// upload streams the raw body into <see cref="IUploadPipeline"/> (no form binding, so nothing is buffered).
/// </summary>
internal static class AttachmentsEndpoints
{
    internal const string LogCategory = "Codelab.Attachments.Api.Endpoints";
    internal const string NoSniff = "nosniff";
    internal const string NoStore = "no-store";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(AttachmentsRoutes.Uploads, UploadAsync)
            .RequireAuthorization(AttachmentsAuthentication.UploadTicketPolicy)
            .RequireCors(UploadCors.PolicyName)
            .AddEndpointFilter<UploadBodyCeilingFilter>();
        endpoints.MapGet(AttachmentsRoutes.File, DownloadAsync)
            .RequireAuthorization(AttachmentsAuthentication.ServiceCallerPolicy);
        endpoints.MapDelete(AttachmentsRoutes.File, DeleteAsync)
            .RequireAuthorization(AttachmentsAuthentication.ServiceCallerPolicy);
    }

    internal static async Task<IResult> UploadAsync(HttpRequest request, ClaimsPrincipal user, IUploadPipeline pipeline, ILoggerFactory loggers)
    {
        var scope = user.FindFirstValue(UploadTickets.ScopeClaimType);
        if (string.IsNullOrWhiteSpace(scope))
        {
            return AttachmentProblems.TicketInvalid();
        }

        var logger = Log(loggers);
        var outcome = await pipeline.RunAsync(new UploadRequest(request.Body, request.ContentType, scope), request.HttpContext.RequestAborted);
        if (outcome.Refusal is { } refusal)
        {
            AttachmentsApiLog.UploadRefused(logger, refusal.Reason, refusal.Code);
            return AttachmentProblems.Refusal(refusal.StatusCode, refusal.Code);
        }

        AttachmentsApiLog.UploadStored(logger, outcome.Files.Count);
        return TypedResults.Ok(new UploadResponse(outcome.Files));
    }

    internal static async Task<IResult> DownloadAsync(string fileId, IAttachmentFileStore store, HttpResponse response)
    {
        var file = await store.OpenReadAsync(fileId, response.HttpContext.RequestAborted);
        if (file is null)
        {
            return AttachmentProblems.FileNotFound();
        }

        response.Headers.XContentTypeOptions = NoSniff;
        response.Headers.CacheControl = NoStore;
        return TypedResults.File(file.Content, DownloadContentType(file.Metadata.FileName), fileDownloadName: file.Metadata.FileName);
    }

    internal static async Task<IResult> DeleteAsync(string fileId, IAttachmentFileStore store, HttpContext httpContext, ILoggerFactory loggers)
    {
        if (!await store.DeleteAsync(fileId, httpContext.RequestAborted))
        {
            return AttachmentProblems.FileNotFound();
        }

        var logger = Log(loggers);
        AttachmentsApiLog.FileDeleted(logger, fileId);
        return TypedResults.NoContent();
    }

    private static ILogger Log(ILoggerFactory loggers) => loggers.CreateLogger(LogCategory);

    /// <summary>The download type comes from the one MIME allowlist by the stored (safe) name; anything else is opaque bytes.</summary>
    internal static string DownloadContentType(string fileName) =>
        UploadContentTypes.TryGetContentType(fileName, out var contentType) ? contentType : MediaTypeNames.Application.Octet;
}
