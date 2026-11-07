# Starter.Api

## Purpose
The host. `Program.cs` (at most 150 lines, asserted by an architecture test) wires the Codelab packages
(Observability, Errors, Security), the kernel (`AddStarterKernel`, `UseStarterPipeline`, `MapStarterEndpoints`) and
every module discovered from this project's module ProjectReferences. It holds no endpoint and no business code.

## Use
```powershell
powershell -File tools\devseed\New-LocalSecrets.ps1          # once: appsettings.Development.local.json (gitignored)
& E:\codelab-tools\dotnet10\dotnet.exe run --project backend\src\Starter.Api   # http://localhost:5180
```
- Development: LocalDB `StarterDev` (created and migrated on start), CORS for `http://localhost:4280`, OpenAPI at
  `/openapi/v1.json`, Seq at `http://localhost:5341` when it runs.
- Configuration: `appsettings.json` (strict JSON, a fork prefix target: `Codelab:Security:Protocol:Prefix` = `STARTER`),
  `appsettings.Development.json`, then the local secrets file, environment variables and the command line.
- Request and response signing are `Enforce`; the replay and session stores are `SqlServer` on `ConnectionStrings:Starter`.

**Where the security edge is wired.** `Program.cs` holds no edge code; two kernel calls carry it (details in
`backend/src/Starter.Infrastructure/MODULE.md`, Security edge):

| `Program.cs` line | What it wires |
| --- | --- |
| `builder.AddCodelabSecurity()` | Binds `Codelab:Security:Cors` (`CodelabCorsOptions`) and the client rate limiter (`Codelab:Security:RateLimiting`, policy `codelab-client`). |
| `app.UseStarterPipeline()` | First runs `CorsOriginBootCheck.EnsureValid`: a bad `Codelab:Security:Cors:AllowedOrigins` throws `OptionsValidationException` and the host never starts. Then `UseCors()` and the rest of the pipeline. |
| `app.MapStarterEndpoints(modules)` | Maps, after the module controllers and the signing-key endpoint, the anonymous CSP report sink `POST /api/security/csp-reports` (`CspReportEndpoint.MapCspReportSink`, route `SecurityEdgeRoutes.CspReports`), then health. |

In Development the API's own origin list is `appsettings.Development.json` (`http://localhost:4280`); every other
environment sets https origins on the server (`deploy/kits/templates/api/README.md`).

## Extend
- **A module:** `node tools/scaffold/new-module.mjs --name <Name>` adds the ProjectReference here (and the slnx entry);
  `tools/remove-module.ps1 -Name <Name>` removes it. Never edit `Program.cs` for a module.
- **Host wiring:** a new `AddStarterXxx`/`UseStarterXxx` extension in Starter.Infrastructure, called from `Program.cs`.
- **A configuration key:** an options class with `public const string SectionName` and a validator; secrets go to the
  local secrets file (and `New-LocalSecrets.ps1`), never to a committed file.

## Rules
- Modules appear only as ProjectReferences in `Starter.Api.csproj`; the build turns each into a
  `ModuleAssemblyAttribute` (backend/Directory.Build.targets, `CodelabModuleHost=true`).
- `appsettings.json` and `appsettings.Development.json` are strict JSON (no comments) and hold no secret.
- The host never uses a module namespace (`starterapi.no-module-namespace`).

## Gotchas
- `AddStarterKernel` must stay before `AddCodelabSecurity` (startup order: migrations, then the security store schema).
- A start that fails with `OptionsValidationException` naming `Codelab:Security:Cors` is the CORS boot check: fix the
  listed origins (exact `scheme://host[:port]`, https outside Development, each once). Never move `UseCors()` or the
  check out of `UseStarterPipeline` into `Program.cs`.
- The CSP sink is anonymous and unsigned by design: its signature exemption carries `CspReportEndpoint.AnonymousReason`,
  and its row in `backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs` says why. A trace context sent by a caller is ignored (Codelab.Observability) and, behind
  the IIS or nginx front site, stripped before it arrives (`deploy/kits/MODULE.md`).
- Without the local secrets file the start fails on the Codelab.Security key rings; run `New-LocalSecrets.ps1`.
- `Database:ApplyMigrationsOnStartup` is true only in `appsettings.Development.json`; any other environment refuses it.
- Modules referenced today: Identity, Access, Users, Lookups, Audit, Settings, Jobs, Notifications, Portal,
  Attachments, Records, Assignments, Deadlines, Ops (marker `codelab:module Ops`; its settings live under
  `Codelab:Ops` in `appsettings.json`) and the deletable sample ServiceRequests (each a ProjectReference in
  `Starter.Api.csproj`; the sample's line carries the `codelab:module ServiceRequests` marker, so
  `tools/remove-module.ps1 -Name ServiceRequests` drops it). With Records in the host, every registered record type
  needs a workflow (`AddRecordWorkflow<TDefinition>()`) or the start fails (`RecordTypesStartupCheck`); a test host
  that registers a record type registers its workflow too. `appsettings.json` documents the Phase 5 defaults: `Messaging:Outbox`
  (on), `Numbering:TimeZoneId`, `Settings:CheckStampPerScope`, `Jobs` (job server on per node), `Notifications` (e-mail
  Smtp with no relay host, so a server fails at start until `Notifications:Email:Smtp:Host` is set; SMS off) and
  `Portal:Contact` (the inbox, required on start: every deployment sets its own); `Attachments` (phase 6): `TicketLifetimeSeconds` 300 and `Codelab:Security:ServiceAssertions:Client` ServiceName `starter-api` / Audience `starter-attachments` in `appsettings.json`; `Attachments:ServiceBaseUrl` only in Development (`http://localhost:5185`), so a server fails at start until it sets an https URL; the client KeyId and PrivateKeyPem come from the local secrets file (`New-LocalSecrets.ps1`) or the server's settings. An API local secrets file from before phase 6 lacks `Codelab:Security:Uploads` and `ServiceAssertions:Client`; rerun `New-LocalSecrets.ps1` (no -Force) or `run-local.ps1` to add them; Development switches e-mail to the
  FileDrop folder and turns the file SMS sink on (both refused outside Development). The Users
  module needs `Users:Activation:LinkBaseUrl` to issue activation links, and SSO needs `Auth:WebBaseUrl` plus sealed
  tickets (`Codelab:Security:Tickets`); attachment upload tickets use their own ring (`Codelab:Security:UploadTickets`,
  the only ring the attachment service holds). `New-LocalSecrets.ps1` writes both local ticket rings.
- The OpenAPI document lists the request's host under `servers`; DocsGen generates `docs/generated/openapi.v1.json`.
