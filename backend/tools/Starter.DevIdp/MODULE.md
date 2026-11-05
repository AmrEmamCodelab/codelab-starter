# Starter.DevIdp

## Purpose
A small, standard OpenID Connect issuer so SSO login can be run and tested without a real identity provider: on a
developer machine (`http://localhost:5190`) and in process inside tests (TestServer, no socket). It implements the
authorization code flow with PKCE S256, an RS256 id_token, a JWKS, userinfo and RP-initiated logout, and nothing
more. It is a **test double: it refuses to start outside the `Development` environment and is never deployed**.

| Endpoint | Path | Behaviour |
| --- | --- | --- |
| Discovery | `GET /.well-known/openid-configuration` | Issuer and endpoint URLs built on `DevIdp:Issuer`; `code`, `authorization_code`, `S256`, `RS256` only. |
| Authorize | `GET /authorize`, `POST /authorize` | Validates `client_id` and the exact `redirect_uri` (an error there is shown on an IdP page, never redirected), then `response_type=code`, `openid` in `scope`, an S256 `code_challenge` (errors redirect back with `state`). `login_hint` signs in at once; without it a fixture-user picker is shown (one button per user, POSTed back). `state` is echoed on the redirect, `nonce` in the id_token. |
| Token | `POST /token` | `authorization_code` only. Client auth: `client_secret_basic`, `client_secret_post`, or none for a public client. One-time codes (consumed by the first attempt, even a failed one), `CodeLifetimeSeconds` expiry, same client and `redirect_uri`, PKCE verified. Returns `id_token`, an opaque `access_token`, `token_type=Bearer`, `expires_in`, `scope`; `Cache-Control: no-store`. |
| JWKS | `GET /jwks` | The public half of the RSA-2048 key generated at start (`kty`, `use`, `alg`, `kid`, `n`, `e`). |
| Userinfo | `GET` or `POST /userinfo` | `Authorization: Bearer <access_token>` gives `sub`, `email`, `preferred_username`, `upn`, `name`; otherwise 401 `invalid_token`. |
| End-session | `GET /end-session` | No `id_token_hint`: signed-out page. A hint not signed by this run for a registered client (or for another client than `client_id`): 400 page. A valid hint: 302 to `post_logout_redirect_uri` (with `state`) only when that URI is registered for the hint's client, else the signed-out page. |

id_token claims: `iss`, `aud` (client id), `azp`, `sub`, `iat`, `exp`, `nonce` (when sent), and `email`,
`preferred_username` (the login name), `upn`, `name` when the fixture user has them.

## Use
**Run it locally** (the launch profile sets `ASPNETCORE_ENVIRONMENT=Development` and `http://localhost:5190`):

```powershell
# from the repository root (the .NET 10 SDK is private here: E:\codelab-tools\dotnet10\dotnet.exe)
dotnet run --project backend/tools/Starter.DevIdp
# without the launch profile, name the environment yourself; any other environment exits with code 2
dotnet run --project backend/tools/Starter.DevIdp --no-launch-profile -- --environment Development --urls http://localhost:5190
dotnet test backend/tools/Starter.DevIdp.Tests
```

Point the API at it with the `Codelab:Identity:Sso` settings (Authority `http://localhost:5190`, ClientId
`starter-web`, RedirectUri `http://localhost:5180/api/auth/sso/signin-oidc`, PostLogoutRedirectUri
`http://localhost:4280/login`, the explicit endpoints from the discovery document, signing keys from `/jwks`). The
committed `starter-web` client is public (PKCE only), so no secret is needed locally.

Fixture users (`appsettings.Development.json`, embedded): `dev.viewer` (`dev-sso-0001`, matches the seeded dev account
by login name), `dev.admin` (`dev-sso-0002`) and `dev.unmatched` (`dev-sso-0003`, for "no matching account" tests);
each has `<login>@example.test` as email and UPN. A `login_hint` may be the subject (exact), or the login name, email
or UPN (ignoring case).

**Host it in a test** (`Starter.Integration.Tests` references this project):

```csharp
await using var idp = await DevIdpInProcessServer.StartAsync(
    configure: options =>
    {
        // optional: a confidential client with a secret generated per run, extra redirect URIs, other users
        options.Clients[0].ClientSecret = generatedSecret;
    },
    timeProvider: fakeClock);                       // optional: drives code/token expiry and iat/exp

// 1. The API's named OIDC HttpClient sends to the IdP through this handler (any absolute URL reaches it).
services.AddHttpClient(OidcClientName).ConfigurePrimaryHttpMessageHandler(() => idp.CreateHandler());
// 2. The test plays the browser: follow the API's 302 to /authorize (add login_hint=dev.viewer), read the 302 back.
using var browser = idp.CreateClient();             // BaseAddress = idp.Issuer (http://localhost:5190); no auto-redirect
```

`DevIdpInProcessServer` reads ONLY the embedded settings plus `configure` (never environment variables or the test
project's own appsettings), so a test sees the same clients and users on every machine. `idp.Options` is the effective
configuration and `idp.Issuer` the base of every URL it publishes. Another host can also mount it with
`services.AddDevIdp(configuration)` + `app.MapDevIdp()` (same Development guard).

## Extend
- **A fixture user or client:** add it to `appsettings.Development.json` (neutral `example.test` data only), or in a
  test through `configure`. `DevIdpOptionsValidator` fails the start on a duplicate, an empty list or a relative URI.
- **A claim:** add its name to `Protocol/OidcClaimNames`, the value to `DevIdpUser`, emit it in `Token/UserClaims`
  (id_token and userinfo share it), list it in `DiscoveryDocument.ClaimsSupported`, and test it in `TokenTests`.
- **A protocol feature** (another grant, `prompt`, `max_age`): only when the API needs it and a real provider has it.
  Constants go in `Protocol/`, the handler in its endpoint folder, the HTTP tests in the test project.

## Rules
- Development only, never deployed (`starterdevidp.development-only`): the process, `MapDevIdp` and
  `DevIdpInProcessServer` all call `DevIdpEnvironmentGuard`; the project is not packable or publishable and no deploy
  kit may include it.
- No key material (`starterdevidp.no-key-material`): the RSA key lives in memory only; client secrets are never
  committed (the committed client is public; tests generate secrets at run time).
- Standard protocol only (`starterdevidp.standard-protocol`); names and values from `Protocol/` constants.
- Self-contained (`starterdevidp.no-starter-dependency`): no `using Starter.*` outside this tool.
- Neutral fixtures (`starterdevidp.neutral-fixtures`).

## Gotchas
- `appsettings.Development.json` is an **embedded resource**, not a copied file: a test project that references both
  this tool and the API would otherwise get two `appsettings.Development.json` in its output. Override it with
  environment variables (`DevIdp__Users__3__Subject=...`) or the command line, which take precedence.
- Every start generates a new key and `kid`, and codes and access tokens are in memory: after a restart, earlier
  id_tokens fail as `id_token_hint` (400) and the API must refetch `/jwks`.
- `DevIdp:Issuer` must have no path: the endpoints are mapped at the root.
- A public client's presented `client_secret` is ignored (PKCE is its proof); set `ClientSecret` to make a client
  confidential.
- The picker form has no CSRF token and the IdP keeps no session cookie; end-session therefore only validates the hint
  and redirects. Fine for a local double, one more reason it must never be reachable from outside the machine.
- The entry point is the named class `DevIdpEntryPoint` (no top-level statements), so tests that reference both this
  tool and the API do not see two global `Program` types.
