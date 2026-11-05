using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Modules.Identity.Application.Accounts;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.Activation;

/// <summary>
/// Issues an activation link for a password-bound account: a new account's first password, or an administrator's
/// password reset. It clears any current password, supersedes any earlier link (only the newest one redeems), ends
/// every open session of the user and records an auth event. Never a default or shared password.
/// </summary>
public sealed class ActivationIssuer(IActivationTickets tickets, AccountWrites writes, IAuthEventSink audit)
{
    /// <summary>
    /// The issued ticket, or a refusal: <see cref="IdentityErrorCodes.BreakGlassProtected"/>,
    /// <see cref="IdentityErrorCodes.ActivationNotApplicable"/> (AD/SSO binding), <see cref="IdentityErrorCodes.AccountInactive"/>,
    /// <see cref="IdentityErrorCodes.ActivationUnavailable"/> (sealed tickets off).
    /// </summary>
    public async Task<Outcome<ActivationIssue>> IssueAsync(UserAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (RefusalFor(account.CheckActivationEligibility()) is { } refusal)
        {
            return refusal;
        }

        if (!tickets.IsAvailable)
        {
            return Refusal.Unprocessable(IdentityErrorCodes.ActivationUnavailable);
        }

        var issued = tickets.Issue(account.SubjectId);
        account.BeginActivation(issued.TicketId);
        await writes.SaveAsync(account, SessionImpact.EndSessions, cancellationToken);
        await audit.RecordAsync(new AuthEventRecord(AuthEventType.ActivationIssued, UserId: account.Id), cancellationToken);
        return new ActivationIssue(Ticket: issued.Ticket, TicketId: issued.TicketId, ExpiresAt: issued.ExpiresAt);
    }

    private static Refusal? RefusalFor(ActivationEligibility eligibility) => eligibility switch
    {
        ActivationEligibility.Eligible => null,
        ActivationEligibility.BreakGlass => Refusal.Conflict(IdentityErrorCodes.BreakGlassProtected),
        ActivationEligibility.NotPasswordBound => Refusal.Unprocessable(IdentityErrorCodes.ActivationNotApplicable),
        _ => Refusal.Unprocessable(IdentityErrorCodes.AccountInactive),
    };
}
