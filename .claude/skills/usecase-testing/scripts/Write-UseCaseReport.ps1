<#
.SYNOPSIS
  Turns a use-case test run (results.json) into an .xlsx workbook or a Markdown bug report.

.DESCRIPTION
  The results file is described in .claude/skills/usecase-testing/REFERENCE.md ("results.json"). The script validates
  it, derives every count itself (never hand-computed), and writes:
    -Format Xlsx      worksheets Summary, Test Cases, Issues, Evidence, Requirements, Open Questions (inline strings,
                      bold header row, written with System.IO.Compression: no Excel, no Python, no module);
    -Format Markdown  a findings report shaped for the prd-and-plan skill (issues, open spec questions, passed and
                      blocked scenarios, rule coverage, fix order, test-code changes).
  Invalid input is a terminating error that names every problem (exit code 1 under -File). Fix results.json and
  run it again; never hand-write the report.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .claude/skills/usecase-testing/scripts/Write-UseCaseReport.ps1 -ResultsPath test-reports/UC-001/results.json -Format Xlsx
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ResultsPath,
    [ValidateSet('Xlsx', 'Markdown')] [string] $Format = 'Xlsx',
    # Default: next to results.json, named <use-case id>-test-report.xlsx or <use-case id>-test-findings.md.
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$FormatXlsx         = 'Xlsx'
$StatusPass         = 'Pass'
$StatusFail         = 'Fail'
$StatusBlocked      = 'Blocked'
$StatusSkipped      = 'Skipped'
$Statuses           = @($StatusPass, $StatusFail, $StatusBlocked, $StatusSkipped)
$Severities         = @('Critical', 'High', 'Medium', 'Low')
$ConfidenceConfirmed = 'Confirmed'
$Confidences        = @($ConfidenceConfirmed, 'Probable')
$TestableYes        = 'Yes'
$VerdictFail        = 'FAIL'
$VerdictIncomplete  = 'INCOMPLETE'
$VerdictPass        = 'PASS'
$XlsxSuffix         = '-test-report.xlsx'
$MarkdownSuffix     = '-test-findings.md'
$ListSeparator      = ', '
$HeaderStyleIndex   = 1
$ColumnBase         = 26
$LetterA            = 65
$MaxSheetNameLength = 31
$EmptyMark          = '-'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ---------------------------------------------------------------- input --
function Get-Field($Object, [string] $Name, $Default = '') {
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}

function Get-List($Object, [string] $Name) { return @(Get-Field $Object $Name @()) }

function Join-List($Object, [string] $Name) { return ((Get-List $Object $Name) -join $ListSeparator) }

