using Starter.Application.Users;

namespace Starter.Modules.Settings.Application.Admin;

/// <summary>When a change is made and by whom.</summary>
/// <param name="At">The time of the change (from the injected <see cref="TimeProvider"/>).</param>
/// <param name="ByUserId">The acting user's internal id, or null when the caller has no known account.</param>
public sealed record SettingChangeMark(DateTimeOffset At, long? ByUserId);

/// <summary>
/// Stamps a settings change with the time and the acting user: the token subject resolved to the internal user id
/// through the kernel <see cref="IUserDirectory"/> port (Identity), so this module never references Identity.
/// </summary>
public sealed class SettingChangeMarks(ICurrentUserContext currentUser, IUserDirectory users, TimeProvider timeProvider)
{
    /// <summary>The mark of a change made now by the current caller.</summary>
    public async Task<SettingChangeMark> CurrentAsync(CancellationToken cancellationToken)
    {
        var at = timeProvider.GetUtcNow();
        if (currentUser.SubjectId is not { } subject)
        {
            return new SettingChangeMark(at, ByUserId: null);
        }

        var user = await users.FindBySubjectAsync(subject, cancellationToken);
        return new SettingChangeMark(at, user?.UserId);
    }
}
