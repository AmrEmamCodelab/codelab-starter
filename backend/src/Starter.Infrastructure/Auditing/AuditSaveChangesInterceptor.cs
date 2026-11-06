using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Starter.Application.Auditing;

namespace Starter.Infrastructure.Auditing;

/// <summary>
/// The one place where entity changes become audit rows. <c>AddModuleDbContext</c> attaches it to every module
/// DbContext, so every save (a use case, a job, a startup task) is audited without a call site knowing it: added,
/// modified and deleted rows of every audited entity land in <c>audit.AuditEntries</c> with the entity type and key,
/// the action, the changed values (personal data masked, secrets left out), the actor from
/// <see cref="IAuditContextAccessor"/> (<see cref="AuditActors.System"/> outside a user's request), the correlation id
/// and the <see cref="TimeProvider"/> time.
/// </summary>
/// <remarks>
/// <para><b>One unit.</b> Before the save, the changes are captured and the writer begins (opening a transaction when
/// the caller has none); after the save, the inserts are completed with their database keys and the rows are written
/// and committed. A failed or cancelled save rolls the opened transaction back.</para>
/// <para><b>Failures propagate.</b> A change that silently escaped the trail is worse than a visible error the caller can
/// retry, so a failure to write the trail fails the save.</para>
/// <para>It is scoped and may serve several contexts of one scope, so the state of a save is kept per context.</para>
/// </remarks>
public sealed class AuditSaveChangesInterceptor(
    IAuditContextAccessor auditContext,
    TimeProvider timeProvider,
    IAuditTrailWriter writer) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, PendingSave> _pending = [];

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Abandon(eventData.Context);
        if (CaptureOf(eventData.Context) is { } capture)
        {
            _pending.AddOrUpdate(eventData.Context!, new PendingSave(capture, writer.Begin(eventData.Context!)));
        }

        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        await AbandonAsync(eventData.Context);
        if (CaptureOf(eventData.Context) is { } capture)
        {
            var session = await writer.BeginAsync(eventData.Context!, cancellationToken);
            _pending.AddOrUpdate(eventData.Context!, new PendingSave(capture, session));
        }

        return result;
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (Take(eventData.Context) is { } pending)
        {
            using var session = pending.Session;
            session.Commit(pending.Capture.Complete());
        }

        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (Take(eventData.Context) is { } pending)
        {
            await using var session = pending.Session;
            await session.CommitAsync(pending.Capture.Complete(), cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Abandon(eventData?.Context);

    /// <inheritdoc />
    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) =>
        await AbandonAsync(eventData?.Context);

    /// <inheritdoc />
    public override void SaveChangesCanceled(DbContextEventData eventData) => Abandon(eventData?.Context);

    /// <inheritdoc />
    public override async Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default) =>
        await AbandonAsync(eventData?.Context);

    /// <summary>True when <paramref name="context"/> is audited: any module context except the audit trail's own.</summary>
    public static bool Audits(DbContext? context) => context is not null and not AuditDbContext;

    private AuditCapture? CaptureOf(DbContext? context)
    {
        if (!Audits(context))
        {
            return null;
        }

        var capture = AuditCapture.Take(context!, StampNow());
        return capture.IsEmpty ? null : capture;
    }

    private AuditStamp StampNow()
    {
        var current = auditContext.Current;
        var actor = current.SubjectId is { } subject ? subject.ToString() : AuditActors.System;
        return new AuditStamp(actor, current.CorrelationId, timeProvider.GetUtcNow());
    }

    private PendingSave? Take(DbContext? context) =>
        context is not null && _pending.TryGetValue(context, out var pending) && _pending.Remove(context) ? pending : null;

    private void Abandon(DbContext? context) => Take(context)?.Session.Dispose();

    private async Task AbandonAsync(DbContext? context)
    {
        if (Take(context) is { } pending)
        {
            await pending.Session.DisposeAsync();
        }
    }

    private sealed record PendingSave(AuditCapture Capture, IAuditWriteSession Session);
}
