using Codelab.Integration.Classification;

namespace Codelab.Gateway.Api.Diagnostics;

/// <summary>
/// The gateway's log lines (source-generated). Profile, operation, codes, kinds and statuses only: never a relayed
/// body, a token, a form field, a phone number, a message text or the provider credential.
/// </summary>
internal static partial class GatewayLog
{
    [LoggerMessage(EventId = 5440, Level = LogLevel.Information, Message = "Relay {Profile}/{Operation} completed with upstream status {UpstreamStatus}")]
    internal static partial void RelayCompleted(ILogger logger, string profile, string operation, int? upstreamStatus);

    [LoggerMessage(EventId = 5441, Level = LogLevel.Warning, Message = "Relay {Profile}/{Operation} failed: {ErrorCode} ({FailureKind}, upstream status {UpstreamStatus})")]
    internal static partial void RelayFailed(ILogger logger, string profile, string operation, string errorCode, OutboundFailureKind failureKind, int? upstreamStatus);

    [LoggerMessage(EventId = 5442, Level = LogLevel.Information, Message = "Relay {Profile}/{Operation} refused a malformed request: {ErrorCode}")]
    internal static partial void RelayRefused(ILogger logger, string profile, string operation, string errorCode);

    [LoggerMessage(EventId = 5443, Level = LogLevel.Information, Message = "JWKS document refreshed and cached for {CacheDuration}")]
    internal static partial void JwksRefreshed(ILogger logger, TimeSpan cacheDuration);
}
