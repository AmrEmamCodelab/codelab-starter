# Pester 3.4 tests for deploy/kits/Install-Common.psm1 (Should Be / Should Throw syntax; no IIS change, no admin).
# Run: powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Common.Tests.ps1 -EnableExit"
# Every test works in its own folder under %TEMP% and deletes it afterwards.
Set-StrictMode -Version Latest

$ModuleName   = 'Install-Common'
$ModulePath   = Join-Path (Split-Path -Parent $PSScriptRoot) "$ModuleName.psm1"
$FixtureRoot  = Join-Path $PSScriptRoot 'fixtures\common'
$ServerFixture   = Join-Path $FixtureRoot 'server.json'
$TemplateFixture = Join-Path $FixtureRoot 'template.json'
$ExpectedFixture = Join-Path $FixtureRoot 'expected-merged.json'
$TempPrefix   = 'codelab-kit-tests-'
$SettingsName = 'appsettings.json'
$RobocopyTestRetry = 0
$FixedTimestamp = New-Object DateTime 2026, 1, 2, 3, 4, 5
$ExpectedStamp  = '20260102-030405'
$ExpectedAddedKeys = @(
    'Logging:LogLevel:Microsoft.AspNetCore', 'Attachments:Storage:TempPath', 'Attachments:Cors:AllowCredentials',
    'Codelab:Security:Uploads:BlockedExtensions', 'Codelab:Security:Uploads:Receipts', 'EmptySection:Added', 'AllowedHosts')

Import-Module $ModulePath -Force

