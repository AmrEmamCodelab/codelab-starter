#Requires -Version 5.1
<#
.SYNOPSIS
    Installs or updates the Seq log server on a Windows server, with a neutral instance title.

.DESCRIPTION
    One shared Seq serves every tier; events carry Application and DeploymentEnvironment (applied by the ingest keys
    apply-seq-config.ps1 creates). The steps, in order (MODULE.md in this folder has the details):

      1. Checks the parameters (refuses before anything changes): -ListenUrl is https with -CertificateThumbprint
         (plain http only for -Environment development on localhost), -StoragePath is fully qualified and the title
         is neutral (it names no application of seq-config.json).
      2. Plans from the server's facts (read-only): InstallPackage (the Seq MSI, offline, from -MsiPath),
         CreateService (seq.exe install with the storage folder and listen URL), EnableAuthentication (seq.exe config
         firstRun.adminPasswordHash, from SEQ_FIRSTRUN_ADMINPASSWORDHASH, so a new instance never starts open),
         BindCertificate (seq.exe bind-ssl, when the port's http.sys certificate differs), StartService,
         SetInstanceTitle (seqcli, needs the admin key in SEQ_ADMIN_KEY; without it the title is an operator step).
         A new instance outside development is REFUSED without SEQ_FIRSTRUN_ADMINPASSWORDHASH: the log server holds
         every tier's events, and the first admin must not be claimable by whoever reaches the port first.
      3. Applies each step (skipped under -WhatIf), then waits for GET <ListenUrl>/api to answer 200 over validated
         TLS (no certificate check is ever skipped). Outside development it then fails the run when an anonymous
         read (GET <ListenUrl>/api/events) is answered 200: authentication is off.

    The first admin password is chosen by an operator; only its hash reaches this script: docs/runbooks/seq.md.
    Exit codes (when run as a script): 0 done (or previewed), 1 refused or failed.

.EXAMPLE
    .\Install-Seq.ps1 -Environment production -MsiPath D:\media\seq.msi -ListenUrl https://logs.example.test -CertificateThumbprint <thumbprint> -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('development', 'test', 'staging', 'preproduction', 'production')]
    [string] $Environment,
    [string] $MsiPath,
    [string] $StoragePath,
    [string] $ListenUrl,
    [string] $CertificateThumbprint,
    [string] $InstanceTitle,
    [string] $SeqExePath,
    [string] $SeqCli
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Enums (integers with explicit values) ------------------------------------------------------------------------
enum SeqInstallAction {
    None = 0
    InstallPackage = 1
    CreateService = 2
    BindCertificate = 3
    StartService = 4
    SetInstanceTitle = 5
    EnableAuthentication = 6
}

# --- Constants ----------------------------------------------------------------------------------------------------
$script:CommonModuleFileName     = 'SeqCommon.psm1'
$script:DefaultStorageRelative   = 'Seq'
$script:DefaultSeqExeRelative    = 'Seq\seq.exe'
$script:ServiceName              = 'Seq'
$script:RunningStatus            = 'Running'
$script:AdminKeyVariable         = 'SEQ_ADMIN_KEY'
# The first admin's password hash (seq.exe config hash): read from the environment, never logged or printed.
$script:FirstRunHashVariable     = 'SEQ_FIRSTRUN_ADMINPASSWORDHASH'
$script:FirstRunHashSetting      = 'firstRun.adminPasswordHash'
$script:DevelopmentEnvironment   = 'development'
$script:SeqCliKeyVariable        = 'SEQCLI_CONNECTION_APIKEY'
$script:SeqCliServerVariable     = 'SEQCLI_CONNECTION_SERVERURL'
$script:InstanceTitleSetting     = 'instance-title'
$script:MsiExec                  = 'msiexec.exe'
$script:MsiSuccessCodes          = @(0, 3010)
$script:MsiLogSuffix             = '.install.log'
$script:HttpsScheme              = 'https'
$script:HttpsDefaultPort         = 443
$script:ThumbprintPattern        = '^[0-9A-F]{40}$'
$script:NonHexPattern            = '[^0-9A-Fa-f]'
$script:SslCertHashPattern       = 'Certificate Hash\s*:\s*([0-9A-Fa-f]+)'
$script:SslCertAddressFormat     = 'ipport=0.0.0.0:{0}'
$script:DriveRootedPattern       = '^[A-Za-z]:\\'
$script:HealthPath               = '/api'
$script:AnonymousReadPath        = '/api/events?count=1'
$script:HealthAttempts           = 24
$script:HealthDelaySeconds       = 5
$script:HealthTimeoutSeconds     = 10
$script:HttpOk                   = 200
$script:NoHttpStatus             = 0
$script:UrlSeparator             = '/'

Import-Module (Join-Path $PSScriptRoot $script:CommonModuleFileName) -Force -Verbose:$false

# ==================================================================================================================
# Parameters (pure checks; they throw before anything changes)
# ==================================================================================================================
function ConvertTo-SeqThumbprint([string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    return ($Value -replace $script:NonHexPattern, '').ToUpperInvariant()
}

function Get-SeqDefaultInstanceTitle([object] $Config, [string] $Environment) {
    return ([string] $Config.instanceTitleFormat) -f $Environment
}

function Test-SeqHttpsUrl([string] $Url) {
    $uri = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref] $uri)) { return $false }
    return [string]::Equals($uri.Scheme, $script:HttpsScheme, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-SeqInstallParameters {
    param([string] $Environment, [string] $ListenUrl, [string] $CertificateThumbprint, [string] $StoragePath, [string] $InstanceTitle, [object] $Config)
    if ([string]::IsNullOrWhiteSpace($Environment)) { throw '-Environment is required (development, test, staging, preproduction or production). Nothing was changed.' }
    try { Assert-SeqServerUrl -Url $ListenUrl -Environment $Environment }
    catch { throw "-ListenUrl: $($_.Exception.Message.Replace('-ServerUrl ', '')) Nothing was changed." }
    if ((Test-SeqHttpsUrl $ListenUrl) -and (ConvertTo-SeqThumbprint $CertificateThumbprint) -notmatch $script:ThumbprintPattern) {
        throw '-CertificateThumbprint is required for an https -ListenUrl: the 40-hex thumbprint of a LocalMachine\My certificate. Nothing was changed.'
    }
    if ($StoragePath -notmatch $script:DriveRootedPattern) { throw "-StoragePath '$StoragePath' must be a fully qualified local folder. Nothing was changed." }
    if ([string]::IsNullOrWhiteSpace($InstanceTitle)) { throw '-InstanceTitle must not be empty. Nothing was changed.' }
    if (-not (Test-SeqNeutralTitle -Title $InstanceTitle -Config $Config)) {
        throw "-InstanceTitle '$InstanceTitle' names an application; keep it neutral (for example '$(Get-SeqDefaultInstanceTitle $Config $Environment)'). Nothing was changed."
    }
}

# ==================================================================================================================
# Facts (read-only), plan (pure), apply
# ==================================================================================================================
function Get-SeqPort([string] $ListenUrl) {
    return ([Uri] $ListenUrl).Port
}

function Get-SeqBoundThumbprint([int] $Port) {
    $output = & netsh.exe http show sslcert ($script:SslCertAddressFormat -f $Port) 2>$null | Out-String
    $match = [regex]::Match($output, $script:SslCertHashPattern)
    if ($match.Success) { return $match.Groups[1].Value.ToUpperInvariant() }
    return ''
}

function Get-SeqInstallFacts([string] $SeqExePath, [string] $ListenUrl) {
    $service = Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue
    $https = Test-SeqHttpsUrl $ListenUrl
    return [pscustomobject]@{
        Installed = (Test-Path -LiteralPath $SeqExePath -PathType Leaf)
        ServiceExists = ($null -ne $service)
        ServiceRunning = ($null -ne $service -and [string] $service.Status -eq $script:RunningStatus)
        BoundThumbprint = $(if ($https) { Get-SeqBoundThumbprint (Get-SeqPort $ListenUrl) } else { '' })
        AdminKeyPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($script:AdminKeyVariable))
        FirstRunHashPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($script:FirstRunHashVariable))
    }
}

