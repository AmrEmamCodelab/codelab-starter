<#
.SYNOPSIS
    Runs the kit evals: each fixture's plain-language prompt through Claude Code headless (`claude -p`) in a throwaway
    copy of the working tree, then grades the result (build, rules, content rules, contract tests, docs gate, feature
    spec, expected skill) and aggregates the pass rate (exit criterion: >= 90% over >= 10 conclusive runs).

.DESCRIPTION
    Per run: New-ThrowawayWorkspace under -WorkspaceRoot (%TEMP%), an optional baseline grade of the untouched copy,
    Invoke-HeadlessClaude, the grade, then Remove-ThrowawayWorkspace (marker-verified). Evidence per run goes to
    <EvidenceDir>\runs\<run id>\ (result.json, transcript-summary.json, hook-log.jsonl); full transcripts and check
    logs stay under -WorkspaceRoot (they can be large). <EvidenceDir>\summary.json and summary.md hold the aggregate.

    Exit codes: 0 every conclusive run passed (or the dry run found every fixture valid); 1 a run failed or the
    -RequireCriteria criterion was not met; 2 usage error or an invalid fixture.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Invoke-KitEval.ps1 -Fixture all -DryRun
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Invoke-KitEval.ps1 -Fixture new-lookup -Runs 1 -EvidenceDir docs\evidence\P9\evals
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Invoke-KitEval.ps1 -AggregateOnly -EvidenceDir <dir>
#>
[CmdletBinding()]
param(
    [string[]] $Fixture = @('all'),
    [ValidateRange(1, 100)][int] $Runs = 1,
    [string] $EvidenceDir,
    [switch] $DryRun,
    [switch] $AggregateOnly,
    [switch] $RequireCriteria,
    [switch] $SkipBaseline,
    [switch] $KeepWorkspace,
    [string] $RepoRoot,
    [string] $WorkspaceRoot,
    [ValidateRange(1, 500)][int] $MaxTurns = 80,
    [ValidateRange(1, 600)][int] $TimeoutMinutes = 45,
    [ValidateSet('acceptEdits', 'dontAsk', 'bypassPermissions')][string] $PermissionMode = 'acceptEdits',
    [ValidateSet('Offline', 'None')][string] $NodeModules = 'Offline',
    [string] $Model
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$EvalFolder = $PSScriptRoot
Import-Module (Join-Path $EvalFolder 'lib\HeadlessClaude.psm1') -Force
Import-Module (Join-Path $EvalFolder 'lib\KitEval.psm1') -Force

$ExitPassed = 0
$ExitFailed = 1
$ExitUsage = 2
$WorkspaceFolderName = 'p9-evals'
$RunsFolderName = 'runs'
$BaselineFolderName = 'baseline'
$TranscriptsFolderName = 'transcripts'
$LogsFolderName = 'logs'
$ResultFileName = 'result.json'
$TranscriptSummaryFileName = 'transcript-summary.json'
$HookLogFileName = 'hook-log.jsonl'
$SummaryJsonFileName = 'summary.json'
$SummaryMarkdownFileName = 'summary.md'
$JsonDepth = 8
$RunIdSuffixLength = 8
$PromptPreviewLength = 160
$ContinuousFlag = [Convert]::ToUInt32('80000000', 16)        # ES_CONTINUOUS
$KeepAwakeFlags = [Convert]::ToUInt32('80000001', 16)        # ES_CONTINUOUS | ES_SYSTEM_REQUIRED
$UnattendedNote = 'You are working unattended in a disposable copy of the repository: nobody will answer questions, ' +
    'so make reasonable assumptions, finish the work, verify it, and leave the result in the working tree. Do not push. ' +
    'Node 24 and the .NET 10 SDK are already first on PATH (node, npm and dotnet; CODELAB_DOTNET is set): call them ' +
    'directly. Do not run the full CI script; targeted builds and tests are enough.'

function Get-DefaultRepoRoot { return (Split-Path -Parent (Split-Path -Parent $EvalFolder)) }
function Get-DefaultWorkspaceRoot { return (Join-Path ([IO.Path]::GetTempPath()) $WorkspaceFolderName) }

function Write-JsonFile([string] $Path, [object] $Value) {
    $folder = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth $JsonDepth), (New-Object Text.UTF8Encoding($false)))
}

