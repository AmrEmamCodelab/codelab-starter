# Codelab.Integration

## Purpose

The outbound call engine and the integration gateway contracts, version 0.6. Every HTTP call a starter service makes
to another system goes through a Codelab outbound client:

- **Typed clients.** `AddCodelabOutboundClient<TClient>(name, ...)` registers a typed `HttpClient` with named,
  start-up-validated options.
- **Retry and timeout policy.** Microsoft.Extensions.Http.Resilience / Polly.Core, outermost first: total timeout,
  retry, attempt timeout. All values come from options.
- **Failure classification.** `OutboundFailureClassifier` maps a status code or an exception to `OutboundFailureKind`
  (stored as int, explicit values) and says whether it is transient. The retry policy and the host's error mapping use
  the same ruling.
- **Certificate validation that is always on.** This fixes the source system's internal calls, whose certificate check
  always passed. There is no "accept anything" mode:
  - `SystemTrust` (default): no callback at all, so .NET's own chain validation against the OS store.
  - `PinnedAuthority`: only the configured internal CA bundle. System roots are not trusted, the certificate must allow
    TLS server authentication, and the host name must match.
  - `PinnedLeaf`: only SHA-256-pinned server certificates, still checked for host name and validity dates.
- **Correlation hook.** A handler slot (`AddCodelabOutboundHandler<THandler>()`) puts host handlers outermost, so a
  correlation-id handler from the observability package or the host runs once per call. This package does not depend
  on the observability package.

- **Integration gateway contracts** (namespace `Codelab.Integration.Gateway`, since 0.6). The backend has no egress;
  the DMZ gateway relays to the outside on its behalf, one **profile** per upstream:
  - The wire contract both sides compile against: `GatewayProfileKey` (`sso`, `sms`), `GatewayRoutes`
    (`POST /relay/sso/token`, `GET /relay/sso/jwks`, `POST /relay/sms`, `/health/live`, `/health/ready`), the DTOs
    `SsoTokenRelayRequest`, `SmsRelayRequest` / `SmsRelayResult` (+ `SmsRelayRequestValidator`), the limits
    (`GatewayRelayLimits`) and the problem codes `GatewayFailureCodes` (`GATEWAY_UPSTREAM_UNAVAILABLE` 503,
    `GATEWAY_UPSTREAM_REJECTED` 502, `GATEWAY_PROFILE_DISABLED` 404) with `GatewayProblems.For(code)`.
  - The extension point: `IGatewayProfile` (`Key`, `Map(endpoints)`), `AddGatewayProfile<T>()`, and the
    `GatewayProfileRegistry` that fails the start on a malformed or duplicate key or an enabled key without a profile.
    `MapGatewayProfiles()` maps the enabled profiles and answers a disabled one with 404 `GATEWAY_PROFILE_DISABLED`.
  - The caller's typed client: `AddCodelabGatewayClient(configuration)` registers `GatewayRelayClient`
    (`RelaySsoTokenAsync`, `GetSsoJwksAsync`, `RelaySmsAsync`) on this engine. Every attempt carries a fresh
    Codelab.Security ES256 service assertion bound to its method, path and body; every failure surfaces as a
    `GatewayRelayException` whose code is mapped through `OutboundFailureClassifier`.

  The gateway host itself (`services/integration-gateway`) is not in this package; it uses these contracts. The source
  system's dispatcher, auth strategies and call log were not taken: the gateway relays, it does not orchestrate.

## Use

```csharp
// The host's own typed options section name lives in a constant, never a string literal at the call site.
services.AddCodelabOutboundClient<ArchiveClient>(ArchiveClient.Name, configuration.GetSection(ArchiveOptions.SectionName));

// Or in code:
services.AddCodelabOutboundClient<ArchiveClient>(ArchiveClient.Name, options =>
{
    options.BaseAddress = new Uri("https://archive.example.test/");
    options.Certificate.Mode = CertificateTrustMode.PinnedAuthority;
    options.Certificate.AuthorityBundlePath = archiveOptions.CaBundlePath;
});

services.AddCodelabOutboundHandler<CorrelationIdHandler>(); // outermost, once per call

// Retry a POST only when the upstream de-duplicates it:
var request = new HttpRequestMessage(HttpMethod.Post, "orders") { Content = body }.MarkIdempotent();
```

