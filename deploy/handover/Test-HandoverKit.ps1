<#
.SYNOPSIS
    Proves a handover kit is a complete closure: restores and builds its projects with no network (ADR-008).

.DESCRIPTION
    Windows PowerShell 5.1 and PowerShell 7. Steps:
      1. Integrity: every file in manifest.json is present with its SHA-256, and nothing unlisted is in the kit.
      2. A clean copy of the source tree in %TEMP% (git's file list; the Codelab feed is NOT copied). Every
         nuget.config in it is replaced by the kit's one (absolute path to <kit>\nuget), and the kit's npm-files\
         tarballs are put back at their source-relative paths.
      3. Network simulation for every child process: HTTP_PROXY / HTTPS_PROXY / ALL_PROXY and the npm proxy
         settings point at an unroutable local port, NO_PROXY is cleared, NUGET_PACKAGES is a new EMPTY folder.
      4. NuGet: `dotnet restore --configfile <kit>\nuget.config` then `dotnet build --no-restore` per project,
         with the manifest's MSBuild properties.
      5. npm: `npm ci --offline --cache <copy of kit\npm-cache>` per install folder, then `npm run build` and
         `npm test` in each npm project that has that script, so the installed packages are imported and run offline.
    The kit is never modified (the npm cache is copied first). Temp folders are removed at the end.

    This is the in-process simulation; the real proof (a VM with the network adapter disabled) is an owner step.
    Exit codes: 0 offline restore and build passed, 1 a check failed, 2 bad arguments.

.PARAMETER KitPath
    The kit folder written by New-HandoverKit.ps1.
.PARAMETER SourceRoot
    The source tree to build. Default: the manifest's sourceRoot.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\handover\Test-HandoverKit.ps1 -KitPath $env:TEMP\kit
#>
[CmdletBinding()]
param(
    [string] $KitPath,
    [string] $SourceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\HandoverKit.psm1') -Force -DisableNameChecking

$Layout      = Get-KitLayout
$ExitSuccess = $Layout.ExitSuccess
$ExitFailure = $Layout.ExitFailure
$ExitUsage   = $Layout.ExitUsage

function Get-ValidatedKit {
    if (-not $KitPath) { Stop-KitUsage 'KitPath is required.' }
    $kit = Get-KitTrimmedPath ([IO.Path]::GetFullPath((Get-KitFullPath $KitPath (Get-Location).Path)))
    if (-not (Test-Path -LiteralPath $kit -PathType Container)) { Stop-KitUsage "KitPath '$kit' does not exist." }
    if (-not (Test-KitIsKitFolder $kit)) { Stop-KitUsage "'$kit' holds no handover kit manifest." }
    $manifest = Read-KitManifest $kit
    $root = $SourceRoot
    if (-not $root) { $root = $manifest.sourceRoot }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { Stop-KitUsage "SourceRoot '$root' does not exist." }
    return [pscustomobject]@{ Kit = $kit; Manifest = $manifest; Root = (Get-KitTrimmedPath ([IO.Path]::GetFullPath($root))) }
}

function Test-Integrity([string] $Kit) {
    $problems = @(Test-KitManifestIntegrity $Kit)
    foreach ($problem in $problems) { Write-Host "INTEGRITY: $problem" -ForegroundColor Red }
    if ($problems.Count -eq 0) { Write-Host 'Integrity: every file matches manifest.json.' -ForegroundColor Green }
    return ($problems.Count -eq 0)
}

# Replaces every nuget.config of the work tree with the kit's single folder source (absolute path).
function Set-WorkTreeNuGetConfigs([string] $WorkTree, [string] $Kit) {
    $xml = New-KitNuGetConfigXml (Join-Path $Kit $Layout.NuGetDir)
    $configs = @(Get-ChildItem -LiteralPath $WorkTree -Recurse -File -Force | Where-Object { $_.Name -ieq $Layout.NuGetConfigFile })
    foreach ($config in $configs) { Write-KitText $config.FullName $xml }
    Write-KitText (Join-Path $WorkTree $Layout.NuGetConfigFile) $xml
}

function Copy-NpmFiles([string] $Kit, [string] $WorkTree) {
    $files = Join-Path $Kit $Layout.NpmFilesDir
    if (Test-Path -LiteralPath $files) { Copy-KitFolderContents $files $WorkTree }
}

function Test-DotnetProjects([object] $Manifest, [string] $Kit, [string] $WorkTree) {
    $projects = @($Manifest.projects)
    if ($projects.Count -eq 0) { return $true }
    $dotnet = Resolve-KitDotnet
    $properties = ConvertTo-KitPropertyArguments @($Manifest.msbuildProperties)
    $config = Join-Path $Kit $Layout.NuGetConfigFile
    $ok = $true
    foreach ($project in $projects) {
        $path = Join-Path $WorkTree $project
        $code = Invoke-KitNative $dotnet (@('restore', $path, '--configfile', $config) + $properties) $WorkTree
        if ($code -eq $ExitSuccess) { $code = Invoke-KitNative $dotnet (@('build', $path, '--no-restore') + $properties) $WorkTree }
        Write-Host "dotnet restore+build $project -> exit $code" -ForegroundColor $(if ($code -eq $ExitSuccess) { 'Green' } else { 'Red' })
        if ($code -ne $ExitSuccess) { $ok = $false }
    }
    return $ok
}

function Test-HasNpmScript([string] $ProjectDir, [string] $Script) {
    $json = Get-Content -LiteralPath (Join-Path $ProjectDir $Layout.PackageJsonFile) -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not ($json.PSObject.Properties.Name -contains $Layout.NpmScriptsKey)) { return $false }
    return ($json.scripts.PSObject.Properties.Name -contains $Script)
}

function Test-NpmProjects([object] $Manifest, [string] $Kit, [string] $WorkTree, [string] $CacheCopy) {
    $projects = @($Manifest.npmProjects | ForEach-Object { [pscustomobject]@{ Relative = $_.path; InstallRoot = $_.installRoot } })
    if ($projects.Count -eq 0) { return $true }
    $npm = Resolve-KitNpm
    Copy-KitFolderContents (Join-Path $Kit $Layout.NpmCacheDir) $CacheCopy
    foreach ($root in Get-KitNpmInstallRoots $projects) {
        $code = Invoke-KitNpmCi $npm (Join-Path $WorkTree $root) $CacheCopy $true
        Write-Host "npm ci --offline in '$(Format-KitInstallRoot $root)' -> exit $code"
        if ($code -ne $ExitSuccess) { return $false }
    }
    # build, then test: the test imports and runs the installed packages, which a build alone never does.
    foreach ($project in $projects) {
        $dir = Join-Path $WorkTree $project.Relative
        foreach ($script in @($Layout.NpmBuildScript, $Layout.NpmTestScript)) {
            if (-not (Test-HasNpmScript $dir $script)) { continue }
            $code = Invoke-KitNative $npm @('run', $script) $dir
            Write-Host "npm run $script in '$($project.Relative)' -> exit $code"
            if ($code -ne $ExitSuccess) { return $false }
        }
    }
    return $true
}

function Invoke-OfflineChecks([pscustomobject] $Target) {
    $workTree = New-KitWorkTree $Target.Root $null
    $emptyPackages = New-KitEmptyPackagesFolder
    $cacheCopy = New-KitNpmCacheCopyFolder
    try {
        Set-WorkTreeNuGetConfigs $workTree $Target.Kit
        Copy-NpmFiles $Target.Kit $workTree
        $checks = {
            $dotnetOk = Test-DotnetProjects $Target.Manifest $Target.Kit $workTree
            $npmOk = Test-NpmProjects $Target.Manifest $Target.Kit $workTree $cacheCopy
            return ($dotnetOk -and $npmOk)
        }
        return (Invoke-KitWithEnvironment (Get-KitOfflineEnvironment $emptyPackages) $checks)
    }
    finally {
        Remove-KitTempFolder $workTree
        Remove-KitTempFolder $emptyPackages
        Remove-KitTempFolder $cacheCopy
    }
}

function Invoke-KitTest {
    $target = Get-ValidatedKit
    if (-not (Test-Integrity $target.Kit)) { return $ExitFailure }
    if (-not (Invoke-OfflineChecks $target)) {
        Write-Host 'OFFLINE CHECK FAILED: the kit is not a complete closure.' -ForegroundColor Red
        return $ExitFailure
    }
    Write-Host 'OFFLINE CHECK PASSED: restore and build used only the kit.' -ForegroundColor Green
    return $ExitSuccess
}

try {
    exit (Invoke-KitTest)
}
catch {
    if (Test-KitUsageError $_.Exception) {
        Write-Host "USAGE: $($_.Exception.Message)" -ForegroundColor Red
        exit $ExitUsage
    }
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    exit $ExitFailure
}
