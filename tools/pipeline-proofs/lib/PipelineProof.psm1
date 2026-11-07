# PipelineProof.psm1: the pipeline-proof harness (Windows PowerShell 5.1). One proof = one plain business scenario
# (tools/pipeline-proofs/inputs/<proof>/) through Claude Code headless in a throwaway workspace, then the evidence the
# Phase 9 exit criteria ask for: routing and skill sequence, the feature spec and its hand-edit count, the hook log
# (a refused edit, then the corrected one), the refusal probe, the gates, the visual result and the review log.
# Workspaces and headless runs come from tools/ai-evals/lib/HeadlessClaude.psm1; the gates from KitEval.psm1.
# See tools/pipeline-proofs/MODULE.md.
Set-StrictMode -Version Latest

$script:ModuleFolder = $PSScriptRoot
$script:ToolFolder = Split-Path -Parent $script:ModuleFolder
$script:EvalLibFolder = Join-Path (Split-Path -Parent $script:ToolFolder) 'ai-evals\lib'
Import-Module (Join-Path $script:EvalLibFolder 'HeadlessClaude.psm1')
Import-Module (Join-Path $script:EvalLibFolder 'KitEval.psm1')

# ---------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------
$script:ProofNames = @('complaint', 'permit', 'feedback')
$script:InputsFolderName = 'inputs'
$script:DefinitionFileName = 'proof.json'
$script:DefaultWorkRootName = 'p9-proofs'
$script:LogsFolderName = 'logs'
$script:TranscriptsFolderName = 'transcripts'
$script:ProbeFolderName = 'refusal-probe'
$script:VisualEvidenceFolderName = 'visual'

$script:ReportJsonFileName = 'report.json'
$script:ReportMarkdownFileName = 'report.md'
$script:HookLogFileName = 'hook-log.jsonl'
$script:ProbeHookLogFileName = 'hook-log.jsonl'
$script:ProbeStdoutFileName = 'hook-output.json'
$script:TranscriptSummaryFileName = 'transcript-summary.json'
$script:SpecEvidenceFileName = 'spec.md'

# The evidence copy of the feature spec: its relative links pointed from docs/features/ inside the throwaway workspace,
# so they are dead in the evidence folder (docs gate DOCS003). They become plain text naming the workspace path.
$script:EvidenceFencePattern = '^ {0,3}(```|~~~)'
$script:EvidenceInlineCodePattern = '(`+).*?\1'
$script:EvidenceInlineLinkPattern = '!?\[(?<label>(?:[^\[\]]|\[[^\]]*\])*)\]\(\s*(?<target><[^>]*>|[^\s)]+)(?:\s+(?:"[^"]*"|''[^'']*''))?\s*\)'
$script:EvidenceReferenceDefinitionPattern = '^(?<lead> {0,3}\[[^\]]+\]:\s*)(?<target><[^>]*>|\S+)(?<rest>.*)$'
$script:EvidenceHtmlAttributePattern = '\b(?<attribute>href|src)(?<equals>\s*=\s*)"(?<target>[^"]*)"'
$script:EvidenceExternalTargetPattern = '^(#|//|[A-Za-z][A-Za-z0-9+.\-]*:)'
$script:EvidenceHtmlNeutralAttribute = 'title'
$script:EvidenceCodePlaceholderMark = [string][char]0
$script:EvidenceCodePlaceholderPattern = '\x00(?<index>\d+)\x00'
$script:PathSeparator = '/'
$script:CurrentSegment = '.'
$script:ParentSegment = '..'
$script:JsonDepth = 10

# Pipeline steps as the proof report names them: the skill names, and the deadline recipe (a document, not a skill).
$script:StepBuildFeature = 'build-feature'
$script:StepDeadlineRecipe = 'deadline-recipe'
$script:KnownSteps = @('build-feature', 'new-module', 'new-transition', 'deadline-recipe', 'new-permission', 'new-lookup',
    'new-endpoint', 'new-screen')
$script:DeadlineRecipePath = 'docs/recipes/add-deadline.md'
$script:SkillToolName = 'Skill'
$script:FilePathInputKey = 'file_path'
$script:ToolUseMarker = '"tool_use"'
$script:EventTypeAssistant = 'assistant'
$script:ContentTypeToolUse = 'tool_use'

# Plain-scenario rule: no slash command at the start of a word (paths such as tools/x are fine).
$script:SlashCommandPattern = '(^|\s)/[a-z][a-z0-9-]*'
$script:FigmaUrlPattern = '^https://(www\.)?figma\.com/(design|file|proto)/[A-Za-z0-9]+'

# Thresholds of the Phase 9 exit criteria.
$script:HandEditLimit = 10
$script:VisualMaxDiffRatio = 0.05
$script:VisualTargetMode = 'target'
$script:VisualReportFileName = 'visual-report.json'
$script:RequiredLanguages = @('ar', 'en')
$script:RequiredWidths = @(1440, 390)
$script:MinimumReviewIterations = 1

# Feature spec: the checker and the Review log section.
$script:SpecChecker = 'tools/feature-spec/check-spec.mjs'
$script:SpecJsonFlag = '--json'
$script:SpecRequireApprovedFlag = '--require-approved'
$script:SpecSeverityError = 'error'
$script:ReviewLogHeadingPattern = '^##\s+Review log\s*$'
$script:SectionHeadingPattern = '^##\s+'
$script:IterationsPattern = '^\s*Iterations:\s*(\d+)'
$script:TableRowPattern = '^\s*\|'
$script:TableSeparatorPattern = '^\s*\|(\s*:?-{3,}:?\s*\|)+\s*$'
$script:ListItemPattern = '^\s*(?:[-*]|\d+\.)\s+(.+)$'
$script:FencePattern = '^\s*```'
$script:MaxReviewFindings = 50
$script:PreApprovedBy = 'pipeline-proof (pre-approved)'
# Set only for a -PreApproveSpec run (its headless session and the harness's own spec check): the feature-spec checker
# accepts approvedBy $script:PreApprovedBy only when it sees this (tools/feature-spec/lib/constants.mjs).
$script:PreApprovalVariable = 'CODELAB_PIPELINE_PROOF_APPROVAL'
$script:PreApprovalEnabled = '1'

# Hook log ({ ts, hook, tool, path, decision, ruleIds[], skill } per line) and the refusal probe.
$script:HookPreToolUse = 'pre-tool-use'
$script:DecisionDeny = 'deny'
$script:DecisionAllow = 'allow'
$script:HookScript = '.claude/hooks/pre-tool-use.mjs'
$script:ProbeSkill = 'new-module'
$script:ProbeRelativePath = 'backend/src/Modules/ProofProbe/Domain/ProofProbe.cs'
$script:ProbeModuleFolder = 'backend/src/Modules/ProofProbe'
$script:ProbeContent = "namespace Starter.Modules.ProofProbe.Domain;`n`npublic sealed class ProofProbe { }`n"
$script:ProbeToolName = 'Write'
$script:ProbeEventName = 'PreToolUse'
$script:ProjectDirVariable = 'CLAUDE_PROJECT_DIR'
$script:HookLogVariable = 'CODELAB_HOOK_LOG'
$script:SkillReferenceFormat = 'Use the `{0}` skill'

# Gates run in the workspace after the headless run (KitEval check names).
$script:GateChecks = @('build', 'rulesCheck', 'contentRules', 'docsGate')
$script:GateStatusPassed = 'passed'

$script:NodeExecutable = 'node.exe'
$script:DefaultNodeDirectory = 'E:\codelab-tools\node24'
$script:DefaultDotnetPath = 'E:\codelab-tools\dotnet10\dotnet.exe'
$script:SpecCheckTimeoutMinutes = 5
$script:ProbeTimeoutMinutes = 2
$script:ReadXlsxScript = '.claude/skills/usecase-testing/scripts/Read-Xlsx.ps1'
$script:RunIdSuffixLength = 6
$script:RunIdTimeFormat = 'yyyyMMdd-HHmmss'
$script:ApprovalDateFormat = 'yyyy-MM-dd'
$script:IsoTimeFormat = 'o'

$script:ModeRun = 'run'
$script:ModeDryRun = 'dry-run'
$script:VerdictPass = 'pass'
$script:VerdictFail = 'fail'
$script:VerdictDryRun = 'dry-run'
$script:VerdictInvalid = 'invalid'
$script:VerdictBlocked = 'blocked'
$script:ExitCodes = @{ pass = 0; 'dry-run' = 0; fail = 1; invalid = 2; blocked = 2 }

