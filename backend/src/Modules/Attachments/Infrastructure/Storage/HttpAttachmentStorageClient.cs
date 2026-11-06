using System.Net;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Infrastructure.Storage;

/// <summary>
/// The <see cref="IAttachmentStorageClient"/> over the attachment service's file API: a typed <see cref="HttpClient"/>
/// whose base address is <c>Attachments:ServiceBaseUrl</c> and whose handler adds the ES256 service assertion
/// (<c>AddCodelabServiceAssertion</c>). 404 means the service has no such file; any other failure (no connection, a
/// timeout, a 5xx, or a 401/403, which means OUR assertion key is misconfigured, never the user's rights) is thrown as
/// <see cref="AttachmentStorageUnavailableException"/>. Cancellation the caller asked for propagates untouched.
/// </summary>
public sealed class HttpAttachmentStorageClient(HttpClient http) : IAttachmentStorageClient
{
    /// <summary>How long one call may take until the response headers arrive (a download then streams).</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken)
    {
        if (!AttachmentsLimits.IsFileId(fileId))
        {
            return null;
        }

        var response = await SendAsync(HttpMethod.Get, fileId, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        EnsureSucceeded(response);
        try
        {
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            response.Dispose();
            throw new AttachmentStorageUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        if (!AttachmentsLimits.IsFileId(fileId))
        {
            return false;
        }

        using var response = await SendAsync(HttpMethod.Delete, fileId, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        EnsureSucceeded(response);
        return true;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string fileId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, AttachmentServiceRoutes.FilePath(fileId));
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            throw new AttachmentStorageUnavailableException(exception);
        }
    }

    /// <summary>Throws (and releases the response) unless the status is a success.</summary>
    private static void EnsureSucceeded(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = response.StatusCode;
        response.Dispose();
        throw new AttachmentStorageUnavailableException(
            new HttpRequestException(HttpRequestError.InvalidResponse, message: null, inner: null, statusCode: status));
    }

    /// <summary>
    /// Cancellation the caller asked for, which must propagate. An <see cref="HttpClient"/> timeout also surfaces as an
    /// <see cref="OperationCanceledException"/>, but with the caller's token still live: that is the service failing to
    /// answer.
    /// </summary>
    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
