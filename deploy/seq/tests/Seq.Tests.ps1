# Pester 3.4 tests for deploy/seq (Windows PowerShell 5.1): the configuration files are valid JSON, every template
# renders for every tier, titles are neutral, the required signals exist, and the pure parts of apply-seq-config.ps1
# and Install-Seq.ps1 plan what they should. No Seq server, no network, no service change.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/seq/tests/Seq.Tests.ps1 -EnableExit"
Set-StrictMode -Version Latest

$SeqFolder = Split-Path -Parent $PSScriptRoot
$TemplateFolder = Join-Path $SeqFolder 'templates'
$Tiers = @('development', 'test', 'staging', 'preproduction', 'production')
$GoodThumbprint = 'AA11BB22CC33DD44EE55FF6600112233445566AA'

# Both scripts declare their own parameters here; test variables use other names.
. (Join-Path $SeqFolder 'apply-seq-config.ps1')
. (Join-Path $SeqFolder 'Install-Seq.ps1')

function Read-TestJson([string] $Path) { return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json) }

$Config = Read-SeqConfig
$TemplateFiles = @(Get-ChildItem -LiteralPath $TemplateFolder -Filter '*.template' | Sort-Object Name)

Describe 'deploy/seq files' {
    It 'every JSON file and every template is valid JSON before rendering' {
        foreach ($file in @(Get-ChildItem -LiteralPath $SeqFolder -Filter '*.json') + $TemplateFiles) {
            { Read-TestJson $file.FullName } | Should Not Throw
        }
    }

    It 'has one thresholds file per deployment tier, each naming its own tier' {
        foreach ($tier in $Tiers) { (Read-SeqThresholds -Environment $tier).environment | Should Be $tier }
        @(Get-ChildItem -LiteralPath $SeqFolder -Filter 'thresholds.*.json').Count | Should Be $Tiers.Count
    }

    It 'every template title starts with the prefix and names no application (neutral titles)' {
        foreach ($file in $TemplateFiles) {
            $title = (Read-TestJson $file.FullName).Title
            $title.StartsWith($Config.titlePrefix) | Should Be $true
            Test-SeqNeutralTitle -Title $title -Config $Config | Should Be $true
        }
        Test-SeqNeutralTitle -Title (Get-SeqDefaultInstanceTitle $Config 'production') -Config $Config | Should Be $true
    }

    It 'carries the agreed signals: reference codes, CSP violations, gateway relay failures, job failures, certificate expiry' {
        $filters = @($TemplateFiles | ForEach-Object { Read-TestJson $_.FullName } | Where-Object { $_.'$entity' -eq 'signal' } | ForEach-Object { $_.Filters[0].Filter }) -join "`n"
        $filters | Should Match 'Has\(ReferenceCode\)'
        $filters | Should Match "'CspViolation'"
        foreach ($code in @('GATEWAY_UPSTREAM_UNAVAILABLE', 'GATEWAY_UPSTREAM_REJECTED', 'GATEWAY_PROFILE_DISABLED')) { $filters | Should Match $code }
        $filters | Should Match "SourceContext = 'Codelab.Jobs'"
        $filters | Should Match 'CertificateStatus in \[2, 3, 4\]'
    }

    It 'has a reference-code saved query and an overview dashboard' {
        $entities = @($TemplateFiles | ForEach-Object { (Read-TestJson $_.FullName).'$entity' })
        ($entities -join ',') | Should Match 'sqlquery'
        ($entities -join ',') | Should Match 'dashboard'
    }

    It 'holds no key material, host name or address' {
        foreach ($file in @(Get-ChildItem -LiteralPath $SeqFolder -Recurse -File | Where-Object { @('.json', '.template') -contains $_.Extension })) {
            $text = [IO.File]::ReadAllText($file.FullName)
            $text | Should Not Match '\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b'
            $text | Should Not Match '"(ApiKey|Token)"\s*:\s*"[^"]+"'
        }
    }
}

