using Codelab.Security.PublicIds;
using Starter.Modules.Users.Application;

namespace Starter.Modules.Users.Infrastructure;

/// <summary>The <see cref="IUserPublicIds"/> of the host's PublicId codec (the same token the API sends).</summary>
public sealed class UserPublicIds : IUserPublicIds
{
    /// <inheritdoc />
    public string TokenOf(long userId) => PublicId<UsersIds>.From(userId).ToToken();
}
