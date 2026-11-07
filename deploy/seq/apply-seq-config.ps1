#Requires -Version 5.1
<#
.SYNOPSIS
    Applies the Seq signals, saved query, alerts, dashboard, retention and API keys kept as code in deploy/seq to
    one tier of a Seq server. Run it again at any time: it converges and never duplicates.

.DESCRIPTION
    What it applies, and how (MODULE.md in this folder has the details):

      templates\*.template   Rendered for the tier (-Environment, thresholds.<env>.json), then imported with
                             `seqcli template import --merge`: every entity is matched on the server by title, so a
                             re-run updates in place and an edit made in the Seq UI is overwritten.
      retention              Through the Seq HTTP API (a template import cannot merge retention policies): delete
                             events after seq-config.json retention.logsDays.
      API keys               Through the Seq HTTP API. One Ingest key per application and tier (one per DMZ node for
                             the gateway, from thresholds.<env>.json gatewayNodes), with the applied properties
                             Application and DeploymentEnvironment. A new key's token is PRINTED ONCE for the
                             operator and never written to a file. A key whose settings drifted fails the run (its
                             token is in use; revoke and re-create it on purpose). The shared Read key the Ops
                             Console uses is created only with -IncludeReadKeys.

    The admin key comes only from the SEQ_ADMIN_KEY environment variable (never a parameter, so it stays out of
    shell history). It reaches seqcli through this process's SEQCLI_CONNECTION_APIKEY, which is restored afterwards.
    -ServerUrl must be https (validated TLS; there is no switch that skips certificate checks). Plain http is
    accepted only for -Environment development on localhost.

    -WhatIf renders and validates every template and lists what would be applied; it contacts no server.
    -Remove deletes this tier's alerts and dashboard and, once no tier has any left, the shared signals and saved
    query. It never deletes a key. -Revoke <name> deletes this tier's one Ingest key for <name> and nothing else.

    Exit code 0 when everything expected is in place, 1 when a check failed.

.EXAMPLE
    $env:SEQ_ADMIN_KEY = '<admin key>'; .\apply-seq-config.ps1 -Environment test -ServerUrl https://logs.example.test -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('development', 'test', 'staging', 'preproduction', 'production')]
    [string] $Environment,
    [string] $ServerUrl,
    [string[]] $NotificationAppInstanceId = @(),
    [switch] $Remove,
    [ValidatePattern('^[a-z0-9]+(-[a-z0-9]+)*$')]
    [string] $Revoke,
    [switch] $IncludeReadKeys,
    [string] $SeqCli
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Constants ----------------------------------------------------------------------------------------------------
$script:SeqFolder                = $PSScriptRoot
$script:TemplateFolderName       = 'templates'
$script:TemplateFilter           = '*.template'
$script:AdminKeyVariable         = 'SEQ_ADMIN_KEY'
$script:SeqCliKeyVariable        = 'SEQCLI_CONNECTION_APIKEY'
$script:SeqCliServerVariable     = 'SEQCLI_CONNECTION_SERVERURL'
$script:ApiKeyHeader             = 'X-Seq-ApiKey'
$script:JsonContentType          = 'application/json'
$script:JsonWriteDepth           = 20
$script:RenderFolderPrefix       = 'seq-render-'
$script:ImportStateFileName      = 'import.state'
$script:ExitSuccess              = 0
$script:ExitFailure              = 1

