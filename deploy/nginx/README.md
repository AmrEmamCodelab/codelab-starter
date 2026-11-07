# deploy/nginx

The SPA front site's security edge for a project that fronts the portal with nginx instead of IIS. The IIS
equivalent is [../kits/templates/web/web.config](../kits/templates/web/web.config), installed by
[../kits/Install-Web.ps1](../kits/Install-Web.ps1).

| File | What it is |
| --- | --- |
| `security-headers.conf` | The response headers: CSP **report-only** (reports to `/api/security/csp-reports`), `Reporting-Endpoints`, HSTS, COOP, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, `X-Frame-Options`. A real file: include it as is. |
| `edge-blocks.conf` | 404 for `/api/internal/*` and `/api/ops/*` at the front site (also with `//` and an encoded slash). |
| `trace-context.conf` | Clears the caller's `traceparent`, `tracestate` and `baggage` (`proxy_set_header <name> "";`) so a client never chooses the trace a backend logs under. Include it in **every** location with `proxy_pass`. |
| `starter-web.conf.example` | An example server block: TLS, the includes, `/api` under the same origin (with `trace-context.conf`), hashed bundles cached for a year, `index.html` / `config.json` / `build-info.json` never cached, deep links to `index.html`. Every `<...>` is filled on the nginx host. |

## One source, two servers

The header values, the blocked paths and the stripped inbound trace headers (`StrippedRequestHeaders`) are defined once, in
[../kits/templates/web/security-headers.psd1](../kits/templates/web/security-headers.psd1). This folder and the IIS
template carry them literally (neither server can read the `.psd1`), and
[../kits/tests/SecurityHeaders.Parity.Tests.ps1](../kits/tests/SecurityHeaders.Parity.Tests.ps1) fails when either
drifts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/SecurityHeaders.Parity.Tests.ps1 -EnableExit"
```

To change a header: edit the `.psd1`, then `security-headers.conf` and the `web.config` template, then run the test.
Moving the CSP from report-only to enforced is a procedure, not an edit: [csp-enforce.md](../../docs/runbooks/csp-enforce.md).

## Gotchas

- **`add_header` is not inherited into a location that has its own `add_header`.** Every such location includes
  `security-headers.conf` again (the example does). A location without `add_header` inherits the server's set.
- **`proxy_set_header` is not inherited either.** A proxying location sets `Host` and `X-Forwarded-*`, so it never
  inherits a server-level `proxy_set_header`: include `trace-context.conf` inside each proxying location (the parity
  test checks the example). Add a new backend location (for example `/attachments/`) the same way.
- **Keep `always`.** Without it nginx sends the headers only on 2xx/3xx, so a 404 or 500 page carries no CSP or HSTS.
- **Same origin.** The CSP's `connect-src 'self'` and the relative report path assume the API is proxied under
  `/api` on the same host. With the API on another origin, add that origin to `connect-src` and make both report
  targets absolute (`https://<api host>/api/security/csp-reports`); Install-Web.ps1 does the same for IIS from
  `config.json`.
- **The Ops Console API and the gateway never call through this host**: `/api/ops` and `/api/internal` answer 404
  here by design. Point them at the API's own binding.
- No hosts, certificates or paths of a real environment in this folder (scrub sweep); they live on the nginx host.
