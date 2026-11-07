<#
.SYNOPSIS
    Runs one Phase 9 pipeline proof (complaint, permit or feedback): the plain business scenario of
    tools/pipeline-proofs/inputs/<proof>/ through Claude Code headless in a throwaway workspace, then writes
    report.json and report.md to -EvidenceDir.

.DESCRIPTION
    Flow: validate the inputs (plain scenario, input files, the workbook's use-case count) -> New-ThrowawayWorkspace
    under -WorkRoot (%TEMP%) -> refusal probe (a hand-made module file through .claude/hooks/pre-tool-use.mjs must be
    refused, naming new-module) -> Invoke-HeadlessClaude with CODELAB_HOOK_LOG -> skill sequence from the transcript,
    the new feature spec (check-spec --json, hand-edit count, Review log), the hook log (a deny, then the corrected
    allow on the same file), the gates in the workspace (dotnet build -warnaserror, rules check, content rules, docs
    gate), the visual-verifier target reports -> report -> Remove-ThrowawayWorkspace.

    -DryRun validates the inputs and runs the refusal probe against the source tree (read-only); it starts no
    workspace and no claude. Exit codes: 0 pass (or dry run ok), 1 a criterion failed, 2 invalid inputs or blocked
    (the feedback proof without -FigmaUrl).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\Invoke-PipelineProof.ps1 -Proof permit -DryRun -EvidenceDir $env:TEMP\p9-proof-dry
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\Invoke-PipelineProof.ps1 -Proof permit -PreApproveSpec -EvidenceDir docs\evidence\P9\<folder>\permit
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('complaint', 'permit', 'feedback')][string] $Proof,
    [Parameter(Mandatory = $true)][string] $EvidenceDir,
    [string] $WorkRoot,
    [switch] $DryRun,
    [switch] $PreApproveSpec,
    [switch] $KeepWorkspace,
    [string] $FigmaUrl,
    [ValidateRange(0, 1000)][int] $MaxTurns = 0,
    [ValidateRange(0, 1440)][double] $TimeoutMinutes = 0,
    [ValidateSet('acceptEdits', 'dontAsk', 'bypassPermissions')][string] $PermissionMode = 'acceptEdits',
    [string] $Model,
    [string] $RepoRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitUsage = 2
$ScriptFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $ScriptFolder 'lib\PipelineProof.psm1') -Force

$parameters = @{
    Proof          = $Proof
    EvidenceDir    = $EvidenceDir
    DryRun         = $DryRun
    PreApproveSpec = $PreApproveSpec
    KeepWorkspace  = $KeepWorkspace
    PermissionMode = $PermissionMode
}
foreach ($name in @('WorkRoot', 'FigmaUrl', 'Model', 'RepoRoot')) {
    $value = Get-Variable -Name $name -ValueOnly
    if ($value) { $parameters[$name] = $value }
}
if ($MaxTurns -gt 0) { $parameters['MaxTurns'] = $MaxTurns }
if ($TimeoutMinutes -gt 0) { $parameters['TimeoutMinutes'] = $TimeoutMinutes }

try {
    $report = Invoke-PipelineProof @parameters
} catch {
    Write-Host "Pipeline proof refused: $($_.Exception.Message)" -ForegroundColor Red
    exit $ExitUsage
}

Write-Host "Pipeline proof '$($report.proof)' ($($report.mode)): $($report.verdict.ToUpperInvariant())"
foreach ($finding in @($report.findings)) { Write-Host "  input finding: $finding" -ForegroundColor Yellow }
foreach ($blocker in @($report.blockers)) { Write-Host "  blocked: $blocker" -ForegroundColor Yellow }
foreach ($criterion in @($report.criteria)) {
    $mark = if ($criterion.passed) { 'pass' } else { 'FAIL' }
    Write-Host ("  [{0}] {1}" -f $mark, $criterion.description)
}
Write-Host "Report: $(Join-Path ([IO.Path]::GetFullPath($EvidenceDir)) 'report.md')"
exit $report.exitCode
