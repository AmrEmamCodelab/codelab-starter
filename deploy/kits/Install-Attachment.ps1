#Requires -Version 5.1
<#
.SYNOPSIS
    Installs or updates the attachment service (Codelab.Attachments.Api) as a child IIS application.

.DESCRIPTION
    The first deploy kit built on Install-Common.psm1 (MODULE.md in this folder). The steps, in order:

      1. Checks the parameters and the package (refuses before anything changes).
      2. Prerequisites: IIS, WebAdministration, the ASP.NET Core Module V2 and the ASP.NET Core 10 runtime.
      3. Storage root: the server's Attachments:Storage:RootPath wins; -StorageRoot is used when the server has none.
         The root must be fully qualified and outside the app folder (the payload mirror deletes what is inside it).
      4. IIS: app pool ("No Managed Code") and child application under -SiteName, via Set-KitIisApplication.
      5. Backup of the deployed folder (logs excluded).
      6. app_offline.htm, then the payload mirror (server settings files and logs are never copied over or deleted),
         then Merge-AppSettings for appsettings.json and appsettings.Production.json (server values kept, only new
         keys added), then app_offline.htm is removed again (also when a step fails).
      7. Storage folder + Modify right for the app pool identity (a UNC share is an operator step).
      8. Settings check: lists every required key that is still empty (paths only, never values).
      9. Health probe GET <HealthBaseUrl><AppPath>/health/ready (the readiness route of Codelab.Observability
         HealthEndpoints; /health alone is not mapped). Skipped under -WhatIf and while settings are incomplete.

    Under -WhatIf nothing is written: no IIS change, folder, backup, copy, settings file, ACL or log file. Every
    planned action is printed instead.

    Exit codes (when run as a script): 0 done (or previewed), 1 refused or failed, 2 installed but settings incomplete.

.PARAMETER PackagePath
    The published service folder (it holds Codelab.Attachments.Api.dll), or a kit folder with that output in app\.
.PARAMETER SiteName
    The existing IIS site the child application goes under.
.PARAMETER AppPath
    The child application path. Default '/attachments'.
.PARAMETER AppPoolName
    The app pool. Default 'StarterAttachments'. Its identity (IIS AppPool\<name>) gets Modify on the storage root.
.PARAMETER StorageRoot
    The attachment store (local folder or UNC share). Used only when the server settings have no RootPath yet.
.PARAMETER TargetPath
    The folder IIS serves the application from. Default <SystemDrive>\inetpub\starter-attachments.
.PARAMETER BackupRoot
    Where backups and the install log go. Default <SystemDrive>\inetpub\backups\starter-attachments.
.PARAMETER HealthBaseUrl
    The site's base URL as seen from this server. Default 'http://localhost'.

.EXAMPLE
    .\Install-Attachment.ps1 -PackagePath .\attachments -SiteName 'Default Web Site' -StorageRoot 'E:\attachments' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $PackagePath,
    [string] $SiteName,
    [string] $AppPath,
    [string] $AppPoolName,
    [string] $StorageRoot,
    [string] $TargetPath,
    [string] $BackupRoot,
    [string] $HealthBaseUrl
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Enums (integers with explicit values) ------------------------------------------------------------------------
enum AttachmentStorageAction {
    None = 0
    CreateFolder = 1
    GrantModify = 2
    ShareAccessManual = 3
}

enum AttachmentStorageRootSource {
    None = 0
    Parameter = 1
    Server = 2
}

enum AttachmentHealthOutcome {
    NotRun = 0
    SkippedPreview = 1
    SkippedSettingsIncomplete = 2
    Healthy = 3
    Unhealthy = 4
}

# --- Constants ----------------------------------------------------------------------------------------------------
$script:CommonModuleFileName        = 'Install-Common.psm1'
$script:ProductionTemplateRelative  = 'templates\attachment\appsettings.Production.template.json'
$script:BackupName                  = 'attachments'
$script:LogFileName                 = 'install-attachment.log'
$script:RuntimeMajor                = 10

$script:DefaultAppPath              = '/attachments'
$script:DefaultAppPoolName          = 'StarterAttachments'
$script:DefaultTargetRelative       = 'inetpub\starter-attachments'
$script:DefaultBackupRelative       = 'inetpub\backups\starter-attachments'
$script:DefaultHealthBaseUrl        = 'http://localhost'

# The package: the published service output, directly or in app\.
$script:PackageAppFolder            = 'app'
$script:EntryAssemblyName           = 'Codelab.Attachments.Api.dll'
$script:WebConfigFileName           = 'web.config'
$script:BaseSettingsFileName        = 'appsettings.json'
$script:ProductionSettingsFileName  = 'appsettings.Production.json'

