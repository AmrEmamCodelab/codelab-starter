using Microsoft.EntityFrameworkCore;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Infrastructure.Persistence;

/// <summary>
/// The <see cref="IAttachmentStore"/> over <see cref="AttachmentsDbContext"/>, and the module's unit of work. Reads
/// that the port documents as read-only use no tracking; <see cref="FindAsync"/> tracks, because its caller changes
/// the attachment and saves.
/// </summary>
public sealed class EfAttachmentStore(AttachmentsDbContext context) : IAttachmentStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceAttachment>> ListTypesAsync(string recordType, CancellationToken cancellationToken) =>
        await context.Types.AsNoTracking()
            .Where(type => type.RecordType == recordType)
            .OrderBy(type => type.Key)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ServiceAttachment?> FindTypeAsync(string recordType, string typeKey, CancellationToken cancellationToken) =>
        context.Types.AsNoTracking()
            .FirstOrDefaultAsync(type => type.RecordType == recordType && type.Key == typeKey, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordAttachment>> ListLiveAsync(string recordType, long recordId, CancellationToken cancellationToken) =>
        await context.Attachments.AsNoTracking()
            .Where(attachment => attachment.RecordType == recordType && attachment.RecordId == recordId && attachment.RemovedAt == null)
            .OrderBy(attachment => attachment.UploadedAt)
            .ThenBy(attachment => attachment.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<RecordAttachment?> FindAsync(long attachmentId, CancellationToken cancellationToken) =>
        context.Attachments.FirstOrDefaultAsync(attachment => attachment.Id == attachmentId, cancellationToken);

    /// <inheritdoc />
    public Task<RecordAttachment?> FindByFileIdAsync(string fileId, CancellationToken cancellationToken) =>
        context.Attachments.AsNoTracking().FirstOrDefaultAsync(attachment => attachment.FileId == fileId, cancellationToken);

    /// <inheritdoc />
    public void Add(RecordAttachment attachment) => context.Attachments.Add(attachment);

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
