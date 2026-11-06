using Codelab.Security;

namespace Starter.Modules.Attachments.Infrastructure.Storage;

/// <summary>
/// The attachment service's HTTP contract as the API sees it (phase 6 contract 1): the upload path the browser posts
/// to, the file paths the API calls server to server, and the upload ticket header. One owner, so the ticket, the
/// storage client and the tests never spell them twice.
/// </summary>
public static class AttachmentServiceRoutes
{
    /// <summary>The URL path separator.</summary>
    public const char PathSeparator = '/';

    /// <summary>The browser upload path, relative to the service root (<c>POST</c>, multipart, ticket header only).</summary>
    public const string Uploads = "api/uploads";

    /// <summary>The stored-file path prefix, relative to the service root (<c>GET</c> and <c>DELETE</c>, S2S only).</summary>
    public const string FilesPrefix = "api/files/";

    /// <summary>The first part of every protocol header (<c>X-</c>).</summary>
    public const string HeaderPrefix = "X-";

    /// <summary>The suffix of the upload ticket header after the protocol prefix (<c>X-{Prefix}-Upload-Ticket</c>).</summary>
    public const string UploadTicketHeaderSuffix = "-Upload-Ticket";

    /// <summary>The path of stored file <paramref name="fileId"/>, relative to the service root (the id is escaped).</summary>
    public static string FilePath(string fileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        return FilesPrefix + Uri.EscapeDataString(fileId);
    }

    /// <summary>The upload ticket header of <paramref name="protocol"/>: <c>X-{Prefix}-Upload-Ticket</c>.</summary>
    public static string UploadTicketHeader(SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return HeaderPrefix + protocol.Prefix + UploadTicketHeaderSuffix;
    }
}
