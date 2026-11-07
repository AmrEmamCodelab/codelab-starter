# Pester 3.4 tests for deploy/kits/Install-Gateway.ps1 (Windows PowerShell 5.1).
# No IIS change, no admin rights, no network, no certificate store: IIS state, prerequisites, the TLS facts and
# binding changes and HTTP are mocked, and every folder the kit touches is under $TestDrive.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Gateway.Tests.ps1 -EnableExit"
Set-StrictMode -Version Latest

$KitFolder = Split-Path -Parent $PSScriptRoot
$KitScript = Join-Path $KitFolder 'Install-Gateway.ps1'
$TemplatePath = Join-Path $KitFolder 'templates\gateway\appsettings.template.json'
$RequirementsPath = Join-Path $KitFolder 'templates\gateway\required-settings.json'
$EntryAssembly = 'Starter.Gateway.Fixture.dll'
$TestSiteName = 'Kit Test Gateway Site'
$TestPoolName = 'KitTestGatewayPool'
$TestHost = 'gateway.example.test'
$GoodThumbprint = 'AA11BB22CC33DD44EE55FF6600112233445566AA'
$OtherThumbprint = 'BB11BB22CC33DD44EE55FF6600112233445566BB'
$HealthUrl = 'https://gateway.example.test/health/ready'

# The kit declares its own parameters (PackagePath, SiteName, ...) here; test variables use other names.
. $KitScript

