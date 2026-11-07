# Pester 3.4 tests for .claude/skills/usecase-testing/scripts/Write-UseCaseReport.ps1 (Should Be / Should Throw syntax).
# Run: powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester .claude/skills/usecase-testing/scripts/tests -EnableExit"
# Every results.json is built at test time in a folder under %TEMP% and deleted afterwards.
Set-StrictMode -Version Latest

$ScriptsRoot = Split-Path -Parent $PSScriptRoot
$WriterPath  = Join-Path $ScriptsRoot 'Write-UseCaseReport.ps1'
$ReaderPath  = Join-Path $ScriptsRoot 'Read-Xlsx.ps1'
. (Join-Path $PSScriptRoot 'XlsxFixture.ps1')
$UseCaseId   = 'UC-001'
$ExpectedSheets = 'Summary;Test Cases;Issues;Evidence;Requirements;Open Questions'

function New-Results {
    return [ordered]@{
        useCase = [ordered]@{ id = $UseCaseId; name = "Submit permit $FixtureArabicWord"; workbook = 'inputs/UC-001_HighLevelScenarios.xlsx'; workbookVersion = '1.0' }
        run = [ordered]@{ testedOn = '2026-01-02'; branch = 'feature/x'; commit = 'abc1234'; environment = @('xUnit: Starter.Integration.Tests', 'run-local.ps1 -Slot 1') }
        executiveSummary = 'Submit works; the empty-title check is missing.'
        testCases = @(
            [ordered]@{ id = 'SCN_SMK_001'; title = 'Submit a permit'; dimension = 'Happy path'; workbookRef = 'Smoke r2'; expected = 'Status becomes Submitted'; actual = 'Submitted'; status = 'Pass'; evidenceIds = @('EV-1'); verifiedBy = 'Starter.Integration.Tests' },
            [ordered]@{ id = 'SCN_DAT_002'; title = 'Empty title <refused> & logged'; dimension = 'Validation'; workbookRef = 'Data r3'; expected = '400 with a title error'; actual = '201 Created'; status = 'Fail'; evidenceIds = @('EV-2') },
            [ordered]@{ id = 'TC-UC-001-003'; title = 'Escalation after the deadline'; dimension = 'Workflow'; workbookRef = ''; expected = 'Escalates'; actual = 'Job host not running'; status = 'Blocked'; unblock = 'Start the jobs host' }
        )
        issues = @(
            [ordered]@{ id = 'ISSUE-UC-001-01'; title = 'Accept an empty title'; severity = 'High'; confidence = 'Confirmed'; testCaseIds = @('SCN_DAT_002'); ruleIds = @('BR-02'); expected = '400'; actual = '201'; rootCause = 'The validator has no rule for Title.'; location = 'backend/src/Modules/Permits/Application/SubmitPermitValidator.cs:12'; callChain = 'PermitsController.Submit -> SubmitPermit'; recommendedFix = 'Add a NotEmpty rule.'; reproSteps = @('Sign in', 'Submit with an empty title'); acceptanceCriteria = @('Empty title returns 400') }
        )
        evidence = @([ordered]@{ id = 'EV-1'; type = 'Command'; description = 'dotnet test'; command = 'dotnet test --filter Permit'; exitCode = 0; location = '' })
        requirements = @(
            [ordered]@{ id = 'BR-02'; rule = 'Title is mandatory'; testable = 'Yes'; testCaseIds = @('SCN_DAT_002'); result = 'Fail' },
            [ordered]@{ id = 'BR-05'; rule = 'Escalate after 5 days'; testable = 'Yes'; testCaseIds = @(); result = 'Blocked'; note = 'Jobs host' }
        )
        openQuestions = @([ordered]@{ id = 'G-01'; gap = 'Undefined reopen rule'; scenarioIds = @('SCN_FUN_004'); specSays = 'Nothing'; appDoes = 'Refuses'; rulingFrom = 'Business analyst' })
        fixOrder = @('ISSUE-UC-001-01: one validator rule.')
        testCodeChanges = @()
    }
}

function Save-Results([string] $Folder, $Results) {
    $path = Join-Path $Folder 'results.json'
    [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $Results -Depth 8), (New-Object Text.UTF8Encoding($false)))
    return $path
}

# Windows PowerShell 5.1 emits a parsed JSON array as one object; returning it from a function unrolls it.
function Read-Report([string] $Path) {
    $parsed = ConvertFrom-Json -InputObject ((& $ReaderPath -Path $Path -AsJson) -join "`n")
    return $parsed
}

function Get-SummaryValue($Sheets, [string] $Field) {
    $row = @($Sheets[0].rows | Where-Object { $_.cells[0] -eq $Field })
    return $row[0].cells[1]
}

