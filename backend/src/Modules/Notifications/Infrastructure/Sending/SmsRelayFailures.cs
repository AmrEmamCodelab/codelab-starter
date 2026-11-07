using Codelab.Errors;
using Codelab.Errors.Classifiers;
using Codelab.Integration.Classification;
using Codelab.Integration.Gateway;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application;

namespace Starter.Modules.Notifications.Infrastructure.Sending;

/// <summary>
/// The codes an SMS delivery failure is recorded with (the outbox message's last error, the log). They never reach a
/// client: SMS is delivered by the outbox drain, never inside a request.
/// </summary>
public static class SmsDeliveryErrorCodes
{
    /// <summary>The gateway or the SMS provider could not be reached or timed out; the same message may go through later (retryable).</summary>
    public const string RelayUnavailable = "NOTIFICATIONS_SMS_RELAY_UNAVAILABLE";

    /// <summary>
    /// The provider refused or did not accept the message, the gateway refused this API (S2S key, audience, certificate)
    /// or its SMS profile is off: repeating the same message does not help until something is fixed (permanent).
    /// </summary>
    public const string RelayRejected = "NOTIFICATIONS_SMS_RELAY_REJECTED";

    /// <summary>The message cannot be relayed as it is (number or text out of the relay's bounds, unknown language) (permanent).</summary>
    public const string MessageInvalid = "NOTIFICATIONS_SMS_MESSAGE_INVALID";
}

/// <summary>
/// An SMS was not delivered. <see cref="IsRetryable"/> says whether sending the same message again can succeed;
/// <see cref="Code"/> is an <see cref="SmsDeliveryErrorCodes"/> value. The message carries codes only, never the
/// number, the text or upstream response text.
/// </summary>
public sealed class SmsDeliveryException : Exception
{
    /// <summary>Creates a permanent failure with <see cref="SmsDeliveryErrorCodes.RelayRejected"/>.</summary>
    public SmsDeliveryException()
        : this("The SMS was not delivered.")
    {
    }

    /// <summary>Creates a permanent failure with <see cref="SmsDeliveryErrorCodes.RelayRejected"/>.</summary>
    public SmsDeliveryException(string message)
        : this(message, innerException: null)
    {
    }

    /// <summary>Creates a permanent failure with <see cref="SmsDeliveryErrorCodes.RelayRejected"/>.</summary>
    public SmsDeliveryException(string message, Exception? innerException)
        : this(new SmsDeliveryFailure(SmsDeliveryErrorCodes.RelayRejected, IsRetryable: false, GatewayCode: null, GatewayReferenceCode: null), message, innerException)
    {
    }

    /// <summary>Creates the exception for a classified failure.</summary>
    public SmsDeliveryException(SmsDeliveryFailure failure, string message, Exception? innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Code = failure.Code;
        IsRetryable = failure.IsRetryable;
        GatewayCode = failure.GatewayCode;
        GatewayReferenceCode = failure.GatewayReferenceCode;
    }

    /// <summary>The <see cref="SmsDeliveryErrorCodes"/> value.</summary>
    public string Code { get; }

    /// <summary>True when the same message may be delivered by a later attempt.</summary>
    public bool IsRetryable { get; }

    /// <summary>The gateway's failure code (<see cref="GatewayFailureCodes"/>), when the gateway was involved.</summary>
    public string? GatewayCode { get; }

    /// <summary>The gateway's error reference code, to find the relay in the gateway's logs.</summary>
    public string? GatewayReferenceCode { get; }
}

/// <summary>A classified SMS delivery failure.</summary>
/// <param name="Code">The <see cref="SmsDeliveryErrorCodes"/> value.</param>
/// <param name="IsRetryable">Whether the same message may go through later.</param>
/// <param name="GatewayCode">The gateway's failure code, if any.</param>
/// <param name="GatewayReferenceCode">The gateway's reference code, if any.</param>
public sealed record SmsDeliveryFailure(string Code, bool IsRetryable, string? GatewayCode, string? GatewayReferenceCode);

