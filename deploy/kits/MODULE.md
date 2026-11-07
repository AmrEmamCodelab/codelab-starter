# deploy/kits (Install-Common.psm1)

## Purpose

`Install-Common.psm1` is the shared PowerShell module every IIS deploy kit is built on (build plan phase 6; the
API, web, gateway and Ops kits in phase 8 reuse it). A kit script stays a short, readable sequence of these steps,
and the risky parts (settings, backups, IIS, file copy) are written and tested once:

| Function | What it does |
| --- | --- |
| `Test-KitPrerequisites` | Read-only check of IIS (W3SVC + InetStp), the WebAdministration module, the ASP.NET Core Module V2 and an ASP.NET Core runtime of `-RuntimeMajor` (default 10). These two come with the .NET Hosting Bundle. `-RequireAdministrator` adds an elevation check. Returns `Passed` and `Checks` (`Name`, `Passed`, `Detail`), and logs each failure with what to install. |
| `Backup-Deployment` | Copies the deployed folder (without `logs\`) and the `-ConfigPath` files (plus `applicationHost.config` with `-IncludeIisConfig`) to `<BackupRoot>\<Name>-<yyyyMMdd-HHmmss>` with `site\`, `config\` and `backup.json`. Returns the path. On a first install with nothing to copy it returns `$null`. |
| `Merge-AppSettings` | Adds to the server's settings file only the keys that are new in the package template. It never overwrites a value, keeps the server's key order, BOM and line endings, and reports `AddedKeys` and `Conflicts`. A missing server file is created from the template. |
| `Copy-KitPayload` | Copies the package folder over the deployed folder with a robocopy `/MIR`. It never writes or deletes `appsettings.json`, `appsettings.*.json`, `app_offline.htm` or `logs\`. |
| `Get-KitIisPlan` | Returns the ordered IIS actions (`KitIisAction`: `CreateFolder`, `CreateAppPool`, `UpdateAppPool`, `CreateSite`, `UpdateSite`, `CreateApplication`, `UpdateApplication`, or a single `None`) with the exact property changes. It is pure when given `-CurrentState`. |
| `Set-KitIisApplication` | Reads IIS (read-only), plans, and applies each step through WebAdministration. It is idempotent: a second run plans `None`. |
| `Write-KitLog` | Writes one `[OK]` / `[!!]` style console line and, with `-LogPath`, appends a timestamped line to a UTF-8 log file. |

## Use

Import the module once at the top of a kit script, and pass `-WhatIf` through explicitly (see Gotchas):

```powershell
[CmdletBinding(SupportsShouldProcess = $true)]
param([string] $PackagePath, [string] $SiteName, [string] $AppPath, [string] $AppPoolName, [string] $TargetPath, [string] $BackupRoot)
Import-Module (Join-Path $PSScriptRoot 'Install-Common.psm1') -Force
$PSDefaultParameterValues['Write-KitLog:LogPath'] = Join-Path $BackupRoot 'install.log'

$check = Test-KitPrerequisites -RuntimeMajor 10 -RequireAdministrator
if (-not $check.Passed) { throw 'Prerequisites are missing; nothing was changed.' }

$plan    = Set-KitIisApplication -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName `
               -PhysicalPath $TargetPath -WhatIf:$WhatIfPreference
$backup  = Backup-Deployment -SourcePath $TargetPath -BackupRoot $BackupRoot -Name 'attachments' -WhatIf:$WhatIfPreference
Copy-KitPayload -SourcePath (Join-Path $PackagePath 'app') -DestinationPath $TargetPath -WhatIf:$WhatIfPreference | Out-Null
$merge   = Merge-AppSettings -TemplatePath (Join-Path $PackagePath 'app\appsettings.json') `
               -ServerPath (Join-Path $TargetPath 'appsettings.json') -WhatIf:$WhatIfPreference
```

The order matters. Plan IIS first, then back up before anything is copied, then copy the payload, then merge
settings. A rollback is a copy of `<backup>\site\*` back over the folder.

Tests (Pester **3.4**, the version Windows PowerShell 5.1 ships; no IIS change and no admin needed; each test
creates and deletes its own `%TEMP%\codelab-kit-tests-*` folder):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Common.Tests.ps1 -EnableExit"
```

The merge fixtures are in `tests/fixtures/common/`: `server.json` and `template.json` in, and `expected-merged.json`
as the exact expected text.

### Install-Attachment.ps1 (the attachment service kit)

```powershell
# Preview (no admin needed; nothing is written, every planned action is printed)
.\Install-Attachment.ps1 -PackagePath <publish folder or kit folder with app\> -SiteName '<existing site>' -StorageRoot '<store folder or share>' -WhatIf
# Install (elevated). Exit 0 done, 1 refused/failed, 2 installed but required settings still empty
.\Install-Attachment.ps1 -PackagePath <...> -SiteName '<existing site>' -StorageRoot '<...>' [-AppPath /attachments] [-AppPoolName StarterAttachments] [-TargetPath ...] [-BackupRoot ...] [-HealthBaseUrl http://localhost]
```

Steps: checks, prerequisites, storage root (the server value wins over -StorageRoot), IIS pool + child application, backup, app_offline.htm, payload mirror, Merge-AppSettings for appsettings.json and appsettings.Production.json (from templates/attachment/appsettings.Production.template.json), storage folder + Modify for IIS AppPool\<pool>, required-settings check, GET <app>/health/ready (the readiness route; /health alone is not mapped and answers 401). The keys an operator fills are listed in templates/attachment/README.md. Tests: `Invoke-Pester deploy/kits/tests/Install-Attachment.Tests.ps1`.

## Extend

- **A new kit (`Install-<Name>.ps1`):** follow the sequence in Use. Put the kit's own names (default site, pool and
  folder names, health path) in a constants block at its top. Give it `[CmdletBinding(SupportsShouldProcess = $true)]`,
  and pass `-WhatIf:$WhatIfPreference` on every call into this module.
