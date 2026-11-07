#Requires -Version 5.1
<#
.SYNOPSIS
    Builds a deploy kit: publish\deploy-<shortsha>\ with the deployables, the install scripts, the DBA scripts, a
    README and a manifest.

.DESCRIPTION
    The kit a developer hands to the server operators (the prep-deploy skill walks through it). In order:

      1. Resolves the commit (git rev-parse HEAD, or -Sha) and refuses a dirty work tree unless -AllowDirty (the
         manifest then says dirty: true, and the README says "not for production").
      2. Resolves the components (-Component, default: the Default entries of kit-components.psd1: api, attachment,
         web) and their projects, and plans the layout. -DryRun prints the plan here and changes nothing.
      3. Refuses an existing kit folder; -Force replaces it only when it holds a manifest this tool wrote.
      4. Builds every part with the private toolchain: dotnet publish -c Release (with --artifacts-path, so no bin\
         or obj\ of the work tree is touched) and npm run build -- --output-path (the private Node first on PATH).
         Development settings and *.local.json files are never copied into the kit.
      5. Copies deploy\kits (without tests\) into kits\, and refuses a component whose install script is missing.
      6. Runs tools\sql-kit\New-DbaSqlScripts.ps1 -OutputPath <kit>\sql when it exists (skipped with -SkipSql, or
         with a notice when the generator is not in the repository).
      7. Writes README.md from tools\kit\templates\README.template.md, refuses the kit when any file is key material
         or a local secret, then writes manifest.json: commit, package versions (packages\versions.json),
         components, the SQL decision and the SHA-256 of every file.

    Exit codes: 0 built (or planned), 1 refused or failed, 2 usage.

.PARAMETER Component
    The components to build: api, attachment, gateway, web, ops ('api,web' or several values). Default: api,
    attachment, web.
.PARAMETER OutputRoot
    The folder the kit folder is created in. Default <repo>\publish (gitignored).
.PARAMETER ArtifactsPath
    The dotnet --artifacts-path and the npm build output root. Default %TEMP%\codelab-kit-<shortsha>.
.PARAMETER Sha
    The commit to name the kit after. Default: git rev-parse HEAD. The build always uses the work tree as it is.
.PARAMETER AllowDirty
    Builds from a work tree with uncommitted changes (marked in the manifest and README).
.PARAMETER SkipSql
    Does not generate the DBA scripts.
.PARAMETER Force
    Replaces an existing kit folder of the same commit (only one this tool wrote).