Describe 'Write-UseCaseReport.ps1' {
    $folder = New-FixtureFolder

    Context 'xlsx' {
        $resultsPath = Save-Results $folder (New-Results)
        $outcome = & $WriterPath -ResultsPath $resultsPath
        $sheets = @(Read-Report $outcome.Path)

        It 'writes <id>-test-report.xlsx next to results.json' {
            $outcome.Path | Should Be (Join-Path $folder "$UseCaseId-test-report.xlsx")
            Test-Path -LiteralPath $outcome.Path | Should Be $true
        }

        It 'has the six worksheets in order' {
            ((@($sheets | ForEach-Object { $_.name })) -join ';') | Should Be $ExpectedSheets
        }

        It 'derives the summary counts and the verdict from the rows' {
            $outcome.Verdict | Should Be 'FAIL'
            Get-SummaryValue $sheets 'Test cases' | Should Be '3'
            Get-SummaryValue $sheets 'Pass' | Should Be '1'
            Get-SummaryValue $sheets 'Fail' | Should Be '1'
            Get-SummaryValue $sheets 'Blocked' | Should Be '1'
            Get-SummaryValue $sheets 'Issues: High' | Should Be '1'
            Get-SummaryValue $sheets 'Open spec questions' | Should Be '1'
        }

        It 'round-trips Arabic text and XML markup characters' {
            Get-SummaryValue $sheets 'Name' | Should Be "Submit permit $FixtureArabicWord"
            $sheets[1].rows[2].cells[1] | Should Be 'Empty title <refused> & logged'
        }

        It 'writes the issue location and joins list fields' {
            $issue = $sheets[2].rows[1].cells
            $issue[9] | Should Be 'backend/src/Modules/Permits/Application/SubmitPermitValidator.cs:12'
            $sheets[4].rows[1].cells[3] | Should Be 'SCN_DAT_002'
        }
    }

    Context 'markdown' {
        $resultsPath = Save-Results $folder (New-Results)
        $outcome = & $WriterPath -ResultsPath $resultsPath -Format Markdown
        $text = [IO.File]::ReadAllText($outcome.Path, [Text.Encoding]::UTF8)

        It 'writes <id>-test-findings.md with every section' {
            $outcome.Path | Should Be (Join-Path $folder "$UseCaseId-test-findings.md")
            foreach ($section in @('## 1. Executive summary', '## 3. Issues found', '## 4. Open spec questions (not defects)', '## 7. Business rule coverage', '## 9. Test-code changes made during this run')) {
                $text.Contains($section) | Should Be $true
            }
        }

        It 'renders the issue with its location, steps and acceptance criteria' {
            $text.Contains('### ISSUE-UC-001-01: Accept an empty title') | Should Be $true
            $text.Contains('SubmitPermitValidator.cs:12') | Should Be $true
            $text.Contains('2. Submit with an empty title') | Should Be $true
            $text.Contains('- [ ] Empty title returns 400') | Should Be $true
        }

        It 'keeps open questions out of the issues and lists the blocked test with its unblock step' {
            $text.Contains('| G-01 | Undefined reopen rule |') | Should Be $true
            $text.Contains('| TC-UC-001-003 | Job host not running | Start the jobs host |') | Should Be $true
        }
    }

    Context 'verdicts' {
        It 'is PASS when every test passes and no issue is open' {
            $results = New-Results
            $results.testCases = @($results.testCases[0]); $results.issues = @(); $results.requirements = @()
            (& $WriterPath -ResultsPath (Save-Results $folder $results) -OutputPath (Join-Path $folder 'pass.xlsx')).Verdict | Should Be 'PASS'
        }

        It 'is INCOMPLETE when nothing failed but a test is blocked' {
            $results = New-Results
            $results.testCases = @($results.testCases[0], $results.testCases[2]); $results.issues = @(); $results.requirements = @()
            (& $WriterPath -ResultsPath (Save-Results $folder $results) -OutputPath (Join-Path $folder 'blocked.xlsx')).Verdict | Should Be 'INCOMPLETE'
        }
    }

    Context 'validation' {
        It 'refuses an unknown status' {
            $results = New-Results
            $results.testCases[0].status = 'Passed'
            { & $WriterPath -ResultsPath (Save-Results $folder $results) } | Should Throw "status 'Passed' is not one of"
        }

        It 'refuses a Confirmed issue without a location' {
            $results = New-Results
            $results.issues[0].location = ''
            { & $WriterPath -ResultsPath (Save-Results $folder $results) } | Should Throw 'needs location (file:line)'
        }

        It 'refuses a Probable issue without the evidence that would confirm it' {
            $results = New-Results
            $results.issues[0].confidence = 'Probable'
            { & $WriterPath -ResultsPath (Save-Results $folder $results) } | Should Throw 'needs confirmWith'
        }

        It 'refuses a testable rule that maps to no test and is not Blocked' {
            $results = New-Results
            $results.requirements[1].result = 'Pass'
            { & $WriterPath -ResultsPath (Save-Results $folder $results) } | Should Throw "Requirement 'BR-05' is testable"
        }

        It 'refuses a report that does not name its workbook' {
            $results = New-Results
            $results.useCase.workbook = ''
            { & $WriterPath -ResultsPath (Save-Results $folder $results) } | Should Throw 'useCase.workbook is required'
        }

        It 'refuses a missing results file' {
            { & $WriterPath -ResultsPath (Join-Path $folder 'none.json') } | Should Throw 'Results file not found'
        }
    }

    Remove-FixtureFolder $folder
}