# Template tokens. "{{ref:<file>}}" (quoted) becomes seqcli's ref("<file>"), "{{json:<name>}}" (quoted) becomes raw
# JSON, and "{{<name>}}" becomes text. Templates stay valid JSON until they are rendered.
$script:RefTokenPattern          = '"\{\{ref:([^}"]+)\}\}"'
$script:RefRenderedFormat        = 'ref("{0}")'
$script:RefRenderedPattern       = 'ref\("([^"]+)"\)'
$script:JsonTokenPrefix          = 'json:'
$script:AnyTokenPattern          = '\{\{[^}]*\}\}'
$script:EntityPattern            = '"\$entity"\s*:\s*"([a-z]+)"'
$script:TitlePattern             = '"Title"\s*:\s*"([^"]+)"'
$script:AppInstancePattern       = '^appinstance-[0-9]+$'
$script:TierTitleFormat          = '{0} [{1}] '
$script:IngestKeyTitleFormat     = '{0} ingest {1} [{2}]'
$script:ReadKeyTitleFormat       = '{0} read {1}'
$script:NodeApplicationFormat    = '{0}-{1}'
$script:IngestPermission         = 'Ingest'
$script:AppliedApplication       = 'Application'
$script:AppliedEnvironment       = 'DeploymentEnvironment'
$script:StreamDataSource         = 'Stream'
$script:RetentionAgeFormat       = 'd\.hh\:mm\:ss'
$script:EntityResources          = @{ signal = 'signals'; alert = 'alerts'; sqlquery = 'sqlqueries'; dashboard = 'dashboards' }
$script:SharedEntities           = @('signal', 'sqlquery')
$script:CommonModuleFileName     = 'SeqCommon.psm1'

Import-Module (Join-Path $PSScriptRoot $script:CommonModuleFileName) -Force -Verbose:$false

# ==================================================================================================================
# Rendering (pure)
# ==================================================================================================================
function Get-SeqTokens([string] $Environment, [object] $Thresholds, [string[]] $NotificationAppInstanceId) {
    $channels = @($NotificationAppInstanceId | Where-Object { $_ } | ForEach-Object {
        if ($_ -notmatch $script:AppInstancePattern) { throw "'$_' is not a Seq app instance id (appinstance-<n>)." }
        ConvertTo-Json -InputObject @{ NotificationAppInstanceId = $_ } -Compress
    })
    return @{
        'env'                            = $Environment
        'serverErrorsPerFiveMinutes'     = [string] $Thresholds.serverErrorsPerFiveMinutes
        'gatewayFailuresPerFiveMinutes'  = [string] $Thresholds.gatewayFailuresPerFiveMinutes
        'cspViolationsPerHour'           = [string] $Thresholds.cspViolationsPerHour
        'json:certificateAlertDisabled'  = $(if ($Thresholds.certificateAlertEnabled) { 'false' } else { 'true' })
        'json:notificationChannels'      = '[' + ($channels -join ',') + ']'
    }
}

function ConvertTo-SeqRenderedText([string] $Text, [hashtable] $Tokens, [string] $Name) {
    $rendered = [regex]::Replace($Text, $script:RefTokenPattern, { param($match) $script:RefRenderedFormat -f $match.Groups[1].Value })
    foreach ($token in $Tokens.Keys) {
        if ($token.StartsWith($script:JsonTokenPrefix)) { $rendered = $rendered.Replace('"{{' + $token + '}}"', $Tokens[$token]) }
        else { $rendered = $rendered.Replace('{{' + $token + '}}', $Tokens[$token]) }
    }
    $left = [regex]::Match($rendered, $script:AnyTokenPattern)
    if ($left.Success) { throw "${Name}: unknown token $($left.Value)." }
    return $rendered
}

