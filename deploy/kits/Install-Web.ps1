#Requires -Version 5.1
<#
.SYNOPSIS
    Installs or updates the SPA front site (starter-web) on IIS, with its security edge.

.DESCRIPTION
    The web kit, built on Install-Common.psm1 (MODULE.md in this folder). The steps, in order:

      1. Checks the parameters and the package (refuses before anything changes). The package is the built SPA (the
         folder holding index.html) or a kit folder with it in web\.
      2. Build id: -BuildId, else the kit manifest.json next to web\, else a digest of the package files.
      3. Prerequisites: IIS, WebAdministration and the IIS URL Rewrite module (no .NET runtime: the site is static).
      4. Settings forecast: config.json as it will be after the merge (server values win). Every required key that is
         still empty, and every API or attachment origin that is not https, is a gap.
      5. IIS: app pool ("No Managed Code") and the site, via Set-KitIisApplication (an existing site is re-pointed at
         -TargetPath; a new one needs -Port).
      6. Backup of the deployed folder.
      7. config.json: Merge-AppSettings (server values kept, only new keys added; a first install gets the package's
         file with the environment values emptied). With gaps the kit stops here and exits 2: the new build is not
         deployed (the running one keeps serving) until an operator fills them.
      8. web.config: the kit template (templates\web\web.config), with connect-src and the CSP report target adapted
         to config.json's origins, merged into the server's web.config. Elements the server has (a header, a rule,
         a location) are kept as they are and reported when they differ; only new elements are added. The edge
         block must stay ahead of every rewrite rule, or the kit refuses.
      9. Payload copy (config.json, web.config and build-info.json are kit-owned and never copied). Old hashed bundles
         are kept so open sessions can still load their lazy chunks; -Purge mirrors instead.
     10. build-info.json stamped with the build id.
     11. Health over validated TLS (never skipped validation): GET / is 200 with the security headers, build-info.json
         carries the build id, and /api/internal/* and /api/ops/* answer 404 through the front site.

    Under -WhatIf nothing is written: no IIS change, folder, backup, copy, settings file, web.config, stamp or log
    file. Every planned action is printed instead.

    Exit codes (when run as a script): 0 done (or previewed), 1 refused or failed, 2 settings incomplete (not deployed).

.PARAMETER PackagePath
    The built SPA folder (index.html at its root), or a kit folder with that output in web\.
.PARAMETER SiteName
    The IIS site the SPA is the root of. An existing site is re-pointed at -TargetPath and -AppPoolName.
.PARAMETER AppPoolName
    The app pool. Default 'StarterWeb'.
.PARAMETER TargetPath
    The folder IIS serves the SPA from. Default <SystemDrive>\inetpub\starter-web.
.PARAMETER BackupRoot
    Where backups and the install log go. Default <SystemDrive>\inetpub\backups\starter-web.
.PARAMETER Port
    Only to create a new site (its http binding; the https binding and certificate are an operator step).
.PARAMETER HealthBaseUrl
    The site's public https base URL, as this server reaches it (required for an install). Its certificate must
    validate for that name.
.PARAMETER BuildId
    The build id to stamp (letters, digits, '.', '_' and '-'; up to 64). Default: from the kit manifest or the package.
.PARAMETER Purge
    Mirror the package (delete files it no longer has, old hashed bundles included).

.EXAMPLE
    .\Install-Web.ps1 -PackagePath .\deploy-abc1234 -SiteName 'Starter Portal' -HealthBaseUrl 'https://portal.example.test' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $PackagePath,
    [string] $SiteName,
    [string] $AppPoolName,
    [string] $TargetPath,
    [string] $BackupRoot,
    [int] $Port,
    [string] $HealthBaseUrl,
    [string] $BuildId,
    [switch] $Purge
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Enums (integers with explicit values) ------------------------------------------------------------------------
enum WebHealthOutcome {
    NotRun = 0
    SkippedPreview = 1
    SkippedSettingsIncomplete = 2
    Healthy = 3
    Unhealthy = 4
}

enum WebBuildIdSource {
    None = 0
    Parameter = 1
    Manifest = 2
    Content = 3
}

enum WebConfigOutcome {
    NotRun = 0
    Unchanged = 1
    Created = 2
    Patched = 3
}

# --- Constants ----------------------------------------------------------------------------------------------------
$script:CommonModuleFileName        = 'Install-Common.psm1'
$script:WebTemplateRelative         = 'templates\web\web.config'
$script:ConfigTemplateRelative      = 'templates\web\config.template.json'
$script:HeadersDataRelative         = 'templates\web\security-headers.psd1'
$script:BackupName                  = 'web'
$script:LogFileName                 = 'install-web.log'

$script:DefaultAppPoolName          = 'StarterWeb'
$script:DefaultTargetRelative       = 'inetpub\starter-web'
$script:DefaultBackupRelative       = 'inetpub\backups\starter-web'
$script:SiteAppPath                 = '/'

# The package: the SPA build output, directly or in web\ of a kit folder (with the kit's manifest.json beside it).
$script:PackageWebFolder            = 'web'
$script:IndexFileName               = 'index.html'
$script:ConfigFileName              = 'config.json'
$script:WebConfigFileName           = 'web.config'
$script:BuildInfoFileName           = 'build-info.json'
$script:KitManifestFileName         = 'manifest.json'
$script:ManifestBuildIdProperties   = @('buildId', 'shortSha', 'commit')
$script:KitOwnedFiles               = @('config.json', 'web.config', 'build-info.json')

# Build id.
$script:BuildIdPattern              = '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$'
$script:ContentDigestLength         = 12
$script:HexByteFormat               = 'x2'
$script:DigestEntrySeparator        = ':'
$script:DigestLineSeparator         = "`n"

# config.json (paths in .NET configuration form; array items as :0, :1, ...).
$script:ConfigSeparator             = ':'
$script:RequiredConfigPaths         = @('apiBaseUrl', 'origins:api', 'origins:attachment', 'security:protocolPrefix')
$script:ApiBaseUrlPath              = 'apiBaseUrl'
$script:OriginListPaths             = @('origins:api', 'origins:attachment')
$script:SecureScheme                = 'https'
$script:InsecureScheme              = 'http'
$script:SameOriginPrefix            = '/'
$script:JsonWriteDepth              = 32
$script:EmptyContainer              = New-Object -TypeName psobject
$script:TempTemplateExtension       = '.json'

# web.config merge: an element with one of these attributes is identified by it and kept whole; any other element is
# a singleton under its parent and merged child by child.
$script:XmlKeyAttributes            = @('name', 'fileExtension', 'path', 'key', 'statusCode')
$script:XmlPathSeparator            = '/'
$script:XmlRootName                 = 'configuration'
$script:XmlDeclarationPrefix        = '<?xml'
$script:RulesXPath                  = '/configuration/system.webServer/rewrite/rules/rule'
$script:CustomHeaderXPathFormat     = "/configuration/system.webServer/httpProtocol/customHeaders/add[@name='{0}']"
$script:EdgeBlockRuleName           = 'Block internal and ops API'
$script:ForwardingActionTypes       = @('Rewrite', 'Redirect')
$script:ActionXPath                 = 'action'
$script:ReportingEndpointsHeader    = 'Reporting-Endpoints'
$script:ReportingEndpointFormat     = '{0}="{1}"'
$script:CspDirectiveSeparator       = ';'
$script:CspTokenSeparator           = ' '
$script:CspConnectDirective         = 'connect-src'
$script:CspReportUriDirective       = 'report-uri'

# Paths: a drive-rooted folder (C:\x) or a UNC share (\\fileserver.example.test\share).
$script:DriveRootedPattern          = '^[A-Za-z]:\\'
$script:UncPattern                  = '^\\\\[^\\]+\\[^\\]+'
$script:PathSeparator               = '\'
$script:InvalidPoolNameChars        = [char[]] '\/:*?"<>|'
$script:UrlSeparator                = '/'

# Prerequisites (the static site needs no ASP.NET Core runtime, so Test-KitPrerequisites is not used).
$script:IisRegistryPath             = 'HKLM:\SOFTWARE\Microsoft\InetStp'
$script:IisMajorVersionValue        = 'MajorVersion'
$script:IisServiceName              = 'W3SVC'
$script:WebAdministrationModule     = 'WebAdministration'
$script:UrlRewriteModuleRelative    = 'System32\inetsrv\rewrite.dll'
$script:CheckAdministrator          = 'Administrator'
$script:CheckIis                    = 'IIS'
$script:CheckWebAdministration      = 'WebAdministration'
$script:CheckUrlRewrite             = 'UrlRewrite'

# Health probe (validated TLS only: no certificate callback is ever installed).
$script:HealthRootPath              = '/'
$script:HealthBlockedProbePaths     = @('/api/internal/edge-probe', '/api/ops/health')
$script:HealthAttempts              = 6
$script:HealthDelaySeconds          = 5
$script:HealthRequestTimeoutSeconds = 15
$script:HttpOk                      = 200
$script:NoHttpStatus                = 0
$script:Tls12                       = [Net.SecurityProtocolType]::Tls12
$script:Tls13Name                   = 'Tls13'
$script:TlsFailureStatuses          = @([Net.WebExceptionStatus]::TrustFailure, [Net.WebExceptionStatus]::SecureChannelFailure)

$script:ExitSettingsIncomplete      = 2

# Write-KitLog levels (KitLogLevel member names in Install-Common.psm1).
$script:LogInfo                     = 'Info'
$script:LogStep                     = 'Step'
$script:LogSuccess                  = 'Success'
$script:LogWarning                  = 'Warning'
$script:LogError                    = 'Error'

$script:KitRoot                     = $PSScriptRoot
$script:KitLogPath                  = $null
$script:Utf8NoBom                   = New-Object Text.UTF8Encoding($false)

Import-Module (Join-Path $PSScriptRoot $script:CommonModuleFileName) -Force -Verbose:$false

# ==================================================================================================================
# Logging and the planned-action list
# ==================================================================================================================
# Install-Common's Write-KitLog does not see this script's -WhatIf, so the log file is dropped here under -WhatIf.
function Write-WebLog([string] $Message, [string] $Level = $script:LogInfo) {
    $logPath = if ($WhatIfPreference) { $null } else { $script:KitLogPath }
    Write-KitLog -Message $Message -Level $Level -LogPath $logPath -WhatIf:$false
}

function Add-WebAction([object] $Result, [string] $Text) {
    $Result.PlannedActions.Add($Text)
    Write-WebLog $Text -Level $script:LogStep
}

# ==================================================================================================================
# Parameters, paths and the package (pure checks; they throw before anything changes)
# ==================================================================================================================
function Test-WebFullyQualifiedPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return ($Path -match $script:DriveRootedPattern -or $Path -match $script:UncPattern)
}

# True when $Path is $Parent itself or anything below it (case-insensitive, like NTFS).
function Test-WebPathInside([string] $Path, [string] $Parent) {
    $child = [IO.Path]::GetFullPath($Path).TrimEnd($script:PathSeparator)
    $root = [IO.Path]::GetFullPath($Parent).TrimEnd($script:PathSeparator)
    if ([string]::Equals($child, $root, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $child.StartsWith($root + $script:PathSeparator, [StringComparison]::OrdinalIgnoreCase)
}

# An absolute https URL with no query, fragment or credentials; returns the parsed Uri or $null.
function ConvertTo-WebHttpsUri([string] $Value) {
    $parsed = $null
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    if (-not [Uri]::TryCreate($Value.Trim(), [UriKind]::Absolute, [ref] $parsed)) { return $null }
    if ($parsed.Scheme -ne $script:SecureScheme -or $parsed.Query -or $parsed.Fragment -or $parsed.UserInfo) { return $null }
    return $parsed
}

function Assert-WebKitParameters {
    param([string] $PackagePath, [string] $SiteName, [string] $AppPoolName, [string] $TargetPath, [string] $BackupRoot,
          [string] $HealthBaseUrl, [string] $BuildId, [bool] $Preview)
    if ([string]::IsNullOrWhiteSpace($PackagePath)) { throw '-PackagePath is required: the built SPA folder (index.html) or the kit folder with web\. Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($SiteName)) { throw '-SiteName is required: the IIS site the SPA is the root of. Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($AppPoolName) -or $AppPoolName.IndexOfAny($script:InvalidPoolNameChars) -ge 0) {
        throw "-AppPoolName '$AppPoolName' is not a valid app pool name. Nothing was changed."
    }
    if (-not (Test-WebFullyQualifiedPath $TargetPath)) { throw "-TargetPath '$TargetPath' must be a fully qualified folder. Nothing was changed." }
    if (-not (Test-WebFullyQualifiedPath $BackupRoot)) { throw "-BackupRoot '$BackupRoot' must be a fully qualified folder. Nothing was changed." }
    if ((Test-WebPathInside -Path $BackupRoot -Parent $TargetPath) -or (Test-WebPathInside -Path $TargetPath -Parent $BackupRoot)) {
        throw "-BackupRoot '$BackupRoot' and -TargetPath '$TargetPath' overlap; a backup would copy itself or be served. Nothing was changed."
    }
    if (-not [string]::IsNullOrWhiteSpace($BuildId) -and $BuildId -notmatch $script:BuildIdPattern) {
        throw "-BuildId '$BuildId' is not a build id (letters, digits, '.', '_' and '-', up to 64). Nothing was changed."
    }
    if ([string]::IsNullOrWhiteSpace($HealthBaseUrl)) {
        if (-not $Preview) { throw '-HealthBaseUrl is required: the public https URL of the site (its certificate must validate for that name). Nothing was changed.' }
        return
    }
    if ($null -eq (ConvertTo-WebHttpsUri $HealthBaseUrl)) {
        throw "-HealthBaseUrl '$HealthBaseUrl' must be an absolute https URL (no query or credentials): the health check runs over validated TLS only. Nothing was changed."
    }
}

# Returns the folder holding the SPA build: the package itself or its web\ folder.
function Resolve-WebPackage([string] $PackagePath) {
    if (-not (Test-Path -LiteralPath $PackagePath -PathType Container)) { throw "Package folder '$PackagePath' does not exist. Nothing was changed." }
    foreach ($candidate in @($PackagePath, (Join-Path $PackagePath $script:PackageWebFolder))) {
        if (Test-Path -LiteralPath (Join-Path $candidate $script:IndexFileName) -PathType Leaf) { return (Resolve-Path -LiteralPath $candidate).ProviderPath }
    }
    throw "No $($script:IndexFileName) in '$PackagePath' or its $($script:PackageWebFolder)\ folder; give the built SPA. Nothing was changed."
}

# ==================================================================================================================
# Build id
# ==================================================================================================================
function Get-WebSha256Hex([byte[]] $Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = $sha.ComputeHash($Bytes) } finally { $sha.Dispose() }
    return (-join ($digest | ForEach-Object { $_.ToString($script:HexByteFormat) }))
}

# A digest of the payload (relative path + file hash, sorted), so the same build always gets the same id.
function Get-WebContentBuildId([string] $WebFolder) {
    $root = $WebFolder.TrimEnd($script:PathSeparator) + $script:PathSeparator
    $lines = Get-ChildItem -LiteralPath $WebFolder -Recurse -File -Force |
        Where-Object { $script:KitOwnedFiles -notcontains $_.Name } |
        ForEach-Object { $_.FullName.Substring($root.Length).ToLowerInvariant() + $script:DigestEntrySeparator + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } |
        Sort-Object
    $text = @($lines) -join $script:DigestLineSeparator
    return (Get-WebSha256Hex ($script:Utf8NoBom.GetBytes($text))).Substring(0, $script:ContentDigestLength)
}

function Get-WebManifestBuildId([string] $WebFolder) {
    $manifestPath = Join-Path (Split-Path -Parent $WebFolder) $script:KitManifestFileName
    $manifest = Read-WebJson $manifestPath
    if ($null -eq $manifest) { return $null }
    foreach ($name in $script:ManifestBuildIdProperties) {
        $property = $manifest.PSObject.Properties[$name]
        if ($null -ne $property -and $property.Value -is [string] -and $property.Value -match $script:BuildIdPattern) { return $property.Value }
    }
    return $null
}

function Resolve-WebBuildId([string] $Requested, [string] $WebFolder) {
    if (-not [string]::IsNullOrWhiteSpace($Requested)) { return [pscustomobject]@{ Id = $Requested; Source = [WebBuildIdSource]::Parameter } }
    $fromManifest = Get-WebManifestBuildId $WebFolder
    if ($fromManifest) { return [pscustomobject]@{ Id = $fromManifest; Source = [WebBuildIdSource]::Manifest } }
    return [pscustomobject]@{ Id = (Get-WebContentBuildId $WebFolder); Source = [WebBuildIdSource]::Content }
}

# ==================================================================================================================
# config.json: read, overlay the kit template, layer like the merge will, find the gaps
# ==================================================================================================================
function Read-WebJson([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $text = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).ProviderPath, $script:Utf8NoBom)
    # The parser error is not passed on: it quotes file text.
    try { return (ConvertFrom-Json -InputObject $text -ErrorAction Stop) }
    catch { throw "'$Path' is not valid JSON (comments and trailing commas are not supported). Nothing was changed." }
}

function Test-WebJsonObject([object] $Value) {
    return ($null -ne $Value -and $Value -is [System.Management.Automation.PSCustomObject])
}

function Test-WebJsonArray([object] $Value) {
    return ($null -ne $Value -and $Value -isnot [string] -and $Value -is [System.Collections.IList])
}

# Copies every key of $Overlay onto $Target (sections merged key by key, values and arrays replaced whole).
function Set-WebJsonOverlay([object] $Target, [object] $Overlay) {
    foreach ($member in @($Overlay.PSObject.Properties)) {
        $existing = $Target.PSObject.Properties[$member.Name]
        if ($null -ne $existing -and (Test-WebJsonObject $existing.Value) -and (Test-WebJsonObject $member.Value)) {
            Set-WebJsonOverlay -Target $existing.Value -Overlay $member.Value
            continue
        }
        if ($null -eq $existing) { $Target | Add-Member -NotePropertyName $member.Name -NotePropertyValue $member.Value }
        else { $existing.Value = $member.Value }
    }
}

# The merge source for config.json: the package's file (it knows every key this build reads) with the environment
# values of the kit template emptied, so a development URL never reaches a server. Written to a temp file.
function New-WebConfigMergeTemplate([string] $PackageConfigPath, [string] $KitTemplatePath) {
    $kitTemplate = Read-WebJson $KitTemplatePath
    if ($null -eq $kitTemplate) { throw "The kit template '$KitTemplatePath' is missing. Nothing was changed." }
    $merged = Read-WebJson $PackageConfigPath
    if ($null -eq $merged) { $merged = [pscustomobject]@{} }
    Set-WebJsonOverlay -Target $merged -Overlay $kitTemplate
    $path = [IO.Path]::Combine([IO.Path]::GetTempPath(), [IO.Path]::GetRandomFileName() + $script:TempTemplateExtension)
    [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $merged -Depth $script:JsonWriteDepth), $script:Utf8NoBom)
    return $path
}

# Flattens a JSON object into "a:b:0" -> value, like .NET configuration keys; an empty object or array is recorded
# as the EmptyContainer marker so a required list that is present but empty is still seen.
function Add-WebSettingLeaves([object] $Node, [string] $Prefix, [hashtable] $Into) {
    if (Test-WebJsonObject $Node) {
        $members = @($Node.PSObject.Properties)
        if ($members.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        foreach ($member in $members) {
            $path = if ($Prefix) { $Prefix + $script:ConfigSeparator + $member.Name } else { $member.Name }
            Add-WebSettingLeaves -Node $member.Value -Prefix $path -Into $Into
        }
        return
    }
    if (Test-WebJsonArray $Node) {
        if ($Node.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        for ($index = 0; $index -lt $Node.Count; $index++) { Add-WebSettingLeaves -Node $Node[$index] -Prefix ($Prefix + $script:ConfigSeparator + $index) -Into $Into }
        return
    }
    $Into[$Prefix] = $Node
}

# Layers are lowest precedence first: the merge template, then the server file (the server always wins).
function Get-WebEffectiveSettings([object[]] $Layers) {
    $leaves = @{}
    foreach ($layer in @($Layers | Where-Object { $null -ne $_ })) { Add-WebSettingLeaves -Node $layer -Prefix '' -Into $leaves }
    return $leaves
}

function Get-WebSettingValues([hashtable] $Effective, [string] $Path) {
    $below = $Path + $script:ConfigSeparator
    $keys = @($Effective.Keys | Where-Object {
        ($_ -eq $Path -or $_.StartsWith($below, [StringComparison]::OrdinalIgnoreCase)) -and -not [object]::ReferenceEquals($Effective[$_], $script:EmptyContainer)
    } | Sort-Object)
    return $keys
}

function Test-WebEmptyValue([object] $Value) {
    if ($null -eq $Value) { return $true }
    return ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value))
}

# The API base may be a same-origin path ('/' or '/api'); any absolute value, and every listed origin, must be https.
function Test-WebSecureEndpoint([string] $Value, [bool] $AllowSameOriginPath) {
    if ($AllowSameOriginPath -and $Value.StartsWith($script:SameOriginPrefix) -and -not $Value.StartsWith($script:SameOriginPrefix + $script:SameOriginPrefix)) { return $true }
    return ($null -ne (ConvertTo-WebHttpsUri $Value))
}

<#
    Returns the config.json paths that are missing, empty or not secure (sorted). A required list counts as filled only
    with at least one item and no empty item. Values are never returned, only paths and the reason.
#>
function Get-WebSettingsGaps([hashtable] $Effective) {
    $gaps = New-Object 'System.Collections.Generic.List[string]'
    foreach ($required in $script:RequiredConfigPaths) {
        $keys = @(Get-WebSettingValues -Effective $Effective -Path $required)
        if ($keys.Count -eq 0) { $gaps.Add("$required (empty)"); continue }
        foreach ($key in $keys) { if (Test-WebEmptyValue $Effective[$key]) { $gaps.Add("$key (empty)") } }
    }
    $endpoints = @(Get-WebSettingValues -Effective $Effective -Path $script:ApiBaseUrlPath | ForEach-Object { @{ Key = $_; SameOrigin = $true } })
    foreach ($listPath in $script:OriginListPaths) { $endpoints += @(Get-WebSettingValues -Effective $Effective -Path $listPath | ForEach-Object { @{ Key = $_; SameOrigin = $false } }) }
    foreach ($endpoint in $endpoints) {
        $value = $Effective[$endpoint.Key]
        if (Test-WebEmptyValue $value) { continue }
        if (-not (Test-WebSecureEndpoint -Value ([string] $value) -AllowSameOriginPath $endpoint.SameOrigin)) { $gaps.Add("$($endpoint.Key) (must be an https origin$(if ($endpoint.SameOrigin) { ' or a same-origin path' }))") }
    }
    return [string[]] @($gaps | Sort-Object -Unique)
}

# ==================================================================================================================
# web.config: adapt the CSP to config.json, merge into the server file, check the edge block order
# ==================================================================================================================
function Read-WebXmlText([string] $Text, [string] $What) {
    $document = New-Object Xml.XmlDocument
    $document.PreserveWhitespace = $true
    $document.XmlResolver = $null
    # The parser error is not passed on: it quotes file text.
    try { $document.LoadXml($Text) }
    catch { throw "$What is not well-formed XML; IIS would answer 500.19 on every page. Fix it (the backup holds the original). Nothing was changed." }
    if ($null -eq $document.DocumentElement -or $document.DocumentElement.LocalName -ne $script:XmlRootName) { throw "$What has no <$($script:XmlRootName)> root. Nothing was changed." }
    return $document
}

function Get-WebOrigin([string] $Value) {
    $parsed = ConvertTo-WebHttpsUri $Value
    if ($null -eq $parsed) { return $null }
    return $parsed.GetLeftPart([UriPartial]::Authority)
}

# The https origins the SPA calls other than the site's own: the absolute API base and the api/attachment lists.
function Get-WebCrossOrigins([hashtable] $Effective, [string] $SiteOrigin) {
    $values = @()
    foreach ($path in @($script:ApiBaseUrlPath) + $script:OriginListPaths) { $values += @(Get-WebSettingValues -Effective $Effective -Path $path | ForEach-Object { [string] $Effective[$_] }) }
    $origins = @($values | ForEach-Object { Get-WebOrigin $_ } | Where-Object { $_ -and -not [string]::Equals($_, $SiteOrigin, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -Unique)
    return [string[]] $origins
}

# The CSP report target: the template's same-origin path, or the absolute API base + that path when the API is elsewhere.
function Get-WebReportUrl([hashtable] $Effective, [string] $ReportPath) {
    $apiBase = if ($Effective.ContainsKey($script:ApiBaseUrlPath)) { [string] $Effective[$script:ApiBaseUrlPath] } else { '' }
    if ($null -eq (ConvertTo-WebHttpsUri $apiBase)) { return $ReportPath }
    return $apiBase.Trim().TrimEnd($script:UrlSeparator) + $ReportPath
}

# Adds $Origins to connect-src and points report-uri at $ReportUrl; every other directive is left as written.
function Update-WebCspValue([string] $Value, [string[]] $Origins, [string] $ReportUrl) {
    $directives = foreach ($directive in $Value.Split($script:CspDirectiveSeparator)) {
        $tokens = @($directive.Trim().Split($script:CspTokenSeparator, [StringSplitOptions]::RemoveEmptyEntries))
        if ($tokens.Count -eq 0) { continue }
        if ($tokens[0] -eq $script:CspConnectDirective) { $tokens = @($tokens + @($Origins | Where-Object { $_ -and $tokens -notcontains $_ })) }
        if ($tokens[0] -eq $script:CspReportUriDirective) { $tokens = @($script:CspReportUriDirective, $ReportUrl) }
        $tokens -join $script:CspTokenSeparator
    }
    return (@($directives) -join ($script:CspDirectiveSeparator + $script:CspTokenSeparator))
}

# The kit template with the CSP adapted to config.json (same origin: unchanged). $SiteOrigin is the site's own public
# origin (from -HealthBaseUrl): listing it in config.json adds nothing, 'self' already covers it.
function Resolve-WebTemplateText([string] $TemplateText, [hashtable] $Effective, [hashtable] $HeaderData, [string] $SiteOrigin) {
    $document = Read-WebXmlText -Text $TemplateText -What 'The kit web.config template'
    $origins = @(Get-WebCrossOrigins -Effective $Effective -SiteOrigin $SiteOrigin)
    $reportUrl = Get-WebReportUrl -Effective $Effective -ReportPath $HeaderData.ReportPath
    $csp = $document.SelectSingleNode(($script:CustomHeaderXPathFormat -f $HeaderData.CspHeaderName))
    if ($null -ne $csp) { $csp.SetAttribute('value', (Update-WebCspValue -Value $csp.GetAttribute('value') -Origins $origins -ReportUrl $reportUrl)) }
    $endpoints = $document.SelectSingleNode(($script:CustomHeaderXPathFormat -f $script:ReportingEndpointsHeader))
    if ($null -ne $endpoints) { $endpoints.SetAttribute('value', ($script:ReportingEndpointFormat -f $HeaderData.ReportGroup, $reportUrl)) }
    return $document.OuterXml
}

function Get-WebXmlKeyAttribute([Xml.XmlElement] $Element) {
    foreach ($name in $script:XmlKeyAttributes) { if ($Element.HasAttribute($name)) { return $name } }
    return $null
}

# "add[@name='X-Frame-Options']" for a keyed element, the bare name for a singleton.
function Get-WebXmlKey([Xml.XmlElement] $Element) {
    $attribute = Get-WebXmlKeyAttribute $Element
    if ($null -eq $attribute) { return $Element.LocalName }
    return "$($Element.LocalName)[@$attribute='$($Element.GetAttribute($attribute))']"
}

function Get-WebXmlChildElements([Xml.XmlNode] $Parent) {
    return @($Parent.ChildNodes | Where-Object { $_.NodeType -eq [Xml.XmlNodeType]::Element })
}

function Find-WebXmlMatch([Xml.XmlNode] $ServerParent, [string] $Key) {
    foreach ($child in (Get-WebXmlChildElements $ServerParent)) { if ((Get-WebXmlKey $child) -ceq $Key) { return $child } }
    return $null
}

# Element text without layout whitespace, to tell a real difference from indentation.
function Get-WebXmlComparable([Xml.XmlElement] $Element) {
    return ([regex]::Replace($Element.OuterXml, '>\s+<', '><')).Trim()
}

function Copy-WebXmlIndent([Xml.XmlNode] $Node) {
    $previous = $Node.PreviousSibling
    if ($null -ne $previous -and $previous.NodeType -eq [Xml.XmlNodeType]::Whitespace) { return $Node.OwnerDocument.CreateWhitespace($previous.Value) }
    return $null
}

# Inserts $Imported after $After (the previous template sibling the server has), or before $Before, or at the end.
function Add-WebXmlElement([Xml.XmlNode] $Parent, [Xml.XmlNode] $Imported, [Xml.XmlNode] $After, [Xml.XmlNode] $Before) {
    if ($null -ne $After) {
        $indent = Copy-WebXmlIndent $After
        if ($null -ne $indent) { $After = $Parent.InsertAfter($indent, $After) }
        [void] $Parent.InsertAfter($Imported, $After)
        return
    }
    if ($null -ne $Before) {
        $indent = Copy-WebXmlIndent $Before
        [void] $Parent.InsertBefore($Imported, $Before)
        if ($null -ne $indent) { [void] $Parent.InsertBefore($indent, $Before) }
        return
    }
    [void] $Parent.AppendChild($Imported)
}

<#
    Adds to $ServerParent every template child it lacks, in template order. A keyed element (a header, a rule, a mime
    map, a location) the server has is kept whole; a difference is listed in $Kept. A singleton section is merged child
    by child, and gets the attributes it lacks.
#>
function Merge-WebXmlChildren([Xml.XmlNode] $ServerParent, [Xml.XmlNode] $TemplateParent, [string] $Path,
                              [Collections.Generic.List[string]] $Added, [Collections.Generic.List[string]] $Kept) {
    $templateChildren = @(Get-WebXmlChildElements $TemplateParent)
    $last = $null
    for ($index = 0; $index -lt $templateChildren.Count; $index++) {
        $templateChild = $templateChildren[$index]
        $key = Get-WebXmlKey $templateChild
        $childPath = $Path + $script:XmlPathSeparator + $key
        $match = Find-WebXmlMatch -ServerParent $ServerParent -Key $key
        if ($null -eq $match) {
            $before = $null
            for ($next = $index + 1; $next -lt $templateChildren.Count -and $null -eq $before; $next++) { $before = Find-WebXmlMatch -ServerParent $ServerParent -Key (Get-WebXmlKey $templateChildren[$next]) }
            $imported = $ServerParent.OwnerDocument.ImportNode($templateChild, $true)
            Add-WebXmlElement -Parent $ServerParent -Imported $imported -After $last -Before $before
            $Added.Add($childPath)
            $last = $imported
            continue
        }
        $last = $match
        if ($null -ne (Get-WebXmlKeyAttribute $templateChild)) {
            if ((Get-WebXmlComparable $match) -cne (Get-WebXmlComparable $templateChild)) { $Kept.Add($childPath) }
            continue
        }
        foreach ($attribute in @($templateChild.Attributes)) {
            if ($match.HasAttribute($attribute.Name)) { continue }
            $match.SetAttribute($attribute.Name, $attribute.Value)
            $Added.Add("$childPath@$($attribute.Name)")
        }
        Merge-WebXmlChildren -ServerParent $match -TemplateParent $templateChild -Path $childPath -Added $Added -Kept $Kept
    }
}

<#
    Pure: merges the (resolved) kit template into the server's web.config text. Returns Text (the server text itself
    when nothing is new, byte for byte), Added and Kept (element paths), and Changed.
#>
function Merge-WebConfigText([string] $TemplateText, [string] $ServerText) {
    $template = Read-WebXmlText -Text $TemplateText -What 'The kit web.config template'
    $server = Read-WebXmlText -Text $ServerText -What "The server's web.config"
    $added = New-Object 'System.Collections.Generic.List[string]'
    $kept = New-Object 'System.Collections.Generic.List[string]'
    Merge-WebXmlChildren -ServerParent $server.DocumentElement -TemplateParent $template.DocumentElement -Path $script:XmlRootName -Added $added -Kept $kept
    $text = if ($added.Count -gt 0) { $server.OuterXml } else { $ServerText }
    return [pscustomobject]@{ Text = $text; Added = [string[]] $added.ToArray(); Kept = [string[]] $kept.ToArray(); Changed = ($added.Count -gt 0) }
}

# URL Rewrite stops at the first matching rule: the edge block must exist and sit above every forwarding rule.
function Assert-WebEdgeRuleOrder([string] $Text) {
    $document = Read-WebXmlText -Text $Text -What 'The merged web.config'
    $rules = @($document.SelectNodes($script:RulesXPath))
    $blockIndex = -1
    for ($index = 0; $index -lt $rules.Count; $index++) { if ($rules[$index].GetAttribute('name') -eq $script:EdgeBlockRuleName) { $blockIndex = $index; break } }
    if ($blockIndex -lt 0) { throw "The web.config has no '$($script:EdgeBlockRuleName)' rewrite rule, so /api/internal and /api/ops would reach the API from the internet. Nothing was changed." }
    for ($index = 0; $index -lt $blockIndex; $index++) {
        $action = $rules[$index].SelectSingleNode($script:ActionXPath)
        if ($null -ne $action -and $script:ForwardingActionTypes -contains $action.GetAttribute('type')) {
            throw "The server's web.config rule '$($rules[$index].GetAttribute('name'))' sits above '$($script:EdgeBlockRuleName)'; URL Rewrite stops at the first match, so a blocked path could be forwarded. Move the block rule to the top. Nothing was changed."
        }
    }
}

# ==================================================================================================================
# Prerequisites (pure check over facts; the facts are read-only)
# ==================================================================================================================
function Get-WebPrerequisiteFacts {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $iisKey = Get-ItemProperty -Path $script:IisRegistryPath -Name $script:IisMajorVersionValue -ErrorAction SilentlyContinue
    return [pscustomobject]@{
        IsAdministrator          = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        IisPresent               = ($null -ne $iisKey -and $null -ne (Get-Service -Name $script:IisServiceName -ErrorAction SilentlyContinue))
        WebAdministrationPresent = ($null -ne (Get-Module -ListAvailable -Name $script:WebAdministrationModule))
        UrlRewritePresent        = (Test-Path -LiteralPath (Join-Path $env:windir $script:UrlRewriteModuleRelative))
    }
}

function Test-WebPrerequisites([object] $Facts, [bool] $RequireAdministrator) {
    $checks = New-Object 'System.Collections.Generic.List[object]'
    if ($RequireAdministrator) { $checks.Add([pscustomobject]@{ Name = $script:CheckAdministrator; Passed = [bool] $Facts.IsAdministrator; Detail = 'Run the kit from an elevated (Run as administrator) PowerShell.' }) }
    $checks.Add([pscustomobject]@{ Name = $script:CheckIis; Passed = [bool] $Facts.IisPresent; Detail = 'Install the Web Server (IIS) role with the W3SVC service.' })
    $checks.Add([pscustomobject]@{ Name = $script:CheckWebAdministration; Passed = [bool] $Facts.WebAdministrationPresent; Detail = 'Install the IIS Management Scripts and Tools feature (WebAdministration module).' })
    $checks.Add([pscustomobject]@{ Name = $script:CheckUrlRewrite; Passed = [bool] $Facts.UrlRewritePresent; Detail = 'Install the IIS URL Rewrite module 2.1; without it the web.config answers 500.19.' })
    foreach ($check in $checks) {
        if ($check.Passed) { Write-WebLog "Prerequisite $($check.Name): present." -Level $script:LogSuccess }
        else { Write-WebLog "Prerequisite $($check.Name) missing: $($check.Detail)" -Level $script:LogError }
    }
    return [pscustomobject]@{ Passed = (@($checks | Where-Object { -not $_.Passed }).Count -eq 0); Checks = [object[]] $checks.ToArray() }
}

# ==================================================================================================================
# Health (validated TLS)
# ==================================================================================================================
function Enable-WebModernTls {
    $protocols = [Net.ServicePointManager]::SecurityProtocol -bor $script:Tls12
    if ([Enum]::GetNames([Net.SecurityProtocolType]) -contains $script:Tls13Name) { $protocols = $protocols -bor [Net.SecurityProtocolType]::$($script:Tls13Name) }
    [Net.ServicePointManager]::SecurityProtocol = $protocols
}

function ConvertTo-WebHeaderTable([object] $Headers) {
    $table = @{}
    if ($null -eq $Headers) { return $table }
    foreach ($name in @($Headers.Keys)) { $table[[string] $name] = [string] $Headers[$name] }
    return $table
}

# One GET. Certificate validation is the platform's: a name or chain problem is a failure, never bypassed.
function Invoke-WebHttpRequest([string] $Url) {
    Enable-WebModernTls
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $script:HealthRequestTimeoutSeconds -MaximumRedirection 0
        return [pscustomobject]@{ StatusCode = [int] $response.StatusCode; Headers = (ConvertTo-WebHeaderTable $response.Headers); Body = [string] $response.Content; Error = $null }
    }
    catch [System.Net.WebException] {
        $response = $_.Exception.Response
        if ($null -eq $response) {
            $reason = if ($script:TlsFailureStatuses -contains $_.Exception.Status) { "TLS validation failed ($($_.Exception.Message)); fix the certificate chain or use the name on the certificate" } else { $_.Exception.Message }
            return [pscustomobject]@{ StatusCode = $script:NoHttpStatus; Headers = @{}; Body = ''; Error = $reason }
        }
        $reader = New-Object IO.StreamReader($response.GetResponseStream())
        try { $body = $reader.ReadToEnd() } finally { $reader.Dispose(); $response.Close() }
        $headers = @{}
        foreach ($name in @($response.Headers.AllKeys)) { $headers[$name] = $response.Headers[$name] }
        return [pscustomobject]@{ StatusCode = [int] $response.StatusCode; Headers = $headers; Body = $body; Error = $null }
    }
}

function Join-WebUrl([string] $BaseUrl, [string] $Path) {
    return $BaseUrl.Trim().TrimEnd($script:UrlSeparator) + $Path
}

function Wait-WebRoot([string] $Url) {
    $probe = $null
    for ($attempt = 1; $attempt -le $script:HealthAttempts; $attempt++) {
        $probe = Invoke-WebHttpRequest $Url
        if ($probe.StatusCode -eq $script:HttpOk) { return $probe }
        if ($attempt -lt $script:HealthAttempts) { Start-Sleep -Seconds $script:HealthDelaySeconds }
    }
    return $probe
}

<#
    Pure over the probes: returns the failed checks (strings) and the warnings. The root must answer 200 with every
    header of security-headers.psd1 (a different value is a warning: the server keeps its own); build-info.json must
    carry the stamped id; each blocked path must answer 404.
#>
function Get-WebHealthFindings([object] $Root, [object] $BuildInfo, [object[]] $Blocked, [string] $ExpectedBuildId, [hashtable] $HeaderData) {
    $failures = New-Object 'System.Collections.Generic.List[string]'
    $warnings = New-Object 'System.Collections.Generic.List[string]'
    if ($Root.StatusCode -ne $script:HttpOk) { $failures.Add("GET / answered $($Root.StatusCode)$(if ($Root.Error) { " ($($Root.Error))" })") }
    else {
        foreach ($header in $HeaderData.Headers) {
            if (-not $Root.Headers.ContainsKey($header.Name)) { $failures.Add("GET / has no $($header.Name) header") }
            elseif ($Root.Headers[$header.Name] -cne $header.Value) { $warnings.Add("$($header.Name) differs from the kit value (the server's own is kept)") }
        }
    }
    $stamped = $null
    if ($BuildInfo.StatusCode -eq $script:HttpOk) { try { $stamped = (ConvertFrom-Json -InputObject $BuildInfo.Body -ErrorAction Stop).buildId } catch { $stamped = $null } }
    if ($stamped -cne $ExpectedBuildId) { $failures.Add("$($script:BuildInfoFileName) does not carry build id '$ExpectedBuildId' (HTTP $($BuildInfo.StatusCode))") }
    foreach ($probe in $Blocked) {
        if ($probe.StatusCode -ne $HeaderData.BlockedStatusCode) { $failures.Add("GET $($probe.Path) answered $($probe.StatusCode), expected $($HeaderData.BlockedStatusCode) (edge block)") }
    }
    return [pscustomobject]@{ Failures = [string[]] $failures.ToArray(); Warnings = [string[]] $warnings.ToArray() }
}

# ==================================================================================================================
# Steps
# ==================================================================================================================
function New-WebKitResult([bool] $Preview, [string] $TargetPath) {
    return [pscustomobject]@{
        Preview = $Preview; WebFolder = $null; TargetPath = $TargetPath; BuildId = $null; BuildIdSource = [int] [WebBuildIdSource]::None
        PrerequisitesPassed = $false; IisPlan = @(); BackupPath = $null; Settings = $null; SettingsGaps = [string[]] @()
        WebConfig = [int] [WebConfigOutcome]::NotRun; WebConfigAdded = [string[]] @(); WebConfigKept = [string[]] @()
        Payload = $null; BuildInfoWritten = $false; Health = [int] [WebHealthOutcome]::NotRun; HealthFailures = [string[]] @(); HealthWarnings = [string[]] @()
        PlannedActions = (New-Object 'System.Collections.Generic.List[string]')
    }
}

function Invoke-WebPrerequisiteStep([object] $Result) {
    $check = Test-WebPrerequisites -Facts (Get-WebPrerequisiteFacts) -RequireAdministrator (-not $Result.Preview)
    $Result.PrerequisitesPassed = [bool] $check.Passed
    if ($check.Passed) { return }
    if (-not $Result.Preview) { throw 'Prerequisites are missing (see the lines above). Nothing was changed.' }
    Write-WebLog 'Prerequisites are missing; a real run stops here. The preview continues.' -Level $script:LogWarning
}

function Invoke-WebIisStep([object] $Result, [string] $SiteName, [string] $AppPoolName, [int] $Port) {
    $plan = @(Set-KitIisApplication -SiteName $SiteName -AppPath $script:SiteAppPath -AppPoolName $AppPoolName -PhysicalPath $Result.TargetPath -Port $Port -WhatIf:$Result.Preview)
    $Result.IisPlan = $plan
    foreach ($step in $plan) { Add-WebAction $Result "IIS $($step.Action) $($step.Target): $(@($step.Changes) -join '; ')" }
}

function Invoke-WebConfigJsonStep([object] $Result, [string] $MergeTemplatePath) {
    $merge = Merge-AppSettings -TemplatePath $MergeTemplatePath -ServerPath (Join-Path $Result.TargetPath $script:ConfigFileName) -WhatIf:$Result.Preview
    $Result.Settings = $merge
    if ($merge.Created) { Add-WebAction $Result "Settings $($script:ConfigFileName): create from the package file (environment values empty)" }
    elseif (@($merge.AddedKeys).Count -gt 0) { Add-WebAction $Result "Settings $($script:ConfigFileName): add new key(s) $(@($merge.AddedKeys) -join ', ') (server values kept)" }
    else { Add-WebAction $Result "Settings $($script:ConfigFileName): no new keys (server values kept)" }
}

function Write-WebSettingsGaps([object] $Result) {
    $file = Join-Path $Result.TargetPath $script:ConfigFileName
    Write-WebLog "Required settings still missing or not https; fill them in '$file' (see templates\web\README.md), then run the kit again:" -Level $script:LogWarning
    foreach ($gap in $Result.SettingsGaps) { Write-WebLog "  $gap" -Level $script:LogWarning }
}

function Invoke-WebConfigXmlStep {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result, [hashtable] $Effective, [hashtable] $HeaderData, [string] $SiteOrigin)
    $templateText = [IO.File]::ReadAllText((Join-Path $script:KitRoot $script:WebTemplateRelative), $script:Utf8NoBom)
    $resolved = Resolve-WebTemplateText -TemplateText $templateText -Effective $Effective -HeaderData $HeaderData -SiteOrigin $SiteOrigin
    $serverPath = Join-Path $Result.TargetPath $script:WebConfigFileName
    if (Test-Path -LiteralPath $serverPath -PathType Leaf) {
        $merge = Merge-WebConfigText -TemplateText $resolved -ServerText ([IO.File]::ReadAllText($serverPath))
        $outcome = if ($merge.Changed) { [WebConfigOutcome]::Patched } else { [WebConfigOutcome]::Unchanged }
    }
    else {
        $merge = [pscustomobject]@{ Text = $resolved; Added = [string[]] @($script:XmlRootName); Kept = [string[]] @(); Changed = $true }
        $outcome = [WebConfigOutcome]::Created
    }
    Assert-WebEdgeRuleOrder $merge.Text
    $Result.WebConfig = [int] $outcome
    $Result.WebConfigAdded = $merge.Added
    $Result.WebConfigKept = $merge.Kept
    foreach ($kept in $merge.Kept) { Write-WebLog "web.config: the server's $kept differs from the kit template; the server's is kept." -Level $script:LogWarning }
    if (-not $merge.Changed) { Add-WebAction $Result 'web.config: no new elements (server values kept)'; return }
    $label = if ($outcome -eq [WebConfigOutcome]::Created) { 'create from the kit template' } else { "add $(@($merge.Added) -join ', ') (server values kept)" }
    Add-WebAction $Result "web.config: $label"
    if ($PSCmdlet.ShouldProcess($serverPath, 'Write web.config')) {
        $hasBom = (Test-Path -LiteralPath $serverPath) -and ([IO.File]::ReadAllBytes($serverPath) | Select-Object -First 1) -eq 0xEF
        [IO.File]::WriteAllText($serverPath, $merge.Text, (New-Object Text.UTF8Encoding($hasBom)))
    }
}

function Invoke-WebPayloadStep([object] $Result, [bool] $Purge) {
    $mode = if ($Purge) { 'Mirror' } else { 'Copy (old hashed bundles kept)' }
    Add-WebAction $Result "$mode '$($Result.WebFolder)' to '$($Result.TargetPath)' ($(@($script:KitOwnedFiles) -join ', ') kit-owned, never copied)"
    $Result.Payload = Copy-KitPayload -SourcePath $Result.WebFolder -DestinationPath $Result.TargetPath -ExcludeFiles $script:KitOwnedFiles -NoPurge:(-not $Purge) -WhatIf:$Result.Preview
}

function Invoke-WebStampStep {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result)
    $path = Join-Path $Result.TargetPath $script:BuildInfoFileName
    Add-WebAction $Result "Stamp $($script:BuildInfoFileName) with build id '$($Result.BuildId)' (from $([WebBuildIdSource] $Result.BuildIdSource))"
    if (-not $PSCmdlet.ShouldProcess($path, 'Write the build id')) { return }
    $info = [ordered]@{ buildId = $Result.BuildId; buildIdSource = ([WebBuildIdSource] $Result.BuildIdSource).ToString(); stampedAt = (Get-Date).ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture) }
    [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $info), $script:Utf8NoBom)
    $Result.BuildInfoWritten = $true
}

function Invoke-WebHealthStep([object] $Result, [string] $HealthBaseUrl, [hashtable] $HeaderData) {
    if ($Result.Preview) {
        $Result.Health = [int] [WebHealthOutcome]::SkippedPreview
        Add-WebAction $Result 'Health probe skipped (-WhatIf): a real run checks GET / (security headers), build-info.json and the 404 edge blocks over validated TLS'
        return
    }
    Write-WebLog "Checking $HealthBaseUrl over validated TLS..." -Level $script:LogStep
    $root = Wait-WebRoot (Join-WebUrl $HealthBaseUrl $script:HealthRootPath)
    $buildInfo = Invoke-WebHttpRequest (Join-WebUrl $HealthBaseUrl ($script:UrlSeparator + $script:BuildInfoFileName))
    $blocked = foreach ($path in $script:HealthBlockedProbePaths) { $probe = Invoke-WebHttpRequest (Join-WebUrl $HealthBaseUrl $path); $probe | Add-Member -NotePropertyName Path -NotePropertyValue $path -PassThru }
    $findings = Get-WebHealthFindings -Root $root -BuildInfo $buildInfo -Blocked @($blocked) -ExpectedBuildId $Result.BuildId -HeaderData $HeaderData
    $Result.HealthFailures = $findings.Failures
    $Result.HealthWarnings = $findings.Warnings
    foreach ($warning in $findings.Warnings) { Write-WebLog $warning -Level $script:LogWarning }
    if ($findings.Failures.Count -eq 0) {
        $Result.Health = [int] [WebHealthOutcome]::Healthy
        Write-WebLog "$HealthBaseUrl serves build '$($Result.BuildId)' with the security headers; /api/internal and /api/ops answer 404." -Level $script:LogSuccess
        return
    }
    $Result.Health = [int] [WebHealthOutcome]::Unhealthy
    foreach ($failure in $findings.Failures) { Write-WebLog "Health: $failure" -Level $script:LogError }
    $rollback = if ($Result.BackupPath) { " Roll back: copy '$($Result.BackupPath)\site\*' over '$($Result.TargetPath)'." } else { '' }
    throw "The web app is deployed but the health check failed ($($findings.Failures.Count) finding(s)).$rollback"
}

# ==================================================================================================================
# The kit
# ==================================================================================================================
function Invoke-WebKit {
    <#
    .SYNOPSIS
        Runs the web kit (see the script help). Returns the result object: Preview, WebFolder, TargetPath, BuildId,
        BuildIdSource, PrerequisitesPassed, IisPlan, BackupPath, Settings, SettingsGaps, WebConfig (WebConfigOutcome as
        int), WebConfigAdded, WebConfigKept, Payload, BuildInfoWritten, Health (WebHealthOutcome as int),
        HealthFailures, HealthWarnings, PlannedActions.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string] $PackagePath,
        [string] $SiteName,
        [string] $AppPoolName = $script:DefaultAppPoolName,
        [string] $TargetPath = (Join-Path $env:SystemDrive $script:DefaultTargetRelative),
        [string] $BackupRoot = (Join-Path $env:SystemDrive $script:DefaultBackupRelative),
        [int] $Port,
        [string] $HealthBaseUrl,
        [string] $BuildId,
        [switch] $Purge
    )
    $preview = [bool] $WhatIfPreference
    Assert-WebKitParameters -PackagePath $PackagePath -SiteName $SiteName -AppPoolName $AppPoolName -TargetPath $TargetPath -BackupRoot $BackupRoot `
        -HealthBaseUrl $HealthBaseUrl -BuildId $BuildId -Preview $preview
    $script:KitLogPath = $null
    $result = New-WebKitResult -Preview $preview -TargetPath $TargetPath
    $mode = if ($preview) { 'preview (-WhatIf): nothing is changed' } else { 'install' }
    Write-WebLog "Web kit: $mode. Site '$SiteName', pool '$AppPoolName', folder '$TargetPath'." -Level $script:LogStep

    $result.WebFolder = Resolve-WebPackage $PackagePath
    if ((Test-WebPathInside -Path $result.WebFolder -Parent $TargetPath) -or (Test-WebPathInside -Path $TargetPath -Parent $result.WebFolder)) {
        throw "The package '$($result.WebFolder)' overlaps -TargetPath '$TargetPath'; copy the package somewhere else first. Nothing was changed."
    }
    $build = Resolve-WebBuildId -Requested $BuildId -WebFolder $result.WebFolder
    $result.BuildId = $build.Id
    $result.BuildIdSource = [int] $build.Source
    $headerData = Import-PowerShellDataFile -LiteralPath (Join-Path $script:KitRoot $script:HeadersDataRelative)
    Invoke-WebPrerequisiteStep $result

    $mergeTemplate = New-WebConfigMergeTemplate -PackageConfigPath (Join-Path $result.WebFolder $script:ConfigFileName) -KitTemplatePath (Join-Path $script:KitRoot $script:ConfigTemplateRelative)
    try {
        $effective = Get-WebEffectiveSettings @((Read-WebJson $mergeTemplate), (Read-WebJson (Join-Path $TargetPath $script:ConfigFileName)))
        $result.SettingsGaps = [string[]] @(Get-WebSettingsGaps $effective)
        # The log file starts once every check that can refuse has passed: a refused run leaves no trace on the server.
        $script:KitLogPath = Join-Path $BackupRoot $script:LogFileName
        Invoke-WebIisStep -Result $result -SiteName $SiteName -AppPoolName $AppPoolName -Port $Port
        $result.BackupPath = Backup-Deployment -SourcePath $TargetPath -BackupRoot $BackupRoot -Name $script:BackupName -WhatIf:$preview
        if ($result.BackupPath) { Add-WebAction $result "Back up '$TargetPath' to '$($result.BackupPath)'" } else { Add-WebAction $result 'Backup: nothing deployed yet (first install)' }
        Invoke-WebConfigJsonStep -Result $result -MergeTemplatePath $mergeTemplate
    }
    finally { Remove-Item -LiteralPath $mergeTemplate -Force -WhatIf:$false -ErrorAction SilentlyContinue }

    if ($result.SettingsGaps.Count -gt 0) {
        Write-WebSettingsGaps $result
        $result.Health = [int] [WebHealthOutcome]::SkippedSettingsIncomplete
        Add-WebAction $result 'Stop: the new build is not deployed while settings are incomplete (the running build keeps serving)'
        return $result
    }
    Invoke-WebConfigXmlStep -Result $result -Effective $effective -HeaderData $headerData -SiteOrigin (Get-WebOrigin $HealthBaseUrl) -WhatIf:$preview
    Invoke-WebPayloadStep -Result $result -Purge ([bool] $Purge)
    Invoke-WebStampStep -Result $result -WhatIf:$preview
    Invoke-WebHealthStep -Result $result -HealthBaseUrl $HealthBaseUrl -HeaderData $headerData
    return $result
}

# Dot-sourced (the Pester tests): define the functions only.
if ($MyInvocation.InvocationName -eq '.') { return }

$kitResult = Invoke-WebKit @PSBoundParameters
if (-not $kitResult.Preview -and $kitResult.SettingsGaps.Count -gt 0) { exit $script:ExitSettingsIncomplete }
