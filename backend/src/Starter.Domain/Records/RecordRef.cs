namespace Starter.Domain.Records;

/// <summary>
/// A reference to one record of one record type, the identity every Records port takes. <see cref="Id"/> is the
/// internal record id (never sent on the wire: endpoints take and return PublicIds and decode them first).
/// </summary>
/// <param name="Id">The internal record id (1 or more).</param>
/// <param name="RecordType">The record type key (<see cref="RecordTypeKeyFormat"/>).</param>
/// <exception cref="ArgumentOutOfRangeException">The id is below 1.</exception>
/// <exception cref="ArgumentException">The record type key is malformed.</exception>
public sealed record RecordRef(long Id, string RecordType)
{
    /// <summary>The smallest record id.</summary>
    public const long MinId = 1;

    /// <summary>The internal record id.</summary>
    public long Id { get; } = Id >= MinId ? Id : throw new ArgumentOutOfRangeException(nameof(Id), Id, "A record id is 1 or more.");

    /// <summary>The record type key.</summary>
    public string RecordType { get; } = RecordTypeKeyFormat.EnsureValid(RecordType, nameof(RecordType));
}
