<#
.SYNOPSIS
  Reads an .xlsx workbook without Excel, Python or any module and prints its rows as text or JSON.

.DESCRIPTION
  An .xlsx file is a zip of OOXML parts. This script opens it with System.IO.Compression (Windows PowerShell 5.1; no Excel or
  Python), resolves each worksheet through xl/workbook.xml and its relationships, and reads shared strings
  (plain and rich-text runs, phonetic runs skipped), inline strings, numbers, booleans and formula results. Missing
  and empty cells become '' at their column position; rows with no text at all are skipped.

  Text output (default): one block per sheet, '===== SHEET: <name> =====', then '[r<row>] cell | cell | ...'.
  JSON output (-AsJson): an array of { name, part, rows: [ { row, cells: [ ... ] } ] } in workbook order.

  Dates arrive as Excel serial numbers (days since 1899-12-30); convert them before quoting.
  Errors (missing file, not a workbook, unknown sheet) are terminating: exit code 1 under -File.
  Workbooks are untrusted input: a part that declares a DTD is refused (no external entity, file, UNC or HTTP fetch,
  no entity expansion), and the refusal names the part.

.PARAMETER Path
  The .xlsx file.

.PARAMETER Sheet
  Read only the sheet with this name (case-insensitive). Unknown names fail and list the sheets that exist.

.PARAMETER AsJson
  Emit JSON instead of text.

.PARAMETER OutFile
  Write the output to this file as UTF-8 (no BOM) instead of the pipeline. Use it when another process reads the
  result, so Arabic text survives the console code page.

.PARAMETER MaxCellChars
  Cells longer than this are cut and end with '...[truncated]'.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .claude/skills/usecase-testing/scripts/Read-Xlsx.ps1 -Path "<workbook.xlsx>" -Sheet BusinessRules -AsJson -OutFile "test-reports/UC-001/BusinessRules.json"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [string] $Sheet,
    [switch] $AsJson,
    [string] $OutFile,
    [ValidateRange(1, 1000000)] [int] $MaxCellChars = 4000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$WorkbookPart          = 'xl/workbook.xml'
