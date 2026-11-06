<#
.SYNOPSIS
    Proves a module is deletable: removes it from a temp copy of the repo and checks that the solution still builds
    and its tests still pass (build plan phase 7 exit criterion; ADR-004).

.DESCRIPTION
    Never touches the repo it runs from. Steps, in order (the first failure stops the run):

      copy            copies the repo into <WorkDir>\repo: the working tree as git sees it (tracked plus untracked,
                      non-ignored files, so no bin/obj/node_modules and no gitignored local secrets), or with
                      -Revision the files of that commit (git archive), then the -Overlay paths from the working tree
      node-modules    a junction <copy>\node_modules -> <source>\node_modules, or `npm ci` in the copy with -NpmCi
      probe           -DummyModule only: scaffolds <Name> (tools\scaffold\new-module.mjs) and registers it with
                      module markers in app.routes.ts and the host project and with <camelName>.* translation keys
                      (tools\fork\fork.mjs add-probe), so the removal is proven before a real sample exists
      remove          tools\remove-module.ps1 -Name <Name> of the copy, after the removal guard (below)
      verify-removed  tools\fork\fork.mjs verify-removed: no folder, solution/host line, marker or key is left
      dotnet-build    dotnet build codelab-starter.slnx -warnaserror --artifacts-path <copy>\artifacts (inside the copy:
                      the architecture tests find the repo root by walking up from their output folder)
      backend-tests   dotnet test --no-build of every backend\**\*.Tests.csproj the solution lists (CODELAB_TEST_SQL
                      names the SQL Server of the integration tests; default (localdb)\MSSQLLocalDB)
      frontend-tests  npx vitest run in frontend\starter-web
      frontend-build  npx ng build in frontend\starter-web

    Removal guard: the work folder must not be the source repo, inside it, or a folder that contains it; an existing
    work folder must be empty or carry this script's marker file (it is then cleared). Right before the removal the
    copy root is checked again: never the source root, never a folder with git metadata.

    The work folder is deleted after a passed run (the junction first, never its target) unless -KeepCopy; after a
    failure it is kept for diagnosis.

    Exit codes: 0 passed; 1 a check failed (remove, verify-removed, build or tests; the failing step is named);
    2 usage error, guard refusal or setup failure (copy, node-modules, probe, missing toolchain).

.PARAMETER Name
    The PascalCase module name, e.g. ServiceRequests.
.PARAMETER WorkDir
    The temp work folder. Default: %TEMP%\codelab-module-deletability-<Name>.
.PARAMETER SourceRoot
    The repo to copy. Default: the repo this script belongs to.
.PARAMETER Revision
    Copy the files of this commit instead of the working tree.
.PARAMETER Overlay
    With -Revision: repo-relative files or folders copied from the working tree over the revision (e.g. tools\fork).
.PARAMETER DummyModule
    Scaffold and register <Name> in the copy before removing it (proves the mechanics without a real module).
.PARAMETER NpmCi
    Run npm ci in the copy instead of linking the source node_modules.
.PARAMETER SkipBackendTests
    Skip the backend-tests step (reported as SKIPPED).
.PARAMETER SkipFrontend
    Skip frontend-tests and frontend-build (reported as SKIPPED).
.PARAMETER KeepCopy
    Keep the work folder after a passed run too.
.PARAMETER PlanOnly
    Validate the arguments and the guard, print the plan and exit 0 without creating anything.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ci\module-deletability.ps1 -Name ServiceRequests
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ci\module-deletability.ps1 -Name DeletionProbe -DummyModule
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Name,
    [string] $WorkDir,
    [string] $SourceRoot,
    [string] $Revision,
    [string[]] $Overlay = @(),
    [switch] $DummyModule,
    [switch] $NpmCi,
    [switch] $SkipBackendTests,
    [switch] $SkipFrontend,
    [switch] $KeepCopy,
    [switch] $PlanOnly,
    [Parameter(ValueFromRemainingArguments = $true)] [string[]] $UnexpectedArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\fork\lib\ForkCommon.ps1')

