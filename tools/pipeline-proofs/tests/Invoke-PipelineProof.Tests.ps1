# Pester 3.4 tests for Invoke-PipelineProof (tools/pipeline-proofs/lib/PipelineProof.psm1) and the
# Invoke-PipelineProof.ps1 entry script (Windows PowerShell 5.1). HeadlessClaude and KitEval are mocked inside the
# module, so no workspace copy, no claude call and no build runs here; the dry run uses the real refusal probe.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/pipeline-proofs/tests -EnableExit"
Set-StrictMode -Version Latest

$ToolFolder = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ToolFolder)
$EntryScript = Join-Path $ToolFolder 'Invoke-PipelineProof.ps1'
$NodeDirectory = 'E:\codelab-tools\node24'
Import-Module (Join-Path $ToolFolder 'lib\PipelineProof.psm1') -Force

$SpecFixture = @(
    '---', 'feature: site-visit-permit', 'status: approved', 'approvedBy: pipeline-proof (pre-approved)', 'approvedOn: 2026-11-07', '---',
    '## Review log', '', '| Iteration | Reviewer | Finding | Resolution |', '| --- | --- | --- | --- |',
    '| 1 | standards-reviewer | magic number | constant |', '', 'Iterations: 1'
) -join "`n"

# Mock bodies run outside this script's scope in Pester 3.4, so the fixtures reach them through these two globals
# (removed at the end of the file).
$global:PipelineProofTestSpec = $SpecFixture
$global:PipelineProofTestTranscript = [string[]] @(
    foreach ($step in @('build-feature', 'new-module', 'new-transition', 'deadline-recipe', 'new-permission', 'new-endpoint', 'new-screen')) {
        $toolUse = if ($step -eq 'deadline-recipe') { @{ type = 'tool_use'; name = 'Read'; input = @{ file_path = 'docs/recipes/add-deadline.md' } } }
        else { @{ type = 'tool_use'; name = 'Skill'; input = @{ skill = $step } } }
        ConvertTo-Json -InputObject @{ type = 'assistant'; message = @{ content = @($toolUse) } } -Depth 6 -Compress
    }
)

function Set-RunMocks([string] $WorkspaceRoot) {
    Mock -ModuleName PipelineProof New-ThrowawayWorkspace {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
        $specFolder = Join-Path $Destination 'docs\features'
        New-Item -ItemType Directory -Path $specFolder -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $specFolder 'site-visit-permit.md'), $global:PipelineProofTestSpec)
        $visualFolder = Join-Path $Destination 'docs\evidence\P9\visual\iteration-1'
        New-Item -ItemType Directory -Path $visualFolder -Force | Out-Null
        $shots = foreach ($language in @('ar', 'en')) { foreach ($width in @(1440, 390)) { @{ route = 'permit-new'; language = $language; width = $width; ratio = 0.02; status = 'pass' } } }
        [IO.File]::WriteAllText((Join-Path $visualFolder 'visual-report.json'), (ConvertTo-Json -InputObject @{ mode = 'target'; iteration = 1; shots = @($shots) } -Depth 5))
        return [pscustomobject]@{ Path = $Destination; BaseRef = 'eval-base'; MarkerId = 'm' }
    }
    Mock -ModuleName PipelineProof Invoke-RefusalProbe { [pscustomobject]@{ path = 'p'; decision = 'deny'; reason = 'Use the `new-module` skill'; namesSkill = $true; loggedSkill = 'new-module'; passed = $true } }
    Mock -ModuleName PipelineProof Invoke-HeadlessClaude {
        New-Item -ItemType Directory -Path (Split-Path -Parent $TranscriptPath) -Force | Out-Null; [IO.File]::WriteAllLines($TranscriptPath, $global:PipelineProofTestTranscript)
        $deny = '{"ts":"t1","hook":"pre-tool-use","tool":"Write","path":"backend/src/Modules/Permits/Api/PermitsController.cs","decision":"deny","ruleIds":["base.endpoint-has-permission"],"skill":"new-endpoint"}'
        $allow = '{"ts":"t2","hook":"pre-tool-use","tool":"Edit","path":"backend/src/Modules/Permits/Api/PermitsController.cs","decision":"allow","ruleIds":[],"skill":null}'
        [IO.File]::WriteAllLines($HookLogPath, [string[]] @($deny, $allow))
        return [pscustomobject]@{ ExitCode = 0; TimedOut = $false; DurationSeconds = 1; SkillsInvoked = @('build-feature'); TranscriptPath = $TranscriptPath; Summary = [pscustomobject]@{ NumTurns = 3 } }
    }
    Mock -ModuleName PipelineProof Get-WorkspaceChangedPaths { @('docs/features/site-visit-permit.md', 'docs/evidence/P9/visual/iteration-1/visual-report.json') }
    Mock -ModuleName PipelineProof Invoke-ProofSpecCheck { [pscustomobject]@{ path = $SpecPath; exitCode = 0; ok = $true; status = 'approved'; handEditCount = 3; errorCount = 0; findings = @(); detail = '' } }
    Mock -ModuleName PipelineProof Invoke-KitEvalChecks { @($Checks | ForEach-Object { [pscustomobject]@{ name = $_; status = 'passed'; exitCode = 0; detail = '' } }) }
    Mock -ModuleName PipelineProof Test-ThrowawayWorkspace { $true }
    Mock -ModuleName PipelineProof Remove-ThrowawayWorkspace { Remove-Item -LiteralPath $Path -Recurse -Force }
}

