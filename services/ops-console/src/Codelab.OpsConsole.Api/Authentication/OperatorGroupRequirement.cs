using Codelab.OpsConsole.Api.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Codelab.OpsConsole.Api.Authentication;

/// <summary>The caller must belong to <c>Codelab:OpsConsole:OperatorGroup</c>.</summary>
public sealed class OperatorGroupRequirement : IAuthorizationRequirement;

/// <summary>
/// Decides <see cref="OperatorGroupRequirement"/> with <see cref="OperatorGroupMembership"/> against the current
/// options, on every request. Membership is what Windows put in the operator's logon token, so removing someone from
/// the group takes effect at their next Windows logon (Kerberos ticket); a change of the setting takes effect at once.
/// An authenticated non-member fails, which the framework answers with 403; an anonymous caller is challenged (401).
/// </summary>
public sealed class OperatorGroupHandler(IOptionsMonitor<OpsConsoleOptions> options) : AuthorizationHandler<OperatorGroupRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperatorGroupRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (OperatorGroupMembership.IsOperator(context.User, options.CurrentValue.OperatorGroup))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
