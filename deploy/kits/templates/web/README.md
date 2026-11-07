# Web kit templates: the SPA front site

[`Install-Web.ps1`](../../Install-Web.ps1) installs the built SPA (`starter-web`) as the root of an IIS site. The site
folder holds three kit-owned files the payload copy never touches. The kit merges the first two and writes the third:

| File on the server | Source | Rule |
| --- | --- | --- |
| `config.json` | the package's `config.json` with the keys of [`config.template.json`](config.template.json) emptied | `Merge-AppSettings`: the server's values are kept, and only keys new in the build are added. |
| `web.config` | [`web.config`](web.config) in this folder, with the CSP adapted to `config.json` | An element the server already has (a header, a rule, a location, a mime map) is kept as it is. A difference from the kit is reported, and only elements new in the kit are added. |
| `build-info.json` | written by the kit | `{ buildId, buildIdSource, stampedAt }`, never cached (see `web.config`). |

## config.json: what an operator fills

On a first install the kit creates `config.json` from the package file, with the environment values empty. It then
stops (exit 2) without deploying the build. Fill the keys and run the kit again. A later build that adds a required
key stops the same way, and the running build keeps serving until the key is filled.

| Key | What to put there |
| --- | --- |
| `apiBaseUrl` | The API root the SPA calls: a same-origin path when the API is a child application of this site (the default topology), or the API's `https://` origin. |
| `origins.api` | The roots whose calls are signed and verified, as `https://` origins (the API's public origin). |
| `origins.attachment` | The attachment service roots uploads may go to, as `https://` origins. |
| `security.protocolPrefix` | Comes from the package (the fork's `init.ps1` sets it) and must equal the API's `Codelab:Security:Protocol:Prefix`. |

An `http://` value, or a dev value such as `localhost`, is never shipped: the kit empties these keys in the merge
source and reports any `http://` value on the server as a gap.

## web.config: what the kit ships

- **Edge block** (`Block internal and ops API`): 404 for `/api/internal/*` and `/api/ops/*` (any case, `//`, and an
  encoded slash). The rule is inherited by child applications, so it also covers an API installed under `/api`.
  The kit refuses a `web.config` where a forwarding rule sits above it.
- **SPA fallback**: deep links serve `index.html`. The paths of child applications (`/api`, `/attachments`, `/ops`, `/ops-api`)
  are excluded. A new child application path is a server edit to this rule, and the kit keeps that edit (it never
  rewrites an existing rule, so a site installed before `/ops-api` was added needs that edit by hand; `Install-Ops.ps1`
  refuses to install while the fallback would rewrite its paths).
- **Security headers**, from [`security-headers.psd1`](security-headers.psd1): CSP **report-only** with `report-uri`
  and `report-to` aimed at `/api/security/csp-reports`, `Reporting-Endpoints`, HSTS, COOP `same-origin`,
  `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, `X-Frame-Options`, and no `X-Powered-By` or
  `Server` header. `deploy/nginx/security-headers.conf` carries the same values (parity test).
- **Inbound trace context stripped** (`Strip inbound trace context`): under each backend child application
  (`/api`, `/attachments`, `/ops-api`: `TraceStripPaths` in `security-headers.psd1`) a `<location>` rule sets the
  server variables `HTTP_TRACEPARENT`, `HTTP_TRACESTATE` and `HTTP_BAGGAGE` to empty (action `None`). The caller's
  `traceparent`, `tracestate` and `baggage` request headers never reach the backend, so a client cannot choose the
  trace id the API logs under or plant baggage. `deploy/nginx/trace-context.conf` strips the same list (parity
  test). The rule needs the three names in the site's **allowed server variables** (next section).
- **Caching**: `index.html` (also when served for a deep link), `config.json` and `build-info.json` are `no-cache`.
  Hashed bundles (`<name>-<8 characters>.<ext>`) are `public, max-age=31536000, immutable`. Other static files are
  cached for a day.

When `config.json` names the API or attachment service on another `https://` origin, the kit adds those origins to
the CSP `connect-src`. It also points `report-uri` and `Reporting-Endpoints` at `<apiBaseUrl>/api/security/csp-reports`.
This applies only to a `web.config` the kit creates, or a CSP header the server does not have yet. An existing
server header is kept, so after a topology change you edit it on the server.

## Allowed server variables (trace-context stripping)

URL Rewrite sets a server variable only when it is listed in `system.webServer/rewrite/allowedServerVariables`.
That section is locked to `applicationHost.config` by default, so it cannot ship in this `web.config`. Until the
three names are allowed for the site, every request under `/api`, `/attachments` and `/ops-api` answers
**500.50** (URL Rewrite Module Error). Register them once per site, elevated, before (or right after) the first
install of a template that carries the rule:

```powershell
$site = '<site name>'
foreach ($name in 'HTTP_TRACEPARENT', 'HTTP_TRACESTATE', 'HTTP_BAGGAGE') {
    & "$env:windir\System32\inetsrv\appcmd.exe" set config $site -section:system.webServer/rewrite/allowedServerVariables "/+[name='$name']" /commit:apphost
}
```

`appcmd` answers "duplicate collection entry" for a name that is already allowed; that is harmless. The names come
from `StrippedRequestHeaders` in `security-headers.psd1` (`HTTP_` + the header upper-cased, `-` as `_`).

Moving the CSP from report-only to enforced: [docs/runbooks/csp-enforce.md](../../../../docs/runbooks/csp-enforce.md).
