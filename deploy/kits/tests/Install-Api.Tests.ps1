# Pester 3.4 tests for deploy/kits/Install-Api.ps1 (Windows PowerShell 5.1).
# No IIS change, no admin rights, no network: IIS state and tuning, the pool environment, prerequisites, the pool
# recycle and HTTP are mocked, and every folder the kit touches is under $TestDrive. The fixtures are built here (no
# fixture files): a fake publish output and settings with obviously fake, non-secret values.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Api.Tests.ps1 -EnableExit"
Set-StrictMode -Version Latest

$KitFolder = Split-Path -Parent $PSScriptRoot
$KitScript = Join-Path $KitFolder 'Install-Api.ps1'
$TemplatePath = Join-Path $KitFolder 'templates\api\appsettings.template.json'
$EntryAssembly = 'Starter.Api.dll'
$TestSiteName = 'Kit Test Site'
$TestPoolName = 'KitTestPool'
$TestHealthBase = 'https://api.example.test'
$TestHealthUrl = 'https://api.example.test/health/ready'
$FakeSecret = 'kit-test-value-not-a-secret'

# The kit declares its own parameters (PackagePath, SiteName, ...) here; test variables use other names.
. $KitScript

function New-TestFolder([string] $Name) {
    $path = Join-Path $TestDrive ('{0}-{1}' -f $Name, [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

function Write-TestText([string] $Path, [string] $Text) {
    $folder = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

function Get-TestWebConfig([string] $EnvironmentName) {
    $variables = if ($EnvironmentName) { "<environmentVariables><environmentVariable name=`"ASPNETCORE_ENVIRONMENT`" value=`"$EnvironmentName`" /></environmentVariables>" } else { '' }
    return @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers><add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" /></handlers>
      <aspNetCore processPath="dotnet" arguments=".\Starter.Api.dll" stdoutLogEnabled="false" hostingModel="inprocess">$variables</aspNetCore>
    </system.webServer>
  </location>
</configuration>
"@
}

# The package appsettings.json: the shape of backend/src/Starter.Api/appsettings.json (no secrets, no hosts).
$PackageSettings = @'
{
  "Codelab": {
    "Security": {
      "RequestSigning": { "Mode": "Enforce", "Audience": "starter-api" },
      "ServiceAssertions": { "Client": { "ServiceName": "starter-api", "Audience": "starter-attachments" } }
    }
  },
  "Notifications": { "Email": { "Mode": 2, "FromAddress": "no-reply@example.test" }, "Sms": { "Enabled": false, "Mode": 1 } },
  "Portal": { "Contact": { "InboxEmail": "contact@example.invalid", "InboxLanguage": 1 } },
  "Jobs": { "Enabled": true, "WorkerCount": 5 }
}
'@

# A fake publish output: the entry DLL (placeholder bytes), web.config and the package appsettings.json.
function New-FakePackage([switch] $InAppFolder, [string] $EnvironmentName) {
    $root = New-TestFolder 'package'
    $app = if ($InAppFolder) { Join-Path $root 'app' } else { $root }
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $app $EntryAssembly), [byte[]] (77, 90))
    Write-TestText (Join-Path $app 'web.config') (Get-TestWebConfig $EnvironmentName)
    Write-TestText (Join-Path $app 'appsettings.json') $PackageSettings
    return $root
}

function New-TestRing([string] $KeyId, [string] $Material) {
    return @{ ActiveKeyId = $KeyId; Keys = @(@{ KeyId = $KeyId; MaterialBase64 = $Material }) }
}

# A server appsettings.Production.json with every required key filled (fake values; each ring its own material).
function Get-CompleteProduction {
    return @{
        ConnectionStrings = @{ Starter = $FakeSecret }
        Auth = @{ WebBaseUrl = 'https://portal.kit.test' }
        Users = @{ Activation = @{ LinkBaseUrl = 'https://portal.kit.test' } }
        Attachments = @{ ServiceBaseUrl = 'https://files.kit.test' }
        Portal = @{ Contact = @{ InboxEmail = 'inbox@kit.test' } }
        Notifications = @{ Email = @{ FromAddress = 'no-reply@kit.test'; Smtp = @{ Host = 'relay.kit.test' } } }
        Identity = @{ Jwt = @{ SigningKey = $FakeSecret }; BreakGlass = @{ Password = $FakeSecret } }
        Observability = @{ UserIdHash = @{ Key = $FakeSecret } }
        Codelab = @{
            Security = @{
                Cors = @{ AllowedOrigins = @('https://portal.kit.test') }
                PublicIds = (New-TestRing 'p1' 'material-publicids')
                RequestSigning = (New-TestRing 'r1' 'material-signing')
                Tickets = (New-TestRing 't1' 'material-tickets') + @{ Enabled = $true }
                UploadTickets = (New-TestRing 'u1' 'material-uploadtickets') + @{ Enabled = $true }
                Uploads = (New-TestRing 'c1' 'material-receipts') + @{ ReceiptsEnabled = $true }
                ServiceAssertions = @{ Client = @{ KeyId = 's2s-1'; PrivateKeyPem = $FakeSecret } }
            }
        }
    }
}

function ConvertTo-TestJson([object] $Value) { return (ConvertTo-Json -InputObject $Value -Depth 16) }

function Write-ServerProduction([string] $TargetPath, [object] $Settings) {
    Write-TestText (Join-Path $TargetPath 'appsettings.Production.json') (ConvertTo-TestJson $Settings)
}

function Read-TestJson([string] $Path) { return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json) }

function Get-FolderSnapshot([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '<missing>' }
    $lines = Get-ChildItem -LiteralPath $Path -Recurse -Force | Sort-Object FullName | ForEach-Object {
        if ($_.PSIsContainer) { $_.FullName } else { '{0} {1}' -f $_.FullName, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    return (@($lines) -join "`n")
}

# Effective settings of the package base + kit template + one Production object (the merge result on a server).
function Get-TestGaps([object] $Production, [hashtable] $Environment) {
    $layers = @(($PackageSettings | ConvertFrom-Json), (Read-ApiJson $TemplatePath))
    if ($null -ne $Production) { $layers += ((ConvertTo-TestJson $Production) | ConvertFrom-Json) }
    if ($null -ne $Environment) { $layers += $Environment }
    return @(Get-ApiSettingsGaps -Effective (Get-ApiEffectiveSettings $layers))
}

function Get-TestGapText([object[]] $Gaps) { return (@($Gaps | ForEach-Object { '{0}|{1}' -f $_.Path, $_.Reason }) -join ',') }

# IIS: an existing site (the API runs at its root); the apply steps are mocked.
$IisStateWithSite = {
    [pscustomobject]@{
        Known = $true; PhysicalPathExists = $false; Pool = $null
        Site = [pscustomobject]@{ PhysicalPath = 'C:\site-root'; ApplicationPool = 'SitePool' }; Application = $null
    }
}
$IisStateWithoutSite = { [pscustomobject]@{ Known = $true; PhysicalPathExists = $false; Pool = $null; Site = $null; Application = $null } }
$PrerequisitesPassed = { [pscustomobject]@{ Passed = $true; Checks = @() } }

Describe 'Install-Api parameters and package' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-ApiSiteHttpsBindingCount { 1 }

    It 'refuses a missing -PackagePath' {
        { Invoke-ApiKit -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -WhatIf } | Should Throw '-PackagePath is required'
    }

    It 'refuses a missing -SiteName' {
        { Invoke-ApiKit -PackagePath (New-FakePackage) -HealthBaseUrl $TestHealthBase -WhatIf } | Should Throw '-SiteName is required'
    }

    It 'refuses an http or missing -HealthBaseUrl (the probe is https with validated certificates only)' {
        $package = New-FakePackage
        { Invoke-ApiKit -PackagePath $package -SiteName $TestSiteName -HealthBaseUrl 'http://localhost' -WhatIf } | Should Throw 'must be the site''s https address'
        { Invoke-ApiKit -PackagePath $package -SiteName $TestSiteName -WhatIf } | Should Throw 'must be the site''s https address'
    }

    It 'refuses an -AppPath that is not rooted' {
        { Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -AppPath 'api' -HealthBaseUrl $TestHealthBase -WhatIf } | Should Throw 'must be ''/'''
    }

    It 'refuses a -BackupRoot inside -TargetPath (the mirror would delete it)' {
        $server = New-TestFolder 'srv'
        { Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'app\backups') -WhatIf } |
            Should Throw 'overlap'
    }

    It 'refuses a folder without the API entry assembly' {
        $empty = New-TestFolder 'empty'
        { Invoke-ApiKit -PackagePath $empty -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -WhatIf } | Should Throw 'No Starter.Api.dll'
    }

    It 'finds the publish output directly or in app\' {
        $direct = New-FakePackage
        $nested = New-FakePackage -InAppFolder
        Resolve-ApiPackage $direct | Should Be (Resolve-Path -LiteralPath $direct).ProviderPath
        Resolve-ApiPackage $nested | Should Be (Resolve-Path -LiteralPath (Join-Path $nested 'app')).ProviderPath
    }

    It 'refuses a package whose web.config forces the Development environment, before anything changes' {
        $server = New-TestFolder 'srv'
        { Invoke-ApiKit -PackagePath (New-FakePackage -EnvironmentName 'Development') -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') } |
            Should Throw 'sets the Development environment'
        Test-Path -LiteralPath (Join-Path $server 'app') | Should Be $false
    }

    It 'reads the hosting model and the stdout switch of an accepted web.config' {
        $facts = Get-ApiWebConfigFacts (New-FakePackage -EnvironmentName 'Production')
        $facts.HostingModel | Should Be 'inprocess'
        $facts.StdoutLogEnabled | Should Be $false
    }

    Context 'the site does not exist' {
        Mock -ModuleName Install-Common Get-KitIisState $IisStateWithoutSite

        It 'refuses a real run (a kit never creates sites or bindings)' {
        $server = New-TestFolder 'srv'
        { Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') } |
            Should Throw 'does not exist'
        Test-Path -LiteralPath (Join-Path $server 'app') | Should Be $false
        Test-Path -LiteralPath (Join-Path $server 'backups') | Should Be $false
        }
    }

    Context 'the site has no https binding' {
        Mock Get-ApiSiteHttpsBindingCount { 0 }

        It 'refuses a real run' {
        $server = New-TestFolder 'srv'
        { Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') } |
            Should Throw 'has no https binding'
        }
    }

    It 'refuses a real run when prerequisites are missing, and changes nothing' {
        Mock Test-KitPrerequisites { [pscustomobject]@{ Passed = $false; Checks = @() } }
        $server = New-TestFolder 'srv'
        { Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') } |
            Should Throw 'Prerequisites are missing'
        Test-Path -LiteralPath (Join-Path $server 'app') | Should Be $false
    }

    It 'exits non-zero with the reason when the script runs without -PackagePath' {
        # The dot-sourced kit sets ErrorActionPreference Stop here; the child's stderr must stay plain output.
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $KitScript -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase 2>&1 | Out-String
        $LASTEXITCODE | Should Not Be 0
        $output | Should Match 'PackagePath is required'
    }
}

Describe 'Install-Api -WhatIf' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-ApiSiteHttpsBindingCount { 1 }
    Mock Get-ApiPoolEnvironmentVariables { @() }
    Mock Get-ApiTuningState { $null }
    Mock Invoke-ApiTuningStep { }
    Mock Restart-ApiAppPool { }
    Mock Invoke-WebRequest { throw 'no HTTP call is expected under -WhatIf' }
    Mock Start-Sleep { }

    It 'changes nothing on a first install, prints every planned action and lists the gaps' {
        $server = New-TestFolder 'srv'
        $before = Get-FolderSnapshot $server

        $result = Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -AppPoolName $TestPoolName -HealthBaseUrl $TestHealthBase -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') -WhatIf

        Get-FolderSnapshot $server | Should BeExactly $before
        $result.Preview | Should Be $true
        $actions = $result.PlannedActions -join "`n"
        $actions | Should Match 'IIS CreateFolder'
        $actions | Should Match 'IIS CreateAppPool'
        $actions | Should Match 'IIS UpdateSite'
        $actions | Should Match "IIS tuning IIS:\\AppPools\\$TestPoolName startMode: '' -> 'AlwaysRunning'"
        $actions | Should Match 'applicationDefaults.preloadEnabled'
        $actions | Should Match 'Backup: nothing deployed yet'
        $actions | Should Match 'Settings appsettings.json: create from the package template'
        $actions | Should Match 'Settings appsettings.Production.json: create from the package template'
        $actions | Should Match 'Keep the API offline'
        $actions | Should Match 'Health probe skipped \(-WhatIf\)'
        $result.SiteHeldOffline | Should Be $true
        ($result.SettingsGaps -join ',') | Should Match 'Codelab:Security:UploadTickets:Keys:0:MaterialBase64'
        $result.Health | Should Be ([int] [ApiHealthOutcome]::SkippedPreview)
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
        Assert-MockCalled Invoke-ApiTuningStep -Times 0 -Scope It
        Assert-MockCalled Restart-ApiAppPool -Times 0 -Scope It
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'leaves an existing deployment byte for byte as it was (settings, files, no offline page, no backup, no log)' {
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        Write-ServerProduction $target (Get-CompleteProduction)
        Write-TestText (Join-Path $target 'appsettings.json') '{ "Jobs": { "WorkerCount": 2 } }'
        [IO.File]::WriteAllBytes((Join-Path $target $EntryAssembly), [byte[]] (1, 2, 3))
        $before = Get-FolderSnapshot $server

        $result = Invoke-ApiKit -PackagePath (New-FakePackage -InAppFolder) -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -TargetPath $target -BackupRoot (Join-Path $server 'backups') -WhatIf

        Get-FolderSnapshot $server | Should BeExactly $before
        $actions = $result.PlannedActions -join "`n"
        $actions | Should Match 'Take the API offline'
        $actions | Should Match "Back up '"
        $actions | Should Match 'Settings appsettings.json: add new key\(s\) Codelab, Notifications, Portal, Jobs:Enabled'
        $actions | Should Match 'Settings appsettings.Production.json: add new key\(s\) Database, Codelab:Identity, Codelab:Integration'
        $actions | Should Match 'Bring the API online'
        $result.SettingsGaps.Count | Should Be 0
        $result.SiteHeldOffline | Should Be $false
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    Context 'the site does not exist' {
        Mock -ModuleName Install-Common Get-KitIisState $IisStateWithoutSite

        It 'previews with a warning and no IIS plan (a real run would stop)' {
            $server = New-TestFolder 'srv'
            $result = Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') -WhatIf
            $result.SiteMissing | Should Be $true
            ($result.PlannedActions -join "`n") | Should Match 'IIS: no plan while the site'
            Test-Path -LiteralPath (Join-Path $server 'app') | Should Be $false
        }
    }
}

Describe 'Install-Api install (IIS, pool and HTTP mocked)' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-ApiSiteHttpsBindingCount { 1 }
    Mock Get-ApiPoolEnvironmentVariables { @() }
    Mock Get-ApiTuningState { $null }
    Mock Invoke-ApiTuningStep { }
    Mock Restart-ApiAppPool { }
    Mock Start-Sleep { }

    It 'creates the Production file, keeps the site offline and skips health while required keys are empty' {
        Mock Invoke-WebRequest { throw 'no HTTP call is expected while settings are incomplete' }
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'

        $result = Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -AppPoolName $TestPoolName -HealthBaseUrl $TestHealthBase -TargetPath $target -BackupRoot (Join-Path $server 'backups')

        $production = Read-TestJson (Join-Path $target 'appsettings.Production.json')
        $production.Codelab.Security.UploadTickets.Enabled | Should Be $true
        $production.Database.ApplyMigrationsOnStartup | Should Be $false
        Test-Path -LiteralPath (Join-Path $target $EntryAssembly) | Should Be $true
        [IO.File]::ReadAllText((Join-Path $target 'app_offline.htm')) | Should Match 'not configured'
        Test-Path -LiteralPath (Join-Path $server 'backups\install-api.log') | Should Be $true
        $result.SiteHeldOffline | Should Be $true
        $result.Health | Should Be ([int] [ApiHealthOutcome]::SkippedSettingsIncomplete)
        Assert-MockCalled Invoke-ApiTuningStep -Times 5 -Scope It
        Assert-MockCalled Restart-ApiAppPool -Times 0 -Scope It
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'keeps every server value, adds only the new keys, brings the site online and reports healthy' {
        Mock Invoke-WebRequest { [pscustomobject]@{ StatusCode = 503; Content = '' } }
        Mock Invoke-WebRequest -ParameterFilter { $Uri -eq $TestHealthUrl } { [pscustomobject]@{ StatusCode = 200; Content = 'Healthy' } }
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $settings = Get-CompleteProduction
        $settings.Notifications.Email.Smtp.Host = 'relay-kept.kit.test'
        Write-ServerProduction $target $settings
        Write-TestText (Join-Path $target 'appsettings.json') '{ "Jobs": { "WorkerCount": 2 } }'
        Write-TestText (Join-Path $target 'app_offline.htm') 'left by an earlier run with gaps'
        [IO.File]::WriteAllBytes((Join-Path $target $EntryAssembly), [byte[]] (1, 2, 3))
        Write-TestText (Join-Path $target 'old-only.txt') 'removed by the mirror'

        $result = Invoke-ApiKit -PackagePath (New-FakePackage -InAppFolder) -SiteName $TestSiteName -AppPoolName $TestPoolName -HealthBaseUrl "$TestHealthBase/" -TargetPath $target -BackupRoot (Join-Path $server 'backups')

        $base = Read-TestJson (Join-Path $target 'appsettings.json')
        $base.Jobs.WorkerCount | Should Be 2
        $base.Jobs.Enabled | Should Be $true
        $production = Read-TestJson (Join-Path $target 'appsettings.Production.json')
        $production.Notifications.Email.Smtp.Host | Should Be 'relay-kept.kit.test'
        $production.Codelab.Security.UploadTickets.Keys[0].KeyId | Should Be 'u1'
        $production.Codelab.Identity.Sso.BackChannel | Should Be 2
        $production.Codelab.Integration.Gateway.BaseUrl | Should Be ''
        @($result.Settings[1].AddedKeys) -join ',' | Should Be 'Database,Codelab:Identity,Codelab:Integration'
        Test-Path -LiteralPath (Join-Path $target 'old-only.txt') | Should Be $false
        Test-Path -LiteralPath (Join-Path $result.BackupPath 'site\old-only.txt') | Should Be $true
        Test-Path -LiteralPath (Join-Path $result.BackupPath 'site\appsettings.Production.json') | Should Be $true
        Test-Path -LiteralPath (Join-Path $target 'app_offline.htm') | Should Be $false
        $result.SettingsGaps.Count | Should Be 0
        $result.HealthUrl | Should Be $TestHealthUrl
        $result.Health | Should Be ([int] [ApiHealthOutcome]::Healthy)
        Assert-MockCalled Restart-ApiAppPool -Times 1 -Scope It -ParameterFilter { $AppPoolName -eq $TestPoolName }
        Assert-MockCalled Invoke-WebRequest -Times 1 -Scope It -ParameterFilter { $Uri -eq $TestHealthUrl }
    }

    It 'throws with the diagnosis and the rollback path when health never answers 200' {
        Mock Invoke-ApiHealthRequest { [pscustomobject]@{ StatusCode = 500; Body = '<html><title>HTTP Error 500.30</title></html>'; Error = $null } }
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        Write-ServerProduction $target (Get-CompleteProduction)
        [IO.File]::WriteAllBytes((Join-Path $target $EntryAssembly), [byte[]] (1, 2, 3))
        { Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -TargetPath $target -BackupRoot (Join-Path $server 'backups') } |
            Should Throw 'did not answer 200. Roll back'
        Assert-MockCalled Invoke-ApiHealthRequest -Times 12 -Scope It
    }

    It 'refuses the probe when the session overrides certificate validation' {
        Mock Invoke-ApiHealthRequest { throw 'no HTTP call is expected with a validation override' }
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        Write-ServerProduction $target (Get-CompleteProduction)
        [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
        try {
            { Invoke-ApiKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -HealthBaseUrl $TestHealthBase -TargetPath $target -BackupRoot (Join-Path $server 'backups') } |
                Should Throw 'overrides certificate validation'
        }
        finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $null }
        Assert-MockCalled Invoke-ApiHealthRequest -Times 0 -Scope It
    }
}

Describe 'Install-Api settings gaps' {
    It 'lists every operator key of the template (UploadTickets included) and nothing the package fills' {
        $expected = @(
            'Attachments:ServiceBaseUrl|Empty'
            'Auth:WebBaseUrl|Empty'
            'Codelab:Security:Cors:AllowedOrigins|Missing'
            'Codelab:Security:PublicIds:ActiveKeyId|Empty'
            'Codelab:Security:PublicIds:Keys:0:KeyId|Empty'
            'Codelab:Security:PublicIds:Keys:0:MaterialBase64|Empty'
            'Codelab:Security:RequestSigning:ActiveKeyId|Empty'
            'Codelab:Security:RequestSigning:Keys:0:KeyId|Empty'
            'Codelab:Security:RequestSigning:Keys:0:MaterialBase64|Empty'
            'Codelab:Security:ServiceAssertions:Client:KeyId|Empty'
            'Codelab:Security:ServiceAssertions:Client:PrivateKeyPem|Empty'
            'Codelab:Security:Tickets:ActiveKeyId|Empty'
            'Codelab:Security:Tickets:Keys:0:KeyId|Empty'
            'Codelab:Security:Tickets:Keys:0:MaterialBase64|Empty'
            'Codelab:Security:Uploads:ActiveKeyId|Empty'
            'Codelab:Security:Uploads:Keys:0:KeyId|Empty'
            'Codelab:Security:Uploads:Keys:0:MaterialBase64|Empty'
            'Codelab:Security:UploadTickets:ActiveKeyId|Empty'
            'Codelab:Security:UploadTickets:Keys:0:KeyId|Empty'
            'Codelab:Security:UploadTickets:Keys:0:MaterialBase64|Empty'
            'ConnectionStrings:Starter|Empty'
            'Identity:BreakGlass:Password|Empty'
            'Identity:Jwt:SigningKey|Empty'
            'Notifications:Email:FromAddress|Empty'
            'Notifications:Email:Smtp:Host|Empty'
            'Observability:UserIdHash:Key|Empty'
            'Portal:Contact:InboxEmail|Empty'
            'Users:Activation:LinkBaseUrl|Empty'
        )
        Get-TestGapText (Get-TestGaps $null) | Should BeExactly ($expected -join ',')
    }

    It 'has no gap when every required key is filled' {
        @(Get-TestGaps (Get-CompleteProduction)).Count | Should Be 0
    }

    It 'requires the upload-ticket ring to be enabled' {
        $settings = Get-CompleteProduction
        $settings.Codelab.Security.UploadTickets.Enabled = $false
        Get-TestGapText (Get-TestGaps $settings) | Should BeExactly 'Codelab:Security:UploadTickets:Enabled|NotEnabled'
    }

    It 'flags http addresses and the example-domain placeholders' {
        $settings = Get-CompleteProduction
        $settings.Auth.WebBaseUrl = 'http://portal.kit.test'
        $settings.Codelab.Security.Cors.AllowedOrigins = @('https://portal.kit.test', 'http://portal.kit.test')
        $settings.Portal.Contact.InboxEmail = 'contact@example.invalid'
        Get-TestGapText (Get-TestGaps $settings) | Should BeExactly 'Auth:WebBaseUrl|NotHttps,Codelab:Security:Cors:AllowedOrigins:1|NotHttps,Portal:Contact:InboxEmail|Placeholder'
    }

    It 'requires the SSO keys only when SSO is on, and the gateway only for the gateway back-channel' {
        $settings = Get-CompleteProduction
        $settings.Codelab['Identity'] = @{ Sso = @{ Enabled = $true; ClientId = 'starter'; Authority = 'https://idp.kit.test'; AuthorizationEndpoint = 'https://idp.kit.test/a'; TokenEndpoint = 'https://idp.kit.test/t'; RedirectUri = 'https://api.kit.test/cb' } }
        Get-TestGapText (Get-TestGaps $settings) | Should BeExactly 'Codelab:Integration:Gateway:BaseUrl|Empty'
        $settings.Codelab.Identity.Sso['BackChannel'] = 1
        @(Get-TestGaps $settings).Count | Should Be 0
        $settings.Codelab.Identity.Sso.Remove('ClientId')
        Get-TestGapText (Get-TestGaps $settings) | Should BeExactly 'Codelab:Identity:Sso:ClientId|Missing'
    }

    It 'requires the gateway when SMS goes through it' {
        $settings = Get-CompleteProduction
        $settings.Notifications['Sms'] = @{ Enabled = $true; Mode = 2 }
        Get-TestGapText (Get-TestGaps $settings) | Should BeExactly 'Codelab:Integration:Gateway:BaseUrl|Empty'
        $settings.Codelab['Integration'] = @{ Gateway = @{ BaseUrl = 'https://gateway.kit.test' } }
        @(Get-TestGaps $settings).Count | Should Be 0
    }

    It 'requires the Ops caller trust once the package carries the Ops section' {
        $settings = Get-CompleteProduction
        $settings.Codelab['Ops'] = @{ CertificateWarningDays = 30 }
        Get-TestGapText (Get-TestGaps $settings) | Should BeExactly 'Codelab:Ops:TrustedCallerKeyId|Missing,Codelab:Security:ServiceAssertions:Server:Audience|Missing,Codelab:Security:ServiceAssertions:Server:TrustedServices|Missing'
    }

    It 'flags a ring whose ActiveKeyId names no key, and key material shared by two rings' {
        $settings = Get-CompleteProduction
        $settings.Codelab.Security.PublicIds.ActiveKeyId = 'p2'
        $settings.Codelab.Security.UploadTickets.Keys[0] = $settings.Codelab.Security.Tickets.Keys[0].Clone()
        $settings.Codelab.Security.UploadTickets.Keys[0].KeyId = 'u1'
        Get-TestGapText (Get-TestGaps $settings) | Should BeExactly 'Codelab:Security:PublicIds:ActiveKeyId|ActiveKeyNotInRing,Codelab:Security:UploadTickets:Keys:0:MaterialBase64|MaterialSharedAcrossRings'
    }

    It 'accepts a value from the app pool environment (A__B), which wins over the files' {
        $settings = Get-CompleteProduction
        $settings.ConnectionStrings.Starter = ''
        $environment = ConvertTo-ApiEnvironmentLayer @([pscustomobject]@{ Name = 'ConnectionStrings__Starter'; Value = $FakeSecret })
        @(Get-TestGaps $settings $environment).Count | Should Be 0
        $emptied = ConvertTo-ApiEnvironmentLayer @([pscustomobject]@{ Name = 'Identity__Jwt__SigningKey'; Value = '' })
        Get-TestGapText (Get-TestGaps (Get-CompleteProduction) $emptied) | Should BeExactly 'Identity:Jwt:SigningKey|Empty'
    }

    It 'never carries a value in a gap' {
        $settings = Get-CompleteProduction
        $settings.Codelab.Security.UploadTickets.Keys[0] = $settings.Codelab.Security.Tickets.Keys[0].Clone()
        $settings.Codelab.Security.UploadTickets.Keys[0].KeyId = 'u1'
        $text = (Get-TestGaps $settings | ConvertTo-Json -Depth 4)
        $text | Should Match 'MaterialSharedAcrossRings'
        $text | Should Not Match 'material-tickets'
    }

    It 'refuses an invalid settings file without quoting its content' {
        $path = Join-Path (New-TestFolder 'json') 'bad.json'
        Set-Content -LiteralPath $path -Value '{ "Note": "do-not-echo", }'
        $message = ''
        try { Read-ApiJson $path | Out-Null } catch { $message = $_.Exception.Message }
        $message | Should Match 'is not valid JSON'
        $message | Should Not Match 'do-not-echo'
    }
}

Describe 'Install-Api IIS tuning plan' {
    It 'plans every pool setting and the root preload when nothing is known' {
        $targets = Get-ApiTuningTargets -SiteName $TestSiteName -AppPath '/' -AppPoolName $TestPoolName
        $plan = @(Get-ApiTuningPlan -Targets $targets -Current $null)
        (@($plan | ForEach-Object { $_.Property }) -join ',') | Should BeExactly 'startMode,processModel.idleTimeout,recycling.periodicRestart.time,processModel.loadUserProfile,applicationDefaults.preloadEnabled'
        $plan[4].Target | Should Be "IIS:\Sites\$TestSiteName"
    }

    It 'targets the child application for a child path' {
        $targets = Get-ApiTuningTargets -SiteName $TestSiteName -AppPath '/api-host' -AppPoolName $TestPoolName
        $last = $targets[$targets.Count - 1]
        $last.Target | Should Be "IIS:\Sites\$TestSiteName\api-host"
        $last.Property | Should Be 'preloadEnabled'
    }

    It 'plans nothing when IIS already matches (case-insensitive)' {
        $targets = Get-ApiTuningTargets -SiteName $TestSiteName -AppPath '/' -AppPoolName $TestPoolName
        $current = @{}
        foreach ($target in $targets) { $current[(Get-ApiTuningKey $target)] = ([string] $target.Want).ToLowerInvariant() }
        @(Get-ApiTuningPlan -Targets $targets -Current $current).Count | Should Be 0
    }
}

Describe 'Install-Api health helpers' {
    It 'builds the health URL for the site root and for a child application' {
        Get-ApiHealthUrl -BaseUrl 'https://api.example.test/' -AppPath '/' | Should Be 'https://api.example.test/health/ready'
        Get-ApiHealthUrl -BaseUrl 'https://host.example.test' -AppPath '/api-host/' | Should Be 'https://host.example.test/api-host/health/ready'
    }

    It 'tells an IIS error page, a TLS failure and an API answer apart' {
        Get-ApiHealthDiagnosis ([pscustomobject]@{ StatusCode = 500; Body = '<html>'; Error = $null }) | Should Match 'IIS answered'
        Get-ApiHealthDiagnosis ([pscustomobject]@{ StatusCode = 0; Body = ''; Error = 'trust failure' }) | Should Match 'certificate'
        Get-ApiHealthDiagnosis ([pscustomobject]@{ StatusCode = 503; Body = '{}'; Error = $null }) | Should Match 'from the API itself'
    }

    It 'stops polling at the first 200' {
        Mock Start-Sleep { }
        $script:probeCount = 0
        Mock Invoke-ApiHealthRequest { $script:probeCount++; if ($script:probeCount -ge 2) { [pscustomobject]@{ StatusCode = 200; Body = ''; Error = $null } } else { [pscustomobject]@{ StatusCode = 503; Body = ''; Error = $null } } }
        (Wait-ApiHealth -Url $TestHealthUrl).StatusCode | Should Be 200
        Assert-MockCalled Invoke-ApiHealthRequest -Times 2 -Scope It
    }
}
