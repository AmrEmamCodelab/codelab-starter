using Starter.DevIdp.Authorize;
using Starter.DevIdp.Configuration;

namespace Starter.DevIdp.Grants;

/// <summary>What an authorization code stands for until the token endpoint redeems it.</summary>
/// <param name="Request">The validated authorization request (client, redirect URI, PKCE challenge, nonce, scope).</param>
/// <param name="User">The signed-in fixture user.</param>
internal sealed record AuthorizationGrant(AuthorizationRequest Request, DevIdpUser User);

/// <summary>What an access token stands for at the userinfo endpoint.</summary>
/// <param name="ClientId">The client the token was issued to.</param>
/// <param name="User">The signed-in fixture user.</param>
internal sealed record AccessGrant(string ClientId, DevIdpUser User);
