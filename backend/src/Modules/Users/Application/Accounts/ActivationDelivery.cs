using System.Text.Json;
using Starter.Application.Messaging;
using Starter.Application.Users;
using Starter.Domain.Users;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>How an activation link reaches the user. Sent on the wire as the integer value.</summary>
public enum ActivationDelivery
{
    /// <summary>The account has no e-mail address: the administrator receives the link and hands it over.</summary>
    AdministratorLink = 1,

    /// <summary>The link is e-mailed to the account's address by the outbox; the administrator never sees it.</summary>
    Email = 2,
}

/// <summary>Why an <see cref="OutboxMessageTypes.AccountActivation"/> message was delivered without an e-mail. Logged as the integer value.</summary>
public enum ActivationEmailSkip
{
    /// <summary>The payload names no account that exists.</summary>
    AccountNotFound = 1,

    /// <summary>The account signs in through Active Directory or SSO now.</summary>
    NotPasswordBound = 2,

    /// <summary>The break-glass administrator (configured, never activated by link).</summary>
    BreakGlass = 3,

    /// <summary>The account was deactivated since the link was asked for.</summary>
    Inactive = 4,

    /// <summary>The account already set its password (a link was redeemed since).</summary>
    AlreadyActivated = 5,

    /// <summary>The account's e-mail address was removed since.</summary>
    NoEmailAddress = 6,
}

/// <summary>The one owner of the activation delivery rules: who is e-mailed, and when a queued e-mail is no longer due.</summary>
public static class ActivationDeliveryRules
{
    /// <summary>E-mail when the account has an address, otherwise the administrator's link.</summary>
    public static ActivationDelivery DeliveryFor(AccountSummary account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return string.IsNullOrWhiteSpace(account.Email) ? ActivationDelivery.AdministratorLink : ActivationDelivery.Email;
    }

    /// <summary>
    /// Null when a fresh activation link should be e-mailed to <paramref name="account"/> now, otherwise why not. A
    /// queued message is stale once the account set its password, was deactivated or rebound, or lost its address:
    /// issuing a ticket then would clear a password the user just set.
    /// </summary>
    public static ActivationEmailSkip? SkipReasonOf(AccountSummary? account) => account switch
    {
        null => ActivationEmailSkip.AccountNotFound,
        { Binding: not AccountBinding.Password } => ActivationEmailSkip.NotPasswordBound,
        { IsBreakGlass: true } => ActivationEmailSkip.BreakGlass,
        { IsActive: false } => ActivationEmailSkip.Inactive,
        { HasPassword: true } => ActivationEmailSkip.AlreadyActivated,
        _ when DeliveryFor(account) != ActivationDelivery.Email => ActivationEmailSkip.NoEmailAddress,
        _ => null,
    };
}

/// <summary>Reads and writes <see cref="AccountActivationPayload"/> JSON (web casing), the one place that owns its shape.</summary>
public static class ActivationPayloads
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    /// <summary>The payload as JSON.</summary>
    public static string Write(AccountActivationPayload payload) => JsonSerializer.Serialize(payload, PayloadJson);

    /// <summary>The payload, or null when <paramref name="json"/> is not one (malformed, or the user id is missing).</summary>
    public static AccountActivationPayload? TryRead(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<AccountActivationPayload>(json, PayloadJson);
            return string.IsNullOrWhiteSpace(payload?.UserPublicId) ? null : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
