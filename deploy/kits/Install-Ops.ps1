#Requires -Version 5.1
<#
.SYNOPSIS
    Installs or updates the Ops Console (API + web) as two child IIS applications under an existing site.

.DESCRIPTION
    A deploy kit built on Install-Common.psm1 (MODULE.md in this folder). The Ops Console is optional: a fork that
    does not ship it drops this script and templates\ops (init). It never creates a site: both applications go under
    the site you name, so no new binding, certificate or firewall rule is needed. The steps, in order:

      1. Checks the parameters and the package (refuses before anything changes). The package holds api\ (the
         published console API; its entry assembly is read from web.config) and web\ (the built console screens,
         whose index.html must carry <base href="<WebAppPath>/">), and that no URL Rewrite rule of the site's own
         web.config (the front site's SPA fallback, inherited by child applications) would rewrite the two paths.
      2. Prerequisites: IIS, WebAdministration, the ASP.NET Core Module V2, the ASP.NET Core 10 runtime and IIS
         Windows Authentication (Web-Windows-Auth): operators sign in with their Windows account and AD group.
      3. IIS: two app pools ("No Managed Code") and two child applications under -SiteName (the API at -ApiAppPath,
         the web at -WebAppPath), via Set-KitIisApplication. The site must exist.
      4. Authentication on the API application: Windows Authentication on, Anonymous off.
      5. Backup of both deployed folders (logs excluded).
      6. API: app_offline.htm, payload mirror, Merge-AppSettings for appsettings.json and appsettings.Production.json
         (server values kept, only new keys added). Web: payload mirror.
      7. Gaps: every required key of templates\ops\required-settings.json that is still empty or malformed (paths
         only, never values): the console API's base URL, the operator AD group, the Seq read key and the S2S signing
         key. With a gap the kit REFUSES TO START the console API: it leaves app_offline.htm in place (it lists what
         is missing) and exits 2. A run with no gap removes it.
      8. Health: GET <HealthBaseUrl><ApiAppPath>/health/ready (as the signed-in operator, Windows auth) and
         GET <HealthBaseUrl><WebAppPath>/ (the console's index.html), over https with normal certificate
         validation (the kit never turns validation off). Skipped under -WhatIf and while gaps remain.

    Under -WhatIf nothing is written: no IIS change, authentication change, folder, backup, copy, settings file or
    log file. Every planned action is printed instead.

    Exit codes (when run as a script): 0 done (or previewed), 1 refused or failed, 2 installed but gaps remain.

.PARAMETER PackagePath
    The Ops Console package: a folder with api\ (published API) and web\ (built screens).
.PARAMETER SiteName
    The existing IIS site both child applications go under.
.PARAMETER ApiAppPath
    The console API application. Default '/ops-api' (the path the screens call).
.PARAMETER WebAppPath
    The console screens application. Default '/ops'.
.PARAMETER ApiAppPoolName
    The API app pool. Default 'StarterOpsApi'.
.PARAMETER WebAppPoolName
    The web app pool. Default 'StarterOpsWeb'.
.PARAMETER ApiTargetPath
    The folder IIS serves the API from. Default <SystemDrive>\inetpub\starter-ops-api.
.PARAMETER WebTargetPath
    The folder IIS serves the screens from. Default <SystemDrive>\inetpub\starter-ops-web.
.PARAMETER BackupRoot
    Where backups and the install log go. Default <SystemDrive>\inetpub\backups\starter-ops.
.PARAMETER HealthBaseUrl
    The site's https base URL as seen from this server, on a name its certificate covers. Default https://<this
    server's DNS name>.

.EXAMPLE
    .\Install-Ops.ps1 -PackagePath .\ops -SiteName 'Starter API' -HealthBaseUrl 'https://app.example.test' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $PackagePath,
    [string] $SiteName,
    [string] $ApiAppPath,
    [string] $WebAppPath,
    [string] $ApiAppPoolName,
    [string] $WebAppPoolName,
    [string] $ApiTargetPath,
    [string] $WebTargetPath,
    [string] $BackupRoot,
    [string] $HealthBaseUrl
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Enums (integers with explicit values) ------------------------------------------------------------------------
enum OpsHealthOutcome {
    NotRun = 0
    SkippedPreview = 1
    SkippedGaps = 2
    Healthy = 3
    Unhealthy = 4
}

enum OpsAuthAction {
    None = 0
    EnableWindowsAuthentication = 1
    DisableAnonymousAuthentication = 2
}

# required-settings.json "format": what a filled value must look like.
enum OpsSettingFormat {
    Value = 1
    HttpsUrl = 2
}

# --- Constants ----------------------------------------------------------------------------------------------------
$script:CommonModuleFileName        = 'Install-Common.psm1'
$script:ProductionTemplateRelative  = 'templates\ops\appsettings.template.json'
$script:RequirementsRelative        = 'templates\ops\required-settings.json'
$script:ApiBackupName               = 'ops-api'
$script:WebBackupName               = 'ops-web'
$script:LogFileName                 = 'install-ops.log'
$script:RuntimeMajor                = 10

$script:DefaultApiAppPath           = '/ops-api'
$script:DefaultWebAppPath           = '/ops'
$script:DefaultApiAppPoolName       = 'StarterOpsApi'
$script:DefaultWebAppPoolName       = 'StarterOpsWeb'
$script:DefaultApiTargetRelative    = 'inetpub\starter-ops-api'
$script:DefaultWebTargetRelative    = 'inetpub\starter-ops-web'
$script:DefaultBackupRelative       = 'inetpub\backups\starter-ops'

# The package: api\ (published console API, entry file named by web.config) and web\ (built screens).
$script:PackageApiFolder            = 'api'
$script:PackageWebFolder            = 'web'
$script:WebConfigFileName           = 'web.config'
$script:IndexFileName               = 'index.html'
$script:BaseSettingsFileName        = 'appsettings.json'
$script:ProductionSettingsFileName  = 'appsettings.Production.json'
$script:AspNetCoreXPath             = '//aspNetCore'
$script:AspNetCoreDotnetHost        = 'dotnet'
$script:EntryFilePattern            = '[^\\/\s"]+\.(dll|exe)'
$script:BaseHrefFormat              = '<base href="{0}/">'

# Settings (.NET configuration paths). The required ones are data: templates\ops\required-settings.json.
$script:ConfigSeparator             = ':'
$script:RequirementWildcard         = '*'
$script:JsonTrueText                = 'true'
$script:EmptyContainer              = New-Object -TypeName psobject
$script:HttpsScheme                 = 'https'
$script:HttpsUrlHint                = ' (must be an absolute https URL)'

# Paths.
$script:DriveRootedPattern          = '^[A-Za-z]:\\'
$script:UncPattern                  = '^\\\\[^\\]+\\[^\\]+'
$script:PathSeparator               = '\'
$script:InvalidPoolNameChars        = [char[]] '\/:*?"<>|'
$script:UrlSeparator                = '/'

# IIS authentication on the API application (applicationHost.config, location <site><app>).
$script:IisConfigPsPath             = 'MACHINE/WEBROOT/APPHOST'
$script:WindowsAuthFilter           = 'system.webServer/security/authentication/windowsAuthentication'
$script:AnonymousAuthFilter         = 'system.webServer/security/authentication/anonymousAuthentication'
$script:EnabledProperty             = 'enabled'
$script:WindowsAuthModuleRelative   = 'System32\inetsrv\authsspi.dll'
$script:WindowsAuthFeatureName      = 'Web-Windows-Auth'

# app_offline.htm: ANCM stops the app while it exists. The kit uses it twice: during the copy, and to refuse to
# start a console API whose settings are incomplete.
$script:AppOfflineFileName          = 'app_offline.htm'
$script:AppOfflineHtml              = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Update in progress</title></head><body><p>The service is being updated and will be back in a moment.</p></body></html>'
$script:GapPageHtmlFormat           = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Configuration incomplete</title></head><body><p>The deploy kit did not start this service: required settings are missing. Fill them, then run the kit again.</p><ul>{0}</ul></body></html>'
$script:GapPageItemFormat           = '<li>{0}</li>'
$script:ShutdownWaitSeconds         = 3

# Health probes (validated TLS only; there is no switch that skips certificate validation).
$script:HealthPath                  = '/health/ready'  # HealthEndpoints.ReadyPath (Codelab.Observability)
$script:HealthAttempts              = 12
$script:HealthDelaySeconds          = 5
$script:HealthRequestTimeoutSeconds = 10
$script:HttpOk                      = 200
$script:NoHttpStatus                = 0
$script:HtmlBodyStart               = '<'

$script:ExitGaps                    = 2

# URL Rewrite rules of the site's own web.config: IIS hands them down to every child application, so the front
# site's SPA fallback must exclude the console paths (deploy/kits/templates/web/web.config).
$script:IisSitesPrefix              = 'IIS:\Sites\'
$script:RewriteRulesXPath           = '/configuration/system.webServer/rewrite/rules/rule'
$script:RewriteActionType           = 'Rewrite'
$script:UrlServerVariable           = '{URL}'
$script:MatchAnyGrouping            = 'MatchAny'
$script:PatternMatchType            = 'Pattern'
$script:XmlTrueText                 = 'true'
# Element and attribute names of a URL Rewrite <rule>.
$script:RuleActionElement           = 'action'
$script:RuleMatchElement            = 'match'
$script:RuleConditionsElement       = 'conditions'
$script:RuleConditionElement        = 'add'
$script:ActionTypeAttribute         = 'type'
$script:MatchUrlAttribute           = 'url'
$script:ConditionInputAttribute     = 'input'
$script:ConditionPatternAttribute   = 'pattern'
$script:ConditionMatchTypeAttribute = 'matchType'
$script:GroupingAttribute           = 'logicalGrouping'
$script:IgnoreCaseAttribute         = 'ignoreCase'
$script:NegateAttribute             = 'negate'

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
function Write-OpsLog([string] $Message, [string] $Level = $script:LogInfo) {
    $logPath = if ($WhatIfPreference) { $null } else { $script:KitLogPath }
    Write-KitLog -Message $Message -Level $Level -LogPath $logPath -WhatIf:$false
}

function Add-OpsAction([object] $Result, [string] $Text) {
    $Result.PlannedActions.Add($Text)
    Write-OpsLog $Text -Level $script:LogStep
}

# ==================================================================================================================
# Parameters, paths and the package (pure checks; they throw before anything changes)
# ==================================================================================================================
function Test-OpsFullyQualifiedPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return ($Path -match $script:DriveRootedPattern -or $Path -match $script:UncPattern)
}

function ConvertTo-OpsComparablePath([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd($script:PathSeparator)
}

# True when $Path is $Parent itself or anything below it (case-insensitive, like NTFS).
function Test-OpsPathInside([string] $Path, [string] $Parent) {
    $child = ConvertTo-OpsComparablePath $Path
    $root = ConvertTo-OpsComparablePath $Parent
    if ([string]::Equals($child, $root, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $child.StartsWith($root + $script:PathSeparator, [StringComparison]::OrdinalIgnoreCase)
}

function Test-OpsPathsOverlap([string] $Left, [string] $Right) {
    return ((Test-OpsPathInside -Path $Left -Parent $Right) -or (Test-OpsPathInside -Path $Right -Parent $Left))
}

function Test-OpsHttpsUrl([object] $Value) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { return $false }
    $uri = $null
    if (-not [Uri]::TryCreate($Value.Trim(), [UriKind]::Absolute, [ref] $uri)) { return $false }
    return [string]::Equals($uri.Scheme, $script:HttpsScheme, [StringComparison]::OrdinalIgnoreCase)
}

# '/ops' style: one leading slash, no trailing slash. An empty result means the site root.
function ConvertTo-OpsAppPath([string] $AppPath) {
    $trimmed = ([string] $AppPath).Trim().Trim($script:UrlSeparator)
    if ([string]::IsNullOrEmpty($trimmed)) { return '' }
    return $script:UrlSeparator + $trimmed
}

function Assert-OpsKitParameters {
    param([string] $PackagePath, [string] $SiteName, [string] $ApiAppPath, [string] $WebAppPath, [string] $ApiAppPoolName, [string] $WebAppPoolName,
          [string] $ApiTargetPath, [string] $WebTargetPath, [string] $BackupRoot, [string] $HealthBaseUrl)
    if ([string]::IsNullOrWhiteSpace($PackagePath)) { throw '-PackagePath is required: the Ops Console package (api\ and web\). Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($SiteName)) { throw '-SiteName is required: the existing IIS site the console applications go under. Nothing was changed.' }
    foreach ($pair in @(@('-ApiAppPath', $ApiAppPath), @('-WebAppPath', $WebAppPath))) {
        if ((ConvertTo-OpsAppPath $pair[1]).Length -eq 0) { throw "$($pair[0]) must name a child application (the console never takes over the site root). Nothing was changed." }
    }
    if ([string]::Equals((ConvertTo-OpsAppPath $ApiAppPath), (ConvertTo-OpsAppPath $WebAppPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw '-ApiAppPath and -WebAppPath must differ. Nothing was changed.'
    }
    foreach ($pool in @($ApiAppPoolName, $WebAppPoolName)) {
        if ([string]::IsNullOrWhiteSpace($pool) -or $pool.IndexOfAny($script:InvalidPoolNameChars) -ge 0) { throw "App pool name '$pool' is not valid. Nothing was changed." }
    }
    if ([string]::Equals($ApiAppPoolName, $WebAppPoolName, [StringComparison]::OrdinalIgnoreCase)) {
        throw '-ApiAppPoolName and -WebAppPoolName must differ: an in-process ASP.NET Core application needs a pool of its own. Nothing was changed.'
    }
    foreach ($pair in @(@('-ApiTargetPath', $ApiTargetPath), @('-WebTargetPath', $WebTargetPath), @('-BackupRoot', $BackupRoot))) {
        if (-not (Test-OpsFullyQualifiedPath $pair[1])) { throw "$($pair[0]) '$($pair[1])' must be a fully qualified folder. Nothing was changed." }
    }
    if (Test-OpsPathsOverlap $ApiTargetPath $WebTargetPath) { throw "-ApiTargetPath and -WebTargetPath overlap; each mirror would delete the other's files. Nothing was changed." }
    foreach ($target in @($ApiTargetPath, $WebTargetPath)) {
        if (Test-OpsPathInside -Path $BackupRoot -Parent $target) { throw "-BackupRoot '$BackupRoot' is inside '$target'; the payload mirror would delete the backups. Nothing was changed." }
    }
    if (-not (Test-OpsHttpsUrl $HealthBaseUrl)) {
        throw "-HealthBaseUrl '$HealthBaseUrl' must be an absolute https URL: the health probes validate the certificate and never skip it. Nothing was changed."
    }
}

# The entry file named by web.config: processPath '.\X.exe', or processPath 'dotnet' with arguments '.\X.dll'.
function Get-OpsEntryFileName([string] $WebConfigPath) {
    $document = New-Object Xml.XmlDocument
    try { $document.Load($WebConfigPath) }
    catch { throw "'$WebConfigPath' is not valid XML. Nothing was changed." }
    $node = $document.SelectSingleNode($script:AspNetCoreXPath)
    if ($null -eq $node) { throw "'$WebConfigPath' has no aspNetCore element; api\ is not an ASP.NET Core publish output. Nothing was changed." }
    $processPath = [string] $node.GetAttribute('processPath')
    $source = if ([string]::Equals($processPath, $script:AspNetCoreDotnetHost, [StringComparison]::OrdinalIgnoreCase)) { [string] $node.GetAttribute('arguments') } else { $processPath }
    $match = [regex]::Match($source, $script:EntryFilePattern)
    if (-not $match.Success) { throw "'$WebConfigPath' names no entry .dll or .exe in its aspNetCore element. Nothing was changed." }
    return $match.Value
}

function Test-OpsIndexBaseHref([string] $Html, [string] $WebAppPath) {
    if ([string]::IsNullOrEmpty($Html)) { return $false }
    return $Html.IndexOf(($script:BaseHrefFormat -f (ConvertTo-OpsAppPath $WebAppPath)), [StringComparison]::OrdinalIgnoreCase) -ge 0
}

# Returns ApiFolder, EntryFileName and WebFolder of a complete package, or throws.
function Resolve-OpsPackage([string] $PackagePath, [string] $WebAppPath) {
    if (-not (Test-Path -LiteralPath $PackagePath -PathType Container)) { throw "Package folder '$PackagePath' does not exist. Nothing was changed." }
    $api = Join-Path $PackagePath $script:PackageApiFolder
    $web = Join-Path $PackagePath $script:PackageWebFolder
    foreach ($folder in @($api, $web)) {
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) { throw "The package has no $(Split-Path -Leaf $folder)\ folder; give the Ops Console package (api\ and web\). Nothing was changed." }
    }
    $webConfig = Join-Path $api $script:WebConfigFileName
    if (-not (Test-Path -LiteralPath $webConfig -PathType Leaf)) { throw "The package's api\ has no $($script:WebConfigFileName); it is not a publish output. Nothing was changed." }
    $entry = Get-OpsEntryFileName $webConfig
    foreach ($required in @($entry, $script:BaseSettingsFileName)) {
        if (-not (Test-Path -LiteralPath (Join-Path $api $required) -PathType Leaf)) { throw "The package's api\ has no $required; it is not a complete publish output. Nothing was changed." }
    }
    $index = Join-Path $web $script:IndexFileName
    if (-not (Test-Path -LiteralPath $index -PathType Leaf)) { throw "The package's web\ has no $($script:IndexFileName); it is not a console build. Nothing was changed." }
    $html = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $index).ProviderPath)
    if (-not (Test-OpsIndexBaseHref -Html $html -WebAppPath $WebAppPath)) {
        throw "web\$($script:IndexFileName) does not carry $($script:BaseHrefFormat -f (ConvertTo-OpsAppPath $WebAppPath)): build the screens for -WebAppPath '$WebAppPath' (ng build --base-href). Nothing was changed."
    }
    return [pscustomobject]@{
        ApiFolder = (Resolve-Path -LiteralPath $api).ProviderPath; EntryFileName = $entry; WebFolder = (Resolve-Path -LiteralPath $web).ProviderPath
    }
}

# ==================================================================================================================
# Settings: read, layer like .NET configuration, find the required keys that are empty or malformed
# ==================================================================================================================
function Read-OpsJson([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $text = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).ProviderPath, (New-Object Text.UTF8Encoding($false)))
    # The parser error is not passed on: it quotes file text, and settings files hold key material.
    try { return (ConvertFrom-Json -InputObject $text -ErrorAction Stop) }
    catch { throw "'$Path' is not valid JSON (comments and trailing commas are not supported). Nothing was changed." }
}

function Test-OpsJsonObject([object] $Value) {
    return ($null -ne $Value -and $Value -is [System.Management.Automation.PSCustomObject])
}

function Test-OpsJsonArray([object] $Value) {
    return ($null -ne $Value -and $Value -isnot [string] -and $Value -is [System.Collections.IList])
}

# Flattens a JSON object into "A:B:0:C" -> value, the way .NET configuration keys it. An empty object or array is
# recorded as the EmptyContainer marker, so a required section that is present but empty is still seen.
function Add-OpsSettingLeaves([object] $Node, [string] $Prefix, [hashtable] $Into) {
    if (Test-OpsJsonObject $Node) {
        $members = @($Node.PSObject.Properties)
        if ($members.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        foreach ($member in $members) {
            $path = if ($Prefix) { $Prefix + $script:ConfigSeparator + $member.Name } else { $member.Name }
            Add-OpsSettingLeaves -Node $member.Value -Prefix $path -Into $Into
        }
        return
    }
    if (Test-OpsJsonArray $Node) {
        if ($Node.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        for ($index = 0; $index -lt $Node.Count; $index++) {
            Add-OpsSettingLeaves -Node $Node[$index] -Prefix ($Prefix + $script:ConfigSeparator + $index) -Into $Into
        }
        return
    }
    $Into[$Prefix] = $Node
}

# Layers are lowest precedence first; a later layer overrides a leaf (case-insensitive keys, like .NET configuration).
function Get-OpsEffectiveSettings([object[]] $Layers) {
    $leaves = @{}
    foreach ($layer in @($Layers | Where-Object { $null -ne $_ })) { Add-OpsSettingLeaves -Node $layer -Prefix '' -Into $leaves }
    return $leaves
}

function Test-OpsEmptyValue([object] $Value) {
    if ($null -eq $Value) { return $true }
    return ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value))
}

function Get-OpsSettingValue([hashtable] $Effective, [string] $Path) {
    if (-not $Effective.ContainsKey($Path)) { return $null }
    $value = $Effective[$Path]
    if ([object]::ReferenceEquals($value, $script:EmptyContainer)) { return $null }
    return $value
}

# Reads templates\ops\required-settings.json: { requirements: [ { path, format (OpsSettingFormat), when? } ] }.
function Read-OpsRequirements([string] $Path) {
    $document = Read-OpsJson $Path
    if ($null -eq $document -or -not (Test-OpsJsonArray $document.requirements)) { throw "The kit file '$Path' is missing or has no requirements array. Nothing was changed." }
    $known = [int[]] [Enum]::GetValues([OpsSettingFormat])
    foreach ($requirement in $document.requirements) {
        if ([string]::IsNullOrWhiteSpace([string] $requirement.path)) { throw "The kit file '$Path' has a requirement without a path. Nothing was changed." }
        if ($known -notcontains [int] $requirement.format) { throw "The kit file '$Path' has requirement '$($requirement.path)' with an unknown format $($requirement.format). Nothing was changed." }
        $wildcards = @($requirement.path.Split($script:ConfigSeparator) | Where-Object { $_ -eq $script:RequirementWildcard }).Count
        if ($wildcards -gt 1) { throw "The kit file '$Path' has requirement '$($requirement.path)' with more than one '*'. Nothing was changed." }
    }
    return [object[]] @($document.requirements | ForEach-Object {
        $when = if ($_.PSObject.Properties['when']) { [string] $_.when } else { '' }
        [pscustomobject]@{ Path = [string] $_.path; Format = [int] $_.format; When = $when }
    })
}

# A requirement with a '*' segment applies to every index of that array (the 'when' path shares the index). An
# array with no items yields index 0, so the gap still names a concrete key.
function Get-OpsRequirementInstances([hashtable] $Effective, [object] $Requirement) {
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
            When = $Requirement.When.Replace($marker, $script:ConfigSeparator + $_)
        }
    })
}

