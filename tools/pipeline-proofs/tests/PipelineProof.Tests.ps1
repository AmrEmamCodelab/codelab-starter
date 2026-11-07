# Pester 3.4 tests for tools/pipeline-proofs/lib/PipelineProof.psm1 (Windows PowerShell 5.1): proof definitions and
# the prompt, the transcript step sequence and skill-sequence check, hook-log analysis, the refusal probe (pure and
# through the real hook), check-spec output and the hand-edit threshold, the Review log, the visual summary, the
# criteria, verdict and report. No claude call.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/pipeline-proofs/tests -EnableExit"
Set-StrictMode -Version Latest

$ToolFolder = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ToolFolder)
$InputsRoot = Join-Path $ToolFolder 'inputs'
$NodeDirectory = 'E:\codelab-tools\node24'
Import-Module (Join-Path $ToolFolder 'lib\PipelineProof.psm1') -Force

$Pipeline = @('build-feature', 'new-module', 'new-transition', 'deadline-recipe', 'new-permission', 'new-endpoint', 'new-screen')

function New-ToolUseLine([string] $Name, [hashtable] $ToolInput) {
    $event = @{ type = 'assistant'; message = @{ content = @(@{ type = 'tool_use'; name = $Name; input = $ToolInput }) } }
    return (ConvertTo-Json -InputObject $event -Depth 6 -Compress)
}

function New-HookLine([string] $Decision, [string] $Path, [string] $Tool = 'Write', [string] $Skill = $null, [string[]] $RuleIds = @()) {
    return (ConvertTo-Json -InputObject ([ordered]@{ ts = '2026-11-07T10:00:00.000Z'; hook = 'pre-tool-use'; tool = $Tool; path = $Path; decision = $Decision; ruleIds = $RuleIds; skill = $Skill }) -Compress)
}

function New-Shot([string] $Language, [int] $Width, [object] $Ratio, [string] $Route = 'complaint-new') {
    return [pscustomobject]@{ id = "$Route.$Language.$Width"; route = $Route; language = $Language; width = $Width; status = 'pass'; ratio = $Ratio }
}

function New-VisualReport([string] $Path, [object] $Iteration, [object[]] $Shots, [string] $Mode = 'target') {
    return [pscustomobject]@{ Path = $Path; Report = [pscustomobject]@{ mode = $Mode; iteration = $Iteration; shots = $Shots } }
}

Describe 'Proof definitions (the committed inputs)' {
    foreach ($proof in @('complaint', 'permit', 'feedback')) {
        It "the $proof definition is valid, plain and expects the full pipeline" {
            $definition = Get-ProofDefinition -Proof $proof -InputsRoot $InputsRoot
            $definition.Proof | Should BeExactly $proof
            @(Test-ProofDefinition -Definition $definition -RepoRoot $RepoRoot).Count | Should Be 0
            ($definition.ExpectedSteps -join ',') | Should BeExactly ($Pipeline -join ',')
        }
    }
    It 'blocks the feedback proof until the owner gives a Figma URL' {
        $definition = Get-ProofDefinition -Proof feedback -InputsRoot $InputsRoot
        @(Get-ProofBlockers -Definition $definition).Count | Should Be 1
        @(Get-ProofBlockers -Definition $definition -FigmaUrl 'https://www.figma.com/design/AbC123/Feedback?node-id=1-2').Count | Should Be 0
    }
    It 'refuses a URL that is not a Figma design link' {
        $definition = Get-ProofDefinition -Proof feedback -InputsRoot $InputsRoot
        (@(Test-ProofDefinition -Definition $definition -RepoRoot $RepoRoot -FigmaUrl 'https://example.invalid/x') -join ';') | Should Match 'not a figma.com design link'
    }
    It 'refuses an unknown proof' {
        { Get-ProofDefinition -Proof 'orders' -InputsRoot $InputsRoot } | Should Throw 'Unknown proof'
    }
    It 'checks the permit workbook through the usecase-testing reader' {
        $definition = Get-ProofDefinition -Proof permit -InputsRoot $InputsRoot
        $workbook = Test-ProofWorkbook -Workbook $definition.Workbook -RepoRoot $RepoRoot
        $workbook.present | Should Be $true
        @($workbook.findings).Count | Should Be 0
        @($workbook.useCaseIds).Count | Should Be 2
    }
}