$ExitPassed = 0
$ExitCheckFailed = 1
$ExitUsage = 2
$RobocopyFailureThreshold = 8

$ModuleNamePattern = '^[A-Z][A-Za-z0-9]*$'
$SolutionFile = 'codelab-starter.slnx'
$RemoveModuleScript = 'tools\remove-module.ps1'
$ScaffoldModuleScript = 'tools\scaffold\new-module.mjs'
$ForkCli = 'tools\fork\fork.mjs'
$FrontendDir = 'frontend\starter-web'
$NodeModulesDir = 'node_modules'
$GitMetadata = '.git'
$WorkDirMarker = '.codelab-module-deletability'
$WorkDirPrefix = 'codelab-module-deletability-'
$CopyDirName = 'repo'
$ArtifactsDirName = 'artifacts'
$RevisionArchiveName = 'revision.tar'
$BackendTestProject = '<Project\s+Path="(backend/[^"]+\.Tests\.csproj)"'
$DotnetOverrideEnvVar = 'CODELAB_DOTNET'
$PrivateDotnet = 'E:\codelab-tools\dotnet10\dotnet.exe'
$TestSqlEnvVar = 'CODELAB_TEST_SQL'
$DefaultTestSql = '(localdb)\MSSQLLocalDB'
$NpxCommand = 'npx.cmd'
$NpmCommand = 'npm.cmd'
$TarCommand = 'tar.exe'

$StepKind = @{ Setup = 'setup'; Check = 'check' }
$StepStatus = @{ Passed = 'PASSED'; Failed = 'FAILED'; Skipped = 'SKIPPED' }

$Usage = 'Usage: tools\ci\module-deletability.ps1 -Name <PascalName> [-WorkDir <dir>] [-SourceRoot <repo>] [-Revision <rev> [-Overlay <path[]>]] [-DummyModule] [-NpmCi] [-SkipBackendTests] [-SkipFrontend] [-KeepCopy] [-PlanOnly]'

class DeletabilityUsageError : System.Exception {
    DeletabilityUsageError([string] $message) : base($message) {}
}

function Stop-Usage([string] $Message) { throw [DeletabilityUsageError]::new($Message) }

