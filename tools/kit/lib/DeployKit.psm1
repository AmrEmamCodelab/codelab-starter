#Requires -Version 5.1
# The decisions of tools/kit/New-DeployKit.ps1: the kit name, the component selection, the layout, the payload
# filters, the SQL decision, the README expansion, the file hashes and the manifest. Every function here is pure or
# touches only the folder it is given, so tests/DeployKit.Tests.ps1 covers it without a build (MODULE.md).
Set-StrictMode -Version Latest

# --- Enums (integers with explicit values) ------------------------------------------------------------------------
enum KitPartKind {
    DotnetPublish = 1
    WebBuild = 2
}

enum KitSqlStatus {
    Included = 1
    SkippedByFlag = 2
    SkippedMissingGenerator = 3
}

# --- Constants ----------------------------------------------------------------------------------------------------
$script:KitFolderPrefix       = 'deploy-'
$script:KitFormatVersion      = 1
$script:ManifestFileName      = 'manifest.json'
$script:ReadmeFileName        = 'README.md'
$script:KitScriptsFolder      = 'kits'
$script:SqlFolder             = 'sql'
$script:SqlFilePattern        = '*.sql'
$script:ShortShaLength        = 7
$script:ShaPattern            = '^[0-9a-f]{7,40}$'
$script:ListSeparators        = [char[]] @(',', ';', ' ')
$script:TokenPattern          = '\{\{[A-Z0-9_]+\}\}'
$script:TokenFormat           = '{{{{{0}}}}}'
$script:PathSeparator         = '/'
$script:HashAlgorithm         = 'SHA256'
$script:IsoUtcFormat          = 'yyyy-MM-ddTHH:mm:ssZ'
# Where each non-payload layout entry comes from (repo-relative; shown by -DryRun).
$script:KitScriptsSource      = 'deploy/kits'
$script:SqlGeneratorSource    = 'tools/sql-kit/New-DbaSqlScripts.ps1'
$script:ReadmeTemplateSource  = 'tools/kit/templates/README.template.md'
$script:ManifestSource        = 'tools/kit (hashes of every other file)'
# The deploy/kits entries that never ship (the kit-module tests and their fixtures).
$script:KitScriptsExcluded    = @('tests')
# Files a payload never carries: development settings and local secrets (the server keeps its own settings; the kit
# templates carry the production keys). Matched against the file name, case-insensitive.
$script:PayloadExcludedNames  = @('appsettings.Development.json', 'appsettings.Development.*.json', '*.local.json')
# Files the finished kit must not hold anywhere (key material and local secrets). The kit is refused when one is found.
$script:ForbiddenKitNames     = @('*.pfx', '*.p12', '*.key', '*.pem', '*.snk', '*.local.json', 'appsettings.Development*.json', '.env')

function Get-KitConstants {
    <# The names other scripts share with this module (the builder, the README template, the tests). #>
    return [pscustomobject]@{
        KitFolderPrefix  = $script:KitFolderPrefix
        KitFormatVersion = $script:KitFormatVersion
        ManifestFileName = $script:ManifestFileName
        ReadmeFileName   = $script:ReadmeFileName
        KitScriptsFolder = $script:KitScriptsFolder
        SqlFolder        = $script:SqlFolder
        ShortShaLength   = $script:ShortShaLength
        KitScriptsSource = $script:KitScriptsSource
        SqlGenerator     = $script:SqlGeneratorSource
        ReadmeTemplate   = $script:ReadmeTemplateSource
    }
}

function Split-KitList {
    <# Splits list parameters: Windows PowerShell 5.1 passes '-Component api,web' to a -File script as one string. #>
    param([string[]] $Values)
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($value in @($Values)) {
        if ($null -eq $value) { continue }
        foreach ($part in $value.Split($script:ListSeparators, [StringSplitOptions]::RemoveEmptyEntries)) {
            $items.Add($part.Trim().ToLowerInvariant())
        }
    }
    return , $items.ToArray()
}

function Get-KitFolderName {
    <# deploy-<shortsha>: the short commit, lower case, validated. #>
    param([string] $Sha)
    $normalized = ([string] $Sha).Trim().ToLowerInvariant()
    if ($normalized -notmatch $script:ShaPattern) { throw "'$Sha' is not a git commit id (7 to 40 hex characters)." }
    return $script:KitFolderPrefix + $normalized.Substring(0, $script:ShortShaLength)
}

