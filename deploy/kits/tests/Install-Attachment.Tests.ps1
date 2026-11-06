# Pester 3.4 tests for deploy/kits/Install-Attachment.ps1 (Windows PowerShell 5.1).
# No IIS change, no admin rights, no network: IIS state, prerequisites, ACL changes and HTTP are mocked, and every
# folder the kit touches is under $TestDrive.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Attachment.Tests.ps1 -EnableExit"
Set-StrictMode -Version Latest

$KitFolder = Split-Path -Parent $PSScriptRoot
$KitScript = Join-Path $KitFolder 'Install-Attachment.ps1'
$FixtureFolder = Join-Path $PSScriptRoot 'fixtures\attachment'
$TemplatePath = Join-Path $KitFolder 'templates\attachment\appsettings.Production.template.json'
$EntryAssembly = 'Codelab.Attachments.Api.dll'
$StorageRootToken = '__STORAGE_ROOT__'
$TestSiteName = 'Kit Test Site'
$TestPoolName = 'KitTestPool'

# The kit declares its own parameters (PackagePath, SiteName, ...) here; test variables use other names.
. $KitScript

function New-TestFolder([string] $Name) {
    $path = Join-Path $TestDrive ('{0}-{1}' -f $Name, [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

# A fake publish output: the entry DLL (placeholder bytes), web.config and the package appsettings.json.
function New-FakePackage([switch] $InAppFolder) {
    $root = New-TestFolder 'package'
    $app = if ($InAppFolder) { Join-Path $root 'app' } else { $root }
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $app $EntryAssembly), [byte[]] (77, 90))
    Copy-Item -LiteralPath (Join-Path $FixtureFolder 'web.config') -Destination (Join-Path $app 'web.config')
    Copy-Item -LiteralPath (Join-Path $FixtureFolder 'package-appsettings.json') -Destination (Join-Path $app 'appsettings.json')
    return $root
}

function Get-FolderSnapshot([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '<missing>' }
    $lines = Get-ChildItem -LiteralPath $Path -Recurse -Force | Sort-Object FullName | ForEach-Object {
        if ($_.PSIsContainer) { $_.FullName } else { '{0} {1}' -f $_.FullName, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    return (@($lines) -join "`n")
}

function Write-CompleteProduction([string] $TargetPath, [string] $StorageRoot) {
    $escaped = $StorageRoot.Replace('\', '\\')
    $text = [IO.File]::ReadAllText((Join-Path $FixtureFolder 'server-production-complete.json')).Replace($StorageRootToken, $escaped)
    New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $TargetPath 'appsettings.Production.json'), $text, (New-Object Text.UTF8Encoding($false)))
}

function Read-TestJson([string] $Path) {
    return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json)
}

# IIS as an existing site without the child application or pool (read-only state; the apply step is mocked).
$IisStateWithSite = {
    [pscustomobject]@{
        Known = $true; PhysicalPathExists = $false; Pool = $null
        Site = [pscustomobject]@{ PhysicalPath = 'C:\site-root'; ApplicationPool = 'SitePool' }; Application = $null
    }
}
$PrerequisitesPassed = { [pscustomobject]@{ Passed = $true; Checks = @() } }

Describe 'Install-Attachment parameters' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }

    It 'refuses a missing -PackagePath' {
        { Invoke-AttachmentKit -SiteName $TestSiteName -StorageRoot 'C:\store' -WhatIf } | Should Throw '-PackagePath is required'
    }

    It 'refuses a missing -SiteName' {
        $package = New-FakePackage
        { Invoke-AttachmentKit -PackagePath $package -StorageRoot 'C:\store' -WhatIf } | Should Throw '-SiteName is required'
    }

    It 'refuses the site root as -AppPath (the kit installs a child application)' {
        $package = New-FakePackage
        { Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -AppPath '/' -WhatIf } | Should Throw 'must name a child application'
    }

    It 'refuses an app pool name with invalid characters' {
        $package = New-FakePackage
        { Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -AppPoolName 'pool/x' -WhatIf } | Should Throw 'is not a valid app pool name'
    }

    It 'refuses a relative -TargetPath' {
        $package = New-FakePackage
        { Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath 'site' -WhatIf } | Should Throw 'must be a fully qualified folder'
    }

    It 'refuses a -BackupRoot inside -TargetPath (the mirror would delete it)' {
        $package = New-FakePackage
        $target = Join-Path $TestDrive 'target-backup'
        { Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot (Join-Path $target 'backups') -WhatIf } |
            Should Throw 'the payload mirror would delete the backups'
    }

    It 'refuses a package folder that does not exist' {
        { Invoke-AttachmentKit -PackagePath (Join-Path $TestDrive 'no-such-package') -SiteName $TestSiteName -WhatIf } | Should Throw 'does not exist'
    }

    It 'refuses a relative storage root and a storage root inside the app folder, before anything changes' {
        $package = New-FakePackage
        $target = Join-Path (New-TestFolder 'srv') 'app'
        $backup = Join-Path (New-TestFolder 'srv') 'backups'
        { Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot $backup -StorageRoot 'files' } |
            Should Throw 'must be a fully qualified folder or UNC share'
        { Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot $backup -StorageRoot (Join-Path $target 'files') } |
            Should Throw 'overlaps the app folder'
        Test-Path -LiteralPath $target | Should Be $false
        Test-Path -LiteralPath $backup | Should Be $false
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
    }

    It 'refuses a real run with no storage root anywhere' {
        $package = New-FakePackage
        $server = New-TestFolder 'srv'
        { Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') } |
            Should Throw 'No storage root'
    }

    It 'refuses a real run when prerequisites are missing, and changes nothing' {
        Mock Test-KitPrerequisites { [pscustomobject]@{ Passed = $false; Checks = @() } }
        $package = New-FakePackage
        $server = New-TestFolder 'srv'
        { Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') -StorageRoot (Join-Path $server 'store') } |
            Should Throw 'Prerequisites are missing'
        Test-Path -LiteralPath (Join-Path $server 'app') | Should Be $false
    }

    It 'exits non-zero with the reason when the script runs without -PackagePath' {
        # The dot-sourced kit sets ErrorActionPreference Stop here; the child's stderr must stay plain output.
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $KitScript -SiteName $TestSiteName 2>&1 | Out-String
        $LASTEXITCODE | Should Not Be 0
        $output | Should Match 'PackagePath is required'
    }
}

Describe 'Install-Attachment -WhatIf' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Invoke-AttachmentStorageStep { }
    Mock Invoke-WebRequest { throw 'no HTTP call is expected under -WhatIf' }
    Mock Start-Sleep { }

    It 'changes nothing on a first install and prints every planned action' {
        $package = New-FakePackage
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $store = Join-Path $server 'store'
        $before = Get-FolderSnapshot $server

        $result = Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -AppPoolName $TestPoolName -TargetPath $target -BackupRoot (Join-Path $server 'backups') -StorageRoot $store -WhatIf

        Get-FolderSnapshot $server | Should BeExactly $before
        $result.Preview | Should Be $true
        $actions = $result.PlannedActions -join "`n"
        $actions | Should Match 'IIS CreateFolder'
        $actions | Should Match 'IIS CreateAppPool'
        $actions | Should Match 'IIS CreateApplication'
        $actions | Should Match 'Backup: nothing deployed yet'
        $actions | Should Match 'Mirror '
        $actions | Should Match 'Settings appsettings.json: create from the package template'
        $actions | Should Match 'Settings appsettings.Production.json: create from the package template'
        $actions | Should Match 'Storage CreateFolder'
        $actions | Should Match ('Storage GrantModify: grant ''IIS AppPool\\{0}'' Modify' -f $TestPoolName)
        $actions | Should Match 'Health probe skipped \(-WhatIf\)'
        $result.Health | Should Be ([int] [AttachmentHealthOutcome]::SkippedPreview)
        @($result.IisPlan | Where-Object { $_.Applied }).Count | Should Be 0
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 0 -Scope It
        Assert-MockCalled Invoke-AttachmentStorageStep -Times 0 -Scope It
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'leaves an existing deployment byte for byte as it was (settings, files, no offline page, no backup, no log)' {
        $package = New-FakePackage -InAppFolder
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $store = Join-Path $server 'store'
        New-Item -ItemType Directory -Path $store | Out-Null
        Write-CompleteProduction -TargetPath $target -StorageRoot $store
        Copy-Item -LiteralPath (Join-Path $FixtureFolder 'server-appsettings.json') -Destination (Join-Path $target 'appsettings.json')
        [IO.File]::WriteAllBytes((Join-Path $target $EntryAssembly), [byte[]] (1, 2, 3))
        Set-Content -LiteralPath (Join-Path $target 'old-only.txt') -Value 'kept'
        $before = Get-FolderSnapshot $server

        $result = Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot (Join-Path $server 'backups') -WhatIf

        Get-FolderSnapshot $server | Should BeExactly $before
        $actions = $result.PlannedActions -join "`n"
        $actions | Should Match 'Take the app offline'
        $actions | Should Match "Back up '"
        $actions | Should Match 'Settings appsettings.json: add new key\(s\) Attachments, Codelab:Security:Uploads:MaxFileCount'
        $actions | Should Match 'Settings appsettings.Production.json: no new keys'
        $result.StorageRootSource | Should Be ([int] [AttachmentStorageRootSource]::Server)
        $result.SettingsGaps.Count | Should Be 0
        $result.Health | Should Be ([int] [AttachmentHealthOutcome]::SkippedPreview)
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'previews without a storage root (warns; a real run would stop)' {
        $package = New-FakePackage
        $server = New-TestFolder 'srv'
        $result = Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath (Join-Path $server 'app') -BackupRoot (Join-Path $server 'backups') -WhatIf
        $result.StorageRoot | Should BeNullOrEmpty
        @($result.StoragePlan).Count | Should Be 0
        $result.SettingsGaps -join ',' | Should Match 'Attachments:Storage:RootPath'
    }
}

Describe 'Install-Attachment install (IIS, ACL and HTTP mocked)' {
    Mock Test-KitPrerequisites $PrerequisitesPassed
    Mock -ModuleName Install-Common Get-KitIisState $IisStateWithSite
    Mock -ModuleName Install-Common Invoke-KitIisAction { }
    Mock Invoke-AttachmentStorageStep { }
    Mock Start-Sleep { }

    It 'keeps server settings, adds only new keys, creates the Production file and skips health while keys are empty' {
        Mock Invoke-WebRequest { throw 'no HTTP call is expected while settings are incomplete' }
        $package = New-FakePackage
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $store = Join-Path $server 'store'
        New-Item -ItemType Directory -Path $target | Out-Null
        Copy-Item -LiteralPath (Join-Path $FixtureFolder 'server-appsettings.json') -Destination (Join-Path $target 'appsettings.json')

        $result = Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -AppPoolName $TestPoolName -TargetPath $target -BackupRoot (Join-Path $server 'backups') -StorageRoot $store

        $base = Read-TestJson (Join-Path $target 'appsettings.json')
        $base.Logging.LogLevel.Default | Should Be 'Warning'
        $base.Codelab.Security.Uploads.MaxFileBytes | Should Be 5242880
        $base.Codelab.Security.Uploads.MaxFileCount | Should Be 10
        @($base.Attachments.Cors.AllowedOrigins).Count | Should Be 0
        $production = Read-TestJson (Join-Path $target 'appsettings.Production.json')
        $production.Attachments.Storage.RootPath | Should Be $store
        $production.Codelab.Security.ServiceAssertions.Server.Audience | Should Be 'starter-attachments'
        Test-Path -LiteralPath (Join-Path $target $EntryAssembly) | Should Be $true
        Test-Path -LiteralPath (Join-Path $target 'app_offline.htm') | Should Be $false
        Test-Path -LiteralPath (Join-Path $server 'backups\install-attachment.log') | Should Be $true
        $result.StorageRootSource | Should Be ([int] [AttachmentStorageRootSource]::Parameter)
        ($result.SettingsGaps -join ',') | Should Match 'Codelab:Security:Tickets:Keys:0:MaterialBase64'
        $result.Health | Should Be ([int] [AttachmentHealthOutcome]::SkippedSettingsIncomplete)
        Assert-MockCalled -ModuleName Install-Common Invoke-KitIisAction -Times 3 -Scope It
        Assert-MockCalled Invoke-AttachmentStorageStep -Times 1 -Scope It -ParameterFilter { $Step.ActionId -eq [int] [AttachmentStorageAction]::CreateFolder }
        Assert-MockCalled Invoke-AttachmentStorageStep -Times 1 -Scope It -ParameterFilter { $Step.ActionId -eq [int] [AttachmentStorageAction]::GrantModify -and $Identity -eq "IIS AppPool\$TestPoolName" }
        Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
    }

    It 'backs up, keeps the server Production file, mirrors the payload and reports healthy' {
        Mock Invoke-WebRequest { [pscustomobject]@{ StatusCode = 401; Content = '' } }
        Mock Invoke-WebRequest -ParameterFilter { $Uri -eq 'http://localhost:8080/attachments/health/ready' } { [pscustomobject]@{ StatusCode = 200; Content = 'Healthy' } }
        $package = New-FakePackage -InAppFolder
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $store = Join-Path $server 'store'
        New-Item -ItemType Directory -Path $store | Out-Null
        Write-CompleteProduction -TargetPath $target -StorageRoot $store
        Copy-Item -LiteralPath (Join-Path $FixtureFolder 'server-appsettings.json') -Destination (Join-Path $target 'appsettings.json')
        [IO.File]::WriteAllBytes((Join-Path $target $EntryAssembly), [byte[]] (1, 2, 3))
        Set-Content -LiteralPath (Join-Path $target 'old-only.txt') -Value 'removed by the mirror'
        $productionBefore = (Get-FileHash -LiteralPath (Join-Path $target 'appsettings.Production.json')).Hash

        $result = Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot (Join-Path $server 'backups') -StorageRoot (Join-Path $server 'ignored') -HealthBaseUrl 'http://localhost:8080/'

        (Get-FileHash -LiteralPath (Join-Path $target 'appsettings.Production.json')).Hash | Should Be $productionBefore
        Test-Path -LiteralPath (Join-Path $target 'old-only.txt') | Should Be $false
        Test-Path -LiteralPath (Join-Path $result.BackupPath 'site\old-only.txt') | Should Be $true
        [IO.File]::ReadAllBytes((Join-Path $target $EntryAssembly)).Length | Should Be 2
        Test-Path -LiteralPath (Join-Path $target 'app_offline.htm') | Should Be $false
        $result.StorageRoot | Should Be $store
        $result.StorageRootSource | Should Be ([int] [AttachmentStorageRootSource]::Server)
        $result.SettingsGaps.Count | Should Be 0
        $result.HealthUrl | Should Be 'http://localhost:8080/attachments/health/ready'
        $result.Health | Should Be ([int] [AttachmentHealthOutcome]::Healthy)
        Assert-MockCalled Invoke-WebRequest -Times 1 -Scope It -ParameterFilter { $Uri -eq 'http://localhost:8080/attachments/health/ready' }
    }

    It 'throws with the diagnosis and the rollback path when health never answers 200' {
        Mock Invoke-AttachmentHealthRequest { [pscustomobject]@{ StatusCode = 500; Body = '<html><title>HTTP Error 500.30</title></html>'; Error = $null } }
        $package = New-FakePackage
        $server = New-TestFolder 'srv'
        $target = Join-Path $server 'app'
        $store = Join-Path $server 'store'
        Write-CompleteProduction -TargetPath $target -StorageRoot $store
        { Invoke-AttachmentKit -PackagePath $package -SiteName $TestSiteName -TargetPath $target -BackupRoot (Join-Path $server 'backups') } |
            Should Throw 'did not answer 200. Roll back'
        Assert-MockCalled Invoke-AttachmentHealthRequest -Times 12 -Scope It
        Test-Path -LiteralPath (Join-Path $target 'app_offline.htm') | Should Be $false
    }
}

Describe 'Install-Attachment settings check' {
    $template = Read-TestJson $TemplatePath

    It 'lists every operator key of the template as empty (and nothing else)' {
        $gaps = @(Get-AttachmentSettingsGaps -Effective (Get-AttachmentEffectiveSettings @($template)) -RequiredPaths $script:RequiredSettingPaths)
        $expected = @(
            'Attachments:Cors:AllowedOrigins'
            'Attachments:Storage:RootPath'
            'Codelab:Security:ServiceAssertions:Server:TrustedServices:0:KeyId'
            'Codelab:Security:ServiceAssertions:Server:TrustedServices:0:PublicKeyPem'
            'Codelab:Security:Tickets:ActiveKeyId'
            'Codelab:Security:Tickets:Keys:0:KeyId'
            'Codelab:Security:Tickets:Keys:0:MaterialBase64'
            'Codelab:Security:Uploads:ActiveKeyId'
            'Codelab:Security:Uploads:Keys:0:KeyId'
            'Codelab:Security:Uploads:Keys:0:MaterialBase64'
        ) | Sort-Object
        ($gaps -join ',') | Should BeExactly ($expected -join ',')
    }

    It 'treats a later layer as the override, key by key and case-insensitively' {
        $low = '{ "Attachments": { "Storage": { "RootPath": "" } } }' | ConvertFrom-Json
        $high = '{ "attachments": { "storage": { "rootpath": "D:\\store" } } }' | ConvertFrom-Json
        $gaps = @(Get-AttachmentSettingsGaps -Effective (Get-AttachmentEffectiveSettings @($low, $high)) -RequiredPaths @('Attachments:Storage:RootPath'))
        $gaps.Count | Should Be 0
    }

    It 'keeps a lower layer array when a later layer has an empty one (as .NET configuration does)' {
        $low = '{ "Attachments": { "Cors": { "AllowedOrigins": [ "https://portal.example.test" ] } } }' | ConvertFrom-Json
        $high = '{ "Attachments": { "Cors": { "AllowedOrigins": [] } } }' | ConvertFrom-Json
        $gaps = @(Get-AttachmentSettingsGaps -Effective (Get-AttachmentEffectiveSettings @($low, $high)) -RequiredPaths @('Attachments:Cors:AllowedOrigins'))
        $gaps.Count | Should Be 0
    }

    It 'reports a missing section by its path' {
        $gaps = @(Get-AttachmentSettingsGaps -Effective (Get-AttachmentEffectiveSettings @(('{}' | ConvertFrom-Json))) -RequiredPaths @('Codelab:Security:Tickets:Keys'))
        ($gaps -join ',') | Should BeExactly 'Codelab:Security:Tickets:Keys'
    }

    It 'ignores missing layers' {
        $effective = Get-AttachmentEffectiveSettings @($null, $template, $null)
        Get-AttachmentSettingValue -Effective $effective -Path 'Codelab:Security:ServiceAssertions:Server:Audience' | Should Be 'starter-attachments'
        Get-AttachmentSettingValue -Effective $effective -Path 'Attachments:Cors:AllowedOrigins' | Should BeNullOrEmpty
    }

    It 'refuses an invalid settings file without quoting its content' {
        $path = Join-Path (New-TestFolder 'json') 'bad.json'
        Set-Content -LiteralPath $path -Value '{ "Note": "do-not-echo", }'
        $message = ''
        try { Read-AttachmentJson $path | Out-Null } catch { $message = $_.Exception.Message }
        $message | Should Match 'is not valid JSON'
        $message | Should Not Match 'do-not-echo'
    }

    It 'fills only the storage root in the per-run Production template' {
        $path = New-AttachmentProductionTemplate -TemplatePath $TemplatePath -StorageRoot 'E:\store'
        try {
            $json = Read-TestJson $path
            $json.Attachments.Storage.RootPath | Should Be 'E:\store'
            $json.Codelab.Security.Tickets.ActiveKeyId | Should Be ''
        }
        finally { Remove-Item -LiteralPath $path -Force }
    }
}

Describe 'Install-Attachment storage root' {
    It 'uses -StorageRoot when the server has none' {
        $resolution = Resolve-AttachmentStorageRoot -Requested 'E:\store' -Configured ''
        $resolution.Root | Should Be 'E:\store'
        $resolution.Source | Should Be ([AttachmentStorageRootSource]::Parameter)
        $resolution.Warning | Should BeNullOrEmpty
    }

    It 'keeps the server value and warns when -StorageRoot differs' {
        $resolution = Resolve-AttachmentStorageRoot -Requested 'E:\other' -Configured 'E:\store'
        $resolution.Root | Should Be 'E:\store'
        $resolution.Source | Should Be ([AttachmentStorageRootSource]::Server)
        $resolution.Warning | Should Match 'is ignored'
    }

    It 'does not warn when both name the same folder' {
        (Resolve-AttachmentStorageRoot -Requested 'E:\Store\' -Configured 'e:\store').Warning | Should BeNullOrEmpty
    }

    It 'returns no root when neither is given' {
        $resolution = Resolve-AttachmentStorageRoot -Requested '' -Configured ''
        $resolution.Root | Should BeNullOrEmpty
        $resolution.Source | Should Be ([AttachmentStorageRootSource]::None)
    }

    It 'accepts a drive folder or a UNC share and refuses relative paths' {
        Test-AttachmentFullyQualifiedPath 'E:\store' | Should Be $true
        Test-AttachmentFullyQualifiedPath '\\fileserver.example.test\share\store' | Should Be $true
        Test-AttachmentFullyQualifiedPath 'store' | Should Be $false
        Test-AttachmentFullyQualifiedPath '\store' | Should Be $false
        Test-AttachmentFullyQualifiedPath 'E:store' | Should Be $false
    }

    It 'refuses a storage root that contains the app folder' {
        { Assert-AttachmentStorageRootSafe -StorageRoot 'E:\web' -TargetPath 'E:\web\app' } | Should Throw 'overlaps the app folder'
    }

    It 'accepts a sibling folder whose name starts like the app folder' {
        { Assert-AttachmentStorageRootSafe -StorageRoot 'E:\web\app-files' -TargetPath 'E:\web\app' } | Should Not Throw
    }
}

Describe 'Install-Attachment storage plan' {
    $identity = 'IIS AppPool\KitTestPool'

    It 'creates and grants a missing local folder' {
        $plan = @(Get-AttachmentStoragePlan -StorageRoot 'E:\store' -Identity $identity -Facts ([pscustomobject]@{ Exists = $false; IsUnc = $false; IdentityHasModify = $false }))
        ($plan | ForEach-Object { $_.ActionId }) -join ',' | Should Be '1,2'
    }

    It 'only grants an existing local folder without the right' {
        $plan = @(Get-AttachmentStoragePlan -StorageRoot 'E:\store' -Identity $identity -Facts ([pscustomobject]@{ Exists = $true; IsUnc = $false; IdentityHasModify = $false }))
        ($plan | ForEach-Object { $_.Action }) -join ',' | Should Be 'GrantModify'
    }

    It 'plans None when the identity already has Modify' {
        $plan = @(Get-AttachmentStoragePlan -StorageRoot 'E:\store' -Identity $identity -Facts ([pscustomobject]@{ Exists = $true; IsUnc = $false; IdentityHasModify = $true }))
        ($plan | ForEach-Object { $_.ActionId }) -join ',' | Should Be '0'
    }

    It 'leaves share access to the operator for a UNC root' {
        $plan = @(Get-AttachmentStoragePlan -StorageRoot '\\fileserver.example.test\share' -Identity $identity -Facts ([pscustomobject]@{ Exists = $false; IsUnc = $true; IdentityHasModify = $false }))
        ($plan | ForEach-Object { $_.Action }) -join ',' | Should Be 'CreateFolder,ShareAccessManual'
    }

    It 'recognizes an inheritable Modify rule for the identity only' {
        $modify = [Security.AccessControl.FileSystemRights]::Modify
        $full = [Security.AccessControl.FileSystemRights]::FullControl
        $read = [Security.AccessControl.FileSystemRights]::ReadAndExecute
        $inherit = [Security.AccessControl.InheritanceFlags] 'ContainerInherit, ObjectInherit'
        $none = [Security.AccessControl.InheritanceFlags]::None
        $allow = [Security.AccessControl.AccessControlType]::Allow
        $deny = [Security.AccessControl.AccessControlType]::Deny
        $rule = { param($who, $rights, $flags, $type) [pscustomobject]@{ IdentityReference = $who; FileSystemRights = $rights; InheritanceFlags = $flags; AccessControlType = $type } }
        Test-AttachmentRulesGrantModify -Rules @((& $rule $identity $modify $inherit $allow)) -Identity $identity | Should Be $true
        Test-AttachmentRulesGrantModify -Rules @((& $rule $identity.ToLowerInvariant() $full $inherit $allow)) -Identity $identity | Should Be $true
        Test-AttachmentRulesGrantModify -Rules @((& $rule $identity $read $inherit $allow)) -Identity $identity | Should Be $false
        Test-AttachmentRulesGrantModify -Rules @((& $rule $identity $modify $none $allow)) -Identity $identity | Should Be $false
        Test-AttachmentRulesGrantModify -Rules @((& $rule $identity $modify $inherit $deny)) -Identity $identity | Should Be $false
        Test-AttachmentRulesGrantModify -Rules @((& $rule 'IIS AppPool\Other' $modify $inherit $allow)) -Identity $identity | Should Be $false
    }
}

Describe 'Install-Attachment package and health helpers' {
    It 'finds the publish output directly or in app\' {
        $direct = New-FakePackage
        $nested = New-FakePackage -InAppFolder
        Resolve-AttachmentPackage $direct | Should Be $direct
        Resolve-AttachmentPackage $nested | Should Be (Join-Path $nested 'app')
    }

    It 'refuses an incomplete publish output' {
        $package = New-FakePackage
        Remove-Item -LiteralPath (Join-Path $package 'web.config')
        { Resolve-AttachmentPackage $package } | Should Throw 'has no web.config'
    }

    It 'refuses a folder without the service entry assembly' {
        { Resolve-AttachmentPackage (New-TestFolder 'empty') } | Should Throw 'No Codelab.Attachments.Api.dll'
    }

    It 'builds the health URL from the base URL and the application path' {
        Get-AttachmentHealthUrl -BaseUrl 'http://localhost' -AppPath '/attachments' | Should BeExactly 'http://localhost/attachments/health/ready'
        Get-AttachmentHealthUrl -BaseUrl 'https://localhost:8443/' -AppPath 'files/' | Should BeExactly 'https://localhost:8443/files/health/ready'
    }

    It 'tells an IIS error page from a service answer' {
        Get-AttachmentHealthDiagnosis ([pscustomobject]@{ StatusCode = 200; Body = 'Healthy'; Error = $null }) | Should Match 'answered 200'
        Get-AttachmentHealthDiagnosis ([pscustomobject]@{ StatusCode = 0; Body = ''; Error = 'refused' }) | Should Match 'no answer'
        Get-AttachmentHealthDiagnosis ([pscustomobject]@{ StatusCode = 500; Body = '  <html>'; Error = $null }) | Should Match 'IIS answered'
        Get-AttachmentHealthDiagnosis ([pscustomobject]@{ StatusCode = 503; Body = '{"status":503}'; Error = $null }) | Should Match 'from the service itself'
    }

    It 'stops polling at the first 200' {
        Mock Start-Sleep { }
        $script:calls = 0
        Mock Invoke-AttachmentHealthRequest { $script:calls++; if ($script:calls -lt 3) { [pscustomobject]@{ StatusCode = 502; Body = ''; Error = $null } } else { [pscustomobject]@{ StatusCode = 200; Body = ''; Error = $null } } }
        (Wait-AttachmentHealth -Url 'http://localhost/attachments/health/ready' -Attempts 5 -DelaySeconds 1).StatusCode | Should Be 200
        Assert-MockCalled Invoke-AttachmentHealthRequest -Times 3 -Exactly -Scope It
        Assert-MockCalled Start-Sleep -Times 2 -Exactly -Scope It
    }
}
