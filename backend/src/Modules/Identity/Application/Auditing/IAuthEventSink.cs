using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.Auditing;

/// <summary>
/// One authentication decision as the deciding code knows it. The ambient facts (the time, the correlation id) and
/// the pseudonymous hash of <see cref="UserName"/> are added by the sink, so a call site records only what it alone
/// knows.
/// </summary>
/// <param name="EventType">What happened.</param>
/// <param name="UserId">The account the decision resolved to, or null (unknown user).</param>
/// <param name="UserName">
/// The identifier the caller typed (user name, or the matched e-mail, login name or UPN), or null. Never stored as
/// typed: the sink keeps only its keyed hash. Never a password.
/// </param>
/// <param name="Mode">The sign-in mode involved, or null outside a sign-in.</param>
/// <param name="Reason">Why the decision went against the caller, or null for a success.</param>
public sealed record AuthEventRecord(
    AuthEventType EventType,
    long? UserId = null,
    string? UserName = null,
    SignInMode? Mode = null,
    AuthFailureReason? Reason = null)
{
    /// <summary>A log-safe description: the typed identifier is never printed.</summary>
    public override string ToString() => nameof(AuthEventRecord) + "(" + EventType + ", " + Reason + ")";
}

/// <summary>
/// The write-only port of the authentication trail (<c>identity.AuthEvents</c>). Nobody reads the trail back through
/// it, edits or deletes a row.
/// </summary>
/// <remarks>
/// <b>Implementations never throw</b> (except for cancellation): a sign-in must not fail because its audit row could
/// not be written, or a full disk becomes an outage of the front door. The implementation logs the event first and
/// then writes the row, so a failed write downgrades the trail without losing the event.
/// </remarks>
public interface IAuthEventSink
{
    /// <summary>Records one decision.</summary>
    Task RecordAsync(AuthEventRecord authEvent, CancellationToken cancellationToken);
}
