using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;

namespace Codelab.Identity.Diagnostics;

/// <summary>The event ids of the package's log lines (one range per provider).</summary>
internal static class IdentityLogEvents
{
    internal const int DirectoryDisabled = 3101;
    internal const int DirectoryUnavailable = 3102;
    internal const int DirectoryTimedOut = 3103;
    internal const int DirectoryRefused = 3104;
    internal const int DirectoryNotExactlyOne = 3105;
    internal const int DirectoryForeignDomain = 3106;
    internal const int DirectoryBoundIdentityMismatch = 3107;

    internal const int OidcStateMismatch = 3201;
    internal const int OidcTokenEndpointUnreachable = 3202;
    internal const int OidcTokenEndpointRefused = 3203;
    internal const int OidcTokenResponseInvalid = 3204;
    internal const int OidcIdTokenRefused = 3205;
    internal const int OidcSigningKeysUnavailable = 3206;
    internal const int OidcJwksFetchFailed = 3207;
    internal const int OidcJwksServedStale = 3208;
}

/// <summary>
/// Every log line the package writes. None carries a password, a token, an authorization code, a PKCE verifier, a
/// client secret or a user name: the values are statuses, result codes, endpoints and counts.
/// </summary>
internal static partial class IdentityLog
{
    [LoggerMessage(EventId = IdentityLogEvents.DirectoryDisabled, Level = LogLevel.Warning, Message = "Active Directory sign-in refused: {Section}:Enabled is false.")]
    internal static partial void DirectoryDisabled(ILogger logger, string section);

    [LoggerMessage(EventId = IdentityLogEvents.DirectoryUnavailable, Level = LogLevel.Error, Message = "Active Directory unavailable at {Host}:{Port} (LDAP result code {ResultCode}). No password was checked.")]
    internal static partial void DirectoryUnavailable(ILogger logger, string host, int port, string resultCode);

    [LoggerMessage(EventId = IdentityLogEvents.DirectoryTimedOut, Level = LogLevel.Error, Message = "Active Directory at {Host}:{Port} did not answer within {TimeoutSeconds}s. No password was checked.")]
    internal static partial void DirectoryTimedOut(ILogger logger, string host, int port, int timeoutSeconds);

    [LoggerMessage(EventId = IdentityLogEvents.DirectoryRefused, Level = LogLevel.Information, Message = "Active Directory refused the sign-in: {Status}.")]
    internal static partial void DirectoryRefused(ILogger logger, DirectoryAuthStatus status);

    [LoggerMessage(EventId = IdentityLogEvents.DirectoryNotExactlyOne, Level = LogLevel.Warning, Message = "Active Directory search matched {MatchCount} entries after a successful bind; exactly one is required.")]
    internal static partial void DirectoryNotExactlyOne(ILogger logger, int matchCount);

    [LoggerMessage(EventId = IdentityLogEvents.DirectoryForeignDomain, Level = LogLevel.Warning, Message = "Active Directory sign-in refused before the bind: the user name names a domain other than the configured Domain, NetBiosName or UpnSuffixes.")]
    internal static partial void DirectoryForeignDomain(ILogger logger);

    [LoggerMessage(EventId = IdentityLogEvents.DirectoryBoundIdentityMismatch, Level = LogLevel.Warning, Message = "Active Directory sign-in refused: the account the password was checked against is not the entry the search found (checked by {Check}).")]
    internal static partial void DirectoryBoundIdentityMismatch(ILogger logger, BoundIdentityCheck check);

    [LoggerMessage(EventId = IdentityLogEvents.OidcStateMismatch, Level = LogLevel.Warning, Message = "OIDC callback refused: the state does not match the pending sign-in.")]
    internal static partial void OidcStateMismatch(ILogger logger);

    [LoggerMessage(EventId = IdentityLogEvents.OidcTokenEndpointUnreachable, Level = LogLevel.Error, Message = "OIDC token endpoint unreachable ({FailureType}).")]
    internal static partial void OidcTokenEndpointUnreachable(ILogger logger, string failureType);

    [LoggerMessage(EventId = IdentityLogEvents.OidcTokenEndpointRefused, Level = LogLevel.Warning, Message = "OIDC token endpoint answered HTTP {Status} with OAuth error {OAuthError}.")]
    internal static partial void OidcTokenEndpointRefused(ILogger logger, int status, string oauthError);

    [LoggerMessage(EventId = IdentityLogEvents.OidcTokenResponseInvalid, Level = LogLevel.Warning, Message = "OIDC token endpoint answered without a usable id_token.")]
    internal static partial void OidcTokenResponseInvalid(ILogger logger);

    [LoggerMessage(EventId = IdentityLogEvents.OidcIdTokenRefused, Level = LogLevel.Warning, Message = "OIDC id_token refused: {Reason}.")]
    internal static partial void OidcIdTokenRefused(ILogger logger, string reason);

    [LoggerMessage(EventId = IdentityLogEvents.OidcSigningKeysUnavailable, Level = LogLevel.Error, Message = "OIDC id_token signing keys are unavailable ({Mode}).")]
    internal static partial void OidcSigningKeysUnavailable(ILogger logger, SigningKeyMode mode);

    [LoggerMessage(EventId = IdentityLogEvents.OidcJwksFetchFailed, Level = LogLevel.Warning, Message = "OIDC JWKS could not be fetched or used: {Reason}.")]
    internal static partial void OidcJwksFetchFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = IdentityLogEvents.OidcJwksServedStale, Level = LogLevel.Warning, Message = "OIDC JWKS refresh failed; serving the key set fetched at {FetchedAt:o}.")]
    internal static partial void OidcJwksServedStale(ILogger logger, DateTimeOffset fetchedAt);
}
