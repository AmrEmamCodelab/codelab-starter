# tools/kit

## Purpose

The deploy-kit builder behind `tools\build.ps1 -Kit` (phase 8). One command turns the current work tree into
`publish\deploy-<shortsha>\`, the folder a developer hands to the server operators:

```text
publish\deploy-<shortsha>\
  api\          dotnet publish of backend/src/Starter.Api             -> kits\Install-Api.ps1
  attachment\   dotnet publish of the Attachment service               -> kits\Install-Attachment.ps1
  gateway\      dotnet publish of the integration gateway (optional)   -> kits\Install-Gateway.ps1
  web\          production build of frontend/starter-web               -> kits\Install-Web.ps1
  ops\api\ ops\web\  Ops Console API + screens (optional)              -> kits\Install-Ops.ps1
  sql\          NN-<context>.sql from tools/sql-kit/New-DbaSqlScripts.ps1 (when that generator exists)
  kits\         deploy/kits without tests\ (Install-*.ps1, Install-Common.psm1, templates\)
  README.md     from templates/README.template.md (install order, SQL, package versions)
  manifest.json commit, dirty flag, packages/versions.json, components, SQL decision, SHA-256 of every file
```

| File | Role |
| --- | --- |
| `New-DeployKit.ps1` | The entry point: resolve, plan (`-DryRun` stops here), build, assemble, check, write the manifest. |
| `kit-components.psd1` | Data: the deployables, their kit folders, parts (`1` dotnet publish, `2` npm web build) and install scripts. |
| `lib/DeployKit.psm1` | The pure decisions: kit name, component selection, layout, payload filters, SQL decision, hashes, manifest, README values. |
| `templates/README.template.md` | The kit README, `{{TOKEN}}` placeholders filled by `Get-KitReadmeValues`. |
| `tests/DeployKit.Tests.ps1` | Pester 3.4 tests of the module, the real catalog and template, and `-DryRun`. |

## Use

```powershell
# The plan only (no build, nothing written)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\kit\New-DeployKit.ps1 -DryRun
# The default kit (api, attachment, web) from a clean, committed tree
powershell -NoProfile -ExecutionPolicy Bypass -File tools\kit\New-DeployKit.ps1
# Every deployable
powershell -NoProfile -ExecutionPolicy Bypass -File tools\kit\New-DeployKit.ps1 -Component api,attachment,gateway,web,ops
# Through the build entry point (CI first, then the kit only when CI is green)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI -Kit
# Tests
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/kit/tests -EnableExit"
```

Parameters: `-Component`, `-OutputRoot` (default `<repo>\publish`), `-ArtifactsPath` (default
`%TEMP%\codelab-kit-<shortsha>`; the dotnet `--artifacts-path` and the web build output), `-Sha`, `-AllowDirty`,
`-SkipSql`, `-Force`, `-DryRun`. Exit codes: 0 built or planned, 1 refused or failed, 2 usage. The operator side
(what to run on each server) is the `prep-deploy` skill (`.claude/skills/prep-deploy/SKILL.md`) and the kit's README.

## Extend

- **A new deployable:** add an entry to `kit-components.psd1` (unique `Name`, `Folder`, `Order`; its parts; its
  `InstallScript` in `deploy/kits`). The catalog tests check it; the layout, README and manifest follow.
- **A new README section:** add a `{{TOKEN}}` to the template and its value to `Get-KitReadmeValues`;
  `Expand-KitTemplate` refuses a token without a value, and the README tests expand the real template.
- **A new manifest field:** add it in `New-KitManifest` and to the manifest test. Bump `KitFormatVersion` when a
  consumer must tell the formats apart.
- **A file type that must never ship:** add its pattern to `ForbiddenKitNames` (whole kit) or
  `PayloadExcludedNames` (left out of a payload), with a test case.

## Rules

- Private toolchain only: `CODELAB_DOTNET`, else `E:\codelab-tools\dotnet10\dotnet.exe`; `CODELAB_NODE_DIR`, else
  `E:\codelab-tools\node24`, first on `PATH`. Builds use `--artifacts-path` outside the work tree, so a running
  local stack's `bin\` is never touched.
- A kit never carries server settings values, hosts, accounts, keys or certificates. Development settings and
  `*.local.json` files are left out of every payload, and the kit is refused when any file is key material
  (`*.pfx`, `*.p12`, `*.key`, `*.pem`, `*.snk`) or a local secret.
- A dirty work tree is refused unless `-AllowDirty`; such a kit is marked `dirty: true` and "not for production".
- A component whose install script is missing is refused: a kit never ships what it cannot install.
- The builder deletes only what it created: a kit folder it is building when a step fails, and, with `-Force`, an
  existing kit folder that holds its own `manifest.json` (`kitFormat`). Any other existing folder is refused.
- The DBA scripts come only from `tools/sql-kit/New-DbaSqlScripts.ps1 -OutputPath <kit>\sql`; when the generator
  is absent the kit is built without `sql\`, and the README and manifest say why.

## Gotchas

- Windows PowerShell 5.1 passes `-Component api,web` to a `-File` script as one string; `Split-KitList` splits it.
- The web build runs the workspace's `npm run build` (its `prebuild` step included) with `-- --output-path`; the
  kit takes the `browser\` folder of that output.
- `dotnet publish` with `--artifacts-path` reuses the artifacts folder between runs (incremental); delete
  `%TEMP%\codelab-kit-<shortsha>` for a cold build.
- The manifest lists every file except itself; the README carries a one-line hash check for the operator.
- A fork that renames or removes a deployable edits `kit-components.psd1` (a project is found by pattern under its
  root, so a renamed project file still resolves while its folder stays).