$script:CriterionHeadless = 'headless-run'
$script:CriterionRouting = 'routing'
$script:CriterionSequence = 'skill-sequence'
$script:CriterionSpec = 'spec-valid'
$script:CriterionHandEdits = 'hand-edits'
$script:CriterionHookProof = 'hook-proof'
$script:CriterionProbe = 'refusal-probe'
$script:CriterionGates = 'gates'
$script:CriterionVisual = 'visual'
$script:CriterionReview = 'review-log'
$script:CriterionInputs = 'inputs-valid'

$script:UnattendedNote = 'You are working unattended in a disposable copy of the repository: nobody will answer questions, ' +
    'so make reasonable assumptions, finish the work, verify it, and leave the result in the working tree. Do not push.'
$script:PreApprovalNoteFormat = 'For this run the feature spec counts as pre-approved by the platform owner: once it is ' +
    'complete and valid, set status: approved, approvedBy: {0} and approvedOn: {1} in it, then carry on through the whole ' +
    'pipeline to the commit gate.'
$script:StopAtApprovalNote = 'Stop when the feature spec is ready for my approval.'
$script:InputsIntro = 'The material for it is in this repository:'
$script:FigmaNoteFormat = 'The screen is this Figma frame: {0}'

$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-PipelineProofConstants {
    return [pscustomobject]@{
        ProofNames         = $script:ProofNames
        KnownSteps         = $script:KnownSteps
        HandEditLimit      = $script:HandEditLimit
        VisualMaxDiffRatio = $script:VisualMaxDiffRatio
        RequiredLanguages  = $script:RequiredLanguages
        RequiredWidths     = $script:RequiredWidths
        GateChecks         = $script:GateChecks
        ProbeSkill         = $script:ProbeSkill
        ProbeRelativePath  = $script:ProbeRelativePath
        PreApprovedBy      = $script:PreApprovedBy
        ExitCodes          = $script:ExitCodes
        ReadXlsxScript     = $script:ReadXlsxScript
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------------------------------------------

function ConvertTo-RepoRelativePath {
    param([AllowEmptyString()][string] $Path)
    if (-not $Path) { return '' }
    return ($Path -replace '\\', '/').TrimStart('.', '/')
}

function Write-ProofJsonFile {
    param([Parameter(Mandatory = $true)][string] $Path, [Parameter(Mandatory = $true)][object] $Value)
    $folder = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth $script:JsonDepth), $script:Utf8NoBom)
}

function Write-ProofTextFile {
    param([Parameter(Mandatory = $true)][string] $Path, [AllowEmptyString()][string] $Text)
    $folder = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, $script:Utf8NoBom)
}

function Read-ProofTextFile {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
}

function Resolve-EvidenceLinkTarget {
    # A workspace-relative path for a local link target written in the spec, or $null for an external or anchor target.
    param([Parameter(Mandatory = $true)][string] $SpecPath, [Parameter(Mandatory = $true)][string] $Target)
    $trimmed = $Target.Trim()
    if ($trimmed.StartsWith('<') -and $trimmed.EndsWith('>')) { $trimmed = $trimmed.Substring(1, $trimmed.Length - 2).Trim() }
    if ($trimmed.Length -eq 0 -or $trimmed -match $script:EvidenceExternalTargetPattern) { return $null }
    $segments = New-Object 'System.Collections.Generic.List[string]'
    if (-not $trimmed.StartsWith($script:PathSeparator)) {
        $specFolder = $SpecPath.Replace('\', $script:PathSeparator)
        $lastSeparator = $specFolder.LastIndexOf($script:PathSeparator)
        $specFolder = if ($lastSeparator -ge 0) { $specFolder.Substring(0, $lastSeparator) } else { '' }
        foreach ($part in $specFolder.Split($script:PathSeparator)) { if ($part) { $segments.Add($part) } }
    }
    foreach ($part in $trimmed.Split($script:PathSeparator)) {
        if (-not $part -or $part -eq $script:CurrentSegment) { continue }
        if ($part -eq $script:ParentSegment) {
            if ($segments.Count -eq 0) { return $trimmed }
            $segments.RemoveAt($segments.Count - 1)
            continue
        }
        $segments.Add($part)
    }
    return ($segments.ToArray() -join $script:PathSeparator)
}

function ConvertTo-EvidenceProseLine {
    param([Parameter(Mandatory = $true)][string] $SpecPath, [AllowEmptyString()][string] $Prose)
    $converted = [regex]::Replace($Prose, $script:EvidenceInlineLinkPattern, {
            param($match)
            $resolved = Resolve-EvidenceLinkTarget -SpecPath $SpecPath -Target $match.Groups['target'].Value
            if ($null -eq $resolved) { return $match.Value }
            return ('{0} (`{1}`)' -f $match.Groups['label'].Value, $resolved)
        })
    return [regex]::Replace($converted, $script:EvidenceHtmlAttributePattern, {
            param($match)
            $resolved = Resolve-EvidenceLinkTarget -SpecPath $SpecPath -Target $match.Groups['target'].Value
            if ($null -eq $resolved) { return $match.Value }
            return ('{0}{1}"{2}"' -f $script:EvidenceHtmlNeutralAttribute, $match.Groups['equals'].Value, $resolved)
        }, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function ConvertTo-ProofEvidenceSpec {
    # The feature spec as kept in the evidence folder: every local link (inline, image, reference definition, HTML
    # href/src) outside code becomes text naming its workspace path, so the copy has no dead links (DOCS003).
    param([Parameter(Mandatory = $true)][string] $SpecPath, [AllowEmptyString()][string] $Text)
    if (-not $Text) { return $Text }
    $lines = $Text -split "`n"
    $inFence = $false
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if ($line -match $script:EvidenceFencePattern) { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        $reference = [regex]::Match($line, $script:EvidenceReferenceDefinitionPattern)
        if ($reference.Success) {
            $resolved = Resolve-EvidenceLinkTarget -SpecPath $SpecPath -Target $reference.Groups['target'].Value
            if ($null -ne $resolved) { $lines[$index] = '{0}`{1}`{2}' -f $reference.Groups['lead'].Value, $resolved, $reference.Groups['rest'].Value }
            continue
        }
        # Code spans are parked behind placeholders (a label may hold code: [`x`](path)) and put back unchanged.
        $codeSpans = New-Object 'System.Collections.Generic.List[string]'
        $parked = [regex]::Replace($line, $script:EvidenceInlineCodePattern, {
                param($match)
                $codeSpans.Add($match.Value)
                return ('{0}{1}{0}' -f $script:EvidenceCodePlaceholderMark, ($codeSpans.Count - 1))
            })
        $converted = ConvertTo-EvidenceProseLine -SpecPath $SpecPath -Prose $parked
        $lines[$index] = [regex]::Replace($converted, $script:EvidenceCodePlaceholderPattern, {
                param($match)
                return $codeSpans[[int]$match.Groups['index'].Value]
            })
    }
    return ($lines -join "`n")
}

function New-Criterion {
    param([string] $Id, [string] $Description, [bool] $Passed, [AllowEmptyString()][string] $Detail = '')
    return [pscustomobject]@{ id = $Id; description = $Description; passed = $Passed; detail = $Detail }
}

# ---------------------------------------------------------------------------------------------------------------
# Proof definitions and the prompt
# ---------------------------------------------------------------------------------------------------------------

function Get-ProofDefinition {
    param([Parameter(Mandatory = $true)][string] $Proof, [Parameter(Mandatory = $true)][string] $InputsRoot)
    if ($script:ProofNames -notcontains $Proof) { throw "Unknown proof '$Proof'; expected one of $($script:ProofNames -join ', ')." }
    $folder = Join-Path $InputsRoot $Proof
    $definitionPath = Join-Path $folder $script:DefinitionFileName
    if (-not (Test-Path -LiteralPath $definitionPath -PathType Leaf)) { throw "Proof definition $definitionPath does not exist." }
    $definition = (Read-ProofTextFile $definitionPath) | ConvertFrom-Json
    $scenarioPath = Join-Path $folder ([string] (Get-ObjectValue $definition 'scenarioFile'))
    $scenario = if (Test-Path -LiteralPath $scenarioPath -PathType Leaf) { (Read-ProofTextFile $scenarioPath).Trim() } else { '' }
    return [pscustomobject]@{
        Proof            = [string] (Get-ObjectValue $definition 'proof')
        RecordType       = [string] (Get-ObjectValue $definition 'recordType')
        InputType        = [string] (Get-ObjectValue $definition 'inputType')
        Folder           = $folder
        ScenarioPath     = $scenarioPath
        Scenario         = $scenario
        Inputs           = @(@(Get-ObjectValue $definition 'inputs') | Where-Object { $_ } | ForEach-Object { [string] $_ })
        ExpectedSteps    = @(@(Get-ObjectValue $definition 'expectedSteps') | Where-Object { $_ } | ForEach-Object { [string] $_ })
        RequiresFigmaUrl = [bool] (Get-ObjectValue $definition 'requiresFigmaUrl')
        MaxTurns         = Get-ObjectValue $definition 'maxTurns'
        TimeoutMinutes   = Get-ObjectValue $definition 'timeoutMinutes'
        Workbook         = Get-ObjectValue $definition 'workbook'
    }
}

# Findings when a scenario is not plain business wording (pure): a slash command or a skill named outright.
function Test-PlainScenario {
    param([AllowEmptyString()][string] $Text)
    $findings = New-Object System.Collections.Generic.List[string]
    if (-not $Text -or -not $Text.Trim()) { $findings.Add('the scenario is empty'); return @($findings) }
    $slash = [regex]::Match($Text, $script:SlashCommandPattern)
    if ($slash.Success) { $findings.Add("the scenario contains a slash command '$($slash.Value.Trim())'") }
    foreach ($step in $script:KnownSteps) {
        if ($Text -match ('\b' + [regex]::Escape($step) + '\b')) { $findings.Add("the scenario names the pipeline step '$step'") }
    }
    return @($findings)
}

# Definition findings (pure apart from Test-Path on the inputs).
function Test-ProofDefinition {
    param([Parameter(Mandatory = $true)][object] $Definition, [Parameter(Mandatory = $true)][string] $RepoRoot, [string] $FigmaUrl)
    $findings = New-Object System.Collections.Generic.List[string]
    if ($Definition.Proof -and $script:ProofNames -notcontains $Definition.Proof) { $findings.Add("proof.json names an unknown proof '$($Definition.Proof)'") }
    if (-not $Definition.RecordType) { $findings.Add('proof.json has no recordType') }
    if (-not (Test-Path -LiteralPath $Definition.ScenarioPath -PathType Leaf)) { $findings.Add("the scenario file $($Definition.ScenarioPath) does not exist") }
    foreach ($finding in @(Test-PlainScenario -Text $Definition.Scenario)) { $findings.Add($finding) }
    foreach ($inputPath in $Definition.Inputs) {
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $inputPath) -PathType Leaf)) { $findings.Add("the input $inputPath does not exist") }
    }
    $steps = @($Definition.ExpectedSteps)
    if ($steps.Count -eq 0) { $findings.Add('proof.json lists no expectedSteps') }
    elseif ($steps[0] -ne $script:StepBuildFeature) { $findings.Add("the first expected step must be '$script:StepBuildFeature'") }
    foreach ($step in $steps) { if ($script:KnownSteps -notcontains $step) { $findings.Add("unknown expected step '$step'") } }
    if ($FigmaUrl -and $FigmaUrl -notmatch $script:FigmaUrlPattern) { $findings.Add("the Figma URL '$FigmaUrl' is not a figma.com design link") }
    return @($findings)
}

