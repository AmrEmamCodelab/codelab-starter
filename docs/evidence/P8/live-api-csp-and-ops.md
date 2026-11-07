# P8 live run: CSP report sink and the `/api/ops` credential matrix

Date: 2026-11-07, 02:21 local. Machine: the local test environment (Windows 10 Pro), **not elevated**.

## Setup

- The P8 working tree's `Starter.Api`, built with the private SDK into a scratch folder so the owner's running stack
  (API :5180, Attachment service :5185, web :4280) stayed untouched.
- Run on `http://localhost:5199`, `ASPNETCORE_ENVIRONMENT=Development`, content root `backend/src/Starter.Api`.
- A new database, `StarterP8Evidence` on `(localdb)\P2Proof`. Migrations applied at startup, including `OpsDbContext`.
- Seq at `http://localhost:5341` was the configured sink (`Observability:Seq:ServerUrl`).
- The API process was stopped after the run. The `StarterP8Evidence` database was left in place for the owner to inspect
  or drop.

## Requests and responses

| # | Request | Expected | Actual |
| --- | --- | --- | --- |
| 1 | `POST /api/security/csp-reports`, `application/csp-report` (report-uri format), `script-src-elem` violation | 204 + one `CspViolation` event | **204**, event logged |
| 2 | `POST /api/security/csp-reports`, `application/reports+json` (Reporting API format), `img-src` violation | 204 + one `CspViolation` event | **204**, event logged |
| 3 | `POST /api/security/csp-reports`, `text/plain` | 415 | **415** |
| 4 | `GET /api/ops/health`, no credential | 401 | **401** |
| 5 | `GET /api/ops/health`, legacy API-key header | 401 | **401** |
| 6 | `GET /api/ops/health`, `Authorization: Bearer <JWT>` | 401 | **401** |

Each request carried the marker `p8-evidence-1794010918` in its document URL, so the events can be found in Seq.

## API log (verbatim excerpt)

```text
[02:21:58 WRN] CSP violation of script-src-elem (Report): https://evil.example.test/x.js on https://portal.example.test/p8-evidence-1794010918
[02:21:58 INF] HTTP POST /api/security/csp-reports responded 204 in 54.3282 ms
[02:21:58 WRN] CSP violation of img-src (Report): https://evil.example.test/p.png on https://portal.example.test/p8-evidence-1794010918-r2
[02:21:58 INF] HTTP POST /api/security/csp-reports responded 204 in 15.6708 ms
[02:21:58 INF] HTTP POST /api/security/csp-reports responded 415 in 40.6679 ms
[02:21:58 INF] AuthenticationScheme: CodelabServiceAssertion was challenged.
[02:21:58 INF] HTTP GET /api/ops/health responded 401 in 13.2176 ms
[02:21:58 INF] AuthenticationScheme: CodelabServiceAssertion was challenged.
[02:21:58 INF] HTTP GET /api/ops/health responded 401 in 4.5612 ms
[02:21:58 INF] AuthenticationScheme: CodelabServiceAssertion was challenged.
[02:21:58 INF] HTTP GET /api/ops/health responded 401 in 51.0681 ms
```

## What this run does not prove

- **That the events reached Seq.** The local Seq requires a sign-in, and the run had no read key. The read API returned
  `{"Error":"Please log in."}`. To confirm, sign in to `http://localhost:5341` and filter on
  `@MessageTemplate like '%CSP violation%' and @Message like '%p8-evidence-1794010918%'`. Two events are expected.
  This is owner item O-5 in [README.md](README.md).
- **That a browser sends the report.** The requests above were hand-built. A browser-generated report needs the SPA to be
  served with the `Content-Security-Policy-Report-Only` header from `deploy/kits/templates/web/web.config`, which needs the
  IIS front site (O-3).
- **A valid assertion and a replayed assertion.** These need the Ops Console's signing key. They are covered in process by
  `OpsHttpTests` (see README.md).