Describe 'Test-PlainScenario' {
    It 'accepts plain wording with repository paths' {
        @(Test-PlainScenario -Text 'Clients request a permit; see tools/pipeline-proofs/inputs/permit/x.xlsx.').Count | Should Be 0
    }
    It 'refuses a slash command, a named skill and an empty text' {
        (@(Test-PlainScenario -Text '/build-feature a permit flow') -join ';') | Should Match 'slash command'
        (@(Test-PlainScenario -Text 'Use new-module for permits') -join ';') | Should Match "names the pipeline step 'new-module'"
        (@(Test-PlainScenario -Text '  ') -join ';') | Should Match 'empty'
    }
}

Describe 'Get-ProofPrompt' {
    $definition = Get-ProofDefinition -Proof permit -InputsRoot $InputsRoot
    It 'stops at the approval gate by default' {
        $prompt = Get-ProofPrompt -Definition $definition -ApprovalDate '2026-11-07'
        $prompt | Should Match 'Stop when the feature spec is ready for my approval'
        $prompt | Should Match ([regex]::Escape('- tools/pipeline-proofs/inputs/permit/permit-use-cases.xlsx'))
        $prompt | Should Not Match 'pre-approved'
    }
    It 'records the pre-approval with its date when asked' {
        $prompt = Get-ProofPrompt -Definition $definition -PreApproveSpec -ApprovalDate '2026-11-07'
        $prompt | Should Match ([regex]::Escape('approvedBy: pipeline-proof (pre-approved) and approvedOn: 2026-11-07'))
    }
    It 'stays a plain scenario (no slash command, no skill named)' {
        @(Test-PlainScenario -Text (Get-ProofPrompt -Definition $definition -PreApproveSpec -ApprovalDate '2026-11-07')).Count | Should Be 0
    }
    It 'adds the Figma frame URL' {
        $feedback = Get-ProofDefinition -Proof feedback -InputsRoot $InputsRoot
        Get-ProofPrompt -Definition $feedback -FigmaUrl 'https://www.figma.com/design/AbC/F' -ApprovalDate '2026-11-07' | Should Match 'Figma frame: https://www.figma.com/design/AbC/F'
    }
}

Describe 'Get-ProofStepSequence' {
    It 'reads skills and the deadline recipe in first-use order, once each' {
        $lines = @(
            (New-ToolUseLine 'Skill' @{ skill = 'build-feature' }),
            (New-ToolUseLine 'Skill' @{ skill = '/new-module Permits' }),
            (New-ToolUseLine 'Read' @{ file_path = 'E:\ws\docs\recipes\add-deadline.md' }),
            (New-ToolUseLine 'Skill' @{ skill = 'new-module' }),
            (New-ToolUseLine 'Read' @{ file_path = 'E:/ws/docs/features/README.md' }),
            'not json "tool_use"',
            '{"type":"user","message":{"content":[]}}'
        )
        (Get-ProofStepSequence -Lines $lines) -join ',' | Should BeExactly 'build-feature,new-module,deadline-recipe'
    }
    It 'gives nothing for a missing transcript' {
        @(Read-ProofTranscriptSteps -TranscriptPath (Join-Path $TestDrive 'none.jsonl')).Count | Should Be 0
    }
}

Describe 'Test-SkillSequence' {
    It 'passes the full pipeline in order, other skills in between allowed' {
        $observed = @('coding-standards', 'build-feature', 'new-module', 'new-transition', 'deadline-recipe', 'new-permission', 'new-lookup', 'new-endpoint', 'new-screen')
        $result = Test-SkillSequence -Expected $Pipeline -Observed $observed
        $result.passed | Should Be $true
        $result.routedToBuildFeature | Should Be $true
    }
    It 'fails when a step is missing' {
        $result = Test-SkillSequence -Expected $Pipeline -Observed @('build-feature', 'new-module', 'new-transition', 'new-permission', 'new-endpoint', 'new-screen')
        $result.passed | Should Be $false
        ($result.missing -join ',') | Should BeExactly 'deadline-recipe'
    }
    It 'fails when the steps are out of order' {
        $result = Test-SkillSequence -Expected $Pipeline -Observed @('build-feature', 'new-transition', 'new-module', 'deadline-recipe', 'new-permission', 'new-endpoint', 'new-screen')
        $result.inOrder | Should Be $false
        $result.passed | Should Be $false
    }
    It 'fails routing when a scaffold skill ran before build-feature' {
        $result = Test-SkillSequence -Expected $Pipeline -Observed @('new-module', 'build-feature')
        $result.routedToBuildFeature | Should Be $false
        $result.firstPipelineStep | Should BeExactly 'new-module'
    }
    It 'fails with no skills at all' {
        (Test-SkillSequence -Expected $Pipeline -Observed @()).passed | Should Be $false
    }
}

