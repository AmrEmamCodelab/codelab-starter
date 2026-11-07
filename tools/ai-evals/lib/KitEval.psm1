# KitEval.psm1: kit-eval fixtures, the grader and the pass-rate aggregation (Windows PowerShell 5.1).
# A fixture (fixtures/<id>.json) names the skill a plain-language prompt must route to and the checks its output must
# pass; the grader runs those checks in the throwaway workspace after the headless run; the aggregation applies the
# exit criterion (pass rate >= 90% over at least 10 conclusive runs). See tools/ai-evals/MODULE.md.
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'HeadlessClaude.psm1')

# ---------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------
$script:FixtureKinds = @('scaffold', 'input')
$script:ScaffoldSkills = @('new-module', 'new-endpoint', 'new-screen', 'new-permission', 'new-transition', 'new-lookup')
$script:InputSkill = 'build-feature'
$script:CheckBuild = 'build'
$script:CheckRules = 'rulesCheck'
$script:CheckContent = 'contentRules'
$script:CheckContract = 'contractTests'
$script:CheckDocsGate = 'docsGate'
$script:CheckSpec = 'specValid'
$script:CheckSkill = 'skillInvoked'
# Order matters: the build runs before the contract tests that reuse its output.
$script:KnownChecks = @($script:CheckBuild, $script:CheckRules, $script:CheckContent, $script:CheckContract,
    $script:CheckDocsGate, $script:CheckSpec, $script:CheckSkill)
# Checks that read only the repository state, so they can run on the fresh workspace as a baseline.
$script:BaselineChecks = @($script:CheckBuild, $script:CheckRules, $script:CheckContent, $script:CheckContract, $script:CheckDocsGate)
$script:FixtureKeys = @('id', 'kind', 'skill', 'prompt', 'inputs', 'checks')
$script:OptionalFixtureKeys = @('description', 'maxTurns', 'timeoutMinutes')
$script:FixtureIdPattern = '^[a-z0-9]+(-[a-z0-9]+)*$'
$script:SlashCommandPattern = '(^|\s)/[a-z][a-z0-9-]*'
$script:FixtureExtension = '.json'
$script:AllFixtures = 'all'
$script:StatusPassed = 'passed'
$script:StatusFailed = 'failed'
$script:StatusSkipped = 'skipped'
$script:PassThreshold = 0.9
$script:MinimumRuns = 10
$script:PercentFactor = 100
$script:RateDecimals = 3
$script:SolutionFile = 'codelab-starter.slnx'
$script:ContractTestProject = 'backend/tests/Starter.Contract.Tests'
$script:DocsGateProject = 'tools/docs-gate'
$script:RulesCompiler = 'tools/rules-engine/compile.mjs'
$script:ContentChecker = 'tools/rules-engine/check-content.mjs'
$script:SpecChecker = 'tools/feature-spec/check-spec.mjs'
$script:FeatureSpecFolder = 'docs/features/'
$script:FeatureSpecExtension = '.md'
$script:FeatureSpecExcludedNames = @('README.md', 'TEMPLATE.md')
$script:NodeExecutable = 'node.exe'
$script:CheckTimeoutMinutes = @{ build = 30; rulesCheck = 5; contentRules = 5; contractTests = 20; docsGate = 10; specValid = 5 }
$script:LogTailLines = 15
$script:DefaultDotnetPath = 'E:\codelab-tools\dotnet10\dotnet.exe'
$script:DefaultNodeDirectory = 'E:\codelab-tools\node24'
$script:ArtifactsFolderName = 'artifacts'

# ---------------------------------------------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------------------------------------------

function Get-KitEvalConstants {
    return [pscustomobject]@{
        FixtureKinds   = $script:FixtureKinds
        ScaffoldSkills = $script:ScaffoldSkills
        InputSkill     = $script:InputSkill
        KnownChecks    = $script:KnownChecks
        BaselineChecks = $script:BaselineChecks
        PassThreshold  = $script:PassThreshold
        MinimumRuns    = $script:MinimumRuns
        AllFixtures    = $script:AllFixtures
    }
}

