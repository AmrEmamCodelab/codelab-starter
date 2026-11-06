using Starter.Application.Results;
using Starter.Application.Users;

namespace Starter.Modules.Assignments.Application;

/// <summary>
/// The caller as a queue worker: the token subject resolved to the internal user through the kernel
/// <see cref="IUserDirectory"/> port (Identity), so this module never references Identity.
/// </summary>
public sealed class CurrentStaff(ICurrentUserContext currentUser, IUserDirectory users)
{
    /// <summary>The caller's user id, or 403 <see cref="AssignmentsErrorCodes.ActorUnknown"/> when the token names no active account.</summary>
    public async Task<Outcome<long>> UserIdAsync(CancellationToken cancellationToken)
    {
        if (currentUser.SubjectId is not { } subject)
        {
            return Refusal.Forbidden(AssignmentsErrorCodes.ActorUnknown);
        }

        var user = await users.FindBySubjectAsync(subject, cancellationToken);
        return user is { IsActive: true } ? user.UserId : Refusal.Forbidden(AssignmentsErrorCodes.ActorUnknown);
    }
}
