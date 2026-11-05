namespace Starter.DevIdp.Protocol;

/// <summary>The protocol values the dev IdP accepts or sends.</summary>
public static class OidcValues
{
    /// <summary>The only supported <c>response_type</c>.</summary>
    public const string ResponseTypeCode = "code";

    /// <summary>The only supported <c>grant_type</c>.</summary>
    public const string GrantTypeAuthorizationCode = "authorization_code";

    /// <summary>The only supported <c>code_challenge_method</c>.</summary>
    public const string CodeChallengeMethodS256 = "S256";

    /// <summary>The scope every request must include.</summary>
    public const string ScopeOpenId = "openid";

    /// <summary>The <c>profile</c> scope.</summary>
    public const string ScopeProfile = "profile";

    /// <summary>The <c>email</c> scope.</summary>
    public const string ScopeEmail = "email";

    /// <summary>The access token type.</summary>
    public const string TokenTypeBearer = "Bearer";

    /// <summary>The Basic authentication scheme (client_secret_basic).</summary>
    public const string AuthenticationSchemeBasic = "Basic";

    /// <summary>The subject type.</summary>
    public const string SubjectTypePublic = "public";

    /// <summary>client_secret_post.</summary>
    public const string ClientAuthSecretPost = "client_secret_post";

    /// <summary>client_secret_basic.</summary>
    public const string ClientAuthSecretBasic = "client_secret_basic";

    /// <summary>A public client (PKCE only).</summary>
    public const string ClientAuthNone = "none";

    /// <summary>The separator of the values in <c>scope</c>.</summary>
    public const char ScopeSeparator = ' ';
}