# Settings (.NET configuration paths). The ones listed in RequiredSettingPaths must be filled on the server.
$script:ConfigSeparator             = ':'
$script:StorageRootSettingPath      = 'Attachments:Storage:RootPath'
$script:RequiredSettingPaths        = @(
    'Attachments:Storage:RootPath'
    'Attachments:Cors:AllowedOrigins'
    'Codelab:Security:Tickets:ActiveKeyId'
    'Codelab:Security:Tickets:Keys'
    'Codelab:Security:Uploads:ActiveKeyId'
    'Codelab:Security:Uploads:Keys'
    'Codelab:Security:ServiceAssertions:Server:Audience'
    'Codelab:Security:ServiceAssertions:Server:TrustedServices'
)
$script:JsonWriteDepth              = 32
$script:EmptyContainer              = New-Object -TypeName psobject

# Paths: a drive-rooted folder (C:\x) or a UNC share (\\fileserver.example.test\share).
$script:DriveRootedPattern          = '^[A-Za-z]:\\'
$script:UncPattern                  = '^\\\\[^\\]+\\[^\\]+'
$script:PathSeparator               = '\'
$script:InvalidPoolNameChars        = [char[]] '\/:*?"<>|'

# Storage access for the app pool identity.
$script:AppPoolIdentityFormat       = 'IIS AppPool\{0}'
$script:StorageRights               = [Security.AccessControl.FileSystemRights]::Modify
$script:StorageInheritance          = [Security.AccessControl.InheritanceFlags] 'ContainerInherit, ObjectInherit'
$script:StoragePropagation          = [Security.AccessControl.PropagationFlags]::None
$script:StorageAllow                = [Security.AccessControl.AccessControlType]::Allow

# app_offline.htm: ANCM stops the app and releases the DLL locks while it exists.
$script:AppOfflineFileName          = 'app_offline.htm'
$script:AppOfflineHtml              = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Update in progress</title></head><body><p>The service is being updated and will be back in a moment.</p></body></html>'
$script:ShutdownWaitSeconds         = 3

# Health probe.
$script:HealthPath                  = '/health/ready'  # HealthEndpoints.ReadyPath (Codelab.Observability)
$script:HealthAttempts              = 12
$script:HealthDelaySeconds          = 5
$script:HealthRequestTimeoutSeconds = 10
$script:HttpOk                      = 200
$script:NoHttpStatus                = 0
$script:HtmlBodyStart               = '<'

$script:ExitSettingsIncomplete      = 2
$script:UrlSeparator                = '/'
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
function Write-AttachmentLog([string] $Message, [string] $Level = $script:LogInfo) {
    $logPath = if ($WhatIfPreference) { $null } else { $script:KitLogPath }
    Write-KitLog -Message $Message -Level $Level -LogPath $logPath -WhatIf:$false
}

function Add-AttachmentAction([object] $Result, [string] $Text) {
    $Result.PlannedActions.Add($Text)
    Write-AttachmentLog $Text -Level $script:LogStep
}

# ==================================================================================================================
# Parameters, paths and the package (pure checks; they throw before anything changes)
# ==================================================================================================================
function Test-AttachmentFullyQualifiedPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return ($Path -match $script:DriveRootedPattern -or $Path -match $script:UncPattern)
}

function Test-AttachmentUncPath([string] $Path) {
    return ($Path -match $script:UncPattern)
}