# Reasons the proof cannot start yet (pure): an input only the owner can provide.
function Get-ProofBlockers {
    param([Parameter(Mandatory = $true)][object] $Definition, [string] $FigmaUrl)
    if ($Definition.RequiresFigmaUrl -and -not $FigmaUrl) {
        return @("the $($Definition.Proof) proof needs a frame in a Codelab Figma file: the owner provides its URL (-FigmaUrl)")
    }
    return @()
}

# The use-case IDs in a Read-Xlsx -AsJson sheet dump: first-column values matching the pattern (pure).
function Get-WorkbookUseCaseIds {
    param([Parameter(Mandatory = $true)][string] $SheetJson, [Parameter(Mandatory = $true)][string] $IdPattern)
    $ids = New-Object System.Collections.Generic.List[string]
    # Windows PowerShell 5.1 emits a JSON array as one object: assign it first so foreach enumerates the sheets.
    $parsed = $SheetJson | ConvertFrom-Json
    foreach ($sheet in $parsed) {
        foreach ($row in @(Get-ObjectValue $sheet 'rows')) {
            $cells = @(Get-ObjectValue $row 'cells')
            if ($cells.Count -eq 0) { continue }
            $first = ([string] $cells[0]).Trim()
            if ($first -match $IdPattern -and -not $ids.Contains($first)) { $ids.Add($first) }
        }
    }
    return @($ids)
}

# Reads the proof's workbook with the usecase-testing reader and checks its use-case count.
function Test-ProofWorkbook {
    param([object] $Workbook, [Parameter(Mandatory = $true)][string] $RepoRoot)
    if ($null -eq $Workbook) { return [pscustomobject]@{ present = $false; useCaseIds = @(); findings = @() } }
    $path = Join-Path $RepoRoot ([string] (Get-ObjectValue $Workbook 'path'))
    $sheet = [string] (Get-ObjectValue $Workbook 'useCaseSheet')
    $expected = [int] (Get-ObjectValue $Workbook 'useCaseCount')
    $reader = Join-Path $RepoRoot $script:ReadXlsxScript
    $findings = New-Object System.Collections.Generic.List[string]
    $ids = @()
    try {
        $json = (& $reader -Path $path -Sheet $sheet -AsJson) -join "`n"
        $ids = @(Get-WorkbookUseCaseIds -SheetJson $json -IdPattern ([string] (Get-ObjectValue $Workbook 'useCaseIdPattern')))
        if ($ids.Count -ne $expected) { $findings.Add("the workbook lists $($ids.Count) use case(s) on '$sheet'; expected $expected") }
    } catch {
        $findings.Add("the workbook $path could not be read: $($_.Exception.Message)")
    }
    return [pscustomobject]@{ present = $true; path = (ConvertTo-RepoRelativePath ([string] (Get-ObjectValue $Workbook 'path'))); useCaseIds = $ids; findings = @($findings) }
}

# The prompt of a proof run (pure): the plain scenario, the input paths, the approval note and the unattended note.
function Get-ProofPrompt {
    param(
        [Parameter(Mandatory = $true)][object] $Definition,
        [switch] $PreApproveSpec,
        [string] $FigmaUrl,
        [Parameter(Mandatory = $true)][string] $ApprovalDate
    )
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add($Definition.Scenario)
    if (@($Definition.Inputs).Count -gt 0) {
        $lines.Add('')
        $lines.Add($script:InputsIntro)
        foreach ($inputPath in $Definition.Inputs) { $lines.Add("- $inputPath") }
    }
    if ($FigmaUrl) { $lines.Add(''); $lines.Add(($script:FigmaNoteFormat -f $FigmaUrl)) }
    $lines.Add('')
    if ($PreApproveSpec) { $lines.Add(($script:PreApprovalNoteFormat -f $script:PreApprovedBy, $ApprovalDate)) } else { $lines.Add($script:StopAtApprovalNote) }
    $lines.Add($script:UnattendedNote)
    return ($lines -join "`n")
}

# ---------------------------------------------------------------------------------------------------------------
# Transcript: the pipeline step sequence
# ---------------------------------------------------------------------------------------------------------------

function ConvertTo-PipelineStep {
    param([Parameter(Mandatory = $true)][object] $ToolUse)
    $name = [string] (Get-ObjectValue $ToolUse 'name')
    $toolInput = Get-ObjectValue $ToolUse 'input'
    if ($name -eq $script:SkillToolName) { return ConvertTo-SkillName $toolInput }
    $filePath = ConvertTo-RepoRelativePath ([string] (Get-ObjectValue $toolInput $script:FilePathInputKey))
    if ($filePath -and $filePath.EndsWith($script:DeadlineRecipePath, [StringComparison]::OrdinalIgnoreCase)) { return $script:StepDeadlineRecipe }
    return $null
}

# Distinct pipeline steps in first-use order from stream-json lines (pure): Skill tool calls give the skill name, a
# file tool on docs/recipes/add-deadline.md gives 'deadline-recipe'. Subagent tool calls count too.
function Get-ProofStepSequence {
    param([AllowEmptyCollection()][string[]] $Lines = @())
    $steps = New-Object System.Collections.Generic.List[string]
    foreach ($line in $Lines) {
        if (-not $line -or -not $line.Contains($script:ToolUseMarker)) { continue }
        try { $event = $line | ConvertFrom-Json } catch { continue }
        if ((Get-ObjectValue $event 'type') -ne $script:EventTypeAssistant) { continue }
        foreach ($item in @(Get-ObjectValue (Get-ObjectValue $event 'message') 'content')) {
            if ($null -eq $item -or (Get-ObjectValue $item 'type') -ne $script:ContentTypeToolUse) { continue }
            $step = ConvertTo-PipelineStep $item
            if ($step -and -not $steps.Contains($step)) { $steps.Add($step) }
        }
    }
    return @($steps)
}

