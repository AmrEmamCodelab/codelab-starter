# Runbook: Seq (logs, signals, alerts)

One shared Seq server receives the logs of every deployable (API, attachment service, integration gateway, Ops
Console) for every tier. Events carry `Application` and `DeploymentEnvironment`, set by the ingest key each app uses.
The scripts and the configuration are in [deploy/seq](../../deploy/seq/MODULE.md).

## Install a Seq server

1. Copy the Seq MSI and the server certificate (in `LocalMachine\My`, with its private key, a name the operators and
   the apps use, and a chain every app server trusts) to the Seq server.
2. Choose the first admin password, keep it in the operators' vault, and turn it into a hash once the MSI is
   installed (`seq.exe config hash` reads the password from standard input and prints the hash). Set the hash for this
   session only; it is never a script parameter, and the script never prints it:

   ```powershell
   $env:SEQ_FIRSTRUN_ADMINPASSWORDHASH = '<hash>'
   ```

3. Preview, then install (elevated):

   ```powershell
   .\deploy\seq\Install-Seq.ps1 -Environment production -MsiPath <msi> -ListenUrl https://<seq host> -CertificateThumbprint <thumbprint> -StoragePath D:\Seq -WhatIf
   .\deploy\seq\Install-Seq.ps1 -Environment production -MsiPath <msi> -ListenUrl https://<seq host> -CertificateThumbprint <thumbprint> -StoragePath D:\Seq
   ```

   The script refuses plain http (except `development` on localhost), a title that names an application, a relative
   storage path, and a new instance outside development without `SEQ_FIRSTRUN_ADMINPASSWORDHASH`, so Seq never
   starts with authentication off. It waits for `GET https://<seq host>/api` to answer 200 over validated TLS, then
   fails if an anonymous `GET https://<seq host>/api/events` is answered 200 (authentication off: sign in as the admin,
   turn authentication on under Settings, and run the script again). Remove the variable afterwards
   (`Remove-Item Env:SEQ_FIRSTRUN_ADMINPASSWORDHASH`).
4. Sign in at `https://<seq host>/` as the admin with the password from the vault.
5. Create an admin API key in the Seq UI (Settings > API keys, permissions Setup + Write), keep it in the
   operators' vault, and set it for the session only:

   ```powershell
   $env:SEQ_ADMIN_KEY = '<admin key>'
   .\deploy\seq\Install-Seq.ps1 -Environment production -ListenUrl https://<seq host> -CertificateThumbprint <thumbprint> -StoragePath D:\Seq
   ```

   The second run sets the neutral instance title (`Application logs (<tier>)`).

## Enable Seq for a tier

1. Name the DMZ gateway nodes of the tier in `deploy/seq/thresholds.<tier>.json` (`gatewayNodes`), and review the
   thresholds.
2. Preview, then apply (from any operator box with `seqcli`):

   ```powershell
   $env:SEQ_ADMIN_KEY = '<admin key>'
   .\deploy\seq\apply-seq-config.ps1 -Environment <tier> -ServerUrl https://<seq host> -WhatIf
   .\deploy\seq\apply-seq-config.ps1 -Environment <tier> -ServerUrl https://<seq host> -NotificationAppInstanceId appinstance-<n>
   ```

   Every new ingest key's token is **printed once**. Put each one in its app's `Observability:Seq:ApiKey`, with
   `Observability:Seq:ServerUrl` = `https://<seq host>`, then recycle the app pool. A token is never written to a
   file by the script; do not paste it into tickets or chat.
3. For the Ops Console, create the read key once and put its token in the console's `Codelab:OpsConsole:Seq:ApiKey`
   (`deploy/kits/templates/ops/README.md`):

   ```powershell
   .\deploy\seq\apply-seq-config.ps1 -Environment <tier> -ServerUrl https://<seq host> -IncludeReadKeys
   ```

4. Alerts notify only through an installed e-mail app instance (`-NotificationAppInstanceId`). Without one they still
   evaluate and show their state in Seq.

Run the script again after any change in `deploy/seq`. It converges: it updates entities by title and never
duplicates them. Edits made in the Seq UI are overwritten; change the templates instead.

## Find a request by its reference code