function Test-OpsRequirementApplies([hashtable] $Effective, [string] $When) {
    if ([string]::IsNullOrWhiteSpace($When)) { return $true }
    $value = Get-OpsSettingValue -Effective $Effective -Path $When
    if ($value -is [bool]) { return $value }
    return [string]::Equals([string] $value, $script:JsonTrueText, [StringComparison]::OrdinalIgnoreCase)
}

<#
    Returns the required configuration paths that are missing, empty or malformed in the effective settings (sorted).
    A section counts as filled only when it has at least one value and none of its values is empty. Values are never
    returned.
#>
function Get-OpsSettingsGaps([hashtable] $Effective, [object[]] $Requirements) {
    $gaps = New-Object 'System.Collections.Generic.List[string]'
    foreach ($instance in @($Requirements | ForEach-Object { Get-OpsRequirementInstances -Effective $Effective -Requirement $_ })) {
        if (-not (Test-OpsRequirementApplies -Effective $Effective -When $instance.When)) { continue }
        $below = $instance.Path + $script:ConfigSeparator
        $keys = @($Effective.Keys | Where-Object {
            ($_ -eq $instance.Path -or $_.StartsWith($below, [StringComparison]::OrdinalIgnoreCase)) -and
            -not [object]::ReferenceEquals($Effective[$_], $script:EmptyContainer)
        })
        if ($keys.Count -eq 0) { $gaps.Add($instance.Path); continue }
        foreach ($key in $keys) {
            if (Test-OpsEmptyValue $Effective[$key]) { $gaps.Add($key); continue }
            if ($instance.Format -eq [int] [OpsSettingFormat]::HttpsUrl -and -not (Test-OpsHttpsUrl $Effective[$key])) { $gaps.Add($key + $script:HttpsUrlHint) }
        }
    }
    return [string[]] @($gaps | Sort-Object -Unique)
}

