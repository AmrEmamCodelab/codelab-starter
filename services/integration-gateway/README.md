# Integration gateway

The DMZ relay of the starter (build plan phase 8): the backend has no egress, so it calls this host with an ES256
service assertion and the gateway relays to the outside upstreams through **profiles**: `sso` (the OIDC token and
JWKS back channel) and `sms` (the client's SMS provider). See [MODULE.md](MODULE.md) for the contract, configuration,
rules and gotchas, and [docs/runbooks/gateway.md](../../docs/runbooks/gateway.md) for deployment and troubleshooting.

## Run

```powershell
# Development, http://localhost:5187; needs the API's S2S public key as a trusted caller (MODULE.md, Run)
E:\codelab-tools\dotnet10\dotnet.exe run --project services\integration-gateway\src\Codelab.Gateway.Api
# http://localhost:5187/health/live   http://localhost:5187/health/ready
```

## Build and test

```powershell
E:\codelab-tools\dotnet10\dotnet.exe build services\integration-gateway\Codelab.Gateway.slnx -c Release -warnaserror
E:\codelab-tools\dotnet10\dotnet.exe test services\integration-gateway\tests\Codelab.Gateway.Api.Tests
```

The tests need no network beyond loopback: `FakeUpstream` plays the OIDC provider and the SMS provider over HTTPS with
a certificate generated per run, and the gateway pins it (real certificate validation).

## Deploy

An IIS site of its own in the DMZ (or Kestrel behind the DMZ reverse proxy), from a `dotnet publish` of
`src/Codelab.Gateway.Api` with the `web.config` template. Secrets (the trusted caller keys, the SMS provider
credential) come from the app pool environment or a secret store. Steps, keys and troubleshooting:
[docs/runbooks/gateway.md](../../docs/runbooks/gateway.md).

## Layout

| Path | Holds |
| --- | --- |
| `src/Codelab.Gateway.Api` | The host and both profiles. |
| `tests/Codelab.Gateway.Api.Tests` | Unit and HTTP tests (`TestSupport/FakeUpstream.cs`, `TestSupport/GatewayHost.cs`). |
| `docs/future-profiles.md` | The profiles planned for later (documentation only, no code). |
| `module.rules.yaml` | The service's rules; `CLAUDE.md`, `.editorconfig`, `BannedSymbols.generated.txt` and `CodeMetricsConfig.txt` are generated from it (`node tools/rules-engine/compile.mjs`). |
