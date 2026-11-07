<#
.SYNOPSIS
    Generates the Permit proof's HLS-format workbook (inputs/permit/permit-use-cases.xlsx) from its JSON source,
    without Excel, Python or any module (System.IO.Compression, Windows PowerShell 5.1).

.DESCRIPTION
    Reads the sheet definitions in -DataPath ({ "sheets": [ { "name", "rows": [[cell, ...], ...] } ] }) and writes a
    text-only .xlsx (shared strings, bold header row) that .claude/skills/usecase-testing/scripts/Read-Xlsx.ps1 reads
    back. The output is byte-stable: rerunning it on unchanged data gives the same file. Commit the regenerated xlsx
    together with the JSON change.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\build-permit-workbook.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\build-permit-workbook.ps1 -OutFile $env:TEMP\permit.xlsx
#>
[CmdletBinding()]
param(
    [string] $DataPath,
    [string] $OutFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
$DefaultDataPath = 'inputs\permit\permit-use-cases.json'
$DefaultOutFile = 'inputs\permit\permit-use-cases.xlsx'
if (-not $DataPath) { $DataPath = Join-Path $ScriptFolder $DefaultDataPath }
if (-not $OutFile) { $OutFile = Join-Path $ScriptFolder $DefaultOutFile }

Import-Module (Join-Path $ScriptFolder 'lib\OoxmlWorkbook.psm1') -Force

if (-not (Test-Path -LiteralPath $DataPath -PathType Leaf)) { throw "Workbook data $DataPath does not exist." }
$json = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $DataPath).ProviderPath, [Text.Encoding]::UTF8)
$sheets = @(ConvertFrom-WorkbookDefinition -Json $json)
$written = New-OoxmlWorkbook -Sheets $sheets -Path $OutFile
Write-Host "Wrote $written ($($sheets.Count) sheets: $(@($sheets | ForEach-Object { $_.Name }) -join ', '))"
