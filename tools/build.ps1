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
                            --no-build --logger trx ; FAILS when 0 tests of Starter.Integration.Tests ran (the
                            integration tier runs on LocalDB or CODELAB_TEST_SQL; a skipped tier never looks green)
      7. docs gate          <dotnet> run --project tools/docs-gate -- check --root . --base <base ref>
      8. conflict markers   node tools/ci/conflict-markers.mjs --root .
      9. content rules      node tools/rules-engine/check-content.mjs --root .   (every forbidden/required rule from
                            rules.compiled.json over every tracked file: the hooks' rules, enforced in CI)
     10. scanner tests      node --test over tools/extract/tests, tools/ci/tests, tools/vectors/tests,
                            deploy/handover/tests, tools/fork/tests, tools/scaffold/tests (with the dogfood test),
                            tools/devseed/tests and tools/run-local (**/*.test.mjs)
     11. npm build+lint+test npm ci (only when node_modules is missing) ; npm run build --workspaces ;
                            node tools/ci/lint-packages.mjs (ESLint: magic numbers, module boundaries) ;
                            npm test --workspaces
     12. vectors verify     node tools/vectors/verify-vectors.mjs --prefix CODELABTEST ; ... --prefix ALTPFX
     13. package integration test   packages/samples, built the way a fork consumes the packages: feed only.
                            Refuses a Codelab ProjectReference in a sample and a sample in the solution. Takes the
                            versions pinned in packages/samples/Codelab.Versions.props and the ng-security tarball named
                            by packages/samples/ng-security-consumer/package.json; when one is not in feed\ (a fresh
                            clone), packs a ci prerelease into a TEMPORARY feed (never feed\, never the
                            Directory.Build.props pin) and tests against that instead. Then
                            <dotnet> build + test of Package.Sample.Tests with an empty temporary NUGET_PACKAGES, and
                            npm ci + npm test of ng-security-consumer.
     14. handover offline   deploy/handover/New-HandoverKit.ps1 builds a kit for Package.Sample.Tests and
                            ng-security-consumer (the exit-criterion targets, at the versions step 13 tested), then
                            Test-HandoverKit.ps1 restores and builds both from the kit alone: empty package folders and
                            HTTP blocked through an unroutable proxy. A simulation; the VM run with the network adapter
                            disabled stays an owner step (ADR-008).
     15. provenance origins node tools/extract/check-origins.mjs --root .   (every harvested file is rewritten with an
                            existing target, or staged with a note or a deferred field)
     16. generated drift    <dotnet> run --project backend/tools/Starter.DocsGen -- --out docs/generated --check ;
                            npm run api:gen:check --workspace frontend/starter-web
     17. frontend lint      npm run lint --workspace frontend/starter-web   (ESLint, Stylelint, i18n parity; step 11
                            lints packages/npm only)
     18. fork-model test    tools\fork\Test-ForkModel.ps1 -RemoveModule Demo   (ADR-003; all in %TEMP%, the repo is only read)
    A step whose entry point does not exist yet FAILS with a MISSING message; nothing is skipped silently.
    All steps run (unless -FailFast) and a summary is printed; the exit code is 1 when any step failed.
    The protected (platform-owned) paths the branch changes against the base ref are listed at the end, so the
    owner sees them before merging while no host enforces CODEOWNERS.

    The base ref is -BaseRef, else $env:CODELAB_BASE_REF, else origin/main or main (tools/ci/base-ref.mjs). An
    unresolvable base ref fails the docs gate; it never silently compares HEAD with itself.

    -Pack packs EVERY package listed in packages/versions.json (the single version source): the NuGet packages
    (Codelab.Build.Sdk with its analyzers, Codelab.Security, .Observability, .Errors, .Integration, .Testing) with
    dotnet pack, and the npm packages (@codelab/ng-security, @codelab/ng-tools) with npm pack on a %TEMP% staging
    copy whose package.json is stamped (dist is built first). All packages of one run are <version>-ci.<n> with the
    SAME <n>. Nothing reaches feed\ until every package is packed and the Codelab dependencies inside each .nupkg
    match the plan. Then the new SDK version is pinned in Directory.Build.props and the solution is built and tested
    once with UseCodelabPackageSource=false and an empty NUGET_PACKAGES folder (ADR-002 feed mode).
    -Pack -Release packs the exact versions.json versions instead. A package whose exact version is already in feed\
    and whose packages/CHANGELOG.md section has no [Unreleased] entry is released and unchanged: it is skipped. One
    that is in the feed but still has an [Unreleased] entry is REFUSED before anything is packed (bump its version):
    a published version is immutable.
    -Target CI never writes to feed\ or Directory.Build.props: when a sample pin is missing, step 13 packs into a
    temporary feed that is removed when CI ends.
    -Pack -DryRun prints the plan and runs the same refusal checks, but packs and changes nothing.
    With -Target CI, packing runs only when every CI step passed.

    dotnet is resolved as: $env:CODELAB_DOTNET, else E:\codelab-tools\dotnet10\dotnet.exe when present, else dotnet
    on PATH. Node: $env:CODELAB_NODE_DIR, else E:\codelab-tools\node24 when present, is put first on PATH for this
    process; otherwise node and npm come from PATH.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI -Pack -CiNumber 42
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack -Release -DryRun
#>
[CmdletBinding()]
param(
    [ValidateSet('CI', 'None')]
    [string] $Target,

    [switch] $Pack,

    # With -Pack: pack the exact packages/versions.json versions (no -ci.<n>); refused when one is already in the feed.
    [switch] $Release,

    # With -Pack: print the plan and run the refusal checks only; nothing is packed and no file is changed.
    [switch] $DryRun,

    # With -Pack -DryRun: check against another feed folder (holding nuget\ and npm\). Default: the repo's feed\.
    [string] $FeedDir,

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
$PrivateNodeDir        = 'E:\codelab-tools\node24'
$NodeDirEnvVar         = 'CODELAB_NODE_DIR'
$NodeExecutableName    = 'node.exe'
$CiNumberEnvVar        = 'CODELAB_CI_NUMBER'
$DocsNotNeededEnvVar   = 'CODELAB_DOCS_NOT_NEEDED'
$PrDescriptionEnvVar   = 'CODELAB_PR_DESCRIPTION'
$DocsNotNeededTrailer  = 'Docs-Not-Needed:'
$GitleaksLocalPath     = 'tools\bin\gitleaks.exe'
$DefaultFeedDir        = Join-Path $RepoRoot 'feed'
$NuGetFeedFolder       = 'nuget'
$NpmFeedFolder         = 'npm'
$PackageVersionsScript = 'tools/ci/package-versions.mjs'
$BuildSdkPackageId     = 'Codelab.Build.Sdk'
$NuGetKind             = 'nuget'
$NpmKind               = 'npm'
$PackConfiguration     = 'Release'
$NpmDistFolder         = 'dist'
$NodeModulesFolder     = 'node_modules'
$NpmWorkspaceEntries   = @('package.json', 'package-lock.json', 'packages/npm')
$VectorsScript         = 'tools/vectors/verify-vectors.mjs'
# CODELABTEST is the prefix the vectors are pinned to; ALTPFX proves a fork with another prefix still verifies.
$VectorPrefixes        = @('CODELABTEST', 'ALTPFX')
# Step 13: the package integration test (packages/samples) and what it is built from.
$SamplesFolder         = 'packages/samples'
$SampleTestsProject    = 'packages/samples/Package.Sample.Tests/Package.Sample.Tests.csproj'
$SampleVersionsProps   = 'packages/samples/Codelab.Versions.props'
$SampleNpmConsumer     = 'packages/samples/ng-security-consumer'
$PackageLintScript     = 'tools/ci/lint-packages.mjs'
$SampleNuGetConfig     = 'packages/samples/nuget.config'
# What a temporary prerelease root mirrors for the sample (step 13), and what it never copies.
$SampleTreeEntries     = @('packages/samples', 'packages/vectors', 'global.json', '.editorconfig')
$SampleTreeSkipPattern = '(^|[\\/])(bin|obj|node_modules)([\\/]|$)'
$SampleEntryPoints     = @($SampleNuGetConfig, 'packages/samples/Directory.Build.props',
    'packages/samples/Directory.Packages.props', $SampleVersionsProps,
    'packages/samples/Package.Sample.Api/Package.Sample.Api.csproj', $SampleTestsProject,
    "$SampleNpmConsumer/package.json", "$SampleNpmConsumer/package-lock.json")
$NgSecurityPackageId   = '@codelab/ng-security'
# Step 14: the handover kit scripts (ADR-008).
$NewHandoverKitScript  = 'deploy/handover/New-HandoverKit.ps1'
$TestHandoverKitScript = 'deploy/handover/Test-HandoverKit.ps1'
$NpmFileSpecPrefix     = 'file:'
$VersionPropertySuffix = 'Version'
$VectorsFolder         = 'packages/vectors'
$VectorsDirEnvVar      = 'CODELAB_VECTORS_DIR'
$CodelabReferencePattern = '(?i)Codelab\.|packages[\/]dotnet'
$RepoBuildProps        = Join-Path $RepoRoot 'Directory.Build.props'
$PinnedSdkVersionProp  = 'CodelabBuildSdkVersion'
$PackageSourceSwitch   = 'UseCodelabPackageSource'
$BaseRefScript         = 'tools/ci/base-ref.mjs'
$NoBaseRef             = 'NONE'
# Platform-owned paths (CODEOWNERS); kept in step with .claude/hooks/lib/constants.mjs PlatformOwnedGlobs.
$ProtectedPathPattern  = '^(rules/|packages/|\.claude/|deploy/|\.github/|docs/adr/|docs/standards/|provenance/|tools/extract/|tools/build\.ps1$|frontend/starter-web/(eslint\.config\.mjs$|stylelint\.config\.mjs$|tools/lint/)|(.*/)?(Directory\.Build\.[^/]*$|Directory\.Packages\.props$|global\.json$|nuget\.config$|\.editorconfig$|module\.rules\.yaml$))'
# Step 6: the integration tier must run (counted from the TRX results); it is never skipped silently.
$IntegrationTestsProject = 'backend/tests/Starter.Integration.Tests/Starter.Integration.Tests.csproj'
$IntegrationTestAssembly = 'Starter.Integration.Tests.dll'
$TrxLogger             = 'trx'
$TrxFilePattern        = '*.trx'
# Steps 15 to 18.
$CheckOriginsScript    = 'tools/extract/check-origins.mjs'
$DocsGenProject        = 'backend/tools/Starter.DocsGen'
$GeneratedDocsDir      = 'docs/generated'
$FrontendWorkspace     = 'frontend/starter-web'
$ForkModelScript       = 'tools/fork/Test-ForkModel.ps1'
$ForkModelModule       = 'Demo'
# Test files are named *.test.mjs. Node 24 'node --test <dir>' treats the dir as a script, so a glob is passed.
$TestFilePattern       = '*.test.mjs'
$ExitSuccess           = 0
$ExitFailure           = 1
# npm is a .cmd shim on Windows; naming it avoids PowerShell running npm.ps1 instead.
$NpmCommand            = $(if ($env:OS -eq 'Windows_NT') { 'npm.cmd' } else { 'npm' })

