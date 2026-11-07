# deploy/seq (Seq install and configuration as code)

## Purpose

Seq is the log server every deployable writes to through Codelab.Observability. This folder installs it and keeps
everything configured on it as code, so a new environment or a rebuilt server gets the same signals, saved query,
alerts, dashboard, retention and API keys from one command (decision A4).

| File | What it is |
| --- | --- |
| `Install-Seq.ps1` | Installs Seq from its MSI (offline), creates the service, binds the TLS certificate, starts it, sets a **neutral instance title** and waits for `GET /api` over validated TLS. |
| `apply-seq-config.ps1` | Applies `templates/` to one tier with `seqcli template import --merge`, and the retention policy and API keys through the Seq HTTP API. |
| `SeqCommon.psm1` | What both scripts share: reading and checking the JSON files, the https rule, the neutral-title rule, finding `seqcli`. |
| `seq-config.json` | Title prefix, instance title format, minimum level, the applications that get ingest keys, the gateway application, retention, the read keys. |
| `thresholds.<tier>.json` | Per tier (`development`, `test`, `staging`, `preproduction`, `production`): alert thresholds, whether the certificate alert is on, the DMZ gateway node names. |
| `templates/*.template` | The Seq entities (seqcli template format, kept as valid JSON until rendered). |
| `tests/Seq.Tests.ps1` | Pester 3.4 tests: JSON validity, rendering for every tier, neutral titles, the required signals, the key plan, the install plan. |

The signals (shared by every tier):

| Signal | Filter | Source |
| --- | --- | --- |
| `Ops: Errors with a reference code` | `Has(ReferenceCode) and @Level in ['Error', 'Fatal']` | Codelab.Observability `ReferenceCode`; the code the error dialog shows |
| `Ops: Server errors (5xx)` | `StatusCode >= 500` | request logging |
| `Ops: CSP violations` | `EventId.Name = 'CspViolation'` | the CSP report sink `POST /api/security/csp-reports` |
| `Ops: Gateway relay failures` | `Code in [GATEWAY_UPSTREAM_UNAVAILABLE, GATEWAY_UPSTREAM_REJECTED, GATEWAY_PROFILE_DISABLED]` | gateway problem+json codes |
| `Ops: Background job failures` | `SourceContext = 'Codelab.Jobs' and @MessageTemplate like 'Job failed%'` | `JobFailureLogFilter` |
| `Ops: Certificate expiry warnings` | `CertificateStatus in [2, 3, 4]` (Warning, Critical, Expired) | the Ops certificate checks |

Per tier: one alert for each signal except the reference-code one (`Ops: [<tier>] ...`, thresholds from the tier's
file) and the dashboard `Ops: [<tier>] Overview`. Shared: the saved query `Ops: Find by reference code`.

## Use

```powershell
# Install (elevated, on the Seq server). -WhatIf previews; nothing is changed.
.\Install-Seq.ps1 -Environment production -MsiPath <seq msi copied to the server> -ListenUrl https://<seq host> -CertificateThumbprint <thumbprint> -StoragePath D:\Seq -WhatIf

# Configure one tier (from any operator box with seqcli). The admin key only through the environment.
$env:SEQ_ADMIN_KEY = '<admin key>'
.\apply-seq-config.ps1 -Environment production -ServerUrl https://<seq host> -WhatIf
.\apply-seq-config.ps1 -Environment production -ServerUrl https://<seq host> -NotificationAppInstanceId appinstance-<n>
# The Ops Console's read key (token printed once; it goes into Codelab:OpsConsole:Seq:ApiKey)
.\apply-seq-config.ps1 -Environment production -ServerUrl https://<seq host> -IncludeReadKeys
# Revoke one ingest key (for example a compromised DMZ node); a plain run afterwards creates a fresh one
.\apply-seq-config.ps1 -Environment production -ServerUrl https://<seq host> -Revoke <application or gateway-node name>

# Tests (no Seq, no network)
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/seq/tests/Seq.Tests.ps1 -EnableExit"
```

The operator steps (the first admin password hash, admin key, ingest tokens into each app's
`Observability:Seq:ApiKey`) are in the [Seq runbook](../../docs/runbooks/seq.md).

## Extend

- **A new signal:** add `templates/signal-<name>.template` with a title that starts with `titlePrefix` and names no
  application, filter on the `LogPropertyNames` contract (Codelab.Observability), add an alert
  (`alert-<name>.template`, `SignalId` as `"{{ref:signal-<name>.template}}"`) and a dashboard chart if operators need
  it, add the signal to the table above and to the runbook, and extend `tests/Seq.Tests.ps1`.
- **A new threshold:** add the key to every `thresholds.<tier>.json`, add it to `$script:ThresholdCounts` in
  `SeqCommon.psm1` (it must be a positive integer) and to `Get-SeqTokens` in `apply-seq-config.ps1`, then use
  `{{<name>}}` in the template.
- **A new application:** add its service name to `applications` in `seq-config.json`; the next run creates its ingest
  key for each tier.
- **A new gateway node:** add its name (lowercase letters, digits, hyphens) to `gatewayNodes` in the tier's
  thresholds file; the next run prints its key once.

## Rules

- Windows PowerShell 5.1 compatible, `Set-StrictMode -Version Latest`, `$ErrorActionPreference = 'Stop'`. Every
  write is behind `ShouldProcess`; `-WhatIf` contacts no server and changes no service.
- **Validated TLS only.** `-ServerUrl` and `-ListenUrl` are https (plain http only for `development` on localhost),
  and no script skips certificate validation.
- **No secrets in files.** The admin key comes only from `SEQ_ADMIN_KEY` and reaches seqcli through the process
  environment. New tokens are printed once and never written. No host, address or key in this folder.
- **Neutral titles.** Entity titles start with `titlePrefix` (`Ops:`) and the instance title follows
  `instanceTitleFormat`; neither may contain an application name from `seq-config.json` or its first segment (the
  product name). The scripts refuse otherwise, and the tests check it.
- Templates stay valid JSON: `"{{ref:<file>}}"` becomes seqcli's `ref("<file>")` and `"{{json:<name>}}"` raw JSON only
  when rendered. Enums are integers with explicit values (`SeqInstallAction`).

## Gotchas

- **The server UI loses to the code.** `template import --merge` matches by title and overwrites edits made in the
  Seq UI. Change the template instead.
- **A drifted key fails the run.** A key whose applied properties, level or permissions differ is reported, not
  fixed: its token is in use. Revoke it (`-Revoke`) and run again to get a fresh token.
- **The gateway has no shared key.** Its ingest keys are per DMZ node (`gatewayNodes`), so a compromised node's key is
  revoked alone. A tier without nodes has no gateway key.
- **The CSP and certificate signals depend on event shapes** owned by other modules: the CSP sink must log with
  event name `CspViolation`, and the certificate checks must log a `CertificateStatus` integer. If either changes,
  change the filter here in the same change.
- **seqcli setting names.** The instance title is set with `seqcli setting set -n instance-title`.
- **Authentication is on from the first start** outside development. `Install-Seq.ps1` refuses to create a new
  instance without `SEQ_FIRSTRUN_ADMINPASSWORDHASH` (the hash of the first admin password, from `seq.exe config hash`)
  and writes it with `seq.exe config -k firstRun.adminPasswordHash`; the password itself never reaches the script.
  Every run outside development then fails when an anonymous `GET /api/events` answers 200 (authentication off).
  The setting key follows the Seq version in use: check it against the Seq MSI's release notes when the MSI changes.
