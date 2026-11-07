# Codelab.Identity

## Purpose
The external sign-in providers a starter host can switch on (phase 3, ADR-002 package). One package owns:

- **Active Directory over LDAP/LDAPS** (`Codelab.Identity.ActiveDirectory`): `IActiveDirectoryAuthenticator` binds as
  the user (simple bind, no service account and so no directory secret in configuration), then searches the user's own
  entry under `BaseDn` and maps it to a `DirectoryIdentity`. LDAP result codes map to `DirectoryAuthStatus`
  (`Succeeded`, `InvalidCredentials`, `AccountDisabled`, `Unavailable`), bounded by a timeout.
  `LdapsCertificateReader` reports the certificate the directory presents; `IActiveDirectoryConnectionProbe` says at
  which layer (TCP, TLS) a connection stops. `ActiveDirectoryDeploymentGuard` refuses an unusable section at start.
- **Generic OpenID Connect** (`Codelab.Identity.Oidc`): `IOidcIdentityProvider` builds the authorization URL
  (code + PKCE S256 + state + nonce), completes the callback server-side (code redemption over the back channel, then
  id_token validation by `OidcIdTokenValidator` against `JwksSigningKeySource` or `PinnedSigningKeySource`) and builds
  the end_session URL with `id_token_hint`. `SsoDeploymentGuard` refuses an unusable section at start.
- **The OIDC back channel** (0.6, `IOidcBackChannel`): the token request and the JWKS fetch go through one port.
  `OidcBackChannel.Direct = 1` (default) calls the provider over the named client `OidcHttpClient.Name`;
  `OidcBackChannel.GatewayRelay = 2` is a backend zone without egress, where the host registers an adapter over the
  integration gateway's SSO relay (`/relay/sso/token`, `/relay/sso/jwks`). The browser redirects (authorize,
  end_session) never change. This package does not reference Codelab.Integration: the adapter lives in the host.
- **Login matching** (`LoginMatchField` `Email = 1`, `LoginName = 2`, `Upn = 3`; `LoginMatch.ValueOf`): one mapping
  from either identity to the value the host matches its local account on.

The package never creates, finds or signs in a local account: that is the Identity module's job (sessions, lockout,
binding checks, auth events). It answers "who did the directory or the provider say this is".

## Use
```csharp
builder.Services.AddCodelabActiveDirectory(builder.Configuration);   // Codelab:Identity:ActiveDirectory, validated on start
builder.Services.AddCodelabOidc(builder.Configuration);              // Codelab:Identity:Sso, validated on start

// AD sign-in (the caller maps the status to its own refusal; never echo the status to an anonymous caller)
var result = await authenticator.AuthenticateAsync(userName, password, cancellationToken);
if (result.Status == DirectoryAuthStatus.Succeeded)
{
    var matchValue = LoginMatch.ValueOf(result.Identity!, adOptions.Value.MatchBy);   // null => refuse
}

// SSO: begin (seal State, CodeVerifier, Nonce and ReturnUrl into an HttpOnly cookie), then complete
var begin = provider.BeginAuthorization(returnUrl);                  // 302 to begin.AuthorizationUrl
var signIn = await provider.CompleteAsync(new OidcCallback(code, state), new OidcPendingAuthorization(...), ct);
var endSession = provider.BuildEndSessionUrl(idTokenHint, postLogoutRedirectUri: null);   // top-level GET
```

Both registrations need the host's `IHostEnvironment` (the guards relax `UseLdaps` and https only in Development) and
use `TryAdd`, so a host or a test can register its own `ILdapConnectionFactory`, `IIdTokenSigningKeySource`,
`IOidcBackChannel` or `TimeProvider` first.

A backend without egress (decision D2) relays the back channel through the integration gateway:

```csharp
// Host adapter (the starter's Identity module: GatewaySsoBackChannel over Codelab.Integration's GatewayRelayClient)
builder.Services.AddSingleton<IOidcBackChannel, GatewaySsoBackChannel>();   // Mode => OidcBackChannel.GatewayRelay
builder.Services.AddCodelabOidc(builder.Configuration);                      // keeps it; Codelab:Identity:Sso:BackChannel = 2
```

An `IOidcBackChannel` never throws for a provider or relay fault: it answers `OidcBackChannelResponse.Answered(status,
body)`, `.Unavailable(reason)` (the sign-in is Unavailable) or `.Rejected(status, reason)` (the token request is
rejected). The reason is logged, so it must be secret-free (an exception type or a relay code); the response's
`ToString` never prints the body.

### Configuration keys
`Codelab:Identity:ActiveDirectory` (`ActiveDirectoryOptions`):

| Key | Default | Notes |
| --- | --- | --- |
| `Enabled` | `false` | Off: every sign-in is `Unavailable`, nothing is opened. |
| `Host` | | Domain controller or domain DNS name; LDAPS validates the certificate against it. |
| `Port` | `636` | `389` only with `UseLdaps=false` (Development only). |
| `UseLdaps` | `true` | Required outside Development. No certificate-validation switch exists. |
| `Domain` | | DNS domain a bare name is bound in: `user` binds as `user@Domain`. Also an accepted UPN suffix. |
| `NetBiosName` | | The only prefix accepted in `DOMAIN\user` (case-insensitive). Empty refuses the down-level form. |
| `UpnSuffixes` | `[]` | Extra suffixes accepted in a typed `user@suffix` (alternative UPN suffixes of this domain). |
| `BaseDn` | | Search base; it also limits who can sign in. |
| `SearchFilter` | `(&(objectCategory=person)(objectClass=user)(\|(sAMAccountName={0})(userPrincipalName={1})))` | `{0}` logon name, `{1}` bind name, both RFC 4515 escaped. |
| `TimeoutSeconds` | `10` | 1 to 120; bounds connect + bind + search. |
| `MatchBy` | `LoginName` | `Email`, `LoginName`, `Upn`. |

`Codelab:Identity:Sso` (`OidcOptions`):

| Key | Default | Notes |
| --- | --- | --- |
| `Enabled` | `false` | Off: nothing is read; the provider methods throw `InvalidOperationException`. |
| `Authority` | | The issuer; the id_token `iss` must equal it exactly. |
| `ClientId` / `ClientSecret` | | Secret optional (public client + PKCE); a secret is sent as `client_secret_post`. Never committed. |
| `RedirectUri` / `PostLogoutRedirectUri` | | As the browser sees them. |
| `AuthorizationEndpoint`, `TokenEndpoint`, `EndSessionEndpoint` | | Required, explicit (no runtime discovery). `TokenEndpoint` only with `BackChannel` 1: in relay mode the gateway holds it. |
| `BackChannel` | `1` (Direct) | `1` Direct (this host calls the provider) or `2` GatewayRelay (the token and JWKS calls go through the integration gateway; the host must register a relay `IOidcBackChannel`, or the start fails). |
| `NoEgress` | `false` | `true` declares that this backend cannot reach the provider: outside Development the start then fails unless `BackChannel` is `2`. |
| `SigningKeyMode` | `Jwks` | `Jwks` (needs `JwksUri` with `BackChannel` 1) or `Pinned` (needs `PinnedKeys`). |
| `JwksUri` | | Fetched through the named client, cached (1 h soft, 24 h hard, 2 min backoff, unknown-kid refetch at most every 30 s). |
| `PinnedKeys:n:Kid`, `PinnedKeys:n:PublicKey` | | SPKI PEM or base64 DER; RSA >= 2048 bits or EC; private keys refused. |
| `Scopes:n` | `openid profile email` | Must include `openid`. |
| `AllowedAlgorithms:n` | `RS256` | Asymmetric only (RS*, PS*, ES*); `none` and HS* fail the boot and are refused per token. |
| `ClockSkewSeconds` | `120` | 0 to 300. |
| `BackchannelTimeoutSeconds` | `15` | 1 to 120; the named client's timeout. |
| `MatchBy` | `Email` | `Email`, `LoginName`, `Upn`. |
| `RequireVerifiedEmail` | `true` | The email is released only with `email_verified` true. |
| `Claims:Email`, `Claims:EmailVerified`, `Claims:LoginName`, `Claims:Upn`, `Claims:DisplayName` | `email`, `email_verified`, `preferred_username`, `upn`, `name` | Provider-specific names (for example `loginName`) are configuration. |