Configuration shape (one section per client):

```json
{
  "BaseAddress": "https://archive.example.test/",
  "Retry": { "MaxRetries": 2, "BaseDelay": "00:00:00.400", "MaxDelay": "00:00:10" },
  "Timeout": { "AttemptTimeout": "00:00:10", "TotalTimeout": "00:00:30" },
  "Certificate": { "Mode": 2, "AuthorityBundlePath": "C:\\certs\\internal-ca.pem", "CheckRevocation": false }
}
```

`Certificate.Mode` is the integer value: 1 is `SystemTrust`, 2 is `PinnedAuthority`, 3 is `PinnedLeaf`. Defaults are
in `OutboundDefaults`.

### Integration gateway, caller side (the API)

```csharp
services.AddCodelabSecurity(configuration);          // the protocol the assertion handler needs
services.AddCodelabGatewayClient(configuration);     // GatewayRelayClient, validated at start-up

// In an adapter (inject GatewayRelayClient):
var result = await gateway.RelaySmsAsync(new SmsRelayRequest { To = phone, Text = text, Language = GatewayLanguages.Arabic }, ct);
using var tokens = await gateway.RelaySsoTokenAsync(new SsoTokenRelayRequest(formFields).ToContent(), ct);
using var jwks = await gateway.GetSsoJwksAsync(ct);
```

```json
{
  "Codelab": {
    "Integration": { "Gateway": { "BaseUrl": "https://gateway.internal.example.test/", "Audience": "integration-gateway", "Profiles": [ "sso", "sms" ] } },
    "Security": { "ServiceAssertions": { "Client": { "ServiceName": "starter-api", "KeyId": "<kid>", "PrivateKeyPem": "<secret>" } } }
  }
}
```

The section also takes `Retry`, `Timeout` and `Certificate` exactly like an outbound client section. The assertion
identity (ServiceName, KeyId, PrivateKeyPem, LifetimeSeconds) is the host's one S2S identity from
`Codelab:Security:ServiceAssertions:Client`; only the audience comes from the gateway section, so the same host can also
call the attachment service with that section's own audience.

### Integration gateway, gateway side

```csharp
services.AddAuthentication().AddCodelabServiceAssertion(configuration);    // the only inbound scheme
services.AddCodelabGatewayProfiles(configuration).AddGatewayProfile<SmsRelayProfile>().AddGatewayProfile<SsoRelayProfile>();

app.MapGroup(string.Empty)
   .RequireAuthorization(policy => policy.AddAuthenticationSchemes(ServiceAssertionAuthenticationHandler.SchemeName).RequireAuthenticatedUser())
   .MapGatewayProfiles();
```

`Codelab:Integration:Gateway:Profiles` lists the enabled profile keys. The gateway's
`Codelab:Security:ServiceAssertions:Server:Audience` must equal the callers' `Audience` (default
`GatewayDefaults.Audience`, `integration-gateway`).

## Extend

- **Another handler for every client:** call `AddCodelabOutboundHandler<T>()`. Slot handlers run in registration
  order, outside the retry policy.
- **A handler for one client, per attempt:** call `AddHttpMessageHandler` on the `IHttpClientBuilder` that
  `AddCodelabOutboundClient` returns. It runs inside the retry policy, once per attempt.
- **A new failure kind:** add an `OutboundFailureKind` member with the next explicit value and never renumber. Map it
  in `OutboundFailureClassifier`, then add a row to `OutboundFailureClassifierTests`.
- **A new gateway profile:** follow [docs/recipes/add-integration.md](../../../docs/recipes/add-integration.md). Add the
  key to `GatewayProfileKey`, its routes to `GatewayRoutes` (under `/relay/{key}`), and its DTOs and validator here.
  The profile class (`IGatewayProfile`) lives in the gateway service; a typed client method lives here or in the
  calling module. Add contract snapshot tests to `GatewayContractTests`.