function New-TestFolder { $path = Join-Path ([IO.Path]::GetTempPath()) ($TempPrefix + [guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $path | Out-Null; return $path }
function Remove-TestFolder([string] $Path) { if ($Path -and (Test-Path -LiteralPath $Path)) { Remove-Item -LiteralPath $Path -Recurse -Force } }
function Get-Sha([string] $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Read-Utf8([string] $Path) { return [IO.File]::ReadAllText($Path, (New-Object Text.UTF8Encoding($false))) }
function ConvertTo-Lf([string] $Text) { return $Text.Replace("`r`n", "`n") }
function Write-Utf8([string] $Path, [string] $Text, [bool] $Bom = $false) { [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($Bom))) }
function New-Facts([bool] $Admin = $true, [object] $IisMajor = 10, [bool] $IisService = $true, [bool] $WebAdmin = $true, [bool] $Ancm = $true, [string[]] $Runtimes = @('10.0.1')) {
    return [pscustomobject]@{
        IsAdministrator = $Admin; IisMajorVersion = $IisMajor; IisServicePresent = $IisService
        WebAdministrationPresent = $WebAdmin; AspNetCoreModulePresent = $Ancm; AspNetCoreRuntimeVersions = $Runtimes
    }
}
function New-IisState([bool] $FolderExists = $true, [object] $Pool = $null, [object] $Site = $null, [object] $Application = $null, [bool] $Known = $true) {
    return [pscustomobject]@{ Known = $Known; PhysicalPathExists = $FolderExists; Pool = $Pool; Site = $Site; Application = $Application }
}
function New-PoolState([string] $Runtime = '', [string] $Pipeline = 'Integrated', [string] $Identity = 'ApplicationPoolIdentity') {
    return [pscustomobject]@{ ManagedRuntimeVersion = $Runtime; ManagedPipelineMode = $Pipeline; IdentityType = $Identity }
}
function New-AppState([string] $Path, [string] $Pool) { return [pscustomobject]@{ PhysicalPath = $Path; ApplicationPool = $Pool } }

Describe 'Merge-AppSettings' {
    BeforeEach {
        $script:work = New-TestFolder
        $script:server = Join-Path $script:work $SettingsName
        Copy-Item -LiteralPath $ServerFixture -Destination $script:server
    }
    AfterEach { Remove-TestFolder $script:work }

    It 'keeps every existing server value' {
        Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server | Out-Null
        $merged = Read-Utf8 $script:server | ConvertFrom-Json
        $original = Read-Utf8 $ServerFixture | ConvertFrom-Json
        $merged.Logging.LogLevel.Default | Should BeExactly 'Warning'
        $merged.ConnectionStrings.Starter | Should BeExactly 'set-on-server'
        $merged.Attachments.Storage.RootPath | Should BeExactly 'D:\data\attachments'
        $merged.Codelab.Security.Uploads.MaxFileBytes | Should Be 10485760
        # Compared with the fixture (Arabic text): this .ps1 stays ASCII, which Windows PowerShell 5.1 reads safely.
        $merged.Branding.NameAr | Should BeExactly $original.Branding.NameAr
        $merged.Branding.NameEn | Should BeExactly $original.Branding.NameEn
        $merged.Legacy | Should BeExactly 'flat-value'
    }

    It 'adds only the keys new in the template and reports them in order' {
        $result = Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server
        ($result.AddedKeys -join '|') | Should BeExactly ($ExpectedAddedKeys -join '|')
        $result.Written | Should Be $true
        $result.Created | Should Be $false
    }

    It 'adds a new nested section whole' {
        Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server | Out-Null
        $merged = Read-Utf8 $script:server | ConvertFrom-Json
        $merged.Codelab.Security.Uploads.Receipts.Enabled | Should Be $true
        $merged.Codelab.Security.Uploads.Receipts.LifetimeSeconds | Should Be 900
        $merged.EmptySection.Added | Should BeNullOrEmpty
        @($merged.EmptySection.PSObject.Properties).Count | Should Be 1
    }

    It 'keeps a server array whole and adds a new array key' {
        Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server | Out-Null
        $merged = Read-Utf8 $script:server | ConvertFrom-Json
        (@($merged.Codelab.Security.Uploads.AllowedExtensions) -join ',') | Should BeExactly '.pdf'
        (@($merged.Attachments.Cors.AllowedOrigins) -join ',') | Should BeExactly 'https://portal.example.test'
        (@($merged.Codelab.Security.Uploads.BlockedExtensions) -join ',') | Should BeExactly '.exe,.js'
    }

    It 'reports a shape conflict and keeps the server value' {
        $result = Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server
        ($result.Conflicts -join '|') | Should BeExactly 'Legacy'
    }

    It 'writes the expected text: server order kept, new keys last in their section, Arabic and decimals unchanged' {
        Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server | Out-Null
        (ConvertTo-Lf (Read-Utf8 $script:server)) | Should BeExactly (ConvertTo-Lf (Read-Utf8 $ExpectedFixture))
    }

    It 'is idempotent: a second run adds nothing and does not rewrite the file' {
        Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server | Out-Null
        $hash = Get-Sha $script:server
        $again = Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server
        $again.AddedKeys.Count | Should Be 0
        $again.Written | Should Be $false
        Get-Sha $script:server | Should Be $hash
    }

    It 'matches keys case-insensitively like .NET configuration' {
        Write-Utf8 $script:server "{`n  `"logging`": {`n    `"loglevel`": {`n      `"default`": `"Error`"`n    }`n  }`n}`n"
        $template = Join-Path $script:work 'template.json'
        Write-Utf8 $template '{ "Logging": { "LogLevel": { "Default": "Information" } } }'
        $result = Merge-AppSettings -TemplatePath $template -ServerPath $script:server
        $result.AddedKeys.Count | Should Be 0
        (Read-Utf8 $script:server | ConvertFrom-Json).logging.loglevel.default | Should BeExactly 'Error'
    }

    It 'writes nothing under -WhatIf but still reports the keys it would add' {
        $hash = Get-Sha $script:server
        $result = Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server -WhatIf
        Get-Sha $script:server | Should Be $hash
        $result.Written | Should Be $false
        $result.AddedKeys.Count | Should Be $ExpectedAddedKeys.Count
        @(Get-ChildItem -LiteralPath $script:work).Count | Should Be 1
    }

    It 'creates a missing server file from the template, and not under -WhatIf' {
        Remove-Item -LiteralPath $script:server
        $planned = Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server -WhatIf
        $planned.Created | Should Be $true
        Test-Path -LiteralPath $script:server | Should Be $false
        $result = Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server
        $result.Written | Should Be $true
        (Read-Utf8 $script:server | ConvertFrom-Json).AllowedHosts | Should BeExactly '*'
    }

    It 'keeps the server file''s BOM and CRLF line endings' {
        Write-Utf8 $script:server "{`r`n  `"A`": 1`r`n}`r`n" $true
        $template = Join-Path $script:work 'template.json'
        Write-Utf8 $template '{ "A": 2, "B": { "C": "x" } }'
        Merge-AppSettings -TemplatePath $template -ServerPath $script:server | Out-Null
        $bytes = [IO.File]::ReadAllBytes($script:server)
        ($bytes[0], $bytes[1], $bytes[2] -join ',') | Should Be '239,187,191'
        Read-Utf8 $script:server | Should BeExactly ("{`r`n  `"A`": 1,`r`n  `"B`": {`r`n    `"C`": `"x`"`r`n  }`r`n}`r`n")
    }

    It 'refuses a server file that is not valid JSON, without echoing its content, and writes nothing' {
        Write-Utf8 $script:server "{ `"Secret`": `"do-not-log`", // comment`n }"
        $hash = Get-Sha $script:server
        { Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server } | Should Throw 'is not valid JSON'
        try { Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $script:server } catch { $_.Exception.Message.Contains('do-not-log') | Should Be $false }
        Get-Sha $script:server | Should Be $hash
    }

    It 'refuses a missing template' {
        { Merge-AppSettings -TemplatePath (Join-Path $script:work 'missing.json') -ServerPath $script:server } | Should Throw 'does not exist'
    }
}