Describe 'apply-seq-config rendering (pure)' {
    It 'renders every template for every tier with no token left and every ref() resolvable' {
        foreach ($tier in $Tiers) {
            $renderValues = Get-SeqTokens -Environment $tier -Thresholds (Read-SeqThresholds -Environment $tier) -NotificationAppInstanceId @('appinstance-12')
            $records = @(Invoke-SeqRender -TemplateFolder $TemplateFolder -Tokens $renderValues -Config $Config -Target $null)
            $records.Count | Should Be $TemplateFiles.Count
            foreach ($record in $records | Where-Object { $_.Entity -eq 'alert' -or $_.Entity -eq 'dashboard' }) { $record.Title | Should Match ([regex]::Escape("[$tier]")) }
        }
    }

    It 'writes seqcli files whose ref() calls are the only non-JSON part' {
        $target = Join-Path $TestDrive 'render'
        New-Item -ItemType Directory -Path $target | Out-Null
        $renderValues = Get-SeqTokens -Environment 'production' -Thresholds (Read-SeqThresholds -Environment 'production') -NotificationAppInstanceId @()
        Invoke-SeqRender -TemplateFolder $TemplateFolder -Tokens $renderValues -Config $Config -Target $target | Out-Null
        $alert = [IO.File]::ReadAllText((Join-Path $target 'alert-server-errors.template'))
        $alert | Should Match 'ref\("signal-server-errors.template"\)'
        $alert | Should Match '"Having": "n > 20"'
        $alert | Should Match '"NotificationChannels": \[\]'
        $json = [regex]::Replace($alert, 'ref\("([^"]+)"\)', '"$1"') | ConvertFrom-Json
        $json.Where | Should Be "DeploymentEnvironment = 'production'"
        $certificate = [regex]::Replace([IO.File]::ReadAllText((Join-Path $target 'alert-certificate-expiry.template')), 'ref\("([^"]+)"\)', '"$1"') | ConvertFrom-Json
        $certificate.IsDisabled | Should Be $false
    }

    It 'disables the certificate alert where the tier says so' {
        $renderValues = Get-SeqTokens -Environment 'development' -Thresholds (Read-SeqThresholds -Environment 'development') -NotificationAppInstanceId @()
        $renderValues['json:certificateAlertDisabled'] | Should Be 'true'
    }

    It 'refuses an unknown token, a dangling ref and a title that is not neutral' {
        { ConvertTo-SeqRenderedText -Text '{ "Title": "{{nope}}" }' -Tokens @{} -Name 'x' } | Should Throw 'unknown token {{nope}}'
        $folder = Join-Path $TestDrive 'bad'
        New-Item -ItemType Directory -Path $folder | Out-Null
        [IO.File]::WriteAllText((Join-Path $folder 'a.template'), '{ "$entity": "signal", "Title": "Ops: A", "SignalId": "{{ref:missing.template}}" }')
        { Invoke-SeqRender -TemplateFolder $folder -Tokens @{} -Config $Config -Target $null } | Should Throw 'names no template'
        [IO.File]::WriteAllText((Join-Path $folder 'a.template'), ('{{ "$entity": "signal", "Title": "Ops: {0} errors" }}' -f $Config.applications[0]))
        { Invoke-SeqRender -TemplateFolder $folder -Tokens @{} -Config $Config -Target $null } | Should Throw 'titles stay neutral'
    }

    It 'refuses a notification id that is not a Seq app instance' {
        { Get-SeqTokens -Environment 'test' -Thresholds (Read-SeqThresholds -Environment 'test') -NotificationAppInstanceId @('mail-team') } | Should Throw 'is not a Seq app instance id'
    }
}