# Temporary feeds CI step 13 packed into; removed when the CI target ends.
$script:TemporaryFeeds = New-Object System.Collections.Generic.List[string]

if (-not $FeedDir) { $FeedDir = $DefaultFeedDir }
if (-not [IO.Path]::IsPathRooted($FeedDir)) { $FeedDir = Join-Path (Get-Location).Path $FeedDir }
$FeedDir      = [IO.Path]::GetFullPath($FeedDir)
$NuGetFeedDir = Join-Path $FeedDir $NuGetFeedFolder
$NpmFeedDir   = Join-Path $FeedDir $NpmFeedFolder

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

# Puts the pinned Node first on PATH for this process (ADR-005): $env:CODELAB_NODE_DIR, else the private install.
function Use-PinnedNode {
    $override = [Environment]::GetEnvironmentVariable($NodeDirEnvVar)
    $nodeDir = $null
    if ($override) {
        if (-not (Test-Path -LiteralPath (Join-Path $override $NodeExecutableName))) {
            throw "$NodeDirEnvVar points to '$override', which has no $NodeExecutableName."
        }
        $nodeDir = $override
    }
    elseif (Test-Path -LiteralPath (Join-Path $PrivateNodeDir $NodeExecutableName)) { $nodeDir = $PrivateNodeDir }
    if ($nodeDir) { $env:PATH = "$nodeDir$([IO.Path]::PathSeparator)$env:PATH" }
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

# Sums the executed and failed counts of every TRX file whose tests come from the named test assembly.
function Get-TrxCounters([string] $ResultsDir, [string] $AssemblyName) {
    $totals = [pscustomobject]@{ Files = 0; Executed = 0; Failed = 0 }
    foreach ($trx in @(Get-ChildItem -LiteralPath $ResultsDir -Recurse -File -Filter $TrxFilePattern -ErrorAction SilentlyContinue)) {
        [xml] $document = Get-Content -LiteralPath $trx.FullName -Raw
        $definitions = $document.TestRun.SelectNodes("//*[local-name()='UnitTest']")
        $storages = @($definitions | ForEach-Object { [IO.Path]::GetFileName($_.GetAttribute('storage')) })
        if ($storages -notcontains $AssemblyName) { continue }
        $counters = $document.TestRun.SelectSingleNode("//*[local-name()='Counters']")
        $totals.Files += 1
        $totals.Executed += [int] $counters.GetAttribute('executed')
        $totals.Failed += [int] $counters.GetAttribute('failed')
    }
    return $totals
}

# FAILS when the integration assembly ran no test: a skipped fixture or an unregistered project never looks green.
function Test-IntegrationTestsRan([string] $ResultsDir) {
    $integration = Get-TrxCounters $ResultsDir $IntegrationTestAssembly
    if ($integration.Executed -eq 0) {
        Write-Host "FAIL: 0 integration tests ran ($IntegrationTestAssembly). LocalDB (or CODELAB_TEST_SQL) must be reachable and the project must be in the solution." -ForegroundColor Red
        return $false
    }
    Write-Host "Integration tests executed: $($integration.Executed) (failed: $($integration.Failed))."
    return $true
}

function Invoke-DotnetStep([string] $Dotnet) {
    if (-not (Test-EntryPoints @($SolutionFile, $IntegrationTestsProject) 'repo skeleton + backend/tests')) { return $false }
    if ((Invoke-Native $Dotnet @('build', $SolutionFile, '-warnaserror')) -ne $ExitSuccess) { return $false }
    Write-Host "Test SQL server: $(if ($env:CODELAB_TEST_SQL) { $env:CODELAB_TEST_SQL } else { '(localdb)\MSSQLLocalDB (default)' })"
    $resultsDir = New-TempFolder 'test-results'
    try {
        $testArguments = @('test', $SolutionFile, '--no-build', '--logger', $TrxLogger, '--results-directory', $resultsDir)
        $testsPassed = (Invoke-Native $Dotnet $testArguments) -eq $ExitSuccess
        $integrationRan = Test-IntegrationTestsRan $resultsDir
        return ($testsPassed -and $integrationRan)
    }
    finally { Remove-Item -LiteralPath $resultsDir -Recurse -Force -ErrorAction SilentlyContinue }
}

# Step 11: every npm workspace builds its dist, passes the package lint and its tests. npm ci runs only when node_modules is missing.
function Invoke-NpmStep {
    if (-not (Test-EntryPoints $NpmWorkspaceEntries 'packages/npm')) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $NodeModulesFolder))) {
        if ((Invoke-Native $NpmCommand @('ci')) -ne $ExitSuccess) { return $false }
    }
    if ((Invoke-Native $NpmCommand @('run', 'build', '--workspaces')) -ne $ExitSuccess) { return $false }
    # ESLint over the package sources: base.no-magic-values and base.module-boundaries (tools/ci/lint-packages.mjs).
    if ((Invoke-Native 'node' @($PackageLintScript, '--root', '.')) -ne $ExitSuccess) { return $false }
    return (Invoke-Native $NpmCommand @('test', '--workspaces')) -eq $ExitSuccess
}

