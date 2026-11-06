using Starter.Application.Records;
using Starter.Application.Users;
using Starter.Domain.Records;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Application.Access;

/// <summary>
/// The sample's gate to its own rows, over the kernel ports: the Records directory (owner, record type), the current
/// user, and the Records module's own visibility decision (<see cref="IRecordVisibility"/>), so the details are never
/// more visible than the record itself. Scoped: the caller's user id is resolved once per request. A record of another
/// record type counts as missing.
/// </summary>
public sealed class ServiceRequestAccess(
    ICurrentUserContext currentUser,
    IUserDirectory users,
    IRecordDirectory records,
    IRecordVisibility visibility)
{
    private long? _userId;
    private bool _userResolved;

    /// <summary>The record when it is a service request owned by the caller; null otherwise (missing, foreign, another type).</summary>
    public async Task<RecordSummary?> FindOwnedAsync(long recordId, CancellationToken cancellationToken)
    {
        var record = await FindAsync(recordId, cancellationToken);
        return record is not null && await IsOwnerAsync(record, cancellationToken) ? record : null;
    }

    /// <summary>
    /// The record when the caller may see it, as the Records module decides (<see cref="IRecordVisibility"/>): its
    /// owner holding <see cref="ServiceRequestsPermissionKeys.ViewOwn"/>; once submitted, a holder of the queue key
    /// <see cref="ServiceRequestsPermissionKeys.Queue"/> or of the records oversight key whose scope reaches the
    /// record's organization. A draft is private to its owner. Null otherwise (the caller cannot tell a hidden record
    /// from a missing one).
    /// </summary>
    public async Task<RecordSummary?> FindViewableAsync(long recordId, CancellationToken cancellationToken)
    {
        var record = await FindAsync(recordId, cancellationToken);
        return record is not null && await visibility.CanViewAsync(new RecordRef(record.Id, record.RecordType), cancellationToken) ? record : null;
    }

    private async Task<RecordSummary?> FindAsync(long recordId, CancellationToken cancellationToken)
    {
        if (recordId < 1)
        {
            return null;
        }

        var found = await records.GetAsync([recordId], cancellationToken);
        return found.FirstOrDefault(record => record.Id == recordId && string.Equals(record.RecordType, ServiceRequestRecord.RecordType, StringComparison.Ordinal));
    }

    private async Task<bool> IsOwnerAsync(RecordSummary record, CancellationToken cancellationToken) =>
        await CurrentUserIdAsync(cancellationToken) is { } userId && record.OwnerUserId == userId;

    private async Task<long?> CurrentUserIdAsync(CancellationToken cancellationToken)
    {
        if (_userResolved)
        {
            return _userId;
        }

        if (currentUser.SubjectId is { } subject)
        {
            var user = await users.FindBySubjectAsync(subject, cancellationToken);
            _userId = user is { IsActive: true } ? user.UserId : null;
        }

        _userResolved = true;
        return _userId;
    }
}
