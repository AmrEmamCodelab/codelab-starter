#Requires -Version 5.1
<#
.SYNOPSIS
    Installs or updates the integration gateway (services/integration-gateway) on a DMZ IIS host.

.DESCRIPTION
    A deploy kit built on Install-Common.psm1 (MODULE.md in this folder). The gateway is the only deployable with
    egress: the API (no egress) calls its relay routes with a Codelab.Security ES256 service assertion, and the
    gateway calls the OIDC provider and the SMS provider. The steps, in order:

      1. Checks the parameters and the package (refuses before anything changes). The entry assembly is read from
         the package's web.config (aspNetCore processPath/arguments), so the kit follows the host's name.
      2. Prerequisites: IIS, WebAdministration, the ASP.NET Core Module V2 and the ASP.NET Core 10 runtime.
      3. IIS: app pool ("No Managed Code") and the site root (AppPath '/', the default) or a child application of
         an existing site, via Set-KitIisApplication. Installing at the root of a site that serves another folder
         needs -AdoptSite.
      4. TLS: the site's https binding and its certificate (LocalMachine\My: private key, validity dates, Server
         Authentication EKU, a trusted chain). With -CertificateThumbprint the kit adds or rebinds the binding;
         without it a missing or bad certificate is a gap.
      5. Backup of the deployed folder (logs excluded).
      6. app_offline.htm, then the payload mirror (server settings files and logs are never copied over or
         deleted), then Merge-AppSettings for appsettings.json and appsettings.Production.json (server values kept,
         only new keys added).
      7. Gaps: every required key of templates\gateway\required-settings.json that is still empty or malformed
         (paths only, never values), plus the TLS gap. With a gap the kit REFUSES TO START the gateway: it leaves
         app_offline.htm in place (it lists what is missing) and exits 2. A run with no gap removes it.
      8. Health probe GET <HealthBaseUrl><AppPath>/health/ready over https with normal certificate validation (the
         kit never turns validation off). Skipped under -WhatIf and while gaps remain.

    Under -WhatIf nothing is written: no IIS change, binding, folder, backup, copy, settings file or log file.
    Every planned action is printed instead.

    Exit codes (when run as a script): 0 done (or previewed), 1 refused or failed, 2 installed but gaps remain.

.PARAMETER PackagePath
    The published gateway folder (web.config + the entry assembly + appsettings.json), or a kit folder with it in app\.
.PARAMETER SiteName
    The existing IIS site on the DMZ host (create it once with its https binding, or let -CertificateThumbprint add it).
.PARAMETER AppPath
    '/' (default: the gateway is the site root) or a child application path such as '/gateway'.
.PARAMETER AppPoolName
    The app pool. Default 'StarterGateway'.
.PARAMETER TargetPath
    The folder IIS serves the gateway from. Default <SystemDrive>\inetpub\starter-gateway.
.PARAMETER BackupRoot
    Where backups and the install log go. Default <SystemDrive>\inetpub\backups\starter-gateway.
.PARAMETER CertificateThumbprint
    The TLS certificate (LocalMachine\My) for the site's https binding. Optional when the binding already has a good one.
.PARAMETER HttpsPort
    The https binding port. Default 443.
.PARAMETER HostName
    The binding host name (SNI) and the host the health probe calls. Default: none on the binding, this server's
    DNS name for the probe.
.PARAMETER HealthBaseUrl
    The https base URL the probe uses. Default https://<HostName or this server's DNS name>[:HttpsPort].
.PARAMETER AdoptSite
    Allows a root install to repoint a site that serves another folder today.

.EXAMPLE
    .\Install-Gateway.ps1 -PackagePath .\gateway -SiteName 'Gateway' -HostName 'gateway.example.test' -CertificateThumbprint <thumbprint> -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $PackagePath,
    [string] $SiteName,
    [string] $AppPath,
    [string] $AppPoolName,
    [string] $TargetPath,
    [string] $BackupRoot,
    [string] $CertificateThumbprint,
    [int] $HttpsPort,
    [string] $HostName,
    [string] $HealthBaseUrl,
    [switch] $AdoptSite
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Enums (integers with explicit values) ------------------------------------------------------------------------
enum GatewayHealthOutcome {
    NotRun = 0
    SkippedPreview = 1
    SkippedGaps = 2
    Healthy = 3
    Unhealthy = 4
}

enum GatewayTlsAction {
    None = 0
    AddHttpsBinding = 1
    BindCertificate = 2
}

enum GatewayCertificateProblem {
    None = 0
    NotFound = 1
    NoPrivateKey = 2
    NotYetValid = 3
    Expired = 4
    NoServerAuthentication = 5
    UntrustedChain = 6
}

# required-settings.json "format": what a filled value must look like.
enum GatewaySettingFormat {
    Value = 1
    HttpsUrl = 2
}

# --- Constants ----------------------------------------------------------------------------------------------------
$script:CommonModuleFileName        = 'Install-Common.psm1'
$script:ProductionTemplateRelative  = 'templates\gateway\appsettings.template.json'
$script:RequirementsRelative        = 'templates\gateway\required-settings.json'
$script:BackupName                  = 'gateway'
$script:LogFileName                 = 'install-gateway.log'
$script:RuntimeMajor                = 10

$script:DefaultAppPath              = '/'
$script:DefaultAppPoolName          = 'StarterGateway'
$script:DefaultTargetRelative       = 'inetpub\starter-gateway'
$script:DefaultBackupRelative       = 'inetpub\backups\starter-gateway'
$script:DefaultHttpsPort            = 443
$script:MinPort                     = 1
$script:MaxPort                     = 65535

# The package: the published gateway output, directly or in app\. The entry file comes from web.config.
$script:PackageAppFolder            = 'app'
$script:WebConfigFileName           = 'web.config'
$script:BaseSettingsFileName        = 'appsettings.json'
$script:ProductionSettingsFileName  = 'appsettings.Production.json'
$script:AspNetCoreXPath             = '//aspNetCore'
$script:AspNetCoreDotnetHost        = 'dotnet'
$script:EntryFilePattern            = '[^\\/\s"]+\.(dll|exe)'

# Settings (.NET configuration paths). The required ones are data: templates\gateway\required-settings.json.
$script:ConfigSeparator             = ':'
$script:RequirementWildcard         = '*'
$script:JsonTrueText                = 'true'
$script:PathMember                  = 'path'
$script:WhenMember                  = 'when'
$script:EqualsMember                = 'equals'
$script:EmptyContainer              = New-Object -TypeName psobject
$script:TlsGapPath                  = 'Tls:Certificate'
$script:HttpsScheme                 = 'https'
$script:HttpsUrlHint                = ' (must be an absolute https URL)'

