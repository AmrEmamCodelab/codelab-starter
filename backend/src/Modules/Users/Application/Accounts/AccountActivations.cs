using System.Text.Json;
using Starter.Application.Messaging;
using Starter.Application.Results;
using Starter.Application.Users;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>An activation issued for an account, as the administrator sees it.</summary>
/// <param name="ActivationLink">The link to hand to the user (it carries the sealed ticket in its fragment).</param>
/// <param name="ExpiresAt">When the link stops working.</param>
public sealed record IssuedActivation(string ActivationLink, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues an account's activation (the only way a password-bound account ever gets a password): Identity seals a
/// one-time ticket, this builds the link from it and writes a Pending <see cref="OutboxMessageTypes.AccountActivation"/>
/// message that names the ticket by id only. The sealed ticket goes to the administrator, never to the outbox, a log
/// or this module's tables. Delivery by e-mail is phase 5.
/// </summary>
public sealed class AccountActivations(IAccountAdministration accounts, IOutbox outbox, IUserPublicIds publicIds, ActivationLinkSettings links)
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    /// <summary>True when activation links can be built (Users:Activation:LinkBaseUrl is configured).</summary>
    public bool IsConfigured => links.IsConfigured;

    /// <summary>
    /// The link and expiry, or a refusal: <see cref="UsersErrorCodes.ActivationLinkUnconfigured"/> (nothing issued), or
    /// Identity's refusal (an unknown, break-glass, Active Directory or SSO account).
    /// </summary>
    public async Task<Outcome<IssuedActivation>> IssueAsync(long userId, CancellationToken cancellationToken)
    {
        if (!links.IsConfigured)
        {
            return Refusal.Unprocessable(UsersErrorCodes.ActivationLinkUnconfigured);
        }

        var issued = await accounts.IssueActivationAsync(userId, cancellationToken);
        if (!issued.IsSuccess)
        {
            return issued.Refusal;
        }

        var activation = issued.Value;
        await outbox.EnqueueAsync(
            new OutboxMessage(OutboxMessageTypes.AccountActivation, PayloadOf(userId, activation), NotBefore: null),
            cancellationToken);
        return new IssuedActivation(links.LinkFor(activation.Ticket), activation.ExpiresAt);
    }

    private string PayloadOf(long userId, ActivationIssue activation) =>
        JsonSerializer.Serialize(
            new AccountActivationPayload(UserPublicId: publicIds.TokenOf(userId), TicketId: activation.TicketId, ExpiresAt: activation.ExpiresAt),
            PayloadJson);
}
