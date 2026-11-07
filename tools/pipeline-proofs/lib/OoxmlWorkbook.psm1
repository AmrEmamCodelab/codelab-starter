# OoxmlWorkbook.psm1: writes a text-only .xlsx workbook with System.IO.Compression (Windows PowerShell 5.1; no Excel,
# Python or module). Used by build-permit-workbook.ps1 to generate the HLS-format input of the Permit proof; the
# output is read back by .claude/skills/usecase-testing/scripts/Read-Xlsx.ps1. See tools/pipeline-proofs/MODULE.md.
#   ConvertFrom-WorkbookDefinition -Json <text>             -> @(@{ Name; Rows = @(@(cell, ...), ...) }, ...)
#   New-OoxmlWorkbook -Sheets <definitions> -Path <file.xlsx> -> the written path (an existing file is replaced)
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ---------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------
$script:SpreadsheetNamespace = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
$script:RelationshipNamespace = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
$script:PackageRelationshipNamespace = 'http://schemas.openxmlformats.org/package/2006/relationships'
$script:ContentTypesNamespace = 'http://schemas.openxmlformats.org/package/2006/content-types'
$script:OfficeDocumentRelationship = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument'
$script:WorksheetRelationship = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet'
$script:SharedStringsRelationship = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings'
$script:StylesRelationship = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles'
$script:WorkbookContentType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'
$script:WorksheetContentType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml'
$script:SharedStringsContentType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml'
$script:StylesContentType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml'
$script:RelationshipsContentType = 'application/vnd.openxmlformats-package.relationships+xml'
$script:XmlContentType = 'application/xml'
$script:XmlDeclaration = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'

$script:ContentTypesPart = '[Content_Types].xml'
$script:RootRelationshipsPart = '_rels/.rels'
$script:WorkbookPart = 'xl/workbook.xml'
$script:WorkbookRelationshipsPart = 'xl/_rels/workbook.xml.rels'
$script:SharedStringsPart = 'xl/sharedStrings.xml'
$script:StylesPart = 'xl/styles.xml'
$script:WorksheetPartFormat = 'xl/worksheets/sheet{0}.xml'
$script:WorksheetTargetFormat = 'worksheets/sheet{0}.xml'
$script:SharedStringsTarget = 'sharedStrings.xml'
$script:StylesTarget = 'styles.xml'

# Excel's own limits on a sheet name.
$script:MaxSheetNameLength = 31
$script:ForbiddenSheetNameCharacters = '[\[\]:*?/\\]'
$script:ColumnBase = 26
$script:FirstColumnLetter = [int][char]'A'
# Column width: the longest cell text plus padding, between these bounds (Excel character units).
$script:MinColumnWidth = 8
$script:MaxColumnWidth = 80
$script:ColumnPadding = 2
# Style indexes in styles.xml cellXfs: 0 default, 1 body (top-aligned, wrapped), 2 bold header (top-aligned, wrapped).
$script:BodyStyleIndex = 1
$script:HeaderStyleIndex = 2
# A fixed entry time keeps the generated file byte-stable between runs (no clock in the output).
$script:EntryTimestamp = New-Object DateTimeOffset -ArgumentList 2026, 1, 1, 0, 0, 0, ([TimeSpan]::Zero)

$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)

# ---------------------------------------------------------------------------------------------------------------
# Pure helpers
# ---------------------------------------------------------------------------------------------------------------

# 1 -> A, 26 -> Z, 27 -> AA (pure).
function ConvertTo-ColumnName {
    param([Parameter(Mandatory = $true)][ValidateRange(1, 16384)][int] $Index)
    $name = ''
    $remaining = $Index
    while ($remaining -gt 0) {
        $remainder = ($remaining - 1) % $script:ColumnBase
        $name = [string][char]($script:FirstColumnLetter + $remainder) + $name
        $remaining = [int][Math]::Floor(($remaining - 1) / $script:ColumnBase)
    }
    return $name
}

function ConvertTo-XmlText {
    param([AllowEmptyString()][string] $Text)
    return [Security.SecurityElement]::Escape($Text)
}

# Findings for a list of sheet definitions (pure): empty, duplicate or invalid names, no rows.
function Test-WorkbookDefinition {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]] $Sheets)
    $findings = New-Object System.Collections.Generic.List[string]
    if ($Sheets.Count -eq 0) { $findings.Add('the workbook has no sheet') }
    $seen = @{}
    foreach ($sheet in $Sheets) {
        $name = [string] $sheet.Name
        if (-not $name) { $findings.Add('a sheet has no name'); continue }
        if ($name.Length -gt $script:MaxSheetNameLength) { $findings.Add("sheet '$name' is longer than $script:MaxSheetNameLength characters") }
        if ($name -match $script:ForbiddenSheetNameCharacters) { $findings.Add("sheet '$name' contains a character Excel refuses") }
        $key = $name.ToLowerInvariant()
        if ($seen.ContainsKey($key)) { $findings.Add("sheet '$name' appears twice") }
        $seen[$key] = $true
        if (@($sheet.Rows).Count -eq 0) { $findings.Add("sheet '$name' has no rows") }
    }
    return @($findings)
}