Describe 'Invoke-PipelineProof -DryRun' {
    Mock -ModuleName PipelineProof New-ThrowawayWorkspace { throw 'no workspace in a dry run' }
    Mock -ModuleName PipelineProof Invoke-HeadlessClaude { throw 'no claude in a dry run' }

    It 'validates the permit inputs, runs the read-only probe and writes the report without a claude call' {
        $evidence = Join-Path $TestDrive 'dry-permit'
        $report = Invoke-PipelineProof -Proof permit -DryRun -EvidenceDir $evidence -RepoRoot $RepoRoot -NodeDirectory $NodeDirectory
        $report.verdict | Should BeExactly 'dry-run'
        $report.exitCode | Should Be 0
        @($report.workbook.useCaseIds).Count | Should Be 2
        $report.refusalProbe.passed | Should Be $true
        Test-Path (Join-Path $evidence 'report.json') | Should Be $true
        Test-Path (Join-Path $evidence 'report.md') | Should Be $true
        Assert-MockCalled -ModuleName PipelineProof Invoke-HeadlessClaude -Times 0 -Exactly -Scope It
        Assert-MockCalled -ModuleName PipelineProof New-ThrowawayWorkspace -Times 0 -Exactly -Scope It
    }
    It 'reports the feedback proof as blocked without a Figma URL' {
        $report = Invoke-PipelineProof -Proof feedback -DryRun -EvidenceDir (Join-Path $TestDrive 'dry-feedback') -RepoRoot $RepoRoot -NodeDirectory $NodeDirectory
        $report.verdict | Should BeExactly 'blocked'
        $report.exitCode | Should Be 2
    }
}

Describe 'Invoke-PipelineProof.ps1 -DryRun (entry script)' {
    It 'exits 0 for the permit proof and leaves report.json' {
        $evidence = Join-Path $TestDrive 'script-dry'
        $env:PATH = "$NodeDirectory;$env:PATH"
        & powershell -NoProfile -ExecutionPolicy Bypass -File $EntryScript -Proof permit -DryRun -EvidenceDir $evidence | Out-Null
        $LASTEXITCODE | Should Be 0
        $report = [IO.File]::ReadAllText((Join-Path $evidence 'report.json')) | ConvertFrom-Json
        $report.verdict | Should BeExactly 'dry-run'
        $report.headless | Should BeNullOrEmpty
    }
    It 'exits 2 for an unknown proof' {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $EntryScript -Proof orders -DryRun -EvidenceDir (Join-Path $TestDrive 'x') 2>&1 | Out-Null
        $LASTEXITCODE | Should Not Be 0
    }
}

