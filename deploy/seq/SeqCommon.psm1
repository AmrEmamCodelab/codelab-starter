#Requires -Version 5.1
# SeqCommon.psm1: what Install-Seq.ps1 and apply-seq-config.ps1 share (MODULE.md in this folder): reading and checking
# seq-config.json and thresholds.<env>.json, the https rule for the server URL, the neutral-title rule and finding
# seqcli. Windows PowerShell 5.1 compatible; no function here writes anything.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Constants ----------------------------------------------------------------------------------------------------
$script:SeqFolder                = $PSScriptRoot
$script:ConfigFileName           = 'seq-config.json'
$script:ThresholdsFileFormat     = 'thresholds.{0}.json'
$script:HttpsScheme              = 'https'
$script:DevelopmentEnvironment   = 'development'
$script:LoopbackHostNames        = @('localhost', '127.0.0.1', '::1')
$script:NodeNamePattern          = '^[a-z0-9]+(-[a-z0-9]+)*$'
$script:ApplicationNameSeparator = '-'
$script:SeqCliFileName           = 'seqcli.exe'
$script:SeqCliCommandName        = 'seqcli'
$script:SeqCliInstalledRelative  = 'Seq\Client\seqcli.exe'
$script:ThresholdCounts          = @('serverErrorsPerFiveMinutes', 'gatewayFailuresPerFiveMinutes', 'cspViolationsPerHour')

function Read-SeqJsonFile([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "'$Path' is missing." }
    try { return (ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($Path)) -ErrorAction Stop) }
    catch { throw "'$Path' is not valid JSON." }
}

function Get-SeqPositiveInteger([object] $Value, [string] $Name, [string] $Source) {
    if ($null -eq $Value -or -not ($Value -is [int] -or $Value -is [long]) -or $Value -lt 1) { throw "$Source must declare $Name as a positive integer." }
    return [int] $Value
}

function Read-SeqConfig([string] $Folder = $script:SeqFolder) {
    $path = Join-Path $Folder $script:ConfigFileName
    $config = Read-SeqJsonFile $path
    if ([string]::IsNullOrWhiteSpace([string] $config.titlePrefix)) { throw "$path must declare titlePrefix." }
    if (@($config.applications).Count -eq 0) { throw "$path must list applications." }
    Get-SeqPositiveInteger $config.retention.logsDays 'retention.logsDays' $path | Out-Null
    return $config
}

function Read-SeqThresholds([string] $Environment, [string] $Folder = $script:SeqFolder) {
    $path = Join-Path $Folder ($script:ThresholdsFileFormat -f $Environment)
    $thresholds = Read-SeqJsonFile $path
    if ($thresholds.environment -ne $Environment) { throw "$path declares environment '$($thresholds.environment)', not '$Environment'." }
    foreach ($name in $script:ThresholdCounts) { Get-SeqPositiveInteger $thresholds.$name $name $path | Out-Null }
    if (-not ($thresholds.certificateAlertEnabled -is [bool])) { throw "$path must declare certificateAlertEnabled as a JSON boolean." }
    foreach ($node in @($thresholds.gatewayNodes)) {
        if ($node -and $node -notmatch $script:NodeNamePattern) { throw "$path gatewayNodes entry '$node' must be lowercase letters, digits and hyphens." }
    }
    return $thresholds
}

# http is allowed only for a development tier on this machine; every other tier needs https (validated TLS).
function Assert-SeqServerUrl([string] $Url, [string] $Environment) {
    $uri = $null
    if ([string]::IsNullOrWhiteSpace($Url) -or -not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref] $uri)) { throw "-ServerUrl '$Url' must be an absolute URL (https://<seq host>)." }
    if ([string]::Equals($uri.Scheme, $script:HttpsScheme, [StringComparison]::OrdinalIgnoreCase)) { return }
    $loopback = $script:LoopbackHostNames -contains $uri.Host.Trim('[', ']').ToLowerInvariant()
    if ($Environment -eq $script:DevelopmentEnvironment -and $loopback) { return }
    throw "-ServerUrl '$Url' must be https: the admin key and the printed tokens never cross plain http (only a development tier on localhost may)."
}

# Neutral titles: no application or product name, so the Seq UI does not reveal what it watches.
function Get-SeqForbiddenTitleWords([object] $Config) {
    $words = New-Object 'System.Collections.Generic.List[string]'
    foreach ($application in @($Config.applications)) {
        $words.Add([string] $application)
        $words.Add(([string] $application).Split($script:ApplicationNameSeparator)[0])
    }
    return [string[]] @($words | Sort-Object -Unique)
}

function Test-SeqNeutralTitle([string] $Title, [object] $Config) {
    foreach ($word in Get-SeqForbiddenTitleWords $Config) {
        if ($Title.IndexOf($word, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $false }
    }
    return $true
}

function Resolve-SeqCliPath([string] $Requested) {
    if ($Requested) { return $Requested }
    $onPath = Get-Command $script:SeqCliCommandName -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $installed = Join-Path $env:ProgramFiles $script:SeqCliInstalledRelative
    if (Test-Path -LiteralPath $installed) { return $installed }
    throw "$($script:SeqCliFileName) was not found: pass -SeqCli <path>."
}

Export-ModuleMember -Function Read-SeqJsonFile, Get-SeqPositiveInteger, Read-SeqConfig, Read-SeqThresholds, Assert-SeqServerUrl, Get-SeqForbiddenTitleWords, Test-SeqNeutralTitle, Resolve-SeqCliPath
