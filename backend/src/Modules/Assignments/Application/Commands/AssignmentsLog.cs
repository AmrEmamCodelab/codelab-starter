using Microsoft.Extensions.Logging;

namespace Starter.Modules.Assignments.Application.Commands;

/// <summary>The module's log lines (event ids 9001-9099; internal ids only, never names or numbers).</summary>
internal static partial class AssignmentsLog
{
    [LoggerMessage(EventId = 9001, Level = LogLevel.Information, Message = "Record {RecordId} ({RecordType}) claimed by user {UserId}")]
    public static partial void Claimed(ILogger logger, long recordId, string recordType, long userId);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Information, Message = "Record {RecordId} ({RecordType}) released by user {UserId}")]
    public static partial void Released(ILogger logger, long recordId, string recordType, long userId);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Information, Message = "Record {RecordId} ({RecordType}) assigned to user {AssigneeUserId} by user {UserId}")]
    public static partial void Assigned(ILogger logger, long recordId, string recordType, long assigneeUserId, long userId);

    [LoggerMessage(EventId = 9004, Level = LogLevel.Information, Message = "Assignment of record {RecordId} ({RecordType}) closed: the record reached terminal status {Status}")]
    public static partial void ClosedOnTerminal(ILogger logger, long recordId, string recordType, int status);
}