function New-TestFolder([string] $Name) {
    $path = Join-Path $TestDrive ('{0}-{1}' -f $Name, [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

function Write-TestText([string] $Path, [string] $Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

# A fake publish output: web.config naming the entry DLL (placeholder bytes) and the package appsettings.json.
function New-FakeGatewayPackage([switch] $InAppFolder, [string] $WebConfig) {
    $root = New-TestFolder 'package'
    $app = if ($InAppFolder) { Join-Path $root 'app' } else { $root }
    if (-not $WebConfig) {
        $WebConfig = '<?xml version="1.0" encoding="utf-8"?><configuration><location path="." inheritInChildApplications="false"><system.webServer><aspNetCore processPath="dotnet" arguments=".\{0}" hostingModel="inprocess" /></system.webServer></location></configuration>' -f $EntryAssembly
    }
    Write-TestText (Join-Path $app 'web.config') $WebConfig
    [IO.File]::WriteAllBytes((Join-Path $app $EntryAssembly), [byte[]] (77, 90))
    Write-TestText (Join-Path $app 'appsettings.json') '{ "Logging": { "LogLevel": { "Default": "Information" } }, "AllowedHosts": "*" }'
    return $root
}

# A complete server Production file (placeholder values only; never key material).
function Write-CompleteProduction([string] $TargetPath) {
    $production = @'
{
  "Codelab": {
    "Integration": {
      "Gateway": {
        "Profiles": [ "sso" ]
      }
    },
    "Security": {
      "ServiceAssertions": {
        "Server": {
          "Audience": "integration-gateway",
          "TrustedServices": [ { "ServiceName": "starter-api", "KeyId": "fixture-s2s-key", "PublicKeyPem": "fixture-not-key-material" } ]
        }
      }
    }
  },
  "Gateway": {
    "Sso": { "TokenEndpoint": "https://idp.example.test/token", "JwksUri": "https://idp.example.test/jwks" },
    "Sms": { "UrlTemplate": "", "BodyTemplate": "", "AuthHeaderName": "", "AuthHeaderValue": "", "Sender": "" }
  }
}
'@
    Write-TestText (Join-Path $TargetPath 'appsettings.Production.json') $production
}

function Get-FolderSnapshot([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '<missing>' }
    $lines = Get-ChildItem -LiteralPath $Path -Recurse -Force | Sort-Object FullName | ForEach-Object {
        if ($_.PSIsContainer) { $_.FullName } else { '{0} {1}' -f $_.FullName, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    return (@($lines) -join "`n")
}

function Read-TestJson([string] $Path) { return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json) }

function New-CertificateFacts([string] $Thumbprint, [switch] $NoPrivateKey, [switch] $Expired, [switch] $NotYetValid, [switch] $NoServerAuthentication, [switch] $Untrusted) {
    $now = Get-Date
    return [pscustomobject]@{
        Thumbprint = $Thumbprint; Exists = $true; HasPrivateKey = (-not $NoPrivateKey)
        NotBefore = $(if ($NotYetValid) { $now.AddDays(1) } else { $now.AddDays(-30) })
        NotAfter = $(if ($Expired) { $now.AddDays(-1) } else { $now.AddDays(300) })
        HasServerAuthentication = (-not $NoServerAuthentication); ChainTrusted = (-not $Untrusted)
    }
}

function New-TlsFacts([object[]] $Bindings, [object[]] $Certificates) {
    $map = @{}
    foreach ($certificate in @($Certificates)) { if ($certificate) { $map[$certificate.Thumbprint] = $certificate } }
    return [pscustomobject]@{ Known = $true; Bindings = @($Bindings | Where-Object { $_ }); Certificates = $map }
}

function New-Binding([string] $Thumbprint, [int] $Port = 443, [string] $HostName = $TestHost) {
    return [pscustomobject]@{ Port = $Port; HostName = $HostName; Thumbprint = $Thumbprint }
}

# IIS state (read-only; the apply step is mocked). The site serves $global:GatewayTestSitePath.
$IisStateWithSite = {
    [pscustomobject]@{
        Known = $true; PhysicalPathExists = $false; Pool = $null
        Site = [pscustomobject]@{ PhysicalPath = $global:GatewayTestSitePath; ApplicationPool = 'SitePool' }; Application = $null
    }
}
$PrerequisitesPassed = { [pscustomobject]@{ Passed = $true; Checks = @() } }
$TlsFactsFromGlobal = { $global:GatewayTestTlsFacts }

Describe 'Install-Gateway parameters and package' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-GatewayTlsFacts $TlsFactsFromGlobal
    Mock Invoke-GatewayTlsAction { }

    It 'refuses a missing -PackagePath' {
        { Invoke-GatewayKit -SiteName $TestSiteName -HostName $TestHost -WhatIf } | Should Throw '-PackagePath is required'
    }

    It 'refuses a missing -SiteName' {
        $package = New-FakeGatewayPackage
        { Invoke-GatewayKit -PackagePath $package -HostName $TestHost -WhatIf } | Should Throw '-SiteName is required'
    }

    It 'refuses an app pool name with invalid characters' {
        $package = New-FakeGatewayPackage
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -AppPoolName 'pool/x' -HostName $TestHost -WhatIf } | Should Throw 'is not a valid app pool name'
    }

    It 'refuses a relative -TargetPath and a -BackupRoot inside -TargetPath' {
        $package = New-FakeGatewayPackage
        $target = Join-Path $TestDrive 'target-backup'
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -TargetPath 'site' -HostName $TestHost -WhatIf } | Should Throw 'must be a fully qualified folder'
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot (Join-Path $target 'b') -HostName $TestHost -WhatIf } |
            Should Throw 'the payload mirror would delete the backups'
    }

    It 'refuses a plain http -HealthBaseUrl (the probe validates TLS, there is no -k)' {
        $package = New-FakeGatewayPackage
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -HealthBaseUrl 'http://gateway.example.test' -WhatIf } | Should Throw 'must be an absolute https URL'
    }

    It 'refuses a port outside 1-65535' {
        $package = New-FakeGatewayPackage
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -HostName $TestHost -HttpsPort 70000 -WhatIf } | Should Throw 'is not a port'
    }

    It 'refuses a package folder that does not exist, a web.config without aspNetCore and a missing entry assembly' {
        { Invoke-GatewayKit -PackagePath (Join-Path $TestDrive 'no-such-package') -SiteName $TestSiteName -HostName $TestHost -WhatIf } | Should Throw 'does not exist'
        $noAncm = New-FakeGatewayPackage -WebConfig '<configuration><system.webServer /></configuration>'
        { Invoke-GatewayKit -PackagePath $noAncm -SiteName $TestSiteName -HostName $TestHost -WhatIf } | Should Throw 'has no aspNetCore element'
        $package = New-FakeGatewayPackage
        Remove-Item -LiteralPath (Join-Path $package $EntryAssembly)
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -HostName $TestHost -WhatIf } | Should Throw "has no $EntryAssembly"
    }

    It 'reads the entry file from web.config (dotnet + arguments, or an apphost exe)' {
        $dll = Join-Path (New-TestFolder 'wc') 'web.config'
        Write-TestText $dll '<configuration><system.webServer><aspNetCore processPath="dotnet" arguments=".\Some.Host.dll --urls x" /></system.webServer></configuration>'
        Get-GatewayEntryFileName $dll | Should Be 'Some.Host.dll'
        $exe = Join-Path (New-TestFolder 'wc') 'web.config'
        Write-TestText $exe '<configuration><system.webServer><aspNetCore processPath=".\Some.Host.exe" arguments="" /></system.webServer></configuration>'
        Get-GatewayEntryFileName $exe | Should Be 'Some.Host.exe'
    }

    It 'refuses an unusable -CertificateThumbprint before anything changes' {
        $global:GatewayTestTlsFacts = New-TlsFacts -Bindings @() -Certificates @((New-CertificateFacts $GoodThumbprint -NoPrivateKey))
        $package = New-FakeGatewayPackage
        $server = New-TestFolder 'srv'
        $global:GatewayTestSitePath = Join-Path $server 'app'
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') -HostName $TestHost -CertificateThumbprint $GoodThumbprint } |
            Should Throw 'NoPrivateKey'
        Test-Path -LiteralPath (Join-Path $server 'app') | Should Be $false
        Test-Path -LiteralPath (Join-Path $server 'backups') | Should Be $false
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
    }

    It 'refuses a root install that would repoint a site serving another folder, unless -AdoptSite' {
        $global:GatewayTestTlsFacts = New-TlsFacts -Bindings @((New-Binding $GoodThumbprint)) -Certificates @((New-CertificateFacts $GoodThumbprint))
        $global:GatewayTestSitePath = 'C:\some-other-site'
        $package = New-FakeGatewayPackage
        $server = New-TestFolder 'srv'
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') -HostName $TestHost -WhatIf } |
            Should Throw 'would repoint the site'
        $result = Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') -HostName $TestHost -AdoptSite -WhatIf
        ($result.PlannedActions -join "`n") | Should Match 'IIS UpdateSite'
    }

    It 'names the missing site plainly' {
        Mock -ModuleName Install-Common Get-KitIisState { [pscustomobject]@{ Known = $true; PhysicalPathExists = $false; Pool = $null; Site = $null; Application = $null } }
        $global:GatewayTestTlsFacts = New-TlsFacts -Bindings @() -Certificates @()
        $package = New-FakeGatewayPackage
        $server = New-TestFolder 'srv'
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') -HostName $TestHost -WhatIf } |
            Should Throw 'create the gateway site on the DMZ host first'
    }

    It 'exits non-zero with the reason when the script runs without -PackagePath' {
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $KitScript -SiteName $TestSiteName -HostName $TestHost 2>&1 | Out-String
        $LASTEXITCODE | Should Not Be 0
        $output | Should Match 'PackagePath is required'
    }
}