function Assert-KitCatalog {
    <# Checks the component catalog (kit-components.psd1): unique names, folders and orders, known part kinds. #>
    param([object[]] $Catalog)
    if (@($Catalog).Count -eq 0) { throw 'The component catalog is empty.' }
    foreach ($key in @('Name', 'Folder', 'Order')) {
        $values = @($Catalog | ForEach-Object { [string] $_[$key] })
        $duplicates = @($values | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
        if ($duplicates.Count -gt 0) { throw "The component catalog repeats $key '$($duplicates -join "', '")'." }
    }
    foreach ($component in $Catalog) {
        if (-not $component['InstallScript']) { throw "Component '$($component['Name'])' names no install script." }
        if (@($component['Parts']).Count -eq 0) { throw "Component '$($component['Name'])' has no parts." }
        foreach ($part in $component['Parts']) {
            if (-not [Enum]::IsDefined([KitPartKind], [int] $part['Kind'])) { throw "Component '$($component['Name'])' has an unknown part kind $($part['Kind'])." }
        }
    }
}

function Resolve-KitComponents {
    <#
    The components to build, in catalog order: the requested names (validated), or every Default component when
    none are requested. Unknown names throw with the known list.
    #>
    param([object[]] $Catalog, [string[]] $Requested)
    Assert-KitCatalog $Catalog
    $names = Split-KitList $Requested
    $known = @($Catalog | ForEach-Object { $_['Name'] })
    $unknown = @($names | Where-Object { $known -notcontains $_ })
    if ($unknown.Count -gt 0) { throw "Unknown component '$($unknown -join "', '")'; known: $($known -join ', ')." }
    $selected = if ($names.Count -eq 0) { @($Catalog | Where-Object { $_['Default'] }) } else { @($Catalog | Where-Object { $names -contains $_['Name'] }) }
    return , @($selected | Sort-Object { [int] $_['Order'] })
}

function Resolve-KitProject {
    <# The one .csproj matching Pattern under Root (repo-relative); throws on none or several. #>
    param([string] $RepoRoot, [string] $ProjectRoot, [string] $Pattern)
    $root = Join-Path $RepoRoot $ProjectRoot
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Project folder '$ProjectRoot' does not exist in this repository." }
    $found = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter $Pattern | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    if ($found.Count -eq 0) { throw "No project matching '$Pattern' under '$ProjectRoot'." }
    if ($found.Count -gt 1) { throw "Several projects match '$Pattern' under '$ProjectRoot': $(($found | ForEach-Object { $_.Name }) -join ', ')." }
    return $found[0].FullName
}

function Join-KitRelativePath {
    <# A kit-relative path with forward slashes (manifest form); empty parts are skipped. #>
    param([string[]] $Parts)
    return (@($Parts | Where-Object { -not [string]::IsNullOrEmpty($_) }) -join $script:PathSeparator)
}

function New-KitLayoutEntry {
    <# One layout entry. Payload entries (IsPayload) carry the catalog part they are built from. #>
    param([string] $Component = '', [string] $Path, [string] $Source, [object] $Part = $null, [bool] $IsDotnet = $false)
    return [pscustomobject]@{
        Component = $Component
        Path      = $Path
        Source    = $Source
        Part      = $Part
        IsPayload = $null -ne $Part
        IsDotnet  = $IsDotnet
    }
}

function Get-KitLayout {
    <#
    The kit's top-level layout for a selection: one entry per payload part (folder, kind, source) plus kits\, sql\
    (when the SQL step runs), README.md and manifest.json. Pure: it lists, it creates nothing.
    #>
    param([object[]] $Components, [KitSqlStatus] $SqlStatus)
    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($component in $Components) {
        foreach ($part in $component['Parts']) {
            $isDotnet = [int] $part['Kind'] -eq [int] [KitPartKind]::DotnetPublish
            $source = if ($isDotnet) { "$($part['ProjectRoot'])/$($part['ProjectPattern'])" } else { [string] $part['Workspace'] }
            $entries.Add((New-KitLayoutEntry -Component ([string] $component['Name']) -Path (Join-KitRelativePath @($component['Folder'], $part['SubFolder'])) -Source $source -Part $part -IsDotnet $isDotnet))
        }
    }
    $entries.Add((New-KitLayoutEntry -Path $script:KitScriptsFolder -Source $script:KitScriptsSource))
    if ($SqlStatus -eq [KitSqlStatus]::Included) { $entries.Add((New-KitLayoutEntry -Path $script:SqlFolder -Source $script:SqlGeneratorSource)) }
    $entries.Add((New-KitLayoutEntry -Path $script:ReadmeFileName -Source $script:ReadmeTemplateSource))
    $entries.Add((New-KitLayoutEntry -Path $script:ManifestFileName -Source $script:ManifestSource))
    return , $entries.ToArray()
}

function Get-KitSqlDecision {
    <# Whether the DBA scripts are generated: -SkipSql wins, then a missing generator skips (with the reason). #>
    param([bool] $SkipSql, [bool] $GeneratorExists, [string] $GeneratorPath)
    if ($SkipSql) { return [pscustomobject]@{ Status = [KitSqlStatus]::SkippedByFlag; Included = $false; Reason = 'skipped (-SkipSql)' } }
    if (-not $GeneratorExists) {
        return [pscustomobject]@{ Status = [KitSqlStatus]::SkippedMissingGenerator; Included = $false; Reason = "$GeneratorPath is not in this repository; the DBA scripts are not in the kit" }
    }
    return [pscustomobject]@{ Status = [KitSqlStatus]::Included; Included = $true; Reason = '' }
}

function Test-KitNameMatch {
    param([string] $Name, [string[]] $Patterns)
    foreach ($pattern in $Patterns) { if ($Name -like $pattern) { return $true } }
    return $false
}

function Test-KitPayloadExcluded {
    <# True for a file a payload never carries (development settings, local secrets). #>
    param([string] $FileName)
    return Test-KitNameMatch -Name $FileName -Patterns $script:PayloadExcludedNames
}

function Test-KitScriptsEntryExcluded {
    <# True for a top-level deploy/kits entry that never ships (its tests). #>
    param([string] $EntryName)
    return $script:KitScriptsExcluded -contains $EntryName
}

function Find-KitForbiddenFiles {
    <# The kit-relative paths whose file name is key material or a local secret (the kit is refused when any). #>
    param([string[]] $RelativePaths)
    return , @(@($RelativePaths) | Where-Object { $_ -and (Test-KitNameMatch -Name ($_ -split '[\\/]')[-1] -Patterns $script:ForbiddenKitNames) })
}

function Get-KitFileHashes {
    <# SHA-256 of every file under KitRoot except manifest.json: [{ path, sha256, bytes }], sorted by path (ordinal). #>
    param([string] $KitRoot)
    $root = (Resolve-Path -LiteralPath $KitRoot).ProviderPath.TrimEnd('\', '/')
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.FullName.Substring($root.Length + 1) -ne $script:ManifestFileName })
    $entries = foreach ($file in $files) {
        [pscustomobject][ordered]@{
            path   = $file.FullName.Substring($root.Length + 1).Replace('\', $script:PathSeparator)
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm $script:HashAlgorithm).Hash.ToLowerInvariant()
            bytes  = $file.Length
        }
    }
    $sorted = New-Object System.Collections.Generic.List[object]
    foreach ($entry in @($entries)) { $sorted.Add($entry) }
    $sorted.Sort([Comparison[object]] { param($left, $right) [string]::CompareOrdinal($left.path, $right.path) })
    return , $sorted.ToArray()
}

