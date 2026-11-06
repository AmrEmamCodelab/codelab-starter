namespace Starter.Modules.Assignments.Application;

/// <summary>
/// The permission keys the Assignments module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints with these constants, never with literals. Add one with
/// <c>node tools/scaffold/new-permission.mjs --module Assignments --action &lt;kebab&gt;</c>. Seeing a record type's
/// queue ALSO needs that record type's queue permission (<c>RecordTypeRegistration.QueuePermissionKey</c>), checked
/// per request with its organization scope.
/// </summary>
public static class AssignmentsPermissionKeys
{
    /// <summary>Work a queue: list it, claim a record for yourself and release your own.</summary>
    public const string Work = "assignments.work";

    /// <summary>Hand a queue record to another user who holds the record type's queue permission.</summary>
    public const string Assign = "assignments.assign";

    // scaffold:permission-keys
}
