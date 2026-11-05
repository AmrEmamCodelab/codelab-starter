using Microsoft.Extensions.Logging;

namespace Starter.Modules.Access.Application;

/// <summary>The kind of an access change in the audit trail. Logged as the integer value.</summary>
public enum AccessChangeKind
{
    /// <summary>A role was created.</summary>
    RoleCreated = 1,

    /// <summary>A role's names, permissions or active state changed.</summary>
    RoleUpdated = 2,

    /// <summary>A user's roles and direct grants were replaced.</summary>
    UserGrantsReplaced = 3,
}

/// <summary>One audited access change: internal ids and counts only, never a name or other personal data.</summary>
/// <param name="Kind">What changed.</param>
/// <param name="TargetId">The role id (role changes) or the user id (grant changes).</param>
/// <param name="RoleCount">The roles the user holds after the change (0 for role changes).</param>
/// <param name="PermissionCount">The permissions the role grants, or the user holds directly, after the change.</param>
/// <param name="Verdict">What the self-lockout guard decided.</param>
public sealed record AccessAuditEvent(AccessChangeKind Kind, long TargetId, int RoleCount, int PermissionCount, GuardVerdict Verdict);

/// <summary>
/// Commits an access write and records it. Until the audit module (phase 5) the trail is a structured log line
/// (event ids 3110-3119) that carries internal ids only; the request's log scope adds the caller's user hash. Replace
/// <see cref="Record"/> with the audit port when it lands; the use cases do not change.
/// </summary>
public sealed partial class AccessChanges(IAccessUnitOfWork unitOfWork, TimeProvider timeProvider, ILogger<AccessChanges> logger)
{
    /// <summary>The time of the change (from the injected clock).</summary>
    public DateTimeOffset Now => timeProvider.GetUtcNow();

    /// <summary>Saves the staged changes in one transaction, then records <paramref name="describe"/>'s event.</summary>
    /// <param name="describe">Builds the event after the save, so a new role's id is known.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public async Task CommitAsync(Func<AccessAuditEvent> describe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(describe);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        Record(describe());
    }

    private void Record(AccessAuditEvent change)
    {
        LogChange(logger, change.Kind, change.TargetId, roleCount: change.RoleCount, permissionCount: change.PermissionCount);
        if (change.Verdict is GuardVerdict.AllowedBreakGlassOnly)
        {
            LogBreakGlassOnly(logger, change.Kind, change.TargetId);
        }
    }

    [LoggerMessage(EventId = 3110, Level = LogLevel.Information, Message = "Access change {Kind} on {TargetId}: {RoleCount} roles, {PermissionCount} permissions")]
    private static partial void LogChange(ILogger logger, AccessChangeKind kind, long targetId, int roleCount, int permissionCount);

    [LoggerMessage(EventId = 3111, Level = LogLevel.Warning, Message = "Access change {Kind} on {TargetId} left only the break-glass administrator able to manage roles and grants (confirmed)")]
    private static partial void LogBreakGlassOnly(ILogger logger, AccessChangeKind kind, long targetId);
}