# Step 12: C# and TS produce the same canonical bytes and signatures, for the pinned prefix and a second one.
function Invoke-VectorsStep {
    if (-not (Test-EntryPoints @($VectorsScript) 'golden vectors (tools/vectors)')) { return $false }
    $allPassed = $true
    foreach ($prefix in $VectorPrefixes) {
        if ((Invoke-Native 'node' @($VectorsScript, '--prefix', $prefix)) -ne $ExitSuccess) {
            Write-Host "Vectors do not verify for prefix $prefix." -ForegroundColor Red
            $allPassed = $false
        }
    }
    return $allPassed
}

# ---------------------------------------------------------------------------------------------------------------
# Step 13: package integration test (packages/samples consumes the packages from the feed only)
# ---------------------------------------------------------------------------------------------------------------
# A sample proves the packages only while it consumes them like a fork: never through the solution, never through
# a ProjectReference to a Codelab project.
function Test-SampleBoundary {
    $passed = $true
    $solution = [IO.File]::ReadAllText((Join-Path $RepoRoot $SolutionFile))
    if ($solution -match [regex]::Escape($SamplesFolder)) {
        Write-Host "BOUNDARY: $SolutionFile lists a project under $SamplesFolder; the samples restore from the feed only." -ForegroundColor Red
        $passed = $false
    }
    $projects = Get-ChildItem -LiteralPath (Join-Path $RepoRoot $SamplesFolder) -Recurse -File -Filter '*.csproj' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' }
    foreach ($project in $projects) {
        [xml] $xml = [IO.File]::ReadAllText($project.FullName)
        foreach ($reference in $xml.SelectNodes("//*[local-name()='ProjectReference']")) {
            if ($reference.Include -match $CodelabReferencePattern) {
                Write-Host "BOUNDARY: $($project.Name) references $($reference.Include); use the Codelab package from the feed." -ForegroundColor Red
                $passed = $false
            }
        }
    }
    return $passed
}

