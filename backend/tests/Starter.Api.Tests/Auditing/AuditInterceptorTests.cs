using Microsoft.EntityFrameworkCore;
using Starter.Application.Auditing;
using Starter.Domain.Auditing;
using Starter.Infrastructure.Auditing;

namespace Starter.Api.Tests.Auditing;

/// <summary>The interceptor's save protocol over a recording writer and a fake database save (no SQL).</summary>
public sealed class AuditInterceptorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 15, 0, TimeSpan.Zero);
    private static readonly Guid Subject = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    [Fact]
    public async Task A_save_by_a_user_is_written_with_the_subject_the_correlation_id_and_the_clock()
    {
        var writer = new RecordingAuditWriter();
        await using var context = Context(writer, new AuditContext(Subject, ClientAddress: "192.0.2.1", CorrelationId: "trace-1"));
        context.People.Add(AuditCaptureTests.NewPerson());

        await context.SaveChangesAsync();

        var row = Assert.Single(writer.Written);
        Assert.Equal(Subject.ToString(), row.Actor);
        Assert.Equal("trace-1", row.CorrelationId);
        Assert.Equal(Now, row.At);
        var session = Assert.Single(writer.Sessions);
        Assert.True(session.Committed);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task A_save_without_a_user_is_written_as_the_system_actor()
    {
        var writer = new RecordingAuditWriter();
        await using var context = Context(writer, AuditContext.System with { CorrelationId = "job-7" });
        context.Teams.Add(new ProbeTeam { Name = "Night shift" });

        await context.SaveChangesAsync();

        var row = Assert.Single(writer.Written);
        Assert.Equal(AuditActors.System, row.Actor);
        Assert.Equal("job-7", row.CorrelationId);
    }

    [Fact]
    public async Task An_insert_is_written_with_the_key_the_database_assigned()
    {
        var writer = new RecordingAuditWriter();
        await using var context = Context(writer, AuditContext.System);
        context.Teams.Add(new ProbeTeam { Name = "Team" });

        await context.SaveChangesAsync();

        var row = Assert.Single(writer.Written);
        Assert.Equal("100", row.EntityKey);
        Assert.Equal("100", AuditCaptureTests.Changes(row)[nameof(ProbeTeam.Id)].NewValue);
    }

    [Fact]
    public void The_synchronous_save_follows_the_same_protocol()
    {
        var writer = new RecordingAuditWriter();
        using var context = Context(writer, AuditContext.System);
        context.Teams.Add(new ProbeTeam { Name = "Team" });

        context.SaveChanges();

        Assert.Equal(AuditAction.Added, Assert.Single(writer.Written).Action);
        Assert.True(Assert.Single(writer.Sessions).Committed);
    }

    [Fact]
    public async Task A_save_with_nothing_audited_begins_no_write()
    {
        var writer = new RecordingAuditWriter();
        await using var context = Context(writer, AuditContext.System);
        context.Heartbeats.Add(new ProbeHeartbeat { Id = 1, At = Now });

        await context.SaveChangesAsync();

        Assert.Empty(writer.Sessions);
    }

    [Fact]
    public async Task A_failed_save_disposes_the_write_without_committing_it()
    {
        var writer = new RecordingAuditWriter();
        await using var context = AuditProbeContext.Offline(new FakeDatabaseSave(fail: true), Interceptor(writer, AuditContext.System));
        context.Teams.Add(new ProbeTeam { Name = "Team" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        var session = Assert.Single(writer.Sessions);
        Assert.False(session.Committed);
        Assert.True(session.Disposed);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public void A_failed_synchronous_save_disposes_the_write_without_committing_it()
    {
        var writer = new RecordingAuditWriter();
        using var context = AuditProbeContext.Offline(new FakeDatabaseSave(fail: true), Interceptor(writer, AuditContext.System));
        context.Teams.Add(new ProbeTeam { Name = "Team" });

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        var session = Assert.Single(writer.Sessions);
        Assert.False(session.Committed);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Two_saves_of_one_context_write_two_batches()
    {
        var writer = new RecordingAuditWriter();
        await using var context = Context(writer, AuditContext.System);
        context.Teams.Add(new ProbeTeam { Name = "One" });
        await context.SaveChangesAsync();
        context.Teams.Add(new ProbeTeam { Name = "Two" });

        await context.SaveChangesAsync();

        Assert.Equal(2, writer.Sessions.Count);
        Assert.All(writer.Sessions, session => Assert.True(session.Committed));
        Assert.Equal(2, writer.Written.Count);
    }

    [Fact]
    public void The_audit_trail_context_is_never_audited_and_module_contexts_are()
    {
        using var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer("Server=.;Database=NeverOpened").Options);
        using var module = AuditProbeContext.Offline();

        Assert.False(AuditSaveChangesInterceptor.Audits(audit));
        Assert.False(AuditSaveChangesInterceptor.Audits(context: null));
        Assert.True(AuditSaveChangesInterceptor.Audits(module));
    }

    private static AuditProbeContext Context(RecordingAuditWriter writer, AuditContext current) =>
        AuditProbeContext.Offline(Interceptor(writer, current), new FakeDatabaseSave());

    private static AuditSaveChangesInterceptor Interceptor(RecordingAuditWriter writer, AuditContext current) =>
        new(new FixedAuditContext(current), new StoppedClock(Now), writer);
}
