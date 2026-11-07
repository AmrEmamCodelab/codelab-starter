<#
.SYNOPSIS
    Merges starter (upstream) changes into this fork, replaying the fork's rename (ADR-003).

.DESCRIPTION
    Run it in an initialized fork (codelab.starter.json has a starterBase) on a clean work tree.
      1. git fetch <Upstream> <Branch>.
      2. git diff --binary <starterBase>..<fetched commit>.
      3. Every path and content identifier is rewritten through renameMap (longest first, Starter/starter/STARTER
         variants, identifier boundaries); the pre-images are re-hashed so the three-way fallback merges renamed
         content.
      4. Dropped (keep-deletion): files under removedModules and packages\, upstream-added files there included, and
         the removed modules' registrations: their lines in the solution and host project, their marked lines
         (codelab:module <Name>) in shared files and their translation keys. Rules-engine outputs are not merged;
         they are regenerated. codelab.starter.json is the fork's and is never patched.
      5. git apply -3 --index. A file the fork deleted that upstream changed, or a file both added, is reported.
      6. starterBase moves to the fetched commit and starterVersion to the upstream one; on a clean merge the rules
         outputs are regenerated, the hashes
         recomputed and tools\build.ps1 -Target CI (or -BuildCommand) runs. Changes are staged, never committed.
    With conflicts: resolve them, 'git add' the files, then run this script with -Continue.

    Exit codes: 0 merged clean (or already up to date), 1 conflicts left for the human (or the build failed),
    2 usage or internal error.

.PARAMETER Upstream
    A git remote name (e.g. upstream) or a path/URL of the starter repository.

.PARAMETER Branch
    The upstream branch. Default: main.

.PARAMETER Continue
    After resolving conflicts: check none are left, regenerate, rehash and build.

.PARAMETER BuildCommand
    PowerShell text to run instead of tools\build.ps1 -Target CI.

.PARAMETER SkipBuild
    Do not build after a clean merge.

.PARAMETER ReportPath
    Write a JSON report: status, previousBase, upstreamCommit, applied, dropped[{path,upstreamPath,reason}],
    conflicts[{path,kind}].

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\upstream-merge.ps1 -Upstream upstream
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\upstream-merge.ps1 -Continue
#>
[CmdletBinding()]
param(
    [string] $Upstream,
    [string] $Branch,
    [switch] $Continue,
    [string] $BuildCommand,
    [switch] $SkipBuild,
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'fork\lib\ForkCommon.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot

$arguments = @('upstream-merge', '--root', $repoRoot)
if ($Upstream) { $arguments += @('--upstream', $Upstream) }
if ($Branch) { $arguments += @('--branch', $Branch) }
if ($Continue) { $arguments += '--continue' }
if ($SkipBuild) { $arguments += '--skip-build' }
if ($ReportPath) { $arguments += @('--report', $ReportPath) }
# Free text travels in the environment: Windows PowerShell 5.1 mangles quotes inside native-command arguments.
if ($BuildCommand) { [Environment]::SetEnvironmentVariable($ForkBuildCommandEnv, $BuildCommand, 'Process') }

Invoke-ForkCli $repoRoot $arguments
exit $LASTEXITCODE