# The MSBuild property that pins a package in Codelab.Versions.props: the id without dots, plus "Version".
function Get-VersionPropertyName([string] $PackageId) { return ($PackageId -replace '\.', '') + $VersionPropertySuffix }

# What the sample pins: @{ Nuget = @{ <package id> = <version> }; NpmTarball = <absolute path>; Override = $false }.
# Every NuGet package of packages/versions.json must have its property in Codelab.Versions.props.
function Get-SamplePins {
    [xml] $props = [IO.File]::ReadAllText((Join-Path $RepoRoot $SampleVersionsProps))
    $byProperty = @{}
    foreach ($node in $props.SelectNodes("//*[local-name()='PropertyGroup']/*")) { $byProperty[$node.LocalName] = $node.InnerText.Trim() }
    $versionsFile = [IO.File]::ReadAllText((Join-Path $RepoRoot 'packages/versions.json')) | ConvertFrom-Json
    $nuget = @{}
    foreach ($id in @($versionsFile.PSObject.Properties.Name | Where-Object { -not $_.StartsWith('@') })) {
        $property = Get-VersionPropertyName $id
        if (-not $byProperty.ContainsKey($property)) { throw "$SampleVersionsProps has no $property for $id." }
        $nuget[$id] = $byProperty[$property]
    }
    $consumer = Join-Path $RepoRoot $SampleNpmConsumer
    $manifest = [IO.File]::ReadAllText((Join-Path $consumer 'package.json')) | ConvertFrom-Json
    $spec = [string] $manifest.dependencies.$NgSecurityPackageId
    if (-not $spec.StartsWith($NpmFileSpecPrefix)) { throw "$SampleNpmConsumer must install $NgSecurityPackageId from a feed tarball ($NpmFileSpecPrefix...)." }
    $tarball = [IO.Path]::GetFullPath((Join-Path $consumer $spec.Substring($NpmFileSpecPrefix.Length)))
    return @{ Nuget = $nuget; NpmTarball = $tarball; Override = $false; Feed = $null; Root = $null }
}

# Pinned package files that are not in the feed; empty when the sample can run as pinned.
function Get-MissingSamplePins($Pins) {
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($id in $Pins.Nuget.Keys) {
        $file = Join-Path $NuGetFeedDir "$id.$($Pins.Nuget[$id]).nupkg"
        if (-not (Test-Path -LiteralPath $file)) { $missing.Add($file) }
    }
    if (-not (Test-Path -LiteralPath $Pins.NpmTarball)) { $missing.Add($Pins.NpmTarball) }
    # An empty array unrolls to $null; the caller wraps the result in @().
    return $missing.ToArray()
}

# The versions of the prerelease just packed, to override the pins with.
function Get-PrereleaseOverrides($Plan) {
    $nuget = @{}
    foreach ($entry in @($Plan.packages | Where-Object { $_.kind -eq $NuGetKind })) { $nuget[$entry.id] = $entry.version }
    $npm = $Plan.packages | Where-Object { $_.id -eq $NgSecurityPackageId } | Select-Object -First 1
    return @{ Nuget = $nuget; NpmTarball = $npm.feedFile; Override = $true; Feed = $null; Root = $null }
}

