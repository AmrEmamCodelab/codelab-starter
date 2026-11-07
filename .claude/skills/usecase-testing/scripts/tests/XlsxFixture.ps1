# Builds tiny OOXML workbooks for the Pester tests at test time (nothing binary is committed).
# Dot-source it: . (Join-Path $PSScriptRoot 'XlsxFixture.ps1')
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$FixtureTempPrefix = 'codelab-usecase-testing-'
# Arabic "yes", built from code points so this file stays ASCII (Windows PowerShell 5.1 reads BOM-less scripts as ANSI).
$FixtureArabicWord = -join @([char] 0x0646, [char] 0x0639, [char] 0x0645)

function New-FixtureFolder {
    $path = Join-Path ([IO.Path]::GetTempPath()) ($FixtureTempPrefix + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

function Remove-FixtureFolder([string] $Path) {
    if ($Path -and (Test-Path -LiteralPath $Path) -and (Split-Path -Leaf $Path).StartsWith($FixtureTempPrefix)) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Write-FixturePart([System.IO.Compression.ZipArchive] $Zip, [string] $Name, [string] $Text) {
    $writer = New-Object System.IO.StreamWriter($Zip.CreateEntry($Name).Open(), (New-Object System.Text.UTF8Encoding($false)))
    try { $writer.Write($Text) } finally { $writer.Dispose() }
}

function New-XlsxFixture([string] $Folder) {
    $main = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
    $rel = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
    $sharedStrings = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<sst xmlns="$main" count="6" uniqueCount="6">
<si><t>Scenario ID</t></si>
<si><t xml:space="preserve">Expected</t></si>
<si><r><t>Rich</t></r><r><rPr><b/></rPr><t>Text</t></r><rPh sb="0" eb="1"><t>IGNORED</t></rPh></si>
<si><t>$FixtureArabicWord</t></si>
<si><t>a|b</t></si>
<si><t>line1
line2</t></si>
</sst>
"@
    $sheet1 = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="$main"><sheetData>
<row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="inlineStr"><is><t>Notes</t></is></c></row>
<row r="2"><c r="A2" t="s"><v>2</v></c><c r="C2" t="inlineStr"><is><r><t>In</t></r><r><t>line</t></r></is></c><c r="D2"><v>42</v></c></row>
<row r="3"><c r="A3"/><c r="B3" t="s"><v>3</v></c></row>
<row r="4"><c r="A4"/><c r="B4" t="inlineStr"><is><t></t></is></c></row>
<row r="5"><c r="A5" t="b"><v>1</v></c><c r="C5" t="s"><v>4</v></c><c t="inlineStr"><is><t>next</t></is></c></row>
<row r="6"><c r="A6" t="s"><v>5</v></c></row>
</sheetData></worksheet>
"@
    $sheet2 = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="$main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>BR-01</t></is></c><c r="B1"><v>3.5</v></c></row></sheetData></worksheet>
"@
    $workbook = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><workbook xmlns=`"$main`" xmlns:r=`"$rel`"><sheets><sheet name=`"Smoke`" sheetId=`"1`" r:id=`"rId1`"/><sheet name=`"Business Rules`" sheetId=`"2`" r:id=`"rId2`"/></sheets></workbook>"
    $workbookRels = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><Relationships xmlns=`"http://schemas.openxmlformats.org/package/2006/relationships`"><Relationship Id=`"rId1`" Type=`"$rel/worksheet`" Target=`"worksheets/sheet1.xml`"/><Relationship Id=`"rId2`" Type=`"$rel/worksheet`" Target=`"/xl/worksheets/sheet2.xml`"/><Relationship Id=`"rId3`" Type=`"$rel/sharedStrings`" Target=`"sharedStrings.xml`"/></Relationships>"

    $path = Join-Path $Folder 'fixture.xlsx'
    $zip = [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Write-FixturePart $zip 'xl/workbook.xml' $workbook
        Write-FixturePart $zip 'xl/_rels/workbook.xml.rels' $workbookRels
        Write-FixturePart $zip 'xl/sharedStrings.xml' $sharedStrings
        Write-FixturePart $zip 'xl/worksheets/sheet1.xml' $sheet1
        Write-FixturePart $zip 'xl/worksheets/sheet2.xml' $sheet2
    }
    finally { $zip.Dispose() }
    return $path
}

# A one-sheet workbook whose sheet part (or shared-strings part) is the given raw XML, for the hostile-input tests.
function New-XlsxFixtureWithParts([string] $Folder, [string] $SheetXml, [string] $SharedStringsXml) {
    $main = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
    $rel = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
    if (-not $SheetXml) { $SheetXml = "<?xml version=`"1.0`" encoding=`"UTF-8`"?><worksheet xmlns=`"$main`"><sheetData><row r=`"1`"><c r=`"A1`" t=`"s`"><v>0</v></c></row></sheetData></worksheet>" }
    $workbook = "<?xml version=`"1.0`" encoding=`"UTF-8`"?><workbook xmlns=`"$main`" xmlns:r=`"$rel`"><sheets><sheet name=`"Hostile`" sheetId=`"1`" r:id=`"rId1`"/></sheets></workbook>"
    $workbookRels = "<?xml version=`"1.0`" encoding=`"UTF-8`"?><Relationships xmlns=`"http://schemas.openxmlformats.org/package/2006/relationships`"><Relationship Id=`"rId1`" Type=`"$rel/worksheet`" Target=`"worksheets/sheet1.xml`"/></Relationships>"

    $path = Join-Path $Folder ('hostile-' + [guid]::NewGuid().ToString('N') + '.xlsx')
    $zip = [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Write-FixturePart $zip 'xl/workbook.xml' $workbook
        Write-FixturePart $zip 'xl/_rels/workbook.xml.rels' $workbookRels
        Write-FixturePart $zip 'xl/worksheets/sheet1.xml' $SheetXml
        if ($SharedStringsXml) { Write-FixturePart $zip 'xl/sharedStrings.xml' $SharedStringsXml }
    }
    finally { $zip.Dispose() }
    return $path
}