function Read-ProofTranscriptSteps {
    param([Parameter(Mandatory = $true)][string] $TranscriptPath)
    if (-not (Test-Path -LiteralPath $TranscriptPath -PathType Leaf)) { return @() }
    return @(Get-ProofStepSequence -Lines ([IO.File]::ReadAllLines($TranscriptPath)))
}

# The routing and skill-sequence check (pure). Routed: build-feature was invoked before any other pipeline step.
# In order: the expected steps appear (first use) in the expected order; other skills in between are fine.
function Test-SkillSequence {
    param([Parameter(Mandatory = $true)][string[]] $Expected, [AllowEmptyCollection()][string[]] $Observed = @())
    $observedList = @($Observed | Where-Object { $_ })
    $missing = @($Expected | Where-Object { $observedList -notcontains $_ })
    $positions = @($Expected | Where-Object { $observedList -contains $_ } | ForEach-Object { [array]::IndexOf($observedList, $_) })
    $inOrder = $true
    for ($i = 1; $i -lt $positions.Count; $i++) { if ($positions[$i] -le $positions[$i - 1]) { $inOrder = $false } }
    $pipelineSteps = @($observedList | Where-Object { $script:KnownSteps -contains $_ })
    $routed = ($pipelineSteps.Count -gt 0 -and $pipelineSteps[0] -eq $script:StepBuildFeature)
    return [pscustomobject]@{
        expected             = @($Expected)
        observed             = $observedList
        missing              = $missing
        inOrder              = $inOrder
        firstPipelineStep    = if ($pipelineSteps.Count -gt 0) { $pipelineSteps[0] } else { $null }
        routedToBuildFeature = $routed
        passed               = ($routed -and $missing.Count -eq 0 -and $inOrder)
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Hook log: a refused edit, then the corrected one
# ---------------------------------------------------------------------------------------------------------------

function ConvertFrom-HookLogLines {
    param([AllowEmptyCollection()][string[]] $Lines = @())
    $entries = New-Object System.Collections.Generic.List[object]
    $malformed = 0
    foreach ($line in $Lines) {
        if (-not $line -or -not $line.Trim()) { continue }
        try { $entries.Add(($line | ConvertFrom-Json)) } catch { $malformed++ }
    }
    return [pscustomobject]@{ Entries = $entries.ToArray(); MalformedLines = $malformed }
}

function Read-HookLog {
    param([Parameter(Mandatory = $true)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return [pscustomobject]@{ Entries = @(); MalformedLines = 0; Present = $false } }
    $parsed = ConvertFrom-HookLogLines -Lines ([IO.File]::ReadAllLines($Path))
    return [pscustomobject]@{ Entries = $parsed.Entries; MalformedLines = $parsed.MalformedLines; Present = $true }
}

# Every repo file a log entry names (pure): its paths[] (a shell command can touch several files), else its single path.
function Get-HookEntryPaths {
    param([object] $Entry)
    $raw = @(Get-ObjectValue $Entry 'paths' | Where-Object { $_ })
    if ($raw.Count -eq 0) { $raw = @(Get-ObjectValue $Entry 'path' | Where-Object { $_ }) }
    return @($raw | ForEach-Object { ConvertTo-RepoRelativePath ([string] $_) } | Where-Object { $_ } | Select-Object -Unique)
}

# True when two path lists share a file (pure, case-insensitive like the Windows file system).
function Test-HookPathOverlap {
    param([AllowEmptyCollection()][string[]] $Left = @(), [AllowEmptyCollection()][string[]] $Right = @())
    foreach ($candidate in $Right) { foreach ($target in $Left) { if ($candidate -ieq $target) { return $true } } }
    return $false
}

# Pairs every pre-tool-use deny with the first later allow on any of the files it named (paths[], else path) (pure).
# The hook proof passes with at least one corrected pair.
function Get-HookProof {
    param([AllowEmptyCollection()][object[]] $Entries = @(), [int] $MalformedLines = 0)
    $list = @($Entries | Where-Object { $null -ne $_ -and (Get-ObjectValue $_ 'hook') -eq $script:HookPreToolUse })
    $corrected = New-Object System.Collections.Generic.List[object]
    $uncorrected = New-Object System.Collections.Generic.List[object]
    $denies = 0
    for ($i = 0; $i -lt $list.Count; $i++) {
        $entry = $list[$i]
        if ((Get-ObjectValue $entry 'decision') -ne $script:DecisionDeny) { continue }
        $denies++
        $paths = @(Get-HookEntryPaths $entry)
        $path = if ($paths.Count -gt 0) { $paths[0] } else { '' }
        $record = [ordered]@{
            path    = $path
            paths   = $paths
            tool    = [string] (Get-ObjectValue $entry 'tool')
            ruleIds = @(Get-ObjectValue $entry 'ruleIds' | Where-Object { $_ })
            skill   = Get-ObjectValue $entry 'skill'
            deniedAt = Get-ObjectValue $entry 'ts'
        }
        if (-not $path) { continue }
        $fix = $null
        for ($j = $i + 1; $j -lt $list.Count; $j++) {
            $later = $list[$j]
            if ((Get-ObjectValue $later 'decision') -eq $script:DecisionAllow -and
                (Test-HookPathOverlap -Left $paths -Right @(Get-HookEntryPaths $later))) { $fix = $later; break }
        }
        if ($null -ne $fix) {
            $record['correctedAt'] = Get-ObjectValue $fix 'ts'
            $record['correctedWith'] = [string] (Get-ObjectValue $fix 'tool')
            $corrected.Add([pscustomobject] $record)
        } else {
            $uncorrected.Add([pscustomobject] $record)
        }
    }
    return [pscustomobject]@{
        entries        = $list.Count
        denies         = $denies
        corrected      = $corrected.ToArray()
        uncorrected    = $uncorrected.ToArray()
        malformedLines = $MalformedLines
        passed         = ($corrected.Count -ge 1)
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Refusal probe: a hand-made module file through the PreToolUse hook must be refused, naming new-module
# ---------------------------------------------------------------------------------------------------------------

# The hook input of the probe (pure): a Write of a file in a module folder that has no module.rules.yaml.
function Get-RefusalProbeInput {
    param([Parameter(Mandatory = $true)][string] $WorkRoot)
    $root = $WorkRoot -replace '\\', '/'
    $hookInput = [ordered]@{
        cwd             = $root
        hook_event_name = $script:ProbeEventName
        tool_name       = $script:ProbeToolName
        tool_input      = [ordered]@{ file_path = "$root/$script:ProbeRelativePath"; content = $script:ProbeContent }
    }
    return (ConvertTo-Json -InputObject $hookInput -Depth 4 -Compress)
}

# Grades the hook's stdout and decision log (pure): denied, the reason names the skill, the log says so too.
function Test-RefusalProbeOutput {
    param([AllowEmptyString()][string] $Stdout, [AllowEmptyCollection()][object[]] $LogEntries = @(), [string] $ExpectedSkill = $script:ProbeSkill)
    $decision = $null
    $reason = ''
    try {
        $output = $Stdout | ConvertFrom-Json
        $specific = Get-ObjectValue $output 'hookSpecificOutput'
        $decision = [string] (Get-ObjectValue $specific 'permissionDecision')
        $reason = [string] (Get-ObjectValue $specific 'permissionDecisionReason')
    } catch { $reason = "hook output is not JSON: $Stdout" }
    $namesSkill = $reason.Contains(($script:SkillReferenceFormat -f $ExpectedSkill))
    $logged = @($LogEntries | Where-Object { $null -ne $_ -and (Get-ObjectValue $_ 'decision') -eq $script:DecisionDeny } | Select-Object -Last 1)
    $loggedSkill = if ($logged.Count -gt 0) { Get-ObjectValue $logged[0] 'skill' } else { $null }
    return [pscustomobject]@{
        path        = $script:ProbeRelativePath
        decision    = $decision
        reason      = $reason
        namesSkill  = $namesSkill
        loggedSkill = $loggedSkill
        passed      = ($decision -eq $script:DecisionDeny -and $namesSkill -and $loggedSkill -eq $ExpectedSkill)
    }
}

function Invoke-RefusalProbe {
    param(
        [Parameter(Mandatory = $true)][string] $WorkRoot,
        [Parameter(Mandatory = $true)][string] $LogFolder,
        [string] $NodeDirectory = $script:DefaultNodeDirectory
    )
    if (Test-Path -LiteralPath (Join-Path $WorkRoot $script:ProbeModuleFolder)) {
        return [pscustomobject]@{ path = $script:ProbeRelativePath; decision = $null; reason = "$script:ProbeModuleFolder already exists: the probe needs a module folder nobody created"; namesSkill = $false; loggedSkill = $null; passed = $false }
    }
    if (-not (Test-Path -LiteralPath $LogFolder)) { New-Item -ItemType Directory -Path $LogFolder -Force | Out-Null }
    $hookLog = Join-Path $LogFolder $script:ProbeHookLogFileName
    $stdoutPath = Join-Path $LogFolder $script:ProbeStdoutFileName
    $environment = Get-HeadlessEnvironment -HookLogPath $hookLog -NodeDirectory $NodeDirectory
    $environment[$script:ProjectDirVariable] = $WorkRoot
    $node = Join-Path $NodeDirectory $script:NodeExecutable
    if (-not (Test-Path -LiteralPath $node)) { $node = $script:NodeExecutable }
    $run = Invoke-LoggedProcess -FilePath $node -ArgumentList @($script:HookScript) -WorkingDirectory $WorkRoot -LogPath $stdoutPath `
        -TimeoutMinutes $script:ProbeTimeoutMinutes -StandardInputText (Get-RefusalProbeInput -WorkRoot $WorkRoot) -Environment $environment
    $stdout = if (Test-Path -LiteralPath $stdoutPath) { Read-ProofTextFile $stdoutPath } else { '' }
    $result = Test-RefusalProbeOutput -Stdout $stdout -LogEntries @((Read-HookLog -Path $hookLog).Entries)
    $result | Add-Member -NotePropertyName exitCode -NotePropertyValue $run.ExitCode
    return $result
}

# ---------------------------------------------------------------------------------------------------------------
# Feature spec: check-spec result, hand edits, review log
# ---------------------------------------------------------------------------------------------------------------

# The check-spec --json output as a result object (pure). Unparseable output is a failed check.
function ConvertFrom-SpecCheckOutput {
    param([AllowEmptyString()][string] $Text, [int] $ExitCode, [string] $SpecPath)
    try { $parsed = $Text | ConvertFrom-Json } catch { $parsed = $null }
    if ($null -eq $parsed) {
        return [pscustomobject]@{ path = $SpecPath; exitCode = $ExitCode; ok = $false; status = $null; handEditCount = $null; errorCount = $null; findings = @(); detail = "check-spec output is not JSON: $Text" }
    }
    $findings = @(@(Get-ObjectValue $parsed 'findings') | Where-Object { $null -ne $_ } | ForEach-Object {
            [pscustomobject]@{ ruleId = Get-ObjectValue $_ 'ruleId'; severity = Get-ObjectValue $_ 'severity'; message = Get-ObjectValue $_ 'message'; line = Get-ObjectValue $_ 'line' }
        })
    $errors = @($findings | Where-Object { $_.severity -eq $script:SpecSeverityError }).Count
    return [pscustomobject]@{
        path          = $SpecPath
        exitCode      = $ExitCode
        ok            = ([bool] (Get-ObjectValue $parsed 'ok') -and $ExitCode -eq 0)
        status        = Get-ObjectValue $parsed 'status'
        handEditCount = Get-ObjectValue $parsed 'handEditCount'
        errorCount    = $errors
        findings      = $findings
        detail        = ''
    }
}

function Invoke-ProofSpecCheck {
    param(
        [Parameter(Mandatory = $true)][string] $WorkRoot,
        [Parameter(Mandatory = $true)][string] $SpecPath,
        [Parameter(Mandatory = $true)][string] $LogFolder,
        [switch] $RequireApproved,
        [string] $NodeDirectory = $script:DefaultNodeDirectory
    )
    $arguments = @($script:SpecChecker, $SpecPath, $script:SpecJsonFlag)
    if ($RequireApproved) { $arguments += $script:SpecRequireApprovedFlag }
    $node = Join-Path $NodeDirectory $script:NodeExecutable
    if (-not (Test-Path -LiteralPath $node)) { $node = $script:NodeExecutable }
    $log = Join-Path $LogFolder 'spec-check.json'
    $extra = if ($RequireApproved) { Get-PreApprovalEnvironment } else { @{} }
    $run = Invoke-LoggedProcess -FilePath $node -ArgumentList $arguments -WorkingDirectory $WorkRoot -LogPath $log `
        -TimeoutMinutes $script:SpecCheckTimeoutMinutes -Environment (Get-HeadlessEnvironment -NodeDirectory $NodeDirectory -ExtraEnvironment $extra)
    $text = if (Test-Path -LiteralPath $log) { Read-ProofTextFile $log } else { '' }
    return ConvertFrom-SpecCheckOutput -Text $text -ExitCode $run.ExitCode -SpecPath $SpecPath
}

# The hand-edit criterion (pure): at most $Limit edits after approval, and a count must exist.
function Test-HandEditLimit {
    param([object] $Count, [int] $Limit = $script:HandEditLimit)
    if ($null -eq $Count) { return [pscustomobject]@{ count = $null; limit = $Limit; passed = $false; detail = 'no hand-edit count (no valid spec)' } }
    $value = [int] $Count
    return [pscustomobject]@{ count = $value; limit = $Limit; passed = ($value -le $Limit); detail = "$value of at most $Limit hand edits" }
}

# The Review log section of a spec (pure): its findings (table rows after the header, list items) and the
# 'Iterations: <n>' count.
function Get-ReviewLogSummary {
    param([AllowEmptyString()][string] $SpecText)
    $lines = @(($SpecText -replace "`r", '') -split "`n")
    $inSection = $false
    $inFence = $false
    $present = $false
    $iterations = $null
    $tableRows = 0
    $findings = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line -match $script:ReviewLogHeadingPattern) { $inSection = $true; $present = $true; continue }
        if (-not $inSection) { continue }
        if ($line -match $script:FencePattern) { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        if ($line -match $script:SectionHeadingPattern) { break }
        if ($line -match $script:IterationsPattern) { $iterations = [int] $Matches[1]; continue }
        if ($line -match $script:TableSeparatorPattern) { continue }
        if ($line -match $script:TableRowPattern) {
            $tableRows++
            if ($tableRows -gt 1) { $findings.Add($line.Trim()) }
            continue
        }
        if ($line -match $script:ListItemPattern) { $findings.Add($Matches[1].Trim()) }
    }
    $kept = @($findings | Select-Object -First $script:MaxReviewFindings)
    return [pscustomobject]@{
        present      = $present
        iterations   = $iterations
        findingCount = $findings.Count
        findings     = $kept
        passed       = ($present -and $null -ne $iterations -and $iterations -ge $script:MinimumReviewIterations)
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Visual result
# ---------------------------------------------------------------------------------------------------------------

# Final per-viewport differences across visual-verifier target reports (pure). A later iteration of the same
# route, language and width replaces an earlier one. Pass: every final viewport within the ratio, ar and en and the
# desktop and mobile widths all present.
function Get-VisualSummary {
    param(
        [AllowEmptyCollection()][object[]] $Reports = @(),
        [double] $MaxDiffRatio = $script:VisualMaxDiffRatio,
        [string[]] $RequiredLanguages = $script:RequiredLanguages,
        [int[]] $RequiredWidths = $script:RequiredWidths
    )
    $targets = @($Reports | Where-Object { $null -ne $_ -and (Get-ObjectValue $_.Report 'mode') -eq $script:VisualTargetMode })
    if ($targets.Count -eq 0) {
        return [pscustomobject]@{ present = $false; reports = @(); viewports = @(); worstRatio = $null; iterations = $null; languages = @(); widths = @(); passed = $false; detail = 'no visual-verifier target report found' }
    }
    $ordered = @($targets | Sort-Object -Property @{ Expression = { $iteration = Get-ObjectValue $_.Report 'iteration'; if ($null -eq $iteration) { 0 } else { [int] $iteration } } }, @{ Expression = { $_.Path } })
    $final = [ordered]@{}
    $maxIteration = $null
    foreach ($entry in $ordered) {
        $iteration = Get-ObjectValue $entry.Report 'iteration'
        if ($null -ne $iteration -and ($null -eq $maxIteration -or [int] $iteration -gt $maxIteration)) { $maxIteration = [int] $iteration }
        foreach ($shot in @(Get-ObjectValue $entry.Report 'shots')) {
            if ($null -eq $shot) { continue }
            $key = '{0}|{1}|{2}' -f (Get-ObjectValue $shot 'route'), (Get-ObjectValue $shot 'language'), (Get-ObjectValue $shot 'width')
            $ratio = Get-ObjectValue $shot 'ratio'
            $final[$key] = [pscustomobject]@{
                route     = [string] (Get-ObjectValue $shot 'route')
                language  = [string] (Get-ObjectValue $shot 'language')
                width     = [int] (Get-ObjectValue $shot 'width')
                ratio     = $ratio
                status    = [string] (Get-ObjectValue $shot 'status')
                iteration = $iteration
                report    = $entry.Path
                passed    = ($null -ne $ratio -and [double] $ratio -le $MaxDiffRatio)
            }
        }
    }
    $viewports = @($final.Values)
    $languages = @($viewports | ForEach-Object { $_.language } | Sort-Object -Unique)
    $widths = @($viewports | ForEach-Object { $_.width } | Sort-Object -Unique)
    $ratios = @($viewports | Where-Object { $null -ne $_.ratio } | ForEach-Object { [double] $_.ratio })
    $worst = if ($ratios.Count -gt 0) { ($ratios | Measure-Object -Maximum).Maximum } else { $null }
    $missingLanguages = @($RequiredLanguages | Where-Object { $languages -notcontains $_ })
    $missingWidths = @($RequiredWidths | Where-Object { $widths -notcontains $_ })
    $failing = @($viewports | Where-Object { -not $_.passed })
    $details = New-Object System.Collections.Generic.List[string]
    if ($missingLanguages.Count -gt 0) { $details.Add("missing languages: $($missingLanguages -join ', ')") }
    if ($missingWidths.Count -gt 0) { $details.Add("missing widths: $($missingWidths -join ', ')") }
    if ($failing.Count -gt 0) { $details.Add("$($failing.Count) viewport(s) above $MaxDiffRatio") }
    return [pscustomobject]@{
        present    = $true
        reports    = @($ordered | ForEach-Object { $_.Path })
        viewports  = $viewports
        worstRatio = $worst
        iterations = $maxIteration
        languages  = $languages
        widths     = $widths
        passed     = ($viewports.Count -gt 0 -and $failing.Count -eq 0 -and $missingLanguages.Count -eq 0 -and $missingWidths.Count -eq 0)
        detail     = ($details -join '; ')
    }
}

function Read-VisualReports {
    param([Parameter(Mandatory = $true)][string] $WorkRoot, [AllowEmptyCollection()][string[]] $ChangedPaths = @())
    $reports = New-Object System.Collections.Generic.List[object]
    foreach ($path in @($ChangedPaths | Where-Object { $_ -and (Split-Path -Leaf $_) -eq $script:VisualReportFileName } | Sort-Object -Unique)) {
        $full = Join-Path $WorkRoot $path
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        try { $reports.Add([pscustomobject]@{ Path = $path; Report = ((Read-ProofTextFile $full) | ConvertFrom-Json) }) } catch { continue }
    }
    return $reports.ToArray()
}

# ---------------------------------------------------------------------------------------------------------------
# Workspace state
# ---------------------------------------------------------------------------------------------------------------

function Get-WorkspaceChangedPaths {
    param([Parameter(Mandatory = $true)][string] $WorkRoot, [Parameter(Mandatory = $true)][string] $BaseRef)
    $ErrorActionPreference = 'Continue'
    $changed = @(& git -C $WorkRoot diff --name-only $BaseRef 2>$null)
    $untracked = @(& git -C $WorkRoot ls-files -o --exclude-standard 2>$null)
    return @($changed + $untracked | Where-Object { $_ } | Sort-Object -Unique)
}

# ---------------------------------------------------------------------------------------------------------------
# Criteria, verdict and report
# ---------------------------------------------------------------------------------------------------------------

function Get-RunCriteria {
    param(
        [object] $Headless, [object] $Sequence, [object] $Spec, [object] $HandEdits, [object] $HookProof,
        [object] $Probe, [AllowEmptyCollection()][object[]] $Gates = @(), [object] $Visual, [object] $Review
    )
    $criteria = New-Object System.Collections.Generic.List[object]
    $headlessPassed = ($null -ne $Headless -and $Headless.ExitCode -eq 0 -and -not $Headless.TimedOut)
    $headlessDetail = if ($null -eq $Headless) { 'the headless run did not start' } else { "exit $($Headless.ExitCode), timed out: $($Headless.TimedOut)" }
    $criteria.Add((New-Criterion $script:CriterionHeadless 'The headless run finished (exit 0, no timeout)' $headlessPassed $headlessDetail))
    $sequenceKnown = ($null -ne $Sequence)
    $criteria.Add((New-Criterion $script:CriterionRouting 'A plain scenario routed to build-feature first' ($sequenceKnown -and $Sequence.routedToBuildFeature) $(if ($sequenceKnown) { "first pipeline step: $($Sequence.firstPipelineStep)" } else { 'no transcript' })))
    $sequenceDetail = if ($sequenceKnown) { "observed: $(@($Sequence.observed) -join ' > '); missing: $(@($Sequence.missing) -join ', '); in order: $($Sequence.inOrder)" } else { 'no transcript' }
    $criteria.Add((New-Criterion $script:CriterionSequence 'build-feature invoked the expected skills in order' ($sequenceKnown -and $Sequence.passed) $sequenceDetail))
    $specDetail = if ($null -eq $Spec) { 'no feature spec written under docs/features/' } else { "$($Spec.path): status $($Spec.status), $($Spec.errorCount) error finding(s)" }
    $criteria.Add((New-Criterion $script:CriterionSpec 'The feature spec validates (check-spec)' ($null -ne $Spec -and $Spec.ok) $specDetail))
    $handDetail = if ($null -eq $HandEdits) { 'no hand-edit count' } else { $HandEdits.detail }
    $criteria.Add((New-Criterion $script:CriterionHandEdits "At most $script:HandEditLimit hand edits after approval, each logged" ($null -ne $HandEdits -and $HandEdits.passed) $handDetail))
    $hookDetail = if ($null -eq $HookProof) { 'no hook log' } else { "$($HookProof.denies) deny, $(@($HookProof.corrected).Count) corrected, $(@($HookProof.uncorrected).Count) not corrected" }
    $criteria.Add((New-Criterion $script:CriterionHookProof 'The hook log shows a refused edit that was then corrected' ($null -ne $HookProof -and $HookProof.passed) $hookDetail))
    $criteria.Add((New-Criterion $script:CriterionProbe "A hand-made module file is refused, naming $script:ProbeSkill" ($null -ne $Probe -and $Probe.passed) $(if ($null -eq $Probe) { 'not run' } else { $Probe.reason })))
    $gateList = @($Gates | Where-Object { $null -ne $_ })
    $failedGates = @($gateList | Where-Object { $_.status -ne $script:GateStatusPassed } | ForEach-Object { $_.name })
    $gatesPassed = ($gateList.Count -eq $script:GateChecks.Count -and $failedGates.Count -eq 0)
    $gateDetail = if ($gateList.Count -eq 0) { 'gates not run' } else { "ran: $(@($gateList | ForEach-Object { $_.name }) -join ', '); failed: $($failedGates -join ', ')" }
    $criteria.Add((New-Criterion $script:CriterionGates 'The gates pass in the workspace (build -warnaserror, rules, content rules, docs gate)' $gatesPassed $gateDetail))
    $visualDetail = if ($null -eq $Visual) { 'no visual result' } else { "worst $($Visual.worstRatio); languages $(@($Visual.languages) -join ','); widths $(@($Visual.widths) -join ','); $($Visual.detail)" }
    $criteria.Add((New-Criterion $script:CriterionVisual "visual-verifier at most $script:VisualMaxDiffRatio per viewport after masking, ar and en" ($null -ne $Visual -and $Visual.passed) $visualDetail))
    $reviewDetail = if ($null -eq $Review) { 'no spec' } else { "iterations: $($Review.iterations); findings: $($Review.findingCount)" }
    $criteria.Add((New-Criterion $script:CriterionReview 'Review findings and the iteration count are recorded' ($null -ne $Review -and $Review.passed) $reviewDetail))
    return $criteria.ToArray()
}

# The verdict (pure): blocked and invalid come first, then a dry run passes on its own criteria.
function Get-ProofVerdict {
    param(
        [Parameter(Mandatory = $true)][string] $Mode,
        [AllowEmptyCollection()][object[]] $Criteria = @(),
        [AllowEmptyCollection()][string[]] $Findings = @(),
        [AllowEmptyCollection()][string[]] $Blockers = @(),
        [string] $InfrastructureError
    )
    if (@($Findings | Where-Object { $_ }).Count -gt 0) { return $script:VerdictInvalid }
    if (@($Blockers | Where-Object { $_ }).Count -gt 0) { return $script:VerdictBlocked }
    $allPassed = (@($Criteria).Count -gt 0 -and @($Criteria | Where-Object { -not $_.passed }).Count -eq 0)
    if ($Mode -eq $script:ModeDryRun) { if ($allPassed) { return $script:VerdictDryRun } else { return $script:VerdictFail } }
    if ($InfrastructureError -or -not $allPassed) { return $script:VerdictFail }
    return $script:VerdictPass
}

function Get-ProofExitCode {
    param([Parameter(Mandatory = $true)][string] $Verdict)
    if ($script:ExitCodes.ContainsKey($Verdict)) { return $script:ExitCodes[$Verdict] }
    return $script:ExitCodes[$script:VerdictFail]
}

function Format-PipelineProofReport {
    param([Parameter(Mandatory = $true)][object] $Report)
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Pipeline proof: $($Report.proof) ($($Report.recordType))")
    $lines.Add('')
    $lines.Add("- Verdict: **$($Report.verdict.ToUpperInvariant())** ($($Report.mode))")
    $lines.Add("- Input type: $($Report.inputType)")
    $lines.Add("- Run: $($Report.runId); started $($Report.startedAt), finished $($Report.finishedAt)")
    if ($Report.infrastructureError) { $lines.Add("- Infrastructure error: $($Report.infrastructureError)") }
    foreach ($finding in @($Report.findings)) { $lines.Add("- Input finding: $finding") }
    foreach ($blocker in @($Report.blockers)) { $lines.Add("- Blocked: $blocker") }
    $lines.Add('')
    $lines.Add('## Criteria')
    $lines.Add('')
    $lines.Add('| Criterion | Result | Detail |')
    $lines.Add('| --- | --- | --- |')
    foreach ($criterion in @($Report.criteria)) {
        $result = if ($criterion.passed) { 'pass' } else { 'FAIL' }
        $lines.Add("| $($criterion.description) | $result | $(([string] $criterion.detail) -replace '\|', '/' -replace "`n", ' ') |")
    }
    $lines.Add('')
    $lines.Add('## Scenario prompt')
    $lines.Add('')
    $lines.Add('```text')
    $lines.Add([string] $Report.prompt)
    $lines.Add('```')
    if ($null -ne $Report.skillSequence) {
        $lines.Add('')
        $lines.Add('## Skill sequence')
        $lines.Add('')
        $lines.Add("- Expected: $(@($Report.skillSequence.expected) -join ' > ')")
        $lines.Add("- Observed: $(@($Report.skillSequence.observed) -join ' > ')")
    }
    if ($null -ne $Report.hookProof) {
        $lines.Add('')
        $lines.Add('## Hook log (refused, then corrected)')
        $lines.Add('')
        foreach ($pair in @($Report.hookProof.corrected)) { $lines.Add("- $($pair.path): [$(@($pair.ruleIds) -join ', ')] skill $($pair.skill); corrected with $($pair.correctedWith)") }
        foreach ($open in @($Report.hookProof.uncorrected)) { $lines.Add("- not corrected: $($open.path) [$(@($open.ruleIds) -join ', ')]") }
    }
    if ($null -ne $Report.visual -and $Report.visual.present) {
        $lines.Add('')
        $lines.Add('## Visual result (final iteration per viewport)')
        $lines.Add('')
        $lines.Add('| Route | Language | Width | Ratio | Iteration |')
        $lines.Add('| --- | --- | --- | --- | --- |')
        foreach ($viewport in @($Report.visual.viewports)) { $lines.Add("| $($viewport.route) | $($viewport.language) | $($viewport.width) | $($viewport.ratio) | $($viewport.iteration) |") }
    }
    if ($null -ne $Report.review -and $Report.review.present) {
        $lines.Add('')
        $lines.Add("## Review log (iterations: $($Report.review.iterations))")
        $lines.Add('')
        foreach ($finding in @($Report.review.findings)) { $lines.Add("- $(([string] $finding) -replace '^\|\s*', '')") }
    }
    $lines.Add('')
    return ($lines -join "`n")
}

function Write-PipelineProofReport {
    param([Parameter(Mandatory = $true)][object] $Report, [Parameter(Mandatory = $true)][string] $EvidenceDir)
    Write-ProofJsonFile -Path (Join-Path $EvidenceDir $script:ReportJsonFileName) -Value $Report
    Write-ProofTextFile -Path (Join-Path $EvidenceDir $script:ReportMarkdownFileName) -Text (Format-PipelineProofReport -Report $Report)
}

function New-ProofRunId {
    param([Parameter(Mandatory = $true)][string] $Proof, [Parameter(Mandatory = $true)][datetime] $Time)
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, $script:RunIdSuffixLength)
    return '{0}-{1}-{2}' -f $Proof, $Time.ToString($script:RunIdTimeFormat), $suffix
}

# ---------------------------------------------------------------------------------------------------------------
# Orchestration
# ---------------------------------------------------------------------------------------------------------------

# The variable that lets the feature-spec checker accept the pipeline-proof approver (pure).
function Get-PreApprovalEnvironment { return @{ $script:PreApprovalVariable = $script:PreApprovalEnabled } }

function Invoke-ProofHeadlessStage {
    param([object] $Definition, [object] $Workspace, [string] $Prompt, [string] $TranscriptPath, [string] $HookLogPath,
        [int] $MaxTurns, [double] $TimeoutMinutes, [string] $PermissionMode, [string] $Model, [string] $NodeDirectory, [string] $DotnetPath,
        [switch] $PreApproveSpec)
    $parameters = @{
        WorkRoot       = $Workspace.Path
        Prompt         = $Prompt
        TranscriptPath = $TranscriptPath
        MaxTurns       = $MaxTurns
        TimeoutMinutes = $TimeoutMinutes
        HookLogPath    = $HookLogPath
        PermissionMode = $PermissionMode
        NodeDirectory  = $NodeDirectory
        DotnetPath     = $DotnetPath
    }
    if ($Model) { $parameters['Model'] = $Model }
    if ($PreApproveSpec) { $parameters['ExtraEnvironment'] = Get-PreApprovalEnvironment }
    return Invoke-HeadlessClaude @parameters
}

function Invoke-PipelineProof {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $Proof,
        [Parameter(Mandatory = $true)][string] $EvidenceDir,
        [string] $WorkRoot,
        [string] $RepoRoot,
        [switch] $DryRun,
        [switch] $PreApproveSpec,
        [switch] $KeepWorkspace,
        [string] $FigmaUrl,
        [int] $MaxTurns,
        [double] $TimeoutMinutes,
        [string] $PermissionMode = 'acceptEdits',
        [string] $Model,
        [string] $NodeDirectory = $script:DefaultNodeDirectory,
        [string] $DotnetPath = $script:DefaultDotnetPath
    )
    if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $script:ToolFolder) }
    if (-not $WorkRoot) { $WorkRoot = Join-Path ([IO.Path]::GetTempPath()) $script:DefaultWorkRootName }
    $RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
    $WorkRoot = [IO.Path]::GetFullPath($WorkRoot)
    $EvidenceDir = [IO.Path]::GetFullPath($EvidenceDir)
    $started = Get-Date
    $runId = New-ProofRunId -Proof $Proof -Time $started
    $mode = if ($DryRun) { $script:ModeDryRun } else { $script:ModeRun }

    $definition = Get-ProofDefinition -Proof $Proof -InputsRoot (Join-Path $script:ToolFolder $script:InputsFolderName)
    $findings = New-Object System.Collections.Generic.List[string]
    foreach ($finding in @(Test-ProofDefinition -Definition $definition -RepoRoot $RepoRoot -FigmaUrl $FigmaUrl)) { $findings.Add($finding) }
    $workbook = Test-ProofWorkbook -Workbook $definition.Workbook -RepoRoot $RepoRoot
    foreach ($finding in @($workbook.findings)) { $findings.Add($finding) }
    $blockers = @(Get-ProofBlockers -Definition $definition -FigmaUrl $FigmaUrl)
    $prompt = Get-ProofPrompt -Definition $definition -PreApproveSpec:$PreApproveSpec -FigmaUrl $FigmaUrl -ApprovalDate $started.ToString($script:ApprovalDateFormat)
    if (-not $MaxTurns) { $MaxTurns = [int] $definition.MaxTurns }
    if (-not $TimeoutMinutes) { $TimeoutMinutes = [double] $definition.TimeoutMinutes }

    $hookLogPath = Join-Path $EvidenceDir $script:HookLogFileName
    $workspacePath = Join-Path $WorkRoot $runId
    $logFolder = Join-Path (Join-Path $WorkRoot $script:LogsFolderName) $runId
    $transcriptPath = Join-Path (Join-Path $WorkRoot $script:TranscriptsFolderName) "$runId.jsonl"

    $headless = $null; $sequence = $null; $spec = $null; $handEdits = $null; $hookProof = $null
    $probe = $null; $gates = @(); $visual = $null; $review = $null; $infrastructureError = $null; $specPaths = @()
    $criteria = @()

    if ($DryRun) {
        if ($findings.Count -eq 0 -and $blockers.Count -eq 0) {
            # The probe is read-only against the source tree: the hook only evaluates its input; its log goes to the evidence folder.
            $probe = Invoke-RefusalProbe -WorkRoot $RepoRoot -LogFolder (Join-Path $EvidenceDir $script:ProbeFolderName) -NodeDirectory $NodeDirectory
        }
        $criteria = @(
            (New-Criterion $script:CriterionInputs 'The proof inputs are valid (plain scenario, files, workbook)' ($findings.Count -eq 0) ($findings -join '; ')),
            (New-Criterion $script:CriterionProbe "A hand-made module file is refused, naming $script:ProbeSkill" ($null -ne $probe -and $probe.passed) $(if ($null -eq $probe) { 'not run' } else { $probe.reason }))
        )
    } elseif ($findings.Count -eq 0 -and $blockers.Count -eq 0) {
        if (Test-Path -LiteralPath $hookLogPath) { throw "$EvidenceDir already holds a proof run ($script:HookLogFileName); pass an empty evidence folder." }
        if (-not (Test-Path -LiteralPath $EvidenceDir)) { New-Item -ItemType Directory -Path $EvidenceDir -Force | Out-Null }
        try {
            $workspace = New-ThrowawayWorkspace -Source $RepoRoot -Destination $workspacePath
            $probe = Invoke-RefusalProbe -WorkRoot $workspace.Path -LogFolder (Join-Path $EvidenceDir $script:ProbeFolderName) -NodeDirectory $NodeDirectory
            $headless = Invoke-ProofHeadlessStage -Definition $definition -Workspace $workspace -Prompt $prompt -TranscriptPath $transcriptPath `
                -HookLogPath $hookLogPath -MaxTurns $MaxTurns -TimeoutMinutes $TimeoutMinutes -PermissionMode $PermissionMode -Model $Model `
                -NodeDirectory $NodeDirectory -DotnetPath $DotnetPath -PreApproveSpec:$PreApproveSpec
            if ($null -ne $headless.Summary) { Write-ProofJsonFile -Path (Join-Path $EvidenceDir $script:TranscriptSummaryFileName) -Value $headless.Summary }
            $sequence = Test-SkillSequence -Expected $definition.ExpectedSteps -Observed @(Read-ProofTranscriptSteps -TranscriptPath $transcriptPath)
            $changed = @(Get-WorkspaceChangedPaths -WorkRoot $workspace.Path -BaseRef $workspace.BaseRef)
            $specPaths = @(Select-FeatureSpecPaths -ChangedPaths $changed)
            if ($specPaths.Count -gt 0) {
                $spec = Invoke-ProofSpecCheck -WorkRoot $workspace.Path -SpecPath $specPaths[0] -LogFolder $logFolder -RequireApproved:$PreApproveSpec -NodeDirectory $NodeDirectory
                $specText = Read-ProofTextFile (Join-Path $workspace.Path $specPaths[0])
                Write-ProofTextFile -Path (Join-Path $EvidenceDir $script:SpecEvidenceFileName) -Text (ConvertTo-ProofEvidenceSpec -SpecPath $specPaths[0] -Text $specText)
                $handEdits = Test-HandEditLimit -Count $spec.handEditCount
                $review = Get-ReviewLogSummary -SpecText $specText
            }
            $hookLog = Read-HookLog -Path $hookLogPath
            $hookProof = Get-HookProof -Entries @($hookLog.Entries) -MalformedLines $hookLog.MalformedLines
            $gates = @(Invoke-KitEvalChecks -Checks $script:GateChecks -WorkRoot $workspace.Path -BaseRef $workspace.BaseRef -LogFolder $logFolder `
                    -DotnetPath $DotnetPath -NodeDirectory $NodeDirectory)
            $visualReports = @(Read-VisualReports -WorkRoot $workspace.Path -ChangedPaths $changed)
            $index = 0
            foreach ($entry in $visualReports) {
                $index++
                Write-ProofJsonFile -Path (Join-Path (Join-Path $EvidenceDir $script:VisualEvidenceFolderName) ("$index-$script:VisualReportFileName")) -Value $entry.Report
            }
            $visual = Get-VisualSummary -Reports $visualReports
        } catch {
            $infrastructureError = $_.Exception.Message
        } finally {
            if (-not $KeepWorkspace -and (Test-Path -LiteralPath $workspacePath) -and (Test-ThrowawayWorkspace -Path $workspacePath)) {
                try { Remove-ThrowawayWorkspace -Path $workspacePath } catch { Write-Warning $_.Exception.Message }
            }
        }
        $criteria = @(Get-RunCriteria -Headless $headless -Sequence $sequence -Spec $spec -HandEdits $handEdits -HookProof $hookProof `
                -Probe $probe -Gates $gates -Visual $visual -Review $review)
    }

    $verdict = Get-ProofVerdict -Mode $mode -Criteria $criteria -Findings @($findings) -Blockers $blockers -InfrastructureError $infrastructureError
    $finished = Get-Date
    $report = [ordered]@{
        proof               = $definition.Proof
        recordType          = $definition.RecordType
        inputType           = $definition.InputType
        mode                = $mode
        verdict             = $verdict
        exitCode            = Get-ProofExitCode -Verdict $verdict
        runId               = $runId
        startedAt           = $started.ToString($script:IsoTimeFormat)
        finishedAt          = $finished.ToString($script:IsoTimeFormat)
        durationSeconds     = [Math]::Round(($finished - $started).TotalSeconds, 1)
        preApproveSpec      = [bool] $PreApproveSpec
        prompt              = $prompt
        inputs              = $definition.Inputs
        workbook            = $workbook
        findings            = @($findings)
        blockers            = $blockers
        infrastructureError = $infrastructureError
        workspace           = [ordered]@{ path = $workspacePath; kept = [bool] $KeepWorkspace; transcript = $transcriptPath; logs = $logFolder }
        headless            = if ($null -eq $headless) { $null } else { [ordered]@{ exitCode = $headless.ExitCode; timedOut = $headless.TimedOut; durationSeconds = $headless.DurationSeconds; skillsInvoked = @($headless.SkillsInvoked) } }
        plan                = [ordered]@{ maxTurns = $MaxTurns; timeoutMinutes = $TimeoutMinutes; permissionMode = $PermissionMode; claudeArguments = @(Get-HeadlessClaudeArguments -MaxTurns ([Math]::Max(1, $MaxTurns)) -PermissionMode $PermissionMode -Model $Model) }
        skillSequence       = $sequence
        specPaths           = $specPaths
        spec                = $spec
        handEdits           = $handEdits
        hookProof           = $hookProof
        refusalProbe        = $probe
        gates               = $gates
        visual              = $visual
        review              = $review
        criteria            = $criteria
    }
    $reportObject = [pscustomobject] $report
    Write-PipelineProofReport -Report $reportObject -EvidenceDir $EvidenceDir
    return $reportObject
}

Export-ModuleMember -Function Get-PipelineProofConstants, ConvertTo-ProofEvidenceSpec, Get-ProofDefinition, Test-PlainScenario, Test-ProofDefinition, Get-ProofBlockers,
    Get-WorkbookUseCaseIds, Test-ProofWorkbook, Get-ProofPrompt, Get-ProofStepSequence, Read-ProofTranscriptSteps, Test-SkillSequence,
    ConvertFrom-HookLogLines, Read-HookLog, Get-HookEntryPaths, Test-HookPathOverlap, Get-HookProof, Get-RefusalProbeInput, Test-RefusalProbeOutput, Invoke-RefusalProbe,
    ConvertFrom-SpecCheckOutput, Invoke-ProofSpecCheck, Test-HandEditLimit, Get-ReviewLogSummary, Get-VisualSummary, Read-VisualReports,
    Get-WorkspaceChangedPaths, Get-RunCriteria, Get-ProofVerdict, Get-ProofExitCode, Format-PipelineProofReport, Write-PipelineProofReport,
    New-ProofRunId, Invoke-PipelineProof
