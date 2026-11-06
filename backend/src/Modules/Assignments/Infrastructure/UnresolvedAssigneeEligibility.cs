using Microsoft.Extensions.Logging;
using Starter.Application.Authorization;
using Starter.Modules.Assignments.Application;

namespace Starter.Modules.Assignments.Infrastructure;

/// <summary>
/// The default <see cref="IAssigneeEligibility"/> while the kernel has no "permission scope of another user" port: it
/// refuses every user (fail closed), so an assignment to another user answers 400
/// <see cref="AssignmentsErrorCodes.AssigneeNotEligible"/>, and logs a warning (event 9005) each time. Claims and
/// releases do not use it. A host replaces it by registering its own <see cref="IAssigneeEligibility"/> before the
/// module (the module adds this one with TryAdd). See MODULE.md.
/// </summary>
public sealed partial class UnresolvedAssigneeEligibility(ILogger<UnresolvedAssigneeEligibility> logger) : IAssigneeEligibility
{
    /// <inheritdoc />
    public Task<ScopeDecision> ScopeForUserAsync(long userId, string permissionKey, CancellationToken cancellationToken)
    {
        LogUnresolved(logger, userId: userId, permissionKey: permissionKey);
        return Task.FromResult(ScopeDecision.Denied);
    }

    [LoggerMessage(EventId = 9005, Level = LogLevel.Warning, Message = "Assignee check for permission {PermissionKey} refused user {UserId}: no IAssigneeEligibility adapter is registered, so every assignment to another user is refused")]
    private static partial void LogUnresolved(ILogger logger, long userId, string permissionKey);
}
