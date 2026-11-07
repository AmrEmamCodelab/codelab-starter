# Runbook: move the SPA's CSP from report-only to enforced

The portal ships its Content Security Policy as **`Content-Security-Policy-Report-Only`**. Browsers report every
violation and block nothing. This runbook reads those reports and fixes what they show. It then switches the policy to
**`Content-Security-Policy`**, which blocks, without breaking a screen.

| Piece | Where |
| --- | --- |
| The policy and the other security headers (single source) | [deploy/kits/templates/web/security-headers.psd1](../../deploy/kits/templates/web/security-headers.psd1) |
| IIS front site | [deploy/kits/templates/web/web.config](../../deploy/kits/templates/web/web.config), installed by [Install-Web.ps1](../../deploy/kits/Install-Web.ps1) |
| nginx front site | [deploy/nginx/security-headers.conf](../../deploy/nginx/security-headers.conf) |
| Parity test (fails when the three drift) | [deploy/kits/tests/SecurityHeaders.Parity.Tests.ps1](../../deploy/kits/tests/SecurityHeaders.Parity.Tests.ps1) |
| The report sink | `POST /api/security/csp-reports` on the API (anonymous, rate limited, 16 KiB cap, answers 204) |

## 1. Check that reports arrive

The policy sends each report twice: `report-uri` posts `application/csp-report` and `report-to` posts
`application/reports+json` through the `Reporting-Endpoints` group `csp-endpoint`. The sink accepts both. It writes
one **`CspViolation`** event per violation, at Warning level, in the logger category `Starter.Security.CspReports`.

1. Open the portal in a browser and check the response headers of the document. They should include
   `Content-Security-Policy-Report-Only` and `Reporting-Endpoints`. From a shell, run
   `curl -sI https://<portal host>/`. Never use `-k`: a certificate that does not validate is a finding of its own.
2. In the browser console, run `fetch('https://example.test/')`. The `connect-src` policy reports it.
3. In Seq, filter on `EventId.Name = 'CspViolation'`. The event should appear within a minute with
   `CspDirective = connect-src` and `CspBlockedUri` set to the test origin.

No event? Check, in this order:

- The front site does not route `/api/security/csp-reports` to the API. With the API on another origin, the CSP
  needs an absolute report target. Install-Web.ps1 writes one from `config.json`'s `apiBaseUrl` only when it creates
  the header.
- The API's request log shows 415 (wrong content type), 413 (body over 16 KiB) or 429 (rate limited).
- The API's Seq sink is not configured.

## 2. Read the reports in Seq

Each event carries these properties. They are a contract, so the saved queries keep working:

| Property | Meaning |
| --- | --- |
| `CspDirective` | The directive that was violated (`script-src-elem`, `style-src-attr`, `connect-src`, ...). |
| `CspDisposition` | `Report` while report-only, `Enforce` after the switch. |
| `CspBlockedUri` | What was blocked (an origin, `inline`, `eval`, `data`), redacted. |
| `CspDocumentUri` | The page, redacted (no query string). |
| `CspSourceFile`, `CspLineNumber`, `CspColumnNumber` | Where the violating code is, when the browser says. |
| `CspStatusCode` | The document's HTTP status. |
| `CspReportFormat` | Which channel delivered it: `ReportUri` (`report-uri`) or `ReportingApi` (`report-to`). |

Useful queries (Seq SQL):

```sql
-- What is being blocked, most frequent first (last 7 days)
select count(*) as reports from stream
where EventId.Name = 'CspViolation' and @Timestamp > Now() - 7d
group by CspDirective, CspBlockedUri
order by reports desc

-- Which pages, for one directive
select count(*) as reports from stream
where EventId.Name = 'CspViolation' and CspDirective like 'script-src%' and @Timestamp > Now() - 7d
group by CspDocumentUri
```

Save the first one as a signal ("CSP violations"), so the trend shows on the dashboard.

## 3. Triage every distinct (directive, blocked) pair