Describe 'Install-Gateway required settings' {
    $requirements = Read-GatewayRequirements $RequirementsPath
    $template = Read-TestJson $TemplatePath

    It 'lists the operator keys of the template as gaps, and not the upstream of the sms profile it leaves off' {
        $gaps = @(Get-GatewaySettingsGaps -Effective (Get-GatewayEffectiveSettings @($template)) -Requirements $requirements)
        ($gaps -join ',') | Should Be 'Codelab:Security:ServiceAssertions:Server:TrustedServices:0:KeyId,Codelab:Security:ServiceAssertions:Server:TrustedServices:0:PublicKeyPem,Gateway:Sso:JwksUri,Gateway:Sso:TokenEndpoint'
    }

    It 'ships the profiles as the list of keys the host reads, and the audience the API signs for' {
        @($template.Codelab.Integration.Gateway.Profiles) -join ',' | Should BeExactly 'sso'
        $template.Codelab.Integration.Gateway.Profiles[0] -is [string] | Should Be $true
        # GatewayRelayLimits.Audience (Codelab.Integration) and the API's Codelab:Integration:Gateway:Audience.
        $template.Codelab.Security.ServiceAssertions.Server.Audience | Should BeExactly 'integration-gateway'
    }

    It 'requires the sms upstream once the profile is listed, and https URLs for the sso upstream' {
        $server = '{ "Codelab": { "Integration": { "Gateway": { "Profiles": [ "sso", "sms" ] } } }, "Gateway": { "Sso": { "TokenEndpoint": "http://idp.example.test/token" }, "Sms": { "Method": 2 } } }' | ConvertFrom-Json
        $gaps = @(Get-GatewaySettingsGaps -Effective (Get-GatewayEffectiveSettings @($template, $server)) -Requirements $requirements) -join ','
        $gaps | Should Match ([regex]::Escape('Gateway:Sso:TokenEndpoint (must be an absolute https URL)'))
        $gaps | Should Match ([regex]::Escape('Gateway:Sms:UrlTemplate'))
        $gaps | Should Match ([regex]::Escape('Gateway:Sms:BodyTemplate'))
    }

    It 'accepts an sms URL template with placeholders, and needs no body for a GET provider' {
        $server = '{ "Codelab": { "Integration": { "Gateway": { "Profiles": [ "sms" ] } } }, "Gateway": { "Sms": { "Method": 1, "UrlTemplate": "https://sms.example.test/send?to={to}&text={text}&from={sender}" } } }' | ConvertFrom-Json
        $gaps = @(Get-GatewaySettingsGaps -Effective (Get-GatewayEffectiveSettings @($template, $server)) -Requirements $requirements) -join ','
        $gaps | Should Not Match 'Gateway:Sms'
        $gaps | Should Not Match 'Gateway:Sso'
    }

    It 'matches profile keys exactly, like the host (SSO is not sso)' {
        $server = '{ "Codelab": { "Integration": { "Gateway": { "Profiles": [ "SSO" ] } } } }' | ConvertFrom-Json
        $effective = Get-GatewayEffectiveSettings @($server)
        @(Get-GatewaySettingsGaps -Effective $effective -Requirements $requirements) -join ',' | Should Not Match 'Gateway:Sso'
    }

    It 'has no gap once the server file holds every value (a later layer wins)' {
        $folder = New-TestFolder 'complete'
        Write-CompleteProduction $folder
        $server = Read-TestJson (Join-Path $folder 'appsettings.Production.json')
        @(Get-GatewaySettingsGaps -Effective (Get-GatewayEffectiveSettings @($template, $server)) -Requirements $requirements).Count | Should Be 0
    }

    It 'names an empty profile list and a missing section' {
        $empty = '{ "Codelab": { "Integration": { "Gateway": { "Profiles": [] } } } }' | ConvertFrom-Json
        $gaps = @(Get-GatewaySettingsGaps -Effective (Get-GatewayEffectiveSettings @($empty)) -Requirements $requirements) -join ','
        $gaps | Should Match 'Codelab:Integration:Gateway:Profiles(,|$)'
        $gaps | Should Match 'Codelab:Security:ServiceAssertions:Server:Audience'
    }

    It 'shares the wildcard index between a requirement and its condition' {
        $file = Join-Path (New-TestFolder 'req') 'required-settings.json'
        Write-TestText $file '{ "requirements": [ { "path": "A:*:Url", "format": 2, "when": "A:*:On" } ] }'
        $rules = Read-GatewayRequirements $file
        $settings = '{ "A": [ { "On": true, "Url": "" }, { "On": false, "Url": "" } ] }' | ConvertFrom-Json
        @(Get-GatewaySettingsGaps -Effective (Get-GatewayEffectiveSettings @($settings)) -Requirements $rules) -join ',' | Should Be 'A:0:Url'
    }

    It 'refuses a when that is neither a path nor a list of conditions with paths' {
        $bad = Join-Path (New-TestFolder 'req') 'required-settings.json'
        Write-TestText $bad '{ "requirements": [ { "path": "A:B", "format": 1, "when": 5 } ] }'
        { Read-GatewayRequirements $bad } | Should Throw 'neither a path nor an array'
        Write-TestText $bad '{ "requirements": [ { "path": "A:B", "format": 1, "when": [ { "equals": "x" } ] } ] }'
        { Read-GatewayRequirements $bad } | Should Throw 'condition without a path'
    }

    It 'refuses a requirements file with an unknown format or two wildcards' {
        $bad = Join-Path (New-TestFolder 'req') 'required-settings.json'
        Write-TestText $bad '{ "requirements": [ { "path": "A:B", "format": 9 } ] }'
        { Read-GatewayRequirements $bad } | Should Throw 'unknown format 9'
        Write-TestText $bad '{ "requirements": [ { "path": "A:*:B:*", "format": 1 } ] }'
        { Read-GatewayRequirements $bad } | Should Throw 'with more than one'
    }

    It 'every format value in the shipped requirements is a GatewaySettingFormat member' {
        foreach ($requirement in $requirements) { [Enum]::IsDefined([GatewaySettingFormat], $requirement.Format) | Should Be $true }
    }
}