# The settings as they are (or will be) after the merge: package template < server file, per file, base < Production.
function Get-OpsSettingsLayers([string] $ApiFolder, [string] $TargetPath, [string] $ProductionTemplatePath) {
    return @(
        (Read-OpsJson (Join-Path $ApiFolder $script:BaseSettingsFileName)),
        (Read-OpsJson (Join-Path $TargetPath $script:BaseSettingsFileName)),
        (Read-OpsJson $ProductionTemplatePath),
        (Read-OpsJson (Join-Path $TargetPath $script:ProductionSettingsFileName))
    )
}

# ==================================================================================================================
# Windows Authentication on the API application: facts (read-only), plan (pure), apply
# ==================================================================================================================
function Get-OpsIisLocation([string] $SiteName, [string] $AppPath) {
    return $SiteName + (ConvertTo-OpsAppPath $AppPath)
}

function Get-OpsAuthFacts([string] $SiteName, [string] $AppPath) {
    $facts = [pscustomobject]@{ Known = $false; WindowsModuleInstalled = $false; WindowsEnabled = $false; AnonymousEnabled = $true }
    $facts.WindowsModuleInstalled = Test-Path -LiteralPath (Join-Path $env:SystemRoot $script:WindowsAuthModuleRelative) -PathType Leaf
    try {
        Import-Module WebAdministration -ErrorAction Stop -Verbose:$false
        $location = Get-OpsIisLocation -SiteName $SiteName -AppPath $AppPath
        $facts.WindowsEnabled = [bool] (Get-WebConfigurationProperty -PSPath $script:IisConfigPsPath -Location $location -Filter $script:WindowsAuthFilter -Name $script:EnabledProperty -ErrorAction Stop).Value
        $facts.AnonymousEnabled = [bool] (Get-WebConfigurationProperty -PSPath $script:IisConfigPsPath -Location $location -Filter $script:AnonymousAuthFilter -Name $script:EnabledProperty -ErrorAction Stop).Value
        $facts.Known = $true
    }
    catch { Write-OpsLog "The API application's authentication settings could not be read ($($_.Exception.Message)); planned as the IIS defaults." -Level $script:LogWarning }
    return $facts
}

