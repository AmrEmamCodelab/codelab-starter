namespace Starter.Application.Attachments;

/// <summary>
/// A module that serves the <see cref="IAttachableRecordAccess"/> of several record types at once, resolved per request:
/// the Records module publishes one per registered record type, so a record-type module needs no host wiring for its
/// files. The Attachments module reads every source next to the single <see cref="IAttachableRecordAccess"/>
/// registrations; two accesses for one record type stop the request as a wiring fault.
/// </summary>
public interface IAttachableRecordAccessSource
{
    /// <summary>The accesses this source serves, at most one per record type.</summary>
    IEnumerable<IAttachableRecordAccess> Accesses { get; }
}
