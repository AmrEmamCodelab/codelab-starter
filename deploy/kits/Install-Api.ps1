#Requires -Version 5.1
<#
.SYNOPSIS
    Installs or updates the Starter API (Starter.Api) on an existing IIS site, built on Install-Common.psm1.

.DESCRIPTION
    The API kit (MODULE.md in this folder). The steps, in order:

      1. Checks the parameters, the package and its web.config (refuses before anything changes). -HealthBaseUrl
         must be https: the health probe validates the certificate like any client and never skips validation.
      2. Prerequisites: IIS, WebAdministration, the ASP.NET Core Module V2 and the ASP.NET Core 10 runtime.
      3. IIS: the site must already exist with an https binding (bindings and certificates are server
         prerequisites, never kit actions). App pool ("No Managed Code") and the site root or a child application,
         via Set-KitIisApplication, then the API's pool and application tuning: AlwaysRunning, no idle timeout, no
         periodic recycle, user profile loaded, preload on (the job server and the outbox run inside the API).
      4. Backup of the deployed folder (logs excluded) and the server settings files.
      5. app_offline.htm, the payload mirror (server settings files and logs are never copied over or deleted), then
         Merge-AppSettings: appsettings.json from the package, appsettings.Production.json from
         templates\api\appsettings.template.json (server values kept, only new keys added).
      6. Settings check over package < server < template < server Production < app pool environment variables:
         every required key that is missing, empty, a placeholder, not https or not enabled, every ring whose
         ActiveKeyId names no key, and every key material shared by two rings. Paths only, never values.
      7. Start: with gaps the site is refused a start (app_offline.htm stays, with a "not configured" page) and the
         run ends with exit 2. Without gaps app_offline.htm is removed, the pool is recycled and
         GET <HealthBaseUrl><AppPath>/health/ready must answer 200 over validated TLS.

    Under -WhatIf nothing is written: no IIS change, folder, backup, copy, settings file, offline page or log file.
    Every planned action is printed instead, and the settings check runs on what the merge would produce.

    Exit codes (when run as a script): 0 done (or previewed), 1 refused or failed, 2 installed but settings
    incomplete (the site stays offline).

.PARAMETER PackagePath
    The published API folder (it holds Starter.Api.dll), or a kit folder with that output in app\.
.PARAMETER SiteName
    The existing IIS site (with an https binding) the API runs in.
.PARAMETER AppPath
    '/' (default) for the site root, or a child application path such as '/api-host'.
.PARAMETER AppPoolName
    The app pool. Default 'StarterApi'.
.PARAMETER TargetPath
    The folder IIS serves the API from. Default <SystemDrive>\inetpub\starter-api.
.PARAMETER BackupRoot
    Where backups and the install log go. Default <SystemDrive>\inetpub\backups\starter-api.
.PARAMETER HealthBaseUrl
    The site's https base URL as its certificate names it, for example https://api.example.test.

.EXAMPLE
    .\Install-Api.ps1 -PackagePath .\api -SiteName 'Starter API' -HealthBaseUrl 'https://api.example.test' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $PackagePath,
    [string] $SiteName,
    [string] $AppPath,
    [string] $AppPoolName,
    [string] $TargetPath,
    [string] $BackupRoot,
    [string] $HealthBaseUrl
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Enums (integers with explicit values) ------------------------------------------------------------------------
enum ApiRequirementKind {
    Filled = 1
    Enabled = 2
    HttpsUrl = 3
    RealAddress = 4
}

enum ApiConditionKind {
    IsTrue = 1
    EqualsValue = 2
    SectionPresent = 3
}

enum ApiGapReason {
    Missing = 1
    Empty = 2
    NotEnabled = 3
    NotHttps = 4
    Placeholder = 5
    ActiveKeyNotInRing = 6
    MaterialSharedAcrossRings = 7
}

enum ApiHealthOutcome {
    NotRun = 0
    SkippedPreview = 1
    SkippedSettingsIncomplete = 2
    Healthy = 3
    Unhealthy = 4
}

# --- Constants ----------------------------------------------------------------------------------------------------
$script:CommonModuleFileName        = 'Install-Common.psm1'
$script:ProductionTemplateRelative  = 'templates\api\appsettings.template.json'
$script:BackupName                  = 'api'
$script:LogFileName                 = 'install-api.log'
$script:RuntimeMajor                = 10

$script:DefaultAppPath              = '/'
$script:DefaultAppPoolName          = 'StarterApi'
$script:DefaultTargetRelative       = 'inetpub\starter-api'
$script:DefaultBackupRelative       = 'inetpub\backups\starter-api'

# The package: the published API, directly or in app\.
$script:PackageAppFolder            = 'app'
$script:EntryAssemblyName           = 'Starter.Api.dll'
$script:WebConfigFileName           = 'web.config'
$script:BaseSettingsFileName        = 'appsettings.json'
$script:ProductionSettingsFileName  = 'appsettings.Production.json'

# web.config (package-owned): the ASP.NET Core Module element and the environment it must never force.
$script:AspNetCoreElementXPath      = '//aspNetCore'
$script:EnvironmentVariableXPath    = '//aspNetCore/environmentVariables/environmentVariable'
$script:EnvironmentVariableNames    = @('ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT')
$script:RefusedEnvironmentName      = 'Development'
$script:InProcessHostingModel       = 'inprocess'
$script:XmlTrue                     = 'true'

# Settings (.NET configuration paths and the app pool environment variable form of them).
$script:ConfigSeparator             = ':'
$script:EnvironmentSeparator        = '__'
$script:EmptyContainer              = New-Object -TypeName psobject
$script:HttpsScheme                 = 'https'
$script:PlaceholderPattern          = '(?i)[@.]example\.(test|invalid|com|org|net)$'   # RFC 2606 example domains
$script:TrueText                    = 'true'

# Configuration keys the checks read (the same names as docs/generated/config-keys.md).
$script:KeySsoEnabled               = 'Codelab:Identity:Sso:Enabled'
$script:KeySsoBackChannel           = 'Codelab:Identity:Sso:BackChannel'
$script:KeySmsEnabled               = 'Notifications:Sms:Enabled'
$script:KeySmsMode                  = 'Notifications:Sms:Mode'
$script:KeyAdEnabled                = 'Codelab:Identity:ActiveDirectory:Enabled'
$script:SectionOps                  = 'Codelab:Ops'
$script:BackChannelGatewayRelay     = 2   # OidcBackChannel.GatewayRelay (Codelab.Identity)
$script:SmsModeGateway              = 2   # SmsDeliveryMode.Gateway (Notifications module)