function New-OpsAuthStep([OpsAuthAction] $Action, [string] $Detail) {
    return [pscustomobject]@{ Action = $Action.ToString(); ActionId = [int] $Action; Detail = $Detail; Applied = $false }
}

<#
    Pure: the same facts always give the same plan. Returns Steps (OpsAuthAction) and MissingModule ($true when IIS
    Windows Authentication is not installed: a real run refuses, a preview warns).
#>
function Get-OpsAuthPlan([object] $Facts, [string] $Location) {
    $steps = New-Object 'System.Collections.Generic.List[object]'
    if (-not $Facts.WindowsEnabled) { $steps.Add((New-OpsAuthStep EnableWindowsAuthentication "turn Windows Authentication on for '$Location'")) }
    if ($Facts.AnonymousEnabled) { $steps.Add((New-OpsAuthStep DisableAnonymousAuthentication "turn Anonymous Authentication off for '$Location'")) }
    if ($steps.Count -eq 0) { $steps.Add((New-OpsAuthStep None "'$Location' already uses Windows Authentication only")) }
    return [pscustomobject]@{ Steps = [object[]] $steps.ToArray(); MissingModule = (-not $Facts.WindowsModuleInstalled) }
}

function Invoke-OpsAuthAction([object] $Step, [string] $Location) {
    Import-Module WebAdministration -ErrorAction Stop -Verbose:$false
    switch ([OpsAuthAction] $Step.ActionId) {
        ([OpsAuthAction]::EnableWindowsAuthentication) {
            Set-WebConfigurationProperty -PSPath $script:IisConfigPsPath -Location $Location -Filter $script:WindowsAuthFilter -Name $script:EnabledProperty -Value $true
        }
        ([OpsAuthAction]::DisableAnonymousAuthentication) {
            Set-WebConfigurationProperty -PSPath $script:IisConfigPsPath -Location $Location -Filter $script:AnonymousAuthFilter -Name $script:EnabledProperty -Value $false
        }
    }
}