A user quotes the reference code from the error dialog (`data-testid="error-reference-code"`).

- In the Ops Console: Reference codes > paste the code. It shows the request, its events and a link to Seq.
- In Seq: open the saved query **Ops: Find by reference code**, replace `XXXXX-XXXXX` and run it, or open the
  signal **Ops: Errors with a reference code** and search `ReferenceCode = '<code>'`. Follow `CorrelationId` to see
  every event of the same request, including the gateway's.

## Signals and alerts

Each signal has an alert per tier (titles `Ops: [<tier>] ...`; thresholds in `thresholds.<tier>.json`) and a chart on
the dashboard **Ops: [<tier>] Overview**.

### Server errors

`StatusCode >= 500`, grouped by application. Open the signal, group by `ErrorFingerprint` to see whether it is one
failure or many, and read one event's `ReferenceCode`, `Code` and `ExceptionType`. A burst right after a deployment
points at a refused setting: read the app's startup events.

### Gateway relay failures

`Code` is one of:

| Code | Meaning | First check |
| --- | --- | --- |
| `GATEWAY_UPSTREAM_UNAVAILABLE` (503) | The provider (OIDC or SMS) did not answer the gateway. | The DMZ firewall and the provider's status; the gateway's own events with the same `CorrelationId`. |
| `GATEWAY_UPSTREAM_REJECTED` (502) | The provider answered with an error. | The provider credentials and URLs in the gateway settings. |
| `GATEWAY_PROFILE_DISABLED` (404) | The API called a relay profile the gateway has disabled. | `Codelab:Integration:Gateway:Profiles` on the gateway (for example `sms` `Enabled`). |

A 401 from the gateway is not a relay failure: it means the API's service assertion was refused (wrong key or
clock). See the gateway runbook.

### Background job failures

One line per failed job (`Job`, `JobId`, `Code`, `Retry`). The scheduler retries; act when the same job keeps failing.
Open the job in the admin Jobs screen or the Ops Console and read the events with its `JobId`.

### CSP violations

Reports the browser sent to `POST /api/security/csp-reports` (event `CspViolation`). The policy is report-only, so
nothing was blocked. A steady stream after a release usually means a new script, style or font origin; fix the page or
extend the policy before enforcing it (see the CSP enforcement runbook).

### Certificate expiry warnings

The Ops certificate checks log `CertificateStatus` 2 (Warning), 3 (Critical) or 4 (Expired), with the warning and
critical windows from `Codelab:Ops:CertificateWarningDays` and `CertificateCriticalDays`. Renew the certificate named in
the event (TLS bindings, the IdP signing certificate). The alert is off in `development` and `test`.

## Rotate or revoke an ingest key

```powershell
.\deploy\seq\apply-seq-config.ps1 -Environment <tier> -ServerUrl https://<seq host> -Revoke <application or gateway-node name>
.\deploy\seq\apply-seq-config.ps1 -Environment <tier> -ServerUrl https://<seq host>
```

The first command deletes the key (its token stops working at once). The second prints a fresh token for the same
name; put it in the app's settings and recycle the pool. To retire a gateway node, remove it from `gatewayNodes` first.

## Remove a tier

```powershell
.\deploy\seq\apply-seq-config.ps1 -Environment <tier> -ServerUrl https://<seq host> -Remove
```

It deletes the tier's alerts and dashboard. Once no tier is left, it also deletes the shared signals and saved
query. It never deletes the retention policy or an API key.

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `apply-seq-config.ps1` fails with "must be https" | Use the https URL the certificate covers. Plain http is accepted only for `development` on localhost. |
| "No admin key" | Set `$env:SEQ_ADMIN_KEY` in the same session. The script takes no key parameter. |
| A key is reported as drifted | Someone changed it in the UI. Revoke it and run again to get a fresh token. |
| An app logs nothing | Check its `Observability:Seq:ServerUrl` and `ApiKey`, and that the app server trusts the Seq certificate chain. The app keeps writing its local log file while Seq is unreachable. |
| `Install-Seq.ps1` health never answers 200 | Check the certificate binding (`netsh http show sslcert`), the service status, and the Seq log under the storage folder. |