# The key rings (Codelab.Security): each ring's ActiveKeyId must name one of its Keys, and no key material may be
# shared by two rings (the upload-ticket ring is the only one the attachment service holds).
$script:KeyRingSections             = @(
    'Codelab:Security:PublicIds'
    'Codelab:Security:RequestSigning'
    'Codelab:Security:Tickets'
    'Codelab:Security:UploadTickets'
    'Codelab:Security:Uploads'
)
$script:RingActiveKeyId             = 'ActiveKeyId'
$script:RingKeys                    = 'Keys'
$script:RingKeyId                   = 'KeyId'
$script:RingMaterial                = 'MaterialBase64'

# App pool environment variables (applicationHost.config) win over the settings files.
$script:AppHostPsPath               = 'MACHINE/WEBROOT/APPHOST'
$script:PoolEnvironmentFilterFormat = "system.applicationHost/applicationPools/add[@name='{0}']/environmentVariables"
$script:CollectionProperty          = 'Collection'

# IIS tuning the API needs on top of Install-Common's pool settings.
$script:IisAppPoolsDrive            = 'IIS:\AppPools'
$script:IisSitesDrive               = 'IIS:\Sites'
$script:IisPathSeparator            = '\'
$script:WebAdministrationModule     = 'WebAdministration'
$script:PoolTuning                  = [ordered]@{
    'startMode'                       = 'AlwaysRunning'
    'processModel.idleTimeout'        = '00:00:00'
    'recycling.periodicRestart.time'  = '00:00:00'
    'processModel.loadUserProfile'    = 'True'
}
$script:RootPreloadProperty         = 'applicationDefaults.preloadEnabled'
$script:ChildPreloadProperty        = 'preloadEnabled'
$script:PreloadValue                = 'True'
$script:ValueProperty               = 'Value'
$script:HttpsProtocol               = 'https'
$script:UnknownCount                = -1
$script:CreateSiteAction            = 'CreateSite'
$script:PoolStartedState            = 'Started'

# Paths: a drive-rooted folder (C:\x) or a UNC share.
$script:DriveRootedPattern          = '^[A-Za-z]:\\'
$script:UncPattern                  = '^\\\\[^\\]+\\[^\\]+'
$script:PathSeparator               = '\'
$script:InvalidPoolNameChars        = [char[]] '\/:*?"<>|'
$script:UrlSeparator                = '/'

# app_offline.htm: ANCM stops the app (and keeps it stopped) while it exists.
$script:AppOfflineFileName          = 'app_offline.htm'
$script:UpdatingHtml                = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Update in progress</title></head><body><p>The service is being updated and will be back in a moment.</p></body></html>'
$script:NotConfiguredHtml           = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Not configured</title></head><body><p>The service is installed but not configured yet.</p></body></html>'
$script:ShutdownWaitSeconds         = 3

# Health probe (validated TLS only).
$script:HealthPath                  = '/health/ready'  # HealthEndpoints.ReadyPath (Codelab.Observability)
$script:HealthAttempts              = 12
$script:HealthDelaySeconds          = 5
$script:HealthRequestTimeoutSeconds = 10
$script:HttpOk                      = 200
$script:NoHttpStatus                = 0
$script:HtmlBodyStart               = '<'

$script:ExitSettingsIncomplete      = 2

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
# Required settings: one rule per key (Kind) with optional conditions that must all hold for the rule to apply
# ==================================================================================================================
function New-ApiCondition([string] $Path, [ApiConditionKind] $Kind, [object] $Value = $null) {
    return [pscustomobject]@{ Path = $Path; Kind = $Kind; Value = $Value }
}

function New-ApiRequirement([string] $Path, [ApiRequirementKind] $Kind = [ApiRequirementKind]::Filled, [object[]] $When = @()) {
    return [pscustomobject]@{ Path = $Path; Kind = $Kind; When = @($When) }
}

function Get-ApiRequiredSettings {
    $sso = New-ApiCondition $script:KeySsoEnabled IsTrue
    $ssoViaGateway = New-ApiCondition $script:KeySsoBackChannel EqualsValue $script:BackChannelGatewayRelay
    $sms = New-ApiCondition $script:KeySmsEnabled IsTrue
    $smsViaGateway = New-ApiCondition $script:KeySmsMode EqualsValue $script:SmsModeGateway
    $ad = New-ApiCondition $script:KeyAdEnabled IsTrue
    $ops = New-ApiCondition $script:SectionOps SectionPresent
    return @(
        New-ApiRequirement 'ConnectionStrings:Starter'
        New-ApiRequirement 'Auth:WebBaseUrl' HttpsUrl
        New-ApiRequirement 'Users:Activation:LinkBaseUrl' HttpsUrl
        New-ApiRequirement 'Attachments:ServiceBaseUrl' HttpsUrl
        New-ApiRequirement 'Portal:Contact:InboxEmail' RealAddress
        New-ApiRequirement 'Notifications:Email:FromAddress' RealAddress
        New-ApiRequirement 'Notifications:Email:Smtp:Host'
        New-ApiRequirement 'Identity:Jwt:SigningKey'
        New-ApiRequirement 'Identity:BreakGlass:Password'
        New-ApiRequirement 'Observability:UserIdHash:Key'
        New-ApiRequirement 'Codelab:Security:Cors:AllowedOrigins' HttpsUrl
        New-ApiRequirement 'Codelab:Security:PublicIds:ActiveKeyId'
        New-ApiRequirement 'Codelab:Security:PublicIds:Keys'
        New-ApiRequirement 'Codelab:Security:RequestSigning:ActiveKeyId'
        New-ApiRequirement 'Codelab:Security:RequestSigning:Keys'
        New-ApiRequirement 'Codelab:Security:Tickets:Enabled' Enabled
        New-ApiRequirement 'Codelab:Security:Tickets:ActiveKeyId'
        New-ApiRequirement 'Codelab:Security:Tickets:Keys'
        New-ApiRequirement 'Codelab:Security:UploadTickets:Enabled' Enabled
        New-ApiRequirement 'Codelab:Security:UploadTickets:ActiveKeyId'
        New-ApiRequirement 'Codelab:Security:UploadTickets:Keys'
        New-ApiRequirement 'Codelab:Security:Uploads:ReceiptsEnabled' Enabled
        New-ApiRequirement 'Codelab:Security:Uploads:ActiveKeyId'
        New-ApiRequirement 'Codelab:Security:Uploads:Keys'
        New-ApiRequirement 'Codelab:Security:ServiceAssertions:Client:ServiceName'
        New-ApiRequirement 'Codelab:Security:ServiceAssertions:Client:Audience'
        New-ApiRequirement 'Codelab:Security:ServiceAssertions:Client:KeyId'
        New-ApiRequirement 'Codelab:Security:ServiceAssertions:Client:PrivateKeyPem'
        New-ApiRequirement 'Codelab:Identity:Sso:Authority' HttpsUrl -When @($sso)
        New-ApiRequirement 'Codelab:Identity:Sso:ClientId' -When @($sso)
        New-ApiRequirement 'Codelab:Identity:Sso:AuthorizationEndpoint' HttpsUrl -When @($sso)
        New-ApiRequirement 'Codelab:Identity:Sso:TokenEndpoint' HttpsUrl -When @($sso)
        New-ApiRequirement 'Codelab:Identity:Sso:RedirectUri' HttpsUrl -When @($sso)
        New-ApiRequirement 'Codelab:Integration:Gateway:BaseUrl' HttpsUrl -When @($sso, $ssoViaGateway)
        New-ApiRequirement 'Codelab:Integration:Gateway:BaseUrl' HttpsUrl -When @($sms, $smsViaGateway)
        New-ApiRequirement 'Codelab:Identity:ActiveDirectory:Host' -When @($ad)
        New-ApiRequirement 'Codelab:Identity:ActiveDirectory:BaseDn' -When @($ad)
        New-ApiRequirement 'Codelab:Identity:ActiveDirectory:Domain' -When @($ad)
        New-ApiRequirement 'Codelab:Ops:TrustedCallerKeyId' -When @($ops)
        New-ApiRequirement 'Codelab:Security:ServiceAssertions:Server:Audience' -When @($ops)
        New-ApiRequirement 'Codelab:Security:ServiceAssertions:Server:TrustedServices' -When @($ops)
    )
}