- **A new gateway failure code:** add the constant to `GatewayFailureCodes` and its case in `StatusOf` and `TitleOf`,
  then a row in `GatewayContractTests`. Never rename a code; callers and logs match on it.
- **A new trust mode:** add a `CertificateTrustMode` value, an `IServerCertificateValidator` and its case in
  `ServerCertificatePolicy` and in `OutboundCertificateOptionsValidation`. Add a real-handshake test to
  `ServerCertificateValidationTests`. The validator must refuse by default.

## Rules

- The public API is stable since 1.0.0 (`PublicAPI.Shipped.txt`): a breaking change needs a new major version; a fix ships as a patch release (ADR-013).
- No always-true certificate validator, anywhere. `ServerCertificatePolicy` is the only file that may set a
  validation callback. `NoAlwaysTrueValidatorTests` fails the build otherwise.
- A pinned anchor never excuses a host-name mismatch or a missing certificate (`HandshakeErrors`).
- Retries fail closed. Only GET, HEAD and OPTIONS, or a request marked with `MarkIdempotent`, are retried, and only
  after a transient failure. A refused certificate, a 4xx other than 408 and 429, and a cancellation are never retried.
- Every number lives in `OutboundDefaults` or the options. Options are validated at start-up (`ValidateOnStart`) and
  on every read, and all failures are reported in one message.
- This package depends only on Codelab.Security (service assertions; see `module.rules.yaml`), never on
  Observability, Errors or Testing. The public API is tracked in `PublicAPI.Unshipped.txt`.
- The gateway accepts one inbound scheme only: the Codelab.Security ES256 service assertion. No JWT, no API key. A
  profile never adds its own authentication.
- Relay POSTs are never retried (an authorization code is single-use and an SMS provider may not de-duplicate); the
  JWKS GET is retried when transient. Each attempt gets a fresh assertion (the handler sits inside the retry policy),
  because the gateway refuses a replayed `jti`.
- Gateway refusals are problem+json with a `code` from `GatewayFailureCodes` and a neutral title. Upstream text never
  reaches the caller, and `GatewayRelayException.Message` never contains response text.
- Every route, key, code, limit and media type is a constant here; the gateway and the API never write them as
  literals.

## Gotchas

- `HttpClient.Timeout` is set to infinite on purpose: the resilience pipeline owns timeouts. A client timeout would
  cut across the retries.
- `MaxRetries = 0` leaves the retry strategy out of the pipeline. Polly itself refuses 0 attempts, so the engine does
  not add a retry strategy with a predicate that never fires.
- A Retry-After value is honoured only on 429 and 503, and is capped at `Retry.MaxDelay`. Its date form is measured
  against the injected `TimeProvider`.
- Revocation checking is off by default, as in .NET. A backend zone without egress usually cannot reach a CRL or OCSP
  endpoint. Turn on `CheckRevocation` where the internal CA publishes one.
- In the TLS tests on Windows, Schannel cannot serve a certificate with an ephemeral key, so the run-time test
  certificates are round-tripped through PKCS#12.
- Two `AddCodelabOutboundClient` calls with the same `TClient` and different names register the typed client twice,
  and the last one wins. Use one typed client per upstream.
- Since 0.6 the package references the ASP.NET Core shared framework (`IGatewayProfile.Map` takes an
  `IEndpointRouteBuilder`) and Codelab.Security. A plain console host that uses only the outbound engine now gets that
  framework too.
- `AddCodelabGatewayClient` needs `SecurityProtocol` from `AddCodelabSecurity`; without it the first call fails to
  build the assertion handler. The BaseUrl may be `http` only in Development (`IHostEnvironment`); with no
  `IHostEnvironment` registered the client behaves as Production.
- The BaseUrl gets a trailing slash and the routes are sent relative to it, so a gateway under an IIS application path
  (`https://dmz.example.test/gateway/`) works, and the assertion binds the full path the gateway sees.
- The JSON uses web defaults, so `+` and non-ASCII characters are `\u`-escaped on the wire. That is valid JSON; compare
  parsed values, never raw bodies.
- `RelaySsoTokenAsync` and `GetSsoJwksAsync` return a `JsonDocument`: dispose it. The token response holds tokens;
  never log it.
