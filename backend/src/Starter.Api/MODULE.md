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
- Modules referenced today: Identity, Access and Users (each a ProjectReference in `Starter.Api.csproj`). The Users
  module needs `Users:Activation:LinkBaseUrl` to issue activation links, and SSO needs `Auth:WebBaseUrl` plus sealed
  tickets (`Codelab:Security:Tickets`); `New-LocalSecrets.ps1` writes the local ticket ring.
- The OpenAPI document lists the request's host under `servers`; DocsGen generates `docs/generated/openapi.v1.json`.