Describe 'Hook-log analysis' {
    It 'pairs a deny with the later allow on the same file' {
        $lines = @(
            (New-HookLine 'deny' 'backend/src/Modules/Permits/Api/PermitsController.cs' 'Write' 'new-endpoint' @('base.endpoint-has-permission')),
            (New-HookLine 'allow' 'backend/src/Modules/Other/A.cs'),
            (New-HookLine 'allow' 'backend\src\Modules\Permits\Api\PermitsController.cs' 'Edit'),
            'broken line'
        )
        $parsed = ConvertFrom-HookLogLines -Lines $lines
        $parsed.MalformedLines | Should Be 1
        $proof = Get-HookProof -Entries $parsed.Entries -MalformedLines $parsed.MalformedLines
        $proof.passed | Should Be $true
        $proof.denies | Should Be 1
        @($proof.corrected).Count | Should Be 1
        $proof.corrected[0].skill | Should BeExactly 'new-endpoint'
        $proof.corrected[0].correctedWith | Should BeExactly 'Edit'
    }
    It 'does not count an allow before the deny, nor a deny never corrected' {
        $lines = @(
            (New-HookLine 'allow' 'a/b.cs'),
            (New-HookLine 'deny' 'a/b.cs' 'Write' 'new-module' @('hooks.module-without-rules')),
            (New-HookLine 'warn' 'a/b.cs')
        )
        $proof = Get-HookProof -Entries (ConvertFrom-HookLogLines -Lines $lines).Entries
        $proof.passed | Should Be $false
        @($proof.uncorrected).Count | Should Be 1
    }
    It 'ignores Stop-gate entries and denies without a path' {
        $stop = (ConvertTo-Json -InputObject @{ hook = 'on-stop-gate'; decision = 'deny'; path = $null } -Compress)
        $proof = Get-HookProof -Entries (ConvertFrom-HookLogLines -Lines @($stop, (New-HookLine 'deny' $null 'Bash'))).Entries
        $proof.entries | Should Be 1
        $proof.passed | Should Be $false
    }
    It 'pairs a multi-target shell deny with a later allow on any of its paths, not only the first' {
        $deny = (ConvertTo-Json -InputObject ([ordered]@{ ts = '2026-11-07T10:00:00.000Z'; hook = 'pre-tool-use'; tool = 'Bash'
                    path = 'backend/src/Modules/Permits/A.cs'; paths = @('backend/src/Modules/Permits/A.cs', 'backend/src/Modules/Permits/B.cs')
                    decision = 'deny'; ruleIds = @('hooks.shell-source-write'); skill = 'new-module' }) -Compress)
        $allow = (ConvertTo-Json -InputObject ([ordered]@{ ts = '2026-11-07T10:01:00.000Z'; hook = 'pre-tool-use'; tool = 'Edit'
                    path = 'backend\src\Modules\Permits\B.cs'; paths = @('backend\src\Modules\Permits\B.cs'); decision = 'allow'; ruleIds = @() }) -Compress)
        $proof = Get-HookProof -Entries (ConvertFrom-HookLogLines -Lines @($deny, $allow)).Entries
        $proof.passed | Should Be $true
        $proof.corrected[0].path | Should BeExactly 'backend/src/Modules/Permits/A.cs'
        @($proof.corrected[0].paths).Count | Should Be 2
        $proof.corrected[0].correctedWith | Should BeExactly 'Edit'
    }
    It 'falls back to the single path when an entry has no paths[]' {
        @(Get-HookEntryPaths ([pscustomobject]@{ path = '.\a\b.cs' }))[0] | Should BeExactly 'a/b.cs'
        @(Get-HookEntryPaths ([pscustomobject]@{ path = 'a/b.cs'; paths = @() }))[0] | Should BeExactly 'a/b.cs'
        @(Get-HookEntryPaths ([pscustomobject]@{ path = $null })).Count | Should Be 0
        Test-HookPathOverlap -Left @('a/B.cs') -Right @('x.cs', 'a/b.cs') | Should Be $true
        Test-HookPathOverlap -Left @('a/b.cs') -Right @() | Should Be $false
    }
    It 'reports a missing log as not present' {
        (Read-HookLog -Path (Join-Path $TestDrive 'missing.jsonl')).Present | Should Be $false
    }
}

