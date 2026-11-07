# Pester 3.4 tests for .claude/skills/usecase-testing/scripts/Read-Xlsx.ps1 (Should Be / Should Throw syntax).
# Run: powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester .claude/skills/usecase-testing/scripts/tests -EnableExit"
# The workbook is built at test time in its own folder under %TEMP% and deleted afterwards.
Set-StrictMode -Version Latest

$ReaderPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Read-Xlsx.ps1'
. (Join-Path $PSScriptRoot 'XlsxFixture.ps1')

function Read-FixtureJson([string] $Workbook, [string] $SheetName) {
    $arguments = @{ Path = $Workbook; AsJson = $true }
    if ($SheetName) { $arguments.Sheet = $SheetName }
    $text = (& $ReaderPath @arguments) -join "`n"
    return @(ConvertFrom-Json -InputObject $text)
}

function Get-Row($SheetResult, [int] $Number) { return @($SheetResult.rows | Where-Object { $_.row -eq $Number }) }

Describe 'Read-Xlsx.ps1' {
    $folder = New-FixtureFolder
    $workbook = New-XlsxFixture -Folder $folder
    $sheets = Read-FixtureJson -Workbook $workbook
    $smoke = $sheets[0]

    Context 'JSON output' {
        It 'lists every sheet in workbook order with its resolved part (relative and absolute targets)' {
            $sheets.Count | Should Be 2
            $smoke.name | Should Be 'Smoke'
            $smoke.part | Should Be 'xl/worksheets/sheet1.xml'
            $sheets[1].name | Should Be 'Business Rules'
            $sheets[1].part | Should Be 'xl/worksheets/sheet2.xml'
        }

        It 'reads shared strings, keeping xml:space text and ignoring phonetic runs' {
            $header = (Get-Row $smoke 1)[0].cells
            ($header -join ';') | Should Be 'Scenario ID;Expected;Notes'
            (Get-Row $smoke 2)[0].cells[0] | Should Be 'RichText'
        }

        It 'reads inline strings made of several runs, and numbers as written' {
            $cells = (Get-Row $smoke 2)[0].cells
            $cells[2] | Should Be 'Inline'
            $cells[3] | Should Be '42'
            $sheets[1].rows[0].cells[1] | Should Be '3.5'
        }

        It 'puts an empty string where a cell is missing or empty, at its column' {
            $row2 = (Get-Row $smoke 2)[0].cells
            $row2.Count | Should Be 4
            $row2[1] | Should Be ''
            $row3 = (Get-Row $smoke 3)[0].cells
            $row3[0] | Should Be ''
            $row3[1] | Should Be $FixtureArabicWord
        }

        It 'skips rows with no text at all' {
            @(Get-Row $smoke 4).Count | Should Be 0
            ((@($smoke.rows | ForEach-Object { $_.row })) -join ',') | Should Be '1,2,3,5,6'
        }

        It 'maps booleans, keeps pipes raw and places a cell without a reference after the previous one' {
            ((Get-Row $smoke 5)[0].cells -join ';') | Should Be 'TRUE;;a|b;next'
        }
    }

    Context 'text output' {
        $text = @(& $ReaderPath -Path $workbook)

        It 'prints a header per sheet and one pipe-delimited line per row' {
            $text -contains '===== SHEET: Smoke (xl/worksheets/sheet1.xml) =====' | Should Be $true
            $text -contains '[r1] Scenario ID | Expected | Notes' | Should Be $true
            $text -contains '[r5] TRUE |  | a/b | next' | Should Be $true
            $text -contains '[r6] line1 / line2' | Should Be $true
        }
    }

    Context '-Sheet' {
        It 'returns only the named sheet, case-insensitively' {
            $picked = Read-FixtureJson -Workbook $workbook -SheetName 'business rules'
            $picked.Count | Should Be 1
            $picked[0].rows[0].cells[0] | Should Be 'BR-01'
        }

        It 'fails on an unknown sheet and names the sheets that exist' {
            { & $ReaderPath -Path $workbook -Sheet 'Nope' } | Should Throw "Sheets: Smoke, Business Rules"
        }

        It 'exits non-zero under -File for an unknown sheet' {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ReaderPath -Path $workbook -Sheet 'Nope' 2>&1 | Out-Null
            $LASTEXITCODE | Should Not Be 0
        }
    }

    Context '-OutFile and -MaxCellChars' {
        It 'writes UTF-8 without a BOM so Arabic text survives' {
            $out = Join-Path $folder 'dump.json'
            & $ReaderPath -Path $workbook -AsJson -OutFile $out
            $bytes = [IO.File]::ReadAllBytes($out)
            ($bytes[0] -eq 0xEF) | Should Be $false
            $parsed = @(ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($out, [Text.Encoding]::UTF8)))
            (Get-Row $parsed[0] 3)[0].cells[1] | Should Be $FixtureArabicWord
        }

        It 'truncates long cells with a marker' {
            $short = @(ConvertFrom-Json -InputObject ((& $ReaderPath -Path $workbook -AsJson -MaxCellChars 3) -join "`n"))
            (Get-Row $short[0] 1)[0].cells[0] | Should Be 'Sce...[truncated]'
        }
    }

    Context 'bad input' {
        It 'fails on a missing file' {
            { & $ReaderPath -Path (Join-Path $folder 'missing.xlsx') } | Should Throw 'Workbook not found'
        }

        It 'fails on a file that is not a zip' {
            $notZip = Join-Path $folder 'plain.xlsx'
            Set-Content -LiteralPath $notZip -Value 'not a workbook'
            { & $ReaderPath -Path $notZip } | Should Throw 'Not an .xlsx workbook'
        }
    }

    Context 'hostile workbooks (DTDs and entities are refused, never resolved)' {
        $main = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
        $marker = 'XXE-MARKER-' + [guid]::NewGuid().ToString('N')
        $markerFile = Join-Path $folder 'local-file.txt'
        Set-Content -LiteralPath $markerFile -Value $marker
        $markerFileUri = ([Uri] $markerFile).AbsoluteUri

        It 'refuses an external entity in a sheet part and never prints the referenced file' {
            $sheetXml = "<?xml version=`"1.0`"?><!DOCTYPE worksheet [<!ENTITY xxe SYSTEM `"$markerFileUri`">]><worksheet xmlns=`"$main`"><sheetData><row r=`"1`"><c r=`"A1`" t=`"inlineStr`"><is><t>&xxe;</t></is></c></row></sheetData></worksheet>"
            $hostile = New-XlsxFixtureWithParts -Folder $folder -SheetXml $sheetXml
            { & $ReaderPath -Path $hostile } | Should Throw "workbook part 'xl/worksheets/sheet1.xml'"
            $printed = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ReaderPath -Path $hostile 2>&1 | Out-String)
            $LASTEXITCODE | Should Not Be 0
            $printed.Contains($marker) | Should Be $false
        }

        It 'refuses an external entity in the shared-strings part' {
            $sstXml = "<?xml version=`"1.0`"?><!DOCTYPE sst [<!ENTITY xxe SYSTEM `"$markerFileUri`">]><sst xmlns=`"$main`"><si><t>&xxe;</t></si></sst>"
            $hostile = New-XlsxFixtureWithParts -Folder $folder -SharedStringsXml $sstXml
            { & $ReaderPath -Path $hostile } | Should Throw "workbook part 'xl/sharedStrings.xml'"
        }

        It 'refuses a nested-entity expansion (billion laughs) instead of expanding it' {
            $sheetXml = "<?xml version=`"1.0`"?><!DOCTYPE worksheet [<!ENTITY a `"lol`"><!ENTITY b `"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;`"><!ENTITY c `"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;`"><!ENTITY d `"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;`">]><worksheet xmlns=`"$main`"><sheetData><row r=`"1`"><c r=`"A1`" t=`"inlineStr`"><is><t>&d;</t></is></c></row></sheetData></worksheet>"
            $hostile = New-XlsxFixtureWithParts -Folder $folder -SheetXml $sheetXml
            { & $ReaderPath -Path $hostile } | Should Throw 'Refused'
        }
    }
    Remove-FixtureFolder $folder
}
