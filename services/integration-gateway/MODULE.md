# Integration gateway (Codelab.Gateway.Api)

## Purpose

The DMZ relay, released as its own deployable (build plan phase 8). In the production topology the backend API has
**no egress**: it reaches SQL Server, the internal SMTP relay and this gateway, nothing else. Every call the backend
must make to the outside world goes through a gateway **profile**:

| Profile key | Routes | Upstream |
| --- | --- | --- |
| `sso` | `POST /relay/sso/token`, `GET /relay/sso/jwks` | The OIDC provider's token endpoint and JWKS document (the SSO login back channel). |
| `sms` | `POST /relay/sms` | The client's SMS provider, through one generic HTTP adapter driven by templates. |

The gateway authenticates **one credential only**: the Codelab.Security ES256 service assertion of a trusted backend
service (scheme `CodelabServiceAssertion`), with the jti replay guard and the method, path and body binding. It holds
no client secret and no user data: the token request arrives complete from the backend and is passed through; the SMS
provider credential is the only secret it keeps.

| Project | Holds |
| --- | --- |
| `src/Codelab.Gateway.Api` | The host: `Program.cs`, `Authentication/` (the one scheme and policy), `Relay/` (upstream exchange, failure mapping, bounded bodies, refusals), `Sso/` and `Sms/` (the two profiles), `Diagnostics/` (log lines, upstream readiness), `Hosting/`, `web.config`. |
| `tests/Codelab.Gateway.Api.Tests` | Unit tests of every rule, and HTTP tests that start the real `Program` in memory against `FakeUpstream` (a loopback HTTPS host playing the OIDC provider and an SMS provider stub), plus a contract test with the backend's real `GatewayRelayClient`. |

Built only on the shared packages: Codelab.Build.Sdk, Codelab.Security (protocol, S2S assertions, replay guard,
headers), Codelab.Observability (Serilog, correlation id, health), Codelab.Errors (the problem+json envelope) and
Codelab.Integration 0.5 (the gateway contracts in `Codelab.Integration.Gateway`, and the outbound engine with its
certificate policy for every upstream call).

## Use

### Run

```powershell
# Development, http://localhost:5187 (Properties/launchSettings.json). The SSO profile points at the local DevIdp (:5190).
E:\codelab-tools\dotnet10\dotnet.exe run --project services\integration-gateway\src\Codelab.Gateway.Api
# http://localhost:5187/health/live   http://localhost:5187/health/ready
```

The host refuses to start until it trusts a caller: put the API's S2S public key in the gitignored
`src/Codelab.Gateway.Api/appsettings.Development.local.json` (or environment variables):

```json
{
  "Codelab": { "Security": { "ServiceAssertions": { "Server": { "TrustedServices": [
    { "ServiceName": "<the API's Codelab:Security:ServiceAssertions:Client:ServiceName>", "KeyId": "<its KeyId>", "PublicKeyPem": "<its public key>" }
  ] } } } }
}
```

`tools/devseed/New-LocalSecrets.ps1` does not write this file yet; until it does, copy the API's public key by hand.

### Endpoints

| Route | Auth | Body in | Answer |
| --- | --- | --- | --- |
| `POST /relay/sso/token` | assertion | `application/x-www-form-urlencoded`, at most 16 KiB and 32 fields | the provider's JSON verbatim, `Cache-Control: no-store` |
| `GET /relay/sso/jwks` | assertion | none | the provider's JWKS verbatim (cached `Gateway:Sso:JwksCacheDuration`) |
| `POST /relay/sms` | assertion | `SmsRelayRequest` JSON `{to, text, language}` (1 Arabic, 2 English), at most 16 KiB | `SmsRelayResult` `{accepted, providerMessageId?, referenceCode}` |
| `GET /health/live`, `GET /health/ready` | anonymous | none | readiness: a profile is enabled, every enabled upstream address is configured and its host resolves |

Failures are the Codelab.Errors envelope (`code`, `correlationId`, `referenceCode`):

| Situation | Status | `code` |
| --- | --- | --- |
| No, invalid, replayed, rebound or untrusted assertion; a JWT; an API key | 401 | `UNAUTHENTICATED` |
| Wrong content type, unparsable body, nameless or non-ASCII form field | 400 | `MALFORMED_REQUEST` |
| SMS request that fails `SmsRelayRequestValidator` | 400 | `VALIDATION_FAILED` |
| Upstream unreachable, timed out, throttled, 5xx, or its TLS certificate refused | 503 | `GATEWAY_UPSTREAM_UNAVAILABLE` |
| Upstream refused (4xx other than 408/429), redirected, or answered something unusable | 502 | `GATEWAY_UPSTREAM_REJECTED` |
| Profile registered but not listed in `Codelab:Integration:Gateway:Profiles` | 404 | `GATEWAY_PROFILE_DISABLED` |

The backend calls the gateway with `services.AddCodelabGatewayClient(configuration)` (Codelab.Integration): the typed
`GatewayRelayClient` signs every attempt and maps every failure to `GatewayRelayException.Code` with the codes above.

### Configuration