# Reads fixtures/<id>.json; $Id 'all' reads every fixture (sorted by id). Returns @{ Id; Path; Fixture; ParseError }.
function Get-KitEvalFixture {
    param([Parameter(Mandatory = $true)][string] $FixtureDirectory, [string] $Id = $script:AllFixtures)
    if (-not (Test-Path -LiteralPath $FixtureDirectory -PathType Container)) { throw "Fixture folder $FixtureDirectory does not exist." }
    if ($Id -eq $script:AllFixtures) {
        $files = @(Get-ChildItem -LiteralPath $FixtureDirectory -Filter ('*' + $script:FixtureExtension) -File | Sort-Object Name)
    } else {
        $path = Join-Path $FixtureDirectory ($Id + $script:FixtureExtension)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Fixture '$Id' not found ($path)." }
        $files = @(Get-Item -LiteralPath $path)
    }
    foreach ($file in $files) {
        $fixture = $null
        $parseError = $null
        try { $fixture = [IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json } catch { $parseError = $_.Exception.Message }
        [pscustomobject]@{
            Id         = [IO.Path]::GetFileNameWithoutExtension($file.Name)
            Path       = $file.FullName
            Fixture    = $fixture
            ParseError = $parseError
        }
    }
}

# Validates one fixture against the EVAL FIXTURE contract; returns the findings (empty when valid).
function Test-KitEvalFixture {
    param(
        [object] $Fixture,
        [Parameter(Mandatory = $true)][string] $ExpectedId,
        [Parameter(Mandatory = $true)][string] $RepoRoot
    )
    $findings = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Fixture) { $findings.Add('the fixture is empty or not JSON'); return @($findings) }
    $names = @($Fixture.PSObject.Properties | ForEach-Object { $_.Name })
    foreach ($key in $script:FixtureKeys) { if ($names -notcontains $key) { $findings.Add("missing key '$key'") } }
    foreach ($name in $names) {
        if ($script:FixtureKeys -notcontains $name -and $script:OptionalFixtureKeys -notcontains $name) { $findings.Add("unknown key '$name'") }
    }
    $id = [string] (Get-ObjectValue $Fixture 'id')
    if ($id -notmatch $script:FixtureIdPattern) { $findings.Add("id '$id' is not kebab-case") }
    if ($id -ne $ExpectedId) { $findings.Add("id '$id' does not match the file name '$ExpectedId'") }
    $kind = [string] (Get-ObjectValue $Fixture 'kind')
    if ($script:FixtureKinds -notcontains $kind) { $findings.Add("kind '$kind' is not one of: $($script:FixtureKinds -join ', ')") }
    $skill = [string] (Get-ObjectValue $Fixture 'skill')
    if ($kind -eq 'scaffold' -and $script:ScaffoldSkills -notcontains $skill) {
        $findings.Add("a scaffold fixture expects one of: $($script:ScaffoldSkills -join ', ') (got '$skill')")
    }
    if ($kind -eq 'input' -and $skill -ne $script:InputSkill) { $findings.Add("an input fixture expects the skill '$script:InputSkill' (got '$skill')") }
    $prompt = [string] (Get-ObjectValue $Fixture 'prompt')
    if (-not $prompt.Trim()) { $findings.Add('prompt is empty') }
    if ($prompt -match $script:SlashCommandPattern) { $findings.Add('prompt names a slash command; it must be plain language') }
    if ($prompt -match [regex]::Escape($skill) -and $skill) { $findings.Add("prompt names the expected skill '$skill'; describe the work instead") }
    $inputs = @(Get-ObjectValue $Fixture 'inputs' | Where-Object { $null -ne $_ })
    foreach ($inputPath in $inputs) {
        $text = [string] $inputPath
        if ([IO.Path]::IsPathRooted($text) -or $text -match '(^|[\\/])\.\.([\\/]|$)') { $findings.Add("input '$text' is not repo-relative"); continue }
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $text) -PathType Leaf)) { $findings.Add("input '$text' does not exist") }
    }
    if ($kind -eq 'input' -and $inputs.Count -eq 0) { $findings.Add('an input fixture needs at least one input file') }
    $checks = @(Get-ObjectValue $Fixture 'checks' | Where-Object { $null -ne $_ })
    if ($checks.Count -eq 0) { $findings.Add('checks is empty') }
    foreach ($check in $checks) { if ($script:KnownChecks -notcontains $check) { $findings.Add("unknown check '$check'") } }
    if ($checks -notcontains $script:CheckSkill) { $findings.Add("checks must include '$script:CheckSkill'") }
    if ($kind -eq 'input' -and $checks -notcontains $script:CheckSpec) { $findings.Add("an input fixture must include '$script:CheckSpec'") }
    foreach ($limit in @('maxTurns', 'timeoutMinutes')) {
        $value = Get-ObjectValue $Fixture $limit
        if ($null -ne $value -and -not ($value -is [int] -and $value -gt 0)) { $findings.Add("$limit must be a positive integer") }
    }
    return @($findings)
}

