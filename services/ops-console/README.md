# Ops Console

The Ops Console's server side: a read-only backend-for-frontend for operators who sign in with Windows and belong to
one AD group. It reads the starter API's `/api/ops` with an ES256 service assertion per request and Seq with a
read-only key, and traces a user's reference code to the request and its log events. Optional per project.

- **What it does, its endpoints, configuration, rules and gotchas:** [MODULE.md](MODULE.md).
- **IdP signing certificate renewal and the expiry alert:** [docs/runbooks/idp-cert-renewal.md](../../docs/runbooks/idp-cert-renewal.md).

## Run

```powershell
& E:\codelab-tools\dotnet10\dotnet.exe run --project services\ops-console\src\Codelab.OpsConsole.Api
# http://localhost:5188/health/live, then http://localhost:5188/ops-api/me (Development: the dev operator)
```

## Build and test

```powershell
& E:\codelab-tools\dotnet10\dotnet.exe build services\ops-console\Codelab.OpsConsole.slnx -warnaserror
& E:\codelab-tools\dotnet10\dotnet.exe test services\ops-console\Codelab.OpsConsole.slnx
```

The tests need no SQL Server, no domain and no network beyond loopback: keys are generated per run, the API and Seq are
fakes, and the host runs on Kestrel on a free loopback port.

## Deploy

Publish `src/Codelab.OpsConsole.Api` (framework-dependent) and install it as a child application under the existing
site with the Ops install kit, with Windows authentication on and anonymous authentication off for that application.
Supply the operator group, the API address, the console's service key and the Seq read key from the app pool
environment or a secret store; the shipped settings and `web.config` carry none.

## Layout

```
services/ops-console/
  Codelab.OpsConsole.slnx
  src/Codelab.OpsConsole.Application/   reference-code lookup, ports (no HTTP)
  src/Codelab.OpsConsole.Api/           the host: Authentication/, Upstream/, Endpoints/, Hosting/, web.config
  tests/                                unit and HTTP tests
```