Describe 'Install-Gateway TLS plan (pure)' {
    $now = Get-Date

    It 'reports a gap when the site has no https binding and no thumbprint is given' {
        $plan = Get-GatewayTlsPlan -Facts (New-TlsFacts @() @()) -RequestedThumbprint '' -HttpsPort 443 -HostName $TestHost -Now $now
        $plan.Gap | Should Match 'Tls:Certificate \(the site has no https binding'
        @($plan.Steps).Count | Should Be 0
    }

    It 'plans nothing when the binding has a usable certificate' {
        $plan = Get-GatewayTlsPlan -Facts (New-TlsFacts @((New-Binding $GoodThumbprint)) @((New-CertificateFacts $GoodThumbprint))) -RequestedThumbprint '' -HttpsPort 443 -HostName $TestHost -Now $now
        $plan.Gap | Should BeNullOrEmpty
        $plan.Steps[0].ActionId | Should Be ([int] [GatewayTlsAction]::None)
    }

    It 'reports the expired certificate of an existing binding as a gap' {
        $plan = Get-GatewayTlsPlan -Facts (New-TlsFacts @((New-Binding $GoodThumbprint)) @((New-CertificateFacts $GoodThumbprint -Expired))) -RequestedThumbprint '' -HttpsPort 443 -HostName $TestHost -Now $now
        $plan.Gap | Should Match "certificate $GoodThumbprint is Expired"
    }

    It 'adds the binding and binds a usable requested certificate' {
        $plan = Get-GatewayTlsPlan -Facts (New-TlsFacts @() @((New-CertificateFacts $GoodThumbprint))) -RequestedThumbprint $GoodThumbprint.ToLowerInvariant() -HttpsPort 8443 -HostName $TestHost -Now $now
        (@($plan.Steps | ForEach-Object { $_.Action }) -join ',') | Should Be 'AddHttpsBinding,BindCertificate'
        $plan.Gap | Should BeNullOrEmpty
    }

    It 'rebinds an existing binding to a new certificate, and does nothing when it already has it' {
        $facts = New-TlsFacts @((New-Binding $OtherThumbprint)) @((New-CertificateFacts $GoodThumbprint), (New-CertificateFacts $OtherThumbprint))
        $plan = Get-GatewayTlsPlan -Facts $facts -RequestedThumbprint $GoodThumbprint -HttpsPort 443 -HostName $TestHost -Now $now
        (@($plan.Steps | ForEach-Object { $_.Action }) -join ',') | Should Be 'BindCertificate'
        $same = Get-GatewayTlsPlan -Facts $facts -RequestedThumbprint $OtherThumbprint -HttpsPort 443 -HostName $TestHost -Now $now
        $same.Steps[0].ActionId | Should Be ([int] [GatewayTlsAction]::None)
    }

    It 'refuses a requested certificate that is missing from the store' {
        $plan = Get-GatewayTlsPlan -Facts (New-TlsFacts @() @()) -RequestedThumbprint $GoodThumbprint -HttpsPort 443 -HostName $TestHost -Now $now
        $plan.Refusal | Should Match 'NotFound'
    }

    It 'checks private key, dates, server authentication and chain in that order' {
        Get-GatewayCertificateProblem -Certificate (New-CertificateFacts 'A' -NotYetValid -Untrusted) -Now $now | Should Be ([GatewayCertificateProblem]::NotYetValid)
        Get-GatewayCertificateProblem -Certificate (New-CertificateFacts 'A' -NoServerAuthentication -Untrusted) -Now $now | Should Be ([GatewayCertificateProblem]::NoServerAuthentication)
        Get-GatewayCertificateProblem -Certificate (New-CertificateFacts 'A' -Untrusted) -Now $now | Should Be ([GatewayCertificateProblem]::UntrustedChain)
        Get-GatewayCertificateProblem -Certificate (New-CertificateFacts 'A') -Now $now | Should Be ([GatewayCertificateProblem]::None)
    }
}

