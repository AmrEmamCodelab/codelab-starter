using Starter.Domain.Users;
using static Starter.Domain.Users.UserKind;

namespace Starter.Application.Users;

/// <summary>
/// A user as other modules see it: the internal id, whether the account may act, break-glass status, kind,
/// organization and the name to show for it.
/// </summary>
/// <param name="UserId">The internal user id (never on the wire; the wire carries a PublicId).</param>
/// <param name="IsActive">False for a deactivated account: it holds no permission.</param>
/// <param name="HoldsAllPermissions">True for the break-glass administrator: it holds every catalog permission.</param>
/// <param name="Kind">Internal or external; it selects the matching virtual roles. Internal when not given.</param>
/// <param name="OrganizationId">The user's organization (internal id), or null when the user has none.</param>
/// <param name="DisplayName">The name shown in the shell and the lists (for example "triggered by"), or null when unknown.</param>
public sealed record DirectoryUser(
    long UserId,
    bool IsActive,
    bool HoldsAllPermissions,
    UserKind Kind = Internal,
    long? OrganizationId = null,
    string? DisplayName = null);

/// <summary>
/// Looks users up for other modules (implemented by the Identity module), so a module never references Identity's
/// tables or project.
/// </summary>
public interface IUserDirectory
{
    /// <summary>The user with this token subject, or null when none exists.</summary>
    Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken);
}
