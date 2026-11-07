# Pester 3.4 tests for tools/ai-evals/Invoke-KitEval.ps1 (Windows PowerShell 5.1). The script is dot-sourced (it
# then defines its functions without running); the workspace, the headless run and the grader are mocked, so no
# claude call, no copy of the repository and no build. The dry run and usage errors run the real script.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/ai-evals/tests -EnableExit"
$RunnerPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Invoke-KitEval.ps1'
. $RunnerPath
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

function New-RunnerFixture {
    return ([ordered]@{
            id = 'new-lookup'; kind = 'scaffold'; skill = 'new-lookup'; prompt = 'We need a list of channels.'; inputs = @()
            checks = @('build', 'skillInvoked')
        } | ConvertTo-Json | ConvertFrom-Json)
}

function Invoke-RunnerProcess([string[]] $Arguments) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $RunnerPath @Arguments 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
}

Describe 'Get-RunPrompt' {
    It 'sends the fixture words plus the unattended note' {
        $prompt = Get-RunPrompt (New-RunnerFixture)
        $prompt | Should Match '^We need a list of channels\.'
        $prompt | Should Match 'unattended'
    }
}

Describe 'Invoke-KitEvalRun (workspace, claude and grader mocked)' {
    $evidence = Join-Path $TestDrive 'evidence'
    $workspaceBase = Join-Path $TestDrive 'workspaces'
    Mock New-ThrowawayWorkspace {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
        return [pscustomobject]@{ Path = $Destination; BaseRef = 'eval-base'; MarkerId = 'm' }
    }
    Mock Invoke-HeadlessClaude {
        return [pscustomobject]@{ ExitCode = 0; TranscriptPath = $TranscriptPath; DurationSeconds = 12; SkillsInvoked = @('new-lookup'); TimedOut = $false
            Summary = [pscustomobject]@{ SkillsInvoked = @('new-lookup'); NumTurns = 3 } }
    }
    Mock Invoke-KitEvalChecks { return @($Checks | ForEach-Object { [pscustomobject]@{ name = $_; status = 'passed' } }) }
    Mock Test-ThrowawayWorkspace { return $true }
    Mock Remove-ThrowawayWorkspace { }

    $result = Invoke-KitEvalRun -FixtureObject (New-RunnerFixture) -RunIndex 1 -Root 'X:\repo' -WorkspaceBase $workspaceBase -Evidence $evidence

    It 'grades a passing run and writes result.json and the transcript summary as evidence' {
        $result.passed | Should Be $true
        $runFolder = Join-Path (Join-Path $evidence 'runs') $result.runId
        $written = Get-Content -LiteralPath (Join-Path $runFolder 'result.json') -Raw | ConvertFrom-Json
        $written.fixture | Should Be 'new-lookup'
        $written.passed | Should Be $true
        Test-Path -LiteralPath (Join-Path $runFolder 'transcript-summary.json') | Should Be $true
    }
    It 'keeps the transcript outside the evidence folder and points the hook log into it' {
        $result.transcriptPath | Should Match ([regex]::Escape((Join-Path $workspaceBase 'transcripts')))
        Assert-MockCalled Invoke-HeadlessClaude -Times 1 -Exactly -ParameterFilter {
            $HookLogPath -like "$evidence*hook-log.jsonl" -and $WorkRoot -like "$workspaceBase*" -and $Prompt -match 'channels'
        }
    }
    It 'runs a baseline grade of the untouched workspace, then the full grade' {
        Assert-MockCalled Invoke-KitEvalChecks -Times 1 -Exactly -ParameterFilter { ($Checks -join ',') -eq 'build' -and $LogFolder -like '*baseline' }
        Assert-MockCalled Invoke-KitEvalChecks -Times 1 -Exactly -ParameterFilter { ($Checks -join ',') -eq 'build,skillInvoked' -and $ExpectedSkill -eq 'new-lookup' }
    }
    It 'removes the workspace it created' {
        Assert-MockCalled Remove-ThrowawayWorkspace -Times 1 -Exactly
    }
}

Describe 'Invoke-KitEvalRun when the headless run throws' {
    Mock New-ThrowawayWorkspace {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
        return [pscustomobject]@{ Path = $Destination; BaseRef = 'eval-base'; MarkerId = 'm' }
    }
    Mock Invoke-HeadlessClaude { throw 'claude was not found on PATH' }
    Mock Invoke-KitEvalChecks { return @() }
    Mock Test-ThrowawayWorkspace { return $true }
    Mock Remove-ThrowawayWorkspace { }

    $result = Invoke-KitEvalRun -FixtureObject (New-RunnerFixture) -RunIndex 2 -Root 'X:\repo' -WorkspaceBase (Join-Path $TestDrive 'ws2') -Evidence (Join-Path $TestDrive 'ev2')

    It 'records an inconclusive run with the error and still removes the workspace' {
        $result.inconclusive | Should Be $true
        $result.infrastructureError | Should Match 'not found'
        Assert-MockCalled Remove-ThrowawayWorkspace -Times 1 -Exactly
    }
}