Describe 'Install-Gateway health probe' {
    It 'builds the readiness URL at the site root and under a child application' {
        Get-GatewayHealthUrl -BaseUrl 'https://gateway.example.test/' -AppPath '/' | Should Be $HealthUrl
        Get-GatewayHealthUrl -BaseUrl 'https://gateway.example.test' -AppPath '/gateway' | Should Be 'https://gateway.example.test/gateway/health/ready'
        Get-GatewayDefaultHealthBaseUrl -HostName $TestHost -HttpsPort 8443 | Should Be 'https://gateway.example.test:8443'
        Get-GatewayDefaultHealthBaseUrl -HostName $TestHost -HttpsPort 443 | Should Be 'https://gateway.example.test'
    }

    It 'runs the request with certificate validation in force and restores the caller''s callback' {
        $permissive = [Net.Security.RemoteCertificateValidationCallback] { $true }
        [Net.ServicePointManager]::ServerCertificateValidationCallback = $permissive
        $global:GatewayTestCallbackDuringCall = 'not called'
        Mock Invoke-WebRequest { $global:GatewayTestCallbackDuringCall = [Net.ServicePointManager]::ServerCertificateValidationCallback; [pscustomobject]@{ StatusCode = 200; Content = 'Healthy' } }
        try {
            $probe = Invoke-GatewayHealthRequest $HealthUrl
            $probe.StatusCode | Should Be 200
            $global:GatewayTestCallbackDuringCall | Should BeNullOrEmpty
            [object]::ReferenceEquals([Net.ServicePointManager]::ServerCertificateValidationCallback, $permissive) | Should Be $true
        }
        finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $null }
    }

    It 'never carries a switch that skips certificate validation' {
        $text = [IO.File]::ReadAllText($KitScript)
        $text | Should Not Match 'SkipCertificateCheck'
        $text | Should Not Match 'curl(\.exe)?\s+-k'
        $text | Should Not Match 'ServerCertificateValidationCallback\s*=\s*\{'
    }
}

