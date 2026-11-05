namespace Starter.Application.Auditing;

/// <summary>Who and where a change came from, recorded with audited writes. Identifiers only, never personal data.</summary>
/// <param name="SubjectId">The acting user's subject, or null for the system actor (jobs, startup, anonymous calls).</param>
/// <param name="ClientAddress">The caller's address as the host sees it, or null outside a request.</param>
/// <param name="CorrelationId">The request or job correlation id, or null when there is none.</param>
public sealed record AuditContext(Guid? SubjectId, string? ClientAddress, string? CorrelationId)
{
    /// <summary>The system actor outside any request.</summary>
    public static AuditContext System { get; } = new(SubjectId: null, ClientAddress: null, CorrelationId: null);

    /// <summary>True when no user is acting.</summary>
    public bool IsSystem => SubjectId is null;
}