# The checks of a fixture in execution order.
function Get-OrderedChecks {
    param([Parameter(Mandatory = $true)][object] $Fixture)
    $requested = @(Get-ObjectValue $Fixture 'checks')
    return @($script:KnownChecks | Where-Object { $requested -contains $_ })
}

# ---------------------------------------------------------------------------------------------------------------
# Grader
# ---------------------------------------------------------------------------------------------------------------

# The command line of a process-based check (pure). skillInvoked and specValid are not process checks.
function Get-KitEvalCheckCommand {
    param(
        [Parameter(Mandatory = $true)][string] $Check,
        [Parameter(Mandatory = $true)][string] $WorkRoot,
        [Parameter(Mandatory = $true)][string] $ArtifactsPath,
        [Parameter(Mandatory = $true)][string] $BaseRef,
        [Parameter(Mandatory = $true)][string] $DotnetPath,
        [Parameter(Mandatory = $true)][string] $NodePath
    )
    switch ($Check) {
        $script:CheckBuild {
            return @{ FilePath = $DotnetPath; Arguments = @('build', $script:SolutionFile, '-warnaserror', '--artifacts-path', $ArtifactsPath, '-nologo') }
        }
        $script:CheckRules { return @{ FilePath = $NodePath; Arguments = @($script:RulesCompiler, '--check', '--root', $WorkRoot) } }
        $script:CheckContent { return @{ FilePath = $NodePath; Arguments = @($script:ContentChecker, '--root', $WorkRoot) } }
        $script:CheckContract {
            return @{ FilePath = $DotnetPath; Arguments = @('test', $script:ContractTestProject, '--artifacts-path', $ArtifactsPath, '-nologo') }
        }
        $script:CheckDocsGate {
            return @{ FilePath = $DotnetPath; Arguments = @('run', '--project', $script:DocsGateProject, '--artifacts-path', $ArtifactsPath,
                    '--', 'check', '--root', '.', '--base', $BaseRef) }
        }
    }
    throw "Check '$Check' is not a process check."
}

function New-CheckResult {
    param([string] $Name, [string] $Status, [object] $ExitCode = $null, [double] $DurationSeconds = 0, [string] $Detail = '', [string] $LogPath = '')
    return [pscustomobject]@{
        name            = $Name
        status          = $Status
        exitCode        = $ExitCode
        durationSeconds = $DurationSeconds
        detail          = $Detail
        logPath         = $LogPath
    }
}

# The expected-skill check (pure): passes when the expected skill was invoked through the Skill tool.
function Test-SkillInvoked {
    param([Parameter(Mandatory = $true)][string] $ExpectedSkill, [string[]] $SkillsInvoked = @())
    $invoked = @($SkillsInvoked | Where-Object { $_ })
    if ($invoked -contains $ExpectedSkill) {
        return New-CheckResult -Name $script:CheckSkill -Status $script:StatusPassed -Detail "invoked: $($invoked -join ', ')"
    }
    $seen = if ($invoked.Count -gt 0) { $invoked -join ', ' } else { 'none' }
    return New-CheckResult -Name $script:CheckSkill -Status $script:StatusFailed -Detail "expected '$ExpectedSkill'; invoked: $seen"
}

