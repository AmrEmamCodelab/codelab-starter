namespace Codelab.Observability;

/// <summary>
/// The structured log property names every Codelab host writes. They are the contract of saved Seq queries and
/// signals, so a rename is a breaking change.
/// </summary>
public static class LogPropertyNames
{
    /// <summary>The host's service name (<see cref="ObservabilityOptions.ServiceName"/>).</summary>
    public const string Application = "Application";

    /// <summary>The ASP.NET Core environment name (<c>Production</c>, <c>Development</c>, ...).</summary>
    public const string Environment = "Environment";

    /// <summary>The deployment tier, one of <see cref="DeploymentEnvironments.All"/>.</summary>
    public const string DeploymentEnvironment = "DeploymentEnvironment";

    /// <summary>The host assembly's informational version, <c>&lt;semver&gt;+&lt;short sha&gt;</c>.</summary>
    public const string Release = "Release";

    /// <summary>The node the event was written on.</summary>
    public const string MachineName = "MachineName";

    /// <summary>The request's correlation id (32 lowercase hex, server owned).</summary>
    public const string CorrelationId = "CorrelationId";

    /// <summary>The short code a user quotes to the helpdesk (<see cref="Observability.ReferenceCode"/>).</summary>
    public const string ReferenceCode = "ReferenceCode";

    /// <summary>A well-formed inbound <c>X-Correlation-ID</c> that differs from the server's id; recorded, never adopted.</summary>
    public const string InboundCorrelationId = "InboundCorrelationId";

    /// <summary>The keyed hash of the caller's NameIdentifier (<see cref="Observability.UserIdHash"/>); absent for an anonymous caller.</summary>
    public const string UserIdHash = "UserIdHash";

    /// <summary>The matched route pattern, which groups requests without logging the ids in them.</summary>
    public const string RouteTemplate = "RouteTemplate";

    /// <summary>The failure group (<see cref="FailureFingerprint"/>).</summary>
    public const string ErrorFingerprint = "ErrorFingerprint";

    /// <summary>The exception's full type name, logged beside <see cref="ErrorFingerprint"/>.</summary>
    public const string ExceptionType = "ExceptionType";

    /// <summary>The failure's machine code (an error code such as <c>INTERNAL_ERROR</c>).</summary>
    public const string Code = "Code";

    /// <summary>The classifier that decided a request failure.</summary>
    public const string Classifier = "Classifier";

    /// <summary>The response status code.</summary>
    public const string StatusCode = "StatusCode";

    /// <summary>The background job id on a job failure line.</summary>
    public const string JobId = "JobId";

    /// <summary>The background job's method name on a job failure line (never its arguments).</summary>
    public const string Job = "Job";

    /// <summary>The background job's retry count on a job failure line.</summary>
    public const string Retry = "Retry";
}
