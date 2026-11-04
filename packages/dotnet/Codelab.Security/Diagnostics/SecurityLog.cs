namespace Codelab.Security;

/// <summary>The event ids of every log line the package writes (stable: alerting rules may key on them).</summary>
internal static class SecurityLogEvents
{
    internal const int StoreUnavailable = 7101;
    internal const int PurgeCapped = 7102;
    internal const int SignatureReported = 7110;
    internal const int SignatureRejected = 7111;
    internal const int SigningLayerUnavailable = 7112;
    internal const int SignedBodyTooLarge = 7113;
    internal const int SigningKeyRotatedMidRequest = 7114;
    internal const int ResponseUnsignable = 7120;
    internal const int ResponseSigningFailed = 7121;
    internal const int LargeSignedResponse = 7122;
    internal const int SessionRejected = 7130;
    internal const int SessionCheckUnavailable = 7131;
    internal const int ServiceAssertionRejected = 7140;
    internal const int SchemaEnsured = 7150;
}

/// <summary>The middleware log lines (source-generated; templates carry identifiers only, never key material or PII).</summary>
internal static partial class SecurityLog
{
    [LoggerMessage(EventId = SecurityLogEvents.SignatureReported, Level = LogLevel.Warning, Message = "Request signature REPORT failure on {Method} {Path}: {Code}. The mode is Report, so the request continues unaltered.")]
    internal static partial void SignatureReported(ILogger logger, string method, PathString path, string code);

    [LoggerMessage(EventId = SecurityLogEvents.SignatureRejected, Level = LogLevel.Warning, Message = "Request signature rejected on {Method} {Path}: {Code}.")]
    internal static partial void SignatureRejected(ILogger logger, string method, PathString path, string code);

    [LoggerMessage(EventId = SecurityLogEvents.SigningLayerUnavailable, Level = LogLevel.Critical, Message = "Request signing is {Mode} but its services could not be resolved; requests are NOT being verified.")]
    internal static partial void SigningLayerUnavailable(ILogger logger, Exception? exception, Signing.SecurityEnforcementMode mode);

    [LoggerMessage(EventId = SecurityLogEvents.SignedBodyTooLarge, Level = LogLevel.Warning, Message = "The body of {Method} {Path} could not be hashed within the {Cap}-byte signed-body cap.")]
    internal static partial void SignedBodyTooLarge(ILogger logger, string method, PathString path, int cap);

    [LoggerMessage(EventId = SecurityLogEvents.SigningKeyRotatedMidRequest, Level = LogLevel.Warning, Message = "The proof on {Method} {Path} verified but its key left the ring before the response; the response is not signed.")]
    internal static partial void SigningKeyRotatedMidRequest(ILogger logger, string method, PathString path);

    [LoggerMessage(EventId = SecurityLogEvents.ResponseUnsignable, Level = LogLevel.Warning, Message = "The response for {Path} started before its body could be covered; no response signature was emitted.")]
    internal static partial void ResponseUnsignable(ILogger logger, PathString path);

    [LoggerMessage(EventId = SecurityLogEvents.ResponseSigningFailed, Level = LogLevel.Error, Message = "Signing the response for {Path} failed; it is sent unsigned and a verifying client refuses it.")]
    internal static partial void ResponseSigningFailed(ILogger logger, Exception exception, PathString path);

    [LoggerMessage(EventId = SecurityLogEvents.LargeSignedResponse, Level = LogLevel.Warning, Message = "The signed response for {Endpoint} was {Bytes} bytes, buffered in full; mark file endpoints [UnsignedResponse].")]
    internal static partial void LargeSignedResponse(ILogger logger, string endpoint, int bytes);

    [LoggerMessage(EventId = SecurityLogEvents.SessionRejected, Level = LogLevel.Information, Message = "Session refused on {Method} {Path}: {Code}.")]
    internal static partial void SessionRejected(ILogger logger, string method, PathString path, string code);

    [LoggerMessage(EventId = SecurityLogEvents.SessionCheckUnavailable, Level = LogLevel.Error, Message = "The session store could not answer for {Method} {Path}; refusing with 503.")]
    internal static partial void SessionCheckUnavailable(ILogger logger, Exception exception, string method, PathString path);

    [LoggerMessage(EventId = SecurityLogEvents.ServiceAssertionRejected, Level = LogLevel.Warning, Message = "Service assertion refused on {Method} {Path}: {Outcome}.")]
    internal static partial void ServiceAssertionRejected(ILogger logger, string method, PathString path, string outcome);

    [LoggerMessage(EventId = SecurityLogEvents.SchemaEnsured, Level = LogLevel.Information, Message = "The Codelab.Security SQL store schema is in place.")]
    internal static partial void SchemaEnsured(ILogger logger);
}