# Parses the JSON workbook definition { "sheets": [ { "name": "...", "rows": [ [ "cell", ... ], ... ] } ] } (pure).
function ConvertFrom-WorkbookDefinition {
    param([Parameter(Mandatory = $true)][string] $Json)
    $document = $Json | ConvertFrom-Json
    $sheets = New-Object System.Collections.Generic.List[object]
    foreach ($sheet in @($document.sheets)) {
        $rows = New-Object System.Collections.Generic.List[object]
        foreach ($row in @($sheet.rows)) {
            $cells = [string[]] @(@($row) | ForEach-Object { if ($null -eq $_) { '' } else { [string] $_ } })
            $rows.Add($cells)
        }
        $sheets.Add([pscustomobject]@{ Name = [string] $sheet.name; Rows = $rows.ToArray() })
    }
    return $sheets.ToArray()
}

# The shared-string table of all sheets: distinct texts in first-use order and their indexes (pure).
function New-SharedStringTable {
    param([Parameter(Mandatory = $true)][object[]] $Sheets)
    $index = New-Object 'System.Collections.Generic.Dictionary[string,int]' ([StringComparer]::Ordinal)
    $texts = New-Object System.Collections.Generic.List[string]
    $count = 0
    foreach ($sheet in $Sheets) {
        foreach ($row in @($sheet.Rows)) {
            foreach ($cell in @($row)) {
                $text = [string] $cell
                if ($text.Length -eq 0) { continue }
                $count++
                if (-not $index.ContainsKey($text)) { $index[$text] = $texts.Count; $texts.Add($text) }
            }
        }
    }
    return [pscustomobject]@{ Index = $index; Texts = @($texts); ReferenceCount = $count }
}

function Get-ColumnWidths {
    param([Parameter(Mandatory = $true)][object[]] $Rows)
    $widths = @{}
    foreach ($row in $Rows) {
        $column = 0
        foreach ($cell in @($row)) {
            $column++
            $longestLine = (([string] $cell) -split "`n" | Measure-Object -Property Length -Maximum).Maximum
            $width = [Math]::Min($script:MaxColumnWidth, [Math]::Max($script:MinColumnWidth, [int] $longestLine + $script:ColumnPadding))
            if (-not $widths.ContainsKey($column) -or $widths[$column] -lt $width) { $widths[$column] = $width }
        }
    }
    return $widths
}

