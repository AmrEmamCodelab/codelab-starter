# Pester 3.4 tests for tools/pipeline-proofs/lib/OoxmlWorkbook.psm1 and build-permit-workbook.ps1 (Windows
# PowerShell 5.1): the writer's pure helpers, a workbook read back by the usecase-testing reader, and the committed
# permit workbook (2 use cases, byte-identical to a fresh build of its JSON source).
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/pipeline-proofs/tests -EnableExit"
Set-StrictMode -Version Latest

$ToolFolder = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ToolFolder)
$ReadXlsx = Join-Path $RepoRoot '.claude\skills\usecase-testing\scripts\Read-Xlsx.ps1'
$BuildScript = Join-Path $ToolFolder 'build-permit-workbook.ps1'
$PermitFolder = Join-Path $ToolFolder 'inputs\permit'
$PermitData = Join-Path $PermitFolder 'permit-use-cases.json'
$PermitWorkbook = Join-Path $PermitFolder 'permit-use-cases.xlsx'
$ExpectedSheets = @('Summary', 'Functional', 'Data', 'BusinessRules', 'StateTransitions', 'Gaps_Messages')
Import-Module (Join-Path $ToolFolder 'lib\OoxmlWorkbook.psm1') -Force
Import-Module (Join-Path $ToolFolder 'lib\PipelineProof.psm1') -Force

function Read-WorkbookJson([string] $Path, [string] $Sheet) {
    $arguments = @{ Path = $Path; AsJson = $true }
    if ($Sheet) { $arguments['Sheet'] = $Sheet }
    $parsed = ((& $ReadXlsx @arguments) -join "`n") | ConvertFrom-Json
    # Windows PowerShell 5.1 emits a JSON array as one object; enumerate it explicitly.
    return @($parsed | ForEach-Object { $_ })
}

Describe 'ConvertTo-ColumnName' {
    It 'maps 1-based indexes to Excel column letters' {
        ConvertTo-ColumnName -Index 1 | Should BeExactly 'A'
        ConvertTo-ColumnName -Index 26 | Should BeExactly 'Z'
        ConvertTo-ColumnName -Index 27 | Should BeExactly 'AA'
        ConvertTo-ColumnName -Index 703 | Should BeExactly 'AAA'
    }
}

Describe 'Test-WorkbookDefinition' {
    It 'accepts valid sheets' {
        @(Test-WorkbookDefinition -Sheets @([pscustomobject]@{ Name = 'Summary'; Rows = @(, @('ID')) })).Count | Should Be 0
    }
    It 'refuses no sheets, long or forbidden names, duplicates and empty sheets' {
        @(Test-WorkbookDefinition -Sheets @()).Count | Should Be 1
        $sheets = @(
            [pscustomobject]@{ Name = ('x' * 32); Rows = @(, @('a')) },
            [pscustomobject]@{ Name = 'a/b'; Rows = @(, @('a')) },
            [pscustomobject]@{ Name = 'Dup'; Rows = @(, @('a')) },
            [pscustomobject]@{ Name = 'dup'; Rows = @(, @('a')) },
            [pscustomobject]@{ Name = 'Empty'; Rows = @() }
        )
        $findings = @(Test-WorkbookDefinition -Sheets $sheets)
        $findings.Count | Should Be 4
        ($findings -join ';') | Should Match 'longer than 31'
        ($findings -join ';') | Should Match 'refuses'
        ($findings -join ';') | Should Match 'appears twice'
        ($findings -join ';') | Should Match 'no rows'
    }
}

Describe 'ConvertFrom-WorkbookDefinition and New-SharedStringTable' {
    It 'keeps a one-row sheet as a list of rows and empty cells as empty strings' {
        $sheets = @(ConvertFrom-WorkbookDefinition -Json '{ "sheets": [ { "name": "One", "rows": [ ["a", null, "b"] ] } ] }')
        $sheets.Count | Should Be 1
        @($sheets[0].Rows).Count | Should Be 1
        @($sheets[0].Rows[0]).Count | Should Be 3
        $sheets[0].Rows[0][1] | Should BeExactly ''
    }
    It 'deduplicates texts and skips empty cells' {
        $sheets = @(ConvertFrom-WorkbookDefinition -Json '{ "sheets": [ { "name": "S", "rows": [ ["x", "y"], ["x", ""] ] } ] }')
        $table = New-SharedStringTable -Sheets $sheets
        @($table.Texts).Count | Should Be 2
        $table.ReferenceCount | Should Be 3
        $table.Index['x'] | Should Be 0
    }
}

