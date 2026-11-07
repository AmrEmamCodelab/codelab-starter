# Pester 3.4 tests for tools/ai-evals/lib/KitEval.psm1 (Windows PowerShell 5.1): the fixture schema (including every
# real fixture in tools/ai-evals/fixtures), the grader commands and scoring, and the pass-rate math. No process runs:
# Invoke-LoggedProcess is mocked inside the module.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/ai-evals/tests -EnableExit"
Set-StrictMode -Version Latest

$EvalFolder = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $EvalFolder)
$FixtureFolder = Join-Path $EvalFolder 'fixtures'
Import-Module (Join-Path $EvalFolder 'lib\HeadlessClaude.psm1') -Force
Import-Module (Join-Path $EvalFolder 'lib\KitEval.psm1') -Force

function New-TestFixture([hashtable] $Overrides = @{}) {
    $fixture = [ordered]@{
        id     = 'sample'
        kind   = 'scaffold'
        skill  = 'new-lookup'
        prompt = 'We need a list of submission channels with Arabic and English names.'
        inputs = @()
        checks = @('build', 'docsGate', 'skillInvoked')
    }
    foreach ($key in $Overrides.Keys) { $fixture[$key] = $Overrides[$key] }
    # Round-trip through JSON so the object has the same shape as a fixture file.
    return ($fixture | ConvertTo-Json -Depth 4 | ConvertFrom-Json)
}

function New-TestCheck([string] $Name, [string] $Status) { return [pscustomobject]@{ name = $Name; status = $Status } }

function New-TestResult([string] $Fixture, [bool] $Passed, [bool] $Inconclusive = $false) {
    return [pscustomobject]@{ fixture = $Fixture; passed = $Passed; inconclusive = $Inconclusive }
}

