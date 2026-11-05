namespace Starter.DevIdp.Protocol;

/// <summary>The OAuth 2.0 / OpenID Connect request and response parameter names the dev IdP reads or writes.</summary>
public static class OidcParameterNames
{
    /// <summary><c>client_id</c>.</summary>
    public const string ClientId = "client_id";

    /// <summary><c>client_secret</c> (client_secret_post).</summary>
    public const string ClientSecret = "client_secret";

    /// <summary><c>redirect_uri</c>.</summary>
    public const string RedirectUri = "redirect_uri";

    /// <summary><c>response_type</c>.</summary>
    public const string ResponseType = "response_type";

    /// <summary><c>scope</c>.</summary>
    public const string Scope = "scope";

    /// <summary><c>state</c>.</summary>
    public const string State = "state";

    /// <summary><c>nonce</c>.</summary>
    public const string Nonce = "nonce";

    /// <summary><c>code_challenge</c> (PKCE).</summary>
    public const string CodeChallenge = "code_challenge";

    /// <summary><c>code_challenge_method</c> (PKCE).</summary>
    public const string CodeChallengeMethod = "code_challenge_method";

    /// <summary><c>code_verifier</c> (PKCE).</summary>
    public const string CodeVerifier = "code_verifier";

    /// <summary><c>login_hint</c>: a subject, login name, email or UPN of a fixture user; signs in without the picker.</summary>
    public const string LoginHint = "login_hint";

    /// <summary><c>code</c>.</summary>
    public const string Code = "code";

    /// <summary><c>grant_type</c>.</summary>
    public const string GrantType = "grant_type";

    /// <summary><c>id_token_hint</c> (end-session).</summary>
    public const string IdTokenHint = "id_token_hint";

    /// <summary><c>post_logout_redirect_uri</c> (end-session).</summary>
    public const string PostLogoutRedirectUri = "post_logout_redirect_uri";

    /// <summary><c>error</c>.</summary>
    public const string Error = "error";

    /// <summary><c>error_description</c>.</summary>
    public const string ErrorDescription = "error_description";
}