Describe 'Backup-Deployment' {
    BeforeEach {
        $script:work = New-TestFolder
        $script:site = Join-Path $script:work 'site'
        $script:backups = Join-Path $script:work 'backups'
        New-Item -ItemType Directory -Path (Join-Path $script:site 'logs'), (Join-Path $script:site 'bin') | Out-Null
        Copy-Item -LiteralPath $ServerFixture -Destination (Join-Path $script:site $SettingsName)
        Write-Utf8 (Join-Path $script:site 'bin\app.dll') 'v1'
        Write-Utf8 (Join-Path $script:site 'logs\app.log') 'log'
        $script:extraConfig = Join-Path $script:work 'web.config'
        Write-Utf8 $script:extraConfig '<configuration />'
    }
    AfterEach { Remove-TestFolder $script:work }

    It 'copies the site (logs excluded) and the config files to a timestamped folder and returns its path' {
        $path = Backup-Deployment -SourcePath $script:site -BackupRoot $script:backups -Name 'attachments' -ConfigPath $script:extraConfig -Timestamp $FixedTimestamp
        $path | Should Be (Join-Path $script:backups "attachments-$ExpectedStamp")
        Test-Path -LiteralPath (Join-Path $path 'site\bin\app.dll') | Should Be $true
        Test-Path -LiteralPath (Join-Path $path 'site\logs') | Should Be $false
        Test-Path -LiteralPath (Join-Path $path 'config\web.config') | Should Be $true
        $manifest = Read-Utf8 (Join-Path $path 'backup.json') | ConvertFrom-Json
        $manifest.siteCopied | Should Be $true
        @($manifest.configFiles).Count | Should Be 1
    }

    It 'is taken before the change: the backup keeps the original settings after a merge' {
        $live = Join-Path $script:site $SettingsName
        $original = Get-Sha $live
        $path = Backup-Deployment -SourcePath $script:site -BackupRoot $script:backups -Name 'attachments' -Timestamp $FixedTimestamp
        Merge-AppSettings -TemplatePath $TemplateFixture -ServerPath $live | Out-Null
        Get-Sha (Join-Path $path "site\$SettingsName") | Should Be $original
        Get-Sha $live | Should Not Be $original
    }

    It 'adds a suffix when a backup with the same timestamp exists' {
        Backup-Deployment -SourcePath $script:site -BackupRoot $script:backups -Name 'attachments' -Timestamp $FixedTimestamp | Out-Null
        $second = Backup-Deployment -SourcePath $script:site -BackupRoot $script:backups -Name 'attachments' -Timestamp $FixedTimestamp
        $second | Should Be (Join-Path $script:backups "attachments-$ExpectedStamp-2")
    }

    It 'creates nothing under -WhatIf and returns the path it would use' {
        $path = Backup-Deployment -SourcePath $script:site -BackupRoot $script:backups -Name 'attachments' -Timestamp $FixedTimestamp -WhatIf
        $path | Should Be (Join-Path $script:backups "attachments-$ExpectedStamp")
        Test-Path -LiteralPath $script:backups | Should Be $false
    }

    It 'returns nothing on a first install (no folder, no config)' {
        $path = Backup-Deployment -SourcePath (Join-Path $script:work 'absent') -BackupRoot $script:backups -Name 'attachments'
        $path | Should BeNullOrEmpty
        Test-Path -LiteralPath $script:backups | Should Be $false
    }

    It 'refuses a config file that does not exist' {
        { Backup-Deployment -SourcePath $script:site -BackupRoot $script:backups -Name 'attachments' -ConfigPath (Join-Path $script:work 'none.config') } | Should Throw 'does not exist'
    }

    It 'refuses a name that is not a plain folder name' {
        { Backup-Deployment -SourcePath $script:site -BackupRoot $script:backups -Name '..\escape' } | Should Throw
    }
}

