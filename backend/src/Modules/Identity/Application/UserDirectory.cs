using Starter.Application.Users;

namespace Starter.Modules.Identity.Application;

/// <summary>
/// <see cref="IUserDirectory"/> over the Identity accounts: how other modules (Access) see a user without referencing
/// Identity. The break-glass administrator holds every permission; kind and organization select virtual roles and
/// organization scope.
/// </summary>
public sealed class UserDirectory(IUserAccountRepository accounts) : IUserDirectory
{
    /// <inheritdoc />
    public async Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindBySubjectAsync(subjectId, cancellationToken);
        return account is null
            ? null
            : new DirectoryUser(
                UserId: account.Id,
                IsActive: account.IsActive,
                HoldsAllPermissions: account.IsBreakGlass && account.IsActive,
                Kind: account.Kind,
                OrganizationId: account.OrganizationId);
    }
}
