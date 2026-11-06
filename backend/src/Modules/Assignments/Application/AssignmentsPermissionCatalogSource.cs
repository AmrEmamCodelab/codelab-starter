using Starter.Application.Authorization;

namespace Starter.Modules.Assignments.Application;

/// <summary>The Assignments module's share of the permission catalog.</summary>
public sealed class AssignmentsPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: AssignmentsPermissionKeys.Work, Description: "Work record queues: list a queue, claim a record and release your own (with the record type's queue permission)."),
        new(Key: AssignmentsPermissionKeys.Assign, Description: "Assign queue records to other users who hold the record type's queue permission."),
        // scaffold:permission-catalog
    ];
}