Describe 'Copy-KitPayload' {
    BeforeEach {
        $script:work = New-TestFolder
        $script:package = Join-Path $script:work 'package'
        $script:target = Join-Path $script:work 'target'
        New-Item -ItemType Directory -Path $script:package, (Join-Path $script:target 'logs') | Out-Null
        Write-Utf8 (Join-Path $script:package 'app.dll') 'new'
        Write-Utf8 (Join-Path $script:package 'web.config') 'package-web-config'
        Write-Utf8 (Join-Path $script:package $SettingsName) '{ "From": "package" }'
        Write-Utf8 (Join-Path $script:package 'appsettings.Production.json') '{ "From": "package" }'
        Write-Utf8 (Join-Path $script:target 'app.dll') 'old'
        Write-Utf8 (Join-Path $script:target 'stale.dll') 'stale'
        Write-Utf8 (Join-Path $script:target $SettingsName) '{ "From": "server" }'
        Write-Utf8 (Join-Path $script:target 'appsettings.Production.json') '{ "From": "server" }'
        Write-Utf8 (Join-Path $script:target 'app_offline.htm') 'offline'
        Write-Utf8 (Join-Path $script:target 'logs\app.log') 'log'
    }
    AfterEach { Remove-TestFolder $script:work }

    It 'mirrors the package but keeps server settings files, app_offline.htm and logs' {
        $summary = Copy-KitPayload -SourcePath $script:package -DestinationPath $script:target
        $summary.Applied | Should Be $true
        Read-Utf8 (Join-Path $script:target 'app.dll') | Should BeExactly 'new'
        Read-Utf8 (Join-Path $script:target 'web.config') | Should BeExactly 'package-web-config'
        Test-Path -LiteralPath (Join-Path $script:target 'stale.dll') | Should Be $false
        Read-Utf8 (Join-Path $script:target $SettingsName) | Should BeExactly '{ "From": "server" }'
        Read-Utf8 (Join-Path $script:target 'appsettings.Production.json') | Should BeExactly '{ "From": "server" }'
        Test-Path -LiteralPath (Join-Path $script:target 'app_offline.htm') | Should Be $true
        Test-Path -LiteralPath (Join-Path $script:target 'logs\app.log') | Should Be $true
    }

    It 'keeps extra destination files with -NoPurge and honours extra exclusions' {
        Copy-KitPayload -SourcePath $script:package -DestinationPath $script:target -NoPurge -ExcludeFiles 'web.config' | Out-Null
        Test-Path -LiteralPath (Join-Path $script:target 'stale.dll') | Should Be $true
        Test-Path -LiteralPath (Join-Path $script:target 'web.config') | Should Be $false
    }

    It 'copies nothing under -WhatIf' {
        $summary = Copy-KitPayload -SourcePath $script:package -DestinationPath $script:target -WhatIf
        $summary.Applied | Should Be $false
        Read-Utf8 (Join-Path $script:target 'app.dll') | Should BeExactly 'old'
        Test-Path -LiteralPath (Join-Path $script:target 'stale.dll') | Should Be $true
    }

    It 'refuses a missing package folder' {
        { Copy-KitPayload -SourcePath (Join-Path $script:work 'absent') -DestinationPath $script:target } | Should Throw 'does not exist'
    }
}