# CI never writes to feed\ or to Directory.Build.props: a missing pin is packed as a ci prerelease into a temporary
# root (removed when CI ends), so concurrent runs cannot race on the feed or the SDK pin. The root mirrors the repo
# layout the sample needs (packages\samples, packages\vectors, global.json, .editorconfig) beside its own feed\, so
# the sample's nuget.config (../../feed/nuget) finds the prerelease, and so does the MSBuild SDK resolver, which reads
# only the nuget.config files above the project. The feed-mode build of the solution belongs to -Pack, not to CI.
function New-TemporaryPrerelease([string] $Dotnet) {
    $root = New-TempFolder 'ci-feed'
    $script:TemporaryFeeds.Add($root)
    Copy-SampleTree $root
    $feed = Join-Path $root 'feed'
    $nugetDir = Join-Path $feed $NuGetFeedFolder
    $npmDir = Join-Path $feed $NpmFeedFolder
    New-Item -ItemType Directory -Force -Path $nugetDir, $npmDir | Out-Null
    $plan = Get-PackPlan (Resolve-CiNumber) $false $feed
    $stage = New-TempFolder 'pack'
    try {
        foreach ($item in (Invoke-PackAll $Dotnet $plan $stage)) {
            $target = $(if ($item.Entry.kind -eq $NuGetKind) { $nugetDir } else { $npmDir })
            Copy-Item -LiteralPath $item.File -Destination $target
            Write-Host "Packed $($item.Entry.id) $($item.Entry.version) -> $($item.Entry.feedFile) (temporary)" -ForegroundColor Green
        }
    }
    finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    return @{ Plan = $plan; Feed = $feed; Root = $root }
}

# Copies what the sample builds from into $Root at the same relative paths (no bin, obj or node_modules).
function Copy-SampleTree([string] $Root) {
    foreach ($relative in $SampleTreeEntries) {
        $source = Join-Path $RepoRoot $relative
        $target = Join-Path $Root $relative
        if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination $target; continue }
        $prefixLength = $source.TrimEnd('\', '/').Length + 1
        Get-ChildItem -LiteralPath $source -Recurse -File -Force |
            Where-Object { $_.FullName.Substring($prefixLength) -notmatch $SampleTreeSkipPattern } |
            ForEach-Object {
                $destination = Join-Path $target $_.FullName.Substring($prefixLength)
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $destination
            }
    }
}

function Remove-TemporaryFeeds {
    foreach ($feed in $script:TemporaryFeeds) { Remove-Item -LiteralPath $feed -Recurse -Force -ErrorAction SilentlyContinue }
    $script:TemporaryFeeds.Clear()
}

