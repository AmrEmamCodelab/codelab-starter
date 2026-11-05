using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Starter.Application.Users;
using Starter.Infrastructure.Authorization;
using Starter.Modules.Access.Application;

namespace Starter.Modules.Access.Infrastructure.Authorization;

/// <summary>
/// Evaluates <see cref="HasPermissionAttribute"/> against the caller's live permissions (the token carries identity
/// only). An unauthenticated caller is left alone, so the framework answers 401; an authenticated caller without the
/// key, without a readable subject, or with an inactive account is not succeeded, so the framework answers 403. The
/// handler never calls <see cref="AuthorizationHandlerContext.Fail()"/>: stacked attributes each add a requirement and AND.
/// </summary>
public sealed class PermissionHandler(ICurrentUserContext currentUser, IUserPermissionResolver resolver)
    : AuthorizationHandler<HasPermissionAttribute>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, HasPermissionAttribute requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        if (context.User.Identity?.IsAuthenticated != true || currentUser.SubjectId is not { } subjectId)
        {
            return;
        }

        var cancellationToken = context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;
        var permissions = await resolver.ResolveAsync(subjectId, cancellationToken);
        if (permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