# Feature specs written since the snapshot (pure filter over repo-relative paths).
function Select-FeatureSpecPaths {
    param([string[]] $ChangedPaths = @())
    return @($ChangedPaths | Where-Object {
            $_ -and $_.StartsWith($script:FeatureSpecFolder) -and $_.EndsWith($script:FeatureSpecExtension) -and
            ($_.Substring($script:FeatureSpecFolder.Length) -notmatch '/') -and
            $script:FeatureSpecExcludedNames -notcontains $_.Substring($script:FeatureSpecFolder.Length)
        } | Sort-Object -Unique)
}

function Get-ChangedPaths {
    param([Parameter(Mandatory = $true)][string] $WorkRoot, [Parameter(Mandatory = $true)][string] $BaseRef)
    $ErrorActionPreference = 'Continue'
    $changed = @(& git -C $WorkRoot diff --name-only $BaseRef 2>$null)
    $untracked = @(& git -C $WorkRoot ls-files -o --exclude-standard 2>$null)
    return @($changed + $untracked | Where-Object { $_ })
}

function Get-LogTail {
    param([string] $LogPath)
    if (-not $LogPath -or -not (Test-Path -LiteralPath $LogPath)) { return '' }
    return ((Get-Content -LiteralPath $LogPath -Tail $script:LogTailLines -ErrorAction SilentlyContinue) -join "`n")
}

function Invoke-ProcessCheck {
    param([string] $Check, [hashtable] $Command, [string] $WorkRoot, [string] $LogPath, [hashtable] $Environment)
    $run = Invoke-LoggedProcess -FilePath $Command.FilePath -ArgumentList $Command.Arguments -WorkingDirectory $WorkRoot `
        -LogPath $LogPath -TimeoutMinutes $script:CheckTimeoutMinutes[$Check] -Environment $Environment
    $status = if ($run.ExitCode -eq 0 -and -not $run.TimedOut) { $script:StatusPassed } else { $script:StatusFailed }
    $detail = if ($run.TimedOut) { 'timed out' } elseif ($status -eq $script:StatusFailed) { Get-LogTail $LogPath } else { '' }
    return New-CheckResult -Name $Check -Status $status -ExitCode $run.ExitCode -DurationSeconds $run.DurationSeconds -Detail $detail -LogPath $LogPath
}

function Invoke-SpecCheck {
    param([string] $WorkRoot, [string] $BaseRef, [string] $NodePath, [string] $LogFolder, [hashtable] $Environment)
    if (-not (Test-Path -LiteralPath (Join-Path $WorkRoot $script:SpecChecker) -PathType Leaf)) {
        return New-CheckResult -Name $script:CheckSpec -Status $script:StatusFailed -Detail "$script:SpecChecker is missing in the workspace"
    }
    $specs = @(Select-FeatureSpecPaths (Get-ChangedPaths -WorkRoot $WorkRoot -BaseRef $BaseRef))
    if ($specs.Count -eq 0) {
        return New-CheckResult -Name $script:CheckSpec -Status $script:StatusFailed -Detail "no new or changed spec under $script:FeatureSpecFolder"
    }
    $failures = New-Object System.Collections.Generic.List[string]
    $duration = 0
    $index = 0
    foreach ($spec in $specs) {
        $index++
        $log = Join-Path $LogFolder ("specValid-$index.log")
        $run = Invoke-LoggedProcess -FilePath $NodePath -ArgumentList @($script:SpecChecker, $spec) -WorkingDirectory $WorkRoot `
            -LogPath $log -TimeoutMinutes $script:CheckTimeoutMinutes[$script:CheckSpec] -Environment $Environment
        $duration += $run.DurationSeconds
        if ($run.ExitCode -ne 0 -or $run.TimedOut) { $failures.Add("$spec (exit $($run.ExitCode)): $(Get-LogTail $log)") }
    }
    $status = if ($failures.Count -eq 0) { $script:StatusPassed } else { $script:StatusFailed }
    $detail = if ($failures.Count -eq 0) { "valid: $($specs -join ', ')" } else { $failures -join "`n" }
    return New-CheckResult -Name $script:CheckSpec -Status $status -DurationSeconds $duration -Detail $detail -LogPath $LogFolder
}