function ConvertTo-AttachmentComparablePath([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd($script:PathSeparator)
}

# True when $Path is $Parent itself or anything below it (case-insensitive, like NTFS).
function Test-AttachmentPathInside([string] $Path, [string] $Parent) {
    $child = ConvertTo-AttachmentComparablePath $Path
    $root = ConvertTo-AttachmentComparablePath $Parent
    if ([string]::Equals($child, $root, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $child.StartsWith($root + $script:PathSeparator, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-AttachmentKitParameters {
    param([string] $PackagePath, [string] $SiteName, [string] $AppPath, [string] $AppPoolName, [string] $TargetPath, [string] $BackupRoot)
    if ([string]::IsNullOrWhiteSpace($PackagePath)) { throw '-PackagePath is required: the published attachment service folder. Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($SiteName)) { throw '-SiteName is required: the existing IIS site the child application goes under. Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($AppPath) -or $AppPath.Trim().Trim($script:UrlSeparator).Length -eq 0) {
        throw "-AppPath must name a child application (for example '$($script:DefaultAppPath)'), not the site root. Nothing was changed."
    }
    if ([string]::IsNullOrWhiteSpace($AppPoolName) -or $AppPoolName.IndexOfAny($script:InvalidPoolNameChars) -ge 0) {
        throw "-AppPoolName '$AppPoolName' is not a valid app pool name. Nothing was changed."
    }
    if (-not (Test-AttachmentFullyQualifiedPath $TargetPath)) { throw "-TargetPath '$TargetPath' must be a fully qualified folder. Nothing was changed." }
    if (-not (Test-AttachmentFullyQualifiedPath $BackupRoot)) { throw "-BackupRoot '$BackupRoot' must be a fully qualified folder. Nothing was changed." }
    if (Test-AttachmentPathInside -Path $BackupRoot -Parent $TargetPath) {
        throw "-BackupRoot '$BackupRoot' is inside -TargetPath '$TargetPath'; the payload mirror would delete the backups. Nothing was changed."
    }
}

# Returns the folder holding the published service: the package itself or its app\ folder.
function Resolve-AttachmentPackage([string] $PackagePath) {
    if (-not (Test-Path -LiteralPath $PackagePath -PathType Container)) { throw "Package folder '$PackagePath' does not exist. Nothing was changed." }
    $candidates = @($PackagePath, (Join-Path $PackagePath $script:PackageAppFolder))
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath (Join-Path $candidate $script:EntryAssemblyName) -PathType Leaf)) { continue }
        foreach ($required in @($script:WebConfigFileName, $script:BaseSettingsFileName)) {
            if (-not (Test-Path -LiteralPath (Join-Path $candidate $required) -PathType Leaf)) {
                throw "The package '$candidate' has no $required; it is not a complete publish output. Nothing was changed."
            }
        }
        return (Resolve-Path -LiteralPath $candidate).ProviderPath
    }
    throw "No $($script:EntryAssemblyName) in '$PackagePath' or its $($script:PackageAppFolder)\ folder; give the published attachment service. Nothing was changed."
}

# ==================================================================================================================
# Settings: read, layer like .NET configuration, find the required keys that are still empty
# ==================================================================================================================
function Read-AttachmentJson([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $text = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).ProviderPath, (New-Object Text.UTF8Encoding($false)))
    # The parser error is not passed on: it quotes file text, and settings files hold key material.
    try { return (ConvertFrom-Json -InputObject $text -ErrorAction Stop) }
    catch { throw "'$Path' is not valid JSON (comments and trailing commas are not supported). Nothing was changed." }
}

function Test-AttachmentJsonObject([object] $Value) {
    return ($null -ne $Value -and $Value -is [System.Management.Automation.PSCustomObject])
}

function Test-AttachmentJsonArray([object] $Value) {
    return ($null -ne $Value -and $Value -isnot [string] -and $Value -is [System.Collections.IList])
}

# Flattens a JSON object into "A:B:0:C" -> value, the way .NET configuration keys it. An empty object or array is
# recorded as the EmptyContainer marker, so a required section that is present but empty is still seen.
function Add-AttachmentSettingLeaves([object] $Node, [string] $Prefix, [hashtable] $Into) {
    if (Test-AttachmentJsonObject $Node) {
        $members = @($Node.PSObject.Properties)
        if ($members.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        foreach ($member in $members) {
            $path = if ($Prefix) { $Prefix + $script:ConfigSeparator + $member.Name } else { $member.Name }
            Add-AttachmentSettingLeaves -Node $member.Value -Prefix $path -Into $Into
        }
        return
    }
    if (Test-AttachmentJsonArray $Node) {
        if ($Node.Count -eq 0) { $Into[$Prefix] = $script:EmptyContainer; return }
        for ($index = 0; $index -lt $Node.Count; $index++) {
            Add-AttachmentSettingLeaves -Node $Node[$index] -Prefix ($Prefix + $script:ConfigSeparator + $index) -Into $Into
        }
        return
    }
    $Into[$Prefix] = $Node
}

# Layers are lowest precedence first; a later layer overrides a leaf (case-insensitive keys, like .NET configuration).
function Get-AttachmentEffectiveSettings([object[]] $Layers) {
    $leaves = @{}
    foreach ($layer in @($Layers | Where-Object { $null -ne $_ })) { Add-AttachmentSettingLeaves -Node $layer -Prefix '' -Into $leaves }
    return $leaves
}

function Test-AttachmentEmptyValue([object] $Value) {
    if ($null -eq $Value) { return $true }
    return ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value))
}

<#
    Returns the required configuration paths that are missing or empty in the effective settings (sorted). A section
    counts as filled only when it has at least one value and none of its values is empty; the paths returned point
    at the empty values (for example Codelab:Security:Tickets:Keys:0:MaterialBase64). Values are never returned.
#>
function Get-AttachmentSettingsGaps([hashtable] $Effective, [string[]] $RequiredPaths) {
    $gaps = New-Object 'System.Collections.Generic.List[string]'
    foreach ($required in $RequiredPaths) {
        $below = $required + $script:ConfigSeparator
        $values = @($Effective.Keys | Where-Object {
            ($_ -eq $required -or $_.StartsWith($below, [StringComparison]::OrdinalIgnoreCase)) -and
            -not [object]::ReferenceEquals($Effective[$_], $script:EmptyContainer)
        })
        if ($values.Count -eq 0) { $gaps.Add($required); continue }
        foreach ($key in $values) { if (Test-AttachmentEmptyValue $Effective[$key]) { $gaps.Add($key) } }
    }
    return [string[]] @($gaps | Sort-Object -Unique)
}

