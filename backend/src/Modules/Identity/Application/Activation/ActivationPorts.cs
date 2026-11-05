namespace Starter.Modules.Identity.Application.Activation;

/// <summary>The sealed-ticket purpose of activation links (bound into the ticket, so no other ticket opens as one).</summary>
public static class ActivationTicketPurpose
{
    /// <summary>The purpose value.</summary>
    public const string Value = "account-activation";
}

/// <summary>A freshly sealed activation ticket.</summary>
/// <param name="Ticket">The sealed, single-use ticket (the link fragment; never logged or stored).</param>
/// <param name="TicketId">The ticket's own random id, stored on the account and safe for logs and outbox payloads.</param>
/// <param name="ExpiresAt">When the ticket stops working.</param>
public sealed record IssuedActivationTicket(string Ticket, string TicketId, DateTimeOffset ExpiresAt)
{
    /// <summary>A log-safe description: the sealed ticket is never printed.</summary>
    public override string ToString() => nameof(IssuedActivationTicket) + "(" + TicketId + ")";
}

/// <summary>An activation ticket that opened (and was consumed: it never opens again).</summary>
/// <param name="SubjectId">The account's token subject the ticket was sealed for.</param>
/// <param name="TicketId">The ticket's id, compared with the account's pending link.</param>
public sealed record OpenedActivationTicket(Guid SubjectId, string TicketId);

/// <summary>Seals and opens activation tickets (over the Codelab.Security sealed one-time tickets).</summary>
public interface IActivationTickets
{
    /// <summary>False when sealed tickets are switched off in this deployment; then nothing can be issued or opened.</summary>
    bool IsAvailable { get; }

    /// <summary>Seals a ticket for <paramref name="subjectId"/> with the configured lifetime.</summary>
    /// <exception cref="InvalidOperationException">Tickets are not available (check <see cref="IsAvailable"/> first).</exception>
    IssuedActivationTicket Issue(Guid subjectId);

    /// <summary>
    /// Opens and consumes <paramref name="ticket"/>; null when it is malformed, forged, sealed for another purpose,
    /// expired, already used, or the replay store could not answer (fail closed), or when tickets are not available.
    /// </summary>
    Task<OpenedActivationTicket?> OpenAsync(string ticket, CancellationToken cancellationToken);
}

/// <summary>The password rule an activation enforces (from <c>Identity:Password</c>).</summary>
/// <param name="MinLength">The shortest password, in characters.</param>
/// <param name="MinDistinctCharacters">The fewest distinct characters a password may have.</param>
public sealed record PasswordPolicy(int MinLength, int MinDistinctCharacters);
