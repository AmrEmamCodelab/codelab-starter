using System.Text.Json;
using Codelab.Security.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Starter.Modules.Identity.Application.LoginModes;

namespace Starter.Modules.Identity.Infrastructure.LoginModes;

/// <summary>The sealed-ticket purposes of the SSO flow (bound into each ticket's AAD, so one never opens as the other).</summary>
public static class SsoTicketPurposes
{
    /// <summary>The pending sign-in kept in the browser's sign-in cookie.</summary>
    public const string Pending = "identity-sso-pending";

    /// <summary>The one-time exchange ticket handed to the SPA in the URL fragment.</summary>
    public const string Exchange = "identity-sso-exchange";

    /// <summary>The subject of an exchange ticket: the user is known only from the sealed refresh token.</summary>
    public const string ExchangeSubject = "sso-session";
}

/// <summary>
/// <see cref="ISsoTickets"/> over the Codelab.Security sealed one-time tickets: AES-GCM sealed, bound to the purpose,
/// expiring on the injected clock and opened once through the replay guard (so a sign-in cookie or a ticket replayed
/// from another browser, node or tab is refused). The payload is JSON inside the sealed data, never readable by the
/// holder. The ticket service is resolved on first use: SSO requires <c>Codelab:Security:Tickets:Enabled</c> at start.
/// </summary>
public sealed class SealedSsoTickets(IOptions<SealedTicketOptions> options, IServiceProvider services) : ISsoTickets
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public string SealPending(SsoPendingSignIn pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return Seal(new TicketRequest(Purpose: SsoTicketPurposes.Pending, Subject: pending.State, Lifetime: SsoLimits.PendingLifetime) { Data = JsonSerializer.Serialize(pending, Json) });
    }

    /// <inheritdoc />
    public Task<SealedValue<SsoPendingSignIn>> OpenPendingAsync(string sealedValue, CancellationToken cancellationToken) =>
        OpenAsync<SsoPendingSignIn>(purpose: SsoTicketPurposes.Pending, token: sealedValue, cancellationToken);

    /// <inheritdoc />
    public string SealExchange(SsoExchangeGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return Seal(new TicketRequest(Purpose: SsoTicketPurposes.Exchange, Subject: SsoTicketPurposes.ExchangeSubject, Lifetime: SsoLimits.ExchangeLifetime)
        {
            Data = JsonSerializer.Serialize(grant, Json),
        });
    }

    /// <inheritdoc />
    public Task<SealedValue<SsoExchangeGrant>> OpenExchangeAsync(string ticket, CancellationToken cancellationToken) =>
        OpenAsync<SsoExchangeGrant>(purpose: SsoTicketPurposes.Exchange, token: ticket, cancellationToken);

    private string Seal(TicketRequest request) =>
        options.Value.Enabled
            ? services.GetRequiredService<ISealedTicketService>().Seal(request)
            : throw new InvalidOperationException($"{SealedTicketOptions.SectionName} is disabled; SSO needs sealed tickets.");

    private async Task<SealedValue<T>> OpenAsync<T>(string purpose, string token, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!options.Value.Enabled)
        {
            return SealedValue.Refused<T>(SealedValueStatus.Unavailable);
        }

        var opened = await services.GetRequiredService<ISealedTicketService>().OpenAsync(purpose: purpose, token: token, cancellationToken);
        return opened switch
        {
            { Status: TicketOpenStatus.Valid, Ticket.Data: { Length: > 0 } data } => Payload<T>(data),
            { Status: TicketOpenStatus.StoreUnavailable } => SealedValue.Refused<T>(SealedValueStatus.Unavailable),
            _ => SealedValue.Refused<T>(SealedValueStatus.Rejected),
        };
    }

    private static SealedValue<T> Payload<T>(string data)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, Json) is { } value
                ? new SealedValue<T>(SealedValueStatus.Opened, value)
                : SealedValue.Refused<T>(SealedValueStatus.Rejected);
        }
        catch (JsonException)
        {
            return SealedValue.Refused<T>(SealedValueStatus.Rejected);
        }
    }
}
