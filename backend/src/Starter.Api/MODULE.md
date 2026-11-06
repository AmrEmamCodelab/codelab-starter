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
- Without the local secrets file the start fails on the Codelab.Security key rings; run `New-LocalSecrets.ps1`.
- `Database:ApplyMigrationsOnStartup` is true only in `appsettings.Development.json`; any other environment refuses it.
- Modules referenced today: Identity, Access, Users, Lookups, Audit, Settings, Jobs, Notifications, Portal and Attachments (each a
  ProjectReference in `Starter.Api.csproj`). `appsettings.json` documents the Phase 5 defaults: `Messaging:Outbox`
  (on), `Numbering:TimeZoneId`, `Settings:CheckStampPerScope`, `Jobs` (job server on per node), `Notifications` (e-mail
  Smtp with no relay host, so a server fails at start until `Notifications:Email:Smtp:Host` is set; SMS off) and
  `Portal:Contact` (the inbox, required on start: every deployment sets its own); `Attachments` (phase 6): `TicketLifetimeSeconds` 300 and `Codelab:Security:ServiceAssertions:Client` ServiceName `starter-api` / Audience `starter-attachments` in `appsettings.json`; `Attachments:ServiceBaseUrl` only in Development (`http://localhost:5185`), so a server fails at start until it sets an https URL; the client KeyId and PrivateKeyPem come from the local secrets file (`New-LocalSecrets.ps1`) or the server's settings. An API local secrets file from before phase 6 lacks `Codelab:Security:Uploads` and `ServiceAssertions:Client`; rerun `New-LocalSecrets.ps1` (no -Force) or `run-local.ps1` to add them; Development switches e-mail to the
  FileDrop folder and turns the file SMS sink on (both refused outside Development). The Users
  module needs `Users:Activation:LinkBaseUrl` to issue activation links, and SSO needs `Auth:WebBaseUrl` plus sealed
  tickets (`Codelab:Security:Tickets`); attachment upload tickets use their own ring (`Codelab:Security:UploadTickets`,
  the only ring the attachment service holds). `New-LocalSecrets.ps1` writes both local ticket rings.
- The OpenAPI document lists the request's host under `servers`; DocsGen generates `docs/generated/openapi.v1.json`.
