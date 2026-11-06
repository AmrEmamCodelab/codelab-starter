using Starter.Application.Messaging;
using Starter.Application.Results;
using Starter.Application.Users;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>An activation issued for an account, as the administrator sees it.</summary>
/// <param name="ActivationLink">
/// The link to hand to the user (it carries the sealed ticket in its fragment) when <paramref name="Delivery"/> is
/// <see cref="ActivationDelivery.AdministratorLink"/>; null when the link is e-mailed.
/// </param>
/// <param name="ExpiresAt">
/// When the issued ticket stops working. For an e-mailed activation the link in the e-mail is issued again at delivery
/// time, so it expires a little later (by the outbox delay).
/// </param>
/// <param name="Delivery">How the link reaches the user.</param>
public sealed record IssuedActivation(string? ActivationLink, DateTimeOffset ExpiresAt, ActivationDelivery Delivery);

/// <summary>
/// Issues an account's activation (the only way a password-bound account ever gets a password). Identity seals a
/// one-time ticket at once, which also clears any current password and supersedes every older link (an administrator's
/// password reset takes effect immediately). Then, by <see cref="ActivationDeliveryRules.DeliveryFor"/>:
/// <list type="bullet">
/// <item>an account with an e-mail address gets a Pending <see cref="OutboxMessageTypes.AccountActivation"/> message
/// that names the ticket by id only; at delivery <see cref="AccountActivationEmailHandler"/> issues a fresh ticket and
/// e-mails its link, so the administrator never sees a link and this ticket's sealed string is dropped here;</item>
/// <item>an account without one gets the link back for the administrator to hand over, and no outbox message
/// (there is nowhere to deliver it).</item>
/// </list>
/// The sealed ticket never reaches the outbox, a log or this module's tables.
/// </summary>
public sealed class AccountActivations(IAccountAdministration accounts, IOutbox outbox, IUserPublicIds publicIds, ActivationLinkSettings links)
{
    /// <summary>True when activation links can be built (Users:Activation:LinkBaseUrl is configured).</summary>
    public bool IsConfigured => links.IsConfigured;

    /// <summary>
    /// The activation as the administrator sees it, or a refusal: <see cref="UsersErrorCodes.ActivationLinkUnconfigured"/>
    /// (nothing issued), or Identity's refusal (an unknown, break-glass, Active Directory or SSO account).
    /// </summary>
    /// <param name="account">The account, as just created or read (its e-mail address decides the delivery).</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Outcome<IssuedActivation>> IssueAsync(AccountSummary account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!links.IsConfigured)
        {
            return Refusal.Unprocessable(UsersErrorCodes.ActivationLinkUnconfigured);
        }

        var issued = await accounts.IssueActivationAsync(account.UserId, cancellationToken);
        if (!issued.IsSuccess)
        {
            return issued.Refusal;
        }

        var activation = issued.Value;
        if (ActivationDeliveryRules.DeliveryFor(account) == ActivationDelivery.AdministratorLink)
        {
            return new IssuedActivation(links.LinkFor(activation.Ticket), activation.ExpiresAt, ActivationDelivery.AdministratorLink);
        }

        var payload = new AccountActivationPayload(UserPublicId: publicIds.TokenOf(account.UserId), TicketId: activation.TicketId, ExpiresAt: activation.ExpiresAt);
        await outbox.EnqueueAsync(
            new OutboxMessage(OutboxMessageTypes.AccountActivation, ActivationPayloads.Write(payload), NotBefore: null),
            cancellationToken);
        return new IssuedActivation(ActivationLink: null, activation.ExpiresAt, ActivationDelivery.Email);
    }
}