Describe 'apply-seq-config keys and server URL (pure)' {
    It 'wants one ingest key per application and tier, one per gateway node, and the read key only on request' {
        $thresholds = [pscustomobject]@{ gatewayNodes = @('dmz1', 'dmz2') }
        $keys = @(Get-SeqWantedKeys -Config $Config -Thresholds $thresholds -Environment 'test' -IncludeReadKeys $false)
        $titles = @($keys | ForEach-Object { $_.Title }) -join ','
        $titles | Should Match ([regex]::Escape("Ops: ingest $($Config.applications[0]) [test]"))
        $titles | Should Match ([regex]::Escape("Ops: ingest $($Config.gatewayApplication)-dmz1 [test]"))
        $titles | Should Not Match ([regex]::Escape("ingest $($Config.gatewayApplication) [test]"))
        $titles | Should Not Match 'read'
        $withRead = @(Get-SeqWantedKeys -Config $Config -Thresholds $thresholds -Environment 'test' -IncludeReadKeys $true)
        (@($withRead | ForEach-Object { $_.Title }) -join ',') | Should Match 'Ops: read ops-console'
    }

    It 'reports drift in applied properties, level and permissions' {
        $want = [pscustomobject]@{ Title = 't'; Application = 'app-a'; Permissions = @('Ingest'); Applied = $true }
        $good = [pscustomobject]@{ AssignedPermissions = @('Ingest'); InputSettings = [pscustomobject]@{ MinimumLevel = 'Information'; AppliedProperties = @([pscustomobject]@{ Name = 'Application'; Value = 'app-a' }, [pscustomobject]@{ Name = 'DeploymentEnvironment'; Value = 'test' }) } }
        @(Get-SeqKeyDrift -Existing $good -Want $want -Environment 'test' -MinimumLevel 'Information').Count | Should Be 0
        $bad = [pscustomobject]@{ AssignedPermissions = @('Ingest', 'Read'); InputSettings = [pscustomobject]@{ MinimumLevel = 'Debug'; AppliedProperties = @([pscustomobject]@{ Name = 'Application'; Value = 'app-b' }) } }
        $drift = @(Get-SeqKeyDrift -Existing $bad -Want $want -Environment 'test' -MinimumLevel 'Information') -join ';'
        $drift | Should Match 'minimum level'
        $drift | Should Match 'applied Application'
        $drift | Should Match 'applied DeploymentEnvironment'
        $drift | Should Match 'permissions'
    }

    It 'accepts https, and plain http only for development on localhost' {
        { Assert-SeqServerUrl -Url 'https://logs.example.test' -Environment 'production' } | Should Not Throw
        { Assert-SeqServerUrl -Url 'http://localhost:5341' -Environment 'development' } | Should Not Throw
        { Assert-SeqServerUrl -Url 'http://localhost:5341' -Environment 'test' } | Should Throw 'must be https'
        { Assert-SeqServerUrl -Url 'http://logs.example.test' -Environment 'development' } | Should Throw 'must be https'
    }

    It 'previews with -WhatIf without a server or an admin key' {
        $previous = [Environment]::GetEnvironmentVariable('SEQ_ADMIN_KEY')
        [Environment]::SetEnvironmentVariable('SEQ_ADMIN_KEY', $null)
        Mock Invoke-RestMethod { throw 'no HTTP call is expected under -WhatIf' }
        try { Invoke-SeqApply -Environment 'staging' -RenderFolder $null -WhatIf | Should Be 0 }
        finally { [Environment]::SetEnvironmentVariable('SEQ_ADMIN_KEY', $previous) }
        Assert-MockCalled Invoke-RestMethod -Times 0 -Scope It
    }
}

