using Codelab.Security.ServiceAuth;
using Microsoft.AspNetCore.Authorization;

namespace Starter.Modules.Ops.Api;

/// <summary>
/// The only way into <c>/api/ops</c>: a valid Codelab.Security ES256 service assertion
/// (<see cref="ServiceAssertionAuthenticationHandler.SchemeName"/>, jti single use, method, path and body bound) from
/// the trusted Ops Console API (<c>Codelab:Ops:TrustedCallerKeyId</c>). Only that scheme is authenticated for these
/// endpoints, so a user's bearer token, a cookie or an API key authenticates nothing here and the call is challenged
/// with 401; a valid assertion of another trusted service is 403. These endpoints carry no <c>[HasPermission]</c>
/// on purpose: no user permission can open them.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class OpsServiceCallerAttribute : AuthorizeAttribute
{
    /// <summary>The authorization policy (registered by the module).</summary>
    public const string PolicyName = "ops-service-caller";

    /// <summary>Requires <see cref="PolicyName"/> over the service-assertion scheme only.</summary>
    public OpsServiceCallerAttribute()
        : base(PolicyName)
    {
        AuthenticationSchemes = ServiceAssertionAuthenticationHandler.SchemeName;
    }
}