## Extend
- **Another directory or IdP shape** (another attribute or claim): add the constant to `ActiveDirectoryAttributes` or
  `OidcClaimNames`, read it in one place (`LdapActiveDirectoryAuthenticator.IdentityOf`,
  `OidcIdentityProvider.IdentityOf`), and extend the identity record additively.
- **A new LDAP result-code meaning:** only `LdapFailureMapping` decides what a code means; add the constant to
  `LdapResultCodes` and a row to `LdapFailureMappingTests`.
- **Route the back channel elsewhere** (the integration gateway relay, another relay): implement `IOidcBackChannel`
  with `Mode` = `GatewayRelay`, register it before `AddCodelabOidc`, and set `BackChannel` to `2`. Map a refusal of the
  request to `Rejected` and every other fault (unreachable, the relay refusing this host, a disabled profile) to
  `Unavailable`. A test host can still replace the named client's primary handler for the direct channel. Keep
  certificate validation on.
- **Tests without a directory or an IdP:** register a fake `ILdapConnectionFactory` (it throws
  `LdapOperationException` with a result code, and AD's `data 533` style sub-code in `ServerMessage` for a disabled
  account); for OIDC sign id_tokens with a run-time RSA key and serve its JWKS from a stub handler on the named client.
- Every public type change updates `PublicAPI.Unshipped.txt` (RS0016/RS0017 fail the build otherwise).

## Rules
- Generated files in this folder (`CLAUDE.md`, `.editorconfig`, `BannedSymbols.generated.txt`, `CodeMetricsConfig.txt`)
  and `module.rules.yaml` are platform-owned. This package depends on no other Codelab package.
- **Certificate validation is always on.** LDAPS never sets `VerifyServerCertificate`, the OIDC client never sets a
  certificate callback, and the one TLS callback (`TlsEndpointInspector`, diagnostics only) returns the platform's
  verdict. `PackageSourceTests` fails the build on a bypass.
- **Algorithm before key.** The id_token header `alg` must be on the asymmetric allowlist before any key is looked up, so
  `none` and HMAC (including HS256 keyed with an RSA public key) never verify, whatever keys are configured. JWKS keys
  other than RSA/EC signing keys are dropped.
