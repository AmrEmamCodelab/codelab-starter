using Starter.Modules.Identity.Application.Activation;
using Starter.Modules.Identity.Application.Auditing;

namespace Starter.Modules.Identity.Tests.Fakes;

/// <summary>Keeps every recorded auth event, in order.</summary>
internal sealed class RecordingAuthEventSink : IAuthEventSink
{
    public List<AuthEventRecord> Events { get; } = [];

    public Task RecordAsync(AuthEventRecord authEvent, CancellationToken cancellationToken)
    {
        Events.Add(authEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Activation tickets in memory: a ticket is <c>ticket:&lt;n&gt;</c>, opens once, and stops opening once the clock passes
/// its expiry, like the sealed tickets.
/// </summary>
internal sealed class FakeActivationTickets(TimeProvider clock) : IActivationTickets
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    private readonly Dictionary<string, (Guid Subject, string TicketId, DateTimeOffset ExpiresAt)> _issued = [];
    private readonly HashSet<string> _used = [];

    public bool IsAvailable { get; set; } = true;

    public List<IssuedActivationTicket> Issued { get; } = [];

    public IssuedActivationTicket Issue(Guid subjectId)
    {
        var number = Issued.Count + 1;
        var issued = new IssuedActivationTicket(Ticket: "ticket:" + number, TicketId: "id-" + number, ExpiresAt: clock.GetUtcNow() + Lifetime);
        _issued[issued.Ticket] = (subjectId, issued.TicketId, issued.ExpiresAt);
        Issued.Add(issued);
        return issued;
    }

    public Task<OpenedActivationTicket?> OpenAsync(string ticket, CancellationToken cancellationToken)
    {
        if (!IsAvailable || !_issued.TryGetValue(ticket, out var issued) || issued.ExpiresAt <= clock.GetUtcNow() || !_used.Add(ticket))
        {
            return Task.FromResult<OpenedActivationTicket?>(null);
        }

        return Task.FromResult<OpenedActivationTicket?>(new OpenedActivationTicket(issued.Subject, issued.TicketId));
    }
}
