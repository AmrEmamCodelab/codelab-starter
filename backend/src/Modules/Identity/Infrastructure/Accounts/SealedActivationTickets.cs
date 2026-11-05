using System.Globalization;
using System.Security.Cryptography;
using Codelab.Security.Tickets;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Starter.Modules.Identity.Application.Activation;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure.Options;

namespace Starter.Modules.Identity.Infrastructure.Accounts;

/// <summary>
/// <see cref="IActivationTickets"/> over the Codelab.Security sealed one-time tickets (purpose
/// <see cref="ActivationTicketPurpose.Value"/>): AES-GCM sealed, bound to the purpose, opened once through the replay
/// guard. The ticket's subject is the account's token subject; its sealed data is a random ticket id of this module,
/// stored on the account, so a newer link supersedes an older one. The ticket service is resolved only when tickets
/// are enabled (<c>Codelab:Security:Tickets:Enabled</c>); otherwise <see cref="IsAvailable"/> is false.
/// </summary>
public sealed class SealedActivationTickets(
    IOptions<SealedTicketOptions> ticketOptions,
    IOptions<ActivationOptions> activation,
    IServiceProvider services,
    TimeProvider clock) : IActivationTickets
{
    /// <inheritdoc />
    public bool IsAvailable => ticketOptions.Value.Enabled;

    /// <inheritdoc />
    public IssuedActivationTicket Issue(Guid subjectId)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException($"{SealedTicketOptions.SectionName} is disabled; check {nameof(IsAvailable)} before issuing.");
        }

        var lifetime = Lifetime();
        var ticketId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(IdentityLimits.ActivationTicketIdBytes));
        var expiresAt = clock.GetUtcNow() + lifetime;
        var ticket = Tickets().Seal(new TicketRequest(ActivationTicketPurpose.Value, subjectId.ToString(), lifetime) { Data = ticketId });
        return new IssuedActivationTicket(Ticket: ticket, TicketId: ticketId, ExpiresAt: expiresAt);
    }

    /// <inheritdoc />
    public async Task<OpenedActivationTicket?> OpenAsync(string ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!IsAvailable)
        {
            return null;
        }

        var opened = await Tickets().OpenAsync(ActivationTicketPurpose.Value, ticket, cancellationToken);
        if (opened is not { Status: TicketOpenStatus.Valid, Ticket: { Data: { Length: > 0 } ticketId } sealedTicket }
            || !Guid.TryParse(sealedTicket.Subject, CultureInfo.InvariantCulture, out var subjectId))
        {
            return null;
        }

        return new OpenedActivationTicket(SubjectId: subjectId, TicketId: ticketId);
    }

    private TimeSpan Lifetime()
    {
        var configured = activation.Value.TicketLifetime;
        var ceiling = TimeSpan.FromSeconds(ticketOptions.Value.MaxLifetimeSeconds);
        return configured <= ceiling ? configured : ceiling;
    }

    private ISealedTicketService Tickets() => services.GetRequiredService<ISealedTicketService>();
}
