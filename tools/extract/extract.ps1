<#
.SYNOPSIS
    Extracts one allowlisted batch from the read-only harvest copy into a staging folder and scans it.

.DESCRIPTION
    The ONLY way source-product files enter the starter (ADR-001). In order:
      1. extract-core.mjs  resolves the batch in allowlist.manifest.yaml, applies hard-deny, copies the allowed
                           files to <Staging>\files and writes <Staging>\origins.json (one origin entry per file).
      2. scrub sweep       node scrub-sweep.mjs --root <Staging>\files
      3. secret scan       node secret-scan.mjs --root <Staging>\files (+ gitleaks when gitleaks.exe is found)
    Nothing is promoted into the repo automatically. A clean batch is promoted by hand (see tools/extract/README.md):
    rewrite the staged files with starter names, copy origins.json to provenance\origins\<batch>.json and rerun
    generate-source.mjs. The harvest copy and the source repository are only ever read.

    Exit code: 0 when every step passed, 1 when any step reported findings or failed.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\extract\extract.ps1 -Batch p0 -Clean
#>
[CmdletBinding()]
param(
    # Batch name under 'batches:' in the manifest.
    [string] $Batch = 'p0',

    # Staging folder. Default: %TEMP%\codelab-extract\<Batch>. It must be outside the repository.
    [string] $Staging,

    # Empty the staging folder first (it must otherwise be empty).
    [switch] $Clean,

    [string] $Manifest,

    # Override the manifest's source.harvestRoot.
    [string] $HarvestRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------
$ToolDir            = $PSScriptRoot
$RepoRoot           = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$DefaultManifest    = Join-Path $ToolDir 'allowlist.manifest.yaml'
$StagingParentName  = 'codelab-extract'
$StagedFilesFolder  = 'files'
$GitleaksLocalPath  = Join-Path $RepoRoot 'tools\bin\gitleaks.exe'
$ExitSuccess        = 0
$ExitFailure        = 1

# ---------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------
function Write-Step([string] $Text) {
    Write-Host ''
    Write-Host "== $Text" -ForegroundColor Cyan
}

# Runs a native command and returns ONLY its exit code; native stderr never becomes a terminating error.
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

function Resolve-StagingPath {
    $path = $Staging
    if (-not $path) { $path = Join-Path (Join-Path $env:TEMP $StagingParentName) $Batch }
    $full = [System.IO.Path]::GetFullPath($path)
    $repoFull = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\') + '\'
    if ($full.StartsWith($repoFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Staging '$full' is inside the repository; choose a folder outside it (default: %TEMP%\$StagingParentName)."
    }
    return $full
}

function Resolve-Gitleaks {
    if (Test-Path -LiteralPath $GitleaksLocalPath) { return $GitleaksLocalPath }
    $onPath = Get-Command gitleaks -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

function Clear-Staging([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Write-Host "Emptying staging folder $Path" -ForegroundColor Yellow
    Get-ChildItem -LiteralPath $Path -Force | Remove-Item -Recurse -Force
}

# ---------------------------------------------------------------------------------------------------------------
# Steps (each returns $true when it passed)
# ---------------------------------------------------------------------------------------------------------------
function Invoke-CopyStep([string] $StagingPath, [string] $ManifestPath) {
    $arguments = @((Join-Path $ToolDir 'extract-core.mjs'), '--batch', $Batch, '--staging', $StagingPath, '--manifest', $ManifestPath)
    if ($HarvestRoot) { $arguments += @('--harvest', $HarvestRoot) }
    return (Invoke-Native 'node' $arguments) -eq $ExitSuccess
}

function Invoke-ScanStep([string] $Script, [string] $FilesPath) {
    return (Invoke-Native 'node' @((Join-Path $ToolDir $Script), '--root', $FilesPath)) -eq $ExitSuccess
}

function Invoke-GitleaksStep([string] $FilesPath) {
    $gitleaks = Resolve-Gitleaks
    if (-not $gitleaks) {
        Write-Host 'gitleaks not found (PATH or tools\bin\gitleaks.exe): skipped; secret-scan.mjs is the gate.' -ForegroundColor Yellow
        return $true
    }
    return (Invoke-Native $gitleaks @('detect', '--no-git', '--source', $FilesPath, '--redact')) -eq $ExitSuccess
}

# ---------------------------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------------------------
$stagingPath = Resolve-StagingPath
$manifestPath = $DefaultManifest
if ($Manifest) { $manifestPath = [System.IO.Path]::GetFullPath($Manifest) }
$filesPath = Join-Path $stagingPath $StagedFilesFolder
if ($Clean) { Clear-Staging $stagingPath }

$results = New-Object System.Collections.Generic.List[object]
Write-Step "1 copy batch '$Batch' -> $stagingPath"
$copied = Invoke-CopyStep $stagingPath $manifestPath
$results.Add([pscustomobject]@{ Step = '1 copy'; Result = $(if ($copied) { 'PASS' } else { 'FAIL' }) })

if (Test-Path -LiteralPath $filesPath) {
    Write-Step '2 scrub sweep (staging)'
    $results.Add([pscustomobject]@{ Step = '2 scrub sweep'; Result = $(if (Invoke-ScanStep 'scrub-sweep.mjs' $filesPath) { 'PASS' } else { 'FAIL' }) })
    Write-Step '3 secret scan (staging)'
    $secretsClean = (Invoke-ScanStep 'secret-scan.mjs' $filesPath) -and (Invoke-GitleaksStep $filesPath)
    $results.Add([pscustomobject]@{ Step = '3 secret scan'; Result = $(if ($secretsClean) { 'PASS' } else { 'FAIL' }) })
}

Write-Step 'Extract summary'
$results | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Staged files: $filesPath"
Write-Host "Origins:      $(Join-Path $stagingPath 'origins.json')"
if ($results | Where-Object { $_.Result -eq 'FAIL' }) {
    Write-Host 'NOT promotable: fix or rewrite the findings above (staged files keep source-product names until rewritten).' -ForegroundColor Red
    exit $ExitFailure
}
Write-Host 'Clean: promote by hand (README "Promote a batch").' -ForegroundColor Green
exit $ExitSuccess