Describe 'Test-KitEvalFixture (schema)' {
    It 'accepts a valid scaffold fixture' {
        @(Test-KitEvalFixture -Fixture (New-TestFixture) -ExpectedId 'sample' -RepoRoot $RepoRoot).Count | Should Be 0
    }
    It 'reports missing and unknown keys' {
        $fixture = New-TestFixture
        $fixture.PSObject.Properties.Remove('checks')
        $fixture | Add-Member -NotePropertyName extra -NotePropertyValue 1
        $findings = @(Test-KitEvalFixture -Fixture $fixture -ExpectedId 'sample' -RepoRoot $RepoRoot)
        ($findings -join '|') | Should Match "missing key 'checks'"
        ($findings -join '|') | Should Match "unknown key 'extra'"
    }
    It 'requires the id to match the file name and be kebab-case' {
        (@(Test-KitEvalFixture -Fixture (New-TestFixture) -ExpectedId 'other' -RepoRoot $RepoRoot) -join '|') | Should Match 'does not match the file name'
        (@(Test-KitEvalFixture -Fixture (New-TestFixture @{ id = 'Bad_Id' }) -ExpectedId 'Bad_Id' -RepoRoot $RepoRoot) -join '|') | Should Match 'kebab-case'
    }
    It 'refuses an unknown kind, a scaffold fixture for a non-scaffold skill and an input fixture not routed to build-feature' {
        (@(Test-KitEvalFixture -Fixture (New-TestFixture @{ kind = 'other' }) -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Match "kind 'other'"
        (@(Test-KitEvalFixture -Fixture (New-TestFixture @{ skill = 'do-work' }) -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Match 'scaffold fixture expects'
        $inputFixture = New-TestFixture @{ kind = 'input'; skill = 'new-screen'; inputs = @('CLAUDE.md'); checks = @('specValid', 'skillInvoked') }
        (@(Test-KitEvalFixture -Fixture $inputFixture -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Match "expects the skill 'build-feature'"
    }
    It 'refuses a slash command or the skill name in the prompt' {
        (@(Test-KitEvalFixture -Fixture (New-TestFixture @{ prompt = 'Run /new-lookup now' }) -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Match 'slash command'
        (@(Test-KitEvalFixture -Fixture (New-TestFixture @{ prompt = 'use new-lookup please' }) -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Match 'names the expected skill'
    }
    It 'does not mistake a repo path in the prompt for a slash command' {
        $fixture = New-TestFixture @{ prompt = 'Read tools/ai-evals/fixtures/inputs/brd-equipment-loan.md and plan it.' }
        (@(Test-KitEvalFixture -Fixture $fixture -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Not Match 'slash command'
    }
    It 'requires inputs to be existing repo-relative files' {
        $fixture = New-TestFixture @{ inputs = @('C:\abs.md', '../outside.md', 'docs/nope.md') }
        $text = @(Test-KitEvalFixture -Fixture $fixture -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|'
        $text | Should Match "'C:\\abs.md' is not repo-relative"
        $text | Should Match "'../outside.md' is not repo-relative"
        $text | Should Match "'docs/nope.md' does not exist"
    }
    It 'requires known checks, skillInvoked always, and specValid plus an input for input fixtures' {
        (@(Test-KitEvalFixture -Fixture (New-TestFixture @{ checks = @('build', 'lint') }) -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Match "unknown check 'lint'"
        (@(Test-KitEvalFixture -Fixture (New-TestFixture @{ checks = @('build') }) -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Match "must include 'skillInvoked'"
        $inputFixture = New-TestFixture @{ kind = 'input'; skill = 'build-feature'; checks = @('skillInvoked') }
        $text = @(Test-KitEvalFixture -Fixture $inputFixture -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|'
        $text | Should Match "must include 'specValid'"
        $text | Should Match 'at least one input file'
    }
    It 'requires positive integer limits when given' {
        (@(Test-KitEvalFixture -Fixture (New-TestFixture @{ maxTurns = 0 }) -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Match 'maxTurns must be a positive integer'
        @(Test-KitEvalFixture -Fixture (New-TestFixture @{ timeoutMinutes = 30 }) -ExpectedId 'sample' -RepoRoot $RepoRoot).Count | Should Be 0
    }
    It 'reports an empty fixture' {
        (@(Test-KitEvalFixture -Fixture $null -ExpectedId 'sample' -RepoRoot $RepoRoot) -join '|') | Should Match 'empty or not JSON'
    }
}

Describe 'The shipped fixtures' {
    $entries = @(Get-KitEvalFixture -FixtureDirectory $FixtureFolder)
    $constants = Get-KitEvalConstants

    It 'are all valid' {
        foreach ($entry in $entries) {
            $entry.ParseError | Should Be $null
            (@(Test-KitEvalFixture -Fixture $entry.Fixture -ExpectedId $entry.Id -RepoRoot $RepoRoot) -join '|') | Should Be ''
        }
    }
    It 'cover every scaffold skill once' {
        $scaffold = @($entries | Where-Object { $_.Fixture.kind -eq 'scaffold' } | ForEach-Object { $_.Fixture.skill } | Sort-Object)
        ($scaffold -join ',') | Should Be ((@($constants.ScaffoldSkills) | Sort-Object) -join ',')
    }
    It 'cover the four input types (BRD, use-case workbook, DGA screenshot, Figma frame)' {
        $inputIds = @($entries | Where-Object { $_.Fixture.kind -eq 'input' } | ForEach-Object { $_.Id } | Sort-Object)
        ($inputIds -join ',') | Should Be 'input-brd,input-dga-screenshot,input-figma-frame,input-usecase-workbook'
    }
    It 'reads one fixture by id and refuses an unknown id' {
        @(Get-KitEvalFixture -FixtureDirectory $FixtureFolder -Id 'new-lookup')[0].Fixture.skill | Should Be 'new-lookup'
        { Get-KitEvalFixture -FixtureDirectory $FixtureFolder -Id 'does-not-exist' } | Should Throw
    }
}

Describe 'Get-OrderedChecks and Get-KitEvalCheckCommand' {
    It 'orders checks so the build runs before the contract tests and the skill check last' {
        $fixture = New-TestFixture @{ checks = @('skillInvoked', 'contractTests', 'build') }
        (Get-OrderedChecks -Fixture $fixture) -join ',' | Should Be 'build,contractTests,skillInvoked'
    }
    $common = @{ WorkRoot = 'X:\ws'; ArtifactsPath = 'X:\ws\artifacts'; BaseRef = 'eval-base'; DotnetPath = 'X:\dotnet.exe'; NodePath = 'X:\node.exe' }
    It 'builds the solution with warnings as errors into the workspace artifacts' {
        $command = Get-KitEvalCheckCommand -Check 'build' @common
        $command.FilePath | Should Be 'X:\dotnet.exe'
        ($command.Arguments -join ' ') | Should Be 'build codelab-starter.slnx -warnaserror --artifacts-path X:\ws\artifacts -nologo'
    }
    It 'runs the rules drift check and the content rules against the workspace' {
        ((Get-KitEvalCheckCommand -Check 'rulesCheck' @common).Arguments -join ' ') | Should Be 'tools/rules-engine/compile.mjs --check --root X:\ws'
        ((Get-KitEvalCheckCommand -Check 'contentRules' @common).Arguments -join ' ') | Should Be 'tools/rules-engine/check-content.mjs --root X:\ws'
    }
    It 'runs the contract tests and the docs gate against the snapshot base' {
        ((Get-KitEvalCheckCommand -Check 'contractTests' @common).Arguments -join ' ') | Should Match '^test backend/tests/Starter.Contract.Tests --artifacts-path'
        ((Get-KitEvalCheckCommand -Check 'docsGate' @common).Arguments -join ' ') | Should Match '-- check --root \. --base eval-base$'
    }
    It 'refuses a check that is not a process check' {
        { Get-KitEvalCheckCommand -Check 'skillInvoked' @common } | Should Throw
    }
}

Describe 'Test-SkillInvoked and Select-FeatureSpecPaths' {
    It 'passes when the expected skill is among the invoked ones' {
        (Test-SkillInvoked -ExpectedSkill 'new-lookup' -SkillsInvoked @('coding-standards', 'new-lookup')).status | Should Be 'passed'
    }
    It 'fails and names what was invoked otherwise' {
        $result = Test-SkillInvoked -ExpectedSkill 'new-lookup' -SkillsInvoked @('do-work')
        $result.status | Should Be 'failed'
        $result.detail | Should Match 'invoked: do-work'
        (Test-SkillInvoked -ExpectedSkill 'new-lookup').detail | Should Match 'invoked: none'
    }
    It 'keeps only top-level feature specs, without the index and the template' {
        $paths = @('docs/features/equipment-loan.md', 'docs/features/README.md', 'docs/features/TEMPLATE.md',
            'docs/features/examples/sample.md', 'docs/other.md', 'docs/features/equipment-loan.md', 'docs/features/x.txt')
        (Select-FeatureSpecPaths $paths) -join ',' | Should Be 'docs/features/equipment-loan.md'
    }
}

Describe 'Invoke-KitEvalChecks (processes mocked)' {
    $workRoot = Join-Path $TestDrive 'ws'
    New-Item -ItemType Directory -Path $workRoot | Out-Null
    $logs = Join-Path $TestDrive 'logs'
    Mock -ModuleName KitEval Invoke-LoggedProcess {
        $code = if (($ArgumentList -join ' ') -match 'check-content') { 1 } else { 0 }
        [IO.File]::WriteAllText($LogPath, "line one`nCONTENT001 finding")
        return [pscustomobject]@{ ExitCode = $code; TimedOut = $false; DurationSeconds = 1.5; LogPath = $LogPath; ErrorLogPath = '' }
    }

    $results = @(Invoke-KitEvalChecks -Checks @('skillInvoked', 'contentRules', 'build', 'specValid') -WorkRoot $workRoot -BaseRef 'eval-base' `
            -LogFolder $logs -ExpectedSkill 'new-lookup' -SkillsInvoked @('new-lookup') -DotnetPath 'X:\dotnet.exe' -NodeDirectory 'X:\node')

    It 'returns one result per check in execution order' {
        ($results | ForEach-Object { $_.name }) -join ',' | Should Be 'build,contentRules,specValid,skillInvoked'
    }
    It 'passes a zero exit code and fails a non-zero one with the log tail' {
        ($results | Where-Object { $_.name -eq 'build' }).status | Should Be 'passed'
        $content = $results | Where-Object { $_.name -eq 'contentRules' }
        $content.status | Should Be 'failed'
        $content.exitCode | Should Be 1
        $content.detail | Should Match 'CONTENT001'
    }
    It 'fails the spec check when the spec validator is missing from the workspace' {
        ($results | Where-Object { $_.name -eq 'specValid' }).detail | Should Match 'check-spec.mjs is missing'
    }
    It 'grades the skill check from the transcript skills' {
        ($results | Where-Object { $_.name -eq 'skillInvoked' }).status | Should Be 'passed'
    }
    It 'runs node and dotnet from the private toolchain' {
        Assert-MockCalled -ModuleName KitEval Invoke-LoggedProcess -Times 1 -Exactly -ParameterFilter { $FilePath -eq 'X:\dotnet.exe' }
        Assert-MockCalled -ModuleName KitEval Invoke-LoggedProcess -Times 1 -Exactly -ParameterFilter { $FilePath -eq 'X:\node\node.exe' }
    }
}

Describe 'Invoke-KitEvalChecks spec validation (processes mocked)' {
    $workRoot = Join-Path $TestDrive 'ws-spec'
    New-Item -ItemType Directory -Path (Join-Path $workRoot 'tools\feature-spec') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $workRoot 'tools\feature-spec\check-spec.mjs'), '// stub')
    Mock -ModuleName KitEval Get-ChangedPaths { return @('docs/features/equipment-loan.md', 'docs/features/README.md') }
    Mock -ModuleName KitEval Invoke-LoggedProcess {
        [IO.File]::WriteAllText($LogPath, 'ok')
        return [pscustomobject]@{ ExitCode = 0; TimedOut = $false; DurationSeconds = 0.5; LogPath = $LogPath; ErrorLogPath = '' }
    }
    It 'validates each new spec with check-spec.mjs' {
        $result = @(Invoke-KitEvalChecks -Checks @('specValid') -WorkRoot $workRoot -BaseRef 'eval-base' -LogFolder (Join-Path $TestDrive 'spec-logs') -NodeDirectory 'X:\node')[0]
        $result.status | Should Be 'passed'
        $result.detail | Should Match 'equipment-loan.md'
        Assert-MockCalled -ModuleName KitEval Invoke-LoggedProcess -Times 1 -Exactly -ParameterFilter {
            ($ArgumentList -join ' ') -eq 'tools/feature-spec/check-spec.mjs docs/features/equipment-loan.md'
        }
    }
    It 'fails when no spec was written' {
        Mock -ModuleName KitEval Get-ChangedPaths { return @('docs/features/README.md') }
        $result = @(Invoke-KitEvalChecks -Checks @('specValid') -WorkRoot $workRoot -BaseRef 'eval-base' -LogFolder (Join-Path $TestDrive 'spec-logs2') -NodeDirectory 'X:\node')[0]
        $result.status | Should Be 'failed'
        $result.detail | Should Match 'no new or changed spec'
    }
}

Describe 'New-KitEvalRunResult (scoring)' {
    $fixture = New-TestFixture
    $headless = [pscustomobject]@{ ExitCode = 0; TimedOut = $false; DurationSeconds = 60; SkillsInvoked = @('new-lookup') }

    It 'passes when every check passed' {
        $result = New-KitEvalRunResult -Fixture $fixture -RunIndex 1 -Headless $headless -Checks @((New-TestCheck 'build' 'passed'), (New-TestCheck 'skillInvoked' 'passed'))
        $result.passed | Should Be $true
        $result.inconclusive | Should Be $false
        $result.fixture | Should Be 'sample'
        @($result.skillsInvoked) -join ',' | Should Be 'new-lookup'
    }
    It 'fails when one check failed and names it' {
        $result = New-KitEvalRunResult -Fixture $fixture -RunIndex 1 -Headless $headless -Checks @((New-TestCheck 'build' 'failed'), (New-TestCheck 'skillInvoked' 'passed'))
        $result.passed | Should Be $false
        $result.inconclusive | Should Be $false
        @($result.failedChecks) -join ',' | Should Be 'build'
    }
    It 'is inconclusive when every failed check had already failed on the untouched workspace' {
        $result = New-KitEvalRunResult -Fixture $fixture -RunIndex 1 -Headless $headless -Checks @((New-TestCheck 'docsGate' 'failed'), (New-TestCheck 'skillInvoked' 'passed')) `
            -BaselineChecks @((New-TestCheck 'docsGate' 'failed'))
        $result.inconclusive | Should Be $true
        $result.passed | Should Be $false
    }
    It 'still fails when a failure is not explained by the baseline' {
        $result = New-KitEvalRunResult -Fixture $fixture -RunIndex 1 -Headless $headless -Checks @((New-TestCheck 'docsGate' 'failed'), (New-TestCheck 'skillInvoked' 'failed')) `
            -BaselineChecks @((New-TestCheck 'docsGate' 'failed'))
        $result.inconclusive | Should Be $false
        $result.passed | Should Be $false
    }
    It 'is inconclusive on an infrastructure error and never passes without checks or after a timeout' {
        (New-KitEvalRunResult -Fixture $fixture -RunIndex 1 -InfrastructureError 'npm ci failed').inconclusive | Should Be $true
        (New-KitEvalRunResult -Fixture $fixture -RunIndex 1 -Headless $headless).passed | Should Be $false
        $timedOut = [pscustomobject]@{ ExitCode = 1; TimedOut = $true; DurationSeconds = 2700; SkillsInvoked = @() }
        (New-KitEvalRunResult -Fixture $fixture -RunIndex 1 -Headless $timedOut -Checks @((New-TestCheck 'build' 'passed'))).passed | Should Be $false
    }
}

Describe 'Get-KitEvalPassRate (pass-rate math)' {
    It 'meets the criterion at exactly 90% over 10 runs' {
        $results = @(1..9 | ForEach-Object { New-TestResult 'a' $true }) + @(New-TestResult 'b' $false)
        $rate = Get-KitEvalPassRate -Results $results
        $rate.passRate | Should Be 0.9
        $rate.meetsCriteria | Should Be $true
        $rate.verdict | Should Be 'met'
    }
    It 'is below the threshold at 80% over 10 runs' {
        $results = @(1..8 | ForEach-Object { New-TestResult 'a' $true }) + @(1..2 | ForEach-Object { New-TestResult 'b' $false })
        $rate = Get-KitEvalPassRate -Results $results
        $rate.passRate | Should Be 0.8
        $rate.meetsCriteria | Should Be $false
        $rate.verdict | Should Be 'below threshold'
    }
    It 'needs at least 10 conclusive runs even at 100%' {
        $rate = Get-KitEvalPassRate -Results @(1..9 | ForEach-Object { New-TestResult 'a' $true })
        $rate.passRate | Should Be 1
        $rate.meetsCriteria | Should Be $false
        $rate.verdict | Should Be 'not enough runs'
    }
    It 'keeps inconclusive runs out of the rate and out of the run count' {
        $results = @(1..10 | ForEach-Object { New-TestResult 'a' $true }) + @(1..3 | ForEach-Object { New-TestResult 'b' $false $true })
        $rate = Get-KitEvalPassRate -Results $results
        $rate.runs | Should Be 13
        $rate.conclusive | Should Be 10
        $rate.inconclusive | Should Be 3
        $rate.passRate | Should Be 1
        $rate.meetsCriteria | Should Be $true
    }
    It 'reports per fixture and survives an empty set' {
        $results = @((New-TestResult 'a' $true), (New-TestResult 'a' $false), (New-TestResult 'b' $true))
        $rate = Get-KitEvalPassRate -Results $results
        $rate.perFixture['a'].passRate | Should Be 0.5
        $rate.perFixture['b'].passRate | Should Be 1
        $empty = Get-KitEvalPassRate -Results @()
        $empty.passRate | Should Be 0
        $empty.meetsCriteria | Should Be $false
    }
    It 'honours a custom threshold and minimum' {
        $rate = Get-KitEvalPassRate -Results @((New-TestResult 'a' $true), (New-TestResult 'a' $false)) -Threshold 0.5 -MinimumRuns 2
        $rate.meetsCriteria | Should Be $true
    }
}

Describe 'Format-KitEvalReport' {
    It 'writes the rate, the verdict and one row per run' {
        $results = @(
            [pscustomobject]@{ fixture = 'new-lookup'; run = 1; passed = $true; inconclusive = $false; expectedSkill = 'new-lookup'; skillsInvoked = @('new-lookup'); failedChecks = @(); durationSeconds = 10 },
            [pscustomobject]@{ fixture = 'new-module'; run = 1; passed = $false; inconclusive = $false; expectedSkill = 'new-module'; skillsInvoked = @(); failedChecks = @('build'); durationSeconds = 20 }
        )
        $report = Format-KitEvalReport -PassRate (Get-KitEvalPassRate -Results $results) -Results $results
        $report | Should Match 'Pass rate: \*\*50%\*\*'
        $report | Should Match 'not enough runs'
        $report | Should Match '\| new-lookup \| 1 \| pass \| new-lookup \| new-lookup \| - \| 10 \|'
        $report | Should Match '\| new-module \| 1 \| fail \| new-module \| - \| build \| 20 \|'
    }
}
