using Codelab.Attachments.Core.Errors;
using Microsoft.AspNetCore.Http;

namespace Codelab.Attachments.Core.Uploads;

/// <summary>Why an upload was refused. Stored and logged as an integer; every member has an explicit value.</summary>
public enum UploadRefusalReason
{
    /// <summary>The request content type is not <c>multipart/form-data</c> with a usable boundary.</summary>
    NotMultipart = 1,

    /// <summary>A part is not a file part named <c>file</c>.</summary>
    UnexpectedPart = 2,

    /// <summary>The body carries no file part.</summary>
    NoFile = 3,

    /// <summary>A file has no content.</summary>
    EmptyFile = 4,

    /// <summary>One file part more than <c>MaxFileCount</c>.</summary>
    TooManyFiles = 5,

    /// <summary>A file passed <c>MaxFileBytes</c>.</summary>
    FileTooLarge = 6,

    /// <summary>The request passed <c>MaxTotalBytes</c>.</summary>
    TotalTooLarge = 7,

    /// <summary>The client name is unusable, or its extension is blocked or not in the allowlist.</summary>
    TypeNotAllowed = 8,

    /// <summary>The content does not start with the magic bytes of its extension (a renamed or polyglot file).</summary>
    SignatureMismatch = 9,

    /// <summary>The multipart body is malformed (bad framing or oversized part headers).</summary>
    MalformedBody = 10,
}

/// <summary>An upload refusal: the reason, the wire <see cref="Code"/> and the HTTP <see cref="StatusCode"/> the host answers with.</summary>
public sealed record UploadRefusal
{
    private UploadRefusal(UploadRefusalReason reason, string code, int statusCode)
    {
        Reason = reason;
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Why the upload was refused.</summary>
    public UploadRefusalReason Reason { get; }

    /// <summary>The machine code (<see cref="AttachmentServiceErrorCodes"/>).</summary>
    public string Code { get; }

    /// <summary>The HTTP status (400, 413 or 415).</summary>
    public int StatusCode { get; }

    /// <summary>The refusal for <paramref name="reason"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not a defined member.</exception>
    public static UploadRefusal For(UploadRefusalReason reason) => reason switch
    {
        UploadRefusalReason.NotMultipart or UploadRefusalReason.UnexpectedPart or UploadRefusalReason.TypeNotAllowed
            => new(reason, AttachmentServiceErrorCodes.TypeNotAllowed, StatusCodes.Status415UnsupportedMediaType),
        UploadRefusalReason.SignatureMismatch
            => new(reason, AttachmentServiceErrorCodes.SignatureMismatch, StatusCodes.Status415UnsupportedMediaType),
        UploadRefusalReason.NoFile or UploadRefusalReason.EmptyFile or UploadRefusalReason.MalformedBody
            => new(reason, AttachmentServiceErrorCodes.EmptyFile, StatusCodes.Status400BadRequest),
        UploadRefusalReason.TooManyFiles
            => new(reason, AttachmentServiceErrorCodes.TooManyFiles, StatusCodes.Status413PayloadTooLarge),
        UploadRefusalReason.FileTooLarge or UploadRefusalReason.TotalTooLarge
            => new(reason, AttachmentServiceErrorCodes.FileTooLarge, StatusCodes.Status413PayloadTooLarge),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown upload refusal reason."),
    };
}