| Report | Usual cause | Fix |
| --- | --- | --- |
| `script-src-elem` / `inline` on every page | The inline loader Angular's critical-CSS inlining writes into `index.html` has changed, so its hash in the policy is stale. | Recompute the hash (below), update `InlineLoaderScriptHash` and the `script-src` value in all three files, and run the parity test. Or turn `inlineCritical` off in `angular.json`. |
| `script-src` / `eval` | A library calls `eval` or `new Function`. | Replace or upgrade the library. Never add `'unsafe-eval'`. |
| `connect-src` / an https origin | The SPA calls a host the policy does not list: the API or attachment service on another origin, or a new integration. | If the call is expected, add the origin. Put it in `config.json` and rerun Install-Web on a new server, or edit the server's header. For a source change, edit the `.psd1` and both files. |
| `img-src` / `font-src` / an external host | A logo or font loaded from a CDN. | Serve it from the app (the starter ships its fonts). |
| `frame-ancestors` | Someone frames the portal. | Nothing to fix. `X-Frame-Options: DENY` already blocks it. |
| `chrome-extension`, `moz-extension`, `safari-web-extension` | Browser extensions in users' browsers. | Ignore. Exclude them in the query with `and CspBlockedUri not like '%-extension%'`. |

To recompute the inline loader hash from a production build (Windows PowerShell):

```powershell
$html = [IO.File]::ReadAllText('frontend\starter-web\dist\starter-web\browser\index.html')
$script = [regex]::Match($html, '<script>(.*?)</script>', 'Singleline').Groups[1].Value
$sha = [Security.Cryptography.SHA256]::Create()
"'sha256-" + [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($script))) + "'"
```

## 4. Decide

Enforce when all of these hold on the tier you are switching. Do production last, after a lower tier ran enforced for
a week:

- The policy has been report-only there for at least **14 days** of normal use, every role included. Make sure the
  staff, admin and requester screens and the SSO round trip were all used.
- Every distinct pair from step 3 is fixed or explained.
- The last **7 days** show no unexplained violations.

## 5. Switch to enforced

In the repository, as one reviewed change (`deploy/` is a platform-owner path):

1. In `security-headers.psd1`, rename the header entry `Content-Security-Policy-Report-Only` to
   `Content-Security-Policy` and set `CspHeaderName` to the same. Keep the value.
2. Make the same rename in `deploy/kits/templates/web/web.config` and `deploy/nginx/security-headers.conf`.
3. Update the test "ships the CSP report-only until the enforce runbook runs" in `SecurityHeaders.Parity.Tests.ps1`
   to expect the enforced name. Then run:
   `powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/SecurityHeaders.Parity.Tests.ps1 -EnableExit"`.

On each IIS server:

4. Run `Install-Web.ps1` with the new kit. The kit only adds headers the server lacks, so the server now sends
   **both** headers: the enforced policy and the old report-only one. Reports keep arriving, now with
   `CspDisposition = Enforce` for anything blocked.
5. When the enforced policy has held for a few days, remove the old `Content-Security-Policy-Report-Only` line from
   the server's `web.config`. Alternatively, keep it with a stricter candidate policy to prepare the next tightening.

On nginx, deploy the new `security-headers.conf` and reload nginx (`nginx -t && nginx -s reload`).

Then repeat step 1. Check that the document carries `Content-Security-Policy`, that a page works for every role,
and that Seq shows no new `Enforce` events from the portal's own code.

## Roll back

On IIS, remove the `Content-Security-Policy` line from the server's `web.config`. IIS reads the change at the next
request and needs no recycle; the report-only header keeps reporting. On nginx, put back the previous
`security-headers.conf` and reload. Then revert the repository change, so the next kit run does not add the header
back.

## Gotchas

- **`frame-ancestors` is ignored in a report-only header.** Until enforcement, `X-Frame-Options: DENY` is what blocks
  framing. Keep both after the switch, because older clients read only `X-Frame-Options`.
- **The kit keeps server values.** A server whose CSP was edited by hand keeps the edit after a kit run. The kit run
  log lists every header that differs from the kit ("differs from the kit template; the server's is kept").
- **Same origin is the default.** `connect-src 'self'` and the relative report path assume the API sits under `/api`
  on the portal's host. Install-Web.ps1 adds the `config.json` origins and an absolute report target only to a header
  it creates.
- **The sink is rate limited.** A storm of reports from one client is cut at the rate limit (429). The counts in Seq
  are a lower bound, not exact.