Describe 'Test-KitPrerequisites' {
    It 'passes when IIS, WebAdministration, the module and the runtime are present' {
        $result = Test-KitPrerequisites -Facts (New-Facts) -RuntimeMajor 10
        $result.Passed | Should Be $true
        (@($result.Checks | ForEach-Object { $_.Name }) -join ',') | Should BeExactly 'IIS,WebAdministration,AspNetCoreModuleV2,AspNetCoreRuntime'
    }

    It 'reports a missing hosting bundle (module and runtime)' {
        $result = Test-KitPrerequisites -Facts (New-Facts -Ancm $false -Runtimes @('8.0.19', '9.0.8')) -RuntimeMajor 10
        $result.Passed | Should Be $false
        $failed = @($result.Checks | Where-Object { -not $_.Passed } | ForEach-Object { $_.Name })
        ($failed -join ',') | Should BeExactly 'AspNetCoreModuleV2,AspNetCoreRuntime'
        ($result.Checks | Where-Object { $_.Name -eq 'AspNetCoreRuntime' }).Detail | Should Match 'Hosting Bundle'
    }

    It 'reports missing IIS and WebAdministration' {
        $result = Test-KitPrerequisites -Facts (New-Facts -IisMajor $null -IisService $false -WebAdmin $false)
        $failed = @($result.Checks | Where-Object { -not $_.Passed } | ForEach-Object { $_.Name })
        ($failed -join ',') | Should BeExactly 'IIS,WebAdministration'
    }

    It 'accepts a prerelease runtime of the required major' {
        (Test-KitPrerequisites -Facts (New-Facts -Runtimes @('10.0.0-rc.2.25502.107')) -RuntimeMajor 10).Passed | Should Be $true
    }

    It 'checks elevation only with -RequireAdministrator' {
        $result = Test-KitPrerequisites -Facts (New-Facts -Admin $false) -RequireAdministrator
        $result.Passed | Should Be $false
        $result.Checks[0].Name | Should BeExactly 'Administrator'
        (Test-KitPrerequisites -Facts (New-Facts -Admin $false)).Passed | Should Be $true
    }
}

