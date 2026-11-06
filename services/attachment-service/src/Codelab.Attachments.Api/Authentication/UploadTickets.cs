using Codelab.Security;

namespace Codelab.Attachments.Api.Authentication;

/// <summary>
/// The upload-ticket contract shared with the API (phase 6 contract 1). The API seals a ticket with
/// <c>ISealedTicketService.Seal(new TicketRequest(Purpose, subject, lifetime))</c>; the browser sends it in
/// <see cref="HeaderName"/>; this service opens it once and binds every receipt to its subject.
/// </summary>
public static class UploadTickets
{
    /// <summary>The ticket purpose (bound into the ticket's AAD: a ticket sealed for anything else never opens here).</summary>
    public const string Purpose = "attachment-upload";

    /// <summary>The claim that carries the opened ticket's subject (the receipt scope).</summary>
    public const string ScopeClaimType = "codelab_upload_scope";

    /// <summary>The claim that carries the opened ticket's id (for logs; never a credential).</summary>
    public const string TicketIdClaimType = "codelab_upload_ticket";

    private const string HeaderPrefix = "X-";
    private const string HeaderSuffix = "-Upload-Ticket";

    /// <summary>The request header that carries the ticket: <c>X-{Prefix}-Upload-Ticket</c>, from the protocol prefix.</summary>
    public static string HeaderName(SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return HeaderPrefix + protocol.Prefix + HeaderSuffix;
    }
}
