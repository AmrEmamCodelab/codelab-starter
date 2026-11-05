<#
.SYNOPSIS
    Turns a fresh clone of codelab-starter into a project fork, in one recorded commit (ADR-002, ADR-003).

.DESCRIPTION
    Phase 2 prototype: only -Minimal is implemented (the interactive init with branding, login modes and keys arrives
    in phase 10). Run it once, inside the fork clone, on a clean work tree with npm dependencies installed
    (`npm ci`; the rules compiler needs them). It refuses when codelab.starter.json already has a starterBase.

    Steps: record starterBase = HEAD; remove each -RemoveModule through tools\remove-module.ps1; capture
    packages\versions.json and remove packages\; switch to feed mode (UseCodelabPackageSource defaults to false,
    Codelab.* pinned in Directory.Packages.props, @codelab/* npm dependencies pointed at file: tarballs in the fork's
    feed\npm, the needed .nupkg/.tgz copied from -FeedDir into the fork's feed\, whose contents are gitignored);
    apply -RenameMap; set -ProductPrefix in every codelab.starter.json prefixTargets entry; regenerate the rules
    outputs; refresh package-lock.json; run tools\devseed\New-LocalSecrets.ps1 when it exists; hash the source-owned
    files; write codelab.starter.json; commit 'init: minimal fork (prefix <P>, removed <names>)'.

    Exit codes: 0 done, 1 refused (nothing changed: bad prefix, required module, already initialized, dirty tree,
    missing feed versions or targets), 2 usage or internal error.

.PARAMETER Minimal
    Required in this version: the scripted, non-interactive init.

.PARAMETER ProductPrefix
    The product prefix: 2-16 characters, A-Z first, then A-Z or 0-9 (STARTER, CODELABTEST and ALTPFX are reserved).

.PARAMETER RemoveModule
    Module names to remove (comma-separated or repeated). Identity and Access are required and refused.

.PARAMETER FeedDir
    Folder holding nuget\ and npm\ with the packed Codelab packages. Default: the repo's feed\. When a
    packages\versions.json version is missing, its highest <version>-ci.<n> prerelease is pinned instead.

.PARAMETER RenameMap
    Optional JSON file: [{ "from": "Starter", "to": "Acme" }, ...]. Default: identity (no rename).

.PARAMETER SkipSecrets
    Do not run tools\devseed\New-LocalSecrets.ps1.

.PARAMETER SkipNpmLock
    Do not refresh package-lock.json (npm install --package-lock-only) after the npm manifests change.

.PARAMETER AllowMissingTargets
    Skip, with a notice, prefix targets and npm manifests that do not exist (for repos without a frontend).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1 -Minimal -ProductPrefix ACME -RemoveModule Demo -FeedDir E:\codelab-starter\feed
#>
[CmdletBinding()]
param(
    [switch] $Minimal,
    [string] $ProductPrefix,
    [string[]] $RemoveModule = @(),
    [string] $FeedDir,
    [string] $RenameMap,
    [switch] $SkipSecrets,
    [switch] $SkipNpmLock,
    [switch] $AllowMissingTargets
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\fork\lib\ForkCommon.ps1')

$arguments = @('init', '--root', $PSScriptRoot)
if ($Minimal) { $arguments += '--minimal' }
if ($ProductPrefix) { $arguments += @('--product-prefix', $ProductPrefix) }
foreach ($name in (Split-ForkList $RemoveModule)) { $arguments += @('--remove-module', $name) }
if ($FeedDir) { $arguments += @('--feed-dir', $FeedDir) }
if ($RenameMap) { $arguments += @('--rename-map', $RenameMap) }
if ($SkipSecrets) { $arguments += '--skip-secrets' }
if ($SkipNpmLock) { $arguments += '--skip-npm-lock' }
if ($AllowMissingTargets) { $arguments += '--allow-missing-targets' }

Invoke-ForkCli $PSScriptRoot $arguments
exit $LASTEXITCODE
