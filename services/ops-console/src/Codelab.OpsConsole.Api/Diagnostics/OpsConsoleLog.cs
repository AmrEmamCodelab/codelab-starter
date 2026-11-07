using Codelab.OpsConsole.Application.ReferenceCodes;

namespace Codelab.OpsConsole.Api.Diagnostics;

/// <summary>
/// The console's log lines (source-generated). Identifiers and outcomes only: never the Seq key, an assertion, an
/// operator's name or a log event's text.
/// </summary>
internal static partial class OpsConsoleLog
{
    [LoggerMessage(EventId = 5401, Level = LogLevel.Warning, Message = "Ops API call {Route} answered HTTP {StatusCode}")]
    internal static partial void OpsApiRefused(ILogger logger, string route, int statusCode);

    [LoggerMessage(EventId = 5402, Level = LogLevel.Warning, Message = "Ops API call {Route} failed: {Failure}")]
    internal static partial void OpsApiUnreachable(ILogger logger, string route, string failure);

    [LoggerMessage(EventId = 5403, Level = LogLevel.Warning, Message = "Seq lookup answered HTTP {StatusCode}")]
    internal static partial void SeqRefused(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 5404, Level = LogLevel.Warning, Message = "Seq lookup failed: {Failure}")]
    internal static partial void SeqUnreachable(ILogger logger, string failure);

    [LoggerMessage(EventId = 5405, Level = LogLevel.Information, Message = "Reference code lookup ended {Outcome} with {EventCount} log event(s)")]
    internal static partial void ReferenceCodeLookedUp(ILogger logger, ReferenceCodeLookupOutcome outcome, int eventCount);

    /// <summary>
    /// One certificate that needs attention. <c>CertificateStatus</c> is the integer value: the Seq signal
    /// "Ops: Certificate expiry warnings" (deploy/seq) matches <c>CertificateStatus in [2, 3, 4]</c>.
    /// </summary>
    [LoggerMessage(EventId = 5406, EventName = "CertificateNeedsAttention", Level = LogLevel.Warning, Message = "Certificate {CertificateName} is at status {CertificateStatus} with {DaysLeft} day(s) left")]
    internal static partial void CertificateNeedsAttention(ILogger logger, string certificateName, int certificateStatus, int? daysLeft);
}
