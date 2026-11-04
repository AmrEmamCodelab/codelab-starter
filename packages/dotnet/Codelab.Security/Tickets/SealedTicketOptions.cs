using Codelab.Security.Keys;

namespace Codelab.Security.Tickets;

/// <summary>
/// Sealed one-time tickets (<c>Codelab:Security:Tickets</c>): account activation, password reset, upload grants.
/// Disabled until <see cref="Enabled"/> is set; then the key ring is required and guarded like every other ring.
/// </summary>
public sealed class SealedTicketOptions : KeyRingOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.Tickets;

    /// <summary>Register and validate the ticket service.</summary>
    public bool Enabled { get; set; }

    /// <summary>The longest lifetime a ticket may be sealed with.</summary>
    public int MaxLifetimeSeconds { get; set; } = 7 * 24 * 60 * 60;
}

/// <summary>What to seal: a purpose (bound into the AAD), the subject it is for, a lifetime and optional opaque data.</summary>
public sealed record TicketRequest(string Purpose, string Subject, TimeSpan Lifetime)
{
    /// <summary>Opaque application data carried sealed inside the ticket (never readable by the holder).</summary>
    public string? Data { get; init; }
}

/// <summary>An opened ticket.</summary>
public sealed record SealedTicket(string Id, string Purpose, string Subject, DateTimeOffset ExpiresAt, string? Data);

/// <summary>Why a ticket did or did not open.</summary>
public enum TicketOpenStatus
{
    /// <summary>Malformed or forged; sealed for another purpose; or under a key outside the ring.</summary>
    Invalid = 0,

    /// <summary>Opened and consumed: this was its one use.</summary>
    Valid = 1,

    /// <summary>Authentic but past its expiry.</summary>
    Expired = 2,

    /// <summary>Authentic but already used.</summary>
    AlreadyUsed = 3,

    /// <summary>The replay store could not answer; try again (fail closed).</summary>
    StoreUnavailable = 4,
}

/// <summary>The result of opening a ticket; <see cref="Ticket"/> is set only when <see cref="Status"/> is Valid.</summary>
public sealed record TicketOpenResult(TicketOpenStatus Status, SealedTicket? Ticket)
{
    /// <summary>A result with no ticket.</summary>
    public static TicketOpenResult Refused(TicketOpenStatus status) => new(status, Ticket: null);
}

/// <summary>Seals and opens one-time tickets.</summary>
public interface ISealedTicketService
{
    /// <summary>Seals a new ticket and returns its token.</summary>
    string Seal(TicketRequest request);

    /// <summary>Opens <paramref name="token"/> for <paramref name="purpose"/> and consumes it (single use).</summary>
    Task<TicketOpenResult> OpenAsync(string purpose, string token, CancellationToken cancellationToken);
}
