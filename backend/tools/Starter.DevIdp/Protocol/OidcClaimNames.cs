namespace Starter.DevIdp.Protocol;

/// <summary>The claim names the dev IdP puts in an id_token and the userinfo response.</summary>
public static class OidcClaimNames
{
    /// <summary>Subject.</summary>
    public const string Subject = "sub";

    /// <summary>Nonce echoed from the authorization request.</summary>
    public const string Nonce = "nonce";

    /// <summary>Authorized party (the client id).</summary>
    public const string AuthorizedParty = "azp";

    /// <summary>Email address.</summary>
    public const string Email = "email";

    /// <summary>Login name.</summary>
    public const string PreferredUserName = "preferred_username";

    /// <summary>User principal name.</summary>
    public const string Upn = "upn";

    /// <summary>Display name.</summary>
    public const string Name = "name";
}