function Get-AttachmentSettingValue([hashtable] $Effective, [string] $Path) {
    if (-not $Effective.ContainsKey($Path)) { return $null }
    $value = $Effective[$Path]
    if ([object]::ReferenceEquals($value, $script:EmptyContainer)) { return $null }
    return $value
}

# The settings as they are (or will be) after the merge: package template < server file, per file, base < Production.
function Get-AttachmentSettingsLayers([string] $AppFolder, [string] $TargetPath, [string] $ProductionTemplatePath) {
    return @(
        (Read-AttachmentJson (Join-Path $AppFolder $script:BaseSettingsFileName)),
        (Read-AttachmentJson (Join-Path $TargetPath $script:BaseSettingsFileName)),
        (Read-AttachmentJson $ProductionTemplatePath),
        (Read-AttachmentJson (Join-Path $TargetPath $script:ProductionSettingsFileName))
    )
}

# ==================================================================================================================
# Storage root: resolve (server wins), check it is safe, write the per-run Production template
# ==================================================================================================================
function Resolve-AttachmentStorageRoot([string] $Requested, [string] $Configured) {
    $hasConfigured = -not [string]::IsNullOrWhiteSpace($Configured)
    $hasRequested = -not [string]::IsNullOrWhiteSpace($Requested)
    $resolution = [pscustomobject]@{ Root = $null; Source = [AttachmentStorageRootSource]::None; Warning = $null }
    if ($hasConfigured) {
        $resolution.Root = $Configured
        $resolution.Source = [AttachmentStorageRootSource]::Server
        $differs = $hasRequested -and -not [string]::Equals($Requested.TrimEnd($script:PathSeparator), $Configured.TrimEnd($script:PathSeparator), [StringComparison]::OrdinalIgnoreCase)
        if ($differs) {
            $resolution.Warning = "The server settings keep $($script:StorageRootSettingPath) = '$Configured'; -StorageRoot '$Requested' is ignored. To move the store, move the files and edit $($script:ProductionSettingsFileName) on the server."
        }
        return $resolution
    }
    if ($hasRequested) {
        $resolution.Root = $Requested
        $resolution.Source = [AttachmentStorageRootSource]::Parameter
    }
    return $resolution
}

function Assert-AttachmentStorageRootSafe([string] $StorageRoot, [string] $TargetPath) {
    if (-not (Test-AttachmentFullyQualifiedPath $StorageRoot)) {
        throw "The storage root '$StorageRoot' must be a fully qualified folder or UNC share; a relative root would land inside the app folder. Nothing was changed."
    }
    if ((Test-AttachmentPathInside -Path $StorageRoot -Parent $TargetPath) -or (Test-AttachmentPathInside -Path $TargetPath -Parent $StorageRoot)) {
        throw "The storage root '$StorageRoot' overlaps the app folder '$TargetPath'; the payload mirror would delete stored files. Nothing was changed."
    }
}

function Set-AttachmentJsonValue([object] $Root, [string] $Path, [object] $Value) {
    $node = $Root
    $segments = $Path.Split($script:ConfigSeparator)
    for ($index = 0; $index -lt $segments.Length - 1; $index++) {
        $next = $node.PSObject.Properties[$segments[$index]]
        if ($null -eq $next) { $node | Add-Member -NotePropertyName $segments[$index] -NotePropertyValue ([pscustomobject]@{}); $next = $node.PSObject.Properties[$segments[$index]] }
        $node = $next.Value
    }
    $leaf = $segments[$segments.Length - 1]
    if ($null -eq $node.PSObject.Properties[$leaf]) { $node | Add-Member -NotePropertyName $leaf -NotePropertyValue $Value }
    else { $node.$leaf = $Value }
}

# The kit's Production template with this run's storage root filled in, written to a temp file the caller deletes.
# It is only ever the merge *source*: Merge-AppSettings adds the root only when the server has no RootPath key.
function New-AttachmentProductionTemplate([string] $TemplatePath, [string] $StorageRoot) {
    $template = Read-AttachmentJson $TemplatePath
    if ($null -eq $template) { throw "The kit template '$TemplatePath' is missing. Nothing was changed." }
    if (-not [string]::IsNullOrWhiteSpace($StorageRoot)) { Set-AttachmentJsonValue -Root $template -Path $script:StorageRootSettingPath -Value $StorageRoot }
    $path = [IO.Path]::Combine([IO.Path]::GetTempPath(), [IO.Path]::GetRandomFileName() + $script:TempTemplateExtension)
    [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $template -Depth $script:JsonWriteDepth), (New-Object Text.UTF8Encoding($false)))
    return $path
}

