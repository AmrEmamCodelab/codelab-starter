<#
.SYNOPSIS
    The real-repo fork-model test (ADR-003; phase 2 exit criterion, wired into CI by phase 2 wave 4).

.DESCRIPTION
    Everything happens in a %TEMP% scratch folder; the starter repo, its .git and its feed\ are only read.
      a. Snapshot the starter working tree (git ls-files -co --exclude-standard) into a scratch upstream repo.
      b. Scaffold the removable module there (node tools\scaffold\new-module.mjs --name <RemoveModule>), commit.
      c. Clone a fork and run init.ps1 -Minimal -RemoveModule <RemoveModule>.
      d. Upstream commit: edit the 5 -EditFile files, add -AddFile, touch the removed module, add a file under
         packages\.
      e. Pre-edit -ConflictFile in the fork (same lines), commit.
      f. tools\upstream-merge.ps1; assert exactly -ConflictFile conflicts, resolve it scripted (keep both sides),
         -Continue.
      g. dotnet build/test codelab-starter.slnx -p:UseCodelabPackageSource=false with an empty temporary
         NUGET_PACKAGES, then npm install, build and test of frontend\starter-web from the feed tarballs.
      h. Assert: no packages\ in the fork; Codelab.* restored only from the fork's feed (project.assets.json and
         .nupkg.metadata); @codelab/* from feed tarballs (package-lock.json); the conflicts equal the expected set;
         starterBase and hashes updated.
    When -FeedDir lacks the current packages\versions.json versions, a ci prerelease is packed with
    tools\build.ps1 -Pack inside a scratch copy of the snapshot, never into the repo's feed\.

    Exit codes: 0 passed, 1 an assertion failed, 2 usage or setup error (e.g. the scaffolder does not exist yet).

.PARAMETER RemoveModule
    The module to scaffold upstream and remove in the fork (e.g. Demo).

.PARAMETER StarterRoot
    The starter repo to snapshot. Default: this script's repo.

.PARAMETER FeedDir
    Feed to init the fork from. Default: <StarterRoot>\feed.

.PARAMETER EditFile
    Exactly 5 repo-relative files the upstream commit edits (a probe comment appended). Default: CONTEXT.md,
    docs\README.md, tools\README.md, docs\adr\README.md, docs\standards\SKILL.md.

.PARAMETER ConflictFile
    The edited file the fork pre-edits (the one expected conflict). Default: the first -EditFile.

.PARAMETER AddFile
    The file the upstream commit adds. Default: docs\fork-model-probe.md.

.PARAMETER KeepScratch
    Keep the scratch folder (its path is printed).

.PARAMETER SkipBuild
    Skip step g and the feed-origin checks (for a starter without backend or frontend, e.g. a synthetic one).

.PARAMETER SkipSecrets
    Passed to init.ps1.

.PARAMETER SkipNpmLock
    Passed to init.ps1.

.PARAMETER AllowMissingTargets
    Passed to init.ps1.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\fork\Test-ForkModel.ps1 -RemoveModule Demo
#>
[CmdletBinding()]
param(
    [string] $RemoveModule,
    [string] $StarterRoot,
    [string] $FeedDir,
    [string[]] $EditFile = @(),
    [string] $ConflictFile,
    [string] $AddFile,
    [switch] $KeepScratch,
    [switch] $SkipBuild,
    [switch] $SkipSecrets,
    [switch] $SkipNpmLock,
    [switch] $AllowMissingTargets
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\ForkCommon.ps1')
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $StarterRoot) { $StarterRoot = $repoRoot }

$arguments = @('fork-model', '--starter-root', $StarterRoot)
if ($RemoveModule) { $arguments += @('--remove-module', $RemoveModule) }
if ($FeedDir) { $arguments += @('--feed-dir', $FeedDir) }
foreach ($file in (Split-ForkList $EditFile)) { $arguments += @('--edit-file', $file) }
if ($ConflictFile) { $arguments += @('--conflict-file', $ConflictFile) }
if ($AddFile) { $arguments += @('--add-file', $AddFile) }
if ($KeepScratch) { $arguments += '--keep-scratch' }
if ($SkipBuild) { $arguments += '--skip-build' }
if ($SkipSecrets) { $arguments += '--skip-secrets' }
if ($SkipNpmLock) { $arguments += '--skip-npm-lock' }
if ($AllowMissingTargets) { $arguments += '--allow-missing-targets' }

Invoke-ForkCli $repoRoot $arguments
exit $LASTEXITCODE