function Write-TextFile([string] $Path, [string] $Text) {
    $folder = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

# The prompt sent to claude: the fixture's own words plus the unattended note.
function Get-RunPrompt([object] $FixtureObject) {
    return ([string] $FixtureObject.prompt).Trim() + "`n`n" + $UnattendedNote
}

function Get-FixtureLimit([object] $FixtureObject, [string] $Name, [int] $Default) {
    $value = Get-ObjectValue $FixtureObject $Name
    if ($null -ne $value) { return [int] $value }
    return $Default
}

# Loads and validates the selected fixtures; returns @{ Fixtures; Findings } (findings as '<id>: <text>').
function Get-ValidatedFixtures([string[]] $Selection, [string] $Root) {
    $fixtureFolder = Join-Path $EvalFolder 'fixtures'
    $all = (Get-KitEvalConstants).AllFixtures
    $ids = @($Selection | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($ids.Count -eq 0 -or $ids -contains $all) { $ids = @($all) }
    $entries = @($ids | ForEach-Object { Get-KitEvalFixture -FixtureDirectory $fixtureFolder -Id $_ })
    $findings = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $entries) {
        if ($entry.ParseError) { $findings.Add("$($entry.Id): not valid JSON ($($entry.ParseError))"); continue }
        foreach ($finding in @(Test-KitEvalFixture -Fixture $entry.Fixture -ExpectedId $entry.Id -RepoRoot $Root)) {
            $findings.Add("$($entry.Id): $finding")
        }
    }
    return [pscustomobject]@{ Fixtures = @($entries | ForEach-Object { $_.Fixture }); Findings = @($findings) }
}

function Write-DryRunPlan([object[]] $Fixtures, [string] $Root, [string] $WorkspaceBase) {
    $claude = Get-Command claude -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $claudeText = if ($claude) { $claude.Source } else { 'NOT FOUND on PATH (set CODELAB_CLAUDE)' }
    Write-Host "Kit eval plan (dry run: no workspace, no claude call)"
    Write-Host "  repo:        $Root"
    Write-Host "  workspaces:  $WorkspaceBase"
    Write-Host "  claude:      $claudeText"
    Write-Host "  runs:        $Runs per fixture, $(@($Fixtures).Count) fixture(s) = $(@($Fixtures).Count * $Runs) headless run(s)"
    Write-Host "  claude args: $((Get-HeadlessClaudeArguments -MaxTurns $MaxTurns -PermissionMode $PermissionMode -Model $Model) -join ' ')"
    foreach ($item in $Fixtures) {
        $prompt = ([string] $item.prompt).Trim() -replace '\s+', ' '
        if ($prompt.Length -gt $PromptPreviewLength) { $prompt = $prompt.Substring(0, $PromptPreviewLength) + '...' }
        Write-Host ''
        Write-Host "  [$($item.id)] kind=$($item.kind) expects skill '$($item.skill)'"
        Write-Host "    checks: $((Get-OrderedChecks -Fixture $item) -join ', ')"
        $inputs = @(Get-ObjectValue $item 'inputs' | Where-Object { $_ })
        if ($inputs.Count -gt 0) { Write-Host "    inputs: $($inputs -join ', ')" }
        Write-Host "    limits: maxTurns=$(Get-FixtureLimit $item 'maxTurns' $MaxTurns) timeoutMinutes=$(Get-FixtureLimit $item 'timeoutMinutes' $TimeoutMinutes)"
        Write-Host "    prompt: $prompt"
    }
}

function New-RunId([object] $FixtureObject, [int] $RunIndex) {
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, $RunIdSuffixLength)
    return '{0}-r{1}-{2}' -f $FixtureObject.id, $RunIndex, $suffix
}

