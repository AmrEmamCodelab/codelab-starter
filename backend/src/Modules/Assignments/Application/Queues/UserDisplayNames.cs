using Starter.Application.Users;

namespace Starter.Modules.Assignments.Application.Queues;

/// <summary>
/// The display names of users by internal id, read through the kernel <see cref="IAccountAdministration"/> port
/// (Identity). One lookup per distinct id; at most a page of owners and assignees per request.
/// </summary>
public sealed class UserDisplayNames(IAccountAdministration accounts)
{
    /// <summary>The name shown for a user id no account matches (a removed or never-synchronized account).</summary>
    public const string Unknown = "";

    /// <summary>The display name of each distinct id in <paramref name="userIds"/> (<see cref="Unknown"/> when no account matches).</summary>
    public async Task<IReadOnlyDictionary<long, string>> ForAsync(IEnumerable<long> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var names = new Dictionary<long, string>();
        foreach (var userId in userIds.Distinct())
        {
            var account = await accounts.FindAsync(userId, cancellationToken);
            names[userId] = account?.DisplayName ?? Unknown;
        }

        return names;
    }
}
