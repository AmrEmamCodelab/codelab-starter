using Starter.Application.Attachments;

namespace Starter.Modules.Records.Application;

/// <summary>
/// The <see cref="IAttachableRecordAccess"/> of one record type served by Records, so a record's files are never more
/// visible than the record: whoever may see the record (owner with view-own, permitted staff, oversight) may view its
/// files; only the owner may change them, and only while the record is editable (its initial status, or a status a
/// resubmit leaves, see <see cref="RegisteredRecordType.IsEditable"/>). A missing record answers false.
/// </summary>
public sealed class RecordAttachmentAccess(string recordType, string attachableRecordType, RecordLoader loader) : IAttachableRecordAccess
{
    /// <inheritdoc />
    public string RecordType { get; } = attachableRecordType;

    /// <inheritdoc />
    public async Task<bool> CanViewAsync(long recordId, CancellationToken cancellationToken) =>
        (await loader.LoadAsync(recordType, recordId, cancellationToken)).IsSuccess;

    /// <inheritdoc />
    public async Task<bool> CanChangeAsync(long recordId, CancellationToken cancellationToken)
    {
        var load = await loader.LoadAsync(recordType, recordId, cancellationToken);
        return load.IsSuccess && load.Value.Standing.IsOwner && load.Value.Type.IsEditable(load.Value.Record.Status);
    }
}

/// <summary>
/// One <see cref="RecordAttachmentAccess"/> per registered record type (keyed by its attachable record type), published
/// to the Attachments module as the kernel <see cref="IAttachableRecordAccessSource"/>, so every record type's files
/// work without host wiring.
/// </summary>
public sealed class RecordAttachmentAccesses(RecordTypes types, RecordLoader loader) : IAttachableRecordAccessSource
{
    /// <inheritdoc />
    public IEnumerable<IAttachableRecordAccess> Accesses => All;

    /// <summary>The accesses, one per record type, ordered by record type.</summary>
    public IReadOnlyList<IAttachableRecordAccess> All =>
        [.. types.All.Select(type => new RecordAttachmentAccess(type.RecordType, type.Registration.AttachableRecordType, loader))];

    /// <summary>The access of the record type whose attachable record type is <paramref name="attachableRecordType"/>, or null.</summary>
    public IAttachableRecordAccess? For(string attachableRecordType)
    {
        var type = types.FindByAttachableType(attachableRecordType);
        return type is null ? null : new RecordAttachmentAccess(type.RecordType, type.Registration.AttachableRecordType, loader);
    }
}