function New-SeqInstallStep([SeqInstallAction] $Action, [string] $Detail) {
    return [pscustomobject]@{ Action = $Action.ToString(); ActionId = [int] $Action; Detail = $Detail; Applied = $false }
}

<#
    Pure: the same facts always give the same plan. Returns Steps (SeqInstallAction), Refusal (text or $null) and
    Notes (operator steps the kit cannot do).
#>
# Every tier but development (localhost, http) runs Seq with authentication on.
function Test-SeqAuthenticationRequired([string] $Environment) {
    return -not [string]::Equals($Environment, $script:DevelopmentEnvironment, [StringComparison]::OrdinalIgnoreCase)
}

function Get-SeqInstallPlan([object] $Facts, [string] $Environment, [string] $MsiPath, [string] $ListenUrl, [string] $CertificateThumbprint, [string] $StoragePath, [string] $InstanceTitle) {
    $plan = [pscustomobject]@{ Steps = @(); Refusal = $null; Notes = @() }
    $steps = New-Object 'System.Collections.Generic.List[object]'
    if (-not $Facts.Installed) {
        if ([string]::IsNullOrWhiteSpace($MsiPath)) { $plan.Refusal = 'Seq is not installed: give -MsiPath (the Seq MSI copied to this server; the kit never downloads). Nothing was changed.'; return $plan }
        $steps.Add((New-SeqInstallStep InstallPackage "install '$MsiPath' silently"))
    }
    if (-not $Facts.ServiceExists) {
        if ((Test-SeqAuthenticationRequired $Environment) -and -not $Facts.FirstRunHashPresent) {
            $plan.Refusal = "A new Seq instance for '$Environment' must start with authentication on: set $($script:FirstRunHashVariable) for this session to the hash of the first admin password (seq.exe config hash; docs/runbooks/seq.md), then run again. Nothing was changed."
            return $plan
        }
        $steps.Add((New-SeqInstallStep CreateService "create the Windows service: storage '$StoragePath', listen '$ListenUrl'"))
        if ($Facts.FirstRunHashPresent) { $steps.Add((New-SeqInstallStep EnableAuthentication "turn authentication on with the first admin password hash from $($script:FirstRunHashVariable)")) }
    }
    $thumbprint = ConvertTo-SeqThumbprint $CertificateThumbprint
    if ((Test-SeqHttpsUrl $ListenUrl) -and $Facts.BoundThumbprint -ne $thumbprint) {
        $steps.Add((New-SeqInstallStep BindCertificate "bind certificate $thumbprint to port $(Get-SeqPort $ListenUrl)"))
    }
    if (-not $Facts.ServiceRunning -or $steps.Count -gt 0) { $steps.Add((New-SeqInstallStep StartService "start (or restart) the '$($script:ServiceName)' service")) }
    if ($Facts.AdminKeyPresent) { $steps.Add((New-SeqInstallStep SetInstanceTitle "set the instance title to '$InstanceTitle'")) }
    else { $plan.Notes += "Set the instance title to '$InstanceTitle': sign in once, create an admin API key, set $($script:AdminKeyVariable) and run this script again." }
    if ($steps.Count -eq 0) { $steps.Add((New-SeqInstallStep None 'Seq is installed, bound and running')) }
    $plan.Steps = [object[]] $steps.ToArray()
    return $plan
}