# One eval run end to end; always returns a result and always removes the workspace it created (unless -KeepWorkspace).
function Invoke-KitEvalRun {
    param([object] $FixtureObject, [int] $RunIndex, [string] $Root, [string] $WorkspaceBase, [string] $Evidence)
    $runId = New-RunId $FixtureObject $RunIndex
    $runEvidence = Join-Path (Join-Path $Evidence $RunsFolderName) $runId
    $logFolder = Join-Path (Join-Path $WorkspaceBase $LogsFolderName) $runId
    $transcriptPath = Join-Path (Join-Path $WorkspaceBase $TranscriptsFolderName) "$runId.jsonl"
    $workspacePath = Join-Path $WorkspaceBase $runId
    $checks = Get-OrderedChecks -Fixture $FixtureObject
    $headless = $null
    $result = $null
    Write-Host "== $runId"
    try {
        $workspace = New-ThrowawayWorkspace -Source $Root -Destination $workspacePath -NodeModules $NodeModules
        $baseline = @()
        if (-not $SkipBaseline) {
            $baselineChecks = @($checks | Where-Object { (Get-KitEvalConstants).BaselineChecks -contains $_ })
            if ($baselineChecks.Count -gt 0) {
                Write-Host "   baseline: $($baselineChecks -join ', ')"
                $baseline = @(Invoke-KitEvalChecks -Checks $baselineChecks -WorkRoot $workspace.Path -BaseRef $workspace.BaseRef `
                        -LogFolder (Join-Path $logFolder $BaselineFolderName))
            }
        }
        Write-Host "   claude -p (expects '$($FixtureObject.skill)')"
        $headless = Invoke-HeadlessClaude -WorkRoot $workspace.Path -Prompt (Get-RunPrompt $FixtureObject) -TranscriptPath $transcriptPath `
            -MaxTurns (Get-FixtureLimit $FixtureObject 'maxTurns' $MaxTurns) -TimeoutMinutes (Get-FixtureLimit $FixtureObject 'timeoutMinutes' $TimeoutMinutes) `
            -HookLogPath (Join-Path $runEvidence $HookLogFileName) -PermissionMode $PermissionMode -Model $Model
        Write-Host "   grade: $($checks -join ', ')"
        $graded = @(Invoke-KitEvalChecks -Checks $checks -WorkRoot $workspace.Path -BaseRef $workspace.BaseRef -LogFolder $logFolder `
                -ExpectedSkill $FixtureObject.skill -SkillsInvoked @($headless.SkillsInvoked))
        $result = New-KitEvalRunResult -Fixture $FixtureObject -RunIndex $RunIndex -Headless $headless -Checks $graded -BaselineChecks $baseline
    } catch {
        $result = New-KitEvalRunResult -Fixture $FixtureObject -RunIndex $RunIndex -Headless $headless -InfrastructureError $_.Exception.Message
    } finally {
        if (-not $KeepWorkspace -and (Test-Path -LiteralPath $workspacePath) -and (Test-ThrowawayWorkspace -Path $workspacePath)) {
            try { Remove-ThrowawayWorkspace -Path $workspacePath } catch { Write-Warning $_.Exception.Message }
        }
    }
    $result | Add-Member -NotePropertyName runId -NotePropertyValue $runId
    $result | Add-Member -NotePropertyName transcriptPath -NotePropertyValue $transcriptPath
    Write-JsonFile (Join-Path $runEvidence $ResultFileName) $result
    if ($null -ne $headless) { Write-JsonFile (Join-Path $runEvidence $TranscriptSummaryFileName) $headless.Summary }
    $outcome = if ($result.inconclusive) { 'INCONCLUSIVE' } elseif ($result.passed) { 'PASS' } else { 'FAIL' }
    Write-Host "   $outcome (skills: $(@($result.skillsInvoked) -join ', '); failed: $(@($result.failedChecks) -join ', '))"
    return $result
}

function Read-RunResults([string] $Evidence) {
    $runsFolder = Join-Path $Evidence $RunsFolderName
    if (-not (Test-Path -LiteralPath $runsFolder)) { return @() }
    return @(Get-ChildItem -LiteralPath $runsFolder -Recurse -Filter $ResultFileName -File | Sort-Object FullName |
            ForEach-Object { [IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json })
}

function Write-Summary([object[]] $Results, [string] $Evidence) {
    $passRate = Get-KitEvalPassRate -Results $Results
    Write-JsonFile (Join-Path $Evidence $SummaryJsonFileName) $passRate
    Write-TextFile (Join-Path $Evidence $SummaryMarkdownFileName) (Format-KitEvalReport -PassRate $passRate -Results $Results)
    Write-Host ''
    Write-Host ("Pass rate {0:P1} ({1}/{2} conclusive, {3} inconclusive): {4}" -f $passRate.passRate, $passRate.passed,
        $passRate.conclusive, $passRate.inconclusive, $passRate.verdict)
    return $passRate
}

# Keeps the machine from sleeping while runs are going on (a sleep stalls builds and tests for hours and defeats the
# timeouts). Uses SetThreadExecutionState; no admin rights, no power-plan change, released when the runs end.
function Set-KeepAwake([bool] $Enabled) {
    if (-not ('KitEvalPower' -as [type])) {
        Add-Type -Namespace '' -Name 'KitEvalPower' -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint esFlags);'
    }
    $flags = if ($Enabled) { $KeepAwakeFlags } else { $ContinuousFlag }
    [void] [KitEvalPower]::SetThreadExecutionState($flags)
}

function Get-ExitCode([object[]] $Results, [object] $PassRate) {
    $failed = @($Results | Where-Object { -not $_.passed -and -not $_.inconclusive })
    if ($failed.Count -gt 0) { return $ExitFailed }
    if ($RequireCriteria -and -not $PassRate.meetsCriteria) { return $ExitFailed }
    return $ExitPassed
}

function Invoke-Main {
    $root = if ($RepoRoot) { [IO.Path]::GetFullPath($RepoRoot) } else { Get-DefaultRepoRoot }
    $workspaceBase = if ($WorkspaceRoot) { [IO.Path]::GetFullPath($WorkspaceRoot) } else { Get-DefaultWorkspaceRoot }
    if ($AggregateOnly) {
        if (-not $EvidenceDir) { Write-Host '-AggregateOnly needs -EvidenceDir.' -ForegroundColor Red; return $ExitUsage }
        $results = Read-RunResults $EvidenceDir
        return (Get-ExitCode $results (Write-Summary $results $EvidenceDir))
    }
    try {
        $validated = Get-ValidatedFixtures $Fixture $root
    } catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        return $ExitUsage
    }
    if (@($validated.Findings).Count -gt 0) {
        Write-Host 'Invalid fixtures:' -ForegroundColor Red
        $validated.Findings | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        return $ExitUsage
    }
    if ($DryRun) {
        Write-DryRunPlan $validated.Fixtures $root $workspaceBase
        Write-Host ''
        Write-Host "$(@($validated.Fixtures).Count) fixture(s) valid."
        return $ExitPassed
    }
    if (-not $EvidenceDir) { Write-Host '-EvidenceDir is required unless -DryRun.' -ForegroundColor Red; return $ExitUsage }
    $evidence = [IO.Path]::GetFullPath($EvidenceDir)
    $results = New-Object System.Collections.Generic.List[object]
    Set-KeepAwake -Enabled $true
    try {
        foreach ($item in $validated.Fixtures) {
            for ($runIndex = 1; $runIndex -le $Runs; $runIndex++) {
                $results.Add((Invoke-KitEvalRun -FixtureObject $item -RunIndex $runIndex -Root $root -WorkspaceBase $workspaceBase -Evidence $evidence))
            }
        }
    } finally {
        Set-KeepAwake -Enabled $false
    }
    # ToArray, not @(): Windows PowerShell 5.1 throws "Argument types do not match" on @() over a List[object] here.
    $runResults = $results.ToArray()
    $passRate = Write-Summary $runResults $evidence
    return (Get-ExitCode $runResults $passRate)
}

# Dot-sourcing (the tests) defines the functions without running anything.
if ($MyInvocation.InvocationName -ne '.') { exit (Invoke-Main) }
