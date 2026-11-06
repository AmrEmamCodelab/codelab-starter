using Codelab.Security;
using Codelab.Security.Tickets;
using Microsoft.Extensions.Options;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Options;
using Starter.Modules.Attachments.Infrastructure.Storage;

namespace Starter.Modules.Attachments.Infrastructure.Security;

/// <summary>
/// The <see cref="IUploadTicketIssuer"/> over the module's <see cref="UploadTicketRing"/>: a ticket of purpose
/// <see cref="UploadTickets.Purpose"/> whose subject is the upload scope, living
/// <c>Attachments:TicketLifetimeSeconds</c>, with the header <c>X-{Prefix}-Upload-Ticket</c> and the service's upload
/// URL. The attachment service opens it once (the replay guard) with the same ring (its only ticket ring) and echoes the
/// subject as the receipt scope. Needs <c>Codelab:Security:UploadTickets:Enabled</c>; never the API's own
/// <c>Codelab:Security:Tickets</c> ring.
/// </summary>
public sealed class UploadTicketIssuer(
    UploadTicketRing tickets,
    IOptions<AttachmentsOptions> options,
    SecurityProtocol protocol,
    TimeProvider clock) : IUploadTicketIssuer
{
    /// <inheritdoc />
    /// <remarks>
    /// <see cref="IssuedUploadTicket.ExpiresAt"/> is read before sealing and cut to whole seconds, as the ticket stores
    /// it, so it is never later than the moment the ticket stops opening.
    /// </remarks>
    public IssuedUploadTicket Issue(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var settings = options.Value;
        var lifetime = settings.TicketLifetime;
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds((clock.GetUtcNow() + lifetime).ToUnixTimeSeconds());
        var ticket = tickets.Seal(new TicketRequest(UploadTickets.Purpose, scope, lifetime));
        return new IssuedUploadTicket(
            Ticket: ticket,
            TicketHeader: AttachmentServiceRoutes.UploadTicketHeader(protocol),
            UploadUrl: new Uri(settings.ServiceRoot(), AttachmentServiceRoutes.Uploads).AbsoluteUri,
            ExpiresAt: expiresAt);
    }
}