Describe 'Get-KitIisPlan' {
    $common = @{ SiteName = 'Starter'; AppPoolName = 'StarterAttachments'; PhysicalPath = 'C:\inetpub\starter\attachments' }

    It 'plans folder, pool and site for a new root site in that order' {
        $plan = Get-KitIisPlan @common -Port 8085 -CurrentState (New-IisState -FolderExists $false)
        (@($plan | ForEach-Object { "$($_.Step):$($_.Action)" }) -join ',') | Should BeExactly '1:CreateFolder,2:CreateAppPool,3:CreateSite'
        $plan[1].Target | Should BeExactly 'IIS:\AppPools\StarterAttachments'
        $plan[1].ActionId | Should Be 2
        ($plan[1].Changes -join ';') | Should Match 'managedPipelineMode'
        ($plan | ForEach-Object { $_.Applied }) -contains $true | Should Be $false
    }

    It 'refuses to create a site without a port' {
        { Get-KitIisPlan @common -CurrentState (New-IisState) } | Should Throw 'give -Port'
    }

    It 'refuses a child application under a missing site' {
        { Get-KitIisPlan @common -AppPath '/attachments' -CurrentState (New-IisState) } | Should Throw 'existing site'
    }

    It 'plans only the child application when the site and pool are in place' {
        $state = New-IisState -Pool (New-PoolState) -Site (New-AppState 'C:\inetpub\starter' 'StarterWeb')
        $plan = @(Get-KitIisPlan @common -AppPath 'attachments/' -CurrentState $state)
        $plan.Count | Should Be 1
        $plan[0].Action | Should BeExactly 'CreateApplication'
        $plan[0].Target | Should BeExactly 'IIS:\Sites\Starter\attachments'
    }

    It 'plans updates for pool and application drift with each change listed' {
        $state = New-IisState -Pool (New-PoolState -Runtime 'v4.0') -Site (New-AppState 'C:\inetpub\starter' 'StarterWeb') `
            -Application (New-AppState 'C:\old\attachments' 'StarterWeb')
        $plan = @(Get-KitIisPlan @common -AppPath '/attachments' -CurrentState $state)
        (@($plan | ForEach-Object { $_.Action }) -join ',') | Should BeExactly 'UpdateAppPool,UpdateApplication'
        ($plan[0].Changes -join ';') | Should BeExactly "managedRuntimeVersion: 'v4.0' -> ''"
        $plan[1].Changes.Count | Should Be 2
    }

    It 'plans None when everything is in place (paths compared case-insensitively, trailing slash ignored)' {
        $state = New-IisState -Pool (New-PoolState) -Site (New-AppState 'C:\INETPUB\starter\attachments\' 'StarterAttachments')
        $plan = @(Get-KitIisPlan @common -CurrentState $state)
        $plan.Count | Should Be 1
        $plan[0].Action | Should BeExactly 'None'
        $plan[0].ActionId | Should Be 0
    }

    It 'is deterministic: the same state gives the same plan' {
        $state = New-IisState -FolderExists $false
        $first = (Get-KitIisPlan @common -Port 8085 -CurrentState $state | ForEach-Object { "$($_.Step)|$($_.Action)|$($_.Target)|$($_.Changes -join ';')" }) -join "`n"
        $second = (Get-KitIisPlan @common -Port 8085 -CurrentState $state | ForEach-Object { "$($_.Step)|$($_.Action)|$($_.Target)|$($_.Changes -join ';')" }) -join "`n"
        $second | Should BeExactly $first
    }

    It 'refuses an application path that climbs out' {
        { Get-KitIisPlan @common -AppPath '/../x' -CurrentState (New-IisState) } | Should Throw 'not a valid IIS application path'
    }

    It 'previews a fresh server, without refusing, when IIS could not be read (not elevated)' {
        $plan = @(Get-KitIisPlan @common -AppPath '/attachments' -CurrentState (New-IisState -Known $false))
        (@($plan | ForEach-Object { $_.Action }) -join ',') | Should BeExactly 'CreateAppPool,CreateApplication'
        ($plan[1].Changes -join ';') | Should Match 'not elevated'
    }
}