Describe 'Invoke-KitEvalRun with -KeepWorkspace' {
    Mock New-ThrowawayWorkspace {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
        return [pscustomobject]@{ Path = $Destination; BaseRef = 'eval-base'; MarkerId = 'm' }
    }
    Mock Invoke-HeadlessClaude { return [pscustomobject]@{ ExitCode = 0; DurationSeconds = 1; SkillsInvoked = @(); TimedOut = $false; Summary = $null } }
    Mock Invoke-KitEvalChecks { return @() }
    Mock Remove-ThrowawayWorkspace { }
    $KeepWorkspace = $true
    $null = Invoke-KitEvalRun -FixtureObject (New-RunnerFixture) -RunIndex 1 -Root 'X:\repo' -WorkspaceBase (Join-Path $TestDrive 'ws3') -Evidence (Join-Path $TestDrive 'ev3')
    $KeepWorkspace = $false
    It 'leaves the workspace for inspection' {
        Assert-MockCalled Remove-ThrowawayWorkspace -Times 0 -Exactly
    }
}

Describe 'Get-ExitCode' {
    $passRate = [pscustomobject]@{ meetsCriteria = $false }
    It 'is 0 when every conclusive run passed and 1 when one failed' {
        Get-ExitCode @([pscustomobject]@{ passed = $true; inconclusive = $false }, [pscustomobject]@{ passed = $false; inconclusive = $true }) $passRate | Should Be 0
        Get-ExitCode @([pscustomobject]@{ passed = $false; inconclusive = $false }) $passRate | Should Be 1
    }
    It 'is 1 under -RequireCriteria when the criterion is not met' {
        $RequireCriteria = $true
        Get-ExitCode @([pscustomobject]@{ passed = $true; inconclusive = $false }) $passRate | Should Be 1
        $RequireCriteria = $false
    }
}

Describe 'Write-Summary and -AggregateOnly' {
    $evidence = Join-Path $TestDrive 'aggregate'
    It 'aggregates the result.json files already in the evidence folder' {
        foreach ($index in 1..3) {
            $folder = Join-Path $evidence "runs\fx-r$index"
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
            $passed = if ($index -lt 3) { 'true' } else { 'false' }
            [IO.File]::WriteAllText((Join-Path $folder 'result.json'), "{`"fixture`":`"fx`",`"run`":$index,`"passed`":$passed,`"inconclusive`":false,`"expectedSkill`":`"new-lookup`",`"skillsInvoked`":[],`"failedChecks`":[],`"durationSeconds`":1}")
        }
        $results = Read-RunResults $evidence
        @($results).Count | Should Be 3
        $rate = Write-Summary $results $evidence
        $rate.passed | Should Be 2
        Test-Path -LiteralPath (Join-Path $evidence 'summary.json') | Should Be $true
        (Get-Content -LiteralPath (Join-Path $evidence 'summary.md') -Raw) | Should Match 'Pass rate'
    }
}

Describe 'Invoke-KitEval.ps1 as a process' {
    It 'validates every fixture and prints the plan on -DryRun without calling claude' {
        $run = Invoke-RunnerProcess @('-Fixture', 'all', '-DryRun')
        $run.ExitCode | Should Be 0
        $run.Output | Should Match '10 fixture\(s\) valid'
        $run.Output | Should Match 'dry run: no workspace, no claude call'
    }
    It 'accepts a comma-separated fixture list' {
        $run = Invoke-RunnerProcess @('-Fixture', 'new-lookup,new-permission', '-DryRun')
        $run.ExitCode | Should Be 0
        $run.Output | Should Match '2 fixture\(s\) valid'
    }
    It 'exits 2 for an unknown fixture and for a real run without -EvidenceDir' {
        (Invoke-RunnerProcess @('-Fixture', 'does-not-exist', '-DryRun')).ExitCode | Should Be 2
        (Invoke-RunnerProcess @('-Fixture', 'new-lookup')).ExitCode | Should Be 2
    }
}

Describe 'Set-KeepAwake' {
    It 'holds and releases the system-required state without admin rights' {
        { Set-KeepAwake -Enabled $true; Set-KeepAwake -Enabled $false } | Should Not Throw
    }
}

Describe 'Invoke-Main over real run results (runs mocked)' {
    Mock Set-KeepAwake { }
    Mock Invoke-KitEvalRun {
        return [pscustomobject]@{ fixture = $FixtureObject.id; run = $RunIndex; passed = $true; inconclusive = $false; expectedSkill = $FixtureObject.skill
            skillsInvoked = @($FixtureObject.skill); failedChecks = @(); durationSeconds = 1; checks = @([pscustomobject]@{ name = 'build'; status = 'passed' }) }
    }
    It 'aggregates the results of a List and writes the summary (regression: @() over List[object] threw in 5.1)' {
        $Fixture = @('new-lookup,new-permission')
        $Runs = 2
        $DryRun = $false
        $EvidenceDir = Join-Path $TestDrive 'main-evidence'
        Invoke-Main | Should Be 0
        Assert-MockCalled Invoke-KitEvalRun -Times 4 -Exactly
        $summary = Get-Content -LiteralPath (Join-Path $EvidenceDir 'summary.json') -Raw | ConvertFrom-Json
        $summary.runs | Should Be 4
        $summary.passRate | Should Be 1
        Assert-MockCalled Set-KeepAwake -Times 1 -Exactly -ParameterFilter { $Enabled -eq $false }
        $Fixture = @('all'); $Runs = 1; $EvidenceDir = $null
    }
}
