using System.Security.Claims;
using Codelab.OpsConsole.Application.Ports;

namespace Codelab.OpsConsole.Api.Authentication;

/// <summary>
/// The one owner of "is this person an operator": an authenticated principal with a name that belongs to the
/// configured group. A group SID is matched against the <see cref="ClaimTypes.GroupSid"/> claims Windows sign-in
/// puts on the identity (every group, nested ones included); a <c>DOMAIN\Group</c> name goes through
/// <see cref="ClaimsPrincipal.IsInRole"/>, which a Windows principal resolves against the domain.
/// </summary>
public static class OperatorGroupMembership
{
    /// <summary>Every Windows security identifier starts with this.</summary>
    public const string SidPrefix = "S-1-";

    /// <summary>True when <paramref name="group"/> is written as a SID rather than a name.</summary>
    public static bool IsSid(string group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Trim().StartsWith(SidPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="user"/> is a named, authenticated member of <paramref name="operatorGroup"/>.</summary>
    public static bool IsOperator(ClaimsPrincipal user, string? operatorGroup)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(operatorGroup) || user.Identity?.IsAuthenticated != true || NameOf(user) is null)
        {
            return false;
        }

        var group = operatorGroup.Trim();
        return IsSid(group)
            ? user.HasClaim(claim => claim.Type == ClaimTypes.GroupSid && string.Equals(claim.Value, group, StringComparison.OrdinalIgnoreCase))
            : user.IsInRole(group);
    }

    /// <summary>The operator's account name, or null when the identity has none.</summary>
    public static OperatorName? NameOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var name = user.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : new OperatorName(name);
    }
}