Describe 'Install-Gateway -WhatIf' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-GatewayTlsFacts $TlsFactsFromGlobal
    Mock Invoke-GatewayTlsAction { }
    Mock Invoke-WebRequest { throw 'no HTTP call is expected under -WhatIf' }
    Mock Start-Sleep { }

    It 'changes nothing on a first install, prints every planned action and lists the gaps' {
        $global:GatewayTestTlsFacts = New-TlsFacts @() @((New-CertificateFacts $GoodThumbprint))
        $package = New-FakeGatewayPackage
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $global:GatewayTestSitePath = $target
        $before = Get-FolderSnapshot $server

        $result = Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -AppPoolName $TestPoolName -TargetPath $target -BackupRoot (Join-Path $server 'backups') -HostName $TestHost -CertificateThumbprint $GoodThumbprint -WhatIf

        Get-FolderSnapshot $server | Should BeExactly $before
        $result.Preview | Should Be $true
        $actions = $result.PlannedActions -join "`n"
        $actions | Should Match 'IIS CreateFolder'
        $actions | Should Match 'IIS CreateAppPool'
        $actions | Should Match 'IIS UpdateSite'
        $actions | Should Match 'TLS AddHttpsBinding'
        $actions | Should Match 'TLS BindCertificate'
        $actions | Should Match 'Backup: nothing deployed yet'
        $actions | Should Match 'Settings appsettings.Production.json: create from the package template'
        $actions | Should Match 'Refuse to start'
        $actions | Should Match 'Health probe skipped \(-WhatIf\)'
        ($result.Gaps -join ',') | Should Match 'Gateway:Sso:TokenEndpoint'
        $result.Health | Should Be ([int] [GatewayHealthOutcome]::SkippedPreview)
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
        Assert-MockCalled Invoke-GatewayTlsAction -Times 0 -Scope It
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'leaves an existing deployment byte for byte as it was' {
        $global:GatewayTestTlsFacts = New-TlsFacts @((New-Binding $GoodThumbprint)) @((New-CertificateFacts $GoodThumbprint))
        $package = New-FakeGatewayPackage -InAppFolder
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $global:GatewayTestSitePath = $target
        Write-CompleteProduction $target
        Write-TestText (Join-Path $target 'appsettings.json') '{ "Logging": { "LogLevel": { "Default": "Warning" } } }'
        [IO.File]::WriteAllBytes((Join-Path $target $EntryAssembly), [byte[]] (1, 2, 3))
        $before = Get-FolderSnapshot $server

        $result = Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot (Join-Path $server 'backups') -HostName $TestHost -WhatIf

        Get-FolderSnapshot $server | Should BeExactly $before
        $actions = $result.PlannedActions -join "`n"
        $actions | Should Match 'Take the gateway offline'
        $actions | Should Match "Back up '"
        $actions | Should Match 'Settings appsettings.json: add new key\(s\) AllowedHosts'
        $actions | Should Match 'Settings appsettings.Production.json: no new keys'
        $actions | Should Match 'TLS None'
        $result.Gaps.Count | Should Be 0
    }
}