function Invoke-SampleDotnet([string] $Dotnet, $Versions) {
    $properties = @()
    if ($Versions.Override) { $properties = @($Versions.Nuget.Keys | ForEach-Object { "-p:$(Get-VersionPropertyName $_)=$($Versions.Nuget[$_])" }) }
    $project = $(if ($Versions.Root) { Join-Path $Versions.Root $SampleTestsProject } else { $SampleTestsProject })
    $packages = New-TempFolder 'sample-packages'
    $previousPackages = $env:NUGET_PACKAGES
    $env:NUGET_PACKAGES = $packages
    try {
        Write-Host "Sample restore from the feed only, NUGET_PACKAGES=$packages (empty)"
        if ((Invoke-Native $Dotnet (@('build', $project, '-warnaserror') + $properties)) -ne $ExitSuccess) { return $false }
        return (Invoke-Native $Dotnet (@('test', $project, '--no-build') + $properties)) -eq $ExitSuccess
    }
    finally {
        $env:NUGET_PACKAGES = $previousPackages
        Remove-Item -LiteralPath $packages -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# As pinned: npm ci + npm test in place (the lock names the feed tarball). Against a fresh prerelease: a %TEMP% copy
# installs that tarball instead (no lock), and the vectors folder is passed in CODELAB_VECTORS_DIR.
function Invoke-SampleNpm($Versions) {
    $consumer = Join-Path $RepoRoot $SampleNpmConsumer
    if (-not $Versions.Override) {
        if ((Invoke-Native $NpmCommand @('ci', '--prefix', $consumer)) -ne $ExitSuccess) { return $false }
        return (Invoke-Native $NpmCommand @('test', '--prefix', $consumer)) -eq $ExitSuccess
    }
    $copy = New-TempFolder 'sample-npm'
    $previousVectors = [Environment]::GetEnvironmentVariable($VectorsDirEnvVar)
    try {
        Get-ChildItem -LiteralPath $consumer -Force | Where-Object { $_.Name -notin @($NodeModulesFolder, 'package-lock.json') } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $copy -Recurse -Force }
        $manifestPath = Join-Path $copy 'package.json'
        $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        $manifest.dependencies.$NgSecurityPackageId = $NpmFileSpecPrefix + ($Versions.NpmTarball -replace '\\', '/')
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
        [Environment]::SetEnvironmentVariable($VectorsDirEnvVar, (Join-Path $RepoRoot $VectorsFolder))
        if ((Invoke-Native $NpmCommand @('install', '--prefix', $copy, '--no-audit')) -ne $ExitSuccess) { return $false }
        return (Invoke-Native $NpmCommand @('test', '--prefix', $copy)) -eq $ExitSuccess
    }
    finally {
        [Environment]::SetEnvironmentVariable($VectorsDirEnvVar, $previousVectors)
        Remove-Item -LiteralPath $copy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-PackageIntegrationStep([string] $Dotnet) {
    if (-not (Test-EntryPoints $SampleEntryPoints 'packages/samples')) { return $false }
    if (-not (Test-SampleBoundary)) { return $false }
    $versions = Get-SamplePins
    $missing = @(Get-MissingSamplePins $versions)
    if ($missing.Count -gt 0) {
        $missing | ForEach-Object { Write-Host "Not in the feed: $_" -ForegroundColor Yellow }
        if ($DryRun) { Write-Host 'Pinned sample versions are missing and -DryRun packs nothing.' -ForegroundColor Red; return $false }
        Write-Host 'Packing a ci prerelease of the current sources into a temporary feed; the sample is tested against it.' -ForegroundColor Yellow
        $temporary = New-TemporaryPrerelease $Dotnet
        $versions = Get-PrereleaseOverrides $temporary.Plan
        $versions.Feed = $temporary.Feed
        $versions.Root = $temporary.Root
    }
    else { Write-Host "Sample pins are in the feed: $(($versions.Nuget.Values | Sort-Object -Unique) -join ', ')" }
    $script:SampleVersions = $versions
    if (-not (Invoke-SampleDotnet $Dotnet $versions)) { return $false }
    return Invoke-SampleNpm $versions
}

# Step 14: a handover kit for the exit-criterion targets restores and builds them with no network (simulation).
function Invoke-HandoverStep {
    if (-not (Test-EntryPoints @($NewHandoverKitScript, $TestHandoverKitScript) 'deploy/handover')) { return $false }
    $versions = $script:SampleVersions
    if (-not $versions) {
        Write-Host 'Step 13 did not resolve the sample package versions, so there is no tested closure to hand over.' -ForegroundColor Red
        return $false
    }
    $properties = @()
    if ($versions.Override) { $properties = @($versions.Nuget.Keys | ForEach-Object { "$(Get-VersionPropertyName $_)=$($versions.Nuget[$_])" }) }
    $work = New-TempFolder 'handover-ci'
    $kit = Join-Path $work 'kit'
    try {
        $build = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $RepoRoot $NewHandoverKitScript),
            '-OutputPath', $kit, '-Projects', $SampleTestsProject)
        if ($versions.Feed) {
            # The consumer's lock names the pinned release tarball, which a temporary prerelease feed does not hold.
            Write-Host 'Pinned releases are not in feed\: the handover proof covers the NuGet closure only (pin a release to cover npm too).' -ForegroundColor Yellow
            # The temporary root mirrors the sample's layout with its own feed\, so the kit restores from it.
            $build += @('-SourceRoot', $versions.Root, '-FeedPath', $versions.Feed)
        }
        else { $build += @('-NpmProjects', $SampleNpmConsumer) }
        if ($properties.Count -gt 0) { $build += @('-MsBuildProperties', ($properties -join ',')) }
        if ((Invoke-Native 'powershell' $build) -ne $ExitSuccess) { return $false }
        $prove = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $RepoRoot $TestHandoverKitScript), '-KitPath', $kit)
        return (Invoke-Native 'powershell' $prove) -eq $ExitSuccess
    }
    finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}

# Step 16: the generated docs and the frontend client match the code (exit 1 on drift).
function Invoke-GeneratedDriftStep([string] $Dotnet) {
    $entryPoints = @($DocsGenProject, $GeneratedDocsDir, "$FrontendWorkspace/package.json")
    if (-not (Test-EntryPoints $entryPoints 'backend/tools/Starter.DocsGen + frontend api-gen')) { return $false }
    $docsArguments = @('run', '--project', $DocsGenProject, '--', '--out', $GeneratedDocsDir, '--check')
    if ((Invoke-Native $Dotnet $docsArguments) -ne $ExitSuccess) { return $false }
    return (Invoke-Native $NpmCommand @('run', 'api:gen:check', '--workspace', $FrontendWorkspace)) -eq $ExitSuccess
}

# Step 17: the app's ESLint (boundaries, HTTP only in core/api), Stylelint (tokens, logical CSS) and i18n parity.
function Invoke-FrontendLintStep {
    if (-not (Test-EntryPoints @("$FrontendWorkspace/package.json") 'frontend/starter-web')) { return $false }
    return (Invoke-Native $NpmCommand @('run', 'lint', '--workspace', $FrontendWorkspace)) -eq $ExitSuccess
}