# ==================================================================================================================
# Health probes (validated TLS; the API probe signs in as the operator running the kit)
# ==================================================================================================================
function Get-OpsDefaultHealthBaseUrl {
    $name = $env:COMPUTERNAME
    try { $name = [Net.Dns]::GetHostEntry([string]::Empty).HostName } catch { $name = $env:COMPUTERNAME }
    return $script:HttpsScheme + '://' + $name
}

function Get-OpsAppUrl([string] $BaseUrl, [string] $AppPath, [string] $Suffix) {
    return $BaseUrl.TrimEnd($script:UrlSeparator) + (ConvertTo-OpsAppPath $AppPath) + $Suffix
}

# One GET with the platform's certificate validation in force: a callback that skips validation is removed for the
# call and restored after it, so a probe can never pass behind an untrusted certificate.
function Invoke-OpsHealthRequest([string] $Url) {
    $previousCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
    $previousProtocols = [Net.ServicePointManager]::SecurityProtocol
    [Net.ServicePointManager]::ServerCertificateValidationCallback = $null
    [Net.ServicePointManager]::SecurityProtocol = $previousProtocols -bor [Net.SecurityProtocolType]::Tls12
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -UseDefaultCredentials -TimeoutSec $script:HealthRequestTimeoutSeconds -MaximumRedirection 0
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

function Get-OpsHealthDiagnosis([object] $Probe) {
    if ($Probe.StatusCode -eq $script:HttpOk) { return 'answered 200' }
    if ($Probe.StatusCode -eq $script:NoHttpStatus) { return "no answer ($($Probe.Error)): check the site's https binding, the host name in -HealthBaseUrl and that this server trusts the certificate" }
    if ($Probe.StatusCode -eq [int] [Net.HttpStatusCode]::Unauthorized) { return 'HTTP 401: Windows Authentication refused the probe; run the kit as a domain account the site accepts' }
    if (([string] $Probe.Body).TrimStart().StartsWith($script:HtmlBodyStart)) {
        return "HTTP $($Probe.StatusCode) with an HTML body: IIS answered, the console API did not start (usually a refused setting). Read Event Viewer > Application (IIS AspNetCore Module V2) and the logs folder."
    }
    return "HTTP $($Probe.StatusCode) from the console itself: read its problem+json body and the logs folder"
}

# Polls until $Accept says the probe is good (or the attempts run out); returns the last probe.
function Wait-OpsHealth([string] $Url, [scriptblock] $Accept, [int] $Attempts = $script:HealthAttempts, [int] $DelaySeconds = $script:HealthDelaySeconds) {
    $probe = $null
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $probe = Invoke-OpsHealthRequest $Url
        if (& $Accept $probe) { return $probe }
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
    return $probe
}

# ==================================================================================================================
# Inherited rewrite rules: the site's SPA fallback must not rewrite the console applications (pure, plus one read)
# ==================================================================================================================
function Test-OpsXmlFlag([System.Xml.XmlElement] $Node, [string] $Name, [bool] $Default) {
    $text = $Node.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($text)) { return $Default }
    return [string]::Equals($text.Trim(), $script:XmlTrueText, [StringComparison]::OrdinalIgnoreCase)
}

function Test-OpsRewritePattern([string] $Pattern, [string] $Text, [bool] $IgnoreCase) {
    $options = if ($IgnoreCase) { [Text.RegularExpressions.RegexOptions]::IgnoreCase } else { [Text.RegularExpressions.RegexOptions]::None }
    return [regex]::IsMatch($Text, $Pattern, $options)
}

<#
    True when $Rule (a <rule> of system.webServer/rewrite/rules) would rewrite a request for $Url (path with its
    leading slash). The rule's <match url> sees the path without the leading slash; its {URL} pattern conditions see
    it with the slash. Other conditions (IsFile, IsDirectory, other server variables) cannot be judged here and count
    as met, so the answer errs towards "rewrites".
