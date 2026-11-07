# Ops Console API (Codelab.OpsConsole.*)

## Purpose

The server side of the Ops Console (module 16), released as its own deployable and optional per project (init ticks
"Ops Console"). Operators paste the reference code a user read from an error dialog and see the request behind it; they
also see the API's health, background job status and certificate expiry. The console is a **read-only
backend-for-frontend** for the ops web:

- **Operators** sign in with **Windows** (Negotiate: Kerberos, NTLM fallback; under IIS it forwards to the server's
  Windows authentication) and must belong to **one AD group** (`Codelab:OpsConsole:OperatorGroup`). Nothing else
  authenticates: no JWT, API key or cookie. In Development only, a configured dev operator replaces Windows sign-in.
- **The starter API** is read through its `/api/ops` surface, which accepts **only** the Codelab.Security ES256 service
  assertion: every call carries a fresh assertion (jti, method, path and body bound) issued with the console's key, and
  names the operator in `X-Codelab-Operator`.
- **Seq** is read over its HTTP API with a read-only key, to merge the log events of the request into the trace.

| Project | Holds |
| --- | --- |
| `src/Codelab.OpsConsole.Application` | The reference-code lookup (`LookUpReferenceCode`: normalize, ask the API and Seq at once, merge), the ports `IOpsApiClient` and `ILogEventSource`, `OperatorName`, `CertificateExpiry` and `CertificateStatus`, `OpsConsoleErrorCodes`. No HTTP. |
| `src/Codelab.OpsConsole.Api` | The host: `Program.cs`, operator authentication and the operator policy (`Authentication/`), the `/api/ops` client and the Seq reader (`Upstream/`), the `/ops-api` endpoints (`Endpoints/`), options and wiring (`Hosting/`), `web.config`. |
| `tests/*` | Unit tests of both, and HTTP tests that run the real `Program` on Kestrel against a fake `/api/ops` (which checks every assertion with the real `ServiceAssertionValidator`) and a fake Seq. |

Built only on the shared packages: Codelab.Build.Sdk, Codelab.Security (protocol, the assertion issuer, headers),
Codelab.Observability (Serilog, correlation id, health, `ReferenceCode`) and Codelab.Errors (problem+json).

## Use

### Run

```powershell
& E:\codelab-tools\dotnet10\dotnet.exe run --project services\ops-console\src\Codelab.OpsConsole.Api   # http://localhost:5188
& E:\codelab-tools\dotnet10\dotnet.exe test services\ops-console\Codelab.OpsConsole.slnx
```

Development needs the console's ES256 key in the gitignored
`src/Codelab.OpsConsole.Api/appsettings.Development.local.json` (`Codelab:Security:ServiceAssertions:Client:KeyId` and
`PrivateKeyPem`), and the API must trust its public half (the API's `Codelab:Ops:TrustedCallerKeyId` and
`Codelab:Security:ServiceAssertions:Server:TrustedServices[]` entry `starter-ops-console`). Without the key the console
refuses to start and names the missing setting. The dev operator `DEV\ops-operator` (group `StarterOpsOperators`) signs
every request in; remove the group from `DevOperator:Groups` to see the 403 screen. The ops web dev server (:4281)
proxies `/ops-api` here.

### Endpoints (all GET, all behind the operator policy)

| Route | Answer | Refusals (problem+json `code`) |
| --- | --- | --- |
| `/ops-api/me` | `{ name, groups }`: the Windows account and the operator group that admitted it | 401 (no Windows sign-in, `WWW-Authenticate: Negotiate`), 403 `FORBIDDEN` (not in the group) |
| `/ops-api/health` | the API's `GET api/ops/health` body, passed through | 401, 403, 502 `UPSTREAM_UNAVAILABLE` |
| `/ops-api/jobs` | the API's `GET api/ops/jobs` body, passed through | 401, 403, 502 |
| `/ops-api/certificates` | `[{ name, subject, notAfter, daysLeft, status, code }]`, `status` = `CertificateStatus` (Ok 1, Warning 2, Critical 3, Expired 4); an unreadable certificate has null dates and a finding `code` | 401, 403, 502 |
| `/ops-api/reference-codes/{code}` | `{ referenceCode, correlationId, traceId, firstSeen, request { method, path, status }, events [{ timestamp, level, message }], seqUrl, seqAvailable }` | 400 `OPS_REFERENCE_CODE_INVALID`, 404 `OPS_REFERENCE_CODE_NOT_FOUND`, 502 `UPSTREAM_UNAVAILABLE` (API down, or the API does not know the code and Seq could not be asked) |

`{code}` is a reference code in any case (`9c1e5-b7a20`) or the full 32-hex correlation id; both become `9C1E5-B7A20`.
The API's facts win; Seq events fill what the API left out. Two requests can share a reference code (40 bits), so the
trace keeps only the events of the request it settled on. `seqAvailable` false means Seq was not asked or did not
answer, not that there were no events. `/health/live` and `/health/ready` are anonymous.

### The calls the console makes

| Upstream | Request | Credential |
| --- | --- | --- |
| Starter API | `GET api/ops/health`, `api/ops/jobs`, `api/ops/certificates`, `api/ops/reference-codes/{code}` (404 = unknown) | `X-{Prefix}-Service-Assertion` (ES256, issuer `starter-ops-console`, audience `starter-api`), plus `X-Codelab-Operator` and `X-Correlation-ID` |
| Seq | `GET api/events?filter=ReferenceCode = '<code>'&count=<MaxEvents>&render=true&fromDateUtc=<now - LookbackDays>` | `X-Seq-ApiKey` (read-only key), no trace headers |

`X-Codelab-Operator` carries the account name as is when it is printable ASCII without `%` (`CORP\jdoe`), otherwise
UTF-8 percent-encoded. The API records it; it authorizes nothing (the assertion does).