# Renders every template into $Target (when given) and returns one record per template: File, Entity, Title,
# References. Checks the title prefix, neutral titles and that every ref() names a template that exists.
function Invoke-SeqRender([string] $TemplateFolder, [hashtable] $Tokens, [object] $Config, [string] $Target) {
    $files = @(Get-ChildItem -LiteralPath $TemplateFolder -Filter $script:TemplateFilter | Sort-Object Name)
    $names = @($files | ForEach-Object { $_.Name })
    $records = @()
    foreach ($file in $files) {
        $text = ConvertTo-SeqRenderedText -Text ([IO.File]::ReadAllText($file.FullName)) -Tokens $Tokens -Name $file.Name
        $entity = [regex]::Match($text, $script:EntityPattern).Groups[1].Value
        $title = [regex]::Match($text, $script:TitlePattern).Groups[1].Value
        if (-not $script:EntityResources.ContainsKey($entity)) { throw "$($file.Name): unknown entity '$entity'." }
        if (-not $title.StartsWith([string] $Config.titlePrefix)) { throw "$($file.Name): title '$title' does not start with '$($Config.titlePrefix)'." }
        if (-not (Test-SeqNeutralTitle -Title $title -Config $Config)) { throw "$($file.Name): title '$title' names an application; titles stay neutral." }
        $references = @([regex]::Matches($text, $script:RefRenderedPattern) | ForEach-Object { $_.Groups[1].Value })
        foreach ($reference in $references) { if ($names -notcontains $reference) { throw "$($file.Name): ref(""$reference"") names no template." } }
        if ($Target) { [IO.File]::WriteAllText((Join-Path $Target $file.Name), $text, (New-Object Text.UTF8Encoding($false))) }
        $records += [pscustomobject]@{ File = $file.Name; Entity = $entity; Title = $title; References = [string[]] $references }
    }
    return $records
}

# ==================================================================================================================
# API keys (pure plan and drift)
# ==================================================================================================================
function Get-SeqWantedKeys([object] $Config, [object] $Thresholds, [string] $Environment, [bool] $IncludeReadKeys) {
    $prefix = [string] $Config.titlePrefix
    $keys = @()
    foreach ($application in @($Config.applications)) {
        if ($application -eq $Config.gatewayApplication) { continue }
        $keys += [pscustomobject]@{ Title = ($script:IngestKeyTitleFormat -f $prefix, $application, $Environment); Application = [string] $application; Permissions = @($script:IngestPermission); Applied = $true }
    }
    foreach ($node in @($Thresholds.gatewayNodes | Where-Object { $_ })) {
        $name = $script:NodeApplicationFormat -f $Config.gatewayApplication, $node
        $keys += [pscustomobject]@{ Title = ($script:IngestKeyTitleFormat -f $prefix, $name, $Environment); Application = [string] $Config.gatewayApplication; Permissions = @($script:IngestPermission); Applied = $true }
    }
    if ($IncludeReadKeys) {
        foreach ($entry in @($Config.readKeys)) {
            $keys += [pscustomobject]@{ Title = ($script:ReadKeyTitleFormat -f $prefix, $entry.name); Application = $null; Permissions = @($entry.permissions); Applied = $false }
        }
    }
    return $keys
}

function Get-SeqKeyDrift([object] $Existing, [object] $Want, [string] $Environment, [string] $MinimumLevel) {
    $drift = @()
    $applied = @{}
    foreach ($property in @($Existing.InputSettings.AppliedProperties)) { if ($property) { $applied[$property.Name] = [string] $property.Value } }
    $expected = @{}
    if ($Want.Applied) {
        $expected[$script:AppliedApplication] = $Want.Application
        $expected[$script:AppliedEnvironment] = $Environment
        if ($Existing.InputSettings.MinimumLevel -ne $MinimumLevel) { $drift += "minimum level is '$($Existing.InputSettings.MinimumLevel)', expected '$MinimumLevel'" }
    }
    foreach ($name in (@($applied.Keys) + @($expected.Keys) | Sort-Object -Unique)) {
        if ($applied[$name] -ne $expected[$name]) { $drift += "applied $name is '$($applied[$name])', expected '$($expected[$name])'" }
    }
    $have = (@($Existing.AssignedPermissions) | Sort-Object) -join ','
    $need = (@($Want.Permissions) | Sort-Object) -join ','
    if ($have -ne $need) { $drift += "permissions are '$have', expected '$need'" }
    return [string[]] $drift
}

