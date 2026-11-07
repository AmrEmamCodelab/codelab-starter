# Deploy kit {{KIT_NAME}}

| | |
| --- | --- |
| Commit | `{{COMMIT}}` |
| Built from uncommitted changes | {{DIRTY}} |
| Built at (UTC) | {{CREATED_UTC}} |

This kit was built by `tools/kit/New-DeployKit.ps1` (`tools\build.ps1 -Kit`). It carries no server settings, hosts,
accounts, keys or certificates: every server keeps its own `appsettings*.json`, and each install script adds only
the keys that are new in this release, then lists the required settings that are still empty and refuses to start
the application while any is.

## Contents

| Folder | Component | Installed with |
| --- | --- | --- |
{{COMPONENT_ROWS}}
| `kits\` | the install scripts, `Install-Common.psm1` and `templates\` | |
| `manifest.json` | commit, package versions, components and the SHA-256 of every file | |

## Before you start

1. Copy the whole folder to the server (keep the layout: the scripts find `templates\` next to themselves).
2. Check that nothing changed on the way (prints nothing when every file matches):

   ```powershell
   $kit = Get-Content .\manifest.json -Raw | ConvertFrom-Json
   $kit.files | Where-Object { (Get-FileHash -LiteralPath (Join-Path . $_.path) -Algorithm SHA256).Hash -ne $_.sha256 } | ForEach-Object { $_.path }
   ```

3. Open an elevated Windows PowerShell 5.1 in this folder. Each script prints its full parameter help with
   `Get-Help .\kits\<script> -Full`.

## Install order

Run every script with `-WhatIf` first: it prints each planned IIS, file and settings change and makes none.

{{INSTALL_ORDER}}

After each install the script checks `/health/ready` over HTTPS with certificate validation on. A script that
reports required settings as missing has installed the files but left the application stopped: fill in the listed
keys on that server (never in the kit) and run the same command again.

## Database

{{SQL_SECTION}}

## Package versions

{{PACKAGES}}
