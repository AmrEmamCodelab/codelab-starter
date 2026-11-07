<#
.SYNOPSIS
    Removes one module from this repo at file level (ADR-004) and records it in codelab.starter.json.

.DESCRIPTION
    Edits codelab-starter.slnx and the ProjectReference list of backend\src\Starter.Api\Starter.Api.csproj; deletes
    every line that carries the module marker '// codelab:module <Name>' (or '<!-- codelab:module <Name> -->') and
    every block from 'codelab:module-begin <Name>' to 'codelab:module-end <Name>' in the .ts, .html and .csproj files
    under frontend\starter-web\src\ and backend\src\ (app.routes.ts, app-nav.ts, app.config.ts,
    features\admin\admin.routes.ts, Starter.Api.csproj, ...); deletes the <camelName>.* keys from
    frontend\starter-web\src\assets\i18n\ar.json and en.json; deletes backend\src\Modules\<Name>\,
    backend\tests\Modules\Starter.Modules.<Name>.Tests\ and frontend\starter-web\src\app\features\<kebab-name>\ when
    present; appends <Name> to removedModules; then reruns node tools\rules-engine\compile.mjs. Nothing is staged or
    committed (init.ps1 commits its own run).

    Refuses (exit 1, nothing changed) a required module (codelab.starter.json requiredModules), a kernel project
    (Domain, Application, Infrastructure, Api), an invalid name, an unknown module, unpaired begin/end markers and a
    translation file that is not a JSON object. A module already in removedModules is a no-op (exit 0). Exit 2: usage
    or internal error.

.PARAMETER Name
    The PascalCase module name, e.g. ServiceRequests.

.PARAMETER Optional
    Instead of -Name: optional module ids to remove whole (init Step 2): attachments, portal, sms, ops. Each id takes
    its backend modules (as -Name does), its services, deploy-kit scripts and tests, its .slnx projects, its root npm
    workspaces and every line marked 'codelab:module <Name>' in .cs, .csproj, .ts, .html, .ps1 and .psm1 files under
    backend\, services\, frontend\starter-web\src\ and deploy\ ('# codelab:module <Name>' in PowerShell). Refused,
    nothing changed, while a shared file the id lists as a marker anchor lacks its markers
    (tools\fork\lib\modules.mjs OPTIONAL_MODULES). Exit 0 done, 1 refused, 2 usage or internal error.

.PARAMETER WhatIf
    With -Optional: lists the edits and deletions and changes nothing.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\remove-module.ps1 -Name Demo

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\remove-module.ps1 -Optional attachments,ops -WhatIf
#>
[CmdletBinding()]
param(
    [string] $Name,
    [string[]] $Optional,
    [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'fork\lib\ForkCommon.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot

$OptionalRemovalLibrary = 'tools\fork\lib\remove-module.mjs'
$ExitUsage              = 2

if ($Optional) {
    if ($Name) {
        Write-Host '-Name and -Optional cannot be combined.' -ForegroundColor Red
        exit $ExitUsage
    }
    try { $node = Use-ForkNode }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        exit $ExitUsage
    }
    $libraryUrl = ([Uri](Join-Path $repoRoot $OptionalRemovalLibrary)).AbsoluteUri
    $entry = "import { runOptionalRemovalCli } from '$libraryUrl'; process.exitCode = runOptionalRemovalCli(process.argv.slice(1), console);"
    $optionalArguments = @('--root', $repoRoot, '--ids', ((Split-ForkList $Optional) -join ','))
    if ($WhatIf) { $optionalArguments += '--what-if' }
    & $node --input-type=module -e $entry -- @optionalArguments
    exit $LASTEXITCODE
}

$arguments = @('remove-module', '--root', $repoRoot)
if ($Name) { $arguments += @('--name', $Name) }

Invoke-ForkCli $repoRoot $arguments
exit $LASTEXITCODE
