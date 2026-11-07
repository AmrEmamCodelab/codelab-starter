# Phase 8 evidence: exit criteria status

Every Phase 8 exit criterion, what proves it, and what is still open. **Status** has four values:
**Proven live** (run against a real process on this machine, with evidence here), **Proven in process** (automated tests
only), **Partial**, and **Owner item** (needs an elevated shell, a signed-in Seq or a clean VM, none of which an agent
has here).

Environment for the live runs: the local test environment from the phase log (Windows 10 Pro, local IIS, local SQL,
Seq at :5341). Agent shells on this machine are **not elevated**: `WindowsBuiltInRole.Administrator` is `False`. Without
elevation an agent cannot add a Windows Firewall rule, create or change an IIS site, or run the kit installers. Seq
requires a sign-in, so an agent can write to it but cannot read it back.

## Status matrix

| # | Exit criterion | Status | Evidence | Open item |
| --- | --- | --- | --- | --- |
| a | The API host has an outbound deny-all firewall rule; **SSO login completes through the gateway relay** | Partial: proven in process only | `GatewaySsoBackChannelTests` (the token form is relayed and the provider JSON returned, JWKS through the relay, refusal and unavailability mapping); `OidcBackChannelTests` | O-1 |
| b | **SMS delivered through the gateway SMS relay** to a provider stub; a direct SMS call from the API host fails | Partial: relay proven in process, no-egress not proven | `GatewaySmsRelayIntegrationTests` (loopback Kestrel gateway stub; relay with assertion, refusal without assertion, untrusted API left pending). It contains **no** firewall or egress assertion. | O-1 |
| c1 | `/api/ops`: a valid ES256 assertion is accepted | Proven in process | `OpsHttpTests.A_valid_ES256_assertion_from_the_Ops_Console_is_accepted_and_the_operator_is_recorded` | none |
| c2 | `/api/ops`: JWT, legacy API-key header, replayed assertion and no credential each return 401 | **Proven live** (JWT, legacy API-key header, none); in process (replay) | [live-api-csp-and-ops.md](live-api-csp-and-ops.md) rows 4-6; `OpsHttpTests` (bearer, api key, replay, no credential, other path) | none |
| c3 | `/api/ops` and `/api/internal` return **404 through the front site** | Partial: static only | `SecurityHeaders.Parity.Tests.ps1` checks the block patterns in the web.config and nginx text. Nothing has been requested through IIS. | O-3 |
| d | Ops Console flow (Playwright): an error in the web app, copy the reference code, find the Seq trace and the request in Ops Console, see job status and cert expiry | Partial: API pieces in process; UI flow not run | `OpsHttpTests.A_reference_code_from_an_error_response_leads_back_to_its_request`, `Jobs_and_certificates_answer_for_the_console`; [ui/](ui/) has the P8 starter-web screens (`07-wizard-submit-refcode-alert-ar.png` shows a reference code in the error alert) but **no Ops Console screen** | O-4 |
| e1 | CSP report-only header present on the SPA | Partial: static only | Parity test `ships the CSP report-only until the enforce runbook runs` and `sends CSP reports to the sink through report-uri and report-to` | O-3 |
| e2 | A CSP violation report lands in the sink **and in Seq** | Partial: sink **proven live**, Seq not read back | [live-api-csp-and-ops.md](live-api-csp-and-ops.md) rows 1-3 (both wire formats 204, wrong type 415, `CspViolation` event logged); `CspReportSinkHttpTests` | O-5 |
| f | Header parity: CSP/HSTS/COOP in `deploy/nginx/security-headers.conf` equal the IIS web.config template | Proven in process | `deploy/kits/tests/SecurityHeaders.Parity.Tests.ps1` | none |
| g | Full kit install (`Install-Api/Web/Attachment/Gateway/Ops`) on a clean Windows Server 2025 VM with IIS + SQL; health green; basic login | **Owner item**: no VM exists | Kit Pester tests (`deploy/kits/tests/Install-*.Tests.ps1`) cover the scripts, not an install | O-6 |
| h | Codelab.Testing security sweep green for all deployables | Not re-verified in this evidence pass | none | Final gate |

## Phase close gate

**Phase 8 is not done.** Rows a, b, c3, d, e1, e2, g and h are not proven. The phase can close only when one of these
is true for each of those rows:

1. The owner item for the row (O-1 to O-6, or the final gate for h) has run, and its transcript or screenshots are in
   this folder. The row's status then changes to **Proven live**.
2. The phase log records an explicit owner decision that defers the row, with a named follow-up.

An agent cannot meet either condition. Agent shells here are not elevated and cannot sign in to Seq, and only the owner
can record a deferral. Agents must not mark these rows proven, and must not mark the phase done, from evidence in
process or static evidence.

## Owner items (need an elevated shell, a Seq sign-in or a VM)

Run these from an **elevated** PowerShell at the repo root, then save the output (a transcript or screenshots) in this folder.

- **O-1, no-egress proof for SSO and SMS (criteria a, b).** Install the API through the kit as an IIS app
  (or run it as one process), then add a program-scoped outbound block for its process. Allow only SQL, the SMTP relay
  and the gateway host:
  `New-NetFirewallRule -DisplayName 'P8 API no egress' -Direction Outbound -Action Block -Program '<path of the API host exe or w3wp>'`,
  with explicit `-Action Allow` rules (remote address and port) for SQL, SMTP and the gateway. Then:
  1. Sign in with SSO through the gateway relay (DevIdp behind the gateway). Expect success.
  2. Trigger an SMS notification through the gateway SMS relay to a provider stub. Expect delivery.
  3. Switch `Notifications:Sms` to a direct provider sender, or call the provider URL from the API host. Expect a
     connection failure. Save the firewall rule listing (`Get-NetFirewallRule -DisplayName 'P8 API no egress' | Get-NetFirewallApplicationFilter`)
     with the results.
  Remove only the rules you added afterwards.
- **O-3, front-site checks (criteria c3, e1).** Install the web kit under a local IIS site. Then capture
  `curl -i https://<site>/api/ops/health`, `curl -i https://<site>/api/internal/x` (both 404) and
  `curl -I https://<site>/` (the `Content-Security-Policy-Report-Only`, HSTS and COOP headers present).
- **O-4, Ops Console Playwright flow (criterion d).** Start the gateway and the Ops Console (`run-local.ps1` does not start
  them), sign in as an operator, trigger an error in starter-web, copy the reference code, look it up in Ops Console,
  open the Seq trace, then screenshot the Jobs and Certificates screens. Save the screenshots as `ops/NN-*.png`.
- **O-5, Seq read-back (criterion e2).** Sign in to Seq at `http://localhost:5341` and filter on the marker
  `p8-evidence-1794010918` (see [live-api-csp-and-ops.md](live-api-csp-and-ops.md)). Screenshot the two `CspViolation`
  events. Alternatively, give the evidence run a Seq read key through `$env:SEQ_READ_KEY`. An agent can then query
  `/api/events` itself.
- **O-6, clean-VM kit install (criterion g).** No Windows Server 2025 test VM exists yet. This criterion stays open until
  one exists: run `build.ps1 -Kit`, then `Install-Api/Web/Attachment/Gateway/Ops` per `deploy/kits/README.md`, check
  health and basic login, and save the transcript here.

## Folder contents

- [live-api-csp-and-ops.md](live-api-csp-and-ops.md): the live non-elevated run (CSP sink, `/api/ops` credential matrix).
- [ui/](ui/): starter-web Playwright screenshots from the P8 UI review.