function ConvertTo-KitOrderedMap {
    <# A ConvertFrom-Json object as an ordered hashtable (keeps packages/versions.json order in the manifest). #>
    param([object] $Value)
    $map = [ordered]@{}
    if ($null -eq $Value) { return $map }
    foreach ($property in $Value.PSObject.Properties) { $map[$property.Name] = $property.Value }
    return $map
}

function New-KitManifest {
    <#
    The manifest.json object: format, kit name, commit, dirty flag, creation time (UTC, passed in), the package
    versions (packages/versions.json), the components with their folders and install scripts, the SQL decision and
    the file hashes.
    #>
    param(
        [string] $Commit,
        [bool] $Dirty,
        [datetime] $CreatedUtc,
        [object] $Versions,
        [object[]] $Components,
        [object] $Sql,
        [object[]] $Files
    )
    $componentEntries = @(foreach ($component in $Components) {
        [ordered]@{
            name          = [string] $component['Name']
            folder        = [string] $component['Folder']
            installScript = Join-KitRelativePath @($script:KitScriptsFolder, $component['InstallScript'])
        }
    })
    $sqlEntry = [ordered]@{ status = [string] $Sql.Status; statusValue = [int] $Sql.Status; folder = ''; files = 0; reason = [string] $Sql.Reason }
    if ($Sql.Status -eq [KitSqlStatus]::Included) {
        $sqlFiles = @($Files | Where-Object { $_.path -like "$($script:SqlFolder)$($script:PathSeparator)$($script:SqlFilePattern)" })
        $sqlEntry.folder = $script:SqlFolder
        $sqlEntry.files = $sqlFiles.Count
    }
    return [ordered]@{
        kitFormat    = $script:KitFormatVersion
        name         = Get-KitFolderName $Commit
        commit       = $Commit.Trim().ToLowerInvariant()
        dirty        = $Dirty
        createdAtUtc = $CreatedUtc.ToUniversalTime().ToString($script:IsoUtcFormat, [Globalization.CultureInfo]::InvariantCulture)
        packages     = ConvertTo-KitOrderedMap $Versions
        components   = $componentEntries
        sql          = $sqlEntry
        # The pipeline unrolls a list passed as one item (a comma-returned array wrapped in @()).
        files        = @(@($Files) | ForEach-Object { $_ })
    }
}