function Test-Results($Results) {
    $problems = New-Object System.Collections.Generic.List[string]
    $useCase = Get-Field $Results 'useCase' $null
    if (-not (Get-Field $useCase 'id')) { $problems.Add('useCase.id is required.') }
    if (-not (Get-Field $useCase 'workbook')) { $problems.Add('useCase.workbook is required (the report must name the workbook it tested against).') }

    $testIds = @{}
    foreach ($test in (Get-List $Results 'testCases')) {
        $id = Get-Field $test 'id'
        if (-not $id) { $problems.Add('Every test case needs an id.'); continue }
        if ($testIds.ContainsKey($id)) { $problems.Add("Duplicate test case id '$id'.") }
        $testIds[$id] = $true
        $status = Get-Field $test 'status'
        if ($Statuses -notcontains $status) { $problems.Add("Test case '$id': status '$status' is not one of $($Statuses -join '/').") }
        if (-not (Get-Field $test 'expected')) { $problems.Add("Test case '$id': expected is required (copy it from the workbook before running).") }
    }

    $issueIds = @{}
    foreach ($issue in (Get-List $Results 'issues')) {
        $id = Get-Field $issue 'id'
        if (-not $id) { $problems.Add('Every issue needs an id.'); continue }
        if ($issueIds.ContainsKey($id)) { $problems.Add("Duplicate issue id '$id'.") }
        $issueIds[$id] = $true
        $severity = Get-Field $issue 'severity'
        if ($Severities -notcontains $severity) { $problems.Add("Issue '$id': severity '$severity' is not one of $($Severities -join '/').") }
        $confidence = Get-Field $issue 'confidence'
        if ($Confidences -notcontains $confidence) { $problems.Add("Issue '$id': confidence '$confidence' is not one of $($Confidences -join '/').") }
        if ($confidence -eq $ConfidenceConfirmed -and -not (Get-Field $issue 'location')) { $problems.Add("Issue '$id': a Confirmed root cause needs location (file:line).") }
        if ($confidence -ne $ConfidenceConfirmed -and -not (Get-Field $issue 'confirmWith')) { $problems.Add("Issue '$id': a Probable root cause needs confirmWith (the evidence that would confirm it).") }
        foreach ($testId in (Get-List $issue 'testCaseIds')) {
            if (-not $testIds.ContainsKey($testId)) { $problems.Add("Issue '$id' references unknown test case '$testId'.") }
        }
    }

    foreach ($rule in (Get-List $Results 'requirements')) {
        $id = Get-Field $rule 'id'
        if (-not $id) { $problems.Add('Every requirement needs an id.'); continue }
        $covered = @(Get-List $rule 'testCaseIds').Count -gt 0
        if ((Get-Field $rule 'testable') -eq $TestableYes -and -not $covered -and (Get-Field $rule 'result') -ne $StatusBlocked) {
            $problems.Add("Requirement '$id' is testable but maps to no test case; map one or mark it Blocked with a reason.")
        }
        foreach ($testId in (Get-List $rule 'testCaseIds')) {
            if (-not $testIds.ContainsKey($testId)) { $problems.Add("Requirement '$id' references unknown test case '$testId'.") }
        }
    }
    return ,$problems
}

function Get-Summary($Results) {
    $tests = @(Get-List $Results 'testCases')
    $issues = @(Get-List $Results 'issues')
    $counts = [ordered]@{}
    foreach ($status in $Statuses) { $counts[$status] = @($tests | Where-Object { (Get-Field $_ 'status') -eq $status }).Count }
    $bySeverity = [ordered]@{}
    foreach ($severity in $Severities) { $bySeverity[$severity] = @($issues | Where-Object { (Get-Field $_ 'severity') -eq $severity }).Count }
    $verdict = if ($counts[$StatusFail] -gt 0 -or $issues.Count -gt 0) { $VerdictFail }
               elseif ($counts[$StatusBlocked] -gt 0 -or $tests.Count -eq 0) { $VerdictIncomplete }
               else { $VerdictPass }
    return [pscustomobject]@{
        Total = $tests.Count; Counts = $counts; BySeverity = $bySeverity; IssueCount = $issues.Count
        OpenQuestions = @(Get-List $Results 'openQuestions').Count; Verdict = $verdict
    }
}

# ----------------------------------------------------------------- xlsx --
function ConvertTo-ColumnName([int] $Number) {
    $name = ''
    while ($Number -gt 0) {
        $remainder = ($Number - 1) % $ColumnBase
        $name = [string][char]($LetterA + $remainder) + $name
        $Number = [int][math]::Floor(($Number - 1) / $ColumnBase)
    }
    return $name
}

function ConvertTo-XmlText([string] $Value) {
    $clean = $Value -replace '[\x00-\x08\x0B\x0C\x0E-\x1F]', ''
    return [System.Security.SecurityElement]::Escape($clean)
}