Describe 'Install-Gateway install (IIS, TLS and HTTP mocked)' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-GatewayTlsFacts $TlsFactsFromGlobal
    Mock Invoke-GatewayTlsAction { }
    Mock Start-Sleep { }

    It 'creates the settings, keeps the gateway offline with the gap list and skips health while gaps remain' {
        Mock Invoke-WebRequest { throw 'no HTTP call is expected while gaps remain' }
        $global:GatewayTestTlsFacts = New-TlsFacts @() @()
        $package = New-FakeGatewayPackage
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $global:GatewayTestSitePath = $target

        $result = Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -AppPoolName $TestPoolName -TargetPath $target -BackupRoot (Join-Path $server 'backups') -HostName $TestHost

        $production = Read-TestJson (Join-Path $target 'appsettings.Production.json')
        $production.Codelab.Security.ServiceAssertions.Server.Audience | Should Be 'integration-gateway'
        Test-Path -LiteralPath (Join-Path $target $EntryAssembly) | Should Be $true
        $page = [IO.File]::ReadAllText((Join-Path $target 'app_offline.htm'))
        $page | Should Match 'Configuration incomplete'
        $page | Should Match 'Tls:Certificate'
        $page | Should Match 'Gateway:Sso:TokenEndpoint'
        $result.StartRefused | Should Be $true
        $result.Health | Should Be ([int] [GatewayHealthOutcome]::SkippedGaps)
        Test-Path -LiteralPath (Join-Path $server 'backups\install-gateway.log') | Should Be $true
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'binds the given certificate, keeps the server Production file, removes the previous gap page and reports healthy' {
        Mock Invoke-WebRequest { [pscustomobject]@{ StatusCode = 503; Content = '' } }
        Mock Invoke-WebRequest -ParameterFilter { $Uri -eq $HealthUrl } { [pscustomobject]@{ StatusCode = 200; Content = 'Healthy' } }
        $global:GatewayTestTlsFacts = New-TlsFacts @() @((New-CertificateFacts $GoodThumbprint))
        $package = New-FakeGatewayPackage -InAppFolder
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $global:GatewayTestSitePath = $target
        Write-CompleteProduction $target
        [IO.File]::WriteAllBytes((Join-Path $target $EntryAssembly), [byte[]] (1, 2, 3))
        Write-TestText (Join-Path $target 'app_offline.htm') 'gap page of a previous run'
        Set-Content -LiteralPath (Join-Path $target 'old-only.txt') -Value 'removed by the mirror'
        $productionBefore = (Get-FileHash -LiteralPath (Join-Path $target 'appsettings.Production.json')).Hash

        $result = Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot (Join-Path $server 'backups') -HostName $TestHost -CertificateThumbprint $GoodThumbprint

        (Get-FileHash -LiteralPath (Join-Path $target 'appsettings.Production.json')).Hash | Should Be $productionBefore
        Test-Path -LiteralPath (Join-Path $target 'old-only.txt') | Should Be $false
        Test-Path -LiteralPath (Join-Path $result.BackupPath 'site\old-only.txt') | Should Be $true
        Test-Path -LiteralPath (Join-Path $target 'app_offline.htm') | Should Be $false
        $result.Gaps.Count | Should Be 0
        $result.StartRefused | Should Be $false
        $result.Health | Should Be ([int] [GatewayHealthOutcome]::Healthy)
        Assert-MockCalled Invoke-GatewayTlsAction -Times 1 -Scope It -ParameterFilter { $Step.ActionId -eq [int] [GatewayTlsAction]::AddHttpsBinding }
        Assert-MockCalled Invoke-GatewayTlsAction -Times 1 -Scope It -ParameterFilter { $Step.ActionId -eq [int] [GatewayTlsAction]::BindCertificate -and $Thumbprint -eq $GoodThumbprint }
        Assert-MockCalled Invoke-WebRequest -Times 1 -Scope It -ParameterFilter { $Uri -eq $HealthUrl }
    }

    It 'throws with the diagnosis and the rollback path when health never answers 200, and does not stay offline' {
        Mock Invoke-GatewayHealthRequest { [pscustomobject]@{ StatusCode = 0; Body = ''; Error = 'TrustFailure: could not establish trust' } }
        $global:GatewayTestTlsFacts = New-TlsFacts @((New-Binding $GoodThumbprint)) @((New-CertificateFacts $GoodThumbprint))
        $package = New-FakeGatewayPackage
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $global:GatewayTestSitePath = $target
        Write-CompleteProduction $target
        [IO.File]::WriteAllBytes((Join-Path $target $EntryAssembly), [byte[]] (1, 2, 3))
        { Invoke-GatewayKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot (Join-Path $server 'backups') -HostName $TestHost } |
            Should Throw 'did not answer 200. Roll back'
        Assert-MockCalled Invoke-GatewayHealthRequest -Times 12 -Scope It
        Test-Path -LiteralPath (Join-Path $target 'app_offline.htm') | Should Be $false
    }
}

Remove-Variable -Name GatewayTestTlsFacts, GatewayTestSitePath, GatewayTestCallbackDuringCall -Scope Global -ErrorAction SilentlyContinue