# Paths.
$script:DriveRootedPattern          = '^[A-Za-z]:\\'
$script:UncPattern                  = '^\\\\[^\\]+\\[^\\]+'
$script:PathSeparator               = '\'
$script:InvalidPoolNameChars        = [char[]] '\/:*?"<>|'
$script:UrlSeparator                = '/'
$script:IisRootAppPath              = '/'
$script:SiteMissingPattern          = "Site '*' does not exist*"

# TLS: the binding, the certificate store and what a usable server certificate is.
$script:HttpsProtocol               = 'https'
$script:BindingInfoSeparator        = ':'
$script:BindingPortIndex            = 1
$script:BindingHostIndex            = 2
$script:SniFlag                     = 1
$script:NoSslFlags                  = 0
$script:CertificateStoreName        = 'My'
$script:CertificateStorePath        = 'Cert:\LocalMachine\My'
$script:ServerAuthenticationOid     = '1.3.6.1.5.5.7.3.1'

# app_offline.htm: ANCM stops the app while it exists. The kit uses it twice: during the copy, and to refuse to
# start a gateway whose settings or certificate are incomplete.
$script:AppOfflineFileName          = 'app_offline.htm'
$script:AppOfflineHtml              = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Update in progress</title></head><body><p>The service is being updated and will be back in a moment.</p></body></html>'
$script:GapPageHtmlFormat           = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Configuration incomplete</title></head><body><p>The deploy kit did not start this service: required settings are missing. Fill them, then run the kit again.</p><ul>{0}</ul></body></html>'
$script:GapPageItemFormat           = '<li>{0}</li>'
$script:ShutdownWaitSeconds         = 3

# Health probe (validated TLS only; there is no switch that skips certificate validation).
$script:HealthPath                  = '/health/ready'  # HealthEndpoints.ReadyPath (Codelab.Observability)
$script:HealthAttempts              = 12
$script:HealthDelaySeconds          = 5
$script:HealthRequestTimeoutSeconds = 10
$script:HttpOk                      = 200
$script:NoHttpStatus                = 0
$script:HtmlBodyStart               = '<'

$script:ExitGaps                    = 2
$script:TempTemplateExtension       = '.json'

# Write-KitLog levels (KitLogLevel member names in Install-Common.psm1).
$script:LogInfo                     = 'Info'
$script:LogStep                     = 'Step'
$script:LogSuccess                  = 'Success'
$script:LogWarning                  = 'Warning'
$script:LogError                    = 'Error'

$script:KitRoot                     = $PSScriptRoot
$script:KitLogPath                  = $null

Import-Module (Join-Path $PSScriptRoot $script:CommonModuleFileName) -Force -Verbose:$false

# ==================================================================================================================
# Logging and the planned-action list
# ==================================================================================================================
# Install-Common's Write-KitLog does not see this script's -WhatIf, so the log file is dropped here under -WhatIf.
function Write-GatewayLog([string] $Message, [string] $Level = $script:LogInfo) {
    $logPath = if ($WhatIfPreference) { $null } else { $script:KitLogPath }
    Write-KitLog -Message $Message -Level $Level -LogPath $logPath -WhatIf:$false
}

function Add-GatewayAction([object] $Result, [string] $Text) {
    $Result.PlannedActions.Add($Text)
    Write-GatewayLog $Text -Level $script:LogStep
}

# ==================================================================================================================
# Parameters, paths and the package (pure checks; they throw before anything changes)
# ==================================================================================================================
function Test-GatewayFullyQualifiedPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return ($Path -match $script:DriveRootedPattern -or $Path -match $script:UncPattern)
}