Describe 'Install-Seq (pure plan, parameters, -WhatIf)' {
    $fresh = [pscustomobject]@{ Installed = $false; ServiceExists = $false; ServiceRunning = $false; BoundThumbprint = ''; AdminKeyPresent = $false; FirstRunHashPresent = $true }
    $done = [pscustomobject]@{ Installed = $true; ServiceExists = $true; ServiceRunning = $true; BoundThumbprint = $GoodThumbprint; AdminKeyPresent = $false; FirstRunHashPresent = $false }

    It 'plans the whole install on a fresh server, and the title as an operator note without the admin key' {
        $plan = Get-SeqInstallPlan -Facts $fresh -Environment 'test' -MsiPath 'D:\media\seq.msi' -ListenUrl 'https://logs.example.test' -CertificateThumbprint $GoodThumbprint -StoragePath 'D:\Seq' -InstanceTitle 'Application logs (test)'
        (@($plan.Steps | ForEach-Object { $_.Action }) -join ',') | Should Be 'InstallPackage,CreateService,EnableAuthentication,BindCertificate,StartService'
        ($plan.Notes -join ' ') | Should Match 'Set the instance title'
    }

    It 'plans nothing on a server that is in place, and rebinds a changed certificate' {
        $plan = Get-SeqInstallPlan -Facts $done -MsiPath '' -ListenUrl 'https://logs.example.test' -CertificateThumbprint $GoodThumbprint -StoragePath 'D:\Seq' -InstanceTitle 'x'
        $plan.Steps[0].ActionId | Should Be ([int] [SeqInstallAction]::None)
        $rebind = Get-SeqInstallPlan -Facts $done -MsiPath '' -ListenUrl 'https://logs.example.test' -CertificateThumbprint ('BB' + $GoodThumbprint.Substring(2)) -StoragePath 'D:\Seq' -InstanceTitle 'x'
        (@($rebind.Steps | ForEach-Object { $_.Action }) -join ',') | Should Be 'BindCertificate,StartService'
    }

    It 'sets the instance title when the admin key is present' {
        $withKey = [pscustomobject]@{ Installed = $true; ServiceExists = $true; ServiceRunning = $true; BoundThumbprint = $GoodThumbprint; AdminKeyPresent = $true }
        $plan = Get-SeqInstallPlan -Facts $withKey -MsiPath '' -ListenUrl 'https://logs.example.test' -CertificateThumbprint $GoodThumbprint -StoragePath 'D:\Seq' -InstanceTitle 'x'
        (@($plan.Steps | ForEach-Object { $_.Action }) -join ',') | Should Be 'SetInstanceTitle'
    }

    It 'refuses a new instance outside development without the first admin password hash, and asks for none on localhost' {
        $noHash = [pscustomobject]@{ Installed = $true; ServiceExists = $false; ServiceRunning = $false; BoundThumbprint = ''; AdminKeyPresent = $false; FirstRunHashPresent = $false }
        foreach ($tier in @('test', 'staging', 'preproduction', 'production')) {
            $refused = Get-SeqInstallPlan -Facts $noHash -Environment $tier -MsiPath '' -ListenUrl 'https://logs.example.test' -CertificateThumbprint $GoodThumbprint -StoragePath 'D:\Seq' -InstanceTitle 'x'
            $refused.Refusal | Should Match 'SEQ_FIRSTRUN_ADMINPASSWORDHASH'
            @($refused.Steps).Count | Should Be 0
        }
        $local = Get-SeqInstallPlan -Facts $noHash -Environment 'development' -MsiPath '' -ListenUrl 'http://localhost:5341' -CertificateThumbprint '' -StoragePath 'D:\Seq' -InstanceTitle 'x'
        $local.Refusal | Should Be $null
        (@($local.Steps | ForEach-Object { $_.Action }) -join ',') | Should Be 'CreateService,StartService'
    }

    It 'refuses a server without Seq when no MSI is given' {
        (Get-SeqInstallPlan -Facts $fresh -MsiPath '' -ListenUrl 'https://logs.example.test' -CertificateThumbprint $GoodThumbprint -StoragePath 'D:\Seq' -InstanceTitle 'x').Refusal | Should Match 'give -MsiPath'
    }

    It 'refuses http, a missing thumbprint, a relative storage path and a title naming an application' {
        { Invoke-SeqInstall -Environment 'test' -ListenUrl 'http://logs.example.test' -StoragePath 'D:\Seq' -WhatIf } | Should Throw 'must be https'
        { Invoke-SeqInstall -Environment 'test' -ListenUrl 'https://logs.example.test' -StoragePath 'D:\Seq' -WhatIf } | Should Throw '-CertificateThumbprint is required'
        { Invoke-SeqInstall -Environment 'test' -ListenUrl 'https://logs.example.test' -CertificateThumbprint $GoodThumbprint -StoragePath 'Seq' -WhatIf } | Should Throw 'must be a fully qualified'
        { Invoke-SeqInstall -Environment 'test' -ListenUrl 'https://logs.example.test' -CertificateThumbprint $GoodThumbprint -StoragePath 'D:\Seq' -InstanceTitle ($Config.applications[0] + ' logs') -WhatIf } |
            Should Throw 'keep it neutral'
    }

    It 'previews without changing anything' {
        Mock Get-SeqInstallFacts { [pscustomobject]@{ Installed = $true; ServiceExists = $false; ServiceRunning = $false; BoundThumbprint = ''; AdminKeyPresent = $false; FirstRunHashPresent = $true } }
        Mock Invoke-SeqInstallAction { throw 'no change is expected under -WhatIf' }
        Mock Invoke-WebRequest { throw 'no HTTP call is expected under -WhatIf' }
        $result = Invoke-SeqInstall -Environment 'production' -ListenUrl 'https://logs.example.test' -CertificateThumbprint $GoodThumbprint -StoragePath 'D:\Seq' -WhatIf
        $result.Preview | Should Be $true
        $result.InstanceTitle | Should Be 'Application logs (production)'
        (@($result.Plan.Steps | ForEach-Object { $_.Action }) -join ',') | Should Be 'CreateService,EnableAuthentication,BindCertificate,StartService'
        Assert-MockCalled Invoke-SeqInstallAction -Times 0 -Scope It
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'installs, then waits for /api over validated TLS and checks that an anonymous read is refused' {
        Mock Get-SeqInstallFacts { [pscustomobject]@{ Installed = $true; ServiceExists = $true; ServiceRunning = $false; BoundThumbprint = $GoodThumbprint; AdminKeyPresent = $false; FirstRunHashPresent = $false } }
        Mock Invoke-SeqInstallAction { }
        Mock Invoke-SeqHealthRequest { 200 } -ParameterFilter { $Url -eq 'https://logs.example.test/api' }
        Mock Invoke-SeqHealthRequest { 401 } -ParameterFilter { $Url -eq 'https://logs.example.test/api/events?count=1' }
        $result = Invoke-SeqInstall -Environment 'production' -ListenUrl 'https://logs.example.test/' -CertificateThumbprint $GoodThumbprint -StoragePath 'D:\Seq'
        $result.HealthStatusCode | Should Be 200
        $result.AnonymousReadStatusCode | Should Be 401
        Assert-MockCalled Invoke-SeqInstallAction -Times 1 -Scope It -ParameterFilter { $Step.ActionId -eq [int] [SeqInstallAction]::StartService }
        Assert-MockCalled Invoke-SeqHealthRequest -Times 1 -Scope It -ParameterFilter { $Url -eq 'https://logs.example.test/api' }
    }

    It 'fails the run when Seq answers an anonymous read outside development, and not on localhost development' {
        Mock Get-SeqInstallFacts { [pscustomobject]@{ Installed = $true; ServiceExists = $true; ServiceRunning = $true; BoundThumbprint = $GoodThumbprint; AdminKeyPresent = $false; FirstRunHashPresent = $false } }
        Mock Invoke-SeqInstallAction { }
        Mock Invoke-SeqHealthRequest { 200 }  # Pester 3 keeps the earlier It's filtered mocks, so this test uses another host.
        { Invoke-SeqInstall -Environment 'staging' -ListenUrl 'https://open-logs.example.test' -CertificateThumbprint $GoodThumbprint -StoragePath 'D:\Seq' } | Should Throw 'authentication is off'
        $local = Invoke-SeqInstall -Environment 'development' -ListenUrl 'http://localhost:5341' -StoragePath 'D:\Seq'
        $local.AnonymousReadStatusCode | Should Be $null
    }

    It 'passes the first admin password hash only through the environment, never as a parameter or in a log line' {
        $text = [IO.File]::ReadAllText((Join-Path $SeqFolder 'Install-Seq.ps1'))
        $text | Should Not Match '\[string\]\s*\$\w*(Password|Hash)\w*\s*[,)]'
        $text | Should Not Match 'Write-Host[^\n]*\$hash'
    }

    It 'never carries a switch that skips certificate validation' {
        foreach ($script in @('Install-Seq.ps1', 'apply-seq-config.ps1', 'SeqCommon.psm1')) {
            $text = [IO.File]::ReadAllText((Join-Path $SeqFolder $script))
            $text | Should Not Match 'SkipCertificateCheck'
            $text | Should Not Match 'ServerCertificateValidationCallback\s*=\s*\{'
        }
    }
}