- **Lifetime on the injected clock.** `exp` is required; `exp`, `nbf` and `iat` are checked against `TimeProvider` with
  the configured skew (IdentityModel's own lifetime check reads the system clock and is off).
- **Only `invalidCredentials` (49) is a credential verdict.** A search fault, a connect or TLS fault, any other code and
  the timeout are `Unavailable` (no password was checked). A blank password is refused before anything is sent (an
  empty simple bind is an anonymous bind). Zero or several search entries are `InvalidCredentials`, never "the first".
- **Nothing secret is logged or printed:** log lines carry statuses, result codes, endpoints and counts, never a user
  name, password, code, verifier, token or client secret; the records holding them override `ToString`. Guard messages
  name keys, never values.
- Enums are stored as integers with explicit values; time comes from `TimeProvider`.

## Gotchas
- **No egress from the backend zone.** Every OIDC endpoint is explicit configuration, and there is no runtime discovery:
  an unreachable discovery document would take every sign-in down. Where the API cannot reach the IdP at all, set
  `BackChannel=2` (the token and JWKS calls go through the integration gateway's SSO relay) and `NoEgress=true`, so a
  deployment that forgets the relay fails at start instead of at the first sign-in. `SigningKeyMode=Pinned` still
  works with the relay (only the token call is relayed then).
- **The back channel is checked at start.** `OidcBackChannelValidator` compares `BackChannel` with the registered
  `IOidcBackChannel.Mode`: relay mode without a relay registered, or a relay registered for a direct deployment, fails
  the start. The relay answers only a provider success; a provider 4xx arrives as the gateway's
  `GATEWAY_UPSTREAM_REJECTED` without the OAuth `error` (logged as the relay code instead).
- **`Authority` is compared exactly** with the id_token `iss`: `https://idp.example.org` and `https://idp.example.org/`
  are different issuers. Copy the value from the provider's discovery document.
- **Bind name format is a directory decision.** A bare name binds as `user@Domain` (the UPN form); `NETBIOS\user` and
  `user@suffix` bind as typed. A directory whose UPN suffix differs from `Domain` lists it in `UpnSuffixes`, or sets
  `Domain` to the UPN suffix. Prove it against the real directory.
- **Only the configured domain is accepted.** A `DOMAIN\user` whose prefix is not `NetBiosName`, or a `user@suffix`
  whose suffix is neither `Domain` nor in `UpnSuffixes`, is `InvalidCredentials` before anything is sent (log event
  3106). The search looks for the bare logon name under the local `BaseDn`, so in a forest or trust `OTHER\bob` would
  be checked against the trusted domain's bob and then resolve to the local bob. Set `NetBiosName` when users type
  the down-level form; it is empty by default, which refuses that form.
- **The bound account must be the found entry.** A bare `bob` binds as `bob@Domain`, which the directory resolves to
  whichever account holds that UPN *explicitly* (a child-domain account, or one outside `BaseDn`), while the search finds
  the local `sAMAccountName=bob`. After the bind the provider asks RFC 4532 "Who am I?" (`ILdapConnection.WhoAmI`,
  AD answers `u:NETBIOS\sam`) and refuses (`InvalidCredentials`, log event 3107) unless the logon name is the entry's and
  the prefix is `NetBiosName`. Set `NetBiosName`: without it the domain cannot be checked and only the UPN comparison
  backs it. A custom `ILdapConnection` that does not implement `WhoAmI` (default: null) gets only the weaker fallback (a
  found entry whose `userPrincipalName` is present and differs from the bind name is refused); an entry with no UPN
  passes it, so implement `WhoAmI` in any production connection. A "Who am I?" fault is `Unavailable`.
- **AD answers 49 for a disabled, expired or locked account** with a `data 533/701/775` sub-code; those map to
  `AccountDisabled`. A real directory may omit the sub-code (then it is `InvalidCredentials`); the entry's
  `userAccountControl` ACCOUNTDISABLE flag is checked after a bind too.
- **The LDAP API is synchronous.** The attempt runs on the thread pool and is awaited for at most `TimeoutSeconds` on the
  injected clock; the connection carries the same timeout, so an abandoned attempt also ends.
- **`RequireVerifiedEmail` defaults to true.** A provider that never releases `email_verified` (common for directory-backed
  IdPs) yields `Email = null`; match by `LoginName` or `Upn`, or turn it off as a documented deployment decision.
- **Configuration lists replace defaults only when set.** `Scopes` and `AllowedAlgorithms` are empty by default and fall
  back to `EffectiveScopes`/`EffectiveAllowedAlgorithms`, so binding `Scopes:0` does not append to a default list.
- **What only a real directory or IdP proves** (not provable here): the LDAPS chain and name on the target host, the bind
  name format and the attribute names the directory releases; the IdP's JWKS shape and rotation, the claim names and
  `email_verified` behaviour, and whether end_session redirects back with `id_token_hint` on a GET.