/// <summary>
/// The one place that decides what an SMS relay failure means (the outbox retries a retryable one with its back-off):
/// <list type="bullet">
/// <item>The gateway's <see cref="GatewayFailureCodes.UpstreamRejected"/> or <see cref="GatewayFailureCodes.ProfileDisabled"/>: permanent (<see cref="SmsDeliveryErrorCodes.RelayRejected"/>).</item>
/// <item><see cref="GatewayFailureCodes.UpstreamUnavailable"/> after a connection failure, a timeout, a throttle, a server error or an
/// unrecognised transport fault: retryable (<see cref="SmsDeliveryErrorCodes.RelayUnavailable"/>).</item>
/// <item>A refused certificate, a 401/403 or another 4xx from the gateway (<see cref="OutboundFailureKind"/>
/// <c>CertificateRejected</c>, <c>Unauthorized</c>, <c>ClientError</c>): permanent; a deployment fault to fix.</item>
/// <item>A 200 with <c>accepted:false</c>: permanent. A message the relay would refuse: permanent (<see cref="SmsDeliveryErrorCodes.MessageInvalid"/>).</item>
/// </list>
/// </summary>
public static class SmsRelayFailures
{
    private static readonly HashSet<OutboundFailureKind> DeploymentFaults =
    [
        OutboundFailureKind.CertificateRejected,
        OutboundFailureKind.Unauthorized,
        OutboundFailureKind.ClientError,
    ];

    /// <summary>The relay body of <paramref name="message"/>: the language as the gateway's integer (<see cref="GatewayLanguages"/>).</summary>
    public static SmsRelayRequest RequestOf(SmsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new SmsRelayRequest { To = message.To, Text = message.Text, Language = LanguageOf(message.Language) };
    }

    /// <summary>Whether a relay failure may succeed when the same message is sent again.</summary>
    public static bool IsRetryable(GatewayRelayException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Code == GatewayFailureCodes.UpstreamUnavailable && !DeploymentFaults.Contains(failure.Failure.Kind);
    }

    /// <summary>The delivery exception of a relay failure (the relay exception stays the inner exception).</summary>
    public static SmsDeliveryException From(GatewayRelayException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var retryable = IsRetryable(failure);
        var code = retryable ? SmsDeliveryErrorCodes.RelayUnavailable : SmsDeliveryErrorCodes.RelayRejected;
        var classified = new SmsDeliveryFailure(code, retryable, failure.Code, failure.ReferenceCode);
        return new SmsDeliveryException(classified, Describe(classified, failure.Failure.Kind), failure);
    }

    /// <summary>The provider answered but did not accept the message (permanent).</summary>
    public static SmsDeliveryException NotAccepted(string? gatewayReferenceCode)
    {
        var classified = new SmsDeliveryFailure(SmsDeliveryErrorCodes.RelayRejected, IsRetryable: false, GatewayFailureCodes.UpstreamRejected, gatewayReferenceCode);
        return new SmsDeliveryException(classified, $"The SMS provider did not accept the message (gateway reference {gatewayReferenceCode ?? "none"}).", innerException: null);
    }

    /// <summary>The message is outside the relay's bounds (permanent; nothing was sent).</summary>
    public static SmsDeliveryException Invalid()
    {
        var classified = new SmsDeliveryFailure(SmsDeliveryErrorCodes.MessageInvalid, IsRetryable: false, GatewayCode: null, GatewayReferenceCode: null);
        return new SmsDeliveryException(
            classified,
            $"The SMS cannot be relayed: the number must be 1 to {GatewayRelayLimits.MaxSmsRecipientLength} characters without control characters, the text 1 to {GatewayRelayLimits.MaxSmsTextLength}.",
            innerException: null);
    }

    private static int LanguageOf(AppLanguage language) => language switch
    {
        AppLanguage.Arabic => GatewayLanguages.Arabic,
        AppLanguage.English => GatewayLanguages.English,
        _ => (int)language,
    };

    private static string Describe(SmsDeliveryFailure failure, OutboundFailureKind kind) =>
        $"The SMS relay failed: {failure.GatewayCode} ({kind}), gateway reference {failure.GatewayReferenceCode ?? "none"}; "
        + (failure.IsRetryable ? "the outbox retries it." : "repeating it does not help until the cause is fixed.");
}

/// <summary>
/// The Notifications module's entry in the error classifier table: an <see cref="SmsDeliveryException"/> is recorded
/// with its own code (the outbox message's last error), 503 when retryable and 502 when not, at Warning.
/// </summary>
public static class SmsDeliveryFailures
{
    /// <summary>The failure an SMS delivery exception is reported as.</summary>
    public static Failure Of(SmsDeliveryException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var status = exception.IsRetryable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status502BadGateway;
        return new Failure(Status: status, Code: exception.Code, Detail: FrameworkStatusCodes.Describe(status).Detail, Level: LogLevel.Warning);
    }

    /// <summary>Puts the classifier first in the table (it claims only its own type), once: a second call changes nothing.</summary>
    public static CodelabErrorsOptions AddSmsDeliveryFailures(this CodelabErrorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Classifiers.Any(classifier => classifier is ExceptionTypeClassifier<SmsDeliveryException>))
        {
            options.Classifiers.Insert(0, new ExceptionTypeClassifier<SmsDeliveryException>(Of));
        }

        return options;
    }
}
