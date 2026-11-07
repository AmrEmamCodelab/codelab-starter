# P9 p8-leftovers: inbound trace-context stripping and the security-edge docs

Date: 2026-11-07. Machine: the local test environment (Windows 10 Pro, Windows PowerShell 5.1, Pester 3.4), not
elevated. No IIS or nginx was installed or changed. Nothing was committed.

Closes these items of the Phase 8 OPEN list: the IIS rule that strips inbound trace context (with its parity test),
and the `MODULE.md` docs for the CSP sink, the CORS boot check and the reference-code ids.

## What changed

| Path | Change |
| --- | --- |
| `deploy/kits/templates/web/security-headers.psd1` | Single source extended: `StrippedRequestHeaders` (`traceparent`, `tracestate`, `baggage`), `TraceStripServerVariablePrefix` (`HTTP_`), `TraceStripRuleName`, `TraceStripPaths` (`api`, `attachments`, `ops-api`). |
| `deploy/kits/templates/web/web.config` | One `<location>` per stripped path with the rule `Strip inbound trace context`: `match url=".*"`, `serverVariables` `HTTP_TRACEPARENT`, `HTTP_TRACESTATE`, `HTTP_BAGGAGE` set to `""`, action `None`. |
| `deploy/nginx/trace-context.conf` (new) | `proxy_set_header traceparent "";`, `tracestate`, `baggage`. |
| `deploy/nginx/starter-web.conf.example` | The `/api/` location includes `trace-context.conf` instead of three inline lines. |
| `deploy/kits/tests/SecurityHeaders.Parity.Tests.ps1` | New `Describe` with 9 tests: the source list, the IIS server-variable naming, the paths against the SPA fallback exclusion, one IIS `<location>` per path in order, each rule's exact variables / empty values / match / action / no `stopProcessing`, no site-level strip rule, nginx `trace-context.conf` exact list and nothing else, every proxying nginx location includes it, and a drift self-test (a missing `set`, a non-empty value and a missing nginx line each fail the comparison). |
| `deploy/kits/templates/web/README.md`, `deploy/kits/templates/api/README.md`, `deploy/nginx/README.md`, `deploy/kits/MODULE.md` | The rule, the allowed-server-variables prerequisite with the `appcmd` commands, the nginx inheritance gotcha, how to extend the list. |
| `backend/src/Starter.Infrastructure/MODULE.md` | "Security edge" section under Use: the CSP sink contract (route, formats, size, rate, normalizer, log event, answers) and the CORS boot check (what fails the start, the config keys); two Gotchas. |
| `backend/src/Starter.Api/MODULE.md` | Where the edge is wired (`AddCodelabSecurity`, `UseStarterPipeline`, `MapStarterEndpoints`), two Gotchas. |
| `packages/npm/ng-dga/MODULE.md` | The error dialog's reference-code DOM contract (`data-testid` values and their constants, `dir`/`data-fixed-direction`, the status region, the generated ids), the copy logic, and the i18n keys the starter-web host passes; one Gotcha. |

### Design choice: `<location>` elements, not a site-level rule

A site-level rule would also run for static files and would change the site's top-level rule list, which
`Install-Web.Tests.ps1` pins (`'SPA fallback'` after removing the edge block) and which `Install-Ops.ps1` scans.
A `<location path="api">` (and `attachments`, `ops-api`) applies the rule exactly to the backend child applications.
These are the paths the SPA fallback already excludes; the static Ops screens (`ops`) are left out. The site's
inherited edge block and SPA fallback still run first. The strip rule only sets variables (`None`), so it never
changes where a request goes.

## Test runs (verbatim summaries)

```text
> powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/SecurityHeaders.Parity.Tests.ps1 -EnableExit"
Describing Inbound trace context: stripped at the front site before a request reaches a backend
 [+] the source strips exactly the W3C trace context and baggage headers
 [+] names the IIS server variable of a header as URL Rewrite does (HTTP_, upper case, - as _)
 [+] strips under child application paths (one segment each) that the SPA fallback excludes
 [+] IIS: exactly one <location> per stripped path carries the rule, in source order
 [+] IIS: each rule sets exactly the stripped headers' server variables to empty, for every URL, and only sets variables
 [+] IIS: no site-level rule carries the trace strip rule (it belongs under the backend paths only)
 [+] nginx: trace-context.conf clears exactly the stripped headers, in order, and holds nothing else
 [+] nginx: every proxying location of the example includes trace-context.conf and clears no header by hand
 [+] detects drift: a template or conf missing one header no longer matches the source
Passed: 22 Failed: 0 Skipped: 0 Pending: 0 Inconclusive: 0

> powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Web.Tests.ps1 -EnableExit"
Passed: 41 Failed: 0 Skipped: 0 Pending: 0 Inconclusive: 0

> powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Ops.Tests.ps1 -EnableExit"
Passed: 28 Failed: 0 Skipped: 0 Pending: 0 Inconclusive: 0

> E:\codelab-tools\dotnet10\dotnet.exe run --project tools/docs-gate --artifacts-path %TEMP%\p9-p8-leftovers -- check --root .
docs gate: PASSED

> node tools/extract/scrub-sweep.mjs --root .
scrub-sweep: 4620 file(s) scanned, clean.

> node tools/extract/secret-scan.mjs --root .
secret-scan: 4618 file(s) scanned, clean.
```

`node tools/facts-drift/check.mjs --root .` reports 12 findings, all in `.claude/skills/new-screen/SKILL.md`. That
file belongs to a parallel task. None of the findings is in a path this task changed.

The first run of the new block failed once on a test-side regex (`[/\]` lost its escape in the heredoc). The test now
uses `IndexOfAny` on a named separator array; no template change was needed.

## Not proven here (needs a human with IIS)

- **IIS behaviour on a real server.** The rule needs the three names in the site's `allowedServerVariables`
  (`applicationHost.config`). Without them every request under the stripped paths answers 500.50. `Install-Web.ps1`
  does not register them yet; the integrator request below adds that step. Until then, run the `appcmd` commands in
  `deploy/kits/templates/web/README.md` before installing this template. Check on the WS2025 VM: a request to
  `/api/...` with `traceparent: 00-<32 hex>-<16 hex>-01` shows a trace id in Seq that differs from the sent one.
  Also check that IIS removes the header rather than forwarding it empty. Codelab.Observability ignores the inbound
  context either way, so the result is the same.
- **nginx** was not run; `trace-context.conf` is checked by the parity test only.