- **A new reusable step:** add it here as an advanced function with `SupportsShouldProcess`, gate every write behind
  `$PSCmdlet.ShouldProcess`, put every literal in the constants block, add it to `Export-ModuleMember`, add Pester
  3.4 tests in `tests/Install-Common.Tests.ps1`, and add a row to the table in Purpose.
- **A new IIS action:** add a `KitIisAction` member with the next explicit integer value, plan it in
  `Get-KitIisPlan` (pure, from the state object), apply it in `Invoke-KitIisAction`, and test the plan through
  `-CurrentState` and the apply through `Mock -ModuleName Install-Common Invoke-KitIisAction`.
- **Another server-owned file:** pass it with `Copy-KitPayload -ExcludeFiles` / `-ExcludeDirectories` from the kit.
  If every kit needs it, add it to `$script:ServerSettingsFilePatterns` instead.

## Rules

- Windows PowerShell 5.1 compatible. Use no PowerShell 7 syntax (`??`, `?.`, ternary, `&&`, `||`, `-Parallel`).
  The module runs under `Set-StrictMode -Version Latest` and `$ErrorActionPreference = 'Stop'`.
- Every exported function supports `-WhatIf`, and under `-WhatIf` nothing is written: no settings, backup, copy, IIS
  change or log file.
- The server owns its settings. A kit never ships values that replace server values. It only adds new keys, and
  only through `Merge-AppSettings`.
- Enums are integers with explicit values (`KitLogLevel`, `KitIisAction`). There are no magic strings or numbers,
  because every name, path, switch and limit is in the constants block at the top of the module.
- No hosts, IP addresses, accounts, UNC paths or product names in the module, the tests or the fixtures. Those values
  belong in the kit's parameters and on the server (scrub sweep).
- Tests use Pester 3.4 syntax (`Should Be`, `Should BeExactly`, `Should Throw`), only their own temp folders, and
  never IIS or admin rights. `Set-KitIisApplication` is tested with `Get-KitIisState` and `Invoke-KitIisAction`
  mocked.
- Error messages never echo a settings file's content, because settings files can hold secrets.

## Gotchas

- **`-WhatIf` does not cross into a module by itself.** A script's `$WhatIfPreference` is not seen inside module
  functions, so kits pass `-WhatIf:$WhatIfPreference` on every call. Without it a `-WhatIf` run of the kit still
  changes the server.
- **Formatting is normalized.** `Merge-AppSettings` round-trips JSON. It keeps order, values, Arabic text, decimal
  scale (`2.50`), BOM and line endings, but the indentation becomes two spaces. JSON comments and trailing commas are
  refused (the Windows PowerShell 5.1 parser), and the file is left unchanged. The backup holds the original.
- **Arrays are values.** A server array is kept whole even when the package template's array has more items, so a new
  allowed origin or extension is a server edit, not a kit patch. Only a missing array key is added.
- **A shape conflict is kept.** When the server has a value where the template has a section (or the reverse), the
  server side wins and the key is listed in `Conflicts`. Read it in the kit output.
- **Mirror deletes.** `Copy-KitPayload` uses `/MIR`, so files the package no longer has are removed. Keep storage
  roots and uploads outside the app folder, or exclude them with `-ExcludeDirectories`. Excluded names are never
  purged.
- **IIS state needs elevation.** Without admin rights the IIS configuration cannot be read. `Get-KitIisPlan` then
  previews a fresh server (the steps carry a "not elevated" note), and `Set-KitIisApplication` without `-WhatIf`
  refuses to run.
- **The IIS: drive exists only after WebAdministration loads.** IIS paths are joined as plain strings
  (`Join-KitIisPath`), never with `Join-Path`.
- **A first install has no backup.** `Backup-Deployment` returns `$null` when the folder does not exist yet and no
  config file is given.
- **Pester 3.4:** `Should Contain` reads a *file*, not a collection, so collections are compared joined into a
  string. `Assert-MockCalled` needs `-Scope It`, or it counts the calls of the whole `Describe`.
- **Install-Attachment: the storage root is server-owned.** Once the server's appsettings.Production.json has Attachments:Storage:RootPath, -StorageRoot is ignored (with a warning). Moving the store means moving the files and editing that file.
- **Install-Attachment: a first install ends with exit 2.** The Production file is created with empty key rings, CORS origins and S2S key, so the health probe is skipped until an operator fills them. Then recycle the pool or rerun the kit.
- **Install-Attachment: web.config is package-owned.** The mirror replaces it on every run, so put server-specific values in appsettings.Production.json or the app pool environment, not in web.config.
- **Child applications inherit the site's URL Rewrite rules.** The front site's `SPA fallback` (templates/web/web.config) rewrites every path its `{URL}` exclusion does not name to `/index.html`, also inside child applications. A new child path goes into that exclusion; Install-Web never rewrites an existing server rule, so an older site needs the edit by hand. Install-Ops reads the site's web.config and refuses (nothing changed) while a Rewrite rule would capture `-ApiAppPath` or `-WebAppPath`.
- **Install-Gateway: the template follows the host's options.** `Codelab:Integration:Gateway:Profiles` is a list of profile keys (not objects), the upstreams live in `Gateway:Sso` and `Gateway:Sms`, and the audience is `integration-gateway`. A requirement's `when` can be a list of `{ path, equals }` conditions (an item of a list counts), which is how the upstream keys apply only to a listed profile. `DeployKitTemplateTests` (gateway tests) binds the template into the host's options and validators, so a renamed host setting fails there.
