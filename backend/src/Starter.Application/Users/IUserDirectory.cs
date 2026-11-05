namespace Starter.Application.Users;

/// <summary>A user as other modules see it: the internal id, whether the account may act, and break-glass status.</summary>
/// <param name="UserId">The internal user id (never on the wire; the wire carries a PublicId).</param>
/// <param name="IsActive">False for a deactivated account: it holds no permission.</param>
/// <param name="HoldsAllPermissions">True for the break-glass administrator: it holds every catalog permission.</param>
public sealed record DirectoryUser(long UserId, bool IsActive, bool HoldsAllPermissions);

/// <summary>
/// Looks users up for other modules (implemented by the Identity module), so a module never references Identity's
/// tables or project.
/// </summary>
public interface IUserDirectory
{
    /// <summary>The user with this token subject, or null when none exists.</summary>
    Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken);
}