# ==================================================================================================================
# Storage folder and access for the app pool identity: facts (read-only), plan (pure), apply
# ==================================================================================================================
function Test-AttachmentRulesGrantModify([object[]] $Rules, [string] $Identity) {
    foreach ($rule in @($Rules)) {
        if ($null -eq $rule) { continue }
        $sameIdentity = [string]::Equals([string] $rule.IdentityReference, $Identity, [StringComparison]::OrdinalIgnoreCase)
        $allows = $rule.AccessControlType -eq $script:StorageAllow
        $hasRights = ($rule.FileSystemRights -band $script:StorageRights) -eq $script:StorageRights
        $inherits = ($rule.InheritanceFlags -band $script:StorageInheritance) -eq $script:StorageInheritance
        if ($sameIdentity -and $allows -and $hasRights -and $inherits) { return $true }
    }
    return $false
}

function Get-AttachmentStorageFacts([string] $StorageRoot, [string] $Identity) {
    $facts = [pscustomobject]@{ Exists = $false; IsUnc = (Test-AttachmentUncPath $StorageRoot); AccessKnown = $false; IdentityHasModify = $false }
    $facts.Exists = Test-Path -LiteralPath $StorageRoot -PathType Container
    if (-not $facts.Exists -or $facts.IsUnc) { return $facts }
    try {
        $facts.IdentityHasModify = Test-AttachmentRulesGrantModify -Rules @((Get-Acl -LiteralPath $StorageRoot).Access) -Identity $Identity
        $facts.AccessKnown = $true
    }
    catch { Write-AttachmentLog "The access list of '$StorageRoot' could not be read ($($_.Exception.Message)); the grant is planned." -Level $script:LogWarning }
    return $facts
}

function New-AttachmentStorageStep([AttachmentStorageAction] $Action, [string] $Detail) {
    return [pscustomobject]@{ Action = $Action.ToString(); ActionId = [int] $Action; Detail = $Detail; Applied = $false }
}

<#
    Pure: the same facts always give the same plan. A local folder gets CreateFolder (when missing) and GrantModify
    (when the identity has no inheritable Modify rule). A UNC share is never changed by the kit beyond creating the
    folder: the app pool identity reaches a share as the server's computer account, which the share owner grants.
#>
function Get-AttachmentStoragePlan([string] $StorageRoot, [string] $Identity, [object] $Facts) {
    $steps = New-Object 'System.Collections.Generic.List[object]'
    if (-not $Facts.Exists) { $steps.Add((New-AttachmentStorageStep CreateFolder "create '$StorageRoot'")) }
    if ($Facts.IsUnc) {
        $steps.Add((New-AttachmentStorageStep ShareAccessManual "operator: give this server's computer account (or the pool's custom identity) Modify on '$StorageRoot'"))
    }
    elseif (-not $Facts.IdentityHasModify) {
        $steps.Add((New-AttachmentStorageStep GrantModify "grant '$Identity' Modify (inherited by subfolders and files) on '$StorageRoot'"))
    }
    if ($steps.Count -eq 0) { $steps.Add((New-AttachmentStorageStep None "'$Identity' already has Modify on '$StorageRoot'")) }
    return [object[]] $steps.ToArray()
}

function Invoke-AttachmentStorageStep([object] $Step, [string] $StorageRoot, [string] $Identity) {
    switch ([AttachmentStorageAction] $Step.ActionId) {
        ([AttachmentStorageAction]::CreateFolder) { New-Item -ItemType Directory -Path $StorageRoot -Force | Out-Null }
        ([AttachmentStorageAction]::GrantModify) {
            $acl = Get-Acl -LiteralPath $StorageRoot
            $rule = New-Object Security.AccessControl.FileSystemAccessRule($Identity, $script:StorageRights, $script:StorageInheritance, $script:StoragePropagation, $script:StorageAllow)
            $acl.AddAccessRule($rule)
            Set-Acl -LiteralPath $StorageRoot -AclObject $acl
        }
    }
}

# ==================================================================================================================
# Health probe
# ==================================================================================================================
function Get-AttachmentHealthUrl([string] $BaseUrl, [string] $AppPath) {
    $app = $script:UrlSeparator + $AppPath.Trim().Trim($script:UrlSeparator)
    return $BaseUrl.TrimEnd($script:UrlSeparator) + $app + $script:HealthPath
}