#>
function Test-OpsRuleRewrites([System.Xml.XmlElement] $Rule, [string] $Url) {
    $action = $Rule.SelectSingleNode($script:RuleActionElement)
    if ($null -eq $action -or -not [string]::Equals($action.GetAttribute($script:ActionTypeAttribute), $script:RewriteActionType, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $match = $Rule.SelectSingleNode($script:RuleMatchElement)
    if ($null -ne $match) {
        $matched = Test-OpsRewritePattern -Pattern $match.GetAttribute($script:MatchUrlAttribute) -Text $Url.TrimStart($script:UrlSeparator) -IgnoreCase (Test-OpsXmlFlag $match $script:IgnoreCaseAttribute $true)
        if ($matched -eq (Test-OpsXmlFlag $match $script:NegateAttribute $false)) { return $false }
    }
    $conditions = $Rule.SelectSingleNode($script:RuleConditionsElement)
    if ($null -eq $conditions) { return $true }
    $results = @(foreach ($condition in @($conditions.SelectNodes($script:RuleConditionElement))) {
        $matchType = $condition.GetAttribute($script:ConditionMatchTypeAttribute)
        $isUrlPattern = [string]::Equals($condition.GetAttribute($script:ConditionInputAttribute), $script:UrlServerVariable, [StringComparison]::OrdinalIgnoreCase) -and
            ([string]::IsNullOrWhiteSpace($matchType) -or [string]::Equals($matchType, $script:PatternMatchType, [StringComparison]::OrdinalIgnoreCase))
        if (-not $isUrlPattern) { $true; continue }
        $hit = Test-OpsRewritePattern -Pattern $condition.GetAttribute($script:ConditionPatternAttribute) -Text $Url -IgnoreCase (Test-OpsXmlFlag $condition $script:IgnoreCaseAttribute $true)
        ($hit -ne (Test-OpsXmlFlag $condition $script:NegateAttribute $false))
    })
    if ($results.Count -eq 0) { return $true }
    if ([string]::Equals($conditions.GetAttribute($script:GroupingAttribute), $script:MatchAnyGrouping, [StringComparison]::OrdinalIgnoreCase)) { return ($results -contains $true) }
    return -not ($results -contains $false)
}

<#
    The application paths of $AppPaths that a Rewrite rule of the site's web.config ($WebConfigText) would rewrite.
    A child application inherits the site's rules, so the front site's SPA fallback would answer the console's
    calls with index.html unless its {URL} exclusion names the path. Each path is probed as <path>/health/ready.
#>
function Get-OpsRewrittenAppPaths([string] $WebConfigText, [string[]] $AppPaths) {
    if ([string]::IsNullOrWhiteSpace($WebConfigText)) { return [string[]] @() }
    $document = New-Object System.Xml.XmlDocument
    try { $document.LoadXml($WebConfigText) }
    catch { throw "The site's web.config is not valid XML, so its inherited rewrite rules cannot be checked. Nothing was changed." }
    $rules = @($document.SelectNodes($script:RewriteRulesXPath))
    return [string[]] @(foreach ($appPath in $AppPaths) {
        $probe = (ConvertTo-OpsAppPath $appPath) + $script:HealthPath
        if (@($rules | Where-Object { Test-OpsRuleRewrites -Rule $_ -Url $probe }).Count -gt 0) { ConvertTo-OpsAppPath $appPath }
    })
}

# The site's root folder (IIS:\Sites\<name>.physicalPath, read-only), or $null when IIS or the site cannot be read.
function Get-OpsSitePhysicalPath([string] $SiteName) {
    try {
        Import-Module WebAdministration -ErrorAction Stop -Verbose:$false
        $site = Get-Item -LiteralPath ($script:IisSitesPrefix + $SiteName) -ErrorAction SilentlyContinue
        if ($null -eq $site) { return $null }
        return [string] $site.physicalPath
    }
    catch { return $null }
}

# The text of the site's own web.config ('' when it has none), or $null when the site or its folder is unknown.
function Read-OpsSiteWebConfig([string] $SiteName) {
    $folder = Get-OpsSitePhysicalPath $SiteName
    if ([string]::IsNullOrWhiteSpace($folder)) { return $null }
    $path = Join-Path ([Environment]::ExpandEnvironmentVariables($folder)) $script:WebConfigFileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return '' }
    return [IO.File]::ReadAllText($path)
}

# Refuses (before anything changes) when the site's inherited rewrite rules would rewrite a console application.
function Assert-OpsNotRewrittenBySite([string] $SiteName, [string] $ApiAppPath, [string] $WebAppPath) {
    $text = Read-OpsSiteWebConfig $SiteName
    if ($null -eq $text) {
        Write-OpsLog "The web.config of site '$SiteName' could not be read: check by hand that its SPA fallback rule excludes $ApiAppPath and $WebAppPath." -Level $script:LogWarning
        return
    }
    $rewritten = @(Get-OpsRewrittenAppPaths -WebConfigText $text -AppPaths @($ApiAppPath, $WebAppPath))
    if ($rewritten.Count -eq 0) { return }
    throw ("The site '$SiteName' has a URL Rewrite rule (the front site's 'SPA fallback') that child applications inherit and that would rewrite " +
        "$($rewritten -join ', ') to its own page. Add the path to that rule's {URL} exclusion in the site's web.config " +
        '(deploy/kits/templates/web/web.config carries it), then run the kit again. Nothing was changed.')
}

# ==================================================================================================================
# Steps
# ==================================================================================================================
function New-OpsKitResult([bool] $Preview, [string] $ApiTargetPath, [string] $WebTargetPath, [string] $ApiHealthUrl, [string] $WebUrl) {
    return [pscustomobject]@{
        Preview = $Preview; ApiFolder = $null; EntryFileName = $null; WebFolder = $null; ApiTargetPath = $ApiTargetPath; WebTargetPath = $WebTargetPath
        PrerequisitesPassed = $false; IisPlan = @(); AuthPlan = @(); ApiBackupPath = $null; WebBackupPath = $null; Settings = @()
        Gaps = [string[]] @(); StartRefused = $false
        ApiHealthUrl = $ApiHealthUrl; WebUrl = $WebUrl; Health = [int] [OpsHealthOutcome]::NotRun; HealthStatusCode = $null
        PlannedActions = (New-Object 'System.Collections.Generic.List[string]')
    }
}

function Invoke-OpsPrerequisiteStep([object] $Result, [object] $AuthPlan) {
    $check = Test-KitPrerequisites -RuntimeMajor $script:RuntimeMajor -RequireAdministrator:(-not $Result.Preview)
    $Result.PrerequisitesPassed = [bool] $check.Passed -and -not $AuthPlan.MissingModule
    if ($AuthPlan.MissingModule) {
        Write-OpsLog "IIS Windows Authentication is not installed: Install-WindowsFeature $($script:WindowsAuthFeatureName) (elevated)." -Level $script:LogError
    }
    if ($Result.PrerequisitesPassed) { return }
    if (-not $Result.Preview) { throw 'Prerequisites are missing (see the lines above). Nothing was changed.' }
    Write-OpsLog 'Prerequisites are missing; a real run stops here. The preview continues.' -Level $script:LogWarning
}

function Invoke-OpsIisStep([object] $Result, [string] $SiteName, [hashtable[]] $Applications) {
    $plans = @()
    foreach ($application in $Applications) {
        $plan = @(Set-KitIisApplication -SiteName $SiteName -AppPath $application.AppPath -AppPoolName $application.Pool -PhysicalPath $application.Target -WhatIf:$Result.Preview)
        foreach ($step in $plan) { Add-OpsAction $Result "IIS $($step.Action) $($step.Target): $(@($step.Changes) -join '; ')" }
        $plans += $plan
    }
    $Result.IisPlan = $plans
}

function Invoke-OpsAuthStep {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result, [object] $Plan, [string] $Location)
    $Result.AuthPlan = @($Plan.Steps)
    foreach ($step in @($Plan.Steps)) {
        Add-OpsAction $Result "Auth $($step.Action): $($step.Detail)"
        if ($step.ActionId -eq [int] [OpsAuthAction]::None) { continue }
        if ($PSCmdlet.ShouldProcess($Location, $step.Detail)) {
            Invoke-OpsAuthAction -Step $step -Location $Location
            $step.Applied = $true
        }
    }
}

function Enter-OpsOffline {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result)
    $offline = Join-Path $Result.ApiTargetPath $script:AppOfflineFileName
    $running = Test-Path -LiteralPath (Join-Path $Result.ApiTargetPath $Result.EntryFileName) -PathType Leaf
    if (-not $running) { return $null }
    Add-OpsAction $Result "Take the console API offline with $($script:AppOfflineFileName) while files change"
    if (-not $PSCmdlet.ShouldProcess($offline, 'Write the offline page')) { return $null }
    [IO.File]::WriteAllText($offline, $script:AppOfflineHtml, (New-Object Text.UTF8Encoding($false)))
    Start-Sleep -Seconds $script:ShutdownWaitSeconds
    return $offline
}