.PARAMETER DryRun
    Prints the plan (kit folder, layout, sources, SQL decision) and changes nothing.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\kit\New-DeployKit.ps1 -DryRun
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\kit\New-DeployKit.ps1 -Component api,web,gateway,ops
#>
[CmdletBinding()]
param(
    [string[]] $Component,
    [string] $OutputRoot,
    [string] $ArtifactsPath,
    [string] $Sha,
    [switch] $AllowDirty,
    [switch] $SkipSql,
    [switch] $Force,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Constants ----------------------------------------------------------------------------------------------------
$ExitOk                 = 0
$ExitFailed             = 1
$ExitUsage              = 2
$RepoRoot               = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CatalogFile            = Join-Path $PSScriptRoot 'kit-components.psd1'
$ModuleFile             = Join-Path $PSScriptRoot 'lib\DeployKit.psm1'
$DefaultOutputFolder    = 'publish'
$ArtifactsFolderPrefix  = 'codelab-kit-'
$VersionsFile           = 'packages\versions.json'
$DeployKitsFolder       = 'deploy\kits'
$PrivateDotnetPath      = 'E:\codelab-tools\dotnet10\dotnet.exe'
$DotnetOverrideEnvVar   = 'CODELAB_DOTNET'
$DotnetOnPath           = 'dotnet'
$PrivateNodeDir         = 'E:\codelab-tools\node24'
$NodeDirEnvVar          = 'CODELAB_NODE_DIR'
$NpmCommand             = 'npm.cmd'
$PowerShellExe          = 'powershell.exe'
$PublishConfiguration   = 'Release'
$WebBrowserFolder       = 'browser'
$WebOutputFolder        = 'web-build'
$PublishFolder          = 'publish'
$UsageErrorPrefix       = 'USAGE: '
$SqlFilePattern         = '*.sql'
$WebManifestFile        = 'package.json'

Import-Module $ModuleFile -Force
$Kit = Get-KitConstants

function Write-Step([string] $Text) { Write-Host "== $Text" -ForegroundColor Cyan }

function Invoke-Tool {
    <# Runs a native tool from WorkingDirectory; throws with Failure when it exits non-zero. #>
    param([string] $Exe, [string[]] $Arguments, [string] $WorkingDirectory, [string] $Failure)
    Push-Location $WorkingDirectory
    try {
        & $Exe @Arguments
        if ($LASTEXITCODE -ne $ExitOk) { throw "$Failure (exit $LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

function Resolve-Dotnet {
    if ($env:CODELAB_DOTNET) { return $env:CODELAB_DOTNET }
    if (Test-Path -LiteralPath $PrivateDotnetPath) { return $PrivateDotnetPath }
    Write-Warning "The private .NET SDK is not at $PrivateDotnetPath and $DotnetOverrideEnvVar is not set; using '$DotnetOnPath' on PATH."
    return $DotnetOnPath
}

function Use-PrivateNode {
    $dir = if ($env:CODELAB_NODE_DIR) { $env:CODELAB_NODE_DIR } else { $PrivateNodeDir }
    if (Test-Path -LiteralPath $dir) { $env:PATH = "$dir;$env:PATH"; return }
    Write-Warning "The private Node is not at $dir ($NodeDirEnvVar); the web builds use the Node on PATH."
}

function Get-GitText([string[]] $Arguments) {
    $output = & git -C $RepoRoot @Arguments
    if ($LASTEXITCODE -ne $ExitOk) { throw "git $($Arguments -join ' ') failed (exit $LASTEXITCODE)." }
    return (@($output) -join "`n").Trim()
}

function Resolve-Commit {
    if ($Sha) { return $Sha.Trim().ToLowerInvariant() }
    return (Get-GitText @('rev-parse', 'HEAD')).ToLowerInvariant()
}

function Test-DirtyTree { return -not [string]::IsNullOrWhiteSpace((Get-GitText @('status', '--porcelain'))) }

function Resolve-FullPath([string] $Path, [string] $Default) {
    $value = if ($Path) { $Path } else { $Default }
    if (-not [IO.Path]::IsPathRooted($value)) { $value = Join-Path (Get-Location).Path $value }
    return [IO.Path]::GetFullPath($value)
}

function Copy-Payload {
    <# Copies Source into Destination, without development settings and local secrets. #>
    param([string] $Source, [string] $Destination)
    $root = (Resolve-Path -LiteralPath $Source).ProviderPath.TrimEnd('\')
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        if (Test-KitPayloadExcluded $file.Name) { Write-Host "   left out: $($file.FullName.Substring($root.Length + 1))"; continue }
        $target = Join-Path $Destination $file.FullName.Substring($root.Length + 1)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

function Build-DotnetPart {
    param([object] $Entry, [string] $Dotnet, [string] $KitRoot, [string] $Artifacts)
    $project = Resolve-KitProject -RepoRoot $RepoRoot -ProjectRoot $Entry.Part['ProjectRoot'] -Pattern $Entry.Part['ProjectPattern']
    $stage = Join-Path $Artifacts (Join-Path $PublishFolder ($Entry.Path -replace '/', '-'))
    Write-Step "dotnet publish $project -> $($Entry.Path)\"
    Invoke-Tool -Exe $Dotnet -WorkingDirectory $RepoRoot -Failure "dotnet publish of $($Entry.Path) failed" -Arguments @(
        'publish', $project, '-c', $PublishConfiguration, '-o', $stage, '--artifacts-path', $Artifacts, '-nologo')
    Copy-Payload -Source $stage -Destination (Join-Path $KitRoot $Entry.Path)
}

function Build-WebPart {
    param([object] $Entry, [string] $KitRoot, [string] $Artifacts)
    $workspace = Join-Path $RepoRoot $Entry.Part['Workspace']
    if (-not (Test-Path -LiteralPath (Join-Path $workspace $WebManifestFile))) { throw "Web workspace '$($Entry.Part['Workspace'])' has no $WebManifestFile." }
    $output = Join-Path $Artifacts (Join-Path $WebOutputFolder ($Entry.Path -replace '/', '-'))
    Write-Step "npm run build ($($Entry.Part['Workspace'])) -> $($Entry.Path)\"
    Invoke-Tool -Exe $NpmCommand -WorkingDirectory $workspace -Failure "The web build of $($Entry.Path) failed" -Arguments @(
        'run', 'build', '--', '--output-path', $output)
    $browser = Join-Path $output $WebBrowserFolder
    $source = if (Test-Path -LiteralPath $browser) { $browser } else { $output }
    Copy-Payload -Source $source -Destination (Join-Path $KitRoot $Entry.Path)
}

function Copy-KitScripts {
    param([string] $KitRoot, [object[]] $Components)
    $source = Join-Path $RepoRoot $DeployKitsFolder
    $target = Join-Path $KitRoot $Kit.KitScriptsFolder
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $source) {
        if (Test-KitScriptsEntryExcluded $item.Name) { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $target -Recurse
    }
    foreach ($component in $Components) {
        if (-not (Test-Path -LiteralPath (Join-Path $target $component['InstallScript']))) {
            throw "Component '$($component['Name'])' needs $DeployKitsFolder\$($component['InstallScript']), which does not exist; a kit never ships what it cannot install."
        }
    }
}

function Invoke-SqlStep {
    param([string] $KitRoot, [string] $Generator)
    $output = Join-Path $KitRoot $Kit.SqlFolder
    Write-Step "DBA scripts: $($Kit.SqlGenerator) -OutputPath $output"
    Invoke-Tool -Exe $PowerShellExe -WorkingDirectory $RepoRoot -Failure 'The DBA script generator failed' -Arguments @(
        '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $Generator, '-OutputPath', $output)
    if (@(Get-ChildItem -LiteralPath $output -Filter $SqlFilePattern -File -ErrorAction SilentlyContinue).Count -eq 0) {
        throw "The DBA script generator wrote no .sql file into $output."
    }
}

function Write-Plan {
    param([string] $KitRoot, [object[]] $Layout, [object] $SqlDecision, [object[]] $Components, [string] $Commit, [bool] $Dirty)
    Write-Host "Kit:        $KitRoot"
    Write-Host "Commit:     $Commit$(if ($Dirty) { ' (work tree has uncommitted changes)' })"
    Write-Host "Components: $(($Components | ForEach-Object { $_['Name'] }) -join ', ')"
    foreach ($entry in $Layout) { Write-Host ('  {0,-14} <- {1}' -f $entry.Path, $entry.Source) }
    if (-not $SqlDecision.Included) { Write-Host "SQL:        $($SqlDecision.Reason)" }
    foreach ($component in $Components) {
        if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $RepoRoot $DeployKitsFolder) $component['InstallScript']))) {
            Write-Warning "$DeployKitsFolder\$($component['InstallScript']) does not exist: the real build refuses '$($component['Name'])'."
        }
    }
}

function Remove-OwnKitFolder([string] $KitRoot) {
    if (-not (Test-Path -LiteralPath $KitRoot)) { return }
    if (-not $Force) { throw "$KitRoot already exists; pass -Force to replace it." }
    if (-not (Test-KitOwnedFolder $KitRoot)) { throw "$KitRoot exists but holds no kit manifest; it is not replaced. Remove it yourself." }
    Remove-Item -LiteralPath $KitRoot -Recurse -Force
}

function Complete-Kit {
    param([string] $KitRoot, [string] $Commit, [bool] $Dirty, [object] $SqlDecision, [object[]] $Components)
    $relative = @(Get-ChildItem -LiteralPath $KitRoot -Recurse -File | ForEach-Object { $_.FullName.Substring($KitRoot.Length + 1) })
    $forbidden = Find-KitForbiddenFiles $relative
    if ($forbidden.Count -gt 0) { throw "The kit holds key material or local secrets: $($forbidden -join ', '). Nothing may ship." }
    $versions = [IO.File]::ReadAllText((Join-Path $RepoRoot $VersionsFile)) | ConvertFrom-Json
    $utf8 = New-Object Text.UTF8Encoding($false)
    $createdUtc = [DateTime]::UtcNow
    # The README is written from a draft manifest (its values do not depend on the README), then every file is hashed.
    $draft = New-KitManifest -Commit $Commit -Dirty $Dirty -CreatedUtc $createdUtc -Versions $versions -Components $Components -Sql $SqlDecision -Files (Get-KitFileHashes $KitRoot)
    $template = [IO.File]::ReadAllText((Join-Path $RepoRoot $Kit.ReadmeTemplate))
    [IO.File]::WriteAllText((Join-Path $KitRoot $Kit.ReadmeFileName), (Expand-KitTemplate $template (Get-KitReadmeValues $draft $Components)), $utf8)
    $manifest = New-KitManifest -Commit $Commit -Dirty $Dirty -CreatedUtc $createdUtc -Versions $versions -Components $Components -Sql $SqlDecision -Files (Get-KitFileHashes $KitRoot)
    [IO.File]::WriteAllText((Join-Path $KitRoot $Kit.ManifestFileName), (ConvertTo-KitJsonText $manifest), $utf8)
    return $manifest
}

function Invoke-DeployKit {
    $catalog = @((Import-PowerShellDataFile -LiteralPath $CatalogFile).Components)
    try { $components = Resolve-KitComponents -Catalog $catalog -Requested $Component }
    catch { throw "$UsageErrorPrefix$($_.Exception.Message)" }
    $commit = Resolve-Commit
    $kitName = try { Get-KitFolderName $commit } catch { throw "$UsageErrorPrefix$($_.Exception.Message)" }
    $dirty = Test-DirtyTree
    $generator = Join-Path $RepoRoot $Kit.SqlGenerator
    $sqlDecision = Get-KitSqlDecision -SkipSql $SkipSql.IsPresent -GeneratorExists (Test-Path -LiteralPath $generator) -GeneratorPath $Kit.SqlGenerator
    $kitRoot = Join-Path (Resolve-FullPath $OutputRoot (Join-Path $RepoRoot $DefaultOutputFolder)) $kitName
    $layout = Get-KitLayout -Components $components -SqlStatus $sqlDecision.Status
    Write-Plan -KitRoot $kitRoot -Layout $layout -SqlDecision $sqlDecision -Components $components -Commit $commit -Dirty $dirty
    if ($DryRun) { Write-Host 'Dry run: nothing was built or written.' -ForegroundColor Green; return }
    if ($dirty -and -not $AllowDirty) { throw 'The work tree has uncommitted changes; commit them, or pass -AllowDirty for a kit that is not for production.' }

    foreach ($entry in $layout | Where-Object { $_.IsDotnet }) { $null = Resolve-KitProject -RepoRoot $RepoRoot -ProjectRoot $entry.Part['ProjectRoot'] -Pattern $entry.Part['ProjectPattern'] }
    Remove-OwnKitFolder $kitRoot
    $artifacts = Resolve-FullPath $ArtifactsPath (Join-Path ([IO.Path]::GetTempPath()) ($ArtifactsFolderPrefix + $kitName.Substring($Kit.KitFolderPrefix.Length)))
    New-Item -ItemType Directory -Path $kitRoot -Force | Out-Null
    try {
        Copy-KitScripts -KitRoot $kitRoot -Components $components
        $dotnet = Resolve-Dotnet
        Use-PrivateNode
        foreach ($entry in $layout | Where-Object { $_.IsPayload }) {
            if ($entry.IsDotnet) { Build-DotnetPart -Entry $entry -Dotnet $dotnet -KitRoot $kitRoot -Artifacts $artifacts }
            else { Build-WebPart -Entry $entry -KitRoot $kitRoot -Artifacts $artifacts }
        }
        if ($sqlDecision.Included) { Invoke-SqlStep -KitRoot $kitRoot -Generator $generator }
        $manifest = Complete-Kit -KitRoot $kitRoot -Commit $commit -Dirty $dirty -SqlDecision $sqlDecision -Components $components
    }
    catch {
        # This run created the folder: a half-built kit must never look shippable.
        Remove-Item -LiteralPath $kitRoot -Recurse -Force -ErrorAction SilentlyContinue
        throw
    }
    Write-Host "Kit ready: $kitRoot ($(@($manifest.files).Count) files; manifest.json lists their SHA-256)." -ForegroundColor Green
}

try {
    Invoke-DeployKit
    exit $ExitOk
}
catch {
    $message = $_.Exception.Message
    if ($message.StartsWith($UsageErrorPrefix)) {
        Write-Host $message.Substring($UsageErrorPrefix.Length) -ForegroundColor Red
        exit $ExitUsage
    }
    Write-Host "KIT FAILED: $message" -ForegroundColor Red
    exit $ExitFailed
}