# ==================================================================================================================
# Seq HTTP API (validated TLS) and seqcli
# ==================================================================================================================
function Invoke-SeqApi([string] $Method, [string] $Path, [object] $Body) {
    $arguments = @{
        Method = $Method; Uri = ($script:ServerUrl.TrimEnd('/') + '/' + $Path.TrimStart('/'))
        Headers = @{ $script:ApiKeyHeader = $script:AdminKey }; ContentType = $script:JsonContentType; UseBasicParsing = $true
    }
    if ($null -ne $Body) { $arguments.Body = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Body -Depth $script:JsonWriteDepth)) }
    # Assigned first: Windows PowerShell 5.1 writes a JSON array as one object; returning the variable enumerates it.
    $result = Invoke-RestMethod @arguments
    return $result
}

function Get-SeqSharedEntities([string] $Resource) { return @(Invoke-SeqApi 'GET' "api/$Resource/?shared=true" $null) }

function Sync-SeqRetentionPolicy {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Config)
    $age = [TimeSpan]::FromDays([int] $Config.retention.logsDays).ToString($script:RetentionAgeFormat)
    $label = "delete events after $($Config.retention.logsDays) days"
    $existing = @(Invoke-SeqApi 'GET' 'api/retentionpolicies/' $null | Where-Object { $_.DataSource -eq $script:StreamDataSource -and $null -eq $_.RemovedSignalExpression }) | Select-Object -First 1
    if ($null -eq $existing) {
        if ($PSCmdlet.ShouldProcess($label, 'Create retention policy')) {
            Invoke-SeqApi 'POST' 'api/retentionpolicies/' @{ RetentionTime = $age; RemovedSignalExpression = $null; DataSource = $script:StreamDataSource } | Out-Null
            Write-Host "  retention  created  $label"
        }
        return
    }
    if ($existing.RetentionTime -eq $age) { Write-Host "  retention  ok       $label"; return }
    if ($PSCmdlet.ShouldProcess($label, "Update retention policy $($existing.Id) from $($existing.RetentionTime)")) {
        $existing.RetentionTime = $age
        Invoke-SeqApi 'PUT' "api/retentionpolicies/$($existing.Id)" $existing | Out-Null
        Write-Host "  retention  updated  $label"
    }
}

function Sync-SeqKeys {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object[]] $Wanted, [string] $Environment, [string] $MinimumLevel)
    $existingKeys = @(Invoke-SeqApi 'GET' 'api/apikeys/' $null)
    $failures = @()
    foreach ($want in $Wanted) {
        $existing = @($existingKeys | Where-Object { $_.Title -eq $want.Title })
        if ($existing.Count -gt 1) { $failures += "$($want.Title): $($existing.Count) keys share this title"; continue }
        if ($existing.Count -eq 1) {
            $drift = @(Get-SeqKeyDrift -Existing $existing[0] -Want $want -Environment $Environment -MinimumLevel $MinimumLevel)
            if ($drift.Count -eq 0) { Write-Host "  key        ok       $($want.Title)" } else { $failures += "$($want.Title) ($($existing[0].Id)): " + ($drift -join '; ') }
            continue
        }
        if (-not $PSCmdlet.ShouldProcess($want.Title, 'Create API key')) { continue }
        $properties = @()
        $level = $null
        if ($want.Applied) {
            $properties = @(@{ Name = $script:AppliedApplication; Value = $want.Application }, @{ Name = $script:AppliedEnvironment; Value = $Environment })
            $level = $MinimumLevel
        }
        $created = Invoke-SeqApi 'POST' 'api/apikeys/' @{
            Title = $want.Title; AssignedPermissions = $want.Permissions
            InputSettings = @{ AppliedProperties = $properties; MinimumLevel = $level; UseServerTimestamps = $false }
        }
        if (-not $created.Token) { throw "Seq created $($created.Id) for '$($want.Title)' but returned no token." }
        # The one time this token is shown. It goes to the console only, never to a file or a log.
        Write-Host "  key        created  $($want.Title) ($($created.Id))  token: $($created.Token)"
    }
    return $failures
}