Describe 'Set-KitIisApplication' {
    $common = @{ SiteName = 'Starter'; AppPath = '/attachments'; AppPoolName = 'StarterAttachments'; PhysicalPath = 'C:\inetpub\starter\attachments' }
    # Mock bodies run in the module's scope, so the state they return is handed over in a global (removed after each test).
    AfterEach { Remove-Variable -Name KitTestIisState -Scope Global -ErrorAction SilentlyContinue }

    It 'returns the plan and changes nothing under -WhatIf' {
        $global:KitTestIisState = New-IisState -FolderExists $false -Site (New-AppState 'C:\inetpub\starter' 'StarterWeb')
        Mock -ModuleName $ModuleName Get-KitIisState { $global:KitTestIisState }
        Mock -ModuleName $ModuleName Invoke-KitIisAction { }
        $plan = Set-KitIisApplication @common -WhatIf
        (@($plan | ForEach-Object { $_.Action }) -join ',') | Should BeExactly 'CreateFolder,CreateAppPool,CreateApplication'
        ($plan | ForEach-Object { $_.Applied }) -contains $true | Should Be $false
        Assert-MockCalled -ModuleName $ModuleName Invoke-KitIisAction -Times 0 -Exactly -Scope It
    }

    It 'applies each planned step once and marks it applied' {
        $global:KitTestIisState = New-IisState -Pool (New-PoolState -Identity 'NetworkService') -Site (New-AppState 'C:\inetpub\starter' 'StarterWeb')
        Mock -ModuleName $ModuleName Get-KitIisState { $global:KitTestIisState }
        Mock -ModuleName $ModuleName Invoke-KitIisAction { }
        $plan = @(Set-KitIisApplication @common)
        (@($plan | ForEach-Object { $_.Action }) -join ',') | Should BeExactly 'UpdateAppPool,CreateApplication'
        ($plan | ForEach-Object { $_.Applied }) -contains $false | Should Be $false
        Assert-MockCalled -ModuleName $ModuleName Invoke-KitIisAction -Times 2 -Exactly -Scope It
    }

    It 'does nothing when the plan is None' {
        $global:KitTestIisState = New-IisState -Pool (New-PoolState) -Site (New-AppState 'C:\inetpub\starter' 'StarterWeb') `
            -Application (New-AppState 'C:\inetpub\starter\attachments' 'StarterAttachments')
        Mock -ModuleName $ModuleName Get-KitIisState { $global:KitTestIisState }
        Mock -ModuleName $ModuleName Invoke-KitIisAction { }
        $plan = @(Set-KitIisApplication @common)
        $plan[0].Action | Should BeExactly 'None'
        Assert-MockCalled -ModuleName $ModuleName Invoke-KitIisAction -Times 0 -Exactly -Scope It
    }

    It 'refuses to apply when IIS could not be read, and changes nothing' {
        $global:KitTestIisState = New-IisState -Known $false
        Mock -ModuleName $ModuleName Get-KitIisState { $global:KitTestIisState }
        Mock -ModuleName $ModuleName Invoke-KitIisAction { }
        { Set-KitIisApplication @common } | Should Throw 'elevated'
        Assert-MockCalled -ModuleName $ModuleName Invoke-KitIisAction -Times 0 -Exactly -Scope It
    }
}

Describe 'Write-KitLog' {
    BeforeEach { $script:work = New-TestFolder; $script:log = Join-Path $script:work 'logs\kit.log' }
    AfterEach { Remove-TestFolder $script:work }

    It 'appends one line per call with the level, creating the folder' {
        Write-KitLog 'first step' -Level Step -LogPath $script:log
        Write-KitLog 'done' -Level Success -LogPath $script:log
        $lines = @(Get-Content -LiteralPath $script:log -Encoding UTF8)
        $lines.Count | Should Be 2
        $lines[0] | Should Match ' STEP first step$'
        $lines[1] | Should Match ' SUCCESS done$'
    }

    It 'writes no file under -WhatIf' {
        Write-KitLog 'planned' -LogPath $script:log -WhatIf
        Test-Path -LiteralPath $script:log | Should Be $false
    }
}

Remove-Module $ModuleName -Force -ErrorAction SilentlyContinue
