<#
.SYNOPSIS
    One build entry point for CI and humans (Windows PowerShell 5.1 and PowerShell 7).

.DESCRIPTION
    -Target CI runs the build contract, in this exact order:
      1. scrub sweep        node tools/extract/scrub-sweep.mjs --root .
      2. secret scan        node tools/extract/secret-scan.mjs --root .   (+ gitleaks when gitleaks.exe is found)
      3. rules check        node tools/rules-engine/compile.mjs --check
      4. rules tests        node --test "tools/rules-engine/tests/**/*.test.mjs"
      5. hook tests         node --test ".claude/hooks/tests/**/*.test.mjs"
      6. dotnet             <dotnet> build codelab-starter.slnx -warnaserror ; <dotnet> test codelab-starter.slnx
      7. docs gate          <dotnet> run --project tools/docs-gate -- check --root . --base <base ref>
      8. conflict markers   node tools/ci/conflict-markers.mjs --root .
      9. content rules      node tools/rules-engine/check-content.mjs --root .   (every forbidden/required rule from
                            rules.compiled.json over every tracked file: the hooks' rules, enforced in CI)
     10. scanner tests      node --test "tools/extract/tests/**/*.test.mjs" "tools/ci/tests/**/*.test.mjs"
    A step whose entry point does not exist yet FAILS with a MISSING message; nothing is skipped silently.
    All steps run (unless -FailFast) and a summary is printed; the exit code is 1 when any step failed.
    The protected (platform-owned) paths the branch changes against the base ref are listed at the end, so the
    owner sees them before merging while no host enforces CODEOWNERS.

    The base ref is -BaseRef, else $env:CODELAB_BASE_REF, else origin/main or main (tools/ci/base-ref.mjs). An
    unresolvable base ref fails the docs gate; it never silently compares HEAD with itself.

    -Pack packs Codelab.Build.Sdk <base>-ci.<n> (dotnet nuget push to feed\nuget) and @codelab/ng-tools
    <base>-ci.<n> (npm pack to feed\npm), pins the new SDK version in Directory.Build.props, then builds and tests
    the solution once with UseCodelabPackageSource=false and an empty NUGET_PACKAGES folder (ADR-002 feed mode).
    With -Target CI, packing runs only when every CI step passed.

    dotnet is resolved as: $env:CODELAB_DOTNET, else E:\codelab-tools\dotnet10\dotnet.exe when present, else dotnet
    on PATH.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI -Pack -CiNumber 42
#>
[CmdletBinding()]
param(
    [ValidateSet('CI', 'None')]
    [string] $Target,

    [switch] $Pack,

    # Prerelease number for -Pack. Default: $env:CODELAB_CI_NUMBER, else one above the highest ci.<n> in feed\.
    [int] $CiNumber = 0,

    # Stop at the first failing CI step instead of running all of them.
    [switch] $FailFast,

    # Git ref the branch is diffed against (docs gate DOCS005, protected-path report). Default: see .DESCRIPTION.
    [string] $BaseRef
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------
$RepoRoot              = Split-Path -Parent $PSScriptRoot
$SolutionFile          = 'codelab-starter.slnx'
$PrivateDotnetPath     = 'E:\codelab-tools\dotnet10\dotnet.exe'
$DotnetOverrideEnvVar  = 'CODELAB_DOTNET'
$CiNumberEnvVar        = 'CODELAB_CI_NUMBER'
$DocsNotNeededEnvVar   = 'CODELAB_DOCS_NOT_NEEDED'
$PrDescriptionEnvVar   = 'CODELAB_PR_DESCRIPTION'
$DocsNotNeededTrailer  = 'Docs-Not-Needed:'
$GitleaksLocalPath     = 'tools\bin\gitleaks.exe'
$NuGetFeedDir          = Join-Path $RepoRoot 'feed\nuget'
$NpmFeedDir            = Join-Path $RepoRoot 'feed\npm'
$BuildSdkProject       = Join-Path $RepoRoot 'packages\dotnet\Codelab.Build.Sdk\Codelab.Build.Sdk.csproj'
$BuildSdkPackageId     = 'Codelab.Build.Sdk'
$BuildSdkVersionProp   = 'CodelabBuildSdkBaseVersion'
$NgToolsDir            = Join-Path $RepoRoot 'packages\npm\ng-tools'
$NgToolsTarballPrefix  = 'codelab-ng-tools'
$RepoBuildProps        = Join-Path $RepoRoot 'Directory.Build.props'
$PinnedSdkVersionProp  = 'CodelabBuildSdkVersion'
$PackageSourceSwitch   = 'UseCodelabPackageSource'
$BaseRefScript         = 'tools/ci/base-ref.mjs'
$NoBaseRef             = 'NONE'
# Platform-owned paths (CODEOWNERS); kept in step with .claude/hooks/lib/constants.mjs PlatformOwnedGlobs.
$ProtectedPathPattern  = '^(rules/|packages/|\.claude/|deploy/|\.github/|docs/adr/|provenance/|tools/extract/|tools/build\.ps1$|tools/extract/[^/]+\.(txt|yaml)$|Directory\.(Build|Packages)\.|global\.json$|nuget\.config$|.*/module\.rules\.yaml$|module\.rules\.yaml$)'
$CiLabel               = 'ci'
# Test files are named *.test.mjs. Node 24 'node --test <dir>' treats the dir as a script, so a glob is passed.
$TestFilePattern       = '*.test.mjs'
$ExitSuccess           = 0
$ExitFailure           = 1

# ---------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------
function Write-Banner([string] $Text) {
    Write-Host ''
    Write-Host ('=' * 100) -ForegroundColor DarkCyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ('=' * 100) -ForegroundColor DarkCyan
}

function Resolve-Dotnet {
    $override = [Environment]::GetEnvironmentVariable($DotnetOverrideEnvVar)
    if ($override) {
        if (-not (Test-Path -LiteralPath $override)) { throw "$DotnetOverrideEnvVar points to '$override', which does not exist." }
        return $override
    }
    if (Test-Path -LiteralPath $PrivateDotnetPath) { return $PrivateDotnetPath }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw "dotnet not found: set $DotnetOverrideEnvVar, install to $PrivateDotnetPath, or put dotnet on PATH."
}

function Resolve-Gitleaks {
    $local = Join-Path $RepoRoot $GitleaksLocalPath
    if (Test-Path -LiteralPath $local) { return $local }
    $onPath = Get-Command gitleaks -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

# Runs a native command; returns ONLY its exit code (stdout goes to the host, not the pipeline).
# Native stderr is never turned into a terminating error.
function Invoke-Native([string] $Exe, [string[]] $Arguments) {
    Write-Host "> $Exe $($Arguments -join ' ')" -ForegroundColor DarkGray
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments | Out-Host
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Get-MissingPaths([string[]] $RelativePaths) {
    return $RelativePaths | Where-Object { -not (Test-Path -LiteralPath (Join-Path $RepoRoot $_)) }
}

function Get-TestFileCount([string] $RelativeDir) {
    $dir = Join-Path $RepoRoot $RelativeDir
    if (-not (Test-Path -LiteralPath $dir)) { return 0 }
    return @(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter $TestFilePattern).Count
}

# The docs gate's module-changed check is overridden by a host-neutral trailer in the commit message or PR text.
function Set-DocsNotNeededFromTrailer {
    $sources = @()
    $prText = [Environment]::GetEnvironmentVariable($PrDescriptionEnvVar)
    if ($prText) { $sources += $prText }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $commitMessage = & git -C $RepoRoot log -1 --format=%B 2>$null
        if ($LASTEXITCODE -eq $ExitSuccess -and $commitMessage) { $sources += ($commitMessage -join "`n") }
    }
    finally { $ErrorActionPreference = $previous }

    foreach ($text in $sources) {
        foreach ($line in ($text -split "`r?`n")) {
            if ($line.StartsWith($DocsNotNeededTrailer)) {
                $reason = $line.Substring($DocsNotNeededTrailer.Length).Trim()
                if ($reason) {
                    [Environment]::SetEnvironmentVariable($DocsNotNeededEnvVar, $reason)
                    Write-Host "Docs-Not-Needed trailer found: $reason" -ForegroundColor Yellow
                    return
                }
            }
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# CI steps. Each returns $true (pass) or $false (fail) and explains why it failed.
# ---------------------------------------------------------------------------------------------------------------
function Test-EntryPoints([string[]] $RelativePaths, [string] $Owner) {
    # @() at the call site: PowerShell unrolls an empty returned array to $null.
    $missing = @(Get-MissingPaths $RelativePaths)
    if ($missing.Count -eq 0) { return $true }
    foreach ($path in $missing) {
        Write-Host "MISSING: $path - entry point not built yet ($Owner)." -ForegroundColor Red
    }
    return $false
}

function Invoke-NodeScriptStep([string] $Script, [string[]] $Arguments, [string] $Owner) {
    if (-not (Test-EntryPoints @($Script) $Owner)) { return $false }
    return (Invoke-Native 'node' (@($Script) + $Arguments)) -eq $ExitSuccess
}

function Invoke-NodeTestStep([string[]] $TestDirs, [string] $Owner) {
    if (-not (Test-EntryPoints $TestDirs $Owner)) { return $false }
    foreach ($testDir in $TestDirs) {
        if ((Get-TestFileCount $testDir) -eq 0) {
            Write-Host "MISSING: no test files ($TestFilePattern) under $testDir ($Owner)." -ForegroundColor Red
            return $false
        }
    }
    $patterns = @($TestDirs | ForEach-Object { "$_/**/$TestFilePattern" })
    return (Invoke-Native 'node' (@('--test') + $patterns)) -eq $ExitSuccess
}

function Invoke-SecretScanStep {
    $ownScanPassed = Invoke-NodeScriptStep 'tools/extract/secret-scan.mjs' @('--root', '.') 'tools/extract'
    $gitleaks = Resolve-Gitleaks
    if (-not $gitleaks) {
        Write-Host 'gitleaks not found (PATH or tools\bin\gitleaks.exe): skipped; the built-in secret scan is the gate.' -ForegroundColor Yellow
        return $ownScanPassed
    }
    $gitleaksPassed = (Invoke-Native $gitleaks @('detect', '--no-git', '--source', '.', '--redact')) -eq $ExitSuccess
    return ($ownScanPassed -and $gitleaksPassed)
}

function Invoke-DotnetStep([string] $Dotnet) {
    if (-not (Test-EntryPoints @($SolutionFile) 'repo skeleton')) { return $false }
    if ((Invoke-Native $Dotnet @('build', $SolutionFile, '-warnaserror')) -ne $ExitSuccess) { return $false }
    return (Invoke-Native $Dotnet @('test', $SolutionFile)) -eq $ExitSuccess
}

# Returns the base ref to diff against, $NoBaseRef for a repository without commits, or $null (reason printed).
function Resolve-BaseRef {
    $arguments = @($BaseRefScript, '--root', '.')
    if ($BaseRef) { $arguments += @('--base', $BaseRef) }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & node @arguments
        if ($LASTEXITCODE -ne $ExitSuccess) { return $null }
        return ([string] ($output | Select-Object -Last 1)).Trim()
    }
    finally { $ErrorActionPreference = $previous }
}

function Invoke-DocsGateStep([string] $Dotnet) {
    if (-not (Test-EntryPoints @('tools/docs-gate', $BaseRefScript) 'tools/docs-gate')) { return $false }
    Set-DocsNotNeededFromTrailer
    $baseRef = Resolve-BaseRef
    if (-not $baseRef) {
        Write-Host 'Docs gate cannot run the module-changed check without a base ref (see the message above).' -ForegroundColor Red
        return $false
    }
    $arguments = @('run', '--project', 'tools/docs-gate', '--', 'check', '--root', '.')
    if ($baseRef -eq $NoBaseRef) {
        Write-Host 'No commit yet: the docs gate compares the working tree only.' -ForegroundColor Yellow
    }
    else {
        Write-Host "Docs gate base ref: $baseRef"
        $arguments += @('--base', $baseRef)
    }
    return (Invoke-Native $Dotnet $arguments) -eq $ExitSuccess
}

# Informational: lists platform-owned paths the branch changes, so the owner reviews them before a local merge.
function Write-ProtectedPathReport {
    $baseRef = Resolve-BaseRef
    if (-not $baseRef -or $baseRef -eq $NoBaseRef) { return }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $mergeBase = (& git merge-base $baseRef HEAD 2>$null | Select-Object -First 1)
        if (-not $mergeBase) { return }
        $changed = @(& git diff --name-only $mergeBase 2>$null) + @(& git ls-files --others --exclude-standard 2>$null)
    }
    finally { $ErrorActionPreference = $previous }
    $protected = @($changed | Where-Object { $_ -match $ProtectedPathPattern } | Sort-Object -Unique)
    if ($protected.Count -eq 0) { return }
    Write-Banner "Protected paths changed against $baseRef (platform-owner review before merge)"
    $protected | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}

function Invoke-CiTarget([string] $Dotnet) {
    $steps = @(
        @{ Name = '1 scrub sweep';      Run = { Invoke-NodeScriptStep 'tools/extract/scrub-sweep.mjs' @('--root', '.') 'tools/extract' } },
        @{ Name = '2 secret scan';      Run = { Invoke-SecretScanStep } },
        @{ Name = '3 rules check';      Run = { Invoke-NodeScriptStep 'tools/rules-engine/compile.mjs' @('--check') 'tools/rules-engine' } },
        @{ Name = '4 rules tests';      Run = { Invoke-NodeTestStep 'tools/rules-engine/tests' 'tools/rules-engine' } },
        @{ Name = '5 hook tests';       Run = { Invoke-NodeTestStep '.claude/hooks/tests' '.claude/hooks' } },
        @{ Name = '6 dotnet build+test'; Run = { Invoke-DotnetStep $Dotnet } },
        @{ Name = '7 docs gate';        Run = { Invoke-DocsGateStep $Dotnet } },
        @{ Name = '8 conflict markers'; Run = { Invoke-NodeScriptStep 'tools/ci/conflict-markers.mjs' @('--root', '.') 'tools/ci' } },
        @{ Name = '9 content rules';    Run = { Invoke-NodeScriptStep 'tools/rules-engine/check-content.mjs' @('--root', '.') 'tools/rules-engine' } },
        @{ Name = '10 scanner tests';   Run = { Invoke-NodeTestStep @('tools/extract/tests', 'tools/ci/tests') 'tools/extract + tools/ci' } }
    )

    $results = New-Object System.Collections.Generic.List[object]
    foreach ($step in $steps) {
        Write-Banner "CI step $($step.Name)"
        $passed = $false
        try { $passed = [bool](& $step.Run | Select-Object -Last 1) }
        catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red }
        $results.Add([pscustomobject]@{ Step = $step.Name; Result = $(if ($passed) { 'PASS' } else { 'FAIL' }) })
        if (-not $passed -and $FailFast) { break }
    }

    Write-ProtectedPathReport
    Write-Banner 'CI summary'
    $results | Format-Table -AutoSize | Out-String | Write-Host
    return -not ($results | Where-Object { $_.Result -eq 'FAIL' })
}

# ---------------------------------------------------------------------------------------------------------------
# Pack
# ---------------------------------------------------------------------------------------------------------------
function Get-HighestCiNumber {
    $pattern = "-$CiLabel\.(\d+)\.(nupkg|tgz)$"
    $numbers = @(Get-ChildItem -LiteralPath $NuGetFeedDir, $NpmFeedDir -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.Name -match $pattern) { [int] $Matches[1] } })
    if ($numbers.Count -eq 0) { return 0 }
    return ($numbers | Measure-Object -Maximum).Maximum
}

function Resolve-CiNumber {
    if ($CiNumber -gt 0) { return $CiNumber }
    $fromEnv = [Environment]::GetEnvironmentVariable($CiNumberEnvVar)
    if ($fromEnv) { return [int] $fromEnv }
    return (Get-HighestCiNumber) + 1
}

function Get-BuildSdkBaseVersion {
    [xml] $project = Get-Content -LiteralPath $BuildSdkProject -Raw
    $node = $project.SelectSingleNode("//$BuildSdkVersionProp")
    if (-not $node) { throw "$BuildSdkVersionProp not found in $BuildSdkProject." }
    return $node.InnerText.Trim()
}

function Get-NgToolsBaseVersion {
    $manifest = Get-Content -LiteralPath (Join-Path $NgToolsDir 'package.json') -Raw | ConvertFrom-Json
    return $manifest.version
}

function Invoke-PackBuildSdk([string] $Dotnet, [int] $Number) {
    $version = "$(Get-BuildSdkBaseVersion)-$CiLabel.$Number"
    $stage = Join-Path ([IO.Path]::GetTempPath()) "codelab-pack-nuget-$([guid]::NewGuid().ToString('N'))"
    try {
        $packArgs = @('pack', $BuildSdkProject, '-c', 'Release', '-o', $stage, "-p:PackageVersion=$version")
        if ((Invoke-Native $Dotnet $packArgs) -ne $ExitSuccess) { throw "dotnet pack of $BuildSdkPackageId $version failed." }
        $nupkg = Join-Path $stage "$BuildSdkPackageId.$version.nupkg"
        if ((Invoke-Native $Dotnet @('nuget', 'push', $nupkg, '--source', $NuGetFeedDir)) -ne $ExitSuccess) {
            throw "dotnet nuget push of $BuildSdkPackageId $version to $NuGetFeedDir failed."
        }
        Write-Host "Packed $BuildSdkPackageId $version -> $NuGetFeedDir" -ForegroundColor Green
    }
    finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
}

# npm pack runs on a staged copy so the source package.json version is never edited.
function Invoke-PackNgTools([int] $Number) {
    $version = "$(Get-NgToolsBaseVersion)-$CiLabel.$Number"
    $stage = Join-Path ([IO.Path]::GetTempPath()) "codelab-pack-npm-$([guid]::NewGuid().ToString('N'))"
    try {
        Copy-Item -LiteralPath $NgToolsDir -Destination $stage -Recurse
        $setVersion = "const f=process.argv[1];const fs=require('fs');const p=JSON.parse(fs.readFileSync(f,'utf8'));p.version=process.argv[2];fs.writeFileSync(f,JSON.stringify(p,null,2)+'\n');"
        if ((Invoke-Native 'node' @('-e', $setVersion, (Join-Path $stage 'package.json'), $version)) -ne $ExitSuccess) {
            throw "Could not stamp version $version on the staged ng-tools package."
        }
        if ((Invoke-Native 'npm' @('pack', $stage, '--pack-destination', $NpmFeedDir)) -ne $ExitSuccess) {
            throw "npm pack of @codelab/ng-tools $version failed."
        }
        $tarball = Join-Path $NpmFeedDir "$NgToolsTarballPrefix-$version.tgz"
        if (-not (Test-Path -LiteralPath $tarball)) { throw "Expected tarball not found: $tarball" }
        Write-Host "Packed @codelab/ng-tools $version -> $tarball" -ForegroundColor Green
    }
    finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
}

# Rewrites the CodelabBuildSdkVersion pin in Directory.Build.props (text edit, so comments and layout stay).
function Set-PinnedBuildSdkVersion([string] $Version) {
    $text = [IO.File]::ReadAllText($RepoBuildProps)
    $pattern = "(<$PinnedSdkVersionProp\b[^>]*>)[^<]*(</$PinnedSdkVersionProp>)"
    if ($text -notmatch $pattern) { throw "$PinnedSdkVersionProp pin not found in $RepoBuildProps." }
    $updated = [regex]::Replace($text, $pattern, "`${1}$Version`${2}")
    [IO.File]::WriteAllText($RepoBuildProps, $updated, (New-Object Text.UTF8Encoding($false)))
    Write-Host "Pinned $PinnedSdkVersionProp = $Version in Directory.Build.props" -ForegroundColor Green
}

# ADR-002 feed mode: build and test exactly as a fork does, from the feed only, with a cold package cache.
function Invoke-FeedModeBuild([string] $Dotnet) {
    Write-Banner "Feed-mode build ($PackageSourceSwitch=false, isolated NUGET_PACKAGES)"
    $packages = Join-Path ([IO.Path]::GetTempPath()) "codelab-feedmode-$([guid]::NewGuid().ToString('N'))"
    $previousPackages = $env:NUGET_PACKAGES
    $env:NUGET_PACKAGES = $packages
    try {
        $switch = "-p:$PackageSourceSwitch=false"
        if ((Invoke-Native $Dotnet @('build', $SolutionFile, '-warnaserror', $switch)) -ne $ExitSuccess) {
            throw 'Feed-mode build failed: the pinned Codelab.Build.Sdk does not build the solution.'
        }
        if ((Invoke-Native $Dotnet @('test', $SolutionFile, '--no-build', $switch)) -ne $ExitSuccess) {
            throw 'Feed-mode tests failed.'
        }
    }
    finally {
        $env:NUGET_PACKAGES = $previousPackages
        Remove-Item -LiteralPath $packages -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-PackTarget([string] $Dotnet) {
    $number = Resolve-CiNumber
    Write-Banner "Pack (ci.$number)"
    New-Item -ItemType Directory -Force -Path $NuGetFeedDir, $NpmFeedDir | Out-Null
    Invoke-PackBuildSdk $Dotnet $number
    Invoke-PackNgTools $number
    Set-PinnedBuildSdkVersion "$(Get-BuildSdkBaseVersion)-$CiLabel.$Number"
    Invoke-FeedModeBuild $Dotnet
}

# ---------------------------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------------------------
if (-not $Target) { $Target = $(if ($Pack) { 'None' } else { 'CI' }) }

Push-Location $RepoRoot
try {
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $dotnet = Resolve-Dotnet
    Write-Host "Repo:   $RepoRoot"
    Write-Host "dotnet: $dotnet ($(& $dotnet --version))"
    Write-Host "node:   $(& node --version)"

    if ($Target -eq 'CI') {
        if (-not (Invoke-CiTarget $dotnet)) {
            Write-Host 'CI FAILED.' -ForegroundColor Red
            exit $ExitFailure
        }
        Write-Host 'CI PASSED.' -ForegroundColor Green
    }
    if ($Pack) { Invoke-PackTarget $dotnet }
    exit $ExitSuccess
}
catch {
    Write-Host "BUILD FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit $ExitFailure
}
finally {
    Pop-Location
}
