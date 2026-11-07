# Runbook: the integration gateway

The integration gateway (`services/integration-gateway`, `Codelab.Gateway.Api`) is the backend's only way out. The
API host has an outbound deny-all firewall rule except to SQL Server, the internal SMTP relay and the gateway; the
gateway sits in the DMZ and relays, per enabled **profile**, to the OIDC provider (`sso`) and the SMS provider (`sms`).
Contract and configuration reference: [services/integration-gateway/MODULE.md](../../services/integration-gateway/MODULE.md).

```
browser ── front site ── API (no egress) ──ES256 assertion──▶ gateway (DMZ) ──validated TLS──▶ OIDC provider / SMS provider
```

## 1. Before the install

| Need | Where it comes from |
| --- | --- |
| A DMZ Windows Server with IIS and the ASP.NET Core Hosting Bundle (.NET 10) | Infrastructure. |
| A TLS certificate for the gateway's host name, trusted by the API host | Internal CA. The API validates it through `Codelab:Integration:Gateway:Certificate` (system trust, pinned authority or pinned leaf). |
| Firewall: API host → gateway 443; gateway → the provider hosts 443 | Network team. Nothing else inbound to the gateway. |
| The API's S2S identity: `ServiceName`, `KeyId` and the **public** key PEM | The API's `Codelab:Security:ServiceAssertions:Client` (the private half stays on the API). |
| The OIDC provider's token endpoint and JWKS URL | The identity team (same values the API uses in direct mode). |
| The SMS provider's API description and credential | The client's SMS provider contract. |
| How the gateway trusts each provider's certificate | System store, or the provider's issuing CA (PEM bundle), or the leaf SHA-256 thumbprint. |

## 2. Install

1. Publish: `E:\codelab-tools\dotnet10\dotnet.exe publish services\integration-gateway\src\Codelab.Gateway.Api -c Release -o <out>`
   (the deploy kit, `publish/deploy-<sha>/gateway`, holds the same output; `appsettings.*.local.json` is never published).
2. Create a dedicated IIS site and app pool (No Managed Code, its own identity) on the gateway host name with the TLS
   binding. Copy the output; keep the shipped `web.config` (`maxAllowedContentLength` 16384, WebDAV removed).
3. Settings that change per server go in the app pool environment (or the secret store), never in the committed
   `appsettings.json`. Environment names use `__` for `:`.

   ```text
   ASPNETCORE_ENVIRONMENT=Production
   Codelab__Integration__Gateway__Profiles__0=sso
   Codelab__Integration__Gateway__Profiles__1=sms
   Codelab__Security__ServiceAssertions__Server__TrustedServices__0__ServiceName=<api service name>
   Codelab__Security__ServiceAssertions__Server__TrustedServices__0__KeyId=<api key id>
   Codelab__Security__ServiceAssertions__Server__TrustedServices__0__PublicKeyPem=<api public key PEM>
   Gateway__Sso__TokenEndpoint=https://<idp>/<token path>
   Gateway__Sso__JwksUri=https://<idp>/<jwks path>
   Gateway__Sms__UrlTemplate=https://<sms provider>/<path>?sender={sender}
   Gateway__Sms__BodyTemplate={"to":"{to}","text":"{text}","lang":"{language}"}
   Gateway__Sms__AuthHeaderName=<header the provider expects>
   Gateway__Sms__AuthHeaderValue=<provider credential>        (secret)
   Gateway__Sms__Sender=<registered sender id>
   ```

   `deploy/kits/Install-Gateway.ps1` (on `Install-Common.psm1`) automates steps 2 to 5: it keeps the server's settings,
   patches only new keys, reports the required gaps above and refuses to start while any is missing.
4. Upstream trust, per provider (`Gateway:Sso:Certificate` and `Gateway:Sms:Certificate`):
   - `Mode` 1: the Windows trust store (default). Install the provider's CA in Local Machine if it is not public.
   - `Mode` 2 + `AuthorityBundlePath`: the chain must end in the CA(s) in that PEM file.
   - `Mode` 3 + `PinnedThumbprints:0`: the leaf's SHA-256 thumbprint (rotate before the provider renews its certificate).
   There is no "accept any certificate" setting.