function Exit-OpsOffline([string] $OfflinePath) {
    if ([string]::IsNullOrEmpty($OfflinePath) -or -not (Test-Path -LiteralPath $OfflinePath)) { return }
    Remove-Item -LiteralPath $OfflinePath -Force -WhatIf:$false
}

function Get-OpsGapPageHtml([string[]] $Gaps) {
    $items = @($Gaps | ForEach-Object { $script:GapPageItemFormat -f [Net.WebUtility]::HtmlEncode($_) }) -join ''
    return $script:GapPageHtmlFormat -f $items
}

# With gaps the console API must not start: the gap page stays as app_offline.htm. Without gaps any page is
# removed, including the gap page a previous run left.
function Set-OpsStartState {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result)
    $offline = Join-Path $Result.ApiTargetPath $script:AppOfflineFileName
    if ($Result.Gaps.Count -gt 0) {
        $Result.StartRefused = $true
        Add-OpsAction $Result "Refuse to start: keep $($script:AppOfflineFileName) listing $($Result.Gaps.Count) gap(s) until they are filled"
        if ($PSCmdlet.ShouldProcess($offline, 'Write the configuration-incomplete page')) {
            [IO.File]::WriteAllText($offline, (Get-OpsGapPageHtml $Result.Gaps), (New-Object Text.UTF8Encoding($false)))
        }
        return
    }
    if (-not (Test-Path -LiteralPath $offline)) { return }
    Add-OpsAction $Result "Start: remove $($script:AppOfflineFileName)"
    if ($PSCmdlet.ShouldProcess($offline, 'Remove the offline page')) { Remove-Item -LiteralPath $offline -Force; Write-OpsLog 'The console API is back online.' -Level $script:LogSuccess }
}

function Invoke-OpsSettingsMerge([object] $Result, [string] $ProductionTemplatePath) {
    $merges = @()
    $pairs = @(
        @{ Template = (Join-Path $Result.ApiFolder $script:BaseSettingsFileName); Server = (Join-Path $Result.ApiTargetPath $script:BaseSettingsFileName) },
        @{ Template = $ProductionTemplatePath; Server = (Join-Path $Result.ApiTargetPath $script:ProductionSettingsFileName) }
    )
    foreach ($pair in $pairs) {
        $merge = Merge-AppSettings -TemplatePath $pair.Template -ServerPath $pair.Server -WhatIf:$Result.Preview
        $name = Split-Path -Leaf $pair.Server
        if ($merge.Created) { Add-OpsAction $Result "Settings ${name}: create from the package template" }
        elseif (@($merge.AddedKeys).Count -gt 0) { Add-OpsAction $Result "Settings ${name}: add new key(s) $(@($merge.AddedKeys) -join ', ') (server values kept)" }
        else { Add-OpsAction $Result "Settings ${name}: no new keys (server values kept)" }
        $merges += $merge
    }
    $Result.Settings = $merges
}

function Invoke-OpsSettingsCheck([object] $Result, [string] $ProductionTemplatePath, [object[]] $Requirements) {
    $layers = Get-OpsSettingsLayers -ApiFolder $Result.ApiFolder -TargetPath $Result.ApiTargetPath -ProductionTemplatePath $ProductionTemplatePath
    $Result.Gaps = [string[]] @(Get-OpsSettingsGaps -Effective (Get-OpsEffectiveSettings $layers) -Requirements $Requirements)
    if ($Result.Gaps.Count -eq 0) { Write-OpsLog 'Required settings: all filled.' -Level $script:LogSuccess; return }
    $file = Join-Path $Result.ApiTargetPath $script:ProductionSettingsFileName
    Write-OpsLog "Required settings still empty; fill them in '$file' (see templates\ops\README.md):" -Level $script:LogWarning
    foreach ($gap in $Result.Gaps) { Write-OpsLog "  $gap" -Level $script:LogWarning }
}

function Invoke-OpsDeployFiles([object] $Result, [string] $BackupRoot, [string] $ProductionTemplatePath, [object[]] $Requirements) {
    $Result.ApiBackupPath = Backup-Deployment -SourcePath $Result.ApiTargetPath -BackupRoot $BackupRoot -Name $script:ApiBackupName -WhatIf:$Result.Preview
    $Result.WebBackupPath = Backup-Deployment -SourcePath $Result.WebTargetPath -BackupRoot $BackupRoot -Name $script:WebBackupName -WhatIf:$Result.Preview
    foreach ($pair in @(@($Result.ApiTargetPath, $Result.ApiBackupPath), @($Result.WebTargetPath, $Result.WebBackupPath))) {
        if ($pair[1]) { Add-OpsAction $Result "Back up '$($pair[0])' to '$($pair[1])'" } else { Add-OpsAction $Result "Backup of '$($pair[0])': nothing deployed yet (first install)" }
    }
    $offline = Enter-OpsOffline $Result
    $decided = $false
    try {
        Add-OpsAction $Result "Mirror '$($Result.ApiFolder)' to '$($Result.ApiTargetPath)' (server settings, $($script:AppOfflineFileName) and logs kept)"
        Copy-KitPayload -SourcePath $Result.ApiFolder -DestinationPath $Result.ApiTargetPath -WhatIf:$Result.Preview | Out-Null
        Invoke-OpsSettingsMerge -Result $Result -ProductionTemplatePath $ProductionTemplatePath
        Add-OpsAction $Result "Mirror '$($Result.WebFolder)' to '$($Result.WebTargetPath)'"
        Copy-KitPayload -SourcePath $Result.WebFolder -DestinationPath $Result.WebTargetPath -WhatIf:$Result.Preview | Out-Null
        Invoke-OpsSettingsCheck -Result $Result -ProductionTemplatePath $ProductionTemplatePath -Requirements $Requirements
        Set-OpsStartState $Result
        $decided = $true
    }
    finally { if (-not $decided) { Exit-OpsOffline $offline } }
}

