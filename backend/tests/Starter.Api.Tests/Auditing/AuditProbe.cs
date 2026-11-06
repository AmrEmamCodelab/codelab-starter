using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Application.Auditing;
using Starter.Domain.Auditing;
using Starter.Infrastructure.Auditing;
using Starter.Infrastructure.Persistence;

namespace Starter.Api.Tests.Auditing;

public enum ProbeState
{
    Draft = 1,
    Closed = 7,
}

/// <summary>A record with one property of every audit treatment.</summary>
public sealed class ProbePerson
{
    public long Id { get; set; }

    [PersonalData(PersonalDataKind.NationalId)]
    public string NationalNumber { get; set; } = string.Empty;

    [PersonalData(PersonalDataKind.Email)]
    public string Contact { get; set; } = string.Empty;

    [PersonalData(PersonalDataKind.Phone)]
    public string? Cell { get; set; }

    [PersonalData(PersonalDataKind.Name)]
    public string Label { get; set; } = string.Empty;

    [PersonalData(PersonalDataKind.FreeText)]
    public string? Remarks { get; set; }

    public string City { get; set; } = string.Empty;

    [NotAudited]
    public string CacheTag { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    public string? NormalizedEmail { get; set; }

    public byte[]? Photo { get; set; }

    public ProbeState State { get; set; }

    public long? TeamId { get; set; }
}

public sealed class ProbeTeam
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[NotAudited]
public sealed class ProbeHeartbeat
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }
}

public sealed class ProbeMembership
{
    public long PersonId { get; set; }

    public long TeamId { get; set; }

    public bool IsLead { get; set; }
}

public sealed class ProbeHistory : RecordHistoryEntry
{
    private ProbeHistory()
    {
    }

    public ProbeHistory(RecordHistoryFacts facts)
        : base(facts)
    {
    }
}

/// <summary>A module context in its own schema on a SQL Server connection that is never opened.</summary>
public sealed class AuditProbeContext(DbContextOptions<AuditProbeContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "probe";
    private const string NeverOpened = "Server=.;Database=NeverOpened";

    public DbSet<ProbePerson> People => Set<ProbePerson>();

    public DbSet<ProbeTeam> Teams => Set<ProbeTeam>();

    public DbSet<ProbeHeartbeat> Heartbeats => Set<ProbeHeartbeat>();

    public DbSet<ProbeMembership> Memberships => Set<ProbeMembership>();

    public DbSet<ProbeHistory> History => Set<ProbeHistory>();

    protected override string Schema => SchemaName;

    public static AuditProbeContext Offline(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AuditProbeContext>()
        .UseSqlServer(NeverOpened)
        .AddInterceptors(interceptors)
        .Options);
}

public sealed class ProbePersonConfiguration : IEntityTypeConfiguration<ProbePerson>
{
    public void Configure(EntityTypeBuilder<ProbePerson> builder)
    {
        builder.HasOne<ProbeTeam>().WithMany().HasForeignKey(person => person.TeamId);
        builder.Property(person => person.Id).ValueGeneratedNever();
    }
}

public sealed class ProbeMembershipConfiguration : IEntityTypeConfiguration<ProbeMembership>
{
    public void Configure(EntityTypeBuilder<ProbeMembership> builder) =>
        builder.HasKey(membership => new { membership.PersonId, membership.TeamId });
}

public sealed class ProbeHistoryConfiguration : IEntityTypeConfiguration<ProbeHistory>
{
    public void Configure(EntityTypeBuilder<ProbeHistory> builder)
    {
        builder.ToTable("ProbeHistory");
        builder.HasRecordHistory<ProbePerson, ProbeHistory>();
    }
}

/// <summary>An audit context the test controls.</summary>
public sealed class FixedAuditContext(AuditContext current) : IAuditContextAccessor
{
    public AuditContext Current { get; set; } = current;
}

/// <summary>A clock stopped at one instant.</summary>
public sealed class StoppedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Records what the interceptor hands the writer instead of touching a database.</summary>
public sealed class RecordingAuditWriter : IAuditTrailWriter
{
    public List<RecordingSession> Sessions { get; } = [];

    public IReadOnlyList<AuditEntry> Written => [.. Sessions.SelectMany(session => session.Written)];

    public IAuditWriteSession Begin(DbContext source) => Open();

    public Task<IAuditWriteSession> BeginAsync(DbContext source, CancellationToken cancellationToken) => Task.FromResult<IAuditWriteSession>(Open());

    private RecordingSession Open()
    {
        var session = new RecordingSession();
        Sessions.Add(session);
        return session;
    }
}

public sealed class RecordingSession : IAuditWriteSession
{
    public List<AuditEntry> Written { get; } = [];

    public bool Committed { get; private set; }

    public bool Disposed { get; private set; }

    public void Commit(IReadOnlyList<AuditEntry> rows)
    {
        Written.AddRange(rows);
        Committed = true;
    }

    public Task CommitAsync(IReadOnlyList<AuditEntry> rows, CancellationToken cancellationToken)
    {
        Commit(rows);
        return Task.CompletedTask;
    }

    public void Dispose() => Disposed = true;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Stands in for the database: answers the save without SQL, giving store-generated keys a real value first (as the
/// database would) and accepting the changes afterwards; with <c>fail</c> it fails the save after the batch (as a
/// database error would), so register it BEFORE the audit interceptor then.
/// </summary>
public sealed class FakeDatabaseSave(bool fail = false) : SaveChangesInterceptor
{
    private long _nextId = 100;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
        Answer(eventData.Context!);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Answer(eventData.Context!));

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) => Complete(eventData.Context!, result);

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Complete(eventData.Context!, result));

    private int Complete(DbContext context, int result)
    {
        if (fail)
        {
            throw new InvalidOperationException("The fake database refused the save.");
        }

        context.ChangeTracker.AcceptAllChanges();
        return result;
    }

    private InterceptionResult<int> Answer(DbContext context)
    {
        var added = context.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added).ToList();
        foreach (var entry in added.Where(entry => entry.Metadata.FindProperty(nameof(ProbeTeam.Id)) is not null))
        {
            var key = entry.Property(nameof(ProbeTeam.Id));
            if (key.IsTemporary)
            {
                key.CurrentValue = _nextId++;
                key.IsTemporary = false;
            }
        }

        return InterceptionResult<int>.SuppressWithResult(added.Count);
    }
}
