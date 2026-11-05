namespace Starter.DevIdp.Protocol;

/// <summary>The OAuth 2.0 / OpenID Connect error codes the dev IdP returns.</summary>
public static class OidcErrorCodes
{
    /// <summary>A required parameter is missing, repeated or malformed.</summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>The client is unknown, or its secret is wrong.</summary>
    public const string InvalidClient = "invalid_client";

    /// <summary>The code is unknown, expired, already used, for another client or redirect URI, or fails PKCE.</summary>
    public const string InvalidGrant = "invalid_grant";

    /// <summary>The grant type is not <c>authorization_code</c>.</summary>
    public const string UnsupportedGrantType = "unsupported_grant_type";

    /// <summary>The response type is not <c>code</c>.</summary>
    public const string UnsupportedResponseType = "unsupported_response_type";

    /// <summary>The scope lacks <c>openid</c>.</summary>
    public const string InvalidScope = "invalid_scope";

    /// <summary>The <c>login_hint</c> names no fixture user.</summary>
    public const string AccessDenied = "access_denied";

    /// <summary>The access token is unknown or expired.</summary>
    public const string InvalidToken = "invalid_token";
}
