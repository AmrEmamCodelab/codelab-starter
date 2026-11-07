# Pester 3.4 tests for deploy/kits/Install-Web.ps1 (Windows PowerShell 5.1).
# No IIS change, no admin rights, no network: IIS state, prerequisites and HTTP are mocked, and every folder the kit
# touches is under $TestDrive. Fixtures are built here, in the test's own temp folders.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Web.Tests.ps1 -EnableExit"
Set-StrictMode -Version Latest

$KitFolder = Split-Path -Parent $PSScriptRoot
$KitScript = Join-Path $KitFolder 'Install-Web.ps1'
$WebTemplatePath = Join-Path $KitFolder 'templates\web\web.config'
$HeaderDataPath = Join-Path $KitFolder 'templates\web\security-headers.psd1'
$TestSiteName = 'Kit Test Site'
$TestPoolName = 'KitTestPool'
$TestBaseUrl = 'https://portal.example.test'
$TestBuildId = 'build-1a2b3c'

# The kit declares its own parameters (PackagePath, SiteName, ...) here; test variables use other names.
. $KitScript

$HeaderTable = Import-PowerShellDataFile -LiteralPath $HeaderDataPath
$TemplateText = [IO.File]::ReadAllText($WebTemplatePath)

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

# A fake SPA build: index.html, a hashed bundle, an asset and the development config.json the build ships.
function New-FakeBuild([string] $Folder, [string] $BundleText = 'console.log(1);') {
    Write-TestText (Join-Path $Folder 'index.html') '<!doctype html><html><body><app-root></app-root></body></html>'
    Write-TestText (Join-Path $Folder 'main-ABCD1234.js') $BundleText
    Write-TestText (Join-Path $Folder 'assets\i18n\en.json') '{ "title": "Starter" }'
    Write-TestText (Join-Path $Folder 'config.json') (@'
{
  "apiBaseUrl": "http://localhost:5180",
  "origins": { "api": ["http://localhost:5180"], "attachment": ["http://localhost:5185"] },
  "security": { "protocolPrefix": "STARTER" },
  "defaultLanguage": "ar",
  "showcase": false
}
'@)
}

function New-FakePackage([switch] $InWebFolder, [string] $ManifestBuildId) {
    $root = New-TestFolder 'package'
    $web = if ($InWebFolder) { Join-Path $root 'web' } else { $root }
    New-FakeBuild $web
    if ($ManifestBuildId) { Write-TestText (Join-Path $root 'manifest.json') ('{{ "buildId": "{0}" }}' -f $ManifestBuildId) }
    return $root
}

# A server config.json an operator has filled (same-origin API, https origins).
function Write-CompleteServerConfig([string] $TargetPath) {
    Write-TestText (Join-Path $TargetPath 'config.json') (@'
{
  "apiBaseUrl": "/",
  "origins": { "api": ["https://portal.example.test"], "attachment": ["https://portal.example.test"] },
  "security": { "protocolPrefix": "STARTER" },
  "defaultLanguage": "en"
}
'@)
}

function Get-FolderSnapshot([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '<missing>' }
    $lines = Get-ChildItem -LiteralPath $Path -Recurse -Force | Sort-Object FullName | ForEach-Object {
        if ($_.PSIsContainer) { $_.FullName } else { '{0} {1}' -f $_.FullName, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    return (@($lines) -join "`n")
}

function Get-HeaderValue([string] $WebConfigText, [string] $Name) {
    $document = New-Object Xml.XmlDocument
    $document.LoadXml($WebConfigText)
    $node = $document.SelectSingleNode("/configuration/system.webServer/httpProtocol/customHeaders/add[@name='$Name']")
    if ($null -eq $node) { return $null }
    return $node.GetAttribute('value')
}

function Get-RuleNames([string] $WebConfigText) {
    $document = New-Object Xml.XmlDocument
    $document.LoadXml($WebConfigText)
    return @($document.SelectNodes('/configuration/system.webServer/rewrite/rules/rule') | ForEach-Object { $_.GetAttribute('name') })
}

function ConvertTo-Effective([string] $Json) {
    return (Get-WebEffectiveSettings @((ConvertFrom-Json $Json)))
}

# A healthy site: every kit header on /, the stamped build-info.json, 404 on the blocked paths.
$HealthySite = {
    if ($Url -like '*/api/*') { return [pscustomobject]@{ StatusCode = 404; Headers = @{}; Body = ''; Error = $null } }
    if ($Url -like '*/build-info.json') {
        $body = [IO.File]::ReadAllText((Join-Path $script:SiteFolder 'build-info.json'))
        return [pscustomobject]@{ StatusCode = 200; Headers = @{}; Body = $body; Error = $null }
    }
    $headers = @{}
    foreach ($header in $HeaderTable.Headers) { $headers[$header.Name] = $header.Value }
    return [pscustomobject]@{ StatusCode = 200; Headers = $headers; Body = '<html></html>'; Error = $null }
}

# IIS as an existing site served from elsewhere (read-only state; the apply step only creates the folder).
$IisStateWithSite = {
    [pscustomobject]@{
        Known = $true; PhysicalPathExists = (Test-Path -LiteralPath $PhysicalPath); Pool = $null
        Site = [pscustomobject]@{ PhysicalPath = 'C:\old-site'; ApplicationPool = 'OldPool' }; Application = $null
    }
}
$IisApplyCreatesFolder = { if ($Step.Action -eq 'CreateFolder') { New-Item -ItemType Directory -Path $Desired.PhysicalPath -Force | Out-Null } }
$PrerequisiteFactsPresent = { [pscustomobject]@{ IsAdministrator = $true; IisPresent = $true; WebAdministrationPresent = $true; UrlRewritePresent = $true } }

Describe 'Install-Web parameters' {
    Mock Get-WebPrerequisiteFacts $PrerequisiteFactsPresent
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }

    It 'refuses a missing -PackagePath' {
        { Invoke-WebKit -SiteName $TestSiteName -WhatIf } | Should Throw '-PackagePath is required'
    }

    It 'refuses a missing -SiteName' {
        { Invoke-WebKit -PackagePath (New-FakePackage) -WhatIf } | Should Throw '-SiteName is required'
    }

    It 'refuses an app pool name with invalid characters' {
        { Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -AppPoolName 'pool/x' -WhatIf } | Should Throw 'is not a valid app pool name'
    }

    It 'refuses a relative -TargetPath' {
        { Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -TargetPath 'site' -WhatIf } | Should Throw 'must be a fully qualified folder'
    }

    It 'refuses a -BackupRoot inside -TargetPath' {
        $target = Join-Path $TestDrive 'site-overlap'
        { Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -TargetPath $target -BackupRoot (Join-Path $target 'backups') -WhatIf } |
            Should Throw 'overlap'
    }

    It 'refuses a build id with characters outside the allowed set' {
        { Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -BuildId 'a b' -WhatIf } | Should Throw 'is not a build id'
    }

    It 'refuses a real run without -HealthBaseUrl, and an http or credentialed health URL' {
        $server = New-TestFolder 'srv'
        $common = @{ PackagePath = (New-FakePackage); SiteName = $TestSiteName; TargetPath = (Join-Path $server 'site'); BackupRoot = (Join-Path $server 'backups') }
        { Invoke-WebKit @common } | Should Throw '-HealthBaseUrl is required'
        { Invoke-WebKit @common -HealthBaseUrl 'http://portal.example.test' } | Should Throw 'must be an absolute https URL'
        { Invoke-WebKit @common -HealthBaseUrl 'https://operator@portal.example.test' } | Should Throw 'must be an absolute https URL'
        Test-Path -LiteralPath (Join-Path $server 'site') | Should Be $false
    }

    It 'refuses a package without index.html' {
        $package = New-TestFolder 'empty-package'
        { Invoke-WebKit -PackagePath $package -SiteName $TestSiteName -WhatIf } | Should Throw 'No index.html'
    }

    It 'refuses a real run when prerequisites are missing, and changes nothing' {
        Mock Get-WebPrerequisiteFacts { [pscustomobject]@{ IsAdministrator = $true; IisPresent = $true; WebAdministrationPresent = $true; UrlRewritePresent = $false } }
        $server = New-TestFolder 'srv'
        { Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -TargetPath (Join-Path $server 'site') -BackupRoot (Join-Path $server 'backups') -HealthBaseUrl $TestBaseUrl } |
            Should Throw 'Prerequisites are missing'
        Get-FolderSnapshot $server | Should BeExactly ''
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
    }

    It 'exits non-zero with the reason when the script runs without -PackagePath' {
        # The dot-sourced kit sets ErrorActionPreference Stop here; the child's stderr must stay plain output.
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $KitScript -SiteName $TestSiteName 2>&1 | Out-String
        $LASTEXITCODE | Should Not Be 0
        $output | Should Match 'PackagePath is required'
    }
}

Describe 'Install-Web prerequisites' {
    It 'needs IIS, WebAdministration and URL Rewrite, and elevation only for a real run' {
        $facts = [pscustomobject]@{ IsAdministrator = $false; IisPresent = $true; WebAdministrationPresent = $true; UrlRewritePresent = $true }
        (Test-WebPrerequisites -Facts $facts -RequireAdministrator $false).Passed | Should Be $true
        (Test-WebPrerequisites -Facts $facts -RequireAdministrator $true).Passed | Should Be $false
        $facts.UrlRewritePresent = $false
        $check = Test-WebPrerequisites -Facts $facts -RequireAdministrator $false
        $check.Passed | Should Be $false
        (@($check.Checks | Where-Object { -not $_.Passed } | ForEach-Object { $_.Name }) -join ',') | Should BeExactly 'UrlRewrite'
    }
}

Describe 'Install-Web build id' {
    It 'takes -BuildId first' {
        $package = New-FakePackage -InWebFolder -ManifestBuildId 'from-manifest'
        $build = Resolve-WebBuildId -Requested 'given' -WebFolder (Resolve-WebPackage $package)
        $build.Id | Should BeExactly 'given'
        $build.Source | Should Be ([WebBuildIdSource]::Parameter)
    }

    It 'reads the kit manifest beside web\' {
        $package = New-FakePackage -InWebFolder -ManifestBuildId 'abc1234'
        $build = Resolve-WebBuildId -Requested '' -WebFolder (Resolve-WebPackage $package)
        $build.Id | Should BeExactly 'abc1234'
        $build.Source | Should Be ([WebBuildIdSource]::Manifest)
    }

    It 'falls back to a content digest that is stable, ignores the kit-owned files and changes with the build' {
        $first = New-TestFolder 'build-a'
        $second = New-TestFolder 'build-b'
        $third = New-TestFolder 'build-c'
        New-FakeBuild $first
        New-FakeBuild $second
        New-FakeBuild $third -BundleText 'console.log(2);'
        Write-TestText (Join-Path $second 'config.json') '{ "apiBaseUrl": "/other" }'
        $id = Get-WebContentBuildId $first
        $id | Should Match '^[0-9a-f]{12}$'
        Get-WebContentBuildId $second | Should BeExactly $id
        Get-WebContentBuildId $third | Should Not Be $id
        (Resolve-WebBuildId -Requested '' -WebFolder $first).Source | Should Be ([WebBuildIdSource]::Content)
    }
}

Describe 'Install-Web config.json' {
    It 'builds the merge source from the package file with the environment values emptied' {
        $build = New-TestFolder 'build'
        New-FakeBuild $build
        $path = New-WebConfigMergeTemplate -PackageConfigPath (Join-Path $build 'config.json') -KitTemplatePath (Join-Path $KitFolder 'templates\web\config.template.json')
        try { $merged = [IO.File]::ReadAllText($path) | ConvertFrom-Json } finally { Remove-Item -LiteralPath $path }
        $merged.apiBaseUrl | Should BeExactly ''
        @($merged.origins.api).Count | Should Be 0
        @($merged.origins.attachment).Count | Should Be 0
        $merged.security.protocolPrefix | Should BeExactly 'STARTER'
        $merged.defaultLanguage | Should BeExactly 'ar'
    }

    It 'reports every empty required key on a first install' {
        $gaps = Get-WebSettingsGaps (ConvertTo-Effective '{ "apiBaseUrl": "", "origins": { "api": [], "attachment": [] }, "security": { "protocolPrefix": "STARTER" } }')
        ($gaps -join ',') | Should BeExactly 'apiBaseUrl (empty),origins:api (empty),origins:attachment (empty)'
    }

    It 'accepts a same-origin API path and https origins' {
        $gaps = @(Get-WebSettingsGaps (ConvertTo-Effective '{ "apiBaseUrl": "/", "origins": { "api": ["https://portal.example.test"], "attachment": ["https://files.example.test"] }, "security": { "protocolPrefix": "STARTER" } }'))
        $gaps.Count | Should Be 0
    }

    It 'refuses http values, a protocol-relative API path and an empty list item' {
        $gaps = Get-WebSettingsGaps (ConvertTo-Effective '{ "apiBaseUrl": "//api.example.test", "origins": { "api": ["http://api.example.test"], "attachment": [""] }, "security": { "protocolPrefix": "" } }')
        ($gaps -join ',') | Should Match 'apiBaseUrl \(must be an https origin or a same-origin path\)'
        ($gaps -join ',') | Should Match 'origins:api:0 \(must be an https origin\)'
        ($gaps -join ',') | Should Match 'origins:attachment:0 \(empty\)'
        ($gaps -join ',') | Should Match 'security:protocolPrefix \(empty\)'
    }

    It 'lets the server value win over the merge source' {
        $effective = Get-WebEffectiveSettings @((ConvertFrom-Json '{ "apiBaseUrl": "" }'), (ConvertFrom-Json '{ "apiBaseUrl": "/" }'))
        $effective['apiBaseUrl'] | Should BeExactly '/'
    }
}

Describe 'Install-Web CSP adaptation' {
    It 'leaves the template as written for a same-origin API' {
        $effective = ConvertTo-Effective '{ "apiBaseUrl": "/", "origins": { "api": [], "attachment": [] } }'
        $resolved = Resolve-WebTemplateText -TemplateText $TemplateText -Effective $effective -HeaderData $HeaderTable
        foreach ($header in $HeaderTable.Headers) { Get-HeaderValue $resolved $header.Name | Should BeExactly $header.Value }
    }

    It 'adds cross-origin API and attachment origins to connect-src and aims the reports at the API' {
        $effective = ConvertTo-Effective '{ "apiBaseUrl": "https://api.example.test/", "origins": { "api": ["https://api.example.test"], "attachment": ["https://files.example.test:8443"] } }'
        $resolved = Resolve-WebTemplateText -TemplateText $TemplateText -Effective $effective -HeaderData $HeaderTable
        $csp = Get-HeaderValue $resolved 'Content-Security-Policy-Report-Only'
        $csp | Should Match "; connect-src 'self' https://api\.example\.test https://files\.example\.test:8443;"
        $csp | Should Match '; report-uri https://api\.example\.test/api/security/csp-reports; report-to csp-endpoint$'
        $csp | Should Match "^default-src 'self'; script-src 'self' 'sha256-"
        Get-HeaderValue $resolved 'Reporting-Endpoints' | Should BeExactly 'csp-endpoint="https://api.example.test/api/security/csp-reports"'
    }

    It 'never adds an http origin to the policy' {
        $value = Update-WebCspValue -Value "default-src 'self'; connect-src 'self'" -Origins @(Get-WebCrossOrigins -Effective (ConvertTo-Effective '{ "apiBaseUrl": "http://api.example.test" }') -SiteOrigin '') -ReportUrl '/r'
        $value | Should BeExactly "default-src 'self'; connect-src 'self'"
    }

    It 'leaves out the site''s own origin' {
        $effective = ConvertTo-Effective '{ "apiBaseUrl": "/", "origins": { "api": ["https://PORTAL.example.test"], "attachment": ["https://files.example.test"] } }'
        (@(Get-WebCrossOrigins -Effective $effective -SiteOrigin 'https://portal.example.test') -join ',') | Should BeExactly 'https://files.example.test'
    }
}

Describe 'Install-Web web.config merge' {
    $templateDocument = New-Object Xml.XmlDocument
    $templateDocument.PreserveWhitespace = $true
    $templateDocument.LoadXml($TemplateText)

    It 'keeps a server file that already has every element byte for byte' {
        $merge = Merge-WebConfigText -TemplateText $TemplateText -ServerText $TemplateText
        $merge.Changed | Should Be $false
        $merge.Text | Should BeExactly $TemplateText
        $merge.Added.Count | Should Be 0
        $merge.Kept.Count | Should Be 0
    }

    It 'adds the edge block to an older server file above the SPA fallback' {
        $server = $TemplateText -replace '(?s)\s*<rule name="Block internal and ops API".*?</rule>', ''
        (Get-RuleNames $server) -join ',' | Should BeExactly 'SPA fallback'
        $merge = Merge-WebConfigText -TemplateText $TemplateText -ServerText $server
        $merge.Changed | Should Be $true
        ($merge.Added -join ',') | Should BeExactly "configuration/system.webServer/rewrite/rules/rule[@name='Block internal and ops API']"
        (Get-RuleNames $merge.Text) -join ',' | Should BeExactly 'Block internal and ops API,SPA fallback'
        { Assert-WebEdgeRuleOrder $merge.Text } | Should Not Throw
    }

    It 'keeps a server header value and reports the difference' {
        $server = $TemplateText.Replace('name="Cross-Origin-Opener-Policy" value="same-origin"', 'name="Cross-Origin-Opener-Policy" value="unsafe-none"')
        $merge = Merge-WebConfigText -TemplateText $TemplateText -ServerText $server
        $merge.Changed | Should Be $false
        ($merge.Kept -join ',') | Should BeExactly "configuration/system.webServer/httpProtocol/customHeaders/add[@name='Cross-Origin-Opener-Policy']"
        Get-HeaderValue $merge.Text 'Cross-Origin-Opener-Policy' | Should BeExactly 'unsafe-none'
    }

    It 'adds a missing header after its template neighbour and keeps a server-only header' {
        $server = $TemplateText.Replace('<add name="Referrer-Policy" value="same-origin" />', '<add name="X-Server-Local" value="kept" />')
        $merge = Merge-WebConfigText -TemplateText $TemplateText -ServerText $server
        ($merge.Added -join ',') | Should BeExactly "configuration/system.webServer/httpProtocol/customHeaders/add[@name='Referrer-Policy']"
        Get-HeaderValue $merge.Text 'Referrer-Policy' | Should BeExactly 'same-origin'
        Get-HeaderValue $merge.Text 'X-Server-Local' | Should BeExactly 'kept'
        $merge.Text | Should Match '(?s)name="X-Content-Type-Options".*name="Referrer-Policy".*name="X-Server-Local"'
    }

    It 'adds whole sections and missing attributes of a section the server has' {
        $server = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`r`n<configuration>`r`n  <system.webServer>`r`n    <security>`r`n      <requestFiltering allowDoubleEscaping=`"false`" />`r`n    </security>`r`n  </system.webServer>`r`n</configuration>`r`n"
        $merge = Merge-WebConfigText -TemplateText $TemplateText -ServerText $server
        $added = $merge.Added -join ','
        $added | Should Match 'configuration/system.webServer/rewrite'
        $added | Should Match 'configuration/system.webServer/httpProtocol'
        $added | Should Match 'configuration/system.webServer/security/requestFiltering@removeServerHeader'
        $added | Should Match "configuration/location\[@path='index.html'\]"
        $merge.Text | Should Match 'requestFiltering allowDoubleEscaping="false" removeServerHeader="true"'
        { Assert-WebEdgeRuleOrder $merge.Text } | Should Not Throw
        foreach ($header in $HeaderTable.Headers) { Get-HeaderValue $merge.Text $header.Name | Should BeExactly $header.Value }
    }

    It 'refuses a server file where a forwarding rule sits above the edge block' {
        $proxy = '<rule name="Proxy API" stopProcessing="true"><match url="^api/(.*)" /><action type="Rewrite" url="https://api.example.test/{R:1}" /></rule>'
        $server = $TemplateText.Replace('<rule name="Block internal and ops API"', $proxy + '<rule name="Block internal and ops API"')
        $merge = Merge-WebConfigText -TemplateText $TemplateText -ServerText $server
        { Assert-WebEdgeRuleOrder $merge.Text } | Should Throw "rule 'Proxy API' sits above"
    }

    It 'refuses a web.config without the edge block' {
        $server = $TemplateText -replace '(?s)\s*<rule name="Block internal and ops API".*?</rule>', ''
        { Assert-WebEdgeRuleOrder $server } | Should Throw 'has no'
    }

    It 'refuses a server file that is not XML, without quoting it' {
        { Merge-WebConfigText -TemplateText $TemplateText -ServerText '<configuration><secret-value-xyz>' } | Should Throw 'is not well-formed XML'
        try { Merge-WebConfigText -TemplateText $TemplateText -ServerText '<configuration><secret-value-xyz>' } catch { $_.Exception.Message | Should Not Match 'secret-value-xyz' }
    }
}

Describe 'Install-Web -WhatIf' {
    Mock Get-WebPrerequisiteFacts $PrerequisiteFactsPresent
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Invoke-WebHttpRequest { throw 'no HTTP call is expected under -WhatIf' }

    It 'changes nothing on a first install and says the build waits for the settings' {
        $server = New-TestFolder 'srv'
        $before = Get-FolderSnapshot $server
        $result = Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -AppPoolName $TestPoolName -TargetPath (Join-Path $server 'site') -BackupRoot (Join-Path $server 'backups') -WhatIf
        Get-FolderSnapshot $server | Should BeExactly $before
        $result.Preview | Should Be $true
        $actions = $result.PlannedActions -join "`n"
        $actions | Should Match 'IIS CreateFolder'
        $actions | Should Match 'IIS CreateAppPool'
        $actions | Should Match 'IIS UpdateSite'
        $actions | Should Match 'Backup: nothing deployed yet'
        $actions | Should Match 'Settings config.json: create from the package file'
        $actions | Should Match 'Stop: the new build is not deployed'
        $result.Health | Should Be ([int] [WebHealthOutcome]::SkippedSettingsIncomplete)
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
    }

    It 'leaves a complete deployment byte for byte as it was and prints every step' {
        $server = New-TestFolder 'srv'
        $site = Join-Path $server 'site'
        Write-CompleteServerConfig $site
        Write-TestText (Join-Path $site 'index.html') 'old'
        $before = Get-FolderSnapshot $server
        $result = Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -TargetPath $site -BackupRoot (Join-Path $server 'backups') -BuildId $TestBuildId -WhatIf
        Get-FolderSnapshot $server | Should BeExactly $before
        $actions = $result.PlannedActions -join "`n"
        $actions | Should Match "Back up '"
        $actions | Should Match 'Settings config.json: add new key\(s\) showcase'
        $actions | Should Match 'web.config: create from the kit template'
        $actions | Should Match 'Copy \(old hashed bundles kept\)'
        $actions | Should Match "Stamp build-info.json with build id '$TestBuildId'"
        $actions | Should Match 'Health probe skipped \(-WhatIf\)'
        $result.SettingsGaps.Count | Should Be 0
        $result.Health | Should Be ([int] [WebHealthOutcome]::SkippedPreview)
    }
}

Describe 'Install-Web install (IIS and HTTP mocked)' {
    Mock Get-WebPrerequisiteFacts $PrerequisiteFactsPresent
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction $IisApplyCreatesFolder
    Mock Start-Sleep { }

    It 'creates config.json on a first install and stops before deploying the build' {
        Mock Invoke-WebHttpRequest { throw 'no HTTP call is expected while settings are incomplete' }
        $server = New-TestFolder 'srv'
        $site = Join-Path $server 'site'
        $result = Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -TargetPath $site -BackupRoot (Join-Path $server 'backups') -HealthBaseUrl $TestBaseUrl
        $config = [IO.File]::ReadAllText((Join-Path $site 'config.json')) | ConvertFrom-Json
        $config.apiBaseUrl | Should BeExactly ''
        $config.security.protocolPrefix | Should BeExactly 'STARTER'
        Test-Path -LiteralPath (Join-Path $site 'index.html') | Should Be $false
        Test-Path -LiteralPath (Join-Path $site 'web.config') | Should Be $false
        Test-Path -LiteralPath (Join-Path $site 'build-info.json') | Should Be $false
        $result.SettingsGaps.Count | Should Be 3
        $result.Health | Should Be ([int] [WebHealthOutcome]::SkippedSettingsIncomplete)
        Test-Path -LiteralPath (Join-Path $server 'backups\install-web.log') | Should Be $true
    }

    It 'deploys the build, writes web.config with the edge, stamps the build id and passes the health check' {
        Mock Invoke-WebHttpRequest $HealthySite
        $server = New-TestFolder 'srv'
        $site = Join-Path $server 'site'
        $script:SiteFolder = $site
        Write-CompleteServerConfig $site
        $result = Invoke-WebKit -PackagePath (New-FakePackage -InWebFolder -ManifestBuildId 'abc1234') -SiteName $TestSiteName -TargetPath $site -BackupRoot (Join-Path $server 'backups') -HealthBaseUrl $TestBaseUrl
        $result.Health | Should Be ([int] [WebHealthOutcome]::Healthy)
        $result.WebConfig | Should Be ([int] [WebConfigOutcome]::Created)
        Test-Path -LiteralPath (Join-Path $site 'main-ABCD1234.js') | Should Be $true
        Test-Path -LiteralPath (Join-Path $site 'assets\i18n\en.json') | Should Be $true
        $config = [IO.File]::ReadAllText((Join-Path $site 'config.json')) | ConvertFrom-Json
        $config.apiBaseUrl | Should BeExactly '/'
        $config.defaultLanguage | Should BeExactly 'en'
        $config.showcase | Should Be $false
        $webConfig = [IO.File]::ReadAllText((Join-Path $site 'web.config'))
        (Get-RuleNames $webConfig)[0] | Should BeExactly 'Block internal and ops API'
        foreach ($header in $HeaderTable.Headers) { Get-HeaderValue $webConfig $header.Name | Should BeExactly $header.Value }
        ([IO.File]::ReadAllText((Join-Path $site 'build-info.json')) | ConvertFrom-Json).buildId | Should BeExactly 'abc1234'
        $result.BuildIdSource | Should Be ([int] [WebBuildIdSource]::Manifest)
        Assert-MockCalled Invoke-WebHttpRequest -Times 1 -Scope It -ParameterFilter { $Url -eq 'https://portal.example.test/api/internal/edge-probe' }
        Assert-MockCalled Invoke-WebHttpRequest -Times 1 -Scope It -ParameterFilter { $Url -eq 'https://portal.example.test/api/ops/health' }
    }

    It 'keeps the server web.config overrides and the old hashed bundles on an update' {
        Mock Invoke-WebHttpRequest $HealthySite
        $server = New-TestFolder 'srv'
        $site = Join-Path $server 'site'
        $script:SiteFolder = $site
        Write-CompleteServerConfig $site
        $local = $TemplateText.Replace('<add name="X-Frame-Options" value="DENY" />', '<add name="X-Frame-Options" value="SAMEORIGIN" />')
        Write-TestText (Join-Path $site 'web.config') $local
        Write-TestText (Join-Path $site 'main-OLD12345.js') 'old bundle'
        $result = Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -TargetPath $site -BackupRoot (Join-Path $server 'backups') -HealthBaseUrl $TestBaseUrl -BuildId $TestBuildId
        $result.WebConfig | Should Be ([int] [WebConfigOutcome]::Unchanged)
        [IO.File]::ReadAllText((Join-Path $site 'web.config')) | Should BeExactly $local
        ($result.WebConfigKept -join ',') | Should Match 'X-Frame-Options'
        Test-Path -LiteralPath (Join-Path $site 'main-OLD12345.js') | Should Be $true
        Test-Path -LiteralPath (Join-Path $result.BackupPath 'site\web.config') | Should Be $true
        $result.Health | Should Be ([int] [WebHealthOutcome]::Healthy)
    }

    It 'fails the health check with a rollback hint when the edge block does not answer 404' {
        Mock Invoke-WebHttpRequest {
            if ($Url -like '*/api/ops/*') { return [pscustomobject]@{ StatusCode = 200; Headers = @{}; Body = '{}'; Error = $null } }
            & $HealthySite
        }
        $server = New-TestFolder 'srv'
        $site = Join-Path $server 'site'
        $script:SiteFolder = $site
        Write-CompleteServerConfig $site
        Write-TestText (Join-Path $site 'index.html') 'old'
        { Invoke-WebKit -PackagePath (New-FakePackage) -SiteName $TestSiteName -TargetPath $site -BackupRoot (Join-Path $server 'backups') -HealthBaseUrl $TestBaseUrl } |
            Should Throw 'Roll back'
    }
}

Describe 'Install-Web health findings' {
    $headers = @{}
    foreach ($header in $HeaderTable.Headers) { $headers[$header.Name] = $header.Value }
    $okRoot = [pscustomobject]@{ StatusCode = 200; Headers = $headers; Body = ''; Error = $null }
    $okInfo = [pscustomobject]@{ StatusCode = 200; Headers = @{}; Body = '{ "buildId": "b1" }'; Error = $null }
    $blocked = @([pscustomobject]@{ Path = '/api/internal/edge-probe'; StatusCode = 404 }, [pscustomobject]@{ Path = '/api/ops/health'; StatusCode = 404 })

    It 'passes a healthy site' {
        $findings = Get-WebHealthFindings -Root $okRoot -BuildInfo $okInfo -Blocked $blocked -ExpectedBuildId 'b1' -HeaderData $HeaderTable
        $findings.Failures.Count | Should Be 0
        $findings.Warnings.Count | Should Be 0
    }

    It 'fails a missing header, a wrong build id and an open blocked path, and warns on a different header value' {
        $partial = @{} + $headers
        $partial.Remove('Strict-Transport-Security')
        $partial['Referrer-Policy'] = 'no-referrer'
        $root = [pscustomobject]@{ StatusCode = 200; Headers = $partial; Body = ''; Error = $null }
        $open = @([pscustomobject]@{ Path = '/api/internal/edge-probe'; StatusCode = 200 })
        $findings = Get-WebHealthFindings -Root $root -BuildInfo $okInfo -Blocked $open -ExpectedBuildId 'b2' -HeaderData $HeaderTable
        $text = $findings.Failures -join "`n"
        $text | Should Match 'no Strict-Transport-Security header'
        $text | Should Match "does not carry build id 'b2'"
        $text | Should Match 'GET /api/internal/edge-probe answered 200, expected 404'
        ($findings.Warnings -join "`n") | Should Match 'Referrer-Policy differs'
    }

    It 'reports a TLS failure as such' {
        $root = [pscustomobject]@{ StatusCode = 0; Headers = @{}; Body = ''; Error = 'TLS validation failed (x)' }
        $findings = Get-WebHealthFindings -Root $root -BuildInfo $okInfo -Blocked $blocked -ExpectedBuildId 'b1' -HeaderData $HeaderTable
        ($findings.Failures -join "`n") | Should Match 'GET / answered 0 \(TLS validation failed'
    }

    It 'builds probe URLs from the base without a double slash' {
        Join-WebUrl 'https://portal.example.test/' '/api/ops/health' | Should BeExactly 'https://portal.example.test/api/ops/health'
    }
}