Describe 'Refusal probe' {
    It 'builds a Write of a file in a module folder without rules' {
        $probeInput = (Get-RefusalProbeInput -WorkRoot 'C:\ws\run') | ConvertFrom-Json
        $probeInput.tool_name | Should BeExactly 'Write'
        $probeInput.tool_input.file_path | Should BeExactly 'C:/ws/run/backend/src/Modules/ProofProbe/Domain/ProofProbe.cs'
        $probeInput.cwd | Should BeExactly 'C:/ws/run'
    }
    It 'passes a deny that names new-module and is logged with it' {
        $stdout = '{"hookSpecificOutput":{"permissionDecision":"deny","permissionDecisionReason":"[hooks.module-without-rules] x. Fix: Use the `new-module` skill."}}'
        $log = @([pscustomobject]@{ decision = 'deny'; skill = 'new-module' })
        (Test-RefusalProbeOutput -Stdout $stdout -LogEntries $log).passed | Should Be $true
    }
    It 'fails a deny naming another skill, silence, and non-JSON output' {
        $other = '{"hookSpecificOutput":{"permissionDecision":"deny","permissionDecisionReason":"Use the `new-screen` skill"}}'
        (Test-RefusalProbeOutput -Stdout $other -LogEntries @([pscustomobject]@{ decision = 'deny'; skill = 'new-screen' })).passed | Should Be $false
        (Test-RefusalProbeOutput -Stdout '' -LogEntries @()).passed | Should Be $false
        (Test-RefusalProbeOutput -Stdout 'oops' -LogEntries @()).reason | Should Match 'not JSON'
    }
    It 'is refused by the real PreToolUse hook, naming new-module (read-only on the source tree)' {
        $result = Invoke-RefusalProbe -WorkRoot $RepoRoot -LogFolder (Join-Path $TestDrive 'probe') -NodeDirectory $NodeDirectory
        $result.decision | Should BeExactly 'deny'
        $result.namesSkill | Should Be $true
        $result.loggedSkill | Should BeExactly 'new-module'
        $result.passed | Should Be $true
        Test-Path (Join-Path $RepoRoot 'backend\src\Modules\ProofProbe') | Should Be $false
    }
}

Describe 'Spec check output and the hand-edit threshold' {
    It 'reads check-spec --json' {
        $text = '{"file":"docs/features/permit.md","findings":[{"ruleId":"hand-edits.too-many","severity":"warning","message":"m","line":3}],"status":"approved","handEditCount":4,"ok":true}'
        $result = ConvertFrom-SpecCheckOutput -Text $text -ExitCode 0 -SpecPath 'docs/features/permit.md'
        $result.ok | Should Be $true
        $result.status | Should BeExactly 'approved'
        $result.handEditCount | Should Be 4
        $result.errorCount | Should Be 0
    }
    It 'fails on a non-zero exit or output that is not JSON' {
        (ConvertFrom-SpecCheckOutput -Text '{"findings":[{"severity":"error"}],"ok":false}' -ExitCode 1 -SpecPath 'x').ok | Should Be $false
        (ConvertFrom-SpecCheckOutput -Text 'Error: boom' -ExitCode 2 -SpecPath 'x').detail | Should Match 'not JSON'
    }
    It 'runs the real check-spec on the worked example' {
        $result = Invoke-ProofSpecCheck -WorkRoot $RepoRoot -SpecPath 'docs/features/examples/site-visit-permit.md' -LogFolder (Join-Path $TestDrive 'spec') -NodeDirectory $NodeDirectory
        $result.ok | Should Be $true
        $result.handEditCount | Should Be 0
    }
    It 'refuses the draft example when approval is required' {
        $result = Invoke-ProofSpecCheck -WorkRoot $RepoRoot -SpecPath 'docs/features/examples/site-visit-permit.md' -LogFolder (Join-Path $TestDrive 'spec2') -RequireApproved -NodeDirectory $NodeDirectory
        $result.ok | Should Be $false
    }
    It 'passes 10 hand edits and fails 11 or none counted' {
        (Test-HandEditLimit -Count 10).passed | Should Be $true
        (Test-HandEditLimit -Count 0).passed | Should Be $true
        (Test-HandEditLimit -Count 11).passed | Should Be $false
        (Test-HandEditLimit -Count $null).passed | Should Be $false
        (Test-HandEditLimit -Count 3 -Limit 2).passed | Should Be $false
    }
}

