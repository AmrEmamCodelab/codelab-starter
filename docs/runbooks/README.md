# Runbooks

Step-by-step procedures for operating a deployed environment.

| Runbook | Use it to |
| --- | --- |
| [csp-enforce.md](csp-enforce.md) | Move the SPA CSP from report-only to enforced; read the CSP sink (`CspViolation`) in Seq. |
| [gateway.md](gateway.md) | Install the DMZ integration gateway: trusted callers and S2S keys, upstream TLS trust, profiles (sso, sms), health over validated TLS, troubleshooting by failure code. |
| [idp-cert-renewal.md](idp-cert-renewal.md) | Renew the identity provider's signing certificate: the Ops Console expiry alert, the Seq signal, renewal with overlap, verification and the expired case. |
| [key-rotation.md](key-rotation.md) | Rotate signing, PublicId, S2S, ticket, upload-ticket and receipt keys (plus the JWT signing key and the UserIdHash key): generate, add as next key, switch active, retire. |
| [seq.md](seq.md) | Seq: install with a neutral title, apply signals/alerts/dashboard per tier, find a request by reference code, rotate or revoke ingest keys, remove a tier. |
| [upstream-merge.md](upstream-merge.md) | Take starter changes into a fork: `tools/upstream-merge.ps1`, conflict policy, package bumps (v1). |
