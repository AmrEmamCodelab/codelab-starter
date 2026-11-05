<#
.SYNOPSIS
    Writes the 3-row sample workbook for the Excel user import check (admin > users > import).

.DESCRIPTION
    Creates a one-sheet .xlsx (System.IO.Compression only, inline strings, no spreadsheet program needed) with the
    import template's columns (the import matches headers, so their order is free; Email comes last here) and
    three rows:
      row 2  <prefix>.one   password-bound, valid
      row 3  <prefix>.two   password-bound, valid
      row 4  <prefix>.three password-bound, INVALID: its e-mail address is malformed (USERS_IMPORT_ROW_EMAIL_INVALID)
    The preview shows 2 valid rows and 1 rejected row with its reason; the commit creates 2 accounts without a
    password, each with an activation link and a Pending account-activation outbox row.

    There is no password column: the import never sets a password. All data is neutral sample data under the
    reserved example.test domain. The user names carry a random suffix so the check can run again on the same
    database (a second run with the same names would be rejected as taken).

.PARAMETER Path
    The workbook to write. Defaults to sample-users.xlsx in the temp folder (never inside the repository).

.PARAMETER Suffix
    The user-name suffix (letters and digits). Defaults to 4 random hex characters.

.PARAMETER OrganizationCode
    Make the rows external users of this (existing, active, external) organization. Without it the rows are
    internal users with no organization, so the check needs no organization first.

.EXAMPLE
    powershell -NoProfile -File tools\devseed\user-import\New-SampleUserImport.ps1

.NOTES
    Exit codes: 0 written, 2 usage. Prints the path and the three user names.
#>
[CmdletBinding()]
param(
    [string] $Path = (Join-Path ([System.IO.Path]::GetTempPath()) 'sample-users.xlsx'),
    [string] $Suffix,
    [string] $OrganizationCode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$UsageExitCode = 2
$SuffixPattern = '^[A-Za-z0-9]{1,16}$'
$CodePattern = '^[A-Za-z0-9_-]{1,50}$'
$UserNamePrefix = 'sample'
$MailDomain = 'example.test'
$MainNamespace = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
$PackageRelationships = 'http://schemas.openxmlformats.org/package/2006/relationships'
$OfficeRelationships = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
# Email is the last column: the scrub scanner joins a row's cell texts, so a following word would extend the domain.
$Header = @('UserName', 'DisplayName', 'LoginName', 'Upn', 'Kind', 'Binding', 'OrganizationCode', 'Email')
$BindingWord = 'password'

if (-not $Suffix) {
    $Suffix = -join ((1..4) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
}
if ($Suffix -notmatch $SuffixPattern) {
    [Console]::Error.WriteLine('-Suffix must be 1 to 16 letters or digits.')
    exit $UsageExitCode
}
if ($OrganizationCode -and $OrganizationCode -notmatch $CodePattern) {
    [Console]::Error.WriteLine("-OrganizationCode must be 1 to 50 letters, digits, '_' or '-'.")
    exit $UsageExitCode
}

$kind = if ($OrganizationCode) { 'external' } else { 'internal' }
$organization = if ($OrganizationCode) { $OrganizationCode } else { $null }

function New-SampleRow([string] $Name, [string] $Display, [string] $Mail) {
    return @("$UserNamePrefix.$Name.$Suffix", $Display, $null, $null, $kind, $BindingWord, $organization, $Mail)
}

$rows = @(
    , $Header
    , (New-SampleRow 'one' 'Sample One' "$UserNamePrefix.one.$Suffix@$MailDomain")
    , (New-SampleRow 'two' 'Sample Two' "$UserNamePrefix.two.$Suffix@$MailDomain")
    , (New-SampleRow 'three' 'Sample Three' 'not-an-email-address')
)

function ConvertTo-CellXml([string] $Reference, [string] $Text) {
    $escaped = [System.Security.SecurityElement]::Escape($Text)
    return "<c r=`"$Reference`" t=`"inlineStr`"><is><t>$escaped</t></is></c>"
}

function ConvertTo-SheetXml($Rows) {
    $builder = New-Object System.Text.StringBuilder
    [void] $builder.Append("<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><worksheet xmlns=`"$MainNamespace`"><sheetData>")
    for ($rowIndex = 0; $rowIndex -lt $Rows.Count; $rowIndex++) {
        $number = $rowIndex + 1
        [void] $builder.Append("<row r=`"$number`">")
        $cells = $Rows[$rowIndex]
        for ($column = 0; $column -lt $cells.Count; $column++) {
            if ($null -ne $cells[$column]) {
                $reference = [string][char]([int][char]'A' + $column) + $number
                [void] $builder.Append((ConvertTo-CellXml $reference $cells[$column]))
            }
        }
        [void] $builder.Append('</row>')
    }
    [void] $builder.Append('</sheetData></worksheet>')
    return $builder.ToString()
}

$parts = [ordered] @{
    '[Content_Types].xml'        = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><Types xmlns=`"http://schemas.openxmlformats.org/package/2006/content-types`"><Default Extension=`"rels`" ContentType=`"application/vnd.openxmlformats-package.relationships+xml`"/><Default Extension=`"xml`" ContentType=`"application/xml`"/><Override PartName=`"/xl/workbook.xml`" ContentType=`"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml`"/><Override PartName=`"/xl/worksheets/sheet1.xml`" ContentType=`"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml`"/></Types>"
    '_rels/.rels'                = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><Relationships xmlns=`"$PackageRelationships`"><Relationship Id=`"rId1`" Type=`"$OfficeRelationships/officeDocument`" Target=`"xl/workbook.xml`"/></Relationships>"
    'xl/workbook.xml'            = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><workbook xmlns=`"$MainNamespace`" xmlns:r=`"$OfficeRelationships`"><sheets><sheet name=`"Users`" sheetId=`"1`" r:id=`"rId1`"/></sheets></workbook>"
    'xl/_rels/workbook.xml.rels' = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><Relationships xmlns=`"$PackageRelationships`"><Relationship Id=`"rId1`" Type=`"$OfficeRelationships/worksheet`" Target=`"worksheets/sheet1.xml`"/></Relationships>"
    'xl/worksheets/sheet1.xml'   = (ConvertTo-SheetXml $rows)
}

Add-Type -AssemblyName System.IO.Compression
$fullPath = [System.IO.Path]::GetFullPath($Path)
$encoding = New-Object System.Text.UTF8Encoding($false)
$file = [System.IO.File]::Open($fullPath, [System.IO.FileMode]::Create)
try {
    $zip = New-Object System.IO.Compression.ZipArchive($file, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $parts.Keys) {
            $writer = New-Object System.IO.StreamWriter($zip.CreateEntry($name).Open(), $encoding)
            try { $writer.Write($parts[$name]) } finally { $writer.Dispose() }
        }
    }
    finally { $zip.Dispose() }
}
finally { $file.Dispose() }

Write-Output "Wrote $fullPath"
foreach ($row in $rows[1..3]) { Write-Output "  $($row[0])" }
exit 0