# Runs the given checks in the workspace and returns one result per check. Logs go to $LogFolder.
function Invoke-KitEvalChecks {
    param(
        [Parameter(Mandatory = $true)][string[]] $Checks,
        [Parameter(Mandatory = $true)][string] $WorkRoot,
        [Parameter(Mandatory = $true)][string] $BaseRef,
        [Parameter(Mandatory = $true)][string] $LogFolder,
        [string] $ExpectedSkill,
        [string[]] $SkillsInvoked = @(),
        [string] $DotnetPath = $script:DefaultDotnetPath,
        [string] $NodeDirectory = $script:DefaultNodeDirectory
    )
    if (-not (Test-Path -LiteralPath $LogFolder)) { New-Item -ItemType Directory -Path $LogFolder -Force | Out-Null }
    $artifactsPath = Join-Path $WorkRoot $script:ArtifactsFolderName
    $nodePath = [IO.Path]::Combine($NodeDirectory, $script:NodeExecutable)
    $environment = Get-HeadlessEnvironment -NodeDirectory $NodeDirectory -DotnetPath $DotnetPath
    $ordered = @($script:KnownChecks | Where-Object { $Checks -contains $_ })
    foreach ($check in $ordered) {
        if ($check -eq $script:CheckSkill) {
            Test-SkillInvoked -ExpectedSkill $ExpectedSkill -SkillsInvoked $SkillsInvoked
        } elseif ($check -eq $script:CheckSpec) {
            Invoke-SpecCheck -WorkRoot $WorkRoot -BaseRef $BaseRef -NodePath $nodePath -LogFolder $LogFolder -Environment $environment
        } else {
            $command = Get-KitEvalCheckCommand -Check $check -WorkRoot $WorkRoot -ArtifactsPath $artifactsPath -BaseRef $BaseRef `
                -DotnetPath $DotnetPath -NodePath $nodePath
            Invoke-ProcessCheck -Check $check -Command $command -WorkRoot $WorkRoot -LogPath (Join-Path $LogFolder "$check.log") -Environment $environment
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Scoring and aggregation
# ---------------------------------------------------------------------------------------------------------------

# One run's result (pure). A run passes when every check passed and the headless run neither failed to start nor
# timed out. A run is inconclusive when a check that failed after the run had already failed on the untouched
# workspace (the source tree was broken, not the kit): it is reported but kept out of the pass rate.
function New-KitEvalRunResult {
    param(
        [Parameter(Mandatory = $true)][object] $Fixture,
        [Parameter(Mandatory = $true)][int] $RunIndex,
        [object] $Headless,
        [object[]] $Checks = @(),
        [object[]] $BaselineChecks = @(),
        [string] $InfrastructureError
    )
    $failed = @($Checks | Where-Object { $_.status -ne $script:StatusPassed })
    $baselineFailures = @($BaselineChecks | Where-Object { $_.status -eq $script:StatusFailed } | ForEach-Object { $_.name })
    $blamedOnBaseline = @($failed | Where-Object { $baselineFailures -contains $_.name })
    $timedOut = [bool] (Get-ObjectValue $Headless 'TimedOut')
    $inconclusive = [bool] $InfrastructureError -or ($failed.Count -gt 0 -and $blamedOnBaseline.Count -eq $failed.Count)
    $passed = -not $inconclusive -and -not $timedOut -and $null -ne $Headless -and $failed.Count -eq 0 -and @($Checks).Count -gt 0
    return [pscustomobject]@{
        fixture             = [string] (Get-ObjectValue $Fixture 'id')
        kind                = [string] (Get-ObjectValue $Fixture 'kind')
        expectedSkill       = [string] (Get-ObjectValue $Fixture 'skill')
        run                 = $RunIndex
        passed              = $passed
        inconclusive        = $inconclusive
        infrastructureError = $InfrastructureError
        exitCode            = Get-ObjectValue $Headless 'ExitCode'
        timedOut            = $timedOut
        durationSeconds     = Get-ObjectValue $Headless 'DurationSeconds'
        skillsInvoked       = @(Get-ObjectValue $Headless 'SkillsInvoked' | Where-Object { $_ })
        failedChecks        = @($failed | ForEach-Object { $_.name })
        baselineFailures    = $baselineFailures
        checks              = @($Checks)
    }
}

function Get-RateSummary {
    param([object[]] $Results)
    $conclusive = @($Results | Where-Object { -not $_.inconclusive })
    $passed = @($conclusive | Where-Object { $_.passed })
    $rate = if ($conclusive.Count -gt 0) { [Math]::Round($passed.Count / $conclusive.Count, $script:RateDecimals) } else { 0 }
    return [pscustomobject]@{
        runs         = @($Results).Count
        conclusive   = $conclusive.Count
        inconclusive = @($Results).Count - $conclusive.Count
        passed       = $passed.Count
        passRate     = $rate
    }
}

# The pass-rate aggregation (pure): overall and per fixture; the exit criterion needs >= MinimumRuns conclusive runs
# and a pass rate >= PassThreshold.
function Get-KitEvalPassRate {
    param([object[]] $Results = @(), [double] $Threshold = $script:PassThreshold, [int] $MinimumRuns = $script:MinimumRuns)
    $all = @($Results | Where-Object { $null -ne $_ })
    $overall = Get-RateSummary $all
    $perFixture = [ordered]@{}
    foreach ($group in @($all | Group-Object -Property fixture | Sort-Object Name)) {
        $perFixture[$group.Name] = Get-RateSummary @($group.Group)
    }
    $enoughRuns = $overall.conclusive -ge $MinimumRuns
    $meets = $enoughRuns -and $overall.passRate -ge $Threshold
    $verdict = if ($meets) { 'met' } elseif (-not $enoughRuns) { 'not enough runs' } else { 'below threshold' }
    return [pscustomobject]@{
        runs         = $overall.runs
        conclusive   = $overall.conclusive
        inconclusive = $overall.inconclusive
        passed       = $overall.passed
        passRate     = $overall.passRate
        threshold    = $Threshold
        minimumRuns  = $MinimumRuns
        meetsCriteria = $meets
        verdict      = $verdict
        perFixture   = $perFixture
    }
}

# A Markdown report of a set of runs (pure).
function Format-KitEvalReport {
    param([Parameter(Mandatory = $true)][object] $PassRate, [object[]] $Results = @(), [string] $Title = 'Kit eval report')
    $percent = [Math]::Round($PassRate.passRate * $script:PercentFactor, 1)
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# $Title")
    $lines.Add('')
    $lines.Add("Pass rate: **$percent%** ($($PassRate.passed) of $($PassRate.conclusive) conclusive runs; $($PassRate.inconclusive) inconclusive). " +
        "Criterion: >= $([Math]::Round($PassRate.threshold * $script:PercentFactor))% over >= $($PassRate.minimumRuns) runs: **$($PassRate.verdict)**.")
    $lines.Add('')
    $lines.Add('| Fixture | Run | Result | Expected skill | Skills invoked | Failed checks | Duration (s) |')
    $lines.Add('| --- | --- | --- | --- | --- | --- | --- |')
    foreach ($result in @($Results)) {
        $outcome = if ($result.inconclusive) { 'inconclusive' } elseif ($result.passed) { 'pass' } else { 'fail' }
        $skills = if (@($result.skillsInvoked).Count -gt 0) { @($result.skillsInvoked) -join ', ' } else { '-' }
        $failedChecks = if (@($result.failedChecks).Count -gt 0) { @($result.failedChecks) -join ', ' } else { '-' }
        $lines.Add("| $($result.fixture) | $($result.run) | $outcome | $($result.expectedSkill) | $skills | $failedChecks | $($result.durationSeconds) |")
    }
    return ($lines -join "`n") + "`n"
}

Export-ModuleMember -Function Get-KitEvalConstants, Get-KitEvalFixture, Test-KitEvalFixture, Get-OrderedChecks, Get-KitEvalCheckCommand,
    Test-SkillInvoked, Select-FeatureSpecPaths, Invoke-KitEvalChecks, New-KitEvalRunResult, Get-KitEvalPassRate, Format-KitEvalReport
