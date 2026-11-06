using System.Security.Claims;
using System.Text.Encodings.Web;
using Codelab.Attachments.Api.Diagnostics;
using Codelab.Attachments.Api.Endpoints;
using Codelab.Security;
using Codelab.Security.Tickets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Api.Authentication;

/// <summary>Options of the upload-ticket scheme (nothing to set: the ring lives in <c>Codelab:Security:Tickets</c>,
/// which is the API's dedicated <c>Codelab:Security:UploadTickets</c> ring, never the API's own ticket ring).</summary>
public sealed class UploadTicketAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// The browser's only credential: a sealed one-time ticket in <c>X-{Prefix}-Upload-Ticket</c>, opened for
/// <see cref="UploadTickets.Purpose"/> and consumed by the opening (a second use is refused). The principal carries
/// the ticket subject as <see cref="UploadTickets.ScopeClaimType"/>, the scope every receipt is signed for. Cookies,
/// <c>Authorization</c> and API keys are never read. A missing, invalid, expired or reused ticket is challenged with
/// 401 <c>ATTACHMENTS_TICKET_INVALID</c>; a replay store that cannot answer, with 503 (fail closed).
/// </summary>
public sealed class UploadTicketAuthenticationHandler(
    IOptionsMonitor<UploadTicketAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISealedTicketService tickets,
    SecurityProtocol protocol)
    : AuthenticationHandler<UploadTicketAuthenticationOptions>(options, loggerFactory, encoder)
{
    /// <summary>The scheme name.</summary>
    public const string SchemeName = "CodelabUploadTicket";

    private TicketOpenStatus? _refusal;

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers[UploadTickets.HeaderName(protocol)].ToString();
        if (token.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var opened = await tickets.OpenAsync(UploadTickets.Purpose, token, Context.RequestAborted);
        if (opened is not { Status: TicketOpenStatus.Valid, Ticket: { } ticket } || string.IsNullOrWhiteSpace(ticket.Subject))
        {
            _refusal = opened.Status is TicketOpenStatus.Valid ? TicketOpenStatus.Invalid : opened.Status;
            AttachmentsApiLog.TicketRefused(Logger, _refusal.Value);
            return AuthenticateResult.Fail(_refusal.Value.ToString());
        }

        Claim[] claims = [new(UploadTickets.ScopeClaimType, ticket.Subject), new(UploadTickets.TicketIdClaimType, ticket.Id)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var refusal = _refusal is TicketOpenStatus.StoreUnavailable
            ? AttachmentProblems.TicketStoreUnavailable()
            : AttachmentProblems.TicketInvalid();
        return refusal.ExecuteAsync(Context);
    }
}