### Configuration

| Key | Where | Notes |
| --- | --- | --- |
| `Codelab:OpsConsole:OperatorGroup` | server | The AD group SID (`S-1-5-21-...`, preferred) or `DOMAIN\Group`. Required. |
| `Codelab:OpsConsole:Api:BaseUrl` / `TimeoutSeconds` | server | The API's base address (https, or http to loopback); `api/ops/*` is relative to it. Required. Timeout 1-120 s, default 15. |
| `Codelab:OpsConsole:Seq:BaseUrl` | server | Seq's address (UI and API). Empty: the console runs without log events. https outside Development unless loopback. |
| `Codelab:OpsConsole:Seq:ApiKey` | secret | A Seq key with the Read permission only. Environment variable `Codelab__OpsConsole__Seq__ApiKey` or the local secrets file, never a committed file. |
| `Codelab:OpsConsole:Seq:TimeoutSeconds` / `MaxEvents` / `LookbackDays` | `appsettings.json` | 15 s (1-120), 200 events (1-1000), 31 days (1-90). |
| `Codelab:OpsConsole:DevOperator:Enabled` / `Name` / `Groups` | `appsettings.Development.json` | Development only; enabled elsewhere refuses to start. |
| `Codelab:OpsConsole:ServiceName` | `appsettings.json` | Log and trace name, default `starter-ops-console` (ADR-003). |
| `Codelab:Security:ServiceAssertions:Client` | `ServiceName`/`Audience` in `appsettings.json`; `KeyId`/`PrivateKeyPem` secret | `ServiceName` = `starter-ops-console` (the API trusts this name with the key id), `Audience` = the API's server audience (`starter-api`). |
| `Codelab:Security:Protocol:Prefix` | `appsettings.json` | Must equal the API's prefix (the assertion type and header are bound to it). |
| `Observability:*` | server | As every Codelab host; `Observability:UserIdHash:Key` is required in Production. |

Required gaps for an install kit: `OperatorGroup`, `Api:BaseUrl`, the client `KeyId` and `PrivateKeyPem`, and
`Seq:ApiKey` when Seq needs a key. The csproj keeps `appsettings.*.local.json` out of every publish folder.

## Extend

- **A new screen backed by the API:** add the `GET api/ops/...` route to `OpsApiRoutes`, a method to `IOpsApiClient`
  and `OpsApiClient` (failures become `UpstreamUnavailableException`), a route constant to `OpsConsoleRoutes` and one
  `MapGet` in `OpsConsoleEndpoints.Map` (the group already requires the operator policy). Add HTTP tests against
  `FakeOpsApi`; it validates the assertion of every call.
- **Logic beyond pass-through** (merging, deciding): a use case in Application behind the ports, with unit tests.
- **Another Seq query:** add it to `SeqQueries`; only normalized values (never operator text) may enter a filter.
- **A second operator group or a read/admin split:** extend `OperatorGroupMembership` and the policy in
  `OperatorAuthentication`; keep one owner of the decision.

## Rules

- One operator credential: Windows sign-in plus the operator group (`OperatorAuthentication.OperatorPolicy`, the default
  and fallback policy). The dev operator exists only in Development. No JWT, API key or cookie (`module.rules.yaml`).
- One upstream credential: the ES256 service assertion via `AddCodelabServiceAssertion`; every `/api/ops` call goes
  through `IOpsApiClient` and names the operator.
- Read-only: GET endpoints only (forbidden pattern on `MapPost`/`MapPut`/`MapPatch`/`MapDelete`/`MapMethods`), so no
  anti-forgery token is needed. An operator action that changes state belongs to an audited API endpoint.
- Only a code normalized by `ReferenceCodeInput` reaches a Seq filter. The Seq key, assertions and log event text never
  reach a log line.
- No key material in the repository; tests generate keys at run time. Time from `TimeProvider`.
- Generated files here (`CLAUDE.md`, `.editorconfig`, `BannedSymbols.generated.txt`, `CodeMetricsConfig.txt`) come from
  `module.rules.yaml`; run `node tools/rules-engine/compile.mjs` after editing it.

## Gotchas

- **IIS:** `web.config` sets `forwardWindowsAuthToken`; the authentication sections (Windows on, anonymous off) are
  locked at server level, so the install kit sets them for the child application, never `web.config`. With anonymous
  off, IIS also challenges `/health/*`: health checks from the kit use the machine's Windows credentials.
- **Group changes** reach an operator at their next Windows logon (the groups are in the logon token).
- **Tests run on Kestrel, not the in-memory test server:** the Negotiate handler runs on every request and needs a real
  connection. `OpsConsoleHost` calls `UseKestrel(0)` (loopback, free port) and swaps Windows sign-in for a
  header-driven test scheme; `HostStartupTests` keeps the real Negotiate challenge.
- **`X-Codelab-Operator` is not inside the assertion's binding** (Codelab.Security binds method, path, query and body);
  it is trustworthy because only a holder of the console's key can make the API accept the request, and TLS protects
  it in transit.
- **The certificate view writes a `CertificateNeedsAttention` warning** (`CertificateStatus` as an integer) per
  certificate that is not Ok, which feeds the Seq signal "Ops: Certificate expiry warnings". See
  [docs/runbooks/idp-cert-renewal.md](../../docs/runbooks/idp-cert-renewal.md).
- **No PublicId ring:** Codelab.Security requires one at start, so `EphemeralPublicIdRing` supplies a process-only ring;
  a configured `Codelab:Security:PublicIds` section is refused.
- **Not carried from the source console:** its "temporary mode", the SQL operator-grant tables and audit log, the
  integration- and records-specific screens, MVC controllers, its own correlation and error middleware, and `curl -k`
  checks.