# ==================================================================================================================
# Logging and the planned-action list
# ==================================================================================================================
# Install-Common's Write-KitLog does not see this script's -WhatIf, so the log file is dropped here under -WhatIf.
function Write-ApiLog([string] $Message, [string] $Level = $script:LogInfo) {
    $logPath = if ($WhatIfPreference) { $null } else { $script:KitLogPath }
    Write-KitLog -Message $Message -Level $Level -LogPath $logPath -WhatIf:$false
}

function Add-ApiAction([object] $Result, [string] $Text) {
    $Result.PlannedActions.Add($Text)
    Write-ApiLog $Text -Level $script:LogStep
}

# ==================================================================================================================
# Parameters, paths and the package (pure checks; they throw before anything changes)
# ==================================================================================================================
function Test-ApiFullyQualifiedPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return ($Path -match $script:DriveRootedPattern -or $Path -match $script:UncPattern)
}

function ConvertTo-ApiComparablePath([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd($script:PathSeparator)
}

# True when $Path is $Parent itself or anything below it (case-insensitive, like NTFS).
function Test-ApiPathInside([string] $Path, [string] $Parent) {
    $child = ConvertTo-ApiComparablePath $Path
    $root = ConvertTo-ApiComparablePath $Parent
    if ([string]::Equals($child, $root, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $child.StartsWith($root + $script:PathSeparator, [StringComparison]::OrdinalIgnoreCase)
}

function Test-ApiHttpsUrl([string] $Value) {
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref] $uri)) { return $false }
    return [string]::Equals($uri.Scheme, $script:HttpsScheme, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-ApiKitParameters {
    param([string] $PackagePath, [string] $SiteName, [string] $AppPath, [string] $AppPoolName, [string] $TargetPath, [string] $BackupRoot, [string] $HealthBaseUrl)
    if ([string]::IsNullOrWhiteSpace($PackagePath)) { throw '-PackagePath is required: the published API folder. Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($SiteName)) { throw '-SiteName is required: the existing IIS site (with an https binding) the API runs in. Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($AppPath) -or -not $AppPath.Trim().StartsWith($script:UrlSeparator)) {
        throw "-AppPath '$AppPath' must be '/' (the site root) or a child path such as '/api-host'. Nothing was changed."
    }
    if ([string]::IsNullOrWhiteSpace($AppPoolName) -or $AppPoolName.IndexOfAny($script:InvalidPoolNameChars) -ge 0) {
        throw "-AppPoolName '$AppPoolName' is not a valid app pool name. Nothing was changed."
    }
    if (-not (Test-ApiFullyQualifiedPath $TargetPath)) { throw "-TargetPath '$TargetPath' must be a fully qualified folder. Nothing was changed." }
    if (-not (Test-ApiFullyQualifiedPath $BackupRoot)) { throw "-BackupRoot '$BackupRoot' must be a fully qualified folder. Nothing was changed." }
    if ((Test-ApiPathInside -Path $BackupRoot -Parent $TargetPath) -or (Test-ApiPathInside -Path $TargetPath -Parent $BackupRoot)) {
        throw "-BackupRoot '$BackupRoot' and -TargetPath '$TargetPath' overlap; the payload mirror would delete the backups. Nothing was changed."
    }
    if (-not (Test-ApiHttpsUrl $HealthBaseUrl)) {
        throw "-HealthBaseUrl '$HealthBaseUrl' must be the site's https address as its certificate names it (for example https://api.example.test); the health probe never uses http and never skips certificate validation. Nothing was changed."
    }
}

# Returns the folder holding the published API: the package itself or its app\ folder.
function Resolve-ApiPackage([string] $PackagePath) {
    if (-not (Test-Path -LiteralPath $PackagePath -PathType Container)) { throw "Package folder '$PackagePath' does not exist. Nothing was changed." }
    foreach ($candidate in @($PackagePath, (Join-Path $PackagePath $script:PackageAppFolder))) {
        if (-not (Test-Path -LiteralPath (Join-Path $candidate $script:EntryAssemblyName) -PathType Leaf)) { continue }
        foreach ($required in @($script:WebConfigFileName, $script:BaseSettingsFileName)) {
            if (-not (Test-Path -LiteralPath (Join-Path $candidate $required) -PathType Leaf)) {
                throw "The package '$candidate' has no $required; it is not a complete publish output. Nothing was changed."
            }
        }
        return (Resolve-Path -LiteralPath $candidate).ProviderPath
    }
    throw "No $($script:EntryAssemblyName) in '$PackagePath' or its $($script:PackageAppFolder)\ folder; give the published API. Nothing was changed."
}

<#
    Reads the package web.config (package-owned: the mirror replaces it every run). Refuses one that forces the
    Development environment (developer exception pages, Development-only switches, migrations on start); returns the
    hosting model and the stdout-log switch so the kit can warn about them.
#>
function Get-ApiWebConfigFacts([string] $AppFolder) {
    $path = Join-Path $AppFolder $script:WebConfigFileName
    $xml = New-Object Xml.XmlDocument
    try { $xml.Load($path) }
    catch { throw "'$path' is not valid XML. Nothing was changed." }
    $module = $xml.SelectSingleNode($script:AspNetCoreElementXPath)
    if ($null -eq $module) { throw "'$path' has no aspNetCore element; it is not an ASP.NET Core Module web.config. Nothing was changed." }
    foreach ($variable in @($xml.SelectNodes($script:EnvironmentVariableXPath))) {
        $isEnvironment = $script:EnvironmentVariableNames -contains [string] $variable.GetAttribute('name')
        if ($isEnvironment -and [string]::Equals([string] $variable.GetAttribute('value'), $script:RefusedEnvironmentName, [StringComparison]::OrdinalIgnoreCase)) {
            throw "'$path' sets the $($script:RefusedEnvironmentName) environment; a server never runs the API as $($script:RefusedEnvironmentName). Publish without it. Nothing was changed."
        }
    }
    return [pscustomobject]@{
        HostingModel = [string] $module.GetAttribute('hostingModel')
        StdoutLogEnabled = [string]::Equals([string] $module.GetAttribute('stdoutLogEnabled'), $script:XmlTrue, [StringComparison]::OrdinalIgnoreCase)
    }
}

# ==================================================================================================================
# Settings: read, layer like .NET configuration, find the gaps
# ==================================================================================================================
function Read-ApiJson([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $text = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).ProviderPath, (New-Object Text.UTF8Encoding($false)))
    # The parser error is not passed on: it quotes file text, and settings files hold key material.
    try { return (ConvertFrom-Json -InputObject $text -ErrorAction Stop) }
    catch { throw "'$Path' is not valid JSON (comments and trailing commas are not supported). Nothing was changed." }
}

function Test-ApiJsonObject([object] $Value) {
    return ($null -ne $Value -and $Value -is [System.Management.Automation.PSCustomObject])
}

function Test-ApiJsonArray([object] $Value) {
    return ($null -ne $Value -and $Value -isnot [string] -and $Value -is [System.Collections.IList])
}

# Flattens a JSON object into "A:B:0:C" -> value, the way .NET configuration keys it. An empty object or array is
# recorded as the EmptyContainer marker, so a required section that is present but empty is still seen.
function Add-ApiSettingLeaves([object] $Node, [string] $Prefix, [hashtable] $Into) {
    if (Test-ApiJsonObject $Node) {
        $members = @($Node.PSObject.Properties)
        if ($members.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        foreach ($member in $members) {
            $path = if ($Prefix) { $Prefix + $script:ConfigSeparator + $member.Name } else { $member.Name }
            Add-ApiSettingLeaves -Node $member.Value -Prefix $path -Into $Into
        }
        return
    }
    if (Test-ApiJsonArray $Node) {
        if ($Node.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        for ($index = 0; $index -lt $Node.Count; $index++) {
            Add-ApiSettingLeaves -Node $Node[$index] -Prefix ($Prefix + $script:ConfigSeparator + $index) -Into $Into
        }
        return
    }
    $Into[$Prefix] = $Node
}

<#
    Layers are lowest precedence first; a later layer overrides a leaf (case-insensitive keys, like .NET
    configuration). A layer is a parsed JSON object or a hashtable of flat keys (the app pool environment, already in
    the A:B form).
#>
function Get-ApiEffectiveSettings([object[]] $Layers) {
    $leaves = @{}
    foreach ($layer in @($Layers | Where-Object { $null -ne $_ })) {
        if ($layer -is [hashtable]) { foreach ($key in $layer.Keys) { $leaves[$key] = $layer[$key] }; continue }
        Add-ApiSettingLeaves -Node $layer -Prefix '' -Into $leaves
    }
    return $leaves
}

function Get-ApiSettingValue([hashtable] $Effective, [string] $Path) {
    if (-not $Effective.ContainsKey($Path)) { return $null }
    $value = $Effective[$Path]
    if ([object]::ReferenceEquals($value, $script:EmptyContainer)) { return $null }
    return $value
}

function Test-ApiEmptyValue([object] $Value) {
    if ($null -eq $Value) { return $true }
    return ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value))
}

function Test-ApiTrueValue([object] $Value) {
    if ($Value -is [bool]) { return $Value }
    return [string]::Equals([string] $Value, $script:TrueText, [StringComparison]::OrdinalIgnoreCase)
}

# The leaf keys at or below $Path that hold a value (empty containers excluded).
function Get-ApiSettingKeysUnder([hashtable] $Effective, [string] $Path) {
    $below = $Path + $script:ConfigSeparator
    return @($Effective.Keys | Where-Object {
        ($_ -eq $Path -or $_.StartsWith($below, [StringComparison]::OrdinalIgnoreCase)) -and
        -not [object]::ReferenceEquals($Effective[$_], $script:EmptyContainer)
    } | Sort-Object)
}

function Test-ApiCondition([hashtable] $Effective, [object] $Condition) {
    switch ([ApiConditionKind] $Condition.Kind) {
        ([ApiConditionKind]::IsTrue) { return (Test-ApiTrueValue (Get-ApiSettingValue $Effective $Condition.Path)) }
        ([ApiConditionKind]::EqualsValue) { return [string]::Equals([string] (Get-ApiSettingValue $Effective $Condition.Path), [string] $Condition.Value, [StringComparison]::OrdinalIgnoreCase) }
        ([ApiConditionKind]::SectionPresent) { return (@(Get-ApiSettingKeysUnder $Effective $Condition.Path).Count -gt 0) }
    }
    return $false
}

function New-ApiGap([string] $Path, [ApiGapReason] $Reason) {
    return [pscustomobject]@{ Path = $Path; Reason = $Reason.ToString(); ReasonId = [int] $Reason }
}

# The gaps of one requirement. A section counts as filled only when it has at least one value and none is empty.
function Get-ApiRequirementGaps([hashtable] $Effective, [object] $Requirement) {
    $keys = @(Get-ApiSettingKeysUnder $Effective $Requirement.Path)
    if ($keys.Count -eq 0) { return @(New-ApiGap $Requirement.Path Missing) }
    $gaps = New-Object 'System.Collections.Generic.List[object]'
    foreach ($key in $keys) {
        $value = $Effective[$key]
        if (Test-ApiEmptyValue $value) { $gaps.Add((New-ApiGap $key Empty)); continue }
        switch ([ApiRequirementKind] $Requirement.Kind) {
            ([ApiRequirementKind]::Enabled) { if (-not (Test-ApiTrueValue $value)) { $gaps.Add((New-ApiGap $key NotEnabled)) } }
            ([ApiRequirementKind]::HttpsUrl) { if (-not (Test-ApiHttpsUrl ([string] $value))) { $gaps.Add((New-ApiGap $key NotHttps)) } }
            ([ApiRequirementKind]::RealAddress) { if ([string] $value -match $script:PlaceholderPattern) { $gaps.Add((New-ApiGap $key Placeholder)) } }
        }
    }
    return $gaps.ToArray()
}

# Each ring's ActiveKeyId must name one of its KeyIds. Checked only when both are filled (else the requirement says so).
function Get-ApiRingGaps([hashtable] $Effective) {
    $gaps = New-Object 'System.Collections.Generic.List[object]'
    $owners = @{}
    foreach ($ring in $script:KeyRingSections) {
        $activePath = $ring + $script:ConfigSeparator + $script:RingActiveKeyId
        $active = [string] (Get-ApiSettingValue $Effective $activePath)
        $keysPrefix = $ring + $script:ConfigSeparator + $script:RingKeys + $script:ConfigSeparator
        $keyIds = @(Get-ApiSettingKeysUnder $Effective ($ring + $script:ConfigSeparator + $script:RingKeys) |
            Where-Object { $_.EndsWith($script:ConfigSeparator + $script:RingKeyId, [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object { [string] $Effective[$_] } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if (-not [string]::IsNullOrWhiteSpace($active) -and $keyIds.Count -gt 0 -and $keyIds -cnotcontains $active) {
            $gaps.Add((New-ApiGap $activePath ActiveKeyNotInRing))
        }
        $materials = @(Get-ApiSettingKeysUnder $Effective ($ring + $script:ConfigSeparator + $script:RingKeys) |
            Where-Object { $_.StartsWith($keysPrefix, [StringComparison]::OrdinalIgnoreCase) -and $_.EndsWith($script:ConfigSeparator + $script:RingMaterial, [StringComparison]::OrdinalIgnoreCase) })
        foreach ($materialPath in $materials) {
            $material = [string] $Effective[$materialPath]
            if ([string]::IsNullOrWhiteSpace($material)) { continue }
            if ($owners.ContainsKey($material) -and $owners[$material] -ne $ring) { $gaps.Add((New-ApiGap $materialPath MaterialSharedAcrossRings)); continue }
            $owners[$material] = $ring
        }
    }
    return $gaps.ToArray()
}

<#
    Every gap of the effective settings, sorted by path then reason, without duplicates. Values are never returned:
    a gap is the configuration path and the reason only.
#>
function Get-ApiSettingsGaps([hashtable] $Effective, [object[]] $Requirements = (Get-ApiRequiredSettings)) {
    $gaps = New-Object 'System.Collections.Generic.List[object]'
    foreach ($requirement in $Requirements) {
        $applies = $true
        foreach ($condition in @($requirement.When)) { if (-not (Test-ApiCondition $Effective $condition)) { $applies = $false; break } }
        if ($applies) { foreach ($gap in @(Get-ApiRequirementGaps $Effective $requirement)) { $gaps.Add($gap) } }
    }
    foreach ($gap in @(Get-ApiRingGaps $Effective)) { $gaps.Add($gap) }
    $seen = @{}
    $unique = foreach ($gap in ($gaps | Sort-Object Path, ReasonId)) {
        $id = '{0}|{1}' -f $gap.Path.ToLowerInvariant(), $gap.ReasonId
        if ($seen.ContainsKey($id)) { continue }
        $seen[$id] = $true
        $gap
    }
    return @($unique)
}

# App pool environment variables in the A:B form (A__B in the pool). Values are kept only for the gap check.
function ConvertTo-ApiEnvironmentLayer([object[]] $Variables) {
    $layer = @{}
    foreach ($variable in @($Variables)) {
        if ($null -eq $variable -or [string]::IsNullOrWhiteSpace([string] $variable.Name)) { continue }
        $layer[([string] $variable.Name).Replace($script:EnvironmentSeparator, $script:ConfigSeparator)] = [string] $variable.Value
    }
    return $layer
}

# Read-only. Returns Name/Value pairs, or an empty list when IIS cannot be read (not elevated, no pool yet).
function Get-ApiPoolEnvironmentVariables([string] $AppPoolName) {
    try {
        Import-Module $script:WebAdministrationModule -ErrorAction Stop -Verbose:$false
        $filter = $script:PoolEnvironmentFilterFormat -f $AppPoolName
        $items = Get-WebConfigurationProperty -PSPath $script:AppHostPsPath -Filter $filter -Name $script:CollectionProperty -ErrorAction Stop
        return @($items | ForEach-Object { [pscustomobject]@{ Name = [string] $_.name; Value = [string] $_.value } })
    }
    catch {
        Write-ApiLog "The app pool environment variables could not be read ($($_.Exception.Message)); the settings check uses the files only." -Level $script:LogWarning
        return @()
    }
}

# The settings as they are (or will be) after the merge: package template < server file, per file, base < Production,
# then the pool's environment variables.
function Get-ApiSettingsLayers([string] $AppFolder, [string] $TargetPath, [string] $ProductionTemplatePath, [hashtable] $EnvironmentLayer) {
    return @(
        (Read-ApiJson (Join-Path $AppFolder $script:BaseSettingsFileName)),
        (Read-ApiJson (Join-Path $TargetPath $script:BaseSettingsFileName)),
        (Read-ApiJson $ProductionTemplatePath),
        (Read-ApiJson (Join-Path $TargetPath $script:ProductionSettingsFileName)),
        $EnvironmentLayer
    )
}

# ==================================================================================================================
# IIS: the site and its https binding (read-only facts), the API tuning (pure plan, apply)
# ==================================================================================================================
function Get-ApiSiteHttpsBindingCount([string] $SiteName) {
    try {
        Import-Module $script:WebAdministrationModule -ErrorAction Stop -Verbose:$false
        return @(Get-WebBinding -Name $SiteName -Protocol $script:HttpsProtocol -ErrorAction Stop).Count
    }
    catch { return $script:UnknownCount }
}

function Get-ApiTuningTargets([string] $SiteName, [string] $AppPath, [string] $AppPoolName) {
    $pool = $script:IisAppPoolsDrive + $script:IisPathSeparator + $AppPoolName
    $site = $script:IisSitesDrive + $script:IisPathSeparator + $SiteName
    $isRoot = $AppPath.Trim().Trim($script:UrlSeparator).Length -eq 0
    $targets = New-Object 'System.Collections.Generic.List[object]'
    foreach ($property in $script:PoolTuning.Keys) { $targets.Add([pscustomobject]@{ Target = $pool; Property = $property; Want = $script:PoolTuning[$property] }) }
    if ($isRoot) { $targets.Add([pscustomobject]@{ Target = $site; Property = $script:RootPreloadProperty; Want = $script:PreloadValue }) }
    else {
        $app = $site + $script:IisPathSeparator + $AppPath.Trim().Trim($script:UrlSeparator).Replace($script:UrlSeparator, $script:IisPathSeparator)
        $targets.Add([pscustomobject]@{ Target = $app; Property = $script:ChildPreloadProperty; Want = $script:PreloadValue })
    }
    return $targets.ToArray()
}

function Get-ApiTuningKey([object] $Target) { return ('{0}|{1}' -f $Target.Target, $Target.Property) }

# Read-only: the current value of every tuning target ("<target>|<property>" -> string), or $null when unreadable.
function Get-ApiTuningState([object[]] $Targets) {
    try {
        Import-Module $script:WebAdministrationModule -ErrorAction Stop -Verbose:$false
        $state = @{}
        foreach ($target in $Targets) {
            if (-not (Test-Path -LiteralPath $target.Target)) { continue }
            $value = Get-ItemProperty -LiteralPath $target.Target -Name $target.Property -ErrorAction Stop
            if ($null -ne $value -and $null -ne $value.PSObject.Properties[$script:ValueProperty]) { $value = $value.Value }
            $state[(Get-ApiTuningKey $target)] = [string] $value
        }
        return $state
    }
    catch { return $null }
}

<#
    Pure: the tuning steps whose current value differs (case-insensitive). With no state (not elevated, or a target
    created in this run) every target is planned.
#>
function Get-ApiTuningPlan([object[]] $Targets, [hashtable] $Current) {
    $steps = New-Object 'System.Collections.Generic.List[object]'
    foreach ($target in $Targets) {
        $key = Get-ApiTuningKey $target
        $have = if ($null -ne $Current -and $Current.ContainsKey($key)) { [string] $Current[$key] } else { $null }
        if ([string]::Equals($have, [string] $target.Want, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $steps.Add([pscustomobject]@{ Target = $target.Target; Property = $target.Property; Have = $have; Want = $target.Want; Applied = $false })
    }
    return $steps.ToArray()
}

function Invoke-ApiTuningStep([object] $Step) {
    Import-Module $script:WebAdministrationModule -ErrorAction Stop -Verbose:$false
    Set-ItemProperty -LiteralPath $Step.Target -Name $Step.Property -Value $Step.Want
}

function Restart-ApiAppPool([string] $AppPoolName) {
    Import-Module $script:WebAdministrationModule -ErrorAction Stop -Verbose:$false
    if ((Get-WebAppPoolState -Name $AppPoolName).Value -eq $script:PoolStartedState) { Restart-WebAppPool -Name $AppPoolName }
    else { Start-WebAppPool -Name $AppPoolName }
}

# ==================================================================================================================
# Health probe: https with the platform's certificate validation; a session-wide validation override is refused
# ==================================================================================================================
function Assert-ApiCertificateValidationIntact {
    if ($null -ne [Net.ServicePointManager]::ServerCertificateValidationCallback) {
        throw 'This PowerShell session overrides certificate validation (ServicePointManager.ServerCertificateValidationCallback). Open a new PowerShell: the health probe only trusts a certificate the server validates.'
    }
}

function Get-ApiHealthUrl([string] $BaseUrl, [string] $AppPath) {
    $app = $AppPath.Trim().Trim($script:UrlSeparator)
    $prefix = if ($app.Length -gt 0) { $script:UrlSeparator + $app } else { '' }
    return $BaseUrl.TrimEnd($script:UrlSeparator) + $prefix + $script:HealthPath
}

function Invoke-ApiHealthRequest([string] $Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $script:HealthRequestTimeoutSeconds -MaximumRedirection 0
        return [pscustomobject]@{ StatusCode = [int] $response.StatusCode; Body = [string] $response.Content; Error = $null }
    }
    catch [System.Net.WebException] {
        $response = $_.Exception.Response
        if ($null -eq $response) { return [pscustomobject]@{ StatusCode = $script:NoHttpStatus; Body = ''; Error = $_.Exception.Message } }
        $reader = New-Object IO.StreamReader($response.GetResponseStream())
        try { $body = $reader.ReadToEnd() } finally { $reader.Dispose(); $response.Close() }
        return [pscustomobject]@{ StatusCode = [int] $response.StatusCode; Body = $body; Error = $null }
    }
}

# An HTML body means IIS (or the ASP.NET Core Module) answered, not the API: the app did not start.
function Get-ApiHealthDiagnosis([object] $Probe) {
    if ($Probe.StatusCode -eq $script:HttpOk) { return 'the API answered 200' }
    if ($Probe.StatusCode -eq $script:NoHttpStatus) {
        return "no answer ($($Probe.Error)): check the https binding, that the certificate names the -HealthBaseUrl host and chains to a root this server trusts"
    }
    if (([string] $Probe.Body).TrimStart().StartsWith($script:HtmlBodyStart)) {
        return "HTTP $($Probe.StatusCode) with an HTML body: IIS answered, the API did not start (usually a refused setting or a pending database script). Read Event Viewer > Application (IIS AspNetCore Module V2) and the logs folder."
    }
    return "HTTP $($Probe.StatusCode) from the API itself: read its problem+json body and the logs folder"
}

function Wait-ApiHealth([string] $Url, [int] $Attempts = $script:HealthAttempts, [int] $DelaySeconds = $script:HealthDelaySeconds) {
    $probe = $null
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $probe = Invoke-ApiHealthRequest $Url
        if ($probe.StatusCode -eq $script:HttpOk) { return $probe }
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
    return $probe
}

# ==================================================================================================================
# Steps
# ==================================================================================================================
function New-ApiKitResult([bool] $Preview, [string] $TargetPath, [string] $HealthUrl) {
    return [pscustomobject]@{
        Preview = $Preview; AppFolder = $null; TargetPath = $TargetPath; WebConfig = $null
        PrerequisitesPassed = $false; SiteMissing = $false; IisPlan = @(); TuningPlan = @(); BackupPath = $null; Payload = $null; Settings = @()
        SettingsGaps = [string[]] @(); SettingsGapDetails = @(); SiteHeldOffline = $false
        HealthUrl = $HealthUrl; Health = [int] [ApiHealthOutcome]::NotRun; HealthStatusCode = $null
        PlannedActions = (New-Object 'System.Collections.Generic.List[string]')
    }
}

function Invoke-ApiPrerequisiteStep([object] $Result) {
    $check = Test-KitPrerequisites -RuntimeMajor $script:RuntimeMajor -RequireAdministrator:(-not $Result.Preview)
    $Result.PrerequisitesPassed = [bool] $check.Passed
    if ($check.Passed) { return }
    if (-not $Result.Preview) { throw 'Prerequisites are missing (see the lines above). Nothing was changed.' }
    Write-ApiLog 'Prerequisites are missing; a real run stops here. The preview continues.' -Level $script:LogWarning
}

function Invoke-ApiPackageChecks([object] $Result) {
    $Result.WebConfig = Get-ApiWebConfigFacts $Result.AppFolder
    if (-not [string]::Equals($Result.WebConfig.HostingModel, $script:InProcessHostingModel, [StringComparison]::OrdinalIgnoreCase)) {
        Write-ApiLog "web.config hostingModel is '$($Result.WebConfig.HostingModel)'; the API is built and tested in-process." -Level $script:LogWarning
    }
    if ($Result.WebConfig.StdoutLogEnabled) {
        Write-ApiLog 'web.config has stdoutLogEnabled="true": stdout logs grow without limit. Turn it on only while diagnosing a start failure.' -Level $script:LogWarning
    }
}

# The site must exist with an https binding: a kit never creates sites, bindings or certificates.
function Assert-ApiSiteReady([object] $Result, [string] $SiteName, [string] $AppPath, [string] $AppPoolName) {
    # Without -Port, Install-Common refuses to plan a site that does not exist; that refusal means "no site" here.
    $createsSite = $false
    try {
        $plan = @(Get-KitIisPlan -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName -PhysicalPath $Result.TargetPath -WhatIf:$false)
        $createsSite = @($plan | Where-Object { $_.Action -eq $script:CreateSiteAction }).Count -gt 0
    }
    catch { $createsSite = $true }
    $Result.SiteMissing = $createsSite
    $httpsCount = Get-ApiSiteHttpsBindingCount $SiteName
    $problem = $null
    if ($createsSite) { $problem = "The IIS site '$SiteName' does not exist (or IIS could not be read). Create the site with its https binding and certificate first (a server prerequisite)." }
    elseif ($httpsCount -eq 0) { $problem = "The IIS site '$SiteName' has no https binding. Add the binding and its certificate first (a server prerequisite)." }
    elseif ($httpsCount -eq $script:UnknownCount) { $problem = "The bindings of the IIS site '$SiteName' could not be read." }
    if ($null -eq $problem) { return }
    if (-not $Result.Preview) { throw "$problem Nothing was changed." }
    Write-ApiLog "$problem A real run stops here." -Level $script:LogWarning
}

function Invoke-ApiIisStep([object] $Result, [string] $SiteName, [string] $AppPath, [string] $AppPoolName) {
    if ($Result.SiteMissing) { Add-ApiAction $Result "IIS: no plan while the site '$SiteName' is missing"; return }
    $plan = @(Set-KitIisApplication -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName -PhysicalPath $Result.TargetPath -WhatIf:$Result.Preview)
    $Result.IisPlan = $plan
    foreach ($step in $plan) { Add-ApiAction $Result "IIS $($step.Action) $($step.Target): $(@($step.Changes) -join '; ')" }
}

function Invoke-ApiTuningStepAll {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result, [string] $SiteName, [string] $AppPath, [string] $AppPoolName)
    $targets = Get-ApiTuningTargets -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName
    $plan = @(Get-ApiTuningPlan -Targets $targets -Current (Get-ApiTuningState $targets))
    $Result.TuningPlan = $plan
    if ($plan.Count -eq 0) { Write-ApiLog 'IIS tuning: already in place.' -Level $script:LogSuccess; return }
    foreach ($step in $plan) {
        $text = "IIS tuning $($step.Target) $($step.Property): '$($step.Have)' -> '$($step.Want)'"
        Add-ApiAction $Result $text
        if ($PSCmdlet.ShouldProcess($step.Target, "$($step.Property) = $($step.Want)")) {
            Invoke-ApiTuningStep -Step $step
            $step.Applied = $true
        }
    }
}

function Write-ApiOfflinePage {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([string] $TargetPath, [string] $Html)
    $offline = Join-Path $TargetPath $script:AppOfflineFileName
    if (-not $PSCmdlet.ShouldProcess($offline, 'Write the offline page')) { return $null }
    if (-not (Test-Path -LiteralPath $TargetPath)) { New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null }
    [IO.File]::WriteAllText($offline, $Html, (New-Object Text.UTF8Encoding($false)))
    return $offline
}

function Enter-ApiOffline([object] $Result) {
    $running = Test-Path -LiteralPath (Join-Path $Result.TargetPath $script:EntryAssemblyName) -PathType Leaf
    if (-not $running) { return }
    Add-ApiAction $Result "Take the API offline with $($script:AppOfflineFileName) while files change"
    if ($null -ne (Write-ApiOfflinePage -TargetPath $Result.TargetPath -Html $script:UpdatingHtml -WhatIf:$Result.Preview)) {
        Start-Sleep -Seconds $script:ShutdownWaitSeconds
    }
}

function Invoke-ApiSettingsMerge([object] $Result, [string] $ProductionTemplatePath) {
    $merges = @()
    $pairs = @(
        @{ Template = (Join-Path $Result.AppFolder $script:BaseSettingsFileName); Server = (Join-Path $Result.TargetPath $script:BaseSettingsFileName) },
        @{ Template = $ProductionTemplatePath; Server = (Join-Path $Result.TargetPath $script:ProductionSettingsFileName) }
    )
    foreach ($pair in $pairs) {
        $merge = Merge-AppSettings -TemplatePath $pair.Template -ServerPath $pair.Server -WhatIf:$Result.Preview
        $name = Split-Path -Leaf $pair.Server
        if ($merge.Created) { Add-ApiAction $Result "Settings ${name}: create from the package template" }
        elseif (@($merge.AddedKeys).Count -gt 0) { Add-ApiAction $Result "Settings ${name}: add new key(s) $(@($merge.AddedKeys) -join ', ') (server values kept)" }
        else { Add-ApiAction $Result "Settings ${name}: no new keys (server values kept)" }
        $merges += $merge
    }
    $Result.Settings = $merges
}

function Invoke-ApiDeployFiles([object] $Result, [string] $BackupRoot, [string] $ProductionTemplatePath) {
    # The server settings files live in the deployed folder, so the site copy holds them.
    $Result.BackupPath = Backup-Deployment -SourcePath $Result.TargetPath -BackupRoot $BackupRoot -Name $script:BackupName -WhatIf:$Result.Preview
    if ($Result.BackupPath) { Add-ApiAction $Result "Back up '$($Result.TargetPath)' to '$($Result.BackupPath)'" }
    else { Add-ApiAction $Result 'Backup: nothing deployed yet (first install)' }
    Enter-ApiOffline $Result
    Add-ApiAction $Result "Mirror '$($Result.AppFolder)' to '$($Result.TargetPath)' (server settings, $($script:AppOfflineFileName) and logs kept)"
    $Result.Payload = Copy-KitPayload -SourcePath $Result.AppFolder -DestinationPath $Result.TargetPath -WhatIf:$Result.Preview
    Invoke-ApiSettingsMerge -Result $Result -ProductionTemplatePath $ProductionTemplatePath
}

function Invoke-ApiSettingsCheck([object] $Result, [string] $ProductionTemplatePath, [string] $AppPoolName) {
    $environment = ConvertTo-ApiEnvironmentLayer (Get-ApiPoolEnvironmentVariables $AppPoolName)
    $layers = Get-ApiSettingsLayers -AppFolder $Result.AppFolder -TargetPath $Result.TargetPath -ProductionTemplatePath $ProductionTemplatePath -EnvironmentLayer $environment
    $details = @(Get-ApiSettingsGaps -Effective (Get-ApiEffectiveSettings $layers))
    $Result.SettingsGapDetails = $details
    $Result.SettingsGaps = [string[]] @($details | ForEach-Object { $_.Path } | Sort-Object -Unique)
    if ($details.Count -eq 0) { Write-ApiLog 'Required settings: all filled.' -Level $script:LogSuccess; return }
    $file = Join-Path $Result.TargetPath $script:ProductionSettingsFileName
    Write-ApiLog "Required settings are not ready; fix them in '$file' or the pool environment (see templates\api\README.md):" -Level $script:LogWarning
    foreach ($gap in $details) { Write-ApiLog "  $($gap.Path) ($($gap.Reason))" -Level $script:LogWarning }
}

# With gaps the API is refused a start: app_offline.htm stays (or is written on a first install).
function Invoke-ApiStartStep([object] $Result, [string] $AppPoolName) {
    $offline = Join-Path $Result.TargetPath $script:AppOfflineFileName
    if ($Result.SettingsGaps.Count -gt 0) {
        $Result.SiteHeldOffline = $true
        Add-ApiAction $Result "Keep the API offline ($($script:AppOfflineFileName)) until the settings above are filled"
        Write-ApiOfflinePage -TargetPath $Result.TargetPath -Html $script:NotConfiguredHtml -WhatIf:$Result.Preview | Out-Null
        return
    }
    Add-ApiAction $Result "Bring the API online: remove $($script:AppOfflineFileName) and recycle '$AppPoolName'"
    if ($Result.Preview) { return }
    if (Test-Path -LiteralPath $offline) { Remove-Item -LiteralPath $offline -Force -WhatIf:$false }
    Restart-ApiAppPool $AppPoolName
    Write-ApiLog 'The API is online.' -Level $script:LogSuccess
}

function Invoke-ApiHealthStep([object] $Result) {
    if ($Result.Preview) {
        $Result.Health = [int] [ApiHealthOutcome]::SkippedPreview
        Add-ApiAction $Result "Health probe skipped (-WhatIf): a real run waits for GET $($Result.HealthUrl) to answer 200 over validated TLS"
        return
    }
    if ($Result.SettingsGaps.Count -gt 0) {
        $Result.Health = [int] [ApiHealthOutcome]::SkippedSettingsIncomplete
        Write-ApiLog 'Health probe skipped: the API stays offline until the settings above are filled. Fill them and run the kit again.' -Level $script:LogWarning
        return
    }
    Assert-ApiCertificateValidationIntact
    Write-ApiLog "Waiting for GET $($Result.HealthUrl) (the first start is slow)..." -Level $script:LogStep
    $probe = Wait-ApiHealth -Url $Result.HealthUrl
    $Result.HealthStatusCode = $probe.StatusCode
    if ($probe.StatusCode -eq $script:HttpOk) {
        $Result.Health = [int] [ApiHealthOutcome]::Healthy
        Write-ApiLog "$($Result.HealthUrl) answers 200." -Level $script:LogSuccess
        return
    }
    $Result.Health = [int] [ApiHealthOutcome]::Unhealthy
    Write-ApiLog "Health check failed: $(Get-ApiHealthDiagnosis $probe)" -Level $script:LogError
    $rollback = if ($Result.BackupPath) { " Roll back: copy '$($Result.BackupPath)\site\*' over '$($Result.TargetPath)'." } else { '' }
    throw "The API is deployed but $($Result.HealthUrl) did not answer 200.$rollback"
}

# ==================================================================================================================
# The kit
# ==================================================================================================================
function Invoke-ApiKit {
    <#
    .SYNOPSIS
        Runs the API kit (see the script help). Returns the result object: Preview, AppFolder, TargetPath, WebConfig,
        PrerequisitesPassed, IisPlan, TuningPlan, BackupPath, Payload, Settings, SettingsGaps (paths),
        SettingsGapDetails (Path, Reason, ReasonId), SiteHeldOffline, HealthUrl, Health (ApiHealthOutcome as int),
        HealthStatusCode, PlannedActions.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string] $PackagePath,
        [string] $SiteName,
        [string] $AppPath = $script:DefaultAppPath,
        [string] $AppPoolName = $script:DefaultAppPoolName,
        [string] $TargetPath = (Join-Path $env:SystemDrive $script:DefaultTargetRelative),
        [string] $BackupRoot = (Join-Path $env:SystemDrive $script:DefaultBackupRelative),
        [string] $HealthBaseUrl
    )
    Assert-ApiKitParameters -PackagePath $PackagePath -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName -TargetPath $TargetPath -BackupRoot $BackupRoot -HealthBaseUrl $HealthBaseUrl
    $script:KitLogPath = $null
    $result = New-ApiKitResult -Preview ([bool] $WhatIfPreference) -TargetPath $TargetPath -HealthUrl (Get-ApiHealthUrl -BaseUrl $HealthBaseUrl -AppPath $AppPath)
    $mode = if ($result.Preview) { 'preview (-WhatIf): nothing is changed' } else { 'install' }
    Write-ApiLog "API kit: $mode. Site '$SiteName', application '$AppPath', pool '$AppPoolName', folder '$TargetPath'." -Level $script:LogStep

    $result.AppFolder = Resolve-ApiPackage $PackagePath
    if ((Test-ApiPathInside -Path $result.AppFolder -Parent $TargetPath) -or (Test-ApiPathInside -Path $TargetPath -Parent $result.AppFolder)) {
        throw "The package '$($result.AppFolder)' overlaps -TargetPath '$TargetPath'; copy the package somewhere else first. Nothing was changed."
    }
    Invoke-ApiPackageChecks $result
    Invoke-ApiPrerequisiteStep $result
    Assert-ApiSiteReady -Result $result -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName
    $productionTemplate = Join-Path $script:KitRoot $script:ProductionTemplateRelative
    if ($null -eq (Read-ApiJson $productionTemplate)) { throw "The kit template '$productionTemplate' is missing. Nothing was changed." }
    # The log file starts once every check that can refuse has passed: a refused run leaves no trace on the server.
    $script:KitLogPath = Join-Path $BackupRoot $script:LogFileName

    Invoke-ApiIisStep -Result $result -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName
    Invoke-ApiTuningStepAll -Result $result -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName -WhatIf:$result.Preview
    Invoke-ApiDeployFiles -Result $result -BackupRoot $BackupRoot -ProductionTemplatePath $productionTemplate
    Invoke-ApiSettingsCheck -Result $result -ProductionTemplatePath $productionTemplate -AppPoolName $AppPoolName
    Invoke-ApiStartStep -Result $result -AppPoolName $AppPoolName
    Invoke-ApiHealthStep $result
    return $result
}

# Dot-sourced (the Pester tests): define the functions only.
if ($MyInvocation.InvocationName -eq '.') { return }

$kitResult = Invoke-ApiKit @PSBoundParameters
if (-not $kitResult.Preview -and $kitResult.SettingsGaps.Count -gt 0) { exit $script:ExitSettingsIncomplete }