function Invoke-AttachmentHealthRequest([string] $Url) {
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

# An HTML body means IIS (or the ASP.NET Core Module) answered, not the service: the app did not start.
function Get-AttachmentHealthDiagnosis([object] $Probe) {
    if ($Probe.StatusCode -eq $script:HttpOk) { return 'the service answered 200' }
    if ($Probe.StatusCode -eq $script:NoHttpStatus) { return "no answer ($($Probe.Error)): check the site binding and -HealthBaseUrl" }
    if (([string] $Probe.Body).TrimStart().StartsWith($script:HtmlBodyStart)) {
        return "HTTP $($Probe.StatusCode) with an HTML body: IIS answered, the service did not start (usually a refused setting). Read Event Viewer > Application (IIS AspNetCore Module V2) and the logs folder."
    }
    return "HTTP $($Probe.StatusCode) from the service itself: read its problem+json body and the logs folder"
}

function Wait-AttachmentHealth([string] $Url, [int] $Attempts = $script:HealthAttempts, [int] $DelaySeconds = $script:HealthDelaySeconds) {
    $probe = $null
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $probe = Invoke-AttachmentHealthRequest $Url
        if ($probe.StatusCode -eq $script:HttpOk) { return $probe }
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
    return $probe
}

# ==================================================================================================================
# Steps
# ==================================================================================================================
function New-AttachmentKitResult([bool] $Preview, [string] $TargetPath, [string] $HealthUrl) {
    return [pscustomobject]@{
        Preview = $Preview; AppFolder = $null; TargetPath = $TargetPath
        StorageRoot = $null; StorageRootSource = [int] [AttachmentStorageRootSource]::None
        PrerequisitesPassed = $false; IisPlan = @(); BackupPath = $null; Payload = $null; Settings = @(); StoragePlan = @()
        SettingsGaps = [string[]] @(); HealthUrl = $HealthUrl; Health = [int] [AttachmentHealthOutcome]::NotRun; HealthStatusCode = $null
        PlannedActions = (New-Object 'System.Collections.Generic.List[string]')
    }
}

function Invoke-AttachmentPrerequisiteStep([object] $Result) {
    $check = Test-KitPrerequisites -RuntimeMajor $script:RuntimeMajor -RequireAdministrator:(-not $Result.Preview)
    $Result.PrerequisitesPassed = [bool] $check.Passed
    if ($check.Passed) { return }
    if (-not $Result.Preview) { throw 'Prerequisites are missing (see the lines above). Nothing was changed.' }
    Write-AttachmentLog 'Prerequisites are missing; a real run stops here. The preview continues.' -Level $script:LogWarning
}

function Invoke-AttachmentStorageRootStep([object] $Result, [string] $StorageRoot, [object[]] $ServerLayers) {
    $configured = Get-AttachmentSettingValue -Effective (Get-AttachmentEffectiveSettings $ServerLayers) -Path $script:StorageRootSettingPath
    $resolution = Resolve-AttachmentStorageRoot -Requested $StorageRoot -Configured ([string] $configured)
    if ($resolution.Warning) { Write-AttachmentLog $resolution.Warning -Level $script:LogWarning }
    if ($null -eq $resolution.Root) {
        $message = "No storage root: give -StorageRoot (the server settings have no $($script:StorageRootSettingPath))."
        if (-not $Result.Preview) { throw "$message Nothing was changed." }
        Write-AttachmentLog "$message A real run stops here." -Level $script:LogWarning
        return
    }
    Assert-AttachmentStorageRootSafe -StorageRoot $resolution.Root -TargetPath $Result.TargetPath
    $Result.StorageRoot = $resolution.Root
    $Result.StorageRootSource = [int] $resolution.Source
    Write-AttachmentLog "Storage root '$($resolution.Root)' (from $($resolution.Source))." -Level $script:LogInfo
}

function Invoke-AttachmentIisStep([object] $Result, [string] $SiteName, [string] $AppPath, [string] $AppPoolName) {
    $plan = @(Set-KitIisApplication -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName -PhysicalPath $Result.TargetPath -WhatIf:$Result.Preview)
    $Result.IisPlan = $plan
    foreach ($step in $plan) { Add-AttachmentAction $Result "IIS $($step.Action) $($step.Target): $(@($step.Changes) -join '; ')" }
}

function Enter-AttachmentOffline {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result)
    $offline = Join-Path $Result.TargetPath $script:AppOfflineFileName
    $running = Test-Path -LiteralPath (Join-Path $Result.TargetPath $script:EntryAssemblyName) -PathType Leaf
    if (-not $running) { return $null }
    Add-AttachmentAction $Result "Take the app offline with $($script:AppOfflineFileName) while files change"
    if (-not $PSCmdlet.ShouldProcess($offline, 'Write the offline page')) { return $null }
    [IO.File]::WriteAllText($offline, $script:AppOfflineHtml, (New-Object Text.UTF8Encoding($false)))
    Start-Sleep -Seconds $script:ShutdownWaitSeconds
    return $offline
}

function Exit-AttachmentOffline([string] $OfflinePath) {
    if ([string]::IsNullOrEmpty($OfflinePath) -or -not (Test-Path -LiteralPath $OfflinePath)) { return }
    Remove-Item -LiteralPath $OfflinePath -Force -WhatIf:$false
    Write-AttachmentLog 'The app is back online.' -Level $script:LogSuccess
}

