# Prerequisites (d). Decision (ADR-008, settled in P10): the .NET SDK, Node.js and Playwright browser installers are
# LISTED, not shipped. deploy/handover/prerequisites.json is the catalog: exact version, file name, download URL and
# SHA-256 per installer. A kit build refuses a catalog that drifts from the source pins (global.json, .nvmrc, the
# lock file), so PREREQUISITES.md never names a version the source tree does not build with.

function Get-KitJsonProperty([object] $Object, [string] $Name) {
    if ($null -ne $Object -and $Object.PSObject.Properties.Name -contains $Name) { return $Object.$Name }
    return $null
}

# The pinned .NET SDK from global.json: @{ Version; RollForward } (nulls when global.json has no sdk block).
function Get-KitDotnetSdkPin([string] $SourceRoot) {
    $path = Join-Path $SourceRoot $script:GlobalJsonFile
    if (-not (Test-Path -LiteralPath $path)) { return [pscustomobject]@{ Version = $null; RollForward = $null } }
    $sdk = Get-KitJsonProperty (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json) $script:GlobalJsonSdkKey
    return [pscustomobject]@{ Version = (Get-KitJsonProperty $sdk $script:GlobalJsonVersionKey); RollForward = (Get-KitJsonProperty $sdk $script:GlobalJsonRollForwardKey) }
}

function Get-KitNodePin([string] $SourceRoot) {
    $path = Join-Path $SourceRoot $script:NvmrcFile
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return (Get-Content -LiteralPath $path -Raw -Encoding UTF8).Trim()
}

function Format-KitValue([object] $Value) {
    if ($null -eq $Value -or "$Value" -eq '') { return 'not pinned' }
    return "$Value"
}

# Reads the catalog (default: deploy/handover/prerequisites.json). Shape problems are usage errors (exit 2).
function Read-KitPrerequisites([string] $Path) {
    if (-not $Path) { $Path = $script:PrerequisitesCatalogFile }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Stop-KitUsage "Prerequisites catalog '$Path' not found." }
    $catalog = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $problems = @(Get-KitPrerequisitesShapeProblems $catalog)
    if ($problems.Count -gt 0) { Stop-KitUsage "Prerequisites catalog '$Path' is invalid: $($problems -join '; ')." }
    return $catalog
}

# Shape rules: schema version, the 'listed' decision, and per item an id, a version and files with an https URL
# and a lower-case SHA-256.
function Get-KitPrerequisitesShapeProblems([object] $Catalog) {
    $problems = [Collections.Generic.List[string]]::new()
    if ((Get-KitJsonProperty $Catalog 'schemaVersion') -ne $script:PrerequisitesSchemaVersion) {
        $problems.Add("schemaVersion must be $($script:PrerequisitesSchemaVersion)")
    }
    if ((Get-KitJsonProperty $Catalog 'decision') -ne $script:PrerequisitesDecisionListed) {
        $problems.Add("decision must be '$($script:PrerequisitesDecisionListed)'")
    }
    foreach ($item in @(Get-KitJsonProperty $Catalog 'prerequisites')) {
        if ($null -eq $item) { continue }
        $id = Get-KitJsonProperty $item 'id'
        if (-not $id) { $problems.Add('an item has no id'); continue }
        if (-not (Get-KitJsonProperty $item 'version')) { $problems.Add("$id has no version") }
        foreach ($file in @(Get-KitJsonProperty $item 'files')) {
            if ($null -eq $file) { continue }
            $name = Get-KitJsonProperty $file 'fileName'
            if (-not $name) { $problems.Add("$id has a file with no fileName"); continue }
            if ("$(Get-KitJsonProperty $file 'sha256')" -cnotmatch $script:Sha256HexPattern) { $problems.Add("$name has no lower-case SHA-256") }
            if (-not "$(Get-KitJsonProperty $file 'url')".StartsWith($script:HttpsScheme)) { $problems.Add("$name has no https URL") }
        }
    }
    return $problems.ToArray()
}

# True when the catalog's Node version satisfies the .nvmrc pin ('24' matches 24.x.y; 'v24.22.2' matches exactly).
function Test-KitNodeVersionMatch([string] $Version, [string] $Pin) {
    $want = $Pin.TrimStart($script:NodeVersionPrefix)
    $have = $Version.TrimStart($script:NodeVersionPrefix)
    return ($have -eq $want -or $have.StartsWith($want + $script:VersionSeparator))
}

