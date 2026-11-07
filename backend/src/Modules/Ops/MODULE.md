# Ops module

## Purpose

Module 16 (logs, error reference codes and the Ops Console), backend part: the `/api/ops` surface that the Ops Console
API calls on behalf of an operator, and the data behind it. It replaces the source system's four service-auth schemes
and its machine-route guards with **one credential**: the Codelab.Security ES256 per-request service assertion. The
module owns:

- **`/api/ops`**, four read-only endpoints for the Ops Console (never the staff SPA):

| Endpoint | Result |
| --- | --- |
| `GET api/ops/health` | `{status, checkedAt, components: [{name, status, durationMs, code}]}`: the database, and every readiness URL in `Codelab:Ops:ReadinessProbes` (the attachment service, the integration gateway). `status` is `ComponentStatus` (Healthy = 1, Degraded = 2, Unhealthy = 3); the system status is the worst component. Always 200: an unhealthy component is an answer. |
| `GET api/ops/jobs` | `[{key, nameAr, nameEn, cron, lastRun}]`, ordered by key: every job registered with `AddStarterJob`. `lastRun` is the newest run `{status, trigger, startedAt, finishedAt, referenceCode}` (the reference code of a failed run), read through the kernel `IJobRunHistory`; null for a job that never ran, and for every job when the jobs module is removed. |
| `GET api/ops/certificates` | `[{name, subject, notAfter, daysLeft, status, code}]`, the most urgent first: the certificates in `Codelab:Ops:Certificates` and the identity provider's signing certificates from its JWKS (through the SSO back channel when single sign-on is on, see `IdentityProviderJwksUrl`). `status` is `CertificateStatus` (Ok = 1, Warning = 2, Critical = 3, Expired = 4). |
| `GET api/ops/reference-codes/{code}` | `{referenceCode, correlationId, traceId, firstSeen, request: {method, path, status}, searchFrom, searchTo}`: the error response that showed the code and the window to search the log (Seq) in. 400 `OPS_REFERENCE_CODE_INVALID` for a malformed code, 404 `OPS_REFERENCE_CODE_NOT_FOUND` when it is not on record. |

- **`ops.OperatorAccess`**: the Ops audit trail, one row per call: the operator name (header `X-Codelab-Operator`),
  the calling service, the endpoint (`OpsEndpoint`, int), the reference code asked about, the correlation id, the time.
- **`ops.ReferenceCodeSightings`**: the reference-code index. Every response of the host with a status of 400 or more
  and a problem+json body (the Codelab.Errors envelope always shows `referenceCode`) is recorded by
  `ReferenceCodeCaptureMiddleware` (outermost, through an `IStartupFilter`) into a bounded in-memory inbox, and written
  in batches by the hosted `ReferenceCodeIndexWriter`. Identifiers only: code, correlation id, method, path without the
  query string, status, time.
- **The job `ops-reference-code-purge`** (`30 2 * * *`), which deletes sightings older than the retention.

## Use

**Authentication and authorization (the only way in).** Every action of `OpsController` carries
`[OpsServiceCaller]`: policy `ops-service-caller`, authenticated by the `CodelabServiceAssertion` scheme **only**, plus
`OpsTrustedCallerRequirement` (the principal is a service identity, and the service is the one whose key id is
`Codelab:Ops:TrustedCallerKeyId` in `Codelab:Security:ServiceAssertions:Server:TrustedServices`). Consequences:

- a bearer token, a cookie or an API-key header authenticates nothing here: **401**;
- no assertion, an expired, tampered, wrong-audience or replayed assertion (jti single use), or one bound to another
  method, path, query or body: **401**;
- a valid assertion of another trusted service (the gateway): **403**;
- the Ops Console's valid assertion without `X-Codelab-Operator`: **400 `OPS_OPERATOR_REQUIRED`** (nothing runs).

Request signing does not apply to service principals (Codelab.Security skips them), so the console signs
nothing but the assertion. The front site blocks `/api/ops/*` (404); only the console reaches the API host.

**The calling side** (the Ops Console API): an `HttpClient` with `AddCodelabServiceAssertion` (client options with the
API's audience), and on every request the header `X-Codelab-Operator: <DOMAIN\account>` (`OpsHeaders.Operator`).

**Configuration** (`Codelab:Ops`, validated on start by `OpsOptionsValidator` and `OpsTrustedCallerValidator`):

| Key | Default | Meaning |
| --- | --- | --- |
| `TrustedCallerKeyId` | empty | The Ops Console API's key id. Empty: the surface is **closed** (every call 401) and no server assertion settings are needed. Set: `Codelab:Security:ServiceAssertions:Server` (`Audience`, `TrustedServices` with this key id) is required. |
| `CertificateWarningDays` / `CertificateCriticalDays` | 30 / 7 | Fewer whole days left than these is Warning / Critical (1 <= critical < warning <= 365). |
| `ProbeTimeoutSeconds` | 5 | The budget of one component check and of the JWKS read (1 to 30). A ready answer slower than half of it is Degraded. |
| `SearchMarginMinutes` | 15 | The log search window either side of a sighting (1 to 1440). |
| `ReferenceCodeRetentionDays` | 30 | How long a sighting is kept (1 to 365); match the Seq retention. |
| `CaptureReferenceCodes` | true | Record error responses in the index. |
| `ReadinessProbes:N:Name` / `:Url` | none | A component (lowercase kebab, for example `attachment-service`, `integration-gateway`) and its absolute readiness URL (`https://.../health/ready`). Read once at start. |
| `Certificates:N:Name` with `:Thumbprint` (+ `:StoreName` `My`, `:StoreLocation` `LocalMachine`) or `:Path` | none | A watched certificate: from the Windows store, or a public certificate file (`.cer`/`.crt`/`.pem`, never a private key). |
| `IdentityProviderJwksUrl` | empty | The identity provider's JWKS, fetched directly, used **only when single sign-on is not a login mode** of this host; each signing key with an `x5c` certificate is a row `idp-signing-N`. With `sso` in `Auth:Modes` the JWKS comes through the kernel `IIdentityProviderKeySet` (the Identity module's SSO back channel: direct, or the gateway relay with `Codelab:Identity:Sso:BackChannel` GatewayRelay) and this key is ignored. |

