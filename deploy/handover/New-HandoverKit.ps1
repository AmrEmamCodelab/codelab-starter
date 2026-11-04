<#
.SYNOPSIS
    Builds a Codelab handover kit: the full dependency closure a client needs to restore and build offline (ADR-008).

.DESCRIPTION
    Windows PowerShell 5.1 and PowerShell 7. Writes, into -OutputPath:
      nuget\           every NuGet package the -Projects restore (Codelab.* from the feed and all third-party
                       packages, MSBuild SDK packages included), trimmed to a folder feed (.nupkg + hash + nuspec)
      nuget.config     <clear/> and that one folder source (./nuget); no nuget.org, no package source mapping
      npm-cache\       the npm cache after `npm ci --cache` in each -NpmProjects install folder (every tarball)
      npm-files\       tarballs a lock file installs by file: path, at their source-relative path
      OFFLINE.md       how to restore offline (`npm ci --offline --cache ./npm-cache`)
      sources\         one zip per Codelab package folder (packages\dotnet\*, packages\npm\*, packages\vectors)
      PREREQUISITES.md .NET SDK (global.json), Node (.nvmrc) and Playwright browser versions
      manifest.json    kit identity, inputs and the SHA-256 of every other file

    Restores run in a clean copy of the source tree under %TEMP% (git's file list), never in the caller's tree.
    The script deletes nothing outside -OutputPath (and its own temp copy). A non-empty -OutputPath is refused
    unless -Force is given AND the folder holds a previous kit (its manifest.json) and nothing else.

    Exit codes: 0 kit written, 1 a restore or a step failed, 2 bad arguments or an unsafe output folder.

.PARAMETER OutputPath
    Kit folder (relative to the current folder). Must not be inside the source root.
.PARAMETER Projects
    .csproj / .slnx / .sln files to restore, relative to -SourceRoot. Comma-separated values are accepted.
.PARAMETER NpmProjects
    Folders with a package.json, relative to -SourceRoot. A workspace member is installed from its workspace root.
.PARAMETER FeedPath
    The Codelab feed folder (holding nuget\ and npm\), relative to -SourceRoot. Default: feed.
.PARAMETER MsBuildProperties
    Name=Value MSBuild properties for restore (Test-HandoverKit.ps1 reuses them from the manifest),
    e.g. UseCodelabPackageSource=false.
.PARAMETER SourceRoot
    Root of the source tree. Default: the repository that holds this script.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\handover\New-HandoverKit.ps1 -OutputPath $env:TEMP\kit -Projects packages\dotnet\Codelab.Security\Codelab.Security.csproj -NpmProjects packages\npm\ng-security
#>
[CmdletBinding()]
param(
    [string] $OutputPath,
    [string[]] $Projects = @(),
    [string[]] $NpmProjects = @(),
    [string] $FeedPath,
    [string[]] $MsBuildProperties = @(),
    [string] $SourceRoot,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\HandoverKit.psm1') -Force -DisableNameChecking
# Windows PowerShell 5.1 has no $PSScriptRoot in parameter defaults, so the default source root is set here.
if (-not $SourceRoot) { $SourceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }

$Layout      = Get-KitLayout
$ExitSuccess = $Layout.ExitSuccess
$ExitFailure = $Layout.ExitFailure
$ExitUsage   = $Layout.ExitUsage
$NodeExe     = 'node'
$VersionArg  = '--version'
$IsoFormat   = 'o'

function Get-ValidatedInputs {
    if (-not $OutputPath) { Stop-KitUsage 'OutputPath is required.' }
    if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) { Stop-KitUsage "SourceRoot '$SourceRoot' does not exist." }
    $root = Get-KitTrimmedPath ([IO.Path]::GetFullPath($SourceRoot))
    $dotnetProjects = @(Resolve-KitDotnetProjects $root (Split-KitListArgument $Projects))
    $npm = @(Resolve-KitNpmProjects $root (Split-KitListArgument $NpmProjects))
    if ($dotnetProjects.Count -eq 0 -and $npm.Count -eq 0) { Stop-KitUsage 'Give at least one -Projects or -NpmProjects entry.' }
    $feedValue = $FeedPath
    if (-not $feedValue) { $feedValue = $Layout.DefaultFeed }
    $feed = Get-KitFullPath $feedValue $root
    if ($dotnetProjects.Count -gt 0 -and -not (Test-Path -LiteralPath (Join-Path $feed $Layout.FeedNuGetSubdir) -PathType Container)) {
        Stop-KitUsage "Feed '$feed' has no $($Layout.FeedNuGetSubdir) folder."
    }
    $output = Get-KitFullPath $OutputPath (Get-Location).Path
    $clear = Assert-KitOutputPath $output $root ([bool] $Force)
    return [pscustomobject]@{ Root = $root; Projects = $dotnetProjects; Npm = $npm; Feed = $feed; Output = $output; ClearPrevious = $clear }
}

function Build-NuGetClosure([pscustomobject] $Inputs, [string] $Dotnet, [string] $WorkTree) {
    if ($Inputs.Projects.Count -eq 0) { return $true }
    $configPath = Join-Path $WorkTree $Layout.NuGetConfigFile
    Write-KitText $configPath (New-KitRestoreConfigXml (Join-Path $Inputs.Feed $Layout.FeedNuGetSubdir))
    $packagesDir = Join-Path $Inputs.Output $Layout.NuGetDir
    New-Item -ItemType Directory -Path $packagesDir -Force | Out-Null
    $failed = @(Invoke-KitNuGetRestore $Dotnet $WorkTree $Inputs.Projects $packagesDir $configPath (Split-KitListArgument $MsBuildProperties))
    if ($failed.Count -gt 0) { Write-Host "Restore FAILED: $($failed -join ', ')" -ForegroundColor Red; return $false }
    Optimize-KitNuGetFolder $packagesDir
    Write-KitText (Join-Path $Inputs.Output $Layout.NuGetConfigFile) (New-KitNuGetConfigXml $Layout.RelativeNuGetSource)
    return $true
}

function Build-NpmClosure([pscustomobject] $Inputs, [string] $Npm, [string] $WorkTree) {
    $result = [pscustomobject]@{ Ok = $true; Tarballs = @() }
    if ($Inputs.Npm.Count -eq 0) { return $result }
    $cache = Join-Path $Inputs.Output $Layout.NpmCacheDir
    $tarballs = @()
    foreach ($root in Get-KitNpmInstallRoots $Inputs.Npm) {
        if ((Invoke-KitNpmCi $Npm (Join-Path $WorkTree $root) $cache $false) -ne $ExitSuccess) {
            Write-Host "npm ci FAILED in '$(Format-KitInstallRoot $root)'" -ForegroundColor Red
            $result.Ok = $false
            return $result
        }
        $tarballs += Get-KitNpmFileTarballs (Join-Path $Inputs.Root $root) $Inputs.Root
    }
    Remove-KitNpmCacheNoise $cache
    $result.Tarballs = @($tarballs | Sort-Object -Unique)
    foreach ($relative in $result.Tarballs) {
        Copy-KitFile (Join-Path $Inputs.Root $relative) (Join-Path (Join-Path $Inputs.Output $Layout.NpmFilesDir) $relative)
    }
    return $result
}

function Get-ToolVersions([pscustomobject] $Inputs, [string] $Dotnet, [string] $Npm, [string] $WorkTree) {
    $tools = [pscustomobject]@{
        DotnetPin = (Get-KitDotnetSdkPin $Inputs.Root); DotnetUsed = $null
        NodePin = (Get-KitNodePin $Inputs.Root); NodeUsed = $null; NpmUsed = $null
        Playwright = @(Get-KitNpmInstallRoots $Inputs.Npm | ForEach-Object { Get-KitPlaywrightVersions (Join-Path $Inputs.Root $_) } | Sort-Object -Unique)
    }
    if ($Dotnet) {
        # global.json in the work tree selects the SDK, so ask from there.
        Push-Location -LiteralPath $WorkTree
        try { $tools.DotnetUsed = Get-KitNativeOutput $Dotnet @($VersionArg) } finally { Pop-Location }
    }
    if ($Npm) {
        $tools.NodeUsed = Get-KitNativeOutput $NodeExe @($VersionArg)
        $tools.NpmUsed = Get-KitNativeOutput $Npm @($VersionArg)
    }
    return $tools
}

function Get-ManifestDetails([pscustomobject] $Inputs, [pscustomobject] $Tools, [string[]] $Tarballs, [string[]] $Zips) {
    return [ordered]@{
        createdUtc           = (Get-Date).ToUniversalTime().ToString($IsoFormat)
        sourceRoot           = $Inputs.Root
        feedPath             = $Inputs.Feed
        projects             = @($Inputs.Projects | ForEach-Object { $_.Relative })
        npmProjects          = @($Inputs.Npm | ForEach-Object { [ordered]@{ path = $_.Relative; installRoot = $_.InstallRoot } })
        npmFileTarballs      = @($Tarballs)
        msbuildProperties    = @(Split-KitListArgument $MsBuildProperties)
        sourceZips           = @($Zips)
        missingSourceFolders = @(Get-KitMissingSourceFolders $Inputs.Root)
        tools                = [ordered]@{ dotnetSdk = $Tools.DotnetUsed; node = $Tools.NodeUsed; npm = $Tools.NpmUsed }
    }
}

function Write-KitContent([pscustomobject] $Inputs, [string] $Dotnet, [string] $Npm, [string] $WorkTree) {
    if (-not (Build-NuGetClosure $Inputs $Dotnet $WorkTree)) { return $false }
    $npmResult = Build-NpmClosure $Inputs $Npm $WorkTree
    if (-not $npmResult.Ok) { return $false }
    $zips = @(New-KitSourceZips $Inputs.Root $Inputs.Output @(Get-KitSourceFiles $Inputs.Root))
    $tools = Get-ToolVersions $Inputs $Dotnet $Npm $WorkTree
    Write-KitText (Join-Path $Inputs.Output $Layout.OfflineFile) (New-KitOfflineText $Inputs.Npm $npmResult.Tarballs)
    Write-KitText (Join-Path $Inputs.Output $Layout.PrerequisitesFile) (New-KitPrerequisitesText $tools)
    $manifest = Write-KitManifest $Inputs.Output (Get-ManifestDetails $Inputs $tools $npmResult.Tarballs $zips)
    Write-Host "Handover kit written to $($Inputs.Output) ($(@($manifest.files).Count) files)." -ForegroundColor Green
    return $true
}

# A failed run removes the kit entries it wrote (the folder was empty or held only a previous kit), so a half kit
# with no manifest is never left behind.
function Invoke-KitBuild([pscustomobject] $Inputs) {
    $dotnet = $null
    $npm = $null
    if ($Inputs.Projects.Count -gt 0) { $dotnet = Resolve-KitDotnet }
    if ($Inputs.Npm.Count -gt 0) { $npm = Resolve-KitNpm }
    if ($Inputs.ClearPrevious) { Clear-KitPreviousOutput $Inputs.Output }
    New-Item -ItemType Directory -Path $Inputs.Output -Force | Out-Null
    $workTree = New-KitWorkTree $Inputs.Root $Inputs.Feed
    $ok = $false
    try {
        $ok = Write-KitContent $Inputs $dotnet $npm $workTree
    }
    finally {
        Remove-KitTempFolder $workTree
        if (-not $ok) { Clear-KitPreviousOutput $Inputs.Output }
    }
    if ($ok) { return $ExitSuccess }
    return $ExitFailure
}

try {
    exit (Invoke-KitBuild (Get-ValidatedInputs))
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