function New-SheetXml([object[]] $Rows) {
    $builder = New-Object System.Text.StringBuilder
    [void] $builder.Append('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
    [void] $builder.Append('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>')
    $rowNumber = 0
    foreach ($row in $Rows) {
        $rowNumber++
        [void] $builder.Append("<row r=`"$rowNumber`">")
        $column = 0
        foreach ($value in @($row)) {
            $column++
            $style = if ($rowNumber -eq 1) { " s=`"$HeaderStyleIndex`"" } else { '' }
            $reference = (ConvertTo-ColumnName $column) + $rowNumber
            [void] $builder.Append("<c r=`"$reference`" t=`"inlineStr`"$style><is><t xml:space=`"preserve`">$(ConvertTo-XmlText ([string] $value))</t></is></c>")
        }
        [void] $builder.Append('</row>')
    }
    [void] $builder.Append('</sheetData></worksheet>')
    return $builder.ToString()
}

function Write-ZipPart([System.IO.Compression.ZipArchive] $Zip, [string] $Name, [string] $Text) {
    $entry = $Zip.CreateEntry($Name)
    $writer = New-Object System.IO.StreamWriter($entry.Open(), (New-Object System.Text.UTF8Encoding($false)))
    try { $writer.Write($Text) } finally { $writer.Dispose() }
}

function Write-Xlsx([string] $Target, [object[]] $Sheets) {
    $contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
    $sheetEntries = ''
    $relEntries = ''
    $index = 0
    foreach ($sheet in $Sheets) {
        $index++
        $contentTypes += "<Override PartName=`"/xl/worksheets/sheet$index.xml`" ContentType=`"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml`"/>"
        $name = $sheet.Name.Substring(0, [math]::Min($sheet.Name.Length, $MaxSheetNameLength))
        $sheetEntries += "<sheet name=`"$(ConvertTo-XmlText $name)`" sheetId=`"$index`" r:id=`"rId$index`"/>"
        $relEntries += "<Relationship Id=`"rId$index`" Type=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet`" Target=`"worksheets/sheet$index.xml`"/>"
    }
    $stylesRelId = $index + 1
    $relEntries += "<Relationship Id=`"rId$stylesRelId`" Type=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles`" Target=`"styles.xml`"/>"
    $contentTypes += '</Types>'
    $rootRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'
    $workbook = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><workbook xmlns=`"http://schemas.openxmlformats.org/spreadsheetml/2006/main`" xmlns:r=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships`"><sheets>$sheetEntries</sheets></workbook>"
    $workbookRels = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><Relationships xmlns=`"http://schemas.openxmlformats.org/package/2006/relationships`">$relEntries</Relationships>"
    $styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs></styleSheet>'

    if (Test-Path -LiteralPath $Target) { Remove-Item -LiteralPath $Target -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($Target, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Write-ZipPart $zip '[Content_Types].xml' $contentTypes
        Write-ZipPart $zip '_rels/.rels' $rootRels
        Write-ZipPart $zip 'xl/workbook.xml' $workbook
        Write-ZipPart $zip 'xl/_rels/workbook.xml.rels' $workbookRels
        Write-ZipPart $zip 'xl/styles.xml' $styles
        $index = 0
        foreach ($sheet in $Sheets) { $index++; Write-ZipPart $zip "xl/worksheets/sheet$index.xml" (New-SheetXml $sheet.Rows) }
    }
    finally { $zip.Dispose() }
}

function New-Sheet([string] $Name, [string[]] $Header, [object[]] $Items, [scriptblock] $Project) {
    $rows = New-Object System.Collections.Generic.List[object]
    $rows.Add($Header)
    foreach ($item in $Items) { $rows.Add([string[]] @(& $Project $item)) }
    return [pscustomobject]@{ Name = $Name; Rows = $rows.ToArray() }
}

function Get-XlsxSheets($Results, $Summary) {
    $useCase = Get-Field $Results 'useCase' $null
    $run = Get-Field $Results 'run' $null
    $summaryRows = @(
        @('Field', 'Value'),
        @('Use case', (Get-Field $useCase 'id')), @('Name', (Get-Field $useCase 'name')),
        @('Workbook', (Get-Field $useCase 'workbook')), @('Workbook version', (Get-Field $useCase 'workbookVersion')),
        @('Tested on', (Get-Field $run 'testedOn')), @('Branch', (Get-Field $run 'branch')), @('Commit', (Get-Field $run 'commit')),
        @('Verdict', $Summary.Verdict), @('Test cases', [string] $Summary.Total))
    foreach ($status in $Statuses) { $summaryRows += , @($status, [string] $Summary.Counts[$status]) }
    $summaryRows += , @('Issues', [string] $Summary.IssueCount)
    foreach ($severity in $Severities) { $summaryRows += , @("Issues: $severity", [string] $Summary.BySeverity[$severity]) }
    $summaryRows += , @('Open spec questions', [string] $Summary.OpenQuestions)

    return @(
        [pscustomobject]@{ Name = 'Summary'; Rows = $summaryRows },
        (New-Sheet 'Test Cases' @('ID', 'Title', 'Dimension', 'Workbook ref', 'Expected', 'Actual', 'Status', 'Evidence') (Get-List $Results 'testCases') {
            param($t) (Get-Field $t 'id'), (Get-Field $t 'title'), (Get-Field $t 'dimension'), (Get-Field $t 'workbookRef'), (Get-Field $t 'expected'), (Get-Field $t 'actual'), (Get-Field $t 'status'), (Join-List $t 'evidenceIds') }),
        (New-Sheet 'Issues' @('ID', 'Title', 'Severity', 'Confidence', 'Test cases', 'Rules', 'Expected', 'Actual', 'Root cause', 'Location', 'Call chain', 'Confirm with', 'Recommended fix') (Get-List $Results 'issues') {
            param($i) (Get-Field $i 'id'), (Get-Field $i 'title'), (Get-Field $i 'severity'), (Get-Field $i 'confidence'), (Join-List $i 'testCaseIds'), (Join-List $i 'ruleIds'), (Get-Field $i 'expected'), (Get-Field $i 'actual'), (Get-Field $i 'rootCause'), (Get-Field $i 'location'), (Get-Field $i 'callChain'), (Get-Field $i 'confirmWith'), (Get-Field $i 'recommendedFix') }),
        (New-Sheet 'Evidence' @('ID', 'Type', 'Description', 'Command or request', 'Exit or status', 'Location') (Get-List $Results 'evidence') {
            param($e) (Get-Field $e 'id'), (Get-Field $e 'type'), (Get-Field $e 'description'), (Get-Field $e 'command'), [string] (Get-Field $e 'exitCode'), (Get-Field $e 'location') }),
        (New-Sheet 'Requirements' @('ID', 'Rule', 'Testable', 'Test cases', 'Result', 'Note') (Get-List $Results 'requirements') {
            param($r) (Get-Field $r 'id'), (Get-Field $r 'rule'), (Get-Field $r 'testable'), (Join-List $r 'testCaseIds'), (Get-Field $r 'result'), (Get-Field $r 'note') }),
        (New-Sheet 'Open Questions' @('ID', 'Gap', 'Scenarios', 'Spec says', 'App does today', 'Ruling needed from') (Get-List $Results 'openQuestions') {
            param($q) (Get-Field $q 'id'), (Get-Field $q 'gap'), (Join-List $q 'scenarioIds'), (Get-Field $q 'specSays'), (Get-Field $q 'appDoes'), (Get-Field $q 'rulingFrom') })
    )
}

# ------------------------------------------------------------- markdown --
function ConvertTo-Cell([string] $Value) {
    if (-not $Value) { return $EmptyMark }
    return ($Value -replace "`r?`n", ' ' -replace '\|', '\|')
}

function New-MarkdownTable([string[]] $Header, [object[]] $Rows) {
    $lines = @('| ' + ($Header -join ' | ') + ' |', '|' + ((@($Header | ForEach-Object { ' --- ' })) -join '|') + '|')
    foreach ($row in $Rows) { $lines += '| ' + ((@($row | ForEach-Object { ConvertTo-Cell ([string] $_) })) -join ' | ') + ' |' }
    return $lines
}

function Get-MarkdownLines($Results, $Summary) {
    $useCase = Get-Field $Results 'useCase' $null
    $run = Get-Field $Results 'run' $null
    $tests = @(Get-List $Results 'testCases')
    $issues = @(Get-List $Results 'issues' | Sort-Object @{ Expression = { [array]::IndexOf($Severities, (Get-Field $_ 'severity')) } }, @{ Expression = { Get-Field $_ 'id' } })
    $lines = @(
        "# $(Get-Field $useCase 'id') test findings: $(Get-Field $useCase 'name')", '',
        "> Workbook: ``$(Get-Field $useCase 'workbook')`` (version $(ConvertTo-Cell (Get-Field $useCase 'workbookVersion')))",
        "> Tested: $(ConvertTo-Cell (Get-Field $run 'testedOn')) | Branch: $(ConvertTo-Cell (Get-Field $run 'branch')) | Commit: $(ConvertTo-Cell (Get-Field $run 'commit'))",
        '> Status: ready-for-prd (produced by the usecase-testing skill)', '',
        '## 1. Executive summary', '', (Get-Field $Results 'executiveSummary' $EmptyMark), '')
    $metricRows = @(, @('Verdict', $Summary.Verdict)) + @(, @('Test cases', $Summary.Total))
    foreach ($status in $Statuses) { $metricRows += , @($status, $Summary.Counts[$status]) }
    $metricRows += , @('Open spec questions (not defects)', $Summary.OpenQuestions)
    $lines += New-MarkdownTable @('Metric', 'Count') $metricRows
    $lines += '', ('Defects by severity: ' + ((@($Severities | ForEach-Object { "**$_** $($Summary.BySeverity[$_])" })) -join ' | ')), ''

    $lines += '## 2. Scope and environment', ''
    foreach ($item in (Get-List $run 'environment')) { $lines += "- $item" }
    $lines += '', '## 3. Issues found', ''
    if ($issues.Count -eq 0) { $lines += 'None observed.', '' }
    foreach ($issue in $issues) {
        $lines += "### $(Get-Field $issue 'id'): $(Get-Field $issue 'title')", ''
        $lines += New-MarkdownTable @('Field', 'Value') @(
            @('Severity', (Get-Field $issue 'severity')), @('Confidence', (Get-Field $issue 'confidence')),
            @('Test cases', (Join-List $issue 'testCaseIds')), @('Rules', (Join-List $issue 'ruleIds')),
            @('Affected permissions', (Join-List $issue 'permissions')), @('Layer', (Get-Field $issue 'layer')))
        $lines += '', "**Problem (user's words):** $(ConvertTo-Cell (Get-Field $issue 'problem'))", ''
        $lines += "**Expected (per workbook):** $(ConvertTo-Cell (Get-Field $issue 'expected'))", ''
        $lines += "**Actual:** $(ConvertTo-Cell (Get-Field $issue 'actual'))", '', '**Reproduction:**', ''
        $step = 0
        foreach ($s in (Get-List $issue 'reproSteps')) { $step++; $lines += "$step. $s" }
        $lines += '', "**Root cause:** $(ConvertTo-Cell (Get-Field $issue 'rootCause'))", ''
        $lines += "Location: ``$(Get-Field $issue 'location' $EmptyMark)``. Call chain: $(ConvertTo-Cell (Get-Field $issue 'callChain'))", ''
        if ((Get-Field $issue 'confidence') -ne $ConfidenceConfirmed) { $lines += "**To confirm:** $(Get-Field $issue 'confirmWith')", '' }
        $lines += "**Proposed fix:** $(ConvertTo-Cell (Get-Field $issue 'recommendedFix'))", '', '**Acceptance criteria:**', ''
        foreach ($c in (Get-List $issue 'acceptanceCriteria')) { $lines += "- [ ] $c" }
        $lines += '', "**Blast radius:** $(ConvertTo-Cell (Get-Field $issue 'blastRadius'))", '', '---', ''
    }

    $lines += '## 4. Open spec questions (not defects)', ''
    $lines += New-MarkdownTable @('ID', 'Gap', 'Scenarios', 'Spec says', 'App does today', 'Ruling needed from') @(foreach ($q in (Get-List $Results 'openQuestions')) {
        , @((Get-Field $q 'id'), (Get-Field $q 'gap'), (Join-List $q 'scenarioIds'), (Get-Field $q 'specSays'), (Get-Field $q 'appDoes'), (Get-Field $q 'rulingFrom')) })
    $lines += '', '## 5. Passed scenarios', ''
    $lines += New-MarkdownTable @('Test case', 'Title', 'Verified by') @(foreach ($t in @($tests | Where-Object { (Get-Field $_ 'status') -eq $StatusPass })) {
        , @((Get-Field $t 'id'), (Get-Field $t 'title'), (Get-Field $t 'verifiedBy')) })
    $lines += '', '## 6. Blocked or not executed', ''
    $lines += New-MarkdownTable @('Test case', 'Why', 'What would unblock it') @(foreach ($t in @($tests | Where-Object { @($StatusBlocked, $StatusSkipped) -contains (Get-Field $_ 'status') })) {
        , @((Get-Field $t 'id'), (Get-Field $t 'actual'), (Get-Field $t 'unblock')) })
    $lines += '', '## 7. Business rule coverage', ''
    $lines += New-MarkdownTable @('Rule', 'Testable', 'Test cases', 'Result') @(foreach ($r in (Get-List $Results 'requirements')) {
        , @((Get-Field $r 'id'), (Get-Field $r 'testable'), (Join-List $r 'testCaseIds'), (Get-Field $r 'result')) })
    $lines += '', '## 8. Suggested fix order', ''
    $rank = 0
    foreach ($f in (Get-List $Results 'fixOrder')) { $rank++; $lines += "$rank. $f" }
    if ($rank -eq 0) { $lines += 'Nothing to fix.' }
    $lines += '', '## 9. Test-code changes made during this run', ''
    $changes = @(Get-List $Results 'testCodeChanges')
    if ($changes.Count -eq 0) { $lines += 'None.' } else { foreach ($c in $changes) { $lines += "- $c" } }
    return $lines
}

# ----------------------------------------------------------------- main --
if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) { throw "Results file not found: $ResultsPath" }
$resultsFull = (Resolve-Path -LiteralPath $ResultsPath).ProviderPath
try { $results = [System.IO.File]::ReadAllText($resultsFull, [System.Text.Encoding]::UTF8) | ConvertFrom-Json }
catch { throw "Results file is not valid JSON: $ResultsPath. $($_.Exception.Message)" }

$problems = Test-Results $results
if ($problems.Count -gt 0) { throw ("results.json is invalid:`n- " + ($problems -join "`n- ")) }

$summary = Get-Summary $results
$useCaseId = Get-Field (Get-Field $results 'useCase' $null) 'id'
if (-not $OutputPath) {
    $suffix = if ($Format -eq $FormatXlsx) { $XlsxSuffix } else { $MarkdownSuffix }
    $OutputPath = Join-Path (Split-Path -Parent $resultsFull) ($useCaseId + $suffix)
}
$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$folder = Split-Path -Parent $target
if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }

if ($Format -eq $FormatXlsx) { Write-Xlsx -Target $target -Sheets (Get-XlsxSheets $results $summary) }
else { [System.IO.File]::WriteAllText($target, ((Get-MarkdownLines $results $summary) -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false))) }

[pscustomobject]@{ Path = $target; Verdict = $summary.Verdict; Total = $summary.Total; Failed = $summary.Counts[$StatusFail]; Blocked = $summary.Counts[$StatusBlocked]; Issues = $summary.IssueCount }