| Key | Meaning |
| --- | --- |
| `Codelab:Integration:Gateway:Profiles` | The enabled profile keys (`sso`, `sms`). Shipped empty: the server install chooses. Only an enabled profile registers its upstream client and validates its section at start. |
| `Codelab:Security:ServiceAssertions:Server:Audience` | `integration-gateway` (`GatewayDefaults.Audience`); the API's `Codelab:Integration:Gateway:Audience` must match. |
| `Codelab:Security:ServiceAssertions:Server:TrustedServices:N:{ServiceName,KeyId,PublicKeyPem}` | The callers: the API's S2S identity and public key. **Required.** |
| `Codelab:Security:ServiceAssertions:Server:MaxBodyBytes` | 16384, equal to the IIS `maxAllowedContentLength` and `GatewayHostSettings.MaxRequestBodyBytes` (a test pins all three). |
| `Codelab:Security:ReplayProtection` | `Memory` + `AcknowledgeSingleInstance` (one gateway node; see Gotchas). |
| `Gateway:Sso:{TokenEndpoint,JwksUri}` | The provider endpoints (https outside Development). |
| `Gateway:Sso:JwksCacheDuration` | 0 to 24 h, default 10 min; 0 fetches on every call. |
| `Gateway:Sms:UrlTemplate` | Provider URL with placeholders `{to}`, `{text}`, `{language}`, `{sender}` (percent-encoded). |
| `Gateway:Sms:Method` / `BodyTemplate` / `BodyFormat` | `1` GET (no body) or `2` POST with a body template; `1` JSON (values JSON-escaped) or `2` form (percent-encoded). |
| `Gateway:Sms:AuthHeaderName` / `AuthHeaderValue` | The provider credential header. The value is a **secret** (environment or secret store). |
| `Gateway:Sms:Sender`, `Gateway:Sms:LanguageCodes:{Arabic,English}` | The sender id and the provider's language codes (default `ar`, `en`). |
| `Gateway:Sms:AcceptedMember` / `MessageIdMember` | Optional dotted paths into the provider's JSON answer; empty: any 2xx is accepted. |
| `Gateway:{Sso,Sms}:{Retry,Timeout,Certificate}` | The Codelab.Integration outbound policy per upstream. `Certificate:Mode` 1 system trust, 2 pinned authority (`AuthorityBundlePath`), 3 pinned leaf (`PinnedThumbprints`, SHA-256). SMS ships with `Retry:MaxRetries` 0. |
| `Gateway:Readiness:{ResolveUpstreamHosts,ResolveTimeout}` | DNS check of the upstream hosts in readiness (turn off behind a resolving proxy). |
| `Gateway:ServiceName` | The log and trace name (default `starter-gateway`). |

## Extend

A new upstream is a new profile; the recipe is `docs/recipes/add-integration.md`. In short:

1. Add the contract to Codelab.Integration (or, for a project-only profile, to the project): a key that passes
   `GatewayProfileKey.IsValid`, route constants under `/relay/{key}/`, request and result DTOs with a validator.
2. In this service add a folder `<Name>/` next to `Sso/` and `Sms/` with an options class (`: UpstreamOptions`, its own `Gateway:<Name>`
   section), its validator, a typed upstream client registered through `UpstreamClients.Add`, and a class
   implementing `IGatewayProfile` (and `IRelayUpstreams`, so readiness checks its addresses) whose `Map` adds the
   routes. Answer failures with `RelayResponses.Failed` and bad input with `RelayRefusals`.
3. Register it like `SsoRelayRegistration`: `AddGatewayProfile<T>()` always, the client and validation only when the
   key is enabled. Call it from `AddGatewayHost`.
4. Tests: unit tests of the rules, and HTTP tests against a `FakeUpstream` route; never a real provider.
5. On the backend, call it through `GatewayRelayClient.PostJsonAsync`/`GetJsonAsync` behind a typed client.

A provider the template adapter cannot express gets its own `ISmsProvider` implementation registered in place of
`HttpSmsProvider`. The profiles planned for later are listed in [docs/future-profiles.md](docs/future-profiles.md).

## Rules

- One credential: the service assertion is the default and fallback policy; only `/health/*` is anonymous
  (`integrationgateway.assertion-only`). No JWT bearer, API key or cookie scheme (forbidden pattern).
- Every relay route belongs to a profile mapped by `MapGatewayProfiles`; a disabled profile answers 404
  `GATEWAY_PROFILE_DISABLED` after authentication and registers no upstream client.
- Upstream calls go only through Codelab.Integration outbound clients: certificate validation always on, never an
  accept-all callback, no hand-made `HttpClient` or handler (forbidden pattern), plain http only in Development,
  redirects never followed.
- Failures are coded (`UpstreamFailureMapper` is the one ruling); no upstream text reaches a caller.
- Relayed bodies, tokens, phone numbers, message texts and the provider credential are never logged
  (`GatewayLog` carries profile, operation, code, failure kind and upstream status only).
- No key material in committed settings or `web.config`; the gateway refuses a configured PublicId ring
  (`EphemeralPublicIdRing`).

## Gotchas

- **Single node.** The replay guard is in memory (`AcknowledgeSingleInstance`). Two gateway nodes behind a load
  balancer would each accept the same assertion once; run one node, or give the gateway the SQL replay store.
- **Token request client authentication.** Only the form is relayed, never an `Authorization` header: the backend
  must use `client_secret_post` (or `private_key_jwt`) towards the provider, not `client_secret_basic`.
- **No retry of POSTs.** A code is single-use and an SMS must not be sent twice; only the JWKS GET is retried.
- **JWKS freshness.** A failed refresh is answered as a failure, never with the stale copy, so a key rotation reaches
  the backend within `JwksCacheDuration` and a provider outage shows at once.
- **Readiness never calls an upstream.** A provider outage shows as 503 relays, not as the node leaving the load
  balancer. DNS resolution can be switched off when a proxy resolves names.
- **Start-up refusals** name the setting: no trusted caller, an enabled profile without its upstream, an http upstream
  outside Development, an unknown placeholder, an enabled key no profile registers.
- **Body ceiling.** A body above 16 KiB fails authentication (the assertion cannot be checked) and is answered 401,
  before any relay code runs.
