using Codelab.Security.Uploads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Starter.Infrastructure.Startup;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Infrastructure.Persistence;

namespace Starter.Modules.Attachments.Infrastructure.Startup;

/// <summary>
/// Writes the attachment types owning modules declare (<see cref="AttachmentTypeDeclarations"/>) to
/// <c>attachments.ServiceAttachments</c>, by the one rule <see cref="AttachmentTypeSeeding.Plan"/>: insert what is
/// missing, refresh what changed, leave the rest. Idempotent. When two nodes start together and both insert, the loser's
/// save fails on the unique key; it then plans once more against what the winner wrote.
/// </summary>
public sealed class AttachmentTypeSynchronizer(AttachmentsDbContext context, AttachmentTypeDeclarations declarations)
{
    /// <summary>Synchronizes the declared types; returns what it wrote.</summary>
    /// <exception cref="InvalidOperationException">A declared type conflicts with a stored one (see <see cref="AttachmentTypeSeeding.Plan"/>).</exception>
    public async Task<AttachmentTypeSeedReport> SynchronizeAsync(CancellationToken cancellationToken)
    {
        if (declarations.Seeds.Count == 0)
        {
            return new AttachmentTypeSeedReport(Added: 0, Refreshed: 0, Unchanged: 0);
        }

        try
        {
            return await WriteAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            return await WriteAsync(cancellationToken);
        }
    }

    private async Task<AttachmentTypeSeedReport> WriteAsync(CancellationToken cancellationToken)
    {
        var stored = await context.Types.ToListAsync(cancellationToken);
        var plan = AttachmentTypeSeeding.Plan(stored, declarations.Seeds);
        if (plan.IsEmpty)
        {
            return new AttachmentTypeSeedReport(Added: 0, Refreshed: 0, Unchanged: plan.UnchangedCount);
        }

        context.Types.AddRange(plan.ToAdd);
        var refreshed = plan.ApplyRefreshes();
        await context.SaveChangesAsync(cancellationToken);
        return new AttachmentTypeSeedReport(Added: plan.ToAdd.Count, Refreshed: refreshed, Unchanged: plan.UnchangedCount);
    }
}

/// <summary>What one synchronisation wrote.</summary>
/// <param name="Added">Types inserted.</param>
/// <param name="Refreshed">Types whose names or rules were updated.</param>
/// <param name="Unchanged">Declared types already stored as declared.</param>
public sealed record AttachmentTypeSeedReport(int Added, int Refreshed, int Unchanged);

/// <summary>
/// Runs <see cref="AttachmentTypeSynchronizer"/> on every start (<see cref="StartupPhase.ReferenceData"/>, after the
/// migrations), after checking the declared types against the ONE upload allowlist
/// (<see cref="AttachmentCatalogCheck.EnsureFits"/>), so a declared type that promises a refused extension stops the host
/// before it is written. Independent of <see cref="AttachmentCatalogStartupCheck"/>, which checks the stored rows.
/// </summary>
public sealed partial class AttachmentTypeSeedStartupTask(
    AttachmentTypeSynchronizer synchronizer,
    AttachmentTypeDeclarations declarations,
    IOptions<UploadSafetyOptions> uploads,
    ILogger<AttachmentTypeSeedStartupTask> logger) : IStartupTask
{
    /// <inheritdoc />
    public StartupPhase Phase => StartupPhase.ReferenceData;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        AttachmentCatalogCheck.EnsureFits(declarations.Types, AttachmentCatalogCheck.AllowlistOf(uploads.Value));
        var report = await synchronizer.SynchronizeAsync(cancellationToken);
        LogSynchronized(logger, report.Added, report.Refreshed, report.Unchanged);
    }

    [LoggerMessage(EventId = 13003, Level = LogLevel.Information, Message = "Declared attachment types synchronized: {Added} added, {Refreshed} refreshed, {Unchanged} unchanged")]
    private static partial void LogSynchronized(ILogger logger, int added, int refreshed, int unchanged);
}
