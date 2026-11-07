using Codelab.Identity.Oidc;
using Starter.Application.SingleSignOn;

namespace Starter.Modules.Identity.Infrastructure.LoginModes;

/// <summary>
/// The kernel <see cref="IIdentityProviderKeySet"/> over the registered SSO back channel
/// (<see cref="IOidcBackChannel"/>): the direct one, or <see cref="GatewaySsoBackChannel"/> when
/// <c>Codelab:Identity:Sso:BackChannel</c> is GatewayRelay. A success (2xx) answer is the JWKS text; anything else is
/// null. Registered only while <c>sso</c> is a login mode.
/// </summary>
public sealed class SsoIdentityProviderKeySet(IOidcBackChannel backChannel) : IIdentityProviderKeySet
{
    private const int FirstSuccessStatus = 200;
    private const int FirstRedirectStatus = 300;

    /// <inheritdoc />
    public async Task<string?> ReadJwksAsync(CancellationToken cancellationToken)
    {
        var response = await backChannel.GetJwksAsync(cancellationToken);
        return IsSuccess(response) ? response.Body : null;
    }

    private static bool IsSuccess(OidcBackChannelResponse response) =>
        response.Outcome == OidcBackChannelOutcome.Answered
        && response.StatusCode is >= FirstSuccessStatus and < FirstRedirectStatus
        && response.Body is not null;
}