Describe 'Get-ReviewLogSummary' {
    It 'reads the findings table, list items and the iteration count of the Review log only' {
        $spec = @(
            '## Hand-edit log', '', '| # | File | Reason |', '| --- | --- | --- |', '| 1 | a.cs | typo |', '',
            '## Review log', '', '| Iteration | Reviewer | Finding | Resolution |', '| --- | --- | --- | --- |',
            '| 1 | standards-reviewer | magic number | constant |', '| 2 | dga-reviewer | spacing | token |',
            '- i18n-keeper: no findings', '```', '| 9 | fenced | ignored | x |', '```', '', 'Iterations: 2', '', '## After', '- not counted'
        ) -join "`n"
        $summary = Get-ReviewLogSummary -SpecText $spec
        $summary.present | Should Be $true
        $summary.iterations | Should Be 2
        $summary.findingCount | Should Be 3
        $summary.passed | Should Be $true
    }
    It 'fails without the section or with the template default of 0 iterations' {
        (Get-ReviewLogSummary -SpecText "## Scenario`ntext").passed | Should Be $false
        (Get-ReviewLogSummary -SpecText "## Review log`n`nIterations: 0").passed | Should Be $false
    }
}

Describe 'Get-VisualSummary' {
    $passingShots = @((New-Shot 'ar' 1440 0.01), (New-Shot 'en' 1440 0.02), (New-Shot 'ar' 390 0.03), (New-Shot 'en' 390 0.05))
    It 'passes when every final viewport is within 5 % in ar and en, desktop and mobile' {
        $summary = Get-VisualSummary -Reports @((New-VisualReport 'docs/evidence/x/iteration-1/visual-report.json' 1 $passingShots))
        $summary.passed | Should Be $true
        $summary.worstRatio | Should Be 0.05
        $summary.iterations | Should Be 1
    }
    It 'takes the last iteration of each viewport' {
        $first = New-VisualReport 'i1/visual-report.json' 1 @((New-Shot 'ar' 1440 0.20), (New-Shot 'en' 1440 0.30), (New-Shot 'ar' 390 0.01), (New-Shot 'en' 390 0.01))
        $second = New-VisualReport 'i2/visual-report.json' 2 @((New-Shot 'ar' 1440 0.04), (New-Shot 'en' 1440 0.01))
        $summary = Get-VisualSummary -Reports @($second, $first)
        $summary.passed | Should Be $true
        $summary.iterations | Should Be 2
        @($summary.viewports).Count | Should Be 4
    }
    It 'fails above 5 %, with a missing language or width, or a null ratio' {
        (Get-VisualSummary -Reports @((New-VisualReport 'a' 1 @((New-Shot 'ar' 1440 0.051), (New-Shot 'en' 1440 0), (New-Shot 'ar' 390 0), (New-Shot 'en' 390 0))))).passed | Should Be $false
        (Get-VisualSummary -Reports @((New-VisualReport 'a' 1 @((New-Shot 'ar' 1440 0), (New-Shot 'ar' 390 0))))).detail | Should Match 'missing languages: en'
        (Get-VisualSummary -Reports @((New-VisualReport 'a' 1 @((New-Shot 'ar' 1440 0), (New-Shot 'en' 1440 0))))).detail | Should Match 'missing widths: 390'
        (Get-VisualSummary -Reports @((New-VisualReport 'a' 1 @((New-Shot 'ar' 1440 $null), (New-Shot 'en' 1440 0), (New-Shot 'ar' 390 0), (New-Shot 'en' 390 0))))).passed | Should Be $false
    }
    It 'ignores baseline-mode reports and reports absence' {
        $summary = Get-VisualSummary -Reports @((New-VisualReport 'a' $null $passingShots 'baseline'))
        $summary.present | Should Be $false
        $summary.passed | Should Be $false
    }
    It 'reads only changed visual-report.json files from the workspace' {
        $folder = Join-Path $TestDrive 'ws\docs\evidence\v\iteration-1'
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $folder 'visual-report.json'), '{"mode":"target","iteration":1,"shots":[]}')
        $reports = @(Read-VisualReports -WorkRoot (Join-Path $TestDrive 'ws') -ChangedPaths @('docs/evidence/v/iteration-1/visual-report.json', 'docs/other.md', 'docs/gone/visual-report.json'))
        $reports.Count | Should Be 1
        $reports[0].Report.mode | Should BeExactly 'target'
    }
}