function Invoke-SeqNative([string] $FilePath, [string[]] $Arguments) {
    & $FilePath @Arguments | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path -Leaf $FilePath) $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

function Invoke-SeqInstallAction([object] $Step, [hashtable] $Desired) {
    switch ([SeqInstallAction] $Step.ActionId) {
        ([SeqInstallAction]::InstallPackage) {
            $log = Join-Path ([IO.Path]::GetTempPath()) ((Split-Path -Leaf $Desired.MsiPath) + $script:MsiLogSuffix)
            $process = Start-Process -FilePath $script:MsiExec -ArgumentList @('/i', "`"$($Desired.MsiPath)`"", '/qn', '/norestart', '/l*v', "`"$log`"") -Wait -PassThru
            if ($script:MsiSuccessCodes -notcontains $process.ExitCode) { throw "The Seq MSI failed with exit code $($process.ExitCode); read '$log'." }
        }
        ([SeqInstallAction]::CreateService) { Invoke-SeqNative $Desired.SeqExePath @('install', "--storage=$($Desired.StoragePath)", "--listen=$($Desired.ListenUrl)") }
        ([SeqInstallAction]::EnableAuthentication) {
            # The hash, never the password; on failure Invoke-SeqNative names only the sub-command.
            $hash = [Environment]::GetEnvironmentVariable($script:FirstRunHashVariable)
            Invoke-SeqNative $Desired.SeqExePath @('config', "--storage=$($Desired.StoragePath)", '-k', $script:FirstRunHashSetting, '-v', $hash)
        }
        ([SeqInstallAction]::BindCertificate) { Invoke-SeqNative $Desired.SeqExePath @('bind-ssl', "--thumbprint=$($Desired.Thumbprint)", "--port=$($Desired.Port)") }
        ([SeqInstallAction]::StartService) {
            $service = Get-Service -Name $script:ServiceName
            if ([string] $service.Status -eq $script:RunningStatus) { Restart-Service -Name $script:ServiceName } else { Start-Service -Name $script:ServiceName }
        }
        ([SeqInstallAction]::SetInstanceTitle) {
            $previousKey = [Environment]::GetEnvironmentVariable($script:SeqCliKeyVariable)
            $previousServer = [Environment]::GetEnvironmentVariable($script:SeqCliServerVariable)
            try {
                # Through the environment only: the admin key never appears on a command line.
                [Environment]::SetEnvironmentVariable($script:SeqCliServerVariable, $Desired.ListenUrl)
                [Environment]::SetEnvironmentVariable($script:SeqCliKeyVariable, [Environment]::GetEnvironmentVariable($script:AdminKeyVariable))
                Invoke-SeqNative (Resolve-SeqCliPath $Desired.SeqCli) @('setting', 'set', '-n', $script:InstanceTitleSetting, '-v', $Desired.InstanceTitle)
            }
            finally {
                [Environment]::SetEnvironmentVariable($script:SeqCliKeyVariable, $previousKey)
                [Environment]::SetEnvironmentVariable($script:SeqCliServerVariable, $previousServer)
            }
        }
    }
}

# One GET with the platform's certificate validation in force (a permissive callback is removed for the call).
function Invoke-SeqHealthRequest([string] $Url) {
    $previousCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
    $previousProtocols = [Net.ServicePointManager]::SecurityProtocol
    [Net.ServicePointManager]::ServerCertificateValidationCallback = $null
    [Net.ServicePointManager]::SecurityProtocol = $previousProtocols -bor [Net.SecurityProtocolType]::Tls12
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $script:HealthTimeoutSeconds -MaximumRedirection 0
        return [int] $response.StatusCode
    }
    catch [System.Net.WebException] {
        if ($null -eq $_.Exception.Response) { return $script:NoHttpStatus }
        return [int] $_.Exception.Response.StatusCode
    }
    finally {
        [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback
        [Net.ServicePointManager]::SecurityProtocol = $previousProtocols
    }
}

function Wait-SeqHealth([string] $Url) {
    $status = $script:NoHttpStatus
    for ($attempt = 1; $attempt -le $script:HealthAttempts; $attempt++) {
        $status = Invoke-SeqHealthRequest $Url
        if ($status -eq $script:HttpOk) { return $status }
        if ($attempt -lt $script:HealthAttempts) { Start-Sleep -Seconds $script:HealthDelaySeconds }
    }
    return $status
}

# ==================================================================================================================
# The install
# ==================================================================================================================
function Invoke-SeqInstall {
    <#
    .SYNOPSIS
        Runs the Seq install (see the script help). Returns Preview, Plan (Steps, Refusal, Notes), HealthUrl,
        HealthStatusCode and InstanceTitle.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string] $Environment,
        [string] $MsiPath,
        [string] $StoragePath = (Join-Path $env:ProgramData $script:DefaultStorageRelative),
        [string] $ListenUrl,
        [string] $CertificateThumbprint,
        [string] $InstanceTitle,
        [string] $SeqExePath = (Join-Path $env:ProgramFiles $script:DefaultSeqExeRelative),
        [string] $SeqCli
    )
    $config = Read-SeqConfig
    if ([string]::IsNullOrWhiteSpace($InstanceTitle) -and -not [string]::IsNullOrWhiteSpace($Environment)) { $InstanceTitle = Get-SeqDefaultInstanceTitle $config $Environment }
    Assert-SeqInstallParameters -Environment $Environment -ListenUrl $ListenUrl -CertificateThumbprint $CertificateThumbprint -StoragePath $StoragePath -InstanceTitle $InstanceTitle -Config $config
    if ($MsiPath -and -not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) { throw "-MsiPath '$MsiPath' does not exist. Nothing was changed." }

    $baseUrl = $ListenUrl.TrimEnd($script:UrlSeparator)
    $result = [pscustomobject]@{
        Preview = [bool] $WhatIfPreference; Plan = $null; HealthUrl = ($baseUrl + $script:HealthPath); HealthStatusCode = $null; InstanceTitle = $InstanceTitle
        AnonymousReadUrl = ($baseUrl + $script:AnonymousReadPath); AnonymousReadStatusCode = $null
    }
    $plan = Get-SeqInstallPlan -Facts (Get-SeqInstallFacts -SeqExePath $SeqExePath -ListenUrl $ListenUrl) -Environment $Environment -MsiPath $MsiPath -ListenUrl $ListenUrl `
        -CertificateThumbprint $CertificateThumbprint -StoragePath $StoragePath -InstanceTitle $InstanceTitle
    $result.Plan = $plan
    if ($plan.Refusal) { throw $plan.Refusal }
    $desired = @{
        MsiPath = $MsiPath; SeqExePath = $SeqExePath; StoragePath = $StoragePath; ListenUrl = $ListenUrl; Port = (Get-SeqPort $ListenUrl)
        Thumbprint = (ConvertTo-SeqThumbprint $CertificateThumbprint); InstanceTitle = $InstanceTitle; SeqCli = $SeqCli
    }
    foreach ($step in $plan.Steps) {
        Write-Host "  $($step.Action.PadRight(17)) $($step.Detail)"
        if ($step.ActionId -eq [int] [SeqInstallAction]::None) { continue }
        if ($PSCmdlet.ShouldProcess($script:ServiceName, $step.Detail)) { Invoke-SeqInstallAction -Step $step -Desired $desired; $step.Applied = $true }
    }
    foreach ($note in $plan.Notes) { Write-Host "  operator   $note" }
    $authenticationRequired = Test-SeqAuthenticationRequired $Environment
    if ($result.Preview) {
        Write-Host "  would wait for GET $($result.HealthUrl) to answer 200 over validated TLS"
        if ($authenticationRequired) { Write-Host "  would check that an anonymous GET $($result.AnonymousReadUrl) is refused (authentication on)" }
        return $result
    }
    $result.HealthStatusCode = Wait-SeqHealth $result.HealthUrl
    if ($result.HealthStatusCode -ne $script:HttpOk) {
        throw "Seq is installed but $($result.HealthUrl) answered $($result.HealthStatusCode) (0 = no answer: check the binding, the certificate chain and the service log under '$StoragePath')."
    }
    if ($authenticationRequired) {
        $result.AnonymousReadStatusCode = Invoke-SeqHealthRequest $result.AnonymousReadUrl
        if ($result.AnonymousReadStatusCode -eq $script:HttpOk) {
            throw "Seq answers an anonymous read ($($result.AnonymousReadUrl) answered 200): authentication is off, so anyone who reaches the port can read every event. Sign in as the admin and turn authentication on (docs/runbooks/seq.md), then run again."
        }
    }
    Write-Host "OK: $($result.HealthUrl) answers 200. Next: docs/runbooks/seq.md (admin API key, then apply-seq-config.ps1)."
    return $result
}

# Dot-sourced (the Pester tests): define the functions only.
if ($MyInvocation.InvocationName -eq '.') { return }

Invoke-SeqInstall @PSBoundParameters | Out-Null