function Get-FullPath([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

# True when $Child is $Parent or inside it (case-insensitive, whole path segments).
function Test-PathWithin([string] $Child, [string] $Parent) {
    if ([string]::Equals($Child, $Parent, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $Child.StartsWith($Parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-Arguments {
    if ($UnexpectedArguments) { Stop-Usage "Unknown argument(s): $($UnexpectedArguments -join ' ')" }
    if (-not $Name) { Stop-Usage '-Name is required.' }
    if ($Name -cnotmatch $ModuleNamePattern) { Stop-Usage "'$Name' is not a module name (PascalCase letters and digits, e.g. ServiceRequests)." }
    $overlayPaths = @(Split-ForkList $Overlay)
    if ($overlayPaths.Count -gt 0 -and -not $Revision) { Stop-Usage '-Overlay needs -Revision (the working tree copy already holds every file).' }
    foreach ($path in $overlayPaths) {
        if ([IO.Path]::IsPathRooted($path) -or $path -match '(^|[\\/])\.\.([\\/]|$)') { Stop-Usage "-Overlay '$path' must be a repo-relative path inside the repo." }
    }
}

function Resolve-Source {
    $root = $SourceRoot
    if (-not $root) { $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
    $root = Get-FullPath $root
    foreach ($required in @($SolutionFile, $RemoveModuleScript, $ForkCli)) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $required))) { Stop-Usage "$root is not a starter repo: $required not found." }
    }
    return $root
}

# The removal guard for the work folder: never the source, inside it or around it; an existing folder must be empty
# or carry the marker (a folder this script created).
function Assert-SafeWorkDir([string] $Work, [string] $Source) {
    if (Test-PathWithin $Work $Source) { Stop-Usage "REFUSED: the work folder $Work is the source repo or inside it; removal runs only in a temp copy." }
    if (Test-PathWithin $Source $Work) { Stop-Usage "REFUSED: the work folder $Work contains the source repo $Source." }
    if (-not (Test-Path -LiteralPath $Work)) { return }
    if (-not (Test-Path -LiteralPath $Work -PathType Container)) { Stop-Usage "REFUSED: the work folder $Work is a file." }
    $hasMarker = Test-Path -LiteralPath (Join-Path $Work $WorkDirMarker)
    $isEmpty = @(Get-ChildItem -LiteralPath $Work -Force).Count -eq 0
    if (-not $hasMarker -and -not $isEmpty) {
        Stop-Usage "REFUSED: the work folder $Work exists, is not empty and was not created by this script (no $WorkDirMarker); pick another -WorkDir."
    }
}

# Checked again right before tools\remove-module.ps1 runs: the copy is never the source and never a git work tree.
function Assert-RemovalRoot([string] $CopyRoot, [string] $Source) {
    if (Test-PathWithin $CopyRoot $Source) { throw "REFUSED: removal root $CopyRoot is inside the source repo $Source." }
    if (Test-Path -LiteralPath (Join-Path $CopyRoot $GitMetadata)) { throw "REFUSED: removal root $CopyRoot has $GitMetadata; removal runs only in a temp copy." }
}

function Resolve-Dotnet {
    $override = [Environment]::GetEnvironmentVariable($DotnetOverrideEnvVar)
    if ($override) { return $override }
    if (Test-Path -LiteralPath $PrivateDotnet) { return $PrivateDotnet }
    $onPath = Get-Command 'dotnet' -ErrorAction SilentlyContinue
    if (-not $onPath) { Stop-Usage "dotnet not found: set $DotnetOverrideEnvVar or install to $PrivateDotnet." }
    return $onPath.Source
}

function Get-TestSqlServer {
    $server = [Environment]::GetEnvironmentVariable($TestSqlEnvVar)
    if ($server) { return $server }
    return "$DefaultTestSql (default; set $TestSqlEnvVar to override)"
}

# Runs a native command in a folder with its output on the console; returns its exit code.
function Invoke-Native([string] $Folder, [string] $Command, [string[]] $Arguments) {
    Write-Host "  > $Command $($Arguments -join ' ')" -ForegroundColor DarkGray
    Push-Location -LiteralPath $Folder
    try {
        & $Command @Arguments | Out-Host
        return $LASTEXITCODE
    }
    finally { Pop-Location }
}

function Remove-WorkDir([string] $Work, [string] $CopyRoot) {
    $junction = Join-Path $CopyRoot $NodeModulesDir
    if (Test-Path -LiteralPath $junction) {
        $item = Get-Item -LiteralPath $junction -Force
        # A junction is deleted as a link (non-recursive), never through it: its target is the source node_modules.
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { [IO.Directory]::Delete($junction, $false) }
    }
    if (Test-Path -LiteralPath $junction) {
        $junctionItem = Get-Item -LiteralPath $junction -Force
        if ($junctionItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Could not unlink $junction; $Work is left in place." }
    }
    & cmd.exe /d /c rmdir /s /q "`"$Work`""
}

function Copy-WorkingTree([string] $Source, [string] $CopyRoot) {
    $previous = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.Encoding]::UTF8
    try { $listing = & git -C $Source -c core.quotePath=false ls-files -z --cached --others --exclude-standard }
    finally { [Console]::OutputEncoding = $previous }
    if ($LASTEXITCODE -ne 0) { throw "git ls-files failed in $Source (the working-tree copy needs a git work tree; or pass -Revision)." }
    $files = @(($listing -join '') -split "`0" | Where-Object { $_ })
    $copied = 0
    foreach ($file in $files) {
        $from = Join-Path $Source $file
        if (-not (Test-Path -LiteralPath $from -PathType Leaf)) { continue }
        $to = Join-Path $CopyRoot $file
        [void] [IO.Directory]::CreateDirectory((Split-Path -Parent $to))
        [IO.File]::Copy($from, $to, $true)
        $copied += 1
    }
    Write-Host "  copied $copied file(s) of the working tree"
}

function Copy-Revision([string] $Source, [string] $Work, [string] $CopyRoot, [string[]] $OverlayPaths) {
    $archive = Join-Path $Work $RevisionArchiveName
    if ((Invoke-Native $Source 'git' @('archive', '--format=tar', '-o', $archive, $Revision)) -ne 0) { throw "git archive $Revision failed." }
    [void] [IO.Directory]::CreateDirectory($CopyRoot)
    if ((Invoke-Native $Work $TarCommand @('-xf', $archive, '-C', $CopyRoot)) -ne 0) { throw "Extracting $archive failed." }
    Remove-Item -LiteralPath $archive -Force
    foreach ($path in $OverlayPaths) {
        $from = Join-Path $Source $path
        if (-not (Test-Path -LiteralPath $from)) { throw "-Overlay $path not found in $Source." }
        $to = Join-Path $CopyRoot $path
        if (Test-Path -LiteralPath $from -PathType Leaf) {
            [void] [IO.Directory]::CreateDirectory((Split-Path -Parent $to))
            Copy-Item -LiteralPath $from -Destination $to -Force
            continue
        }
        $code = Invoke-Native $Source 'robocopy.exe' @($from, $to, '/E', '/XJ', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/XD', $NodeModulesDir, 'bin', 'obj')
        if ($code -ge $RobocopyFailureThreshold) { throw "robocopy of -Overlay $path failed (exit $code)." }
        $global:LASTEXITCODE = 0
    }
}

function New-Plan([string] $Source) {
    $work = $WorkDir
    if (-not $work) { $work = Join-Path ([IO.Path]::GetTempPath()) "$WorkDirPrefix$Name" }
    $work = Get-FullPath $work
    Assert-SafeWorkDir $work $Source
    return [pscustomobject]@{
        Source    = $Source
        Work      = $work
        CopyRoot  = Join-Path $work $CopyDirName
        Artifacts = Join-Path (Join-Path $work $CopyDirName) $ArtifactsDirName
        Overlay   = @(Split-ForkList $Overlay)
    }
}

# The steps in order; Run names the function that runs the step with the plan and returns $true when it passed.
function Get-Steps {
    return @(
        [pscustomobject]@{ Name = 'copy'; Kind = $StepKind.Setup; Skip = $false; Run = 'Invoke-CopyStep' },
        [pscustomobject]@{ Name = 'node-modules'; Kind = $StepKind.Setup; Skip = $false; Run = 'Invoke-NodeModulesStep' },
        [pscustomobject]@{ Name = 'probe'; Kind = $StepKind.Setup; Skip = -not $DummyModule; Run = 'Invoke-ProbeStep' },
        [pscustomobject]@{ Name = 'remove'; Kind = $StepKind.Check; Skip = $false; Run = 'Invoke-RemoveStep' },
        [pscustomobject]@{ Name = 'verify-removed'; Kind = $StepKind.Check; Skip = $false; Run = 'Invoke-VerifyRemovedStep' },
        [pscustomobject]@{ Name = 'dotnet-build'; Kind = $StepKind.Check; Skip = $false; Run = 'Invoke-DotnetBuildStep' },
        [pscustomobject]@{ Name = 'backend-tests'; Kind = $StepKind.Check; Skip = $SkipBackendTests.IsPresent; Run = 'Invoke-BackendTestsStep' },
        [pscustomobject]@{ Name = 'frontend-tests'; Kind = $StepKind.Check; Skip = $SkipFrontend.IsPresent; Run = 'Invoke-FrontendTestsStep' },
        [pscustomobject]@{ Name = 'frontend-build'; Kind = $StepKind.Check; Skip = $SkipFrontend.IsPresent; Run = 'Invoke-FrontendBuildStep' }
    )
}

function Invoke-VerifyRemovedStep($Plan) {
    $copy = $Plan.CopyRoot
    return (Invoke-Native $copy $script:Node @((Join-Path $copy $ForkCli), 'verify-removed', '--root', $copy, '--name', $Name)) -eq 0
}

function Invoke-DotnetBuildStep($Plan) {
    return (Invoke-Native $Plan.CopyRoot $script:Dotnet @('build', $SolutionFile, '-warnaserror', '-nologo', '--artifacts-path', $Plan.Artifacts)) -eq 0
}

function Invoke-FrontendTestsStep($Plan) {
    return (Invoke-Native (Join-Path $Plan.CopyRoot $FrontendDir) $NpxCommand @('vitest', 'run')) -eq 0
}

function Invoke-FrontendBuildStep($Plan) {
    return (Invoke-Native (Join-Path $Plan.CopyRoot $FrontendDir) $NpxCommand @('ng', 'build')) -eq 0
}

function Invoke-CopyStep($Plan) {
    if (Test-Path -LiteralPath $Plan.Work) { Remove-WorkDir $Plan.Work $Plan.CopyRoot }
    [void] [IO.Directory]::CreateDirectory($Plan.Work)
    Set-Content -LiteralPath (Join-Path $Plan.Work $WorkDirMarker) -Value "Created by tools\ci\module-deletability.ps1 for $Name; safe to delete." -Encoding UTF8
    if ($Revision) { Copy-Revision $Plan.Source $Plan.Work $Plan.CopyRoot $Plan.Overlay }
    else { Copy-WorkingTree $Plan.Source $Plan.CopyRoot }
    return -not (Test-Path -LiteralPath (Join-Path $Plan.CopyRoot $GitMetadata))
}

function Invoke-NodeModulesStep($Plan) {
    if ($NpmCi) { return (Invoke-Native $Plan.CopyRoot $NpmCommand @('ci', '--no-audit', '--no-fund')) -eq 0 }
    $target = Join-Path $Plan.Source $NodeModulesDir
    if (-not (Test-Path -LiteralPath $target -PathType Container)) { throw "$target not found: run npm ci in the source repo, or pass -NpmCi." }
    & cmd.exe /d /c mklink /J "`"$(Join-Path $Plan.CopyRoot $NodeModulesDir)`"" "`"$target`"" | Out-Host
    return $LASTEXITCODE -eq 0
}

function Invoke-ProbeStep($Plan) {
    $copy = $Plan.CopyRoot
    if ((Invoke-Native $copy $script:Node @((Join-Path $copy $ScaffoldModuleScript), '--name', $Name, '--root', $copy, '--skip-generate')) -ne 0) { return $false }
    return (Invoke-Native $copy $script:Node @((Join-Path $copy $ForkCli), 'add-probe', '--root', $copy, '--name', $Name)) -eq 0
}

function Invoke-RemoveStep($Plan) {
    Assert-RemovalRoot $Plan.CopyRoot $Plan.Source
    $script = Join-Path $Plan.CopyRoot $RemoveModuleScript
    return (Invoke-Native $Plan.CopyRoot 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $script, '-Name', $Name)) -eq 0
}

function Invoke-BackendTestsStep($Plan) {
    $solution = Get-Content -LiteralPath (Join-Path $Plan.CopyRoot $SolutionFile) -Raw
    $projects = @([regex]::Matches($solution, $BackendTestProject) | ForEach-Object { $_.Groups[1].Value })
    if ($projects.Count -eq 0) { throw "No backend test project found in $SolutionFile." }
    $failed = @()
    foreach ($project in $projects) {
        if ((Invoke-Native $Plan.CopyRoot $script:Dotnet @('test', $project, '--no-build', '-nologo', '--artifacts-path', $Plan.Artifacts)) -ne 0) { $failed += $project }
    }
    if ($failed.Count -gt 0) { Write-Host "  failing test project(s): $($failed -join ', ')" -ForegroundColor Red }
    return $failed.Count -eq 0
}

function Write-Plan($Plan, $Steps) {
    Write-Host "Module deletability: $Name"
    Write-Host "  source:     $($Plan.Source)$(if ($Revision) { " at $Revision" })"
    if ($Plan.Overlay.Count -gt 0) { Write-Host "  overlay:    $($Plan.Overlay -join ', ')" }
    Write-Host "  work:       $($Plan.Work)"
    Write-Host "  copy:       $($Plan.CopyRoot)"
    Write-Host "  artifacts:  $($Plan.Artifacts)"
    Write-Host "  node:       $script:Node"
    Write-Host "  dotnet:     $script:Dotnet"
    Write-Host "  test SQL:   $(Get-TestSqlServer)"
    Write-Host "  npm:        $(if ($NpmCi) { 'npm ci in the copy' } else { 'junction to the source node_modules' })"
    foreach ($step in $Steps) { Write-Host ("  step {0,-15} {1}" -f $step.Name, $(if ($step.Skip) { $StepStatus.Skipped } else { $step.Kind })) }
}

function Invoke-Steps($Plan, $Steps) {
    $results = @()
    $failure = $null
    foreach ($step in $Steps) {
        if ($step.Skip -or $failure) {
            $status = $StepStatus.Skipped
            if ($failure) { $status = '-' }
            $results += [pscustomobject]@{ Step = $step.Name; Status = $status; Seconds = 0 }
            continue
        }
        Write-Host "== $($step.Name)" -ForegroundColor Cyan
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $passed = $false
        try { $passed = [bool] (& $step.Run $Plan) }
        catch { Write-Host "  $($_.Exception.Message)" -ForegroundColor Red }
        $status = $StepStatus.Passed
        if (-not $passed) { $status = $StepStatus.Failed; $failure = $step }
        $results += [pscustomobject]@{ Step = $step.Name; Status = $status; Seconds = [math]::Round($watch.Elapsed.TotalSeconds) }
    }
    return [pscustomobject]@{ Results = $results; Failure = $failure }
}

function Write-Report($Plan, $Outcome) {
    Write-Host ''
    Write-Host "Module deletability report: $Name"
    foreach ($row in $Outcome.Results) { Write-Host ("  {0,-15} {1,-8} {2,5}s" -f $row.Step, $row.Status, $row.Seconds) }
    if ($Outcome.Failure) {
        Write-Host "RESULT: FAILED at step '$($Outcome.Failure.Name)' ($($Outcome.Failure.Kind)). The copy is kept for diagnosis: $($Plan.CopyRoot)" -ForegroundColor Red
        return
    }
    Write-Host "RESULT: PASSED - $Name removed; the solution builds and the tests pass." -ForegroundColor Green
}

function Invoke-Main {
    Assert-Arguments
    $source = Resolve-Source
    $plan = New-Plan $source
    $script:Node = Use-ForkNode
    $script:Dotnet = Resolve-Dotnet
    $env:CODELAB_DOTNET = $script:Dotnet
    $env:NG_CLI_ANALYTICS = 'false'
    $steps = Get-Steps
    Write-Plan $plan $steps
    if ($PlanOnly) { return $ExitPassed }
    $outcome = Invoke-Steps $plan $steps
    Write-Report $plan $outcome
    if ($outcome.Failure -and $outcome.Failure.Kind -eq $StepKind.Setup) { return $ExitUsage }
    if ($outcome.Failure) { return $ExitCheckFailed }
    if (-not $KeepCopy) { Remove-WorkDir $plan.Work $plan.CopyRoot }
    return $ExitPassed
}

try { $exitCode = Invoke-Main }
catch [DeletabilityUsageError] {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host $Usage
    $exitCode = $ExitUsage
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = $ExitUsage
}
exit $exitCode
