# Pester 3.4 tests for deploy/kits/Install-Ops.ps1 (Windows PowerShell 5.1).
# No IIS change, no admin rights, no network: IIS state, prerequisites, the authentication facts and changes and
# HTTP are mocked, and every folder the kit touches is under $TestDrive.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Ops.Tests.ps1 -EnableExit"
Set-StrictMode -Version Latest

$KitFolder = Split-Path -Parent $PSScriptRoot
$KitScript = Join-Path $KitFolder 'Install-Ops.ps1'
$TemplatePath = Join-Path $KitFolder 'templates\ops\appsettings.template.json'
$RequirementsPath = Join-Path $KitFolder 'templates\ops\required-settings.json'
$EntryAssembly = 'Starter.OpsConsole.Fixture.dll'
$TestSiteName = 'Kit Test Site'
$TestBaseUrl = 'https://app.example.test'
$ApiHealthUrl = 'https://app.example.test/ops-api/health/ready'
$WebUrl = 'https://app.example.test/ops/'
$OpsIndexHtml = '<!doctype html><html><head><base href="/ops/"></head><body><app-root></app-root></body></html>'

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

# A fake Ops Console package: api\ (web.config naming the entry DLL, the DLL, appsettings.json) and web\ (index.html).
function New-FakeOpsPackage([string] $IndexHtml = $OpsIndexHtml) {
    $root = New-TestFolder 'package'
    $api = Join-Path $root 'api'
    Write-TestText (Join-Path $api 'web.config') ('<configuration><location path="."><system.webServer><aspNetCore processPath="dotnet" arguments=".\{0}" /></system.webServer></location></configuration>' -f $EntryAssembly)
    [IO.File]::WriteAllBytes((Join-Path $api $EntryAssembly), [byte[]] (77, 90))
    Write-TestText (Join-Path $api 'appsettings.json') '{ "Logging": { "LogLevel": { "Default": "Information" } }, "AllowedHosts": "*" }'
    Write-TestText (Join-Path $root 'web\index.html') $IndexHtml
    Write-TestText (Join-Path $root 'web\main.js') 'console.log(1);'
    return $root
}

