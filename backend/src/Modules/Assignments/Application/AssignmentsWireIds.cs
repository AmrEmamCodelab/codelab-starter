using Codelab.Security.PublicIds;

namespace Starter.Modules.Assignments.Application;

/// <summary>
/// The family of record ids in the queue routes and bodies (<c>PublicId&lt;QueueRecordIds&gt;</c>), one for every record
/// type. The label is the generic records label, so the id of a queue item opens the record detail
/// (<c>api/records/{recordType}/{id}</c>) unchanged.
/// </summary>
public sealed class QueueRecordIds : IIdDomain
{
    /// <summary>The domain label bound into every record PublicId of the generic record endpoints (never rename).</summary>
    public const string DomainLabel = "records";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>
/// The family of user ids in the queue endpoints (<c>PublicId&lt;AssigneeUserIds&gt;</c>), label <c>users</c>: the same
/// label as the Identity, Users and Access user ids, so a user id from the users list is a valid assignee id.
/// </summary>
public sealed class AssigneeUserIds : IIdDomain
{
    /// <summary>The domain label (shared with every user id family; never rename).</summary>
    public const string DomainLabel = "users";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}
