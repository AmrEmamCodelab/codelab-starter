using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Accounts;

namespace Starter.Modules.Users.Api;

/// <summary>The user accounts: list, open, create and edit. Thin: each action calls one use case and maps its outcome.</summary>
[ApiController]
[Route(UsersRoutes.Users)]
[Produces(UsersRoutes.JsonContentType)]
public sealed class AdminUsersController(ListUsers listUsers, GetUser getUser, CreateUser createUser, UpdateUser updateUser) : ControllerBase
{
    /// <summary>One page of accounts with their organization names.</summary>
    [HttpGet]
    [HasPermission(UsersPermissionKeys.View)]
    [ProducesResponseType<UserPageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserPageResponse>> ListAsync([FromQuery] UserListQuery query, CancellationToken cancellationToken) =>
        (await listUsers.ExecuteAsync(query, cancellationToken)).ToActionResult(UserPageResponse.From);

    /// <summary>One account.</summary>
    [HttpGet(UsersRoutes.IdTemplate)]
    [HasPermission(UsersPermissionKeys.View)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> GetAsync(PublicId<UsersIds> id, CancellationToken cancellationToken) =>
        (await getUser.ExecuteAsync(id, cancellationToken)).ToActionResult(UserResponse.From);

    /// <summary>Creates an account without a password; a password-bound one comes back with its activation link.</summary>
    [HttpPost]
    [HasPermission(UsersPermissionKeys.Manage)]
    [ProducesResponseType<CreatedUserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedUserResponse>> CreateAsync([FromBody] CreateUserCommand command, CancellationToken cancellationToken) =>
        (await createUser.ExecuteAsync(command, cancellationToken)).ToCreatedResult(CreatedUserResponse.From);

    /// <summary>Changes an account's editable fields.</summary>
    [HttpPut(UsersRoutes.IdTemplate)]
    [HasPermission(UsersPermissionKeys.Manage)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserResponse>> UpdateAsync(PublicId<UsersIds> id, [FromBody] UpdateUserCommand command, CancellationToken cancellationToken) =>
        (await updateUser.ExecuteAsync(id, command, cancellationToken)).ToActionResult(UserResponse.From);
}

/// <summary>Account actions: deactivate, reactivate, unlock and issue a new activation link.</summary>
[ApiController]
[Route(UsersRoutes.Users)]
[Produces(UsersRoutes.JsonContentType)]
public sealed class AdminUserActionsController(SetUserActive setUserActive, UnlockUser unlockUser, IssueUserActivation issueActivation) : ControllerBase
{
    /// <summary>Deactivates the account; its open sessions answer 401 from their next call.</summary>
    [HttpPost(UsersRoutes.Deactivate)]
    [HasPermission(UsersPermissionKeys.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeactivateAsync(PublicId<UsersIds> id, CancellationToken cancellationToken) =>
        (await setUserActive.ExecuteAsync(id, isActive: false, cancellationToken)).ToNoContentResult();

    /// <summary>Reactivates the account.</summary>
    [HttpPost(UsersRoutes.Reactivate)]
    [HasPermission(UsersPermissionKeys.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ReactivateAsync(PublicId<UsersIds> id, CancellationToken cancellationToken) =>
        (await setUserActive.ExecuteAsync(id, isActive: true, cancellationToken)).ToNoContentResult();

    /// <summary>Ends a lockout.</summary>
    [HttpPost(UsersRoutes.Unlock)]
    [HasPermission(UsersPermissionKeys.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UnlockAsync(PublicId<UsersIds> id, CancellationToken cancellationToken) =>
        (await unlockUser.ExecuteAsync(id, cancellationToken)).ToNoContentResult();

    /// <summary>Issues a new activation link (password binding only); the current password stops working.</summary>
    [HttpPost(UsersRoutes.Activation)]
    [HasPermission(UsersPermissionKeys.Manage)]
    [ProducesResponseType<ActivationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ActivationResponse>> IssueActivationAsync(PublicId<UsersIds> id, CancellationToken cancellationToken) =>
        (await issueActivation.ExecuteAsync(id, cancellationToken)).ToActionResult(ActivationResponse.From);
}
