using Codelab.Attachments.Core.Uploads;
using Codelab.Security.Tickets;

namespace Codelab.Attachments.Api.Diagnostics;

/// <summary>
/// The host's log lines (source-generated). Identifiers only: never a ticket, a receipt, a file name or a ticket
/// subject (it names a record and a user).
/// </summary>
internal static partial class AttachmentsApiLog
{
    [LoggerMessage(EventId = 5320, Level = LogLevel.Information, Message = "Upload ticket refused: {TicketStatus}")]
    internal static partial void TicketRefused(ILogger logger, TicketOpenStatus ticketStatus);

    [LoggerMessage(EventId = 5321, Level = LogLevel.Information, Message = "Upload refused: {RefusalReason} ({ErrorCode})")]
    internal static partial void UploadRefused(ILogger logger, UploadRefusalReason refusalReason, string errorCode);

    [LoggerMessage(EventId = 5322, Level = LogLevel.Information, Message = "Upload stored {FileCount} file(s)")]
    internal static partial void UploadStored(ILogger logger, int fileCount);

    [LoggerMessage(EventId = 5323, Level = LogLevel.Information, Message = "Upload body of {DeclaredBytes} bytes refused above the ceiling of {CeilingBytes} bytes")]
    internal static partial void BodyAboveCeiling(ILogger logger, long declaredBytes, long ceilingBytes);

    [LoggerMessage(EventId = 5324, Level = LogLevel.Information, Message = "Attachment file {FileId} deleted")]
    internal static partial void FileDeleted(ILogger logger, string fileId);
}