# Drift between the catalog and the source pins. $LockRoots: full paths of the kit's npm install roots.
function Get-KitPrerequisitePinProblems([object] $Catalog, [string] $SourceRoot, [string[]] $LockRoots) {
    $problems = [Collections.Generic.List[string]]::new()
    foreach ($item in @($Catalog.prerequisites)) {
        $pin = Get-KitJsonProperty $item 'pin'
        if ($pin -eq $script:PrerequisitePinGlobalJson) {
            $sdk = (Get-KitDotnetSdkPin $SourceRoot).Version
            if ($sdk -and $sdk -ne $item.version) { $problems.Add("$($item.id) $($item.version) differs from $($script:GlobalJsonFile) $sdk") }
        }
        elseif ($pin -eq $script:PrerequisitePinNvmrc) {
            $node = Get-KitNodePin $SourceRoot
            if ($node -and -not (Test-KitNodeVersionMatch $item.version $node)) { $problems.Add("$($item.id) $($item.version) does not satisfy $($script:NvmrcFile) $node") }
        }
        elseif ($pin -eq $script:PrerequisitePinLockPackage) {
            $key = $script:NpmNodeModulesPrefix + $item.lockPackage
            foreach ($root in @($LockRoots | Where-Object { $_ })) {
                $packages = Get-KitLockPackages $root
                if (-not $packages.ContainsKey($key)) { continue }
                $locked = $packages[$key][$script:NpmLockVersionKey]
                if ($locked -ne $item.version) { $problems.Add("$($item.id) $($item.version) differs from $($item.lockPackage) $locked in $(Format-KitInstallRoot (Get-KitRelativePath $root $SourceRoot))") }
            }
        }
    }
    return $problems.ToArray()
}

function Format-KitInstallerRows([object] $Catalog) {
    $rows = @()
    foreach ($item in @($Catalog.prerequisites)) {
        $files = @(Get-KitJsonProperty $item 'files' | Where-Object { $null -ne $_ })
        if ($files.Count -eq 0) {
            $rows += "| $($item.name) | $($item.version) | - | - | $(Get-KitJsonProperty $item 'note') |"
            continue
        }
        foreach ($file in $files) { $rows += "| $($item.name) | $($item.version) | ``$($file.fileName)`` | ``$($file.sha256)`` | $($file.url) |" }
    }
    return $rows
}

# $Info: DotnetPin, DotnetUsed, NodePin, NodeUsed, NpmUsed, Playwright (string[]). $Catalog: Read-KitPrerequisites.
function New-KitPrerequisitesText([pscustomobject] $Info, [object] $Catalog) {
    $playwright = $script:NoPlaywrightText
    if (@($Info.Playwright).Count -gt 0) { $playwright = (@($Info.Playwright) -join ', ') }
    $lines = @(
        '# Prerequisites', '',
        'Install these on the build machine before using the kit (generated by deploy/handover/New-HandoverKit.ps1).', '',
        '| Tool | Required | Kit built with |',
        '| --- | --- | --- |',
        "| .NET SDK | $(Format-KitValue $Info.DotnetPin.Version) (global.json, rollForward $(Format-KitValue $Info.DotnetPin.RollForward)) | $(Format-KitValue $Info.DotnetUsed) |",
        "| Node.js | $(Format-KitValue $Info.NodePin) ($($script:NvmrcFile)) | $(Format-KitValue $Info.NodeUsed) |",
        "| npm | bundled with Node.js | $(Format-KitValue $Info.NpmUsed) |",
        "| Playwright | $playwright | - |", '',
        '## Installers (listed, not shipped)', '',
        "$($Catalog.decisionNote) Check a download with ``(Get-FileHash <file> -Algorithm SHA256).Hash`` before installing; the same data is in ``$($script:KitPrerequisitesDataFile)``.", '',
        '| Tool | Version | File | SHA-256 | Download / note |',
        '| --- | --- | --- | --- | --- |')
    $lines += @(Format-KitInstallerRows $Catalog)
    return ($lines -join "`n") + "`n"
}

# Differences between the listed installers and the tools on this machine (warnings: rollForward may accept a later
# SDK, but the kit was proved with the listed one). A $null installed version means the tool was not found.
function Get-KitInstalledPrerequisiteWarnings([object] $Catalog, [string] $DotnetVersion, [string] $NodeVersion) {
    $warnings = [Collections.Generic.List[string]]::new()
    foreach ($item in @($Catalog.prerequisites)) {
        $pin = Get-KitJsonProperty $item 'pin'
        $installed = $null
        if ($pin -eq $script:PrerequisitePinGlobalJson) { $installed = $DotnetVersion }
        elseif ($pin -eq $script:PrerequisitePinNvmrc) { $installed = $NodeVersion }
        else { continue }
        if (-not $installed) { $warnings.Add("$($item.name) $($item.version) is not installed."); continue }
        if ($installed.Trim().TrimStart($script:NodeVersionPrefix) -ne $item.version) {
            $warnings.Add("$($item.name) $($item.version) is listed; this machine has $($installed.Trim()).")
        }
    }
    return $warnings.ToArray()
}
