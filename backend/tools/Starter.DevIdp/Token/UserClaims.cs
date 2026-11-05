using Starter.DevIdp.Configuration;
using Starter.DevIdp.Protocol;

namespace Starter.DevIdp.Token;

/// <summary>The identity claims of a fixture user, shared by the id_token and the userinfo response.</summary>
internal static class UserClaims
{
    /// <summary>sub, then email, preferred_username, upn and name for the ones that are set.</summary>
    public static IReadOnlyDictionary<string, string> Of(DevIdpUser user)
    {
        var claims = new Dictionary<string, string>(StringComparer.Ordinal) { [OidcClaimNames.Subject] = user.Subject };
        AddIfPresent(claims, OidcClaimNames.Email, user.Email);
        AddIfPresent(claims, OidcClaimNames.PreferredUserName, user.LoginName);
        AddIfPresent(claims, OidcClaimNames.Upn, user.Upn);
        AddIfPresent(claims, OidcClaimNames.Name, user.DisplayName);
        return claims;
    }

    private static void AddIfPresent(Dictionary<string, string> claims, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            claims[name] = value;
        }
    }
}