function Test-SeqApplied([object[]] $Rendered) {
    $missing = @()
    foreach ($group in ($Rendered | Group-Object Entity)) {
        $titles = @(Get-SeqSharedEntities $script:EntityResources[$group.Name] | ForEach-Object { $_.Title })
        foreach ($item in $group.Group) {
            $count = @($titles | Where-Object { $_ -eq $item.Title }).Count
            if ($count -eq 1) { Write-Host "  $($item.Entity.PadRight(10)) ok       $($item.Title)" }
            else { $missing += "$($item.Entity) '$($item.Title)' exists $count times (expected once)" }
        }
    }
    return $missing
}

function Invoke-SeqRemove {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Config, [string] $Environment)
    $prefix = [string] $Config.titlePrefix
    $tierPrefix = $script:TierTitleFormat -f $prefix, $Environment
    foreach ($resource in @('alerts', 'dashboards')) {
        foreach ($entity in @(Get-SeqSharedEntities $resource | Where-Object { $_.Title.StartsWith($tierPrefix) })) {
            if ($PSCmdlet.ShouldProcess($entity.Title, "Delete from $resource")) { Invoke-SeqApi 'DELETE' "api/$resource/$($entity.Id)" $null | Out-Null; Write-Host "  deleted  $($entity.Title)" }
        }
    }
    $otherTiers = @(@(Get-SeqSharedEntities 'alerts') + @(Get-SeqSharedEntities 'dashboards') | Where-Object { $_.Title.StartsWith("$prefix [") })
    if ($otherTiers.Count -gt 0) { Write-Host "  Other tiers still use $prefix entities; the shared signals and saved query are kept."; return }
    foreach ($entity in $script:SharedEntities) {
        $resource = $script:EntityResources[$entity]
        foreach ($item in @(Get-SeqSharedEntities $resource | Where-Object { $_.Title.StartsWith($prefix) })) {
            if ($PSCmdlet.ShouldProcess($item.Title, "Delete from $resource")) { Invoke-SeqApi 'DELETE' "api/$resource/$($item.Id)" $null | Out-Null; Write-Host "  deleted  $($item.Title)" }
        }
    }
    Write-Host '  The retention policy and every API key are kept.'
}

function Invoke-SeqRevoke {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([object] $Config, [string] $Environment, [string] $Name)
    $title = $script:IngestKeyTitleFormat -f $Config.titlePrefix, $Name, $Environment
    $matching = @(Invoke-SeqApi 'GET' 'api/apikeys/' $null | Where-Object { $_.Title -eq $title })
    if ($matching.Count -ne 1) { Write-Host "  FAIL  $($matching.Count) keys are titled '$title' (expected one)."; return $script:ExitFailure }
    if ($PSCmdlet.ShouldProcess($title, "Revoke API key $($matching[0].Id)")) {
        Invoke-SeqApi 'DELETE' "api/apikeys/$($matching[0].Id)" $null | Out-Null
        Write-Host "  key        revoked  $title ($($matching[0].Id))"
    }
    return $script:ExitSuccess
}

