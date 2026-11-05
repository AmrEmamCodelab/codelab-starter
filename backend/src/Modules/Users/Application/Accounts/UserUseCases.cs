using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Domain.Users;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>An account just created, and its activation when it is password-bound.</summary>
/// <param name="User">The account.</param>
/// <param name="Activation">The activation link, or null for an Active Directory or SSO account.</param>
public sealed record CreatedUser(UserView User, IssuedActivation? Activation);

/// <summary>
/// <c>GET /api/admin/users</c>: one page of accounts with their organization names. A <see cref="UsersPermissionKeys.View"/>
/// holder at own-organization scope sees only their organization's accounts (none when they have no organization).
/// </summary>
public sealed class ListUsers(IAccountAdministration accounts, AccountAccess access, UserViews views)
{
    private static readonly UserViewPage Empty = new([], Total: 0);

    /// <summary>The page (never refused: bad paging values are normalized by <see cref="PageRequest"/>).</summary>
    public async Task<Outcome<UserViewPage>> ExecuteAsync(UserListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = PageRequest.From(query.Page, query.PageSize);
        var requested = query.OrganizationId is { HasValue: true } id ? id.Value : (long?)null;
        var filter = await access.ListFilterAsync(UsersPermissionKeys.View, requested, cancellationToken);
        if (filter.MatchesNothing)
        {
            return Empty;
        }

        var found = await accounts.ListAsync(
            new AccountQuery(PageRequest.Search(query.Search), filter.OrganizationId, query.Kind, query.IsActive, page.Page, page.PageSize),
            cancellationToken);
        return new UserViewPage(await views.OfAsync(found.Items, cancellationToken), found.Total);
    }
}

/// <summary><c>GET /api/admin/users/{id}</c>.</summary>
public sealed class GetUser(AccountAccess access, UserViews views)
{
    /// <summary>The account, or <see cref="UsersErrorCodes.UserNotFound"/> (404, also outside the caller's scope).</summary>
    public async Task<Outcome<UserView>> ExecuteAsync(PublicId<UsersIds> id, CancellationToken cancellationToken) =>
        await access.FindAsync(id.Value, UsersPermissionKeys.View, cancellationToken) is { } account
            ? await views.OfAsync(account, cancellationToken)
            : Refusal.NotFound(UsersErrorCodes.UserNotFound);
}

/// <summary>
/// <c>POST /api/admin/users</c>: a new account without a password. The organization must exist, be active and match
/// the account's kind, and an own-organization <see cref="UsersPermissionKeys.Manage"/> holder can only create accounts
/// in their own organization. A password-bound account also gets its activation link (and a Pending outbox message) in
/// the same call; an Active Directory or SSO account gets none.
/// </summary>
public sealed class CreateUser(
    IValidator<CreateUserCommand> validator,
    IAccountAdministration accounts,
    AccountAccess access,
    AccountActivations activations,
    UserViews views)
{
    /// <summary>
    /// The account and its activation, or a refusal: <see cref="PlatformErrorCodes.Validation"/>,
    /// <see cref="UsersErrorCodes.OrganizationOutOfScope"/> (403), the membership codes,
    /// <see cref="UsersErrorCodes.ActivationLinkUnconfigured"/> (checked before anything is created), or Identity's
    /// duplicate codes. When the account was created but Identity then refused the activation, that refusal is returned;
    /// the account exists and the activation can be issued again.
    /// </summary>
    public async Task<Outcome<CreatedUser>> ExecuteAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var changes = command.Fields.ToChanges();
        if (await RefusalBeforeCreateAsync(changes, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var created = await accounts.CreateAsync(NewAccountOf(command.UserName!, changes), cancellationToken);
        if (!created.IsSuccess)
        {
            return created.Refusal;
        }

        return await WithActivationAsync(created.Value, cancellationToken);
    }

    private async Task<Refusal?> RefusalBeforeCreateAsync(AccountChanges changes, CancellationToken cancellationToken)
    {
        if (await access.PlacementRefusalAsync(changes, current: null, cancellationToken) is { } placement)
        {
            return placement;
        }

        return changes.Binding == AccountBinding.Password && !activations.IsConfigured
            ? Refusal.Unprocessable(UsersErrorCodes.ActivationLinkUnconfigured)
            : null;
    }

    private async Task<Outcome<CreatedUser>> WithActivationAsync(AccountSummary account, CancellationToken cancellationToken)
    {
        var view = await views.OfAsync(account, cancellationToken);
        if (account.Binding != AccountBinding.Password)
        {
            return new CreatedUser(view, Activation: null);
        }

        var activation = await activations.IssueAsync(account.UserId, cancellationToken);
        return activation.IsSuccess ? new CreatedUser(view, activation.Value) : activation.Refusal;
    }

    private static NewAccount NewAccountOf(string userName, AccountChanges changes) => new(
        UserName: userName.Trim(),
        DisplayName: changes.DisplayName,
        Email: changes.Email,
        LoginName: changes.LoginName,
        Upn: changes.Upn,
        Kind: changes.Kind,
        Binding: changes.Binding,
        OrganizationId: changes.OrganizationId);
}

/// <summary>
/// <c>PUT /api/admin/users/{id}</c>: replaces the editable fields. A user who keeps their current organization may stay
/// in it after it was deactivated; moving to another organization needs an active one of the matching kind (inside the
/// caller's scope). The caller must reach the account (<see cref="AccountAccess.FindForChangeAsync"/>: scope,
/// break-glass, privilege) and cannot change their own kind, binding or organization. Identity ends the account's
/// sessions when the binding or a sign-in identifier changes.
/// </summary>
public sealed class UpdateUser(
    IValidator<UpdateUserCommand> validator,
    IAccountAdministration accounts,
    AccountAccess access,
    UserViews views)
{
    /// <summary>
    /// The account, or a refusal: <see cref="PlatformErrorCodes.Validation"/>, the refusals of
    /// <see cref="AccountAccess.FindForChangeAsync"/>, <see cref="UsersErrorCodes.CannotChangeOwnAccess"/> (409),
    /// <see cref="UsersErrorCodes.OrganizationOutOfScope"/> (403), the membership codes, or Identity's duplicate codes.
    /// </summary>
    public async Task<Outcome<UserView>> ExecuteAsync(PublicId<UsersIds> id, UpdateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var found = await access.FindForChangeAsync(id.Value, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Refusal;
        }

        var account = found.Value;
        var changes = command.Fields.ToChanges();
        if (await RefusalForAsync(account, changes, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var updated = await accounts.UpdateAsync(account.UserId, changes, cancellationToken);
        return updated.IsSuccess ? await views.OfAsync(updated.Value, cancellationToken) : updated.Refusal;
    }

    private async Task<Refusal?> RefusalForAsync(AccountSummary account, AccountChanges changes, CancellationToken cancellationToken)
    {
        if (access.IsCaller(account) && ChangesAccess(account, changes))
        {
            return Refusal.Conflict(UsersErrorCodes.CannotChangeOwnAccess);
        }

        return await access.PlacementRefusalAsync(changes, current: account, cancellationToken);
    }

    private static bool ChangesAccess(AccountSummary account, AccountChanges changes) =>
        changes.Kind != account.Kind || changes.Binding != account.Binding || changes.OrganizationId != account.OrganizationId;
}