$WorkbookRelsPart      = 'xl/_rels/workbook.xml.rels'
$SharedStringsPart     = 'xl/sharedStrings.xml'
$PartPrefix            = 'xl/'
$RelationshipNamespace = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
$CellTypeShared        = 's'
$CellTypeInline        = 'inlineStr'
$CellTypeBoolean       = 'b'
$BooleanTrueRaw        = '1'
$BooleanTrue           = 'TRUE'
$BooleanFalse          = 'FALSE'
$TruncationMarker      = '...[truncated]'
$TextCellSeparator     = ' | '
$TextNewlineStandIn    = ' / '
$TextPipeStandIn       = '/'
$ColumnBase            = 26
$LetterOffset          = 64
$JsonDepth             = 8
$UnsafePartMessage     = 'Refused: not well-formed or declares a DTD (entities are never resolved) in workbook part'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-ZipPart([System.IO.Compression.ZipArchive] $Zip, [string] $Name) {
    $entry = $Zip.GetEntry($Name)
    if ($null -eq $entry) { return $null }
    $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function ConvertTo-XmlDocument([string] $Text, [string] $PartName) {
    # Workbooks come from outside: no DTD (so no external or nested entities) and no resolver (no file, UNC or HTTP fetch).
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersFromEntities = 0
    $doc = New-Object System.Xml.XmlDocument
    $doc.XmlResolver = $null
    $doc.PreserveWhitespace = $true
    $reader = [System.Xml.XmlReader]::Create((New-Object System.IO.StringReader($Text)), $settings)
    try { $doc.Load($reader) }
    catch [System.Xml.XmlException] { throw "$UnsafePartMessage '$PartName': $($_.Exception.Message)" }
    finally { $reader.Dispose() }
    return $doc
}

function Get-TextOfNode([System.Xml.XmlNode] $Node) {
    # Text runs (<t>) outside phonetic runs (<rPh>), which would duplicate the reading.
    $parts = foreach ($t in $Node.SelectNodes('.//*[local-name()="t" and not(ancestor::*[local-name()="rPh"])]')) { $t.InnerText }
    return (-join @($parts))
}

function Read-SharedStrings([System.IO.Compression.ZipArchive] $Zip) {
    $list = New-Object System.Collections.Generic.List[string]
    $xml = Read-ZipPart -Zip $Zip -Name $SharedStringsPart
    if ($null -eq $xml) { return ,$list }
    $doc = ConvertTo-XmlDocument -Text $xml -PartName $SharedStringsPart
    foreach ($si in $doc.SelectNodes('/*[local-name()="sst"]/*[local-name()="si"]')) { $list.Add((Get-TextOfNode $si)) }
    return ,$list
}

function Resolve-PartName([string] $Target) {
    if ($Target.StartsWith('/')) { return $Target.TrimStart('/') }
    return $PartPrefix + $Target
}

function Get-SheetParts([System.IO.Compression.ZipArchive] $Zip) {
    $workbookXml = Read-ZipPart -Zip $Zip -Name $WorkbookPart
    if ($null -eq $workbookXml) { throw "Not an .xlsx workbook: '$WorkbookPart' is missing." }
    $relTargets = @{}
    $relsXml = Read-ZipPart -Zip $Zip -Name $WorkbookRelsPart
    if ($null -ne $relsXml) {
        foreach ($rel in (ConvertTo-XmlDocument -Text $relsXml -PartName $WorkbookRelsPart).SelectNodes('//*[local-name()="Relationship"]')) {
            $relTargets[$rel.GetAttribute('Id')] = $rel.GetAttribute('Target')
        }
    }
    $index = 0
    foreach ($node in (ConvertTo-XmlDocument -Text $workbookXml -PartName $WorkbookPart).SelectNodes('//*[local-name()="sheets"]/*[local-name()="sheet"]')) {
        $index++
        $relId = $node.GetAttribute('id', $RelationshipNamespace)
        $target = if ($relTargets.ContainsKey($relId)) { $relTargets[$relId] } else { "worksheets/sheet$index.xml" }
        [pscustomobject]@{ Name = $node.GetAttribute('name'); Part = (Resolve-PartName $target) }
    }
}

function Convert-ColumnLetters([string] $Reference) {
    $letters = ($Reference -replace '[^A-Za-z]', '').ToUpperInvariant()
    $number = 0
    foreach ($ch in $letters.ToCharArray()) { $number = $number * $ColumnBase + ([int] $ch - $LetterOffset) }
    return $number
}

function Get-CellValue([System.Xml.XmlElement] $Cell, [System.Collections.Generic.List[string]] $Shared) {
    $type = $Cell.GetAttribute('t')
    $valueNode = $Cell.SelectSingleNode('*[local-name()="v"]')
    $raw = if ($null -ne $valueNode) { $valueNode.InnerText } else { '' }
    if ($type -eq $CellTypeShared) {
        $at = 0
        if ([int]::TryParse($raw, [ref] $at) -and $at -ge 0 -and $at -lt $Shared.Count) { return $Shared[$at] }
        return ''
    }
    if ($type -eq $CellTypeInline) { return (Get-TextOfNode $Cell) }
    if ($type -eq $CellTypeBoolean) { if ($raw -eq $BooleanTrueRaw) { return $BooleanTrue } else { return $BooleanFalse } }
    if ($null -ne $valueNode) { return $raw }
    return (Get-TextOfNode $Cell)
}

function Limit-Text([string] $Value) {
    if ($Value.Length -le $MaxCellChars) { return $Value }
    return $Value.Substring(0, $MaxCellChars) + $TruncationMarker
}

function Read-SheetRows([string] $SheetXml, [string] $PartName, [System.Collections.Generic.List[string]] $Shared) {
    $doc = ConvertTo-XmlDocument -Text $SheetXml -PartName $PartName
    $rowNumber = 0
    foreach ($row in $doc.SelectNodes('//*[local-name()="sheetData"]/*[local-name()="row"]')) {
        $declared = $row.GetAttribute('r')
        $rowNumber = if ($declared) { [int] $declared } else { $rowNumber + 1 }
        $cells = @{}
        $maxColumn = 0
        $column = 0
        foreach ($cell in $row.SelectNodes('*[local-name()="c"]')) {
            $reference = $cell.GetAttribute('r')
            $column = if ($reference) { Convert-ColumnLetters $reference } else { $column + 1 }
            if ($column -gt $maxColumn) { $maxColumn = $column }
            $cells[$column] = Limit-Text (Get-CellValue -Cell $cell -Shared $Shared)
        }
        if ($maxColumn -eq 0) { continue }
        $values = foreach ($c in 1..$maxColumn) { if ($cells.ContainsKey($c)) { [string] $cells[$c] } else { '' } }
        $values = @($values)
        if ((-join $values).Trim() -eq '') { continue }
        [pscustomobject]@{ row = $rowNumber; cells = $values }
    }
}

function Format-SheetText($SheetResult) {
    ''
    "===== SHEET: $($SheetResult.name) ($($SheetResult.part)) ====="
    foreach ($row in $SheetResult.rows) {
        $flat = foreach ($v in $row.cells) { $v -replace "`r`n", $TextNewlineStandIn -replace "`n", $TextNewlineStandIn -replace '\|', $TextPipeStandIn }
        "[r$($row.row)] " + (@($flat) -join $TextCellSeparator)
    }
}

function Read-Workbook {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Workbook not found: $Path" }
    $full = (Resolve-Path -LiteralPath $Path).ProviderPath
    try { $zip = [System.IO.Compression.ZipFile]::OpenRead($full) }
    catch { throw "Not an .xlsx workbook (cannot open as a zip): $Path. $($_.Exception.Message)" }
    try {
        $shared = Read-SharedStrings $zip
        $parts = @(Get-SheetParts $zip)
        if ($Sheet) {
            $picked = @($parts | Where-Object { $_.Name -ieq $Sheet })
            if ($picked.Count -eq 0) { throw "Sheet '$Sheet' not found. Sheets: $((@($parts | ForEach-Object { $_.Name })) -join ', ')" }
            $parts = $picked
        }
        foreach ($part in $parts) {
            $sheetXml = Read-ZipPart -Zip $zip -Name $part.Part
            if ($null -eq $sheetXml) { continue }
            [pscustomobject]@{ name = $part.Name; part = $part.Part; rows = @(Read-SheetRows -SheetXml $sheetXml -PartName $part.Part -Shared $shared) }
        }
    }
    finally { $zip.Dispose() }
}

$sheets = @(Read-Workbook)
$lines = if ($AsJson) { @(ConvertTo-Json -InputObject $sheets -Depth $JsonDepth) } else { @(foreach ($s in $sheets) { Format-SheetText $s }) }

if ($OutFile) {
    $target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
    [System.IO.File]::WriteAllText($target, (($lines -join [Environment]::NewLine) + [Environment]::NewLine), (New-Object System.Text.UTF8Encoding($false)))
    return
}
try { if ([Console]::IsOutputRedirected) { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } } catch { Write-Verbose 'Console encoding unchanged.' }
$lines
