<#
.SYNOPSIS
    Turns a fresh clone of codelab-starter into a project fork, in one recorded commit (ADR-002, ADR-003), and sets the
    project branding.

.DESCRIPTION
    Full init (default; ADR-003), run inside the fork clone on a clean work tree: asks for the project name (English,
    Arabic), code prefix, logo and primary colour, login modes (basic, ad, sso) and the optional modules to keep
    (attachments, portal, sms, ops) - or reads the same fields from -ConfigFile - then runs five steps, each skipped
    with a notice when it is already done (a rerun continues a partial init):
      1 rename Starter to the project names (toolsorklibename-defaults.mjs), prefix, branding; commit.
      2 remove the unticked optional modules, packages and switch to the pinned feed versions; commit.
      3 local keys: toolsdevseedNew-LocalSecrets.ps1 -Force (-SkipSecrets skips).
      4 local database: toolsdevseedInitialize-LocalDatabase.ps1 (-SkipDatabase or database.skip skips).
      5 Auth:Modes in the API appsettings.json and codelab.starter.json loginModes, optionalModules, hashes; commit.
    -WhatIf prints the plan and changes nothing. Config file shape (toolsorkREADME.md):
      { "prefix": "ACME", "nameEn": "...", "nameAr": "...", "primaryColor": "#rrggbb", "logoPath": null,
        "loginModes": ["basic"], "optionalModules": { "attachments": true, "portal": true, "sms": true, "ops": true },
        "database": { "connectionString": null, "skip": true } }

    -Minimal (Phase 2, kept for the fork-model tests): run it once, inside the fork clone, on a clean work tree with npm dependencies installed (`npm ci`; the rules
    compiler needs them). It refuses when codelab.starter.json already has a starterBase.

    Steps: record starterBase = HEAD; remove each -RemoveModule through tools\remove-module.ps1; capture
    packages\versions.json and remove packages\; switch to feed mode (UseCodelabPackageSource defaults to false,
    Codelab.* pinned in Directory.Packages.props, @codelab/* npm dependencies pointed at file: tarballs in the fork's
    feed\npm, the needed .nupkg/.tgz copied from -FeedDir into the fork's feed\, whose contents are gitignored);
    apply -RenameMap; set -ProductPrefix in every codelab.starter.json prefixTargets entry; regenerate the rules
    outputs; refresh package-lock.json; run tools\devseed\New-LocalSecrets.ps1 when it exists; hash the source-owned
    files; write codelab.starter.json; commit 'init: minimal fork (prefix <P>, removed <names>)'.

    Branding (phase 4): -PrimaryColor, -LogoPath, -NameEn and -NameAr write the "branding" block of
    frontend\starter-web\public\config.json, which @codelab/ng-dga applies at run time (--dga-brand-* colours, logo,
    name). A parameter left out keeps the value already in the block; the first branding run must give the colour and
    both names. -LogoPath copies the image to frontend\starter-web\public\branding\logo<ext> (logoSrc
    'branding/logo<ext>'). The rest of config.json keeps its formatting.
    - With -Minimal: the branding is applied after the fork commit succeeds, and committed as 'init: branding (<NameEn>)'.
    - Without -Minimal: only the branding is written (no fork step, no commit), so a fork can re-brand later.

    Exit codes: 0 done, 1 refused (nothing changed: bad prefix, required module, already initialized, dirty tree,
    missing feed versions or targets, invalid branding), 2 usage or internal error.

.PARAMETER ConfigFile
    The full init's answers as JSON (non-interactive, for CI). A relative logoPath is resolved against the file's folder.

.PARAMETER WhatIf
    Print the full init's planned steps and change nothing.

.PARAMETER SkipDatabase
    Skip the full init's local database step.

.PARAMETER Minimal
    The scripted, non-interactive fork init.

.PARAMETER ProductPrefix
    The product prefix: 2-16 characters, A-Z first, then A-Z or 0-9 (STARTER, CODELABTEST and ALTPFX are reserved).

.PARAMETER RemoveModule
    Module names to remove (comma-separated or repeated). Identity and Access are required and refused.

.PARAMETER FeedDir
    Folder holding nuget\ and npm\ with the packed Codelab packages. Default: the repo's feed\. When a
    packages\versions.json version is missing, its highest <version>-ci.<n> prerelease is pinned instead.

.PARAMETER RenameMap
    Optional JSON file: [{ "from": "Starter", "to": "Acme" }, ...]. Default: identity (no rename).

.PARAMETER SkipSecrets
    Do not run tools\devseed\New-LocalSecrets.ps1.

.PARAMETER SkipNpmLock
    Do not refresh package-lock.json (npm install --package-lock-only) after the npm manifests change.

.PARAMETER AllowMissingTargets
    Skip, with a notice, prefix targets and npm manifests that do not exist (for repos without a frontend).

.PARAMETER PrimaryColor
    Brand primary colour as hex, #RGB or #RRGGBB (stored in lower case). Hover, active and tint shades are derived.

.PARAMETER LogoPath
    A logo image file (.svg, .png, .webp, .jpg or .jpeg, at most 1 MB) to copy into the web app.

.PARAMETER NameEn
    The product name in English (1-120 characters).

.PARAMETER NameAr
    The product name in Arabic (1-120 characters).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1 -ConfigFile C:ciinit.json -WhatIf

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1 -Minimal -ProductPrefix ACME -RemoveModule Demo -FeedDir E:\codelab-starter\feed

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1 -PrimaryColor '#0a5c8f' -LogoPath C:\brand\logo.svg -NameEn 'Acme Portal' -NameAr <the Arabic name>
#>
[CmdletBinding()]
param(
    [string] $ConfigFile,
    [switch] $WhatIf,
    [switch] $SkipDatabase,
    [switch] $Minimal,
    [string] $ProductPrefix,
    [string[]] $RemoveModule = @(),
    [string] $FeedDir,
    [string] $RenameMap,
    [switch] $SkipSecrets,
    [switch] $SkipNpmLock,
    [switch] $AllowMissingTargets,
    [string] $PrimaryColor,
    [string] $LogoPath,
    [string] $NameEn,
    [string] $NameAr
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\fork\lib\ForkCommon.ps1')

$BrandingExitRefused       = 1
$BrandingConfigRelative    = 'frontend\starter-web\public\config.json'
$BrandingLogoDirRelative   = 'frontend\starter-web\public\branding'
$BrandingLogoUrlFolder     = 'branding'
$BrandingLogoStem          = 'logo'
$BrandingLogoExtensions    = @('.svg', '.png', '.webp', '.jpg', '.jpeg')
$BrandingMaxLogoBytes      = 1MB
$BrandingMaxNameLength     = 120
$BrandingColorPattern      = '^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$'
$BrandingJsonLibRelative   = 'tools\fork\lib\prefix.mjs'
$BrandingEnvConfig         = 'CODELAB_BRANDING_CONFIG'
$BrandingEnvLib            = 'CODELAB_BRANDING_LIB'
$BrandingEnvValues         = 'CODELAB_BRANDING_VALUES'

# Merges the given branding values into the config.json "branding" block with tools\fork\lib\prefix.mjs setJsonValue
# (keeps indentation, line endings, BOM and final newline). Exit 1 names the fields a first branding run still needs.
$BrandingWriterScript = "import { readFileSync, writeFileSync } from 'node:fs'; import { pathToFileURL } from 'node:url'; " +
    "const { setJsonValue } = await import(pathToFileURL(process.env.$BrandingEnvLib).href); " +
    "const file = process.env.$BrandingEnvConfig; const given = JSON.parse(process.env.$BrandingEnvValues); " +
    "const text = readFileSync(file, 'utf8'); const old = JSON.parse(text.replace(/^\uFEFF/, '')).branding ?? {}; " +
    "const merged = { primaryColor: given.primaryColor ?? old.primaryColor, logoSrc: given.logoSrc ?? old.logoSrc ?? '', " +
    "appName: { ar: given.ar ?? old.appName?.ar, en: given.en ?? old.appName?.en } }; " +
    "const missing = [['-PrimaryColor', merged.primaryColor], ['-NameAr', merged.appName.ar], ['-NameEn', merged.appName.en]].filter(([, value]) => !value).map(([name]) => name); " +
    "if (missing.length > 0) { console.error('Branding refused: config.json has no branding yet; give ' + missing.join(', ') + '.'); process.exit(1); } " +
    "writeFileSync(file, setJsonValue(text, 'branding', merged).text); " +
    "console.log('Branding written to config.json: ' + merged.primaryColor + ', ' + merged.appName.en + ' / ' + merged.appName.ar + (merged.logoSrc ? ', logo ' + merged.logoSrc : ', no logo') + '.');"

function Test-BrandingRequested {
    return [bool]($PrimaryColor -or $LogoPath -or $NameEn -or $NameAr)
}

# Why the branding parameters are refused, or $null when they are valid. Only the given parameters are checked.
function Get-BrandingRefusal {
    if ($PrimaryColor -and $PrimaryColor -notmatch $BrandingColorPattern) {
        return "-PrimaryColor '$PrimaryColor' is not a hex colour (#RGB or #RRGGBB)."
    }
    foreach ($name in @(@{ Parameter = '-NameEn'; Value = $NameEn }, @{ Parameter = '-NameAr'; Value = $NameAr })) {
        if ($name.Value) {
            $trimmed = "$($name.Value)".Trim()
            if (-not $trimmed -or $trimmed.Length -gt $BrandingMaxNameLength) {
                return "$($name.Parameter) must be 1-$BrandingMaxNameLength characters."
            }
        }
    }
    if ($LogoPath) {
        if (-not (Test-Path -LiteralPath $LogoPath -PathType Leaf)) { return "-LogoPath '$LogoPath' is not a file." }
        $extension = [IO.Path]::GetExtension($LogoPath).ToLowerInvariant()
        if ($BrandingLogoExtensions -notcontains $extension) {
            return "-LogoPath must be one of $($BrandingLogoExtensions -join ', ') (got '$extension')."
        }
        if ((Get-Item -LiteralPath $LogoPath).Length -gt $BrandingMaxLogoBytes) { return '-LogoPath is larger than 1 MB.' }
    }
    $config = Join-Path $PSScriptRoot $BrandingConfigRelative
    if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { return "Branding target $BrandingConfigRelative is missing." }
    return $null
}

# Copies the logo into the web app and returns its logoSrc, or $null when no logo was given.
function Copy-BrandingLogo {
    if (-not $LogoPath) { return $null }
    $extension = [IO.Path]::GetExtension($LogoPath).ToLowerInvariant()
    $folder = Join-Path $PSScriptRoot $BrandingLogoDirRelative
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $fileName = "$BrandingLogoStem$extension"
    Copy-Item -LiteralPath $LogoPath -Destination (Join-Path $folder $fileName) -Force
    return "$BrandingLogoUrlFolder/$fileName"
}

# Writes the branding block; returns the exit code (0 written, 1 refused, 2 internal error).
function Invoke-Branding {
    $refusal = Get-BrandingRefusal
    if ($refusal) {
        Write-Host $refusal -ForegroundColor Red
        return $BrandingExitRefused
    }
    try { $node = Use-ForkNode }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        return $ForkExitUsage
    }
    $values = [ordered]@{}
    if ($PrimaryColor) { $values.primaryColor = $PrimaryColor.ToLowerInvariant() }
    if ($NameEn) { $values.en = $NameEn.Trim() }
    if ($NameAr) { $values.ar = $NameAr.Trim() }
    $logoSrc = Copy-BrandingLogo
    if ($logoSrc) { $values.logoSrc = $logoSrc }

    [Environment]::SetEnvironmentVariable($BrandingEnvConfig, (Join-Path $PSScriptRoot $BrandingConfigRelative))
    [Environment]::SetEnvironmentVariable($BrandingEnvLib, (Join-Path $PSScriptRoot $BrandingJsonLibRelative))
    [Environment]::SetEnvironmentVariable($BrandingEnvValues, (ConvertTo-Json -InputObject $values -Compress))
    & $node --input-type=module -e $BrandingWriterScript | Out-Host
    return $LASTEXITCODE
}

# Commits the branding files after a -Minimal fork commit.
function Save-BrandingCommit {
    $paths = @($BrandingConfigRelative)
    if ($LogoPath) { $paths += $BrandingLogoDirRelative }
    & git -C $PSScriptRoot add -- @paths | Out-Host
    if ($LASTEXITCODE -ne 0) { return $ForkExitUsage }
    $label = $NameEn
    if (-not $label) { $label = 'updated' }
    & git -C $PSScriptRoot commit --quiet -m "init: branding ($label)" | Out-Host
    if ($LASTEXITCODE -ne 0) { return $ForkExitUsage }
    return 0
}

$brandingRequested = Test-BrandingRequested
$fullInitOnly = [bool]($ConfigFile -or $WhatIf -or $SkipDatabase)
if ($Minimal -and $fullInitOnly) {
    Write-Host '-ConfigFile, -WhatIf and -SkipDatabase belong to the full init, not -Minimal.' -ForegroundColor Red
    exit $ForkExitUsage
}
if ($brandingRequested -and $fullInitOnly) {
    Write-Host 'The full init takes the branding from the config (or the prompts), not -PrimaryColor/-LogoPath/-NameEn/-NameAr.' -ForegroundColor Red
    exit $ForkExitUsage
}
if ($brandingRequested -and -not $Minimal) {
    exit (Invoke-Branding)
}
$minimalOnly = [bool]($ProductPrefix -or $RenameMap -or $AllowMissingTargets -or @(Split-ForkList $RemoveModule).Count -gt 0)
if ($minimalOnly -and -not $Minimal) {
    Write-Host '-ProductPrefix, -RemoveModule, -RenameMap and -AllowMissingTargets belong to -Minimal; the full init reads -ConfigFile or asks.' -ForegroundColor Red
    exit $ForkExitUsage
}
if (-not $Minimal) {
    $arguments = @('init', '--root', $PSScriptRoot)
    if ($ConfigFile) {
        if (-not (Test-Path -LiteralPath $ConfigFile -PathType Leaf)) {
            Write-Host "-ConfigFile '$ConfigFile' is not a file." -ForegroundColor Red
            exit $BrandingExitRefused
        }
        $arguments += @('--config-file', (Resolve-Path -LiteralPath $ConfigFile).Path)
    }
    else { $arguments += '--interactive' }
    if ($WhatIf) { $arguments += '--what-if' }
    if ($SkipDatabase) { $arguments += '--skip-database' }
    if ($SkipSecrets) { $arguments += '--skip-secrets' }
    if ($SkipNpmLock) { $arguments += '--skip-npm-lock' }
    if ($FeedDir) { $arguments += @('--feed-dir', $FeedDir) }
    Invoke-ForkCli $PSScriptRoot $arguments
    exit $LASTEXITCODE
}
if ($brandingRequested) {
    $refusal = Get-BrandingRefusal
    if ($refusal) {
        Write-Host $refusal -ForegroundColor Red
        exit $BrandingExitRefused
    }
}

$arguments = @('init', '--root', $PSScriptRoot)
if ($Minimal) { $arguments += '--minimal' }
if ($ProductPrefix) { $arguments += @('--product-prefix', $ProductPrefix) }
foreach ($name in (Split-ForkList $RemoveModule)) { $arguments += @('--remove-module', $name) }
if ($FeedDir) { $arguments += @('--feed-dir', $FeedDir) }
if ($RenameMap) { $arguments += @('--rename-map', $RenameMap) }
if ($SkipSecrets) { $arguments += '--skip-secrets' }
if ($SkipNpmLock) { $arguments += '--skip-npm-lock' }
if ($AllowMissingTargets) { $arguments += '--allow-missing-targets' }

Invoke-ForkCli $PSScriptRoot $arguments
$forkExit = $LASTEXITCODE
if ($forkExit -ne 0 -or -not $brandingRequested) { exit $forkExit }

$brandingExit = Invoke-Branding
if ($brandingExit -ne 0) { exit $brandingExit }
exit (Save-BrandingCommit)