5. On the API: `Codelab:Integration:Gateway:BaseUrl=https://<gateway host>/`, `Codelab:Integration:Gateway:Audience`
   left at `integration-gateway` (must equal the gateway's `ServiceAssertions:Server:Audience`), the gateway's TLS trust
   in `Codelab:Integration:Gateway:Certificate`, then switch the SSO back channel to the gateway relay and the SMS
   sender to the gateway mode.
6. Check health over validated TLS (never `-k` or `-SkipCertificateCheck`):

   ```powershell
   Invoke-WebRequest https://<gateway host>/health/live  -UseBasicParsing   # 200 Healthy
   Invoke-WebRequest https://<gateway host>/health/ready -UseBasicParsing   # 200 Healthy
   ```

   Readiness is green when at least one profile is enabled, every enabled upstream address is configured and its host
   name resolves. It never calls a provider.
7. Prove the topology from the API host: an SSO login completes; an SMS notification arrives (its `referenceCode` is in
   the gateway log); a direct call from the API host to the provider fails (no egress).

## 3. Keys and trusted callers

- The gateway holds **public** keys only for its callers. Each caller is one `TrustedServices` entry
  (`ServiceName`, `KeyId`, `PublicKeyPem`); an assertion is accepted only when its `kid`, `iss`, `aud`, lifetime
  (`MaxLifetimeSeconds`, default 60), clock skew (`ClockSkewSeconds`, default 5), method, path and body hash all match,
  and its `jti` was not seen before.
- **Rotating the API's S2S key:** add the new key as a second `TrustedServices` entry (new `KeyId`), recycle the
  gateway, switch the API to the new private key, confirm relays succeed, then remove the old entry and recycle again.
  The full procedure for every key is in the key-rotation runbook.
- **SMS credential rotation:** set the new `Gateway__Sms__AuthHeaderValue`, recycle the app pool, send a test SMS.
- The gateway refuses a configured `Codelab:Security:PublicIds` ring: never copy the API's settings file to the DMZ.

## 4. Troubleshooting

Every refusal carries a `referenceCode`; search the gateway's Seq (or log file) for it. Log events: 5440 relay
completed, 5441 relay failed (code, failure kind, upstream status), 5442 malformed request refused, 5443 JWKS
refreshed. Relayed bodies, tokens, phone numbers and texts are never logged.

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| The gateway does not start: `TrustedServices entry are required` | No trusted caller configured. | Add the API's `TrustedServices` entry (section 2, step 3). |
| The gateway does not start: `Gateway:Sso:...` or `Gateway:Sms:...` named | An enabled profile without its upstream settings, an http URL outside Development, or an unknown template placeholder. | Complete the named setting, or remove the profile from `Profiles`. |
| The gateway does not start: `enables 'x', but no profile with that key is registered` | A typo or a later profile listed in `Profiles`. | List only `sso` and `sms`. |
| Every relay is 401 `UNAUTHENTICATED`; the log says `WrongAudience` | API `Codelab:Integration:Gateway:Audience` differs from the gateway's `ServiceAssertions:Server:Audience`. | Make them equal (default `integration-gateway`). |
| 401 with `UnknownKey` / `BadSignature` | The API signs with a key the gateway does not trust (rotation half done). | Fix `TrustedServices` (section 3). |
| 401 with `Expired` | Clock drift between API and gateway beyond `ClockSkewSeconds` (an assertion issued "in the future" is also `Expired`). | Fix the time source (both hosts on the same NTP). |
| 401 with `BindingMismatch` | A proxy between API and gateway rewrites the path, query or body, or the body is above 16 KiB. | Pass requests through unchanged; keep `BaseUrl` equal to the path the gateway sees. |
| 401 with `Replayed` on a retry, or `StoreUnavailable` | The same assertion was sent twice (two gateway nodes with an in-memory replay store also cause this). | Run one node, or move the replay store to SQL. |
| 503 `GATEWAY_UPSTREAM_UNAVAILABLE`, failure kind `CertificateRejected` | The provider's certificate fails the trust policy (unknown CA, renewed leaf not pinned, name mismatch, expired). | Install the CA, update `AuthorityBundlePath` or `PinnedThumbprints`. Never weaken validation. |
| 503, failure kind `ConnectionFailed` or `Timeout` | Firewall, DNS or provider outage. | Check gateway → provider 443 and DNS; `/health/ready` shows DNS failures. |
| 502 `GATEWAY_UPSTREAM_REJECTED`, upstream status 400/401 on `sso/token` | The provider refused the token request (wrong client credentials, redirect URI or an expired code). | Fix the API's SSO client settings; the client must authenticate with `client_secret_post`. |
| 502 on `sms`, upstream status 4xx | The provider refused the message or the credential. | Check `AuthHeaderName`/`AuthHeaderValue`, the template against the provider's API, `Sender`. |
| 502 with upstream status 3xx | The provider redirected (the gateway never follows redirects). | Configure the final URL. |
| 404 `GATEWAY_PROFILE_DISABLED` | The profile is not in `Codelab:Integration:Gateway:Profiles` on this gateway. | Enable it and recycle. |
| `/health/ready` 503 | No profile enabled, an enabled upstream address missing, or a host name that does not resolve. | Fix the configuration or DNS; behind a resolving proxy set `Gateway:Readiness:ResolveUpstreamHosts=false`. |
| SSO login slow after an IdP key rotation | The JWKS document is cached for `Gateway:Sso:JwksCacheDuration`. | Wait out the duration (default 10 min) or recycle the app pool. |

## Gotchas

- One gateway node unless the replay store is shared (see the 401 `Replayed` row).
- The SMS relay is never retried by the gateway or the relay client; a 503 means the message may not have left, a 200
  with `accepted:false` means the provider declined it.
- Health checks and smoke tests run over validated TLS; a test that needs `-k` is a broken certificate setup.
