using Codelab.Security;
using Codelab.Security.Replay;
using Codelab.Security.Tickets;
using Microsoft.Extensions.Options;
using Starter.Modules.Attachments.Infrastructure.Options;

namespace Starter.Modules.Attachments.Infrastructure.Security;

/// <summary>
/// The module's own sealed-ticket service over the upload-ticket ring (<see cref="UploadTicketRingOptions.SectionName"/>),
/// the ring the attachment service holds. Only <see cref="UploadTicketIssuer"/> uses it; it is registered as this
/// concrete type and never as <see cref="ISealedTicketService"/>, so the API's SSO and activation tickets keep sealing
/// with the API's own ring and an upload ticket can never be sealed with it.
/// </summary>
public sealed class UploadTicketRing : ISealedTicketService
{
    private readonly SealedTicketService _tickets;

    /// <summary>Builds the service from the validated upload-ticket ring.</summary>
    /// <exception cref="InvalidOperationException">The ring is disabled (or structurally invalid).</exception>
    public UploadTicketRing(IOptions<UploadTicketRingOptions> options, IReplayGuard replayGuard, TimeProvider clock, SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(options);
        var ring = options.Value;
        if (!ring.Enabled)
        {
            throw new InvalidOperationException($"{UploadTicketRingOptions.SectionName} is disabled; enable it (with its own key ring) before issuing upload tickets.");
        }

        _tickets = new SealedTicketService(ring.ToSealedTicketOptions(), replayGuard, clock, protocol);
    }

    /// <inheritdoc />
    public string Seal(TicketRequest request) => _tickets.Seal(request);

    /// <inheritdoc />
    public Task<TicketOpenResult> OpenAsync(string purpose, string token, CancellationToken cancellationToken) =>
        _tickets.OpenAsync(purpose, token, cancellationToken);
}