Describe 'Criteria, verdict and report' {
    $headless = [pscustomobject]@{ ExitCode = 0; TimedOut = $false }
    $sequence = Test-SkillSequence -Expected $Pipeline -Observed $Pipeline
    $spec = [pscustomobject]@{ path = 'docs/features/permit.md'; ok = $true; status = 'approved'; errorCount = 0; handEditCount = 2 }
    $gates = @('build', 'rulesCheck', 'contentRules', 'docsGate' | ForEach-Object { [pscustomobject]@{ name = $_; status = 'passed' } })
    $hook = [pscustomobject]@{ denies = 1; corrected = @([pscustomobject]@{ path = 'a' }); uncorrected = @(); passed = $true }
    $probe = [pscustomobject]@{ passed = $true; reason = 'Use the `new-module` skill' }
    $visual = [pscustomobject]@{ passed = $true; worstRatio = 0.01; languages = @('ar', 'en'); widths = @(390, 1440); detail = '' }
    $review = [pscustomobject]@{ passed = $true; iterations = 2; findingCount = 3 }

    It 'passes when every criterion passes' {
        $criteria = @(Get-RunCriteria -Headless $headless -Sequence $sequence -Spec $spec -HandEdits (Test-HandEditLimit -Count 2) -HookProof $hook -Probe $probe -Gates $gates -Visual $visual -Review $review)
        $criteria.Count | Should Be 10
        @($criteria | Where-Object { -not $_.passed }).Count | Should Be 0
        Get-ProofVerdict -Mode run -Criteria $criteria | Should BeExactly 'pass'
    }
    It 'fails when a gate failed or did not run' {
        $failedGates = @($gates[0..2]) + [pscustomobject]@{ name = 'docsGate'; status = 'failed' }
        $criteria = @(Get-RunCriteria -Headless $headless -Sequence $sequence -Spec $spec -HandEdits (Test-HandEditLimit -Count 2) -HookProof $hook -Probe $probe -Gates $failedGates -Visual $visual -Review $review)
        Get-ProofVerdict -Mode run -Criteria $criteria | Should BeExactly 'fail'
        $missing = @(Get-RunCriteria -Headless $headless -Sequence $sequence -Spec $spec -HandEdits (Test-HandEditLimit -Count 2) -HookProof $hook -Probe $probe -Gates @($gates[0]) -Visual $visual -Review $review)
        (@($missing | Where-Object { $_.id -eq 'gates' })[0]).passed | Should Be $false
    }
    It 'fails every criterion that has no evidence' {
        $criteria = @(Get-RunCriteria)
        @($criteria | Where-Object { $_.passed }).Count | Should Be 0
    }
    It 'orders invalid before blocked, and a dry run passes on its own criteria' {
        Get-ProofVerdict -Mode run -Findings @('x') -Blockers @('y') | Should BeExactly 'invalid'
        Get-ProofVerdict -Mode run -Blockers @('y') | Should BeExactly 'blocked'
        Get-ProofVerdict -Mode dry-run -Criteria @([pscustomobject]@{ passed = $true }) | Should BeExactly 'dry-run'
        Get-ProofVerdict -Mode dry-run -Criteria @([pscustomobject]@{ passed = $false }) | Should BeExactly 'fail'
        Get-ProofVerdict -Mode run -Criteria @([pscustomobject]@{ passed = $true }) -InfrastructureError 'boom' | Should BeExactly 'fail'
    }
    It 'maps verdicts to exit codes' {
        Get-ProofExitCode -Verdict pass | Should Be 0
        Get-ProofExitCode -Verdict dry-run | Should Be 0
        Get-ProofExitCode -Verdict fail | Should Be 1
        Get-ProofExitCode -Verdict invalid | Should Be 2
        Get-ProofExitCode -Verdict blocked | Should Be 2
    }
    It 'formats the Markdown report with criteria, sequence, hook pairs, visual and review sections' {
        $criteria = @(Get-RunCriteria -Headless $headless -Sequence $sequence -Spec $spec -HandEdits (Test-HandEditLimit -Count 2) -HookProof $hook -Probe $probe -Gates $gates -Visual $visual -Review $review)
        $report = [pscustomobject]@{
            proof = 'permit'; recordType = 'Permit'; inputType = 'workbook'; mode = 'run'; verdict = 'pass'; runId = 'permit-1'
            startedAt = 's'; finishedAt = 'f'; infrastructureError = $null; findings = @(); blockers = @(); prompt = 'Clients request a permit.'
            criteria = $criteria; skillSequence = $sequence
            hookProof = [pscustomobject]@{ corrected = @([pscustomobject]@{ path = 'a.cs'; ruleIds = @('r.x'); skill = 'new-endpoint'; correctedWith = 'Edit' }); uncorrected = @() }
            visual = [pscustomobject]@{ present = $true; viewports = @((New-Shot 'ar' 1440 0.01 | Add-Member -NotePropertyName iteration -NotePropertyValue 1 -PassThru)) }
            review = [pscustomobject]@{ present = $true; iterations = 2; findings = @('| 1 | standards-reviewer | x | y |') }
        }
        $markdown = Format-PipelineProofReport -Report $report
        $markdown | Should Match 'Verdict: \*\*PASS\*\*'
        $markdown | Should Match 'build-feature > new-module'
        $markdown | Should Match 'a.cs: \[r.x\] skill new-endpoint; corrected with Edit'
        $markdown | Should Match '\| complaint-new \| ar \| 1440 \| 0.01 \| 1 \|'
        $markdown | Should Match 'Review log \(iterations: 2\)'
    }
}