function Invoke-OpsHealthStep([object] $Result, [string] $WebAppPath) {
    if ($Result.Preview) {
        $Result.Health = [int] [OpsHealthOutcome]::SkippedPreview
        Add-OpsAction $Result "Health probes skipped (-WhatIf): a real run waits for GET $($Result.ApiHealthUrl) and GET $($Result.WebUrl) over validated TLS"
        return
    }
    if ($Result.Gaps.Count -gt 0) {
        $Result.Health = [int] [OpsHealthOutcome]::SkippedGaps
        Write-OpsLog 'Health probes skipped: the console API is held offline until the gaps above are filled. Fill them, then run the kit again.' -Level $script:LogWarning
        return
    }
    $baseHref = $script:BaseHrefFormat -f (ConvertTo-OpsAppPath $WebAppPath)
    $probes = @(
        @{ Name = 'console API'; Url = $Result.ApiHealthUrl; Accept = { param($probe) $probe.StatusCode -eq $script:HttpOk } },
        @{ Name = 'console screens'; Url = $Result.WebUrl; Accept = { param($probe) $probe.StatusCode -eq $script:HttpOk -and ([string] $probe.Body).Contains($baseHref) } }
    )
    foreach ($check in $probes) {
        Write-OpsLog "Waiting for GET $($check.Url) ($($check.Name))..." -Level $script:LogStep
        $probe = Wait-OpsHealth -Url $check.Url -Accept $check.Accept
        $Result.HealthStatusCode = $probe.StatusCode
        if (& $check.Accept $probe) { Write-OpsLog "$($check.Url) answers 200." -Level $script:LogSuccess; continue }
        $Result.Health = [int] [OpsHealthOutcome]::Unhealthy
        $diagnosis = if ($probe.StatusCode -eq $script:HttpOk) { "HTTP 200 without $baseHref`: the site's own fallback answered, the $WebAppPath application is missing" } else { Get-OpsHealthDiagnosis $probe }
        Write-OpsLog "Health check failed ($($check.Name)): $diagnosis" -Level $script:LogError
        $backups = @($Result.ApiBackupPath, $Result.WebBackupPath | Where-Object { $_ })
        $rollback = if ($backups.Count -gt 0) { " Roll back: copy each backup's site\* over its folder ($($backups -join ', '))." } else { '' }
        throw "The Ops Console is deployed but $($check.Url) did not answer as expected.$rollback"
    }
    $Result.Health = [int] [OpsHealthOutcome]::Healthy
}

# ==================================================================================================================
# The kit
# ==================================================================================================================
function Invoke-OpsKit {
    <#
    .SYNOPSIS
        Runs the Ops Console kit (see the script help). Returns the result object: Preview, ApiFolder, EntryFileName,
        WebFolder, ApiTargetPath, WebTargetPath, PrerequisitesPassed, IisPlan, AuthPlan, ApiBackupPath, WebBackupPath,
        Settings, Gaps, StartRefused, ApiHealthUrl, WebUrl, Health (OpsHealthOutcome as int), HealthStatusCode,
        PlannedActions.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string] $PackagePath,
        [string] $SiteName,
        [string] $ApiAppPath = $script:DefaultApiAppPath,
        [string] $WebAppPath = $script:DefaultWebAppPath,
        [string] $ApiAppPoolName = $script:DefaultApiAppPoolName,
        [string] $WebAppPoolName = $script:DefaultWebAppPoolName,
        [string] $ApiTargetPath = (Join-Path $env:SystemDrive $script:DefaultApiTargetRelative),
        [string] $WebTargetPath = (Join-Path $env:SystemDrive $script:DefaultWebTargetRelative),
        [string] $BackupRoot = (Join-Path $env:SystemDrive $script:DefaultBackupRelative),
        [string] $HealthBaseUrl
    )
    if ([string]::IsNullOrWhiteSpace($HealthBaseUrl)) { $HealthBaseUrl = Get-OpsDefaultHealthBaseUrl }
    Assert-OpsKitParameters -PackagePath $PackagePath -SiteName $SiteName -ApiAppPath $ApiAppPath -WebAppPath $WebAppPath -ApiAppPoolName $ApiAppPoolName -WebAppPoolName $WebAppPoolName `
        -ApiTargetPath $ApiTargetPath -WebTargetPath $WebTargetPath -BackupRoot $BackupRoot -HealthBaseUrl $HealthBaseUrl
    $script:KitLogPath = $null
    $result = New-OpsKitResult -Preview ([bool] $WhatIfPreference) -ApiTargetPath $ApiTargetPath -WebTargetPath $WebTargetPath `
        -ApiHealthUrl (Get-OpsAppUrl -BaseUrl $HealthBaseUrl -AppPath $ApiAppPath -Suffix $script:HealthPath) -WebUrl (Get-OpsAppUrl -BaseUrl $HealthBaseUrl -AppPath $WebAppPath -Suffix $script:UrlSeparator)
    $mode = if ($result.Preview) { 'preview (-WhatIf): nothing is changed' } else { 'install' }
    Write-OpsLog "Ops Console kit: $mode. Site '$SiteName', API '$ApiAppPath' (pool '$ApiAppPoolName'), screens '$WebAppPath' (pool '$WebAppPoolName')." -Level $script:LogStep

    $package = Resolve-OpsPackage -PackagePath $PackagePath -WebAppPath $WebAppPath
    $result.ApiFolder = $package.ApiFolder
    $result.EntryFileName = $package.EntryFileName
    $result.WebFolder = $package.WebFolder
    foreach ($source in @($package.ApiFolder, $package.WebFolder)) {
        foreach ($target in @($ApiTargetPath, $WebTargetPath)) {
            if (Test-OpsPathsOverlap $source $target) { throw "The package folder '$source' overlaps '$target'; copy the package somewhere else first. Nothing was changed." }
        }
    }
    $requirements = Read-OpsRequirements (Join-Path $script:KitRoot $script:RequirementsRelative)
    $productionTemplate = Join-Path $script:KitRoot $script:ProductionTemplateRelative
    if (-not (Test-Path -LiteralPath $productionTemplate -PathType Leaf)) { throw "The kit template '$productionTemplate' is missing. Nothing was changed." }
    $location = Get-OpsIisLocation -SiteName $SiteName -AppPath $ApiAppPath
    $authPlan = Get-OpsAuthPlan -Facts (Get-OpsAuthFacts -SiteName $SiteName -AppPath $ApiAppPath) -Location $location
    Assert-OpsNotRewrittenBySite -SiteName $SiteName -ApiAppPath $ApiAppPath -WebAppPath $WebAppPath
    Invoke-OpsPrerequisiteStep -Result $result -AuthPlan $authPlan
    # The log file starts once every check that can refuse has passed: a refused run leaves no trace on the server.
    $script:KitLogPath = Join-Path $BackupRoot $script:LogFileName
    Invoke-OpsIisStep -Result $result -SiteName $SiteName -Applications @(
        @{ AppPath = $ApiAppPath; Pool = $ApiAppPoolName; Target = $ApiTargetPath },
        @{ AppPath = $WebAppPath; Pool = $WebAppPoolName; Target = $WebTargetPath }
    )
    Invoke-OpsAuthStep -Result $result -Plan $authPlan -Location $location -WhatIf:$result.Preview
    Invoke-OpsDeployFiles -Result $result -BackupRoot $BackupRoot -ProductionTemplatePath $productionTemplate -Requirements $requirements
    Invoke-OpsHealthStep -Result $result -WebAppPath $WebAppPath
    return $result
}

# Dot-sourced (the Pester tests): define the functions only.
if ($MyInvocation.InvocationName -eq '.') { return }

$kitResult = Invoke-OpsKit @PSBoundParameters
if (-not $kitResult.Preview -and $kitResult.Gaps.Count -gt 0) { exit $script:ExitGaps }