# ==================================================================================================================
# Main
# ==================================================================================================================
function Invoke-SeqApply {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([string] $Environment, [string] $ServerUrl, [string[]] $NotificationAppInstanceId, [switch] $Remove, [string] $Revoke, [switch] $IncludeReadKeys, [string] $SeqCli, [string] $RenderFolder)
    if ([string]::IsNullOrWhiteSpace($Environment)) { throw '-Environment is required (development, test, staging, preproduction or production).' }
    if ($Revoke -and $Remove) { throw '-Revoke and -Remove are separate operations; pass one.' }
    $config = Read-SeqConfig
    $thresholds = Read-SeqThresholds -Environment $Environment
    $renderValues = Get-SeqTokens -Environment $Environment -Thresholds $thresholds -NotificationAppInstanceId $NotificationAppInstanceId
    $rendered = @(Invoke-SeqRender -TemplateFolder (Join-Path $script:SeqFolder $script:TemplateFolderName) -Tokens $renderValues -Config $config -Target $RenderFolder)
    $wanted = @(Get-SeqWantedKeys -Config $config -Thresholds $thresholds -Environment $Environment -IncludeReadKeys ([bool] $IncludeReadKeys))
    Write-Host "Rendered $($rendered.Count) templates for '$Environment'."
    if (@($thresholds.gatewayNodes | Where-Object { $_ }).Count -eq 0) { Write-Host "  No gatewayNodes in thresholds.$Environment.json: no gateway ingest key for this tier." }

    if ($WhatIfPreference) {
        if ($Revoke) { Write-Host "  would revoke key  $($script:IngestKeyTitleFormat -f $config.titlePrefix, $Revoke, $Environment)"; return $script:ExitSuccess }
        $rendered | ForEach-Object { Write-Host "  would apply  $($_.Entity.PadRight(9)) $($_.Title)" }
        $wanted | ForEach-Object { Write-Host "  would ensure key  $($_.Title)" }
        Write-Host "  would ensure retention  $($config.retention.logsDays) days"
        if (@($NotificationAppInstanceId | Where-Object { $_ }).Count -eq 0) { Write-Host '  No -NotificationAppInstanceId: alerts would evaluate but send nothing.' }
        return $script:ExitSuccess
    }

    Assert-SeqServerUrl -Url $ServerUrl -Environment $Environment
    $script:ServerUrl = $ServerUrl
    $script:AdminKey = [Environment]::GetEnvironmentVariable($script:AdminKeyVariable)
    if ([string]::IsNullOrWhiteSpace($script:AdminKey)) { throw "No admin key: set the $($script:AdminKeyVariable) environment variable for this session." }
    if ($Revoke) { return (Invoke-SeqRevoke -Config $config -Environment $Environment -Name $Revoke) }
    if ($Remove) { Invoke-SeqRemove -Config $config -Environment $Environment; return $script:ExitSuccess }

    Write-Host "Applying to $ServerUrl."
    # Both through the environment: seqcli ignores the key variable when the server is on its command line.
    [Environment]::SetEnvironmentVariable($script:SeqCliServerVariable, $ServerUrl)
    [Environment]::SetEnvironmentVariable($script:SeqCliKeyVariable, $script:AdminKey)
    & (Resolve-SeqCliPath $SeqCli) template import -i $RenderFolder --state (Join-Path $RenderFolder $script:ImportStateFileName) --merge | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "seqcli template import failed with exit code $LASTEXITCODE." }
    Sync-SeqRetentionPolicy -Config $config
    $problems = @(Sync-SeqKeys -Wanted $wanted -Environment $Environment -MinimumLevel ([string] $config.minimumLevel))
    $problems += @(Test-SeqApplied $rendered)
    if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "  FAIL  $_" }; return $script:ExitFailure }
    Write-Host "OK: $($rendered.Count) entities, retention and keys are in place for '$Environment'."
    return $script:ExitSuccess
}

# Dot-sourced (the Pester tests): define the functions only.
if ($MyInvocation.InvocationName -eq '.') { return }

$renderFolder = Join-Path ([IO.Path]::GetTempPath()) ($script:RenderFolderPrefix + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($renderFolder) | Out-Null
$previousKey = [Environment]::GetEnvironmentVariable($script:SeqCliKeyVariable)
$previousServer = [Environment]::GetEnvironmentVariable($script:SeqCliServerVariable)
try {
    $exitCode = Invoke-SeqApply -Environment $Environment -ServerUrl $ServerUrl -NotificationAppInstanceId $NotificationAppInstanceId -Remove:$Remove -Revoke $Revoke `
        -IncludeReadKeys:$IncludeReadKeys -SeqCli $SeqCli -RenderFolder $renderFolder
}
finally {
    [Environment]::SetEnvironmentVariable($script:SeqCliKeyVariable, $previousKey)
    [Environment]::SetEnvironmentVariable($script:SeqCliServerVariable, $previousServer)
    if (Test-Path -LiteralPath $renderFolder) { [IO.Directory]::Delete($renderFolder, $true) }
}
exit $exitCode