# Step 18: scaffold a module upstream, fork, init -Minimal -RemoveModule, upstream-merge, build the fork from the feed.
function Invoke-ForkModelStep {
    if (-not (Test-EntryPoints @($ForkModelScript) 'tools/fork')) { return $false }
    $script = Join-Path $RepoRoot $ForkModelScript
    $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $script, '-RemoveModule', $ForkModelModule)
    return (Invoke-Native 'powershell.exe' $arguments) -eq $ExitSuccess
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
        @{ Name = '10 scanner tests';   Run = { Invoke-NodeTestStep @('tools/extract/tests', 'tools/ci/tests', 'tools/vectors/tests', 'deploy/handover/tests', 'tools/fork/tests', 'tools/scaffold/tests', 'tools/devseed/tests', 'tools/run-local') 'tools/extract + tools/ci + tools/vectors + deploy/handover + tools/fork + tools/scaffold + tools/devseed + tools/run-local' } },
        @{ Name = '11 npm build+lint+test';  Run = { Invoke-NpmStep } },
        @{ Name = '12 vectors verify';  Run = { Invoke-VectorsStep } },
        @{ Name = '13 package integration test'; Run = { Invoke-PackageIntegrationStep $Dotnet } },
        @{ Name = '14 handover offline'; Run = { Invoke-HandoverStep } },
        @{ Name = '15 provenance origins'; Run = { Invoke-NodeScriptStep $CheckOriginsScript @('--root', '.') 'tools/extract' } },
        @{ Name = '16 generated drift'; Run = { Invoke-GeneratedDriftStep $Dotnet } },
        @{ Name = '17 frontend lint';   Run = { Invoke-FrontendLintStep } },
        @{ Name = '18 fork-model test'; Run = { Invoke-ForkModelStep } }
    )

    $results = New-Object System.Collections.Generic.List[object]
    try {
        foreach ($step in $steps) {
            Write-Banner "CI step $($step.Name)"
            $passed = $false
            try { $passed = [bool](& $step.Run | Select-Object -Last 1) }
            catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red }
            $results.Add([pscustomobject]@{ Step = $step.Name; Result = $(if ($passed) { 'PASS' } else { 'FAIL' }) })
            if (-not $passed -and $FailFast) { break }
        }
    }
    finally { Remove-TemporaryFeeds }

    Write-ProtectedPathReport
    Write-Banner 'CI summary'
    $results | Format-Table -AutoSize | Out-String | Write-Host
    return -not ($results | Where-Object { $_.Result -eq 'FAIL' })
}

# ---------------------------------------------------------------------------------------------------------------
# Pack
# ---------------------------------------------------------------------------------------------------------------
function New-TempFolder([string] $Purpose) {
    $folder = Join-Path ([IO.Path]::GetTempPath()) "codelab-$Purpose-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    return $folder
}

# Runs tools/ci/package-versions.mjs and returns its stdout as one string; throws on a non-zero exit.
function Invoke-PackageVersions([string[]] $Arguments, [string] $FailureMessage) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & node @(@($PackageVersionsScript) + $Arguments)
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($exitCode -ne $ExitSuccess) { throw $FailureMessage }
    return ($output -join "`n")
}

function Resolve-CiNumber {
    if ($CiNumber -gt 0) { return $CiNumber }
    $fromEnv = [Environment]::GetEnvironmentVariable($CiNumberEnvVar)
    if ($fromEnv) { return [int] $fromEnv }
    $next = Invoke-PackageVersions @('next-ci', '--root', '.', '--feed', $FeedDir) 'Could not read the next ci number.'
    return [int] $next.Trim()
}

# The pack plan from packages/versions.json against $Feed; refused (throws) when a ci version is already in the feed,
# or, for a release, when a package in the feed still has an [Unreleased] changelog entry. A release marks a released,
# unchanged package skip=true: it is not packed again (a published version is immutable).
function Get-PackPlan([int] $Number, [bool] $IsRelease, [string] $Feed = $FeedDir) {
    $arguments = @('plan', '--root', '.', '--feed', $Feed)
    if ($IsRelease) { $arguments += '--release' } else { $arguments += @('--ci', [string] $Number) }
    $refusal = 'Pack refused (see the REFUSED/MISSING lines above); nothing was packed.'
    $plan = (Invoke-PackageVersions $arguments $refusal) | ConvertFrom-Json
    Write-Host ''
    $plan.packages | Format-Table -AutoSize id, kind, version, skip, source | Out-String | Write-Host
    return $plan
}

function Invoke-PackNuGet([string] $Dotnet, $Entry, [string] $Stage) {
    $version = $Entry.version
    # CodelabRelease stamps the assemblies (Codelab.Build.Sdk release stamping); PackageVersion names the package.
    $packArgs = @('pack', $Entry.source, '-c', $PackConfiguration, '-o', $Stage,
        "-p:PackageVersion=$version", "-p:CodelabRelease=$version")
    if ((Invoke-Native $Dotnet $packArgs) -ne $ExitSuccess) { throw "dotnet pack of $($Entry.id) $version failed." }
    $nupkg = Join-Path $Stage (Split-Path -Leaf $Entry.feedFile)
    if (-not (Test-Path -LiteralPath $nupkg)) { throw "Expected package not found: $nupkg" }
    return $nupkg
}

# Codelab dependencies inside a .nupkg must carry the planned version of that package (a ProjectReference takes the
# version of the pack's global PackageVersion, which is only right when both packages share a version).
function Test-NuspecDependencies([string] $Nupkg, $Plan) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Nupkg)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        $reader = New-Object IO.StreamReader($entry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
    foreach ($dependency in $nuspec.SelectNodes("//*[local-name()='dependency']")) {
        $planned = $Plan.packages | Where-Object { $_.id -eq $dependency.id } | Select-Object -First 1
        if (-not $planned) { continue }
        $actual = ($dependency.version -replace '^[\[\(]', '') -split ',' | Select-Object -First 1
        if ($actual -ne $planned.version) {
            throw "$(Split-Path -Leaf $Nupkg) depends on $($dependency.id) $actual, but this run packs $($planned.version)."
        }
    }
}