**Error and finding codes.** `OpsErrorCodes` (400/404 above) are refusals in the problem+json envelope. Health
components and certificate rows carry `OpsFindingCodes` (`OPS_DATABASE_UNREACHABLE`, `OPS_PROBE_TIMEOUT`,
`OPS_PROBE_UNREACHABLE`, `OPS_PROBE_NOT_READY`, `OPS_PROBE_SLOW`, `OPS_CERTIFICATE_UNREADABLE`,
`OPS_IDP_KEYS_UNAVAILABLE`, `OPS_IDP_KEY_WITHOUT_CERTIFICATE`) in `code`; the console owns the text.

## Extend

- **Another component in the health summary:** a readiness URL needs only a `Codelab:Ops:ReadinessProbes` entry. A
  check that is not an HTTP readiness URL is an `IComponentProbe` (Application port) in `Infrastructure/Health`,
  registered scoped in `OpsModuleRegistration.AddProbes`; run its work through `ProbeBudget.RunAsync` so it is timed,
  bounded and never throws for an unhealthy component. Add its finding codes to `OpsFindingCodes`.
- **Another certificate source** (for example LDAPS certificates of the directory): an `ICertificateSource` in
  `Infrastructure/Certificates`, registered scoped; return a `CertificateFact` with a finding code for anything it
  cannot read. `ListCertificates` judges and orders every source's rows.
- **Another endpoint:** an action on `OpsController` with `[OpsEndpoint(...)]` (add the `OpsEndpoint` member with an
  explicit value) and a route constant in `OpsRoutes`; one use case in `Application`; the `[OpsServiceCaller]`
  authorization and the operator record come from the controller. Add its line to the contract allowlist as a
  service-assertion endpoint.
- **Remove the module:** `tools/remove-module.ps1 -Name Ops`. The `ops` schema stays in an existing database until a
  DBA drops it; the edge block for `/api/ops` can stay.

## Rules

- `/api/ops` accepts only the ES256 service assertion of the trusted caller. Never add `[HasPermission]`,
  `[AllowAnonymous]`, `[AuthenticatedUser]`, `[SignatureExempt]` or a second scheme to an Ops endpoint.
- Every Ops action carries `[OpsEndpoint]` and runs behind `OperatorAccessFilter`: no operator, no answer.
- Health and certificate rows carry finding codes only: never an exception message, a host name or a URL. Logs carry
  identifiers only (endpoint, correlation id, counts), never the operator name.
- Probes and the JWKS read use the default TLS validation; never accept an untrusted certificate.
- The module never references another module; jobs come from the kernel `IJob` registrations and their runs from the
  kernel `IJobRunHistory`, the IdP keys from the kernel `IIdentityProviderKeySet` (both optional: absent when the jobs
  module is removed or single sign-on is off).
- Enums are stored and sent as their explicit integer values; keys, routes, limits and codes are constants.

## Gotchas

- **The operator header is not covered by the assertion.** Codelab.Security binds the assertion to the method, the
  path and query, and the body hash; headers are not bound. The operator name is therefore attributed to the trusted
  caller (which authenticated the operator by Windows group membership), not cryptographically signed. A future
  Codelab.Security version that binds a header (or an `act` claim) would close that gap.
- **`lastRun` is the newest run row, whatever its state:** a run that is queued or still running shows as such, so a
  long job hides the outcome of its previous run until it ends. The Ops module never reads `jobs.JobRuns` itself.
- **The operator record is best effort.** If `ops.OperatorAccess` cannot be written, the failure is logged
  (`OpsOperatorAccessNotRecorded`, endpoint and correlation id) and the call still answers, so the health summary works
  while the database is down. The Ops Console API keeps its own log of who called what.
- **The reference-code index lags by milliseconds and can drop.** The writer runs in the background; a code looked up
  in the same instant may not be there yet. When the inbox (1024 sightings) is full, or a batch fails to write,
  sightings are dropped: the error is still in the log under its correlation id, so search Seq by `ReferenceCode`.
- **Every problem+json error is indexed, including 401 and 404 answers to scanners.** The retention purge bounds the
  table; set `CaptureReferenceCodes` false on a host that should not record them.
- **The closed surface uses a stand-in scheme.** With `TrustedCallerKeyId` empty the module registers
  `ClosedServiceAssertionHandler` under the `CodelabServiceAssertion` name, unless another module already added the real
  scheme (then a valid assertion is 403, because no caller is trusted for Ops). A module that adds the real scheme
  after this one would collide with the stand-in: register it before the Ops module, or open the Ops surface.
- **`Codelab:Ops` is read at start.** Changing it (probes, certificates, thresholds, the JWKS URL) needs an app-pool
  recycle.
- **In the gateway topology** the API host has no egress: with `sso` on and `Codelab:Identity:Sso:BackChannel`
  GatewayRelay the JWKS is read through the gateway's SSO relay (the sign-in's own path), so no URL is needed. An
  unreachable gateway or provider shows one row `idp-signing` with `OPS_IDP_KEYS_UNAVAILABLE`. Without `sso`,
  `IdentityProviderJwksUrl` must be a URL the API can reach over validated TLS.