function Get-WorksheetXml {
    param([Parameter(Mandatory = $true)][object] $Sheet, [Parameter(Mandatory = $true)][object] $SharedStrings)
    $builder = New-Object Text.StringBuilder
    [void] $builder.Append($script:XmlDeclaration)
    [void] $builder.Append("<worksheet xmlns=`"$script:SpreadsheetNamespace`" xmlns:r=`"$script:RelationshipNamespace`">")
    $rows = @($Sheet.Rows)
    $widths = Get-ColumnWidths -Rows $rows
    if ($widths.Count -gt 0) {
        [void] $builder.Append('<cols>')
        foreach ($column in ($widths.Keys | Sort-Object)) {
            [void] $builder.Append("<col min=`"$column`" max=`"$column`" width=`"$($widths[$column])`" customWidth=`"1`"/>")
        }
        [void] $builder.Append('</cols>')
    }
    [void] $builder.Append('<sheetData>')
    $rowNumber = 0
    foreach ($row in $rows) {
        $rowNumber++
        $style = if ($rowNumber -eq 1) { $script:HeaderStyleIndex } else { $script:BodyStyleIndex }
        [void] $builder.Append("<row r=`"$rowNumber`">")
        $column = 0
        foreach ($cell in @($row)) {
            $column++
            $text = [string] $cell
            if ($text.Length -eq 0) { continue }
            $reference = (ConvertTo-ColumnName -Index $column) + $rowNumber
            [void] $builder.Append("<c r=`"$reference`" s=`"$style`" t=`"s`"><v>$($SharedStrings.Index[$text])</v></c>")
        }
        [void] $builder.Append('</row>')
    }
    [void] $builder.Append('</sheetData></worksheet>')
    return $builder.ToString()
}

function Get-SharedStringsXml {
    param([Parameter(Mandatory = $true)][object] $SharedStrings)
    $builder = New-Object Text.StringBuilder
    [void] $builder.Append($script:XmlDeclaration)
    [void] $builder.Append("<sst xmlns=`"$script:SpreadsheetNamespace`" count=`"$($SharedStrings.ReferenceCount)`" uniqueCount=`"$($SharedStrings.Texts.Count)`">")
    foreach ($text in $SharedStrings.Texts) {
        [void] $builder.Append("<si><t xml:space=`"preserve`">$(ConvertTo-XmlText $text)</t></si>")
    }
    [void] $builder.Append('</sst>')
    return $builder.ToString()
}

function Get-StylesXml {
    return $script:XmlDeclaration +
    "<styleSheet xmlns=`"$script:SpreadsheetNamespace`">" +
    '<fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>' +
    '<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>' +
    '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>' +
    '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>' +
    '<cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>' +
    '<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>' +
    '<xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf></cellXfs>' +
    '<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>' +
    '</styleSheet>'
}

# Every part of the package as an ordered map part name -> XML text (pure).
function Get-WorkbookParts {
    param([Parameter(Mandatory = $true)][object[]] $Sheets)
    $sharedStrings = New-SharedStringTable -Sheets $Sheets
    $parts = [ordered]@{}

    $overrides = New-Object Text.StringBuilder
    for ($i = 1; $i -le $Sheets.Count; $i++) {
        [void] $overrides.Append("<Override PartName=`"/$($script:WorksheetPartFormat -f $i)`" ContentType=`"$script:WorksheetContentType`"/>")
    }
    $parts[$script:ContentTypesPart] = $script:XmlDeclaration +
    "<Types xmlns=`"$script:ContentTypesNamespace`">" +
    "<Default Extension=`"rels`" ContentType=`"$script:RelationshipsContentType`"/>" +
    "<Default Extension=`"xml`" ContentType=`"$script:XmlContentType`"/>" +
    "<Override PartName=`"/$script:WorkbookPart`" ContentType=`"$script:WorkbookContentType`"/>" +
    $overrides.ToString() +
    "<Override PartName=`"/$script:SharedStringsPart`" ContentType=`"$script:SharedStringsContentType`"/>" +
    "<Override PartName=`"/$script:StylesPart`" ContentType=`"$script:StylesContentType`"/>" +
    '</Types>'

    $parts[$script:RootRelationshipsPart] = $script:XmlDeclaration +
    "<Relationships xmlns=`"$script:PackageRelationshipNamespace`">" +
    "<Relationship Id=`"rId1`" Type=`"$script:OfficeDocumentRelationship`" Target=`"$script:WorkbookPart`"/>" +
    '</Relationships>'

    $sheetEntries = New-Object Text.StringBuilder
    $sheetRelationships = New-Object Text.StringBuilder
    for ($i = 1; $i -le $Sheets.Count; $i++) {
        $name = ConvertTo-XmlText ([string] $Sheets[$i - 1].Name)
        [void] $sheetEntries.Append("<sheet name=`"$name`" sheetId=`"$i`" r:id=`"rId$i`"/>")
        [void] $sheetRelationships.Append("<Relationship Id=`"rId$i`" Type=`"$script:WorksheetRelationship`" Target=`"$($script:WorksheetTargetFormat -f $i)`"/>")
    }
    $sharedStringsId = $Sheets.Count + 1
    $stylesId = $Sheets.Count + 2
    $parts[$script:WorkbookPart] = $script:XmlDeclaration +
    "<workbook xmlns=`"$script:SpreadsheetNamespace`" xmlns:r=`"$script:RelationshipNamespace`"><sheets>" +
    $sheetEntries.ToString() + '</sheets></workbook>'
    $parts[$script:WorkbookRelationshipsPart] = $script:XmlDeclaration +
    "<Relationships xmlns=`"$script:PackageRelationshipNamespace`">" + $sheetRelationships.ToString() +
    "<Relationship Id=`"rId$sharedStringsId`" Type=`"$script:SharedStringsRelationship`" Target=`"$script:SharedStringsTarget`"/>" +
    "<Relationship Id=`"rId$stylesId`" Type=`"$script:StylesRelationship`" Target=`"$script:StylesTarget`"/>" +
    '</Relationships>'

    for ($i = 1; $i -le $Sheets.Count; $i++) {
        $parts[($script:WorksheetPartFormat -f $i)] = Get-WorksheetXml -Sheet $Sheets[$i - 1] -SharedStrings $sharedStrings
    }
    $parts[$script:SharedStringsPart] = Get-SharedStringsXml -SharedStrings $sharedStrings
    $parts[$script:StylesPart] = Get-StylesXml
    return $parts
}

# ---------------------------------------------------------------------------------------------------------------
# Writer
# ---------------------------------------------------------------------------------------------------------------

function New-OoxmlWorkbook {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object[]] $Sheets,
        [Parameter(Mandatory = $true)][string] $Path
    )
    $findings = @(Test-WorkbookDefinition -Sheets $Sheets)
    if ($findings.Count -gt 0) { throw "The workbook definition is invalid: $($findings -join '; ')." }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $folder = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    $parts = Get-WorkbookParts -Sheets $Sheets
    $stream = New-Object IO.MemoryStream
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in $parts.Keys) {
                $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $script:EntryTimestamp
                $writer = New-Object IO.StreamWriter($entry.Open(), $script:Utf8NoBom)
                try { $writer.Write([string] $parts[$name]) } finally { $writer.Dispose() }
            }
        } finally { $archive.Dispose() }
        [IO.File]::WriteAllBytes($fullPath, $stream.ToArray())
    } finally { $stream.Dispose() }
    return $fullPath
}

Export-ModuleMember -Function ConvertTo-ColumnName, Test-WorkbookDefinition, ConvertFrom-WorkbookDefinition, New-SharedStringTable,
    Get-WorkbookParts, New-OoxmlWorkbook