function Invoke-AttachmentSettingsMerge([object] $Result, [string] $ProductionTemplatePath) {
    $merges = @()
    $pairs = @(
        @{ Template = (Join-Path $Result.AppFolder $script:BaseSettingsFileName); Server = (Join-Path $Result.TargetPath $script:BaseSettingsFileName) },
        @{ Template = $ProductionTemplatePath; Server = (Join-Path $Result.TargetPath $script:ProductionSettingsFileName) }
    )
    foreach ($pair in $pairs) {
        $merge = Merge-AppSettings -TemplatePath $pair.Template -ServerPath $pair.Server -WhatIf:$Result.Preview
        $name = Split-Path -Leaf $pair.Server
        if ($merge.Created) { Add-AttachmentAction $Result "Settings ${name}: create from the package template" }
        elseif (@($merge.AddedKeys).Count -gt 0) { Add-AttachmentAction $Result "Settings ${name}: add new key(s) $(@($merge.AddedKeys) -join ', ') (server values kept)" }
        else { Add-AttachmentAction $Result "Settings ${name}: no new keys (server values kept)" }
        $merges += $merge
    }
    $Result.Settings = $merges
}

function Invoke-AttachmentDeployFiles([object] $Result, [string] $BackupRoot, [string] $ProductionTemplatePath) {
    $Result.BackupPath = Backup-Deployment -SourcePath $Result.TargetPath -BackupRoot $BackupRoot -Name $script:BackupName -WhatIf:$Result.Preview
    if ($Result.BackupPath) { Add-AttachmentAction $Result "Back up '$($Result.TargetPath)' to '$($Result.BackupPath)'" }
    else { Add-AttachmentAction $Result 'Backup: nothing deployed yet (first install)' }
    $offline = Enter-AttachmentOffline $Result
    try {
        Add-AttachmentAction $Result "Mirror '$($Result.AppFolder)' to '$($Result.TargetPath)' (server settings, $($script:AppOfflineFileName) and logs kept)"
        $Result.Payload = Copy-KitPayload -SourcePath $Result.AppFolder -DestinationPath $Result.TargetPath -WhatIf:$Result.Preview
        Invoke-AttachmentSettingsMerge -Result $Result -ProductionTemplatePath $ProductionTemplatePath
    }
    finally { Exit-AttachmentOffline $offline }
}

function Invoke-AttachmentStorageAccessStep {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Result, [string] $AppPoolName)
    if ($null -eq $Result.StorageRoot) { return }
    $identity = $script:AppPoolIdentityFormat -f $AppPoolName
    $facts = Get-AttachmentStorageFacts -StorageRoot $Result.StorageRoot -Identity $identity
    $plan = @(Get-AttachmentStoragePlan -StorageRoot $Result.StorageRoot -Identity $identity -Facts $facts)
    $Result.StoragePlan = $plan
    foreach ($step in $plan) {
        Add-AttachmentAction $Result "Storage $($step.Action): $($step.Detail)"
        $applies = $step.ActionId -eq [int] [AttachmentStorageAction]::CreateFolder -or $step.ActionId -eq [int] [AttachmentStorageAction]::GrantModify
        if ($applies -and $PSCmdlet.ShouldProcess($Result.StorageRoot, $step.Detail)) {
            Invoke-AttachmentStorageStep -Step $step -StorageRoot $Result.StorageRoot -Identity $identity
            $step.Applied = $true
        }
        if ($step.ActionId -eq [int] [AttachmentStorageAction]::ShareAccessManual) { Write-AttachmentLog "Operator step: $($step.Detail)." -Level $script:LogWarning }
    }
}

function Invoke-AttachmentSettingsCheck([object] $Result, [string] $ProductionTemplatePath) {
    $layers = Get-AttachmentSettingsLayers -AppFolder $Result.AppFolder -TargetPath $Result.TargetPath -ProductionTemplatePath $ProductionTemplatePath
    $Result.SettingsGaps = [string[]] @(Get-AttachmentSettingsGaps -Effective (Get-AttachmentEffectiveSettings $layers) -RequiredPaths $script:RequiredSettingPaths)
    if ($Result.SettingsGaps.Count -eq 0) { Write-AttachmentLog 'Required settings: all filled.' -Level $script:LogSuccess; return }
    $file = Join-Path $Result.TargetPath $script:ProductionSettingsFileName
    Write-AttachmentLog "Required settings still empty; fill them in '$file' (see templates\attachment\README.md):" -Level $script:LogWarning
    foreach ($gap in $Result.SettingsGaps) { Write-AttachmentLog "  $gap" -Level $script:LogWarning }
}