Describe 'Evidence copy of the feature spec (no dead links, DOCS003)' {
    $specPath = 'docs/features/permit.md'

    It 'turns relative inline links into text naming the workspace path' {
        $text = 'See the [add-deadline recipe](../recipes/add-deadline.md) and [Deadlines MODULE.md](../../backend/src/Modules/Deadlines/MODULE.md).'
        ConvertTo-ProofEvidenceSpec -SpecPath $specPath -Text $text |
            Should BeExactly 'See the add-deadline recipe (`docs/recipes/add-deadline.md`) and Deadlines MODULE.md (`backend/src/Modules/Deadlines/MODULE.md`).'
    }

    It 'keeps a fragment, resolves a sibling and an image, and handles a label holding code' {
        $text = '[`Permits`](permits.md#rules) ![shot](./img/a.png)'
        ConvertTo-ProofEvidenceSpec -SpecPath $specPath -Text $text |
            Should BeExactly '`Permits` (`docs/features/permits.md#rules`) shot (`docs/features/img/a.png`)'
    }

    It 'leaves external links, anchors, code spans and fenced code untouched' {
        $text = "[web](https://example.test/a) [top](#scope) ``[x](../y.md)```n``````n[z](../recipes/a.md)`n``````"
        ConvertTo-ProofEvidenceSpec -SpecPath $specPath -Text $text | Should BeExactly $text
    }

    It 'rewrites reference definitions and HTML href/src' {
        $text = "[recipe]: ../recipes/add-notification.md `"Recipe`"`n<a href=`"../recipes/add-workflow-type.md`">w</a>"
        ConvertTo-ProofEvidenceSpec -SpecPath $specPath -Text $text |
            Should BeExactly "[recipe]: ``docs/recipes/add-notification.md`` `"Recipe`"`n<a title=`"docs/recipes/add-workflow-type.md`">w</a>"
    }

    It 'keeps a target that climbs above the workspace root as written' {
        ConvertTo-ProofEvidenceSpec -SpecPath 'spec.md' -Text '[x](../../outside.md)' | Should BeExactly 'x (`../../outside.md`)'
    }

    It 'returns empty text unchanged' {
        ConvertTo-ProofEvidenceSpec -SpecPath $specPath -Text '' | Should BeExactly ''
    }
}