Describe 'New-OoxmlWorkbook' {
    It 'writes a workbook the usecase-testing reader reads back, Arabic and XML characters included' {
        $path = Join-Path $TestDrive 'sample.xlsx'
        $sheets = @(
            [pscustomobject]@{ Name = 'Summary'; Rows = @(@('ID', 'Name (ar)'), @('UC-X-01', 'تصريح <زيارة> & "موقع"')) },
            [pscustomobject]@{ Name = 'Rules'; Rows = @(@('Rule'), @('BR-01')) }
        )
        New-OoxmlWorkbook -Sheets $sheets -Path $path | Out-Null
        $read = @(Read-WorkbookJson -Path $path)
        $read.Count | Should Be 2
        $read[0].name | Should BeExactly 'Summary'
        $read[0].rows[1].cells[1] | Should BeExactly 'تصريح <زيارة> & "موقع"'
        $read[1].rows[1].cells[0] | Should BeExactly 'BR-01'
    }
    It 'gives the same bytes for the same definition (no clock in the output)' {
        $sheets = @([pscustomobject]@{ Name = 'S'; Rows = @(, @('a', 'b')) })
        $first = Join-Path $TestDrive 'first.xlsx'
        $second = Join-Path $TestDrive 'second.xlsx'
        New-OoxmlWorkbook -Sheets $sheets -Path $first | Out-Null
        New-OoxmlWorkbook -Sheets $sheets -Path $second | Out-Null
        (Get-FileHash $first).Hash | Should Be (Get-FileHash $second).Hash
    }
    It 'refuses an invalid definition' {
        { New-OoxmlWorkbook -Sheets @([pscustomobject]@{ Name = 'Empty'; Rows = @() }) -Path (Join-Path $TestDrive 'bad.xlsx') } | Should Throw 'invalid'
    }
}

Describe 'The committed permit workbook' {
    It 'is what build-permit-workbook.ps1 builds from its JSON source' {
        $fresh = Join-Path $TestDrive 'permit.xlsx'
        & powershell -NoProfile -ExecutionPolicy Bypass -File $BuildScript -DataPath $PermitData -OutFile $fresh | Out-Null
        $LASTEXITCODE | Should Be 0
        (Get-FileHash $fresh).Hash | Should Be (Get-FileHash $PermitWorkbook).Hash
    }
    It 'has the HLS sheets in order' {
        @(Read-WorkbookJson -Path $PermitWorkbook | ForEach-Object { $_.name }) -join ',' | Should BeExactly ($ExpectedSheets -join ',')
    }
    It 'lists exactly 2 use cases on its Summary sheet' {
        $json = (& $ReadXlsx -Path $PermitWorkbook -Sheet Summary -AsJson) -join "`n"
        $ids = @(Get-WorkbookUseCaseIds -SheetJson $json -IdPattern '^UC-[A-Z]+-[0-9]+$')
        $ids -join ',' | Should BeExactly 'UC-PRM-01,UC-PRM-02'
    }
    It 'covers apply, approve, reject and the 5-day escalation to the manager' {
        $transitions = @(Read-WorkbookJson -Path $PermitWorkbook -Sheet StateTransitions)[0]
        $actions = @($transitions.rows | Select-Object -Skip 1 | ForEach-Object { '{0}>{1}' -f $_.cells[1], $_.cells[2] })
        foreach ($expected in @('Submit>Submitted', 'Approve>Approved', 'Reject>Rejected', 'Escalate>Escalated')) {
            ($actions -contains $expected) | Should Be $true
        }
        $rules = @(Read-WorkbookJson -Path $PermitWorkbook -Sheet BusinessRules)[0]
        (@($rules.rows | Where-Object { $_.cells[0] -eq 'BR-05' })[0].cells[2]) | Should Match '5 days.*inspection manager'
    }
}