# A complete server Production file (placeholder values only; never key material).
function Write-CompleteProduction([string] $TargetPath) {
    $production = @'
{
  "Codelab": {
    "OpsConsole": {
      "Api": { "BaseUrl": "https://api.example.test" },
      "OperatorGroup": "EXAMPLE\\ops-operators",
      "Seq": { "BaseUrl": "https://logs.example.test", "ApiKey": "fixture-not-a-key" }
    },
    "Security": {
      "ServiceAssertions": {
        "Client": { "ServiceName": "starter-ops-console", "Audience": "starter-api", "KeyId": "fixture-s2s-key", "PrivateKeyPem": "fixture-not-key-material" }
      }
    }
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

function New-ServerPaths {
    $server = New-TestFolder 'srv'
    return [pscustomobject]@{
        Root = $server; Api = (Join-Path $server 'ops-api'); Web = (Join-Path $server 'ops-web'); Backups = (Join-Path $server 'backups')
    }
}

function Invoke-TestKit([object] $Paths, [string] $PackagePath, [switch] $WhatIf) {
    return Invoke-OpsKit -PackagePath $PackagePath -SiteName $TestSiteName -ApiTargetPath $Paths.Api -WebTargetPath $Paths.Web -BackupRoot $Paths.Backups -HealthBaseUrl $TestBaseUrl -WhatIf:$WhatIf
}

# IIS as an existing site without the child applications or pools (read-only state; the apply step is mocked).
$IisStateWithSite = {
    [pscustomobject]@{
        Known = $true; PhysicalPathExists = $false; Pool = $null
        Site = [pscustomobject]@{ PhysicalPath = 'C:\site-root'; ApplicationPool = 'SitePool' }; Application = $null
    }
}
$IisStateNoSite = { [pscustomobject]@{ Known = $true; PhysicalPathExists = $false; Pool = $null; Site = $null; Application = $null } }
$PrerequisitesPassed = { [pscustomobject]@{ Passed = $true; Checks = @() } }
$AuthFactsDefault = { [pscustomobject]@{ Known = $true; WindowsModuleInstalled = $true; WindowsEnabled = $false; AnonymousEnabled = $true } }

Describe 'Install-Ops parameters and package' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-OpsAuthFacts $AuthFactsDefault
    Mock Invoke-OpsAuthAction { }

    It 'refuses a missing -PackagePath and a missing -SiteName' {
        { Invoke-OpsKit -SiteName $TestSiteName -HealthBaseUrl $TestBaseUrl -WhatIf } | Should Throw '-PackagePath is required'
        $package = New-FakeOpsPackage
        { Invoke-OpsKit -PackagePath $package -HealthBaseUrl $TestBaseUrl -WhatIf } | Should Throw '-SiteName is required'
    }

    It 'refuses the site root and the same path for both applications' {
        $package = New-FakeOpsPackage
        { Invoke-OpsKit -PackagePath $package -SiteName $TestSiteName -ApiAppPath '/' -HealthBaseUrl $TestBaseUrl -WhatIf } | Should Throw 'must name a child application'
        { Invoke-OpsKit -PackagePath $package -SiteName $TestSiteName -ApiAppPath '/ops' -WebAppPath 'ops/' -HealthBaseUrl $TestBaseUrl -WhatIf } | Should Throw 'must differ'
    }

    It 'refuses one pool for both applications (in-process hosting needs its own pool)' {
        $package = New-FakeOpsPackage
        { Invoke-OpsKit -PackagePath $package -SiteName $TestSiteName -ApiAppPoolName 'Same' -WebAppPoolName 'same' -HealthBaseUrl $TestBaseUrl -WhatIf } | Should Throw 'needs a pool of its own'
    }

    It 'refuses overlapping target folders and a backup root inside a target' {
        $package = New-FakeOpsPackage
        $paths = New-ServerPaths
        { Invoke-OpsKit -PackagePath $package -SiteName $TestSiteName -ApiTargetPath $paths.Api -WebTargetPath (Join-Path $paths.Api 'web') -BackupRoot $paths.Backups -HealthBaseUrl $TestBaseUrl -WhatIf } |
            Should Throw 'overlap'
        { Invoke-OpsKit -PackagePath $package -SiteName $TestSiteName -ApiTargetPath $paths.Api -WebTargetPath $paths.Web -BackupRoot (Join-Path $paths.Web 'b') -HealthBaseUrl $TestBaseUrl -WhatIf } |
            Should Throw 'would delete the backups'
    }

    It 'refuses a plain http -HealthBaseUrl (the probes validate TLS, there is no -k)' {
        $package = New-FakeOpsPackage
        { Invoke-OpsKit -PackagePath $package -SiteName $TestSiteName -HealthBaseUrl 'http://app.example.test' -WhatIf } | Should Throw 'must be an absolute https URL'
    }

    It 'refuses a package without web\, without the entry assembly, or built for another base href' {
        $paths = New-ServerPaths
        $noWeb = New-FakeOpsPackage
        Remove-Item -LiteralPath (Join-Path $noWeb 'web') -Recurse
        { Invoke-TestKit $paths $noWeb -WhatIf } | Should Throw 'has no web\ folder'
        $noEntry = New-FakeOpsPackage
        Remove-Item -LiteralPath (Join-Path $noEntry "api\$EntryAssembly")
        { Invoke-TestKit $paths $noEntry -WhatIf } | Should Throw "has no $EntryAssembly"
        $rootBuild = New-FakeOpsPackage -IndexHtml '<html><head><base href="/"></head></html>'
        { Invoke-TestKit $paths $rootBuild -WhatIf } | Should Throw 'does not carry <base href="/ops/">'
    }

    It 'refuses a site that does not exist (the console never creates a site)' {
        Mock -ModuleName Install-Common Get-KitIisState $IisStateNoSite
        $paths = New-ServerPaths
        { Invoke-TestKit $paths (New-FakeOpsPackage) -WhatIf } | Should Throw 'goes under an existing site'
        Test-Path -LiteralPath $paths.Api | Should Be $false
    }

    It 'refuses a real run when IIS Windows Authentication is not installed, and changes nothing' {
        Mock Get-OpsAuthFacts { [pscustomobject]@{ Known = $true; WindowsModuleInstalled = $false; WindowsEnabled = $false; AnonymousEnabled = $true } }
        $paths = New-ServerPaths
        { Invoke-TestKit $paths (New-FakeOpsPackage) } | Should Throw 'Prerequisites are missing'
        Test-Path -LiteralPath $paths.Api | Should Be $false
        Test-Path -LiteralPath $paths.Backups | Should Be $false
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
    }

    It 'exits non-zero with the reason when the script runs without -PackagePath' {
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $KitScript -SiteName $TestSiteName -HealthBaseUrl $TestBaseUrl 2>&1 | Out-String
        $LASTEXITCODE | Should Not Be 0
        $output | Should Match 'PackagePath is required'
    }
}

# The front site's web.config (Install-Web) and the same file before /ops-api joined its SPA fallback exclusion.
$FrontSiteWebConfig = [IO.File]::ReadAllText((Join-Path $KitFolder 'templates\web\web.config'))
$OlderFrontSiteWebConfig = $FrontSiteWebConfig.Replace('^/(api|attachments|ops|ops-api)(/|$)', '^/(api|attachments|ops)(/|$)')

function New-RuleConfig([string] $Rule) {
    return '<configuration><system.webServer><rewrite><rules>' + $Rule + '</rules></rewrite></system.webServer></configuration>'
}

Describe 'Install-Ops inherited rewrite rules' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-OpsAuthFacts $AuthFactsDefault
    Mock Invoke-OpsAuthAction { }

    It 'the shipped front-site fallback excludes every default child application path' {
        $OlderFrontSiteWebConfig | Should Not Be $FrontSiteWebConfig
        @(Get-OpsRewrittenAppPaths -WebConfigText $FrontSiteWebConfig -AppPaths @('/ops-api', '/ops', '/api', '/attachments')).Count | Should Be 0
    }

    It 'finds the console API rewritten by a fallback that does not exclude /ops-api, and any unlisted path' {
        (Get-OpsRewrittenAppPaths -WebConfigText $OlderFrontSiteWebConfig -AppPaths @('/ops-api', '/ops')) -join ',' | Should BeExactly '/ops-api'
        (Get-OpsRewrittenAppPaths -WebConfigText $FrontSiteWebConfig -AppPaths @('console-api/')) -join ',' | Should BeExactly '/console-api'
    }

    It 'ignores rules that do not rewrite or do not match, and honours MatchAny and a negated match' {
        $block = New-RuleConfig '<rule name="b"><match url=".*" /><action type="CustomResponse" statusCode="404" /></rule>'
        @(Get-OpsRewrittenAppPaths -WebConfigText $block -AppPaths @('/ops-api')).Count | Should Be 0
        $other = New-RuleConfig '<rule name="o"><match url="^legacy/" /><action type="Rewrite" url="/x" /></rule>'
        @(Get-OpsRewrittenAppPaths -WebConfigText $other -AppPaths @('/ops-api')).Count | Should Be 0
        $negated = New-RuleConfig '<rule name="n"><match url="^ops-api/" negate="true" /><action type="Rewrite" url="/x" /></rule>'
        @(Get-OpsRewrittenAppPaths -WebConfigText $negated -AppPaths @('/ops-api')).Count | Should Be 0
        $any = New-RuleConfig '<rule name="a"><match url=".*" /><conditions logicalGrouping="MatchAny"><add input="{URL}" pattern="^/nothing" /><add input="{URL}" pattern="^/OPS-API/" /></conditions><action type="Rewrite" url="/x" /></rule>'
        (Get-OpsRewrittenAppPaths -WebConfigText $any -AppPaths @('/ops-api')) -join ',' | Should BeExactly '/ops-api'
        $caseSensitive = New-RuleConfig '<rule name="c"><match url=".*" /><conditions><add input="{URL}" pattern="^/OPS-API/" ignoreCase="false" /></conditions><action type="Rewrite" url="/x" /></rule>'
        @(Get-OpsRewrittenAppPaths -WebConfigText $caseSensitive -AppPaths @('/ops-api')).Count | Should Be 0
    }

    It 'has nothing to check without a site web.config, and refuses one that is not XML' {
        @(Get-OpsRewrittenAppPaths -WebConfigText '' -AppPaths @('/ops-api')).Count | Should Be 0
        { Get-OpsRewrittenAppPaths -WebConfigText '<configuration>' -AppPaths @('/ops-api') } | Should Throw 'not valid XML'
    }

    It 'reads the web.config of the site folder, and nothing when the site or its file is missing' {
        $global:OpsTestSiteFolder = New-TestFolder 'site'
        Mock Get-OpsSitePhysicalPath { $global:OpsTestSiteFolder }
        Read-OpsSiteWebConfig $TestSiteName | Should BeExactly ''
        Write-TestText (Join-Path $global:OpsTestSiteFolder 'web.config') $FrontSiteWebConfig
        Read-OpsSiteWebConfig $TestSiteName | Should BeExactly $FrontSiteWebConfig
        Mock Get-OpsSitePhysicalPath { $null }
        Read-OpsSiteWebConfig $TestSiteName | Should Be $null
    }

    It 'refuses to install under a site whose fallback would rewrite the console API, and changes nothing' {
        Mock Read-OpsSiteWebConfig { $OlderFrontSiteWebConfig }
        $paths = New-ServerPaths
        { Invoke-TestKit $paths (New-FakeOpsPackage) } | Should Throw "would rewrite /ops-api"
        Test-Path -LiteralPath $paths.Api | Should Be $false
        Test-Path -LiteralPath $paths.Backups | Should Be $false
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
    }

    It 'goes on under a site whose fallback excludes both console paths' {
        Mock Read-OpsSiteWebConfig { $FrontSiteWebConfig }
        $paths = New-ServerPaths
        $result = Invoke-TestKit $paths (New-FakeOpsPackage) -WhatIf
        $result.Preview | Should Be $true
    }
}

Describe 'Install-Ops required settings and authentication plan (pure)' {
    $requirements = Read-OpsRequirements $RequirementsPath
    $template = Read-TestJson $TemplatePath

    It 'lists the API URL, AD group, Seq read key and S2S signing key of the template as gaps' {
        $gaps = @(Get-OpsSettingsGaps -Effective (Get-OpsEffectiveSettings @($template)) -Requirements $requirements) -join ','
        $gaps | Should Be 'Codelab:OpsConsole:Api:BaseUrl,Codelab:OpsConsole:OperatorGroup,Codelab:OpsConsole:Seq:ApiKey,Codelab:OpsConsole:Seq:BaseUrl,Codelab:Security:ServiceAssertions:Client:KeyId,Codelab:Security:ServiceAssertions:Client:PrivateKeyPem'
    }

    It 'flags a URL that is not https' {
        $server = '{ "Codelab": { "OpsConsole": { "Api": { "BaseUrl": "http://api.example.test" } } } }' | ConvertFrom-Json
        (@(Get-OpsSettingsGaps -Effective (Get-OpsEffectiveSettings @($template, $server)) -Requirements $requirements) -join ',') | Should Match ([regex]::Escape('Codelab:OpsConsole:Api:BaseUrl (must be an absolute https URL)'))
    }

    It 'has no gap once the server file holds every value' {
        $folder = New-TestFolder 'complete'
        Write-CompleteProduction $folder
        $server = Read-TestJson (Join-Path $folder 'appsettings.Production.json')
        @(Get-OpsSettingsGaps -Effective (Get-OpsEffectiveSettings @($template, $server)) -Requirements $requirements).Count | Should Be 0
    }

    It 'turns Windows Authentication on and Anonymous off, and plans nothing when both are in place' {
        $plan = Get-OpsAuthPlan -Facts ([pscustomobject]@{ Known = $true; WindowsModuleInstalled = $true; WindowsEnabled = $false; AnonymousEnabled = $true }) -Location 'Site/ops-api'
        (@($plan.Steps | ForEach-Object { $_.Action }) -join ',') | Should Be 'EnableWindowsAuthentication,DisableAnonymousAuthentication'
        $plan.MissingModule | Should Be $false
        $done = Get-OpsAuthPlan -Facts ([pscustomobject]@{ Known = $true; WindowsModuleInstalled = $true; WindowsEnabled = $true; AnonymousEnabled = $false }) -Location 'Site/ops-api'
        $done.Steps[0].ActionId | Should Be ([int] [OpsAuthAction]::None)
        (Get-OpsAuthPlan -Facts ([pscustomobject]@{ Known = $true; WindowsModuleInstalled = $false; WindowsEnabled = $true; AnonymousEnabled = $false }) -Location 'x').MissingModule | Should Be $true
    }

    It 'builds the application URLs and the IIS location' {
        Get-OpsAppUrl -BaseUrl 'https://app.example.test/' -AppPath 'ops-api/' -Suffix '/health/ready' | Should Be $ApiHealthUrl
        Get-OpsAppUrl -BaseUrl $TestBaseUrl -AppPath '/ops' -Suffix '/' | Should Be $WebUrl
        Get-OpsIisLocation -SiteName 'Site' -AppPath 'ops-api' | Should Be 'Site/ops-api'
        Test-OpsIndexBaseHref -Html $OpsIndexHtml -WebAppPath '/ops/' | Should Be $true
    }

    It 'runs the probe with certificate validation in force and Windows credentials, and restores the callback' {
        $permissive = [Net.Security.RemoteCertificateValidationCallback] { $true }
        [Net.ServicePointManager]::ServerCertificateValidationCallback = $permissive
        $global:OpsTestCallbackDuringCall = 'not called'
        Mock Invoke-WebRequest { $global:OpsTestCallbackDuringCall = [Net.ServicePointManager]::ServerCertificateValidationCallback; [pscustomobject]@{ StatusCode = 200; Content = 'Healthy' } }
        try {
            (Invoke-OpsHealthRequest $ApiHealthUrl).StatusCode | Should Be 200
            $global:OpsTestCallbackDuringCall | Should BeNullOrEmpty
            Assert-MockCalled Invoke-WebRequest -Times 1 -Scope It -ParameterFilter { $UseDefaultCredentials }
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

Describe 'Install-Ops -WhatIf' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-OpsAuthFacts $AuthFactsDefault
    Mock Invoke-OpsAuthAction { }
    Mock Invoke-WebRequest { throw 'no HTTP call is expected under -WhatIf' }
    Mock Start-Sleep { }

    It 'changes nothing on a first install, prints both applications, the auth change and the gaps' {
        $paths = New-ServerPaths
        $before = Get-FolderSnapshot $paths.Root

        $result = Invoke-TestKit $paths (New-FakeOpsPackage) -WhatIf

        Get-FolderSnapshot $paths.Root | Should BeExactly $before
        $actions = $result.PlannedActions -join "`n"
        $actions | Should Match 'IIS CreateApplication IIS:\\Sites\\Kit Test Site\\ops-api'
        $actions | Should Match 'IIS CreateApplication IIS:\\Sites\\Kit Test Site\\ops:'
        $actions | Should Match 'IIS CreateAppPool IIS:\\AppPools\\StarterOpsApi'
        $actions | Should Match 'IIS CreateAppPool IIS:\\AppPools\\StarterOpsWeb'
        $actions | Should Match 'Auth EnableWindowsAuthentication'
        $actions | Should Match 'Auth DisableAnonymousAuthentication'
        $actions | Should Match 'Settings appsettings.Production.json: create from the package template'
        $actions | Should Match 'Refuse to start'
        $actions | Should Match 'Health probes skipped \(-WhatIf\)'
        ($result.Gaps -join ',') | Should Match 'Codelab:OpsConsole:OperatorGroup'
        $result.Health | Should Be ([int] [OpsHealthOutcome]::SkippedPreview)
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
        Assert-MockCalled Invoke-OpsAuthAction -Times 0 -Scope It
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'previews a missing Windows Authentication module without refusing' {
        Mock Get-OpsAuthFacts { [pscustomobject]@{ Known = $false; WindowsModuleInstalled = $false; WindowsEnabled = $false; AnonymousEnabled = $true } }
        $result = Invoke-TestKit (New-ServerPaths) (New-FakeOpsPackage) -WhatIf
        $result.PrerequisitesPassed | Should Be $false
    }
}

Describe 'Install-Ops install (IIS, auth and HTTP mocked)' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Get-OpsAuthFacts $AuthFactsDefault
    Mock Invoke-OpsAuthAction { }
    Mock Start-Sleep { }

    It 'creates both applications and the settings, keeps the API offline with the gap list and skips health' {
        Mock Invoke-WebRequest { throw 'no HTTP call is expected while gaps remain' }
        $paths = New-ServerPaths

        $result = Invoke-TestKit $paths (New-FakeOpsPackage)

        (Read-TestJson (Join-Path $paths.Api 'appsettings.Production.json')).Codelab.Security.ServiceAssertions.Client.ServiceName | Should Be 'starter-ops-console'
        Test-Path -LiteralPath (Join-Path $paths.Api $EntryAssembly) | Should Be $true
        Test-Path -LiteralPath (Join-Path $paths.Web 'index.html') | Should Be $true
        $page = [IO.File]::ReadAllText((Join-Path $paths.Api 'app_offline.htm'))
        $page | Should Match 'Codelab:OpsConsole:Seq:ApiKey'
        $page | Should Match 'Codelab:Security:ServiceAssertions:Client:PrivateKeyPem'
        $result.StartRefused | Should Be $true
        $result.Health | Should Be ([int] [OpsHealthOutcome]::SkippedGaps)
        Test-Path -LiteralPath (Join-Path $paths.Backups 'install-ops.log') | Should Be $true
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 6 -Scope It
        Assert-MockCalled Invoke-OpsAuthAction -Times 1 -Scope It -ParameterFilter { $Step.ActionId -eq [int] [OpsAuthAction]::EnableWindowsAuthentication -and $Location -eq "$TestSiteName/ops-api" }
        Assert-MockCalled Invoke-OpsAuthAction -Times 1 -Scope It -ParameterFilter { $Step.ActionId -eq [int] [OpsAuthAction]::DisableAnonymousAuthentication }
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'keeps the server settings, backs up both folders, removes the previous gap page and reports healthy' {
        Mock Invoke-WebRequest { [pscustomobject]@{ StatusCode = 404; Content = '' } }
        Mock Invoke-WebRequest -ParameterFilter { $Uri -eq $ApiHealthUrl } { [pscustomobject]@{ StatusCode = 200; Content = 'Healthy' } }
        Mock Invoke-WebRequest -ParameterFilter { $Uri -eq $WebUrl } { [pscustomobject]@{ StatusCode = 200; Content = $OpsIndexHtml } }
        $paths = New-ServerPaths
        Write-CompleteProduction $paths.Api
        [IO.File]::WriteAllBytes((Join-Path $paths.Api $EntryAssembly), [byte[]] (1, 2, 3))
        Write-TestText (Join-Path $paths.Api 'app_offline.htm') 'gap page of a previous run'
        Write-TestText (Join-Path $paths.Web 'old-chunk.js') 'removed by the mirror'
        $productionBefore = (Get-FileHash -LiteralPath (Join-Path $paths.Api 'appsettings.Production.json')).Hash

        $result = Invoke-TestKit $paths (New-FakeOpsPackage)

        (Get-FileHash -LiteralPath (Join-Path $paths.Api 'appsettings.Production.json')).Hash | Should Be $productionBefore
        Test-Path -LiteralPath (Join-Path $paths.Api 'app_offline.htm') | Should Be $false
        Test-Path -LiteralPath (Join-Path $paths.Web 'old-chunk.js') | Should Be $false
        Test-Path -LiteralPath (Join-Path $result.WebBackupPath 'site\old-chunk.js') | Should Be $true
        Test-Path -LiteralPath (Join-Path $result.ApiBackupPath 'site') | Should Be $true
        $result.Gaps.Count | Should Be 0
        $result.Health | Should Be ([int] [OpsHealthOutcome]::Healthy)
        Assert-MockCalled Invoke-WebRequest -Times 1 -Scope It -ParameterFilter { $Uri -eq $ApiHealthUrl }
        Assert-MockCalled Invoke-WebRequest -Times 1 -Scope It -ParameterFilter { $Uri -eq $WebUrl }
    }

    It 'fails when /ops/ answers with the site''s own page instead of the console, and does not stay offline' {
        Mock Invoke-OpsHealthRequest { [pscustomobject]@{ StatusCode = 200; Body = 'Healthy'; Error = $null } }
        Mock Invoke-OpsHealthRequest -ParameterFilter { $Url -eq $WebUrl } { [pscustomobject]@{ StatusCode = 200; Body = '<html><base href="/"></html>'; Error = $null } }
        $paths = New-ServerPaths
        Write-CompleteProduction $paths.Api
        [IO.File]::WriteAllBytes((Join-Path $paths.Api $EntryAssembly), [byte[]] (1, 2, 3))
        { Invoke-TestKit $paths (New-FakeOpsPackage) } | Should Throw 'did not answer as expected. Roll back'
        Assert-MockCalled Invoke-OpsHealthRequest -Times 12 -Scope It -ParameterFilter { $Url -eq $WebUrl }
        Test-Path -LiteralPath (Join-Path $paths.Api 'app_offline.htm') | Should Be $false
    }
}

Remove-Variable -Name OpsTestCallbackDuringCall -Scope Global -ErrorAction SilentlyContinue