function Invoke-AttachmentHealthStep([object] $Result) {
    if ($Result.Preview) {
        $Result.Health = [int] [AttachmentHealthOutcome]::SkippedPreview
        Add-AttachmentAction $Result "Health probe skipped (-WhatIf): a real run waits for GET $($Result.HealthUrl) to answer 200"
        return
    }
    if ($Result.SettingsGaps.Count -gt 0) {
        $Result.Health = [int] [AttachmentHealthOutcome]::SkippedSettingsIncomplete
        Write-AttachmentLog 'Health probe skipped: the service refuses to start until the settings above are filled. Fill them, then recycle the app pool and run the kit again.' -Level $script:LogWarning
        return
    }
    Write-AttachmentLog "Waiting for GET $($Result.HealthUrl) (the first start is slow)..." -Level $script:LogStep
    $probe = Wait-AttachmentHealth -Url $Result.HealthUrl
    $Result.HealthStatusCode = $probe.StatusCode
    if ($probe.StatusCode -eq $script:HttpOk) {
        $Result.Health = [int] [AttachmentHealthOutcome]::Healthy
        Write-AttachmentLog "$($Result.HealthUrl) answers 200." -Level $script:LogSuccess
        return
    }
    $Result.Health = [int] [AttachmentHealthOutcome]::Unhealthy
    Write-AttachmentLog "Health check failed: $(Get-AttachmentHealthDiagnosis $probe)" -Level $script:LogError
    $rollback = if ($Result.BackupPath) { " Roll back: copy '$($Result.BackupPath)\site\*' over '$($Result.TargetPath)'." } else { '' }
    throw "The attachment service is deployed but $($Result.HealthUrl) did not answer 200.$rollback"
}

# ==================================================================================================================
# The kit
# ==================================================================================================================
function Invoke-AttachmentKit {
    <#
    .SYNOPSIS
        Runs the attachment kit (see the script help). Returns the result object: Preview, AppFolder, TargetPath,
        StorageRoot, StorageRootSource, PrerequisitesPassed, IisPlan, BackupPath, Payload, Settings, StoragePlan,
        SettingsGaps, HealthUrl, Health (AttachmentHealthOutcome as int), HealthStatusCode, PlannedActions.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string] $PackagePath,
        [string] $SiteName,
        [string] $AppPath = $script:DefaultAppPath,
        [string] $AppPoolName = $script:DefaultAppPoolName,
        [string] $StorageRoot,
        [string] $TargetPath = (Join-Path $env:SystemDrive $script:DefaultTargetRelative),
        [string] $BackupRoot = (Join-Path $env:SystemDrive $script:DefaultBackupRelative),
        [string] $HealthBaseUrl = $script:DefaultHealthBaseUrl
    )
    Assert-AttachmentKitParameters -PackagePath $PackagePath -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName -TargetPath $TargetPath -BackupRoot $BackupRoot
    $script:KitLogPath = $null
    $result = New-AttachmentKitResult -Preview ([bool] $WhatIfPreference) -TargetPath $TargetPath -HealthUrl (Get-AttachmentHealthUrl -BaseUrl $HealthBaseUrl -AppPath $AppPath)
    $mode = if ($result.Preview) { 'preview (-WhatIf): nothing is changed' } else { 'install' }
    Write-AttachmentLog "Attachment service kit: $mode. Site '$SiteName', application '$AppPath', pool '$AppPoolName', folder '$TargetPath'." -Level $script:LogStep

    $result.AppFolder = Resolve-AttachmentPackage $PackagePath
    if ((Test-AttachmentPathInside -Path $result.AppFolder -Parent $TargetPath) -or (Test-AttachmentPathInside -Path $TargetPath -Parent $result.AppFolder)) {
        throw "The package '$($result.AppFolder)' overlaps -TargetPath '$TargetPath'; copy the package somewhere else first. Nothing was changed."
    }
    Invoke-AttachmentPrerequisiteStep $result
    $serverLayers = @((Read-AttachmentJson (Join-Path $TargetPath $script:BaseSettingsFileName)), (Read-AttachmentJson (Join-Path $TargetPath $script:ProductionSettingsFileName)))
    Invoke-AttachmentStorageRootStep -Result $result -StorageRoot $StorageRoot -ServerLayers $serverLayers
    # The log file starts once every check that can refuse has passed: a refused run leaves no trace on the server.
    $script:KitLogPath = Join-Path $BackupRoot $script:LogFileName
    Invoke-AttachmentIisStep -Result $result -SiteName $SiteName -AppPath $AppPath -AppPoolName $AppPoolName

    $productionTemplate = New-AttachmentProductionTemplate -TemplatePath (Join-Path $script:KitRoot $script:ProductionTemplateRelative) -StorageRoot $result.StorageRoot
    try {
        Invoke-AttachmentDeployFiles -Result $result -BackupRoot $BackupRoot -ProductionTemplatePath $productionTemplate
        Invoke-AttachmentStorageAccessStep -Result $result -AppPoolName $AppPoolName
        Invoke-AttachmentSettingsCheck -Result $result -ProductionTemplatePath $productionTemplate
    }
    finally { Remove-Item -LiteralPath $productionTemplate -Force -WhatIf:$false -ErrorAction SilentlyContinue }
    Invoke-AttachmentHealthStep $result
    return $result
}

# Dot-sourced (the Pester tests): define the functions only.
if ($MyInvocation.InvocationName -eq '.') { return }

$kitResult = Invoke-AttachmentKit @PSBoundParameters
if (-not $kitResult.Preview -and $kitResult.SettingsGaps.Count -gt 0) { exit $script:ExitSettingsIncomplete }