function Test-KitOwnedFolder {
    <# True when Folder holds a manifest.json this builder wrote (kitFormat set): the only folder -Force replaces. #>
    param([string] $Folder)
    $manifest = Join-Path $Folder $script:ManifestFileName
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { return $false }
    try { $parsed = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json } catch { return $false }
    return ($null -ne $parsed) -and ($parsed.PSObject.Properties.Name -contains 'kitFormat')
}

function Expand-KitTemplate {
    <# Replaces every {{TOKEN}} of Values in Template; throws when a token is left (a template and builder drift). #>
    param([string] $Template, [hashtable] $Values)
    $text = $Template
    foreach ($key in $Values.Keys) { $text = $text.Replace(($script:TokenFormat -f $key), [string] $Values[$key]) }
    $left = @([regex]::Matches($text, $script:TokenPattern) | ForEach-Object { $_.Value } | Select-Object -Unique)
    if ($left.Count -gt 0) { throw "The README template has tokens without a value: $($left -join ', ')." }
    return $text
}

function Get-KitReadmeValues {
    <# The README template values: kit name, commit, time, components table, install order, SQL and package lines. #>
    param([object] $Manifest, [object[]] $Components)
    $rows = @(foreach ($component in $Components) { "| ``$($component['Folder'])\`` | $($component['Name']) | ``kits\$($component['InstallScript'])`` |" })
    $order = New-Object System.Collections.Generic.List[string]
    $step = 1
    if ($Manifest.sql.status -eq [string] [KitSqlStatus]::Included) { $order.Add("$step. The DBA runs the scripts in ``sql\`` in file-name order (each is idempotent)."); $step++ }
    foreach ($component in $Components) {
        $order.Add("$step. ``kits\$($component['InstallScript']) -PackagePath .\$($component['Folder']) ... -WhatIf``, then the same command without ``-WhatIf``.")
        $step++
    }
    $sql = if ($Manifest.sql.status -eq [string] [KitSqlStatus]::Included) { "``sql\`` holds $($Manifest.sql.files) script(s) generated by ``tools/sql-kit/New-DbaSqlScripts.ps1``." } else { "No DBA scripts in this kit: $($Manifest.sql.reason)." }
    $packages = @(foreach ($key in $Manifest.packages.Keys) { "- ``$key`` $($Manifest.packages[$key])" })
    return @{
        KIT_NAME      = $Manifest.name
        COMMIT        = $Manifest.commit
        DIRTY         = $(if ($Manifest.dirty) { 'yes: not for production' } else { 'no' })
        CREATED_UTC   = $Manifest.createdAtUtc
        COMPONENT_ROWS = ($rows -join "`n")
        INSTALL_ORDER = ($order -join "`n")
        SQL_SECTION   = $sql
        PACKAGES      = ($packages -join "`n")
    }
}

function ConvertTo-KitJsonText {
    <# Indented JSON with LF line endings (stable across Windows PowerShell and pwsh). #>
    param([object] $Value)
    return ((ConvertTo-Json -InputObject $Value -Depth 8) -replace "`r`n", "`n") + "`n"
}

Export-ModuleMember -Function Get-KitConstants, Split-KitList, Get-KitFolderName, Assert-KitCatalog, Resolve-KitComponents,
    Resolve-KitProject, Join-KitRelativePath, Get-KitLayout, Get-KitSqlDecision, Test-KitPayloadExcluded,
    Test-KitScriptsEntryExcluded, Find-KitForbiddenFiles, Get-KitFileHashes, New-KitManifest, Test-KitOwnedFolder,
    Expand-KitTemplate, Get-KitReadmeValues, ConvertTo-KitJsonText