Describe 'Invoke-PipelineProof (mocked run)' {
    It 'collects every criterion into a passing report and removes the workspace' {
        $workRoot = Join-Path $TestDrive 'work'
        $evidence = Join-Path $TestDrive 'run-permit'
        Set-RunMocks -WorkspaceRoot $workRoot
        $report = Invoke-PipelineProof -Proof permit -EvidenceDir $evidence -WorkRoot $workRoot -RepoRoot $RepoRoot -PreApproveSpec -NodeDirectory $NodeDirectory
        $failed = @($report.criteria | Where-Object { -not $_.passed } | ForEach-Object { "$($_.id): $($_.detail)" })
        ($failed -join '; ') | Should BeExactly ''
        $report.verdict | Should BeExactly 'pass'
        $report.handEdits.count | Should Be 3
        $report.review.iterations | Should Be 1
        @($report.hookProof.corrected).Count | Should Be 1
        $report.visual.worstRatio | Should Be 0.02
        Test-Path (Join-Path $evidence 'spec.md') | Should Be $true
        Test-Path (Join-Path $evidence 'visual\1-visual-report.json') | Should Be $true
        Test-Path $report.workspace.path | Should Be $false
        Assert-MockCalled -ModuleName PipelineProof Invoke-HeadlessClaude -Times 1 -Exactly -Scope It -ParameterFilter { $Prompt -match 'pre-approved' -and $HookLogPath -like '*hook-log.jsonl' }
        Assert-MockCalled -ModuleName PipelineProof Invoke-HeadlessClaude -Times 1 -Exactly -Scope It -ParameterFilter { $ExtraEnvironment -and $ExtraEnvironment['CODELAB_PIPELINE_PROOF_APPROVAL'] -eq '1' }
        Assert-MockCalled -ModuleName PipelineProof Invoke-ProofSpecCheck -Times 1 -Exactly -Scope It -ParameterFilter { $RequireApproved -and $SpecPath -eq 'docs/features/site-visit-permit.md' }
        Assert-MockCalled -ModuleName PipelineProof Invoke-KitEvalChecks -Times 1 -Exactly -Scope It -ParameterFilter { ($Checks -join ',') -eq 'build,rulesCheck,contentRules,docsGate' }
    }
    It 'refuses an evidence folder that already holds a run' {
        $evidence = Join-Path $TestDrive 'run-permit'
        Set-RunMocks -WorkspaceRoot (Join-Path $TestDrive 'work2')
        { Invoke-PipelineProof -Proof permit -EvidenceDir $evidence -WorkRoot (Join-Path $TestDrive 'work2') -RepoRoot $RepoRoot } | Should Throw 'already holds a proof run'
    }
    It 'fails with the infrastructure error and still removes the workspace when the headless run throws' {
        $workRoot = Join-Path $TestDrive 'work3'
        Set-RunMocks -WorkspaceRoot $workRoot
        Mock -ModuleName PipelineProof Invoke-HeadlessClaude { throw 'claude not found' }
        $report = Invoke-PipelineProof -Proof permit -EvidenceDir (Join-Path $TestDrive 'run-fail') -WorkRoot $workRoot -RepoRoot $RepoRoot -NodeDirectory $NodeDirectory
        Assert-MockCalled -ModuleName PipelineProof Invoke-HeadlessClaude -Times 0 -Exactly -Scope It -ParameterFilter { $ExtraEnvironment -and $ExtraEnvironment.ContainsKey('CODELAB_PIPELINE_PROOF_APPROVAL') }
        $report.verdict | Should BeExactly 'fail'
        $report.exitCode | Should Be 1
        $report.infrastructureError | Should Match 'claude not found'
        Test-Path $report.workspace.path | Should Be $false
        Assert-MockCalled -ModuleName PipelineProof Remove-ThrowawayWorkspace -Times 1 -Exactly -Scope It
    }
    It 'keeps the workspace with -KeepWorkspace' {
        $workRoot = Join-Path $TestDrive 'work4'
        Set-RunMocks -WorkspaceRoot $workRoot
        $report = Invoke-PipelineProof -Proof complaint -EvidenceDir (Join-Path $TestDrive 'run-keep') -WorkRoot $workRoot -RepoRoot $RepoRoot -KeepWorkspace -NodeDirectory $NodeDirectory
        Test-Path $report.workspace.path | Should Be $true
        Assert-MockCalled -ModuleName PipelineProof Remove-ThrowawayWorkspace -Times 0 -Exactly -Scope It
    }
}

Remove-Variable -Name PipelineProofTestSpec, PipelineProofTestTranscript -Scope Global -ErrorAction SilentlyContinue
