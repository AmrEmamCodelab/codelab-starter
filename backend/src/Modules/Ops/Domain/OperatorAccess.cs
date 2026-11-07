using Starter.Domain.Auditing;

namespace Starter.Modules.Ops.Domain;

/// <summary>
/// One call an operator made through the Ops Console (<c>ops.OperatorAccess</c>): who (the operator name the console
/// forwarded with the service-asserted request), through which calling service, which endpoint, about what (a
/// reference code, or nothing), under which correlation id, and when. The table is the Ops audit trail of its own, so
/// it is not audited again.
/// </summary>
[NotAudited]
public sealed class OperatorAccess
{
    private OperatorAccess()
    {
    }

    /// <summary>The internal id (never on the wire).</summary>
    public long Id { get; private set; }

    /// <summary>The operator's account name as the Ops Console forwarded it.</summary>
    public string Operator { get; private set; } = string.Empty;

    /// <summary>The calling service (the assertion's issuer), for example <c>starter-ops-console</c>.</summary>
    public string CallerService { get; private set; } = string.Empty;

    /// <summary>The endpoint called (stored as the integer value).</summary>
    public OpsEndpoint Endpoint { get; private set; }

    /// <summary>What the call asked about (the normalized reference code), or null.</summary>
    public string? Subject { get; private set; }

    /// <summary>The correlation id of the call, or null.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>When the call was received.</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>A new access record.</summary>
    /// <exception cref="ArgumentException">A required value is missing or too long, or the endpoint is unknown.</exception>
    public static OperatorAccess Record(OperatorAccessDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        RequireText(details.Operator, OpsLimits.OperatorMaxLength, nameof(details.Operator));
        RequireText(details.CallerService, OpsLimits.ServiceNameMaxLength, nameof(details.CallerService));
        if (!Enum.IsDefined(details.Endpoint))
        {
            throw new ArgumentException("Unknown Ops endpoint.", nameof(details));
        }

        return new OperatorAccess
        {
            Operator = details.Operator,
            CallerService = details.CallerService,
            Endpoint = details.Endpoint,
            Subject = Truncate(details.Subject, OpsLimits.SubjectMaxLength),
            CorrelationId = Truncate(details.CorrelationId, OpsLimits.CorrelationIdMaxLength),
            At = details.At,
        };
    }

    private static void RequireText(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new ArgumentException($"{name} must be 1 to {maxLength} characters.", name);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>What an <see cref="OperatorAccess"/> records.</summary>
/// <param name="Operator">The operator name (already normalized by <see cref="OperatorName"/>).</param>
/// <param name="CallerService">The calling service.</param>
/// <param name="Endpoint">The endpoint called.</param>
/// <param name="Subject">What the call asked about, or null.</param>
/// <param name="CorrelationId">The call's correlation id, or null.</param>
/// <param name="At">When the call was received.</param>
public sealed record OperatorAccessDetails(
    string Operator,
    string CallerService,
    OpsEndpoint Endpoint,
    string? Subject,
    string? CorrelationId,
    DateTimeOffset At);