function ConvertTo-GatewayComparablePath([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd($script:PathSeparator)
}

# True when $Path is $Parent itself or anything below it (case-insensitive, like NTFS).
function Test-GatewayPathInside([string] $Path, [string] $Parent) {
    $child = ConvertTo-GatewayComparablePath $Path
    $root = ConvertTo-GatewayComparablePath $Parent
    if ([string]::Equals($child, $root, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $child.StartsWith($root + $script:PathSeparator, [StringComparison]::OrdinalIgnoreCase)
}

function Test-GatewayHttpsUrl([object] $Value) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { return $false }
    $uri = $null
    if (-not [Uri]::TryCreate($Value.Trim(), [UriKind]::Absolute, [ref] $uri)) { return $false }
    return [string]::Equals($uri.Scheme, $script:HttpsScheme, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-GatewayKitParameters {
    param([string] $PackagePath, [string] $SiteName, [string] $AppPoolName, [string] $TargetPath, [string] $BackupRoot,
          [int] $HttpsPort, [string] $HealthBaseUrl)
    if ([string]::IsNullOrWhiteSpace($PackagePath)) { throw '-PackagePath is required: the published gateway folder. Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($SiteName)) { throw '-SiteName is required: the existing IIS site on the DMZ host. Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($AppPoolName) -or $AppPoolName.IndexOfAny($script:InvalidPoolNameChars) -ge 0) {
        throw "-AppPoolName '$AppPoolName' is not a valid app pool name. Nothing was changed."
    }
    if (-not (Test-GatewayFullyQualifiedPath $TargetPath)) { throw "-TargetPath '$TargetPath' must be a fully qualified folder. Nothing was changed." }
    if (-not (Test-GatewayFullyQualifiedPath $BackupRoot)) { throw "-BackupRoot '$BackupRoot' must be a fully qualified folder. Nothing was changed." }
    if (Test-GatewayPathInside -Path $BackupRoot -Parent $TargetPath) {
        throw "-BackupRoot '$BackupRoot' is inside -TargetPath '$TargetPath'; the payload mirror would delete the backups. Nothing was changed."
    }
    if ($HttpsPort -lt $script:MinPort -or $HttpsPort -gt $script:MaxPort) {
        throw "-HttpsPort $HttpsPort is not a port ($($script:MinPort)-$($script:MaxPort)). Nothing was changed."
    }
    if (-not (Test-GatewayHttpsUrl $HealthBaseUrl)) {
        throw "-HealthBaseUrl '$HealthBaseUrl' must be an absolute https URL: the health probe validates the certificate and never skips it. Nothing was changed."
    }
}

# The entry file named by web.config: processPath '.\X.exe', or processPath 'dotnet' with arguments '.\X.dll'.
function Get-GatewayEntryFileName([string] $WebConfigPath) {
    $document = New-Object Xml.XmlDocument
    try { $document.Load($WebConfigPath) }
    catch { throw "'$WebConfigPath' is not valid XML. Nothing was changed." }
    $node = $document.SelectSingleNode($script:AspNetCoreXPath)
    if ($null -eq $node) { throw "'$WebConfigPath' has no aspNetCore element; it is not an ASP.NET Core publish output. Nothing was changed." }
    $processPath = [string] $node.GetAttribute('processPath')
    $source = if ([string]::Equals($processPath, $script:AspNetCoreDotnetHost, [StringComparison]::OrdinalIgnoreCase)) { [string] $node.GetAttribute('arguments') } else { $processPath }
    $match = [regex]::Match($source, $script:EntryFilePattern)
    if (-not $match.Success) { throw "'$WebConfigPath' names no entry .dll or .exe in its aspNetCore element. Nothing was changed." }
    return $match.Value
}

# Returns the folder holding the published gateway (the package itself or its app\ folder) and its entry file.
function Resolve-GatewayPackage([string] $PackagePath) {
    if (-not (Test-Path -LiteralPath $PackagePath -PathType Container)) { throw "Package folder '$PackagePath' does not exist. Nothing was changed." }
    foreach ($candidate in @($PackagePath, (Join-Path $PackagePath $script:PackageAppFolder))) {
        $webConfig = Join-Path $candidate $script:WebConfigFileName
        if (-not (Test-Path -LiteralPath $webConfig -PathType Leaf)) { continue }
        $entry = Get-GatewayEntryFileName $webConfig
        foreach ($required in @($entry, $script:BaseSettingsFileName)) {
            if (-not (Test-Path -LiteralPath (Join-Path $candidate $required) -PathType Leaf)) {
                throw "The package '$candidate' has no $required; it is not a complete publish output. Nothing was changed."
            }
        }
        return [pscustomobject]@{ AppFolder = (Resolve-Path -LiteralPath $candidate).ProviderPath; EntryFileName = $entry }
    }
    throw "No $($script:WebConfigFileName) in '$PackagePath' or its $($script:PackageAppFolder)\ folder; give the published gateway. Nothing was changed."
}

# ==================================================================================================================
# Settings: read, layer like .NET configuration, find the required keys that are empty or malformed
# ==================================================================================================================
function Read-GatewayJson([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $text = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).ProviderPath, (New-Object Text.UTF8Encoding($false)))
    # The parser error is not passed on: it quotes file text, and settings files hold key material.
    try { return (ConvertFrom-Json -InputObject $text -ErrorAction Stop) }
    catch { throw "'$Path' is not valid JSON (comments and trailing commas are not supported). Nothing was changed." }
}

function Test-GatewayJsonObject([object] $Value) {
    return ($null -ne $Value -and $Value -is [System.Management.Automation.PSCustomObject])
}

function Test-GatewayJsonArray([object] $Value) {
    return ($null -ne $Value -and $Value -isnot [string] -and $Value -is [System.Collections.IList])
}

# Flattens a JSON object into "A:B:0:C" -> value, the way .NET configuration keys it. An empty object or array is
# recorded as the EmptyContainer marker, so a required section that is present but empty is still seen.
function Add-GatewaySettingLeaves([object] $Node, [string] $Prefix, [hashtable] $Into) {
    if (Test-GatewayJsonObject $Node) {
        $members = @($Node.PSObject.Properties)
        if ($members.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        foreach ($member in $members) {
            $path = if ($Prefix) { $Prefix + $script:ConfigSeparator + $member.Name } else { $member.Name }
            Add-GatewaySettingLeaves -Node $member.Value -Prefix $path -Into $Into
        }
        return
    }
    if (Test-GatewayJsonArray $Node) {
        if ($Node.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        for ($index = 0; $index -lt $Node.Count; $index++) {
            Add-GatewaySettingLeaves -Node $Node[$index] -Prefix ($Prefix + $script:ConfigSeparator + $index) -Into $Into
        }
        return
    }
    $Into[$Prefix] = $Node
}

# Layers are lowest precedence first; a later layer overrides a leaf (case-insensitive keys, like .NET configuration).
function Get-GatewayEffectiveSettings([object[]] $Layers) {
    $leaves = @{}
    foreach ($layer in @($Layers | Where-Object { $null -ne $_ })) { Add-GatewaySettingLeaves -Node $layer -Prefix '' -Into $leaves }
    return $leaves
}

function Test-GatewayEmptyValue([object] $Value) {
    if ($null -eq $Value) { return $true }
    return ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value))
}

function Get-GatewaySettingValue([hashtable] $Effective, [string] $Path) {
    if (-not $Effective.ContainsKey($Path)) { return $null }
    $value = $Effective[$Path]
    if ([object]::ReferenceEquals($value, $script:EmptyContainer)) { return $null }
    return $value
}

# Reads templates\gateway\required-settings.json: { requirements: [ { path, format (GatewaySettingFormat), when? } ] }.
function Read-GatewayRequirements([string] $Path) {
    $document = Read-GatewayJson $Path
    if ($null -eq $document -or -not (Test-GatewayJsonArray $document.requirements)) { throw "The kit file '$Path' is missing or has no requirements array. Nothing was changed." }
    $known = [int[]] [Enum]::GetValues([GatewaySettingFormat])
    foreach ($requirement in $document.requirements) {
        if ([string]::IsNullOrWhiteSpace([string] $requirement.path)) { throw "The kit file '$Path' has a requirement without a path. Nothing was changed." }
        if ($known -notcontains [int] $requirement.format) { throw "The kit file '$Path' has requirement '$($requirement.path)' with an unknown format $($requirement.format). Nothing was changed." }
        $wildcards = @($requirement.path.Split($script:ConfigSeparator) | Where-Object { $_ -eq $script:RequirementWildcard }).Count
        if ($wildcards -gt 1) { throw "The kit file '$Path' has requirement '$($requirement.path)' with more than one '*'. Nothing was changed." }
    }
    return [object[]] @($document.requirements | ForEach-Object {
        [pscustomobject]@{ Path = [string] $_.path; Format = [int] $_.format; When = (Read-GatewayConditions -Path $Path -Requirement $_) }
    })
}

<#
    Reads a requirement's optional "when" into conditions that must all hold: a path string (the value there must be
    true), or an array of { path, equals } objects (the value at path, or one item of the array at path, must equal
    "equals" exactly: case-sensitive, like the host's profile keys). No "when" is no condition.
#>
function Read-GatewayConditions([string] $Path, [object] $Requirement) {
    if (-not $Requirement.PSObject.Properties[$script:WhenMember]) { return [object[]] @() }
    $when = $Requirement.($script:WhenMember)
    if ($when -is [string]) {
        if ([string]::IsNullOrWhiteSpace($when)) { return [object[]] @() }
        return [object[]] @([pscustomobject]@{ Path = $when; HasEquals = $false; Equals = '' })
    }
    if (-not (Test-GatewayJsonArray $when)) { throw "The kit file '$Path' has requirement '$($Requirement.path)' whose 'when' is neither a path nor an array of conditions. Nothing was changed." }
    return [object[]] @($when | ForEach-Object {
        if (-not (Test-GatewayJsonObject $_) -or -not $_.PSObject.Properties[$script:PathMember] -or [string]::IsNullOrWhiteSpace([string] $_.path)) { throw "The kit file '$Path' has requirement '$($Requirement.path)' with a 'when' condition without a path. Nothing was changed." }
        $hasEquals = $null -ne $_.PSObject.Properties[$script:EqualsMember]
        $equals = if ($hasEquals) { [string] $_.($script:EqualsMember) } else { '' }
        [pscustomobject]@{ Path = [string] $_.path; HasEquals = $hasEquals; Equals = $equals }
    })
}

# A requirement with a '*' segment applies to every index of that array (the 'when' path shares the index). An
# array with no items yields index 0, so the gap still names a concrete key.
function Get-GatewayRequirementInstances([hashtable] $Effective, [object] $Requirement) {
    $marker = $script:ConfigSeparator + $script:RequirementWildcard
    $position = $Requirement.Path.IndexOf($marker)
    if ($position -lt 0) { return [object[]] @($Requirement) }
    $prefix = $Requirement.Path.Substring(0, $position)
    $pattern = '^' + [regex]::Escape($prefix + $script:ConfigSeparator) + '(\d+)(' + [regex]::Escape($script:ConfigSeparator) + '|$)'
    $indexes = @($Effective.Keys | ForEach-Object { $found = [regex]::Match($_, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase); if ($found.Success) { [int] $found.Groups[1].Value } } | Sort-Object -Unique)
    if ($indexes.Count -eq 0) { $indexes = @(0) }
    return [object[]] @($indexes | ForEach-Object {
        [pscustomobject]@{
            Path = $Requirement.Path.Replace($marker, $script:ConfigSeparator + $_); Format = $Requirement.Format
            When = (Copy-GatewayConditions -Conditions $Requirement.When -Marker $marker -Index $_)
        }
    })
}

# The conditions with the '*' segment of each path replaced by $Index (the item of the requirement's own array).
function Copy-GatewayConditions([object[]] $Conditions, [string] $Marker, [int] $Index) {
    return [object[]] @($Conditions | Where-Object { $_ } | ForEach-Object {
        [pscustomobject]@{ Path = $_.Path.Replace($Marker, $script:ConfigSeparator + $Index); HasEquals = $_.HasEquals; Equals = $_.Equals }
    })
}

# The value at $Path and the items of an array at $Path (keys "<Path>:<index>"), as strings.
function Get-GatewayConditionValues([hashtable] $Effective, [string] $Path) {
    $itemPattern = '^' + [regex]::Escape($Path + $script:ConfigSeparator) + '\d+$'
    $values = @($Effective.Keys | Where-Object { $_ -match $itemPattern } | ForEach-Object { Get-GatewaySettingValue -Effective $Effective -Path $_ })
    $values += Get-GatewaySettingValue -Effective $Effective -Path $Path
    return [string[]] @($values | Where-Object { $null -ne $_ } | ForEach-Object { [string] $_ })
}

function Test-GatewayConditionHolds([hashtable] $Effective, [object] $Condition) {
    if ($Condition.HasEquals) {
        $matching = @(Get-GatewayConditionValues -Effective $Effective -Path $Condition.Path | Where-Object { [string]::Equals($_, $Condition.Equals, [StringComparison]::Ordinal) })
        return ($matching.Count -gt 0)
    }
    $value = Get-GatewaySettingValue -Effective $Effective -Path $Condition.Path
    if ($value -is [bool]) { return $value }
    return [string]::Equals([string] $value, $script:JsonTrueText, [StringComparison]::OrdinalIgnoreCase)
}

# True when every condition of a requirement holds (a requirement without conditions always applies).
function Test-GatewayRequirementApplies([hashtable] $Effective, [object[]] $When) {
    foreach ($condition in @($When | Where-Object { $_ })) {
        if (-not (Test-GatewayConditionHolds -Effective $Effective -Condition $condition)) { return $false }
    }
    return $true
}

<#
    Returns the required configuration paths that are missing, empty or malformed in the effective settings (sorted).
    A section counts as filled only when it has at least one value and none of its values is empty; the paths
    returned point at the empty values (for example ...:TrustedServices:0:PublicKeyPem). Values are never returned.
#>
function Get-GatewaySettingsGaps([hashtable] $Effective, [object[]] $Requirements) {
    $gaps = New-Object 'System.Collections.Generic.List[string]'
    foreach ($instance in @($Requirements | ForEach-Object { Get-GatewayRequirementInstances -Effective $Effective -Requirement $_ })) {
        if (-not (Test-GatewayRequirementApplies -Effective $Effective -When $instance.When)) { continue }
        $below = $instance.Path + $script:ConfigSeparator
        $keys = @($Effective.Keys | Where-Object {
            ($_ -eq $instance.Path -or $_.StartsWith($below, [StringComparison]::OrdinalIgnoreCase)) -and
            -not [object]::ReferenceEquals($Effective[$_], $script:EmptyContainer)
        })
        if ($keys.Count -eq 0) { $gaps.Add($instance.Path); continue }
        foreach ($key in $keys) {
            if (Test-GatewayEmptyValue $Effective[$key]) { $gaps.Add($key); continue }
            if ($instance.Format -eq [int] [GatewaySettingFormat]::HttpsUrl -and -not (Test-GatewayHttpsUrl $Effective[$key])) { $gaps.Add($key + $script:HttpsUrlHint) }
        }
    }
    return [string[]] @($gaps | Sort-Object -Unique)
}

# The settings as they are (or will be) after the merge: package template < server file, per file, base < Production.
function Get-GatewaySettingsLayers([string] $AppFolder, [string] $TargetPath, [string] $ProductionTemplatePath) {
    return @(
        (Read-GatewayJson (Join-Path $AppFolder $script:BaseSettingsFileName)),
        (Read-GatewayJson (Join-Path $TargetPath $script:BaseSettingsFileName)),
        (Read-GatewayJson $ProductionTemplatePath),
        (Read-GatewayJson (Join-Path $TargetPath $script:ProductionSettingsFileName))
    )
}

# ==================================================================================================================
# TLS: facts (read-only), certificate problems and the binding plan (pure), apply
# ==================================================================================================================
function ConvertTo-GatewayThumbprint([string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    return ($Value -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
}

# Pure: the first reason a certificate cannot serve the gateway, or None.
function Get-GatewayCertificateProblem([object] $Certificate, [datetime] $Now) {
    if ($null -eq $Certificate -or -not $Certificate.Exists) { return [GatewayCertificateProblem]::NotFound }
    if (-not $Certificate.HasPrivateKey) { return [GatewayCertificateProblem]::NoPrivateKey }
    if ($Certificate.NotBefore -gt $Now) { return [GatewayCertificateProblem]::NotYetValid }
    if ($Certificate.NotAfter -le $Now) { return [GatewayCertificateProblem]::Expired }
    if (-not $Certificate.HasServerAuthentication) { return [GatewayCertificateProblem]::NoServerAuthentication }
    if (-not $Certificate.ChainTrusted) { return [GatewayCertificateProblem]::UntrustedChain }
    return [GatewayCertificateProblem]::None
}

function Get-GatewayCertificateFacts([string] $Thumbprint) {
    $facts = [pscustomobject]@{ Thumbprint = $Thumbprint; Exists = $false; HasPrivateKey = $false; NotBefore = [datetime]::MinValue; NotAfter = [datetime]::MinValue; HasServerAuthentication = $false; ChainTrusted = $false }
    $certificate = Get-Item -LiteralPath (Join-Path $script:CertificateStorePath $Thumbprint) -ErrorAction SilentlyContinue
    if ($null -eq $certificate) { return $facts }
    $facts.Exists = $true
    $facts.HasPrivateKey = [bool] $certificate.HasPrivateKey
    $facts.NotBefore = $certificate.NotBefore
    $facts.NotAfter = $certificate.NotAfter
    $usage = $certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | Select-Object -First 1
    $facts.HasServerAuthentication = ($null -eq $usage) -or (@($usage.EnhancedKeyUsages | Where-Object { $_.Value -eq $script:ServerAuthenticationOid }).Count -gt 0)
    $chain = New-Object Security.Cryptography.X509Certificates.X509Chain
    try { $facts.ChainTrusted = $chain.Build($certificate) } finally { $chain.Dispose() }
    return $facts
}

# Read-only: the site's https bindings and the facts of every certificate involved. Known = $false without IIS access.
function Get-GatewayTlsFacts([string] $SiteName, [string] $RequestedThumbprint) {
    $facts = [pscustomobject]@{ Known = $false; Bindings = @(); Certificates = @{} }
    try {
        Import-Module WebAdministration -ErrorAction Stop -Verbose:$false
        $facts.Bindings = @(Get-WebBinding -Name $SiteName -Protocol $script:HttpsProtocol -ErrorAction Stop | ForEach-Object {
            $parts = ([string] $_.bindingInformation).Split($script:BindingInfoSeparator)
            [pscustomobject]@{ Port = [int] $parts[$script:BindingPortIndex]; HostName = [string] $parts[$script:BindingHostIndex]; Thumbprint = (ConvertTo-GatewayThumbprint ([string] $_.certificateHash)) }
        })
        $facts.Known = $true
    }
    catch { Write-GatewayLog "The site's https bindings could not be read ($($_.Exception.Message)); planned as if there were none." -Level $script:LogWarning }
    $thumbprints = @(@($facts.Bindings | ForEach-Object { $_.Thumbprint }) + $RequestedThumbprint | Where-Object { $_ } | Sort-Object -Unique)
    foreach ($thumbprint in $thumbprints) { $facts.Certificates[$thumbprint] = Get-GatewayCertificateFacts $thumbprint }
    return $facts
}

function New-GatewayTlsStep([GatewayTlsAction] $Action, [string] $Detail) {
    return [pscustomobject]@{ Action = $Action.ToString(); ActionId = [int] $Action; Detail = $Detail; Applied = $false }
}

function Get-GatewayCertificateText([string] $Thumbprint, [GatewayCertificateProblem] $Problem) {
    return "certificate $Thumbprint is $($Problem.ToString()) (LocalMachine\$($script:CertificateStoreName))"
}

<#
    Pure: the same facts always give the same plan. Returns Steps (GatewayTlsAction), Gap (text or $null: the site
    has no usable certificate and none was given) and Refusal (text or $null: the given certificate is unusable).
#>
function Get-GatewayTlsPlan([object] $Facts, [string] $RequestedThumbprint, [int] $HttpsPort, [string] $HostName, [datetime] $Now) {
    $plan = [pscustomobject]@{ Steps = @(); Gap = $null; Refusal = $null }
    $steps = New-Object 'System.Collections.Generic.List[object]'
    $requested = ConvertTo-GatewayThumbprint $RequestedThumbprint
    $hostLabel = if ($HostName) { $HostName } else { '*' }
    if ($requested) {
        $problem = Get-GatewayCertificateProblem -Certificate $Facts.Certificates[$requested] -Now $Now
        if ($problem -ne [GatewayCertificateProblem]::None) {
            $plan.Refusal = "-CertificateThumbprint: $(Get-GatewayCertificateText $requested $problem). Nothing was changed."
            return $plan
        }
        $binding = @($Facts.Bindings | Where-Object { $_.Port -eq $HttpsPort -and [string]::Equals($_.HostName, $HostName, [StringComparison]::OrdinalIgnoreCase) }) | Select-Object -First 1
        if ($null -eq $binding) {
            $steps.Add((New-GatewayTlsStep AddHttpsBinding "add https binding port $HttpsPort host '$hostLabel'"))
            $steps.Add((New-GatewayTlsStep BindCertificate "bind certificate $requested to https port $HttpsPort host '$hostLabel'"))
        }
        elseif ($binding.Thumbprint -ne $requested) {
            $from = if ($binding.Thumbprint) { $binding.Thumbprint } else { 'none' }
            $steps.Add((New-GatewayTlsStep BindCertificate "bind certificate $requested to https port $HttpsPort host '$hostLabel' (was $from)"))
        }
    }
    elseif (@($Facts.Bindings).Count -eq 0) {
        $note = if ($Facts.Known) { 'the site has no https binding' } else { 'the https bindings could not be read (not elevated)' }
        $plan.Gap = "$($script:TlsGapPath) ($note; run the kit with -CertificateThumbprint)"
    }
    else {
        foreach ($binding in @($Facts.Bindings)) {
            $problem = Get-GatewayCertificateProblem -Certificate $Facts.Certificates[$binding.Thumbprint] -Now $Now
            if ($problem -eq [GatewayCertificateProblem]::None) { continue }
            $label = if ($binding.HostName) { $binding.HostName } else { '*' }
            $plan.Gap = "$($script:TlsGapPath) (https port $($binding.Port) host '$label': $(Get-GatewayCertificateText $binding.Thumbprint $problem); run the kit with -CertificateThumbprint)"
            break
        }
    }
    if ($steps.Count -eq 0 -and $null -eq $plan.Gap) { $steps.Add((New-GatewayTlsStep None 'the https binding has a usable certificate')) }
    $plan.Steps = [object[]] $steps.ToArray()
    return $plan
}

function Invoke-GatewayTlsAction([object] $Step, [string] $SiteName, [int] $HttpsPort, [string] $HostName, [string] $Thumbprint) {
    Import-Module WebAdministration -ErrorAction Stop -Verbose:$false
    switch ([GatewayTlsAction] $Step.ActionId) {
        ([GatewayTlsAction]::AddHttpsBinding) {
            $flags = if ($HostName) { $script:SniFlag } else { $script:NoSslFlags }
            New-WebBinding -Name $SiteName -Protocol $script:HttpsProtocol -Port $HttpsPort -HostHeader $HostName -SslFlags $flags | Out-Null
        }
        ([GatewayTlsAction]::BindCertificate) {
            $binding = Get-WebBinding -Name $SiteName -Protocol $script:HttpsProtocol -Port $HttpsPort -HostHeader $HostName
            if (-not [string]::IsNullOrWhiteSpace([string] $binding.certificateHash)) { $binding.RemoveSslCertificate() }
            $binding.AddSslCertificate($Thumbprint, $script:CertificateStoreName)
        }
    }
}

# ==================================================================================================================
# Health probe (validated TLS)
# ==================================================================================================================
function Get-GatewayDefaultHealthBaseUrl([string] $HostName, [int] $HttpsPort) {
    $name = $HostName
    if ([string]::IsNullOrWhiteSpace($name)) {
        try { $name = [Net.Dns]::GetHostEntry([string]::Empty).HostName } catch { $name = $env:COMPUTERNAME }
    }
    $port = if ($HttpsPort -eq $script:DefaultHttpsPort) { '' } else { $script:BindingInfoSeparator + $HttpsPort }
    return $script:HttpsScheme + '://' + $name + $port
}

function Get-GatewayHealthUrl([string] $BaseUrl, [string] $AppPath) {
    $app = $AppPath.Trim().Trim($script:UrlSeparator)
    $prefix = if ($app) { $script:UrlSeparator + $app } else { '' }
    return $BaseUrl.TrimEnd($script:UrlSeparator) + $prefix + $script:HealthPath
}

# One GET with the platform's certificate validation in force: a callback that skips validation is removed for the
# call and restored after it, so the probe can never report a gateway healthy behind an untrusted certificate.
function Invoke-GatewayHealthRequest([string] $Url) {
    $previousCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
    $previousProtocols = [Net.ServicePointManager]::SecurityProtocol
    [Net.ServicePointManager]::ServerCertificateValidationCallback = $null
    [Net.ServicePointManager]::SecurityProtocol = $previousProtocols -bor [Net.SecurityProtocolType]::Tls12
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $script:HealthRequestTimeoutSeconds -MaximumRedirection 0
        return [pscustomobject]@{ StatusCode = [int] $response.StatusCode; Body = [string] $response.Content; Error = $null }
    }
    catch [System.Net.WebException] {
        $response = $_.Exception.Response
        if ($null -eq $response) { return [pscustomobject]@{ StatusCode = $script:NoHttpStatus; Body = ''; Error = "$($_.Exception.Status): $($_.Exception.Message)" } }
        $reader = New-Object IO.StreamReader($response.GetResponseStream())
        try { $body = $reader.ReadToEnd() } finally { $reader.Dispose(); $response.Close() }
        return [pscustomobject]@{ StatusCode = [int] $response.StatusCode; Body = $body; Error = $null }
    }
    finally {
        [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback
        [Net.ServicePointManager]::SecurityProtocol = $previousProtocols
    }
}

function Get-GatewayHealthDiagnosis([object] $Probe) {
    if ($Probe.StatusCode -eq $script:HttpOk) { return 'the gateway answered 200' }
    if ($Probe.StatusCode -eq $script:NoHttpStatus) {
        return "no answer ($($Probe.Error)): check the https binding, the host name in -HealthBaseUrl and that this server trusts the certificate's chain"
    }
    if (([string] $Probe.Body).TrimStart().StartsWith($script:HtmlBodyStart)) {
        return "HTTP $($Probe.StatusCode) with an HTML body: IIS answered, the gateway did not start (usually a refused setting). Read Event Viewer > Application (IIS AspNetCore Module V2) and the logs folder."
    }
    return "HTTP $($Probe.StatusCode) from the gateway itself: read its problem+json body and the logs folder"
}

function Wait-GatewayHealth([string] $Url, [int] $Attempts = $script:HealthAttempts, [int] $DelaySeconds = $script:HealthDelaySeconds) {
    $probe = $null
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $probe = Invoke-GatewayHealthRequest $Url
        if ($probe.StatusCode -eq $script:HttpOk) { return $probe }
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
    return $probe
}

# ==================================================================================================================
# Steps
# ==================================================================================================================
function New-GatewayKitResult([bool] $Preview, [string] $TargetPath, [string] $HealthUrl) {
    return [pscustomobject]@{
        Preview = $Preview; AppFolder = $null; EntryFileName = $null; TargetPath = $TargetPath
        PrerequisitesPassed = $false; IisPlan = @(); TlsPlan = @(); BackupPath = $null; Payload = $null; Settings = @()
        SettingsGaps = [string[]] @(); Gaps = [string[]] @(); StartRefused = $false
        HealthUrl = $HealthUrl; Health = [int] [GatewayHealthOutcome]::NotRun; HealthStatusCode = $null
        PlannedActions = (New-Object 'System.Collections.Generic.List[string]')
    }
}

function Invoke-GatewayPrerequisiteStep([object] $Result) {
    $check = Test-KitPrerequisites -RuntimeMajor $script:RuntimeMajor -RequireAdministrator:(-not $Result.Preview)
    $Result.PrerequisitesPassed = [bool] $check.Passed
    if ($check.Passed) { return }
    if (-not $Result.Preview) { throw 'Prerequisites are missing (see the lines above). Nothing was changed.' }
    Write-GatewayLog 'Prerequisites are missing; a real run stops here. The preview continues.' -Level $script:LogWarning
}

# A root install repoints the whole site; refuse when the site serves another folder today, unless -AdoptSite.
function Assert-GatewaySiteAdoption([object[]] $Plan, [string] $AppPath, [bool] $AdoptSite) {
    if ($AppPath -ne $script:IisRootAppPath -or $AdoptSite) { return }
    $repoint = @($Plan | Where-Object { $_.Action -eq 'UpdateSite' -and @($_.Changes | Where-Object { $_ -like 'physicalPath:*' }).Count -gt 0 })
    if ($repoint.Count -gt 0) {
        throw "Site root install would repoint the site ($(@($repoint[0].Changes) -join '; ')). Use a dedicated gateway site, a child -AppPath, or -AdoptSite. Nothing was changed."
    }
}

function Invoke-GatewayIisStep([object] $Result, [string] $SiteName, [string] $AppPath, [string] $AppPoolName, [bool] $AdoptSite) {
    try { $preview = @(Get-KitIisPlan -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName -PhysicalPath $Result.TargetPath -WhatIf:$false) }
    catch {
        if ($_.Exception.Message -like $script:SiteMissingPattern) { throw "Site '$SiteName' does not exist: create the gateway site on the DMZ host first (any binding; the kit adds https with -CertificateThumbprint), and run elevated (without admin rights IIS lists no sites). Nothing was changed." }
        throw
    }
    Assert-GatewaySiteAdoption -Plan $preview -AppPath $AppPath -AdoptSite $AdoptSite
    $plan = @(Set-KitIisApplication -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName -PhysicalPath $Result.TargetPath -WhatIf:$Result.Preview)
    $Result.IisPlan = $plan
    foreach ($step in $plan) { Add-GatewayAction $Result "IIS $($step.Action) $($step.Target): $(@($step.Changes) -join '; ')" }
}

# Plans the binding first (a refused certificate stops the run before anything changes); applies it later.
function Get-GatewayTlsStepPlan([object] $Result, [string] $SiteName, [string] $Thumbprint, [int] $HttpsPort, [string] $HostName) {
    $facts = Get-GatewayTlsFacts -SiteName $SiteName -RequestedThumbprint (ConvertTo-GatewayThumbprint $Thumbprint)
    $plan = Get-GatewayTlsPlan -Facts $facts -RequestedThumbprint $Thumbprint -HttpsPort $HttpsPort -HostName $HostName -Now (Get-Date)
    if ($plan.Refusal) { throw $plan.Refusal }
    return $plan
}

function Invoke-GatewayTlsStep {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result, [object] $Plan, [string] $SiteName, [string] $Thumbprint, [int] $HttpsPort, [string] $HostName)
    $Result.TlsPlan = @($Plan.Steps)
    foreach ($step in @($Plan.Steps)) {
        Add-GatewayAction $Result "TLS $($step.Action): $($step.Detail)"
        if ($step.ActionId -eq [int] [GatewayTlsAction]::None) { continue }
        if ($PSCmdlet.ShouldProcess($SiteName, $step.Detail)) {
            Invoke-GatewayTlsAction -Step $step -SiteName $SiteName -HttpsPort $HttpsPort -HostName $HostName -Thumbprint (ConvertTo-GatewayThumbprint $Thumbprint)
            $step.Applied = $true
        }
    }
}

function Enter-GatewayOffline {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result)
    $offline = Join-Path $Result.TargetPath $script:AppOfflineFileName
    $running = Test-Path -LiteralPath (Join-Path $Result.TargetPath $Result.EntryFileName) -PathType Leaf
    if (-not $running) { return $null }
    Add-GatewayAction $Result "Take the gateway offline with $($script:AppOfflineFileName) while files change"
    if (-not $PSCmdlet.ShouldProcess($offline, 'Write the offline page')) { return $null }
    [IO.File]::WriteAllText($offline, $script:AppOfflineHtml, (New-Object Text.UTF8Encoding($false)))
    Start-Sleep -Seconds $script:ShutdownWaitSeconds
    return $offline
}

function Exit-GatewayOffline([string] $OfflinePath) {
    if ([string]::IsNullOrEmpty($OfflinePath) -or -not (Test-Path -LiteralPath $OfflinePath)) { return }
    Remove-Item -LiteralPath $OfflinePath -Force -WhatIf:$false
}

function Get-GatewayGapPageHtml([string[]] $Gaps) {
    $items = @($Gaps | ForEach-Object { $script:GapPageItemFormat -f [Net.WebUtility]::HtmlEncode($_) }) -join ''
    return $script:GapPageHtmlFormat -f $items
}

# With gaps the gateway must not start: the gap page stays as app_offline.htm. Without gaps any page is removed,
# including the gap page a previous run left.
function Set-GatewayStartState {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result)
    $offline = Join-Path $Result.TargetPath $script:AppOfflineFileName
    if ($Result.Gaps.Count -gt 0) {
        $Result.StartRefused = $true
        Add-GatewayAction $Result "Refuse to start: keep $($script:AppOfflineFileName) listing $($Result.Gaps.Count) gap(s) until they are filled"
        if ($PSCmdlet.ShouldProcess($offline, 'Write the configuration-incomplete page')) {
            [IO.File]::WriteAllText($offline, (Get-GatewayGapPageHtml $Result.Gaps), (New-Object Text.UTF8Encoding($false)))
        }
        return
    }
    if (-not (Test-Path -LiteralPath $offline)) { return }
    Add-GatewayAction $Result "Start: remove $($script:AppOfflineFileName)"
    if ($PSCmdlet.ShouldProcess($offline, 'Remove the offline page')) { Remove-Item -LiteralPath $offline -Force; Write-GatewayLog 'The gateway is back online.' -Level $script:LogSuccess }
}

function Invoke-GatewaySettingsMerge([object] $Result, [string] $ProductionTemplatePath) {
    $merges = @()
    $pairs = @(
        @{ Template = (Join-Path $Result.AppFolder $script:BaseSettingsFileName); Server = (Join-Path $Result.TargetPath $script:BaseSettingsFileName) },
        @{ Template = $ProductionTemplatePath; Server = (Join-Path $Result.TargetPath $script:ProductionSettingsFileName) }
    )
    foreach ($pair in $pairs) {
        $merge = Merge-AppSettings -TemplatePath $pair.Template -ServerPath $pair.Server -WhatIf:$Result.Preview
        $name = Split-Path -Leaf $pair.Server
        if ($merge.Created) { Add-GatewayAction $Result "Settings ${name}: create from the package template" }
        elseif (@($merge.AddedKeys).Count -gt 0) { Add-GatewayAction $Result "Settings ${name}: add new key(s) $(@($merge.AddedKeys) -join ', ') (server values kept)" }
        else { Add-GatewayAction $Result "Settings ${name}: no new keys (server values kept)" }
        $merges += $merge
    }
    $Result.Settings = $merges
}

function Invoke-GatewaySettingsCheck([object] $Result, [string] $ProductionTemplatePath, [string] $RequirementsPath, [object] $TlsPlan) {
    $layers = Get-GatewaySettingsLayers -AppFolder $Result.AppFolder -TargetPath $Result.TargetPath -ProductionTemplatePath $ProductionTemplatePath
    $requirements = Read-GatewayRequirements $RequirementsPath
    $Result.SettingsGaps = [string[]] @(Get-GatewaySettingsGaps -Effective (Get-GatewayEffectiveSettings $layers) -Requirements $requirements)
    $all = @($Result.SettingsGaps)
    if ($TlsPlan.Gap) { $all += $TlsPlan.Gap }
    $Result.Gaps = [string[]] $all
    if ($Result.Gaps.Count -eq 0) { Write-GatewayLog 'Required settings and the TLS certificate: all in place.' -Level $script:LogSuccess; return }
    $file = Join-Path $Result.TargetPath $script:ProductionSettingsFileName
    Write-GatewayLog "Gaps; fill the settings in '$file' (see templates\gateway\README.md):" -Level $script:LogWarning
    foreach ($gap in $Result.Gaps) { Write-GatewayLog "  $gap" -Level $script:LogWarning }
}

function Invoke-GatewayHealthStep([object] $Result) {
    if ($Result.Preview) {
        $Result.Health = [int] [GatewayHealthOutcome]::SkippedPreview
        Add-GatewayAction $Result "Health probe skipped (-WhatIf): a real run waits for GET $($Result.HealthUrl) to answer 200 over validated TLS"
        return
    }
    if ($Result.Gaps.Count -gt 0) {
        $Result.Health = [int] [GatewayHealthOutcome]::SkippedGaps
        Write-GatewayLog 'Health probe skipped: the gateway is held offline until the gaps above are filled. Fill them, then run the kit again.' -Level $script:LogWarning
        return
    }
    Write-GatewayLog "Waiting for GET $($Result.HealthUrl) (the first start is slow)..." -Level $script:LogStep
    $probe = Wait-GatewayHealth -Url $Result.HealthUrl
    $Result.HealthStatusCode = $probe.StatusCode
    if ($probe.StatusCode -eq $script:HttpOk) {
        $Result.Health = [int] [GatewayHealthOutcome]::Healthy
        Write-GatewayLog "$($Result.HealthUrl) answers 200." -Level $script:LogSuccess
        return
    }
    $Result.Health = [int] [GatewayHealthOutcome]::Unhealthy
    Write-GatewayLog "Health check failed: $(Get-GatewayHealthDiagnosis $probe)" -Level $script:LogError
    $rollback = if ($Result.BackupPath) { " Roll back: copy '$($Result.BackupPath)\site\*' over '$($Result.TargetPath)'." } else { '' }
    throw "The gateway is deployed but $($Result.HealthUrl) did not answer 200.$rollback"
}

function Invoke-GatewayDeployFiles([object] $Result, [string] $BackupRoot, [string] $ProductionTemplatePath, [string] $RequirementsPath, [object] $TlsPlan) {
    $Result.BackupPath = Backup-Deployment -SourcePath $Result.TargetPath -BackupRoot $BackupRoot -Name $script:BackupName -WhatIf:$Result.Preview
    if ($Result.BackupPath) { Add-GatewayAction $Result "Back up '$($Result.TargetPath)' to '$($Result.BackupPath)'" }
    else { Add-GatewayAction $Result 'Backup: nothing deployed yet (first install)' }
    $offline = Enter-GatewayOffline $Result
    $decided = $false
    try {
        Add-GatewayAction $Result "Mirror '$($Result.AppFolder)' to '$($Result.TargetPath)' (server settings, $($script:AppOfflineFileName) and logs kept)"
        $Result.Payload = Copy-KitPayload -SourcePath $Result.AppFolder -DestinationPath $Result.TargetPath -WhatIf:$Result.Preview
        Invoke-GatewaySettingsMerge -Result $Result -ProductionTemplatePath $ProductionTemplatePath
        Invoke-GatewaySettingsCheck -Result $Result -ProductionTemplatePath $ProductionTemplatePath -RequirementsPath $RequirementsPath -TlsPlan $TlsPlan
        Set-GatewayStartState $Result
        $decided = $true
    }
    finally { if (-not $decided) { Exit-GatewayOffline $offline } }
}

# ==================================================================================================================
# The kit
# ==================================================================================================================
function Invoke-GatewayKit {
    <#
    .SYNOPSIS
        Runs the gateway kit (see the script help). Returns the result object: Preview, AppFolder, EntryFileName,
        TargetPath, PrerequisitesPassed, IisPlan, TlsPlan, BackupPath, Payload, Settings, SettingsGaps, Gaps (settings
        + TLS), StartRefused, HealthUrl, Health (GatewayHealthOutcome as int), HealthStatusCode, PlannedActions.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string] $PackagePath,
        [string] $SiteName,
        [string] $AppPath = $script:DefaultAppPath,
        [string] $AppPoolName = $script:DefaultAppPoolName,
        [string] $TargetPath = (Join-Path $env:SystemDrive $script:DefaultTargetRelative),
        [string] $BackupRoot = (Join-Path $env:SystemDrive $script:DefaultBackupRelative),
        [string] $CertificateThumbprint,
        [int] $HttpsPort = $script:DefaultHttpsPort,
        [string] $HostName,
        [string] $HealthBaseUrl,
        [switch] $AdoptSite
    )
    if ([string]::IsNullOrWhiteSpace($HealthBaseUrl)) { $HealthBaseUrl = Get-GatewayDefaultHealthBaseUrl -HostName $HostName -HttpsPort $HttpsPort }
    Assert-GatewayKitParameters -PackagePath $PackagePath -SiteName $SiteName -AppPoolName $AppPoolName -TargetPath $TargetPath -BackupRoot $BackupRoot -HttpsPort $HttpsPort -HealthBaseUrl $HealthBaseUrl
    $script:KitLogPath = $null
    $result = New-GatewayKitResult -Preview ([bool] $WhatIfPreference) -TargetPath $TargetPath -HealthUrl (Get-GatewayHealthUrl -BaseUrl $HealthBaseUrl -AppPath $AppPath)
    $mode = if ($result.Preview) { 'preview (-WhatIf): nothing is changed' } else { 'install' }
    Write-GatewayLog "Integration gateway kit: $mode. Site '$SiteName', application '$AppPath', pool '$AppPoolName', folder '$TargetPath'." -Level $script:LogStep

    $package = Resolve-GatewayPackage $PackagePath
    $result.AppFolder = $package.AppFolder
    $result.EntryFileName = $package.EntryFileName
    if ((Test-GatewayPathInside -Path $result.AppFolder -Parent $TargetPath) -or (Test-GatewayPathInside -Path $TargetPath -Parent $result.AppFolder)) {
        throw "The package '$($result.AppFolder)' overlaps -TargetPath '$TargetPath'; copy the package somewhere else first. Nothing was changed."
    }
    $requirementsPath = Join-Path $script:KitRoot $script:RequirementsRelative
    Read-GatewayRequirements $requirementsPath | Out-Null
    Invoke-GatewayPrerequisiteStep $result
    $tlsPlan = Get-GatewayTlsStepPlan -Result $result -SiteName $SiteName -Thumbprint $CertificateThumbprint -HttpsPort $HttpsPort -HostName $HostName
    # The log file starts once every check that can refuse has passed: a refused run leaves no trace on the server.
    $script:KitLogPath = Join-Path $BackupRoot $script:LogFileName
    Invoke-GatewayIisStep -Result $result -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName -AdoptSite ([bool] $AdoptSite)
    Invoke-GatewayTlsStep -Result $result -Plan $tlsPlan -SiteName $SiteName -Thumbprint $CertificateThumbprint -HttpsPort $HttpsPort -HostName $HostName -WhatIf:$result.Preview

    $productionTemplate = Join-Path $script:KitRoot $script:ProductionTemplateRelative
    if (-not (Test-Path -LiteralPath $productionTemplate -PathType Leaf)) { throw "The kit template '$productionTemplate' is missing. Nothing was changed." }
    Invoke-GatewayDeployFiles -Result $result -BackupRoot $BackupRoot -ProductionTemplatePath $productionTemplate -RequirementsPath $requirementsPath -TlsPlan $tlsPlan
    Invoke-GatewayHealthStep $result
    return $result
}

# Dot-sourced (the Pester tests): define the functions only.
if ($MyInvocation.InvocationName -eq '.') { return }

$kitResult = Invoke-GatewayKit @PSBoundParameters
if (-not $kitResult.Preview -and $kitResult.Gaps.Count -gt 0) { exit $script:ExitGaps }