# npm pack runs on a %TEMP% copy (without node_modules) whose package.json is stamped; the source is never edited.
function Invoke-PackNpm($Entry, [string] $Stage) {
    $source = Join-Path $RepoRoot $Entry.source
    if (-not (Test-Path -LiteralPath (Join-Path $source $NpmDistFolder))) {
        throw "$($Entry.id) has no $NpmDistFolder folder after the build."
    }
    $copy = New-TempFolder 'pack-npm-src'
    try {
        Get-ChildItem -LiteralPath $source -Force | Where-Object { $_.Name -ne $NodeModulesFolder } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $copy -Recurse -Force }
        $null = Invoke-PackageVersions @('stamp', '--manifest', (Join-Path $copy 'package.json'), '--version', $Entry.version) `
            "Could not stamp version $($Entry.version) on the staged $($Entry.id)."
        # dist is already built; lifecycle scripts (prepack) would need the workspace's node_modules.
        if ((Invoke-Native $NpmCommand @('pack', $copy, '--pack-destination', $Stage, '--ignore-scripts')) -ne $ExitSuccess) {
            throw "npm pack of $($Entry.id) $($Entry.version) failed."
        }
    }
    finally { Remove-Item -LiteralPath $copy -Recurse -Force -ErrorAction SilentlyContinue }
    $tarball = Join-Path $Stage (Split-Path -Leaf $Entry.feedFile)
    if (-not (Test-Path -LiteralPath $tarball)) { throw "Expected tarball not found: $tarball" }
    return $tarball
}

# Packs every planned package (not the skipped ones) into a staging folder; returns @{ Entry; File } per package.
# Nothing touches the feed.
function Invoke-PackAll([string] $Dotnet, $Plan, [string] $Stage) {
    $toPack = @($Plan.packages | Where-Object { -not $_.skip })
    $npmEntries = @($toPack | Where-Object { $_.kind -eq $NpmKind })
    foreach ($entry in $npmEntries) {
        if ((Invoke-Native $NpmCommand @('run', 'build', '--workspace', $entry.id)) -ne $ExitSuccess) {
            throw "npm run build of $($entry.id) failed."
        }
    }
    $packed = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $toPack) {
        $file = $(if ($entry.kind -eq $NuGetKind) { Invoke-PackNuGet $Dotnet $entry $Stage } else { Invoke-PackNpm $entry $Stage })
        $packed.Add(@{ Entry = $entry; File = $file })
    }
    $packed | Where-Object { $_.Entry.kind -eq $NuGetKind } | ForEach-Object { Test-NuspecDependencies $_.File $Plan }
    return $packed
}

function Publish-ToFeed([string] $Dotnet, $Packed) {
    New-Item -ItemType Directory -Force -Path $NuGetFeedDir, $NpmFeedDir | Out-Null
    foreach ($item in $Packed) {
        if ($item.Entry.kind -eq $NuGetKind) {
            if ((Invoke-Native $Dotnet @('nuget', 'push', $item.File, '--source', $NuGetFeedDir)) -ne $ExitSuccess) {
                throw "dotnet nuget push of $($item.Entry.id) $($item.Entry.version) to $NuGetFeedDir failed."
            }
        }
        else { Copy-Item -LiteralPath $item.File -Destination $NpmFeedDir }
        Write-Host "Packed $($item.Entry.id) $($item.Entry.version) -> $($item.Entry.feedFile)" -ForegroundColor Green
    }
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

function Assert-PackSwitches {
    if (-not $Pack -and ($Release -or $DryRun)) { throw '-Release and -DryRun are options of -Pack.' }
    if ($Release -and $CiNumber -gt 0) { throw '-CiNumber has no meaning with -Release (exact versions are packed).' }
    $isDefaultFeed = [string]::Equals($FeedDir.TrimEnd('\', '/'), $DefaultFeedDir.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)
    if (-not $isDefaultFeed -and -not $DryRun) {
        throw "-FeedDir is only for -DryRun: the feed-mode build restores through nuget.config, which names feed\$NuGetFeedFolder."
    }
}

# Packs every package (ci prerelease, or the exact versions when $IsRelease, skipping released unchanged ones) into
# feed\, pins the SDK version in Directory.Build.props and runs the feed-mode build. -Pack only, never -Target CI.
function Invoke-PackTarget([string] $Dotnet, [bool] $IsRelease) {
    if (-not (Test-EntryPoints @($PackageVersionsScript) 'tools/ci')) { throw "Cannot plan the pack without $PackageVersionsScript." }
    $number = $(if ($IsRelease) { 0 } else { Resolve-CiNumber })
    Write-Banner $(if ($IsRelease) { "Pack release (exact versions) against $FeedDir" } else { "Pack ci.$number against $FeedDir" })
    $plan = Get-PackPlan $number $IsRelease
    if ($DryRun) {
        Write-Host 'Dry run: the plan is valid (skipped packages are released and unchanged). Nothing was packed.' -ForegroundColor Green
        return
    }
    $sdk = $plan.packages | Where-Object { $_.id -eq $BuildSdkPackageId } | Select-Object -First 1
    if (-not $sdk) { throw "$BuildSdkPackageId is not in packages/versions.json; the feed-mode build pins it." }
    $stage = New-TempFolder 'pack'
    try {
        $packed = Invoke-PackAll $Dotnet $plan $stage
        Publish-ToFeed $Dotnet $packed
    }
    finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    Set-PinnedBuildSdkVersion $sdk.version
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
    Assert-PackSwitches
    Use-PinnedNode
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
    if ($Pack) { Invoke-PackTarget $dotnet $Release.IsPresent }
    exit $ExitSuccess
}
catch {
    Write-Host "BUILD FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit $ExitFailure
}
finally {
    Pop-Location
}
