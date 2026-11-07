# Pester 3.4 tests for tools/kit (Should Be / Should Throw syntax). No build, no IIS, no admin: the module's pure
# functions, the real component catalog and README template, and New-DeployKit.ps1 -DryRun.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/kit/tests -EnableExit"
# Every test that needs files works in its own folder under %TEMP% and deletes it afterwards.
Set-StrictMode -Version Latest

$KitFolder     = Split-Path -Parent $PSScriptRoot
$RepoRoot      = Split-Path -Parent (Split-Path -Parent $KitFolder)
$ModulePath    = Join-Path $KitFolder 'lib\DeployKit.psm1'
$CatalogPath   = Join-Path $KitFolder 'kit-components.psd1'
$ScriptPath    = Join-Path $KitFolder 'New-DeployKit.ps1'
$BuildScript   = Join-Path $RepoRoot 'tools\build.ps1'
$TemplatePath  = Join-Path $KitFolder 'templates\README.template.md'
$DeployKitsDir = Join-Path $RepoRoot 'deploy\kits'
$TempPrefix    = 'codelab-kit-builder-tests-'
$Commit        = '7c3e9a1b52d84f06e1a7b3c9d0e2f4a6b8c1d3e5'
$ShortName     = 'deploy-7c3e9a1'
# The payload of the hash tests and its SHA-256, computed independently of Get-FileHash.
$HashFixture   = 'kit-hash-fixture'
$FixtureSha256 = -join ([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::ASCII.GetBytes($HashFixture)) | ForEach-Object { $_.ToString('x2') })
$ExitOk        = 0
$ExitUsage     = 2
$ExitFailed    = 1
$FixedUtc      = New-Object DateTime 2026, 11, 7, 8, 9, 10, ([DateTimeKind]::Utc)

Import-Module $ModulePath -Force

function New-TestFolder { $path = Join-Path ([IO.Path]::GetTempPath()) ($TempPrefix + [guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $path | Out-Null; return $path }
function Remove-TestFolder([string] $Path) { if ($Path -and (Test-Path -LiteralPath $Path)) { Remove-Item -LiteralPath $Path -Recurse -Force } }
function Write-Ascii([string] $Path, [string] $Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.ASCIIEncoding))
}
function Get-Catalog { return @((Import-PowerShellDataFile -LiteralPath $CatalogPath).Components) }
function Get-Names([object[]] $Components) { return (@($Components | ForEach-Object { $_['Name'] }) -join ',') }
function Invoke-KitScript([string[]] $Arguments, [string] $Path = $ScriptPath) {
    $output = & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $Path @Arguments 2>&1
    return [pscustomobject]@{ Code = $LASTEXITCODE; Text = (@($output) -join "`n") }
}

Describe 'Split-KitList' {
    It 'splits one comma string (how 5.1 passes -Component a,b to a -File script) and lower-cases' {
        (Split-KitList @('API,web')) -join '|' | Should BeExactly 'api|web'
    }
    It 'accepts several values, semicolons and spaces' {
        (Split-KitList @('api', 'web; ops')) -join '|' | Should BeExactly 'api|web|ops'
    }
    It 'returns an empty list for nothing' {
        (Split-KitList $null).Count | Should Be 0
    }
}

Describe 'Get-KitFolderName' {
    It 'is deploy-<first 7 hex characters>, lower case' {
        Get-KitFolderName $Commit.ToUpperInvariant() | Should BeExactly $ShortName
        Get-KitFolderName '7c3e9a1' | Should BeExactly $ShortName
    }
    It 'refuses a value that is not a commit id' {
        { Get-KitFolderName 'main' } | Should Throw 'not a git commit id'
        { Get-KitFolderName 'abc12' } | Should Throw 'not a git commit id'
        { Get-KitFolderName '' } | Should Throw 'not a git commit id'
    }
}

Describe 'the component catalog (kit-components.psd1)' {
    $catalog = Get-Catalog

    It 'is valid: unique names, folders and orders, known part kinds, an install script each' {
        { Assert-KitCatalog $catalog } | Should Not Throw
    }
    It 'names install scripts that exist in deploy/kits' {
        foreach ($component in $catalog) { Test-Path -LiteralPath (Join-Path $DeployKitsDir $component['InstallScript']) | Should Be $true }
    }
    It 'resolves the project of every default .NET part in this repository' {
        foreach ($component in @($catalog | Where-Object { $_['Default'] })) {
            foreach ($part in @($component['Parts'] | Where-Object { $_['Kind'] -eq 1 })) {
                Resolve-KitProject -RepoRoot $RepoRoot -ProjectRoot $part['ProjectRoot'] -Pattern $part['ProjectPattern'] | Should Match '\.csproj$'
            }
        }
    }
    It 'refuses a catalog that repeats a folder' {
        $broken = @(
            @{ Name = 'a'; Order = 1; Folder = 'x'; InstallScript = 'A.ps1'; Parts = @(@{ Kind = 1 }) },
            @{ Name = 'b'; Order = 2; Folder = 'x'; InstallScript = 'B.ps1'; Parts = @(@{ Kind = 1 }) })
        { Assert-KitCatalog $broken } | Should Throw "repeats Folder 'x'"
    }
    It 'refuses an unknown part kind' {
        { Assert-KitCatalog @(@{ Name = 'a'; Order = 1; Folder = 'x'; InstallScript = 'A.ps1'; Parts = @(@{ Kind = 9 }) }) } | Should Throw 'unknown part kind 9'
    }
}

Describe 'Resolve-KitComponents' {
    $catalog = Get-Catalog

    It 'builds api, attachment and web by default, in catalog order' {
        Get-Names (Resolve-KitComponents -Catalog $catalog -Requested @()) | Should BeExactly 'api,attachment,web'
    }
    It 'keeps catalog order whatever the request order' {
        Get-Names (Resolve-KitComponents -Catalog $catalog -Requested @('ops,web,api,gateway')) | Should BeExactly 'api,gateway,web,ops'
    }
    It 'refuses an unknown component and lists the known ones' {
        { Resolve-KitComponents -Catalog $catalog -Requested @('api,nope') } | Should Throw "Unknown component 'nope'; known: api, attachment, gateway, web, ops."
    }
}

Describe 'Resolve-KitProject' {
    BeforeEach { $script:work = New-TestFolder }
    AfterEach { Remove-TestFolder $script:work }

    It 'finds the one matching project and ignores bin and obj copies' {
        Write-Ascii (Join-Path $script:work 'src\One.Api\One.Api.csproj') '<Project />'
        Write-Ascii (Join-Path $script:work 'src\One.Api\obj\Copy.Api.csproj') '<Project />'
        Resolve-KitProject -RepoRoot $script:work -ProjectRoot 'src' -Pattern '*.Api.csproj' | Should Match 'One\.Api\.csproj$'
    }
    It 'refuses none and several' {
        Write-Ascii (Join-Path $script:work 'src\A.Api\A.Api.csproj') '<Project />'
        Write-Ascii (Join-Path $script:work 'src\B.Api\B.Api.csproj') '<Project />'
        { Resolve-KitProject -RepoRoot $script:work -ProjectRoot 'src' -Pattern '*.Core.csproj' } | Should Throw 'No project matching'
        { Resolve-KitProject -RepoRoot $script:work -ProjectRoot 'src' -Pattern '*.Api.csproj' } | Should Throw 'Several projects match'
        { Resolve-KitProject -RepoRoot $script:work -ProjectRoot 'missing' -Pattern '*.csproj' } | Should Throw 'does not exist'
    }
}

Describe 'Get-KitSqlDecision' {
    It 'includes the DBA scripts when the generator exists' {
        $decision = Get-KitSqlDecision -SkipSql $false -GeneratorExists $true -GeneratorPath 'g.ps1'
        $decision.Included | Should Be $true
        [int] $decision.Status | Should Be 1
    }
    It 'skips them gracefully, with the reason, when the generator is absent' {
        $decision = Get-KitSqlDecision -SkipSql $false -GeneratorExists $false -GeneratorPath 'tools/sql-kit/New-DbaSqlScripts.ps1'
        $decision.Included | Should Be $false
        [int] $decision.Status | Should Be 3
        $decision.Reason | Should Match 'New-DbaSqlScripts\.ps1 is not in this repository'
    }
    It 'lets -SkipSql win over an existing generator' {
        $decision = Get-KitSqlDecision -SkipSql $true -GeneratorExists $true -GeneratorPath 'g.ps1'
        $decision.Included | Should Be $false
        [int] $decision.Status | Should Be 2
    }
}

Describe 'Get-KitLayout' {
    $catalog = Get-Catalog
    $included = (Get-KitSqlDecision -SkipSql $false -GeneratorExists $true -GeneratorPath 'g').Status
    $skipped = (Get-KitSqlDecision -SkipSql $true -GeneratorExists $true -GeneratorPath 'g').Status

    It 'lays out publish/deploy-<sha>/{api,attachment,web,kits,sql,README.md,manifest.json} by default' {
        $layout = Get-KitLayout -Components (Resolve-KitComponents -Catalog $catalog -Requested @()) -SqlStatus $included
        (@($layout | ForEach-Object { $_.Path }) -join ',') | Should BeExactly 'api,attachment,web,kits,sql,README.md,manifest.json'
        (@($layout | Where-Object { $_.IsPayload } | ForEach-Object { "$($_.Path):$($_.IsDotnet)" }) -join ',') | Should BeExactly 'api:True,attachment:True,web:False'
    }
    It 'puts the Ops Console API and web under ops\api and ops\web, and the gateway under gateway\' {
        $layout = Get-KitLayout -Components (Resolve-KitComponents -Catalog $catalog -Requested @('gateway,ops')) -SqlStatus $included
        (@($layout | Where-Object { $_.IsPayload } | ForEach-Object { "$($_.Component)=$($_.Path):$($_.IsDotnet)" }) -join ',') | Should BeExactly 'gateway=gateway:True,ops=ops/api:True,ops=ops/web:False'
    }
    It 'has no sql folder when the SQL step does not run' {
        $layout = Get-KitLayout -Components (Resolve-KitComponents -Catalog $catalog -Requested @('api')) -SqlStatus $skipped
        (@($layout | ForEach-Object { $_.Path }) -join ',') | Should BeExactly 'api,kits,README.md,manifest.json'
    }
}

Describe 'payload filters' {
    It 'leaves development settings and local secrets out of a payload, and keeps the shipped settings' {
        Test-KitPayloadExcluded 'appsettings.Development.json' | Should Be $true
        Test-KitPayloadExcluded 'appsettings.Development.local.json' | Should Be $true
        Test-KitPayloadExcluded 'anything.local.json' | Should Be $true
        Test-KitPayloadExcluded 'appsettings.json' | Should Be $false
        Test-KitPayloadExcluded 'appsettings.Production.json' | Should Be $false
        Test-KitPayloadExcluded 'web.config' | Should Be $false
    }
    It 'never ships the kit-module tests' {
        Test-KitScriptsEntryExcluded 'tests' | Should Be $true
        Test-KitScriptsEntryExcluded 'templates' | Should Be $false
        Test-KitScriptsEntryExcluded 'Install-Common.psm1' | Should Be $false
    }
    It 'finds key material and local secrets anywhere in the kit' {
        $found = Find-KitForbiddenFiles @('api/Starter.Api.dll', 'api\signing.pfx', 'kits/templates/x.KEY', 'web/config.json', 'api/appsettings.Development.json', 'gateway/client.pem')
        ($found -join ',') | Should BeExactly 'api\signing.pfx,kits/templates/x.KEY,api/appsettings.Development.json,gateway/client.pem'
        (Find-KitForbiddenFiles @('api/appsettings.json', 'README.md')).Count | Should Be 0
    }
}

Describe 'Get-KitFileHashes' {
    BeforeEach { $script:work = New-TestFolder }
    AfterEach { Remove-TestFolder $script:work }

    It 'hashes every file but manifest.json, with forward-slash paths in ordinal order' {
        Write-Ascii (Join-Path $script:work 'web\index.html') $HashFixture
        Write-Ascii (Join-Path $script:work 'api\a.dll') $HashFixture
        Write-Ascii (Join-Path $script:work 'README.md') $HashFixture
        Write-Ascii (Join-Path $script:work 'manifest.json') '{}'
        $hashes = Get-KitFileHashes $script:work
        (@($hashes | ForEach-Object { $_.path }) -join ',') | Should BeExactly 'README.md,api/a.dll,web/index.html'
        $hashes[0].sha256 | Should BeExactly $FixtureSha256
        $hashes[0].bytes | Should Be $HashFixture.Length
    }
    It 'feeds the manifest as a flat list, however the caller passes it' {
        Write-Ascii (Join-Path $script:work 'api\a.dll') $HashFixture
        Write-Ascii (Join-Path $script:work 'web\index.html') $HashFixture
        $sql = Get-KitSqlDecision -SkipSql $true -GeneratorExists $false -GeneratorPath 'g'
        $components = Resolve-KitComponents -Catalog (Get-Catalog) -Requested @('api')
        foreach ($files in @((Get-KitFileHashes $script:work), @(Get-KitFileHashes $script:work))) {
            $manifest = New-KitManifest -Commit $Commit -Dirty $false -CreatedUtc $FixedUtc -Versions $null -Components $components -Sql $sql -Files $files
            (@($manifest.files | ForEach-Object { $_.path }) -join ',') | Should BeExactly 'api/a.dll,web/index.html'
        }
    }
}

Describe 'New-KitManifest' {
    $catalog = Get-Catalog
    $components = Resolve-KitComponents -Catalog $catalog -Requested @('api,web')
    $versions = '{ "Codelab.Security": "0.6.0", "@codelab/ng-dga": "1.0.0" }' | ConvertFrom-Json
    $files = @(
        [pscustomobject]@{ path = 'api/a.dll'; sha256 = 'x'; bytes = 1 },
        [pscustomobject]@{ path = 'sql/01-starter.sql'; sha256 = 'y'; bytes = 2 },
        [pscustomobject]@{ path = 'sql/02-attachments.sql'; sha256 = 'z'; bytes = 3 })

    It 'records the commit, the versions in versions.json order, the components and their install scripts' {
        $sql = Get-KitSqlDecision -SkipSql $false -GeneratorExists $true -GeneratorPath 'g'
        $manifest = New-KitManifest -Commit $Commit -Dirty $false -CreatedUtc $FixedUtc -Versions $versions -Components $components -Sql $sql -Files $files
        $manifest.kitFormat | Should Be 1
        $manifest.name | Should BeExactly $ShortName
        $manifest.commit | Should BeExactly $Commit
        $manifest.dirty | Should Be $false
        $manifest.createdAtUtc | Should BeExactly '2026-11-07T08:09:10Z'
        (@($manifest.packages.Keys) -join ',') | Should BeExactly 'Codelab.Security,@codelab/ng-dga'
        $manifest.packages['@codelab/ng-dga'] | Should BeExactly '1.0.0'
        (@($manifest.components | ForEach-Object { "$($_.name)=$($_.folder)=$($_.installScript)" }) -join ',') | Should BeExactly 'api=api=kits/Install-Api.ps1,web=web=kits/Install-Web.ps1'
        $manifest.sql.status | Should BeExactly 'Included'
        $manifest.sql.statusValue | Should Be 1
        $manifest.sql.folder | Should BeExactly 'sql'
        $manifest.sql.files | Should Be 2
        @($manifest.files).Count | Should Be 3
    }
    It 'records a skipped SQL step with its reason and a dirty tree' {
        $sql = Get-KitSqlDecision -SkipSql $false -GeneratorExists $false -GeneratorPath 'g.ps1'
        $manifest = New-KitManifest -Commit $Commit -Dirty $true -CreatedUtc $FixedUtc -Versions $versions -Components $components -Sql $sql -Files @()
        $manifest.dirty | Should Be $true
        $manifest.sql.status | Should BeExactly 'SkippedMissingGenerator'
        $manifest.sql.files | Should Be 0
        $manifest.sql.reason | Should Match 'g\.ps1 is not in this repository'
    }
    It 'serializes to JSON with LF line endings that parses back' {
        $sql = Get-KitSqlDecision -SkipSql $true -GeneratorExists $false -GeneratorPath 'g'
        $text = ConvertTo-KitJsonText (New-KitManifest -Commit $Commit -Dirty $false -CreatedUtc $FixedUtc -Versions $versions -Components $components -Sql $sql -Files $files)
        $text.Contains("`r") | Should Be $false
        ($text | ConvertFrom-Json).files[1].path | Should BeExactly 'sql/01-starter.sql'
    }
}

Describe 'the README' {
    $catalog = Get-Catalog
    $components = Resolve-KitComponents -Catalog $catalog -Requested @('api,attachment,web')
    $versions = '{ "Codelab.Security": "0.6.0" }' | ConvertFrom-Json

    It 'expands every token of the real template, with the install order and the SQL step first' {
        $sql = Get-KitSqlDecision -SkipSql $false -GeneratorExists $true -GeneratorPath 'g'
        $files = @([pscustomobject]@{ path = 'sql/01-starter.sql'; sha256 = 'y'; bytes = 2 })
        $manifest = New-KitManifest -Commit $Commit -Dirty $false -CreatedUtc $FixedUtc -Versions $versions -Components $components -Sql $sql -Files $files
        $text = Expand-KitTemplate ([IO.File]::ReadAllText($TemplatePath)) (Get-KitReadmeValues $manifest $components)
        $text | Should Match '^# Deploy kit deploy-7c3e9a1'
        $text | Should Match '1\. The DBA runs the scripts in `sql\\`'
        $text | Should Match '2\. `kits\\Install-Api\.ps1 -PackagePath \.\\api'
        $text | Should Match '4\. `kits\\Install-Web\.ps1 -PackagePath \.\\web'
        $text | Should Match '- `Codelab\.Security` 0\.6\.0'
        $text | Should Not Match '\{\{'
    }
    It 'says why there are no DBA scripts when the step was skipped' {
        $sql = Get-KitSqlDecision -SkipSql $false -GeneratorExists $false -GeneratorPath 'tools/sql-kit/New-DbaSqlScripts.ps1'
        $manifest = New-KitManifest -Commit $Commit -Dirty $true -CreatedUtc $FixedUtc -Versions $versions -Components $components -Sql $sql -Files @()
        $text = Expand-KitTemplate ([IO.File]::ReadAllText($TemplatePath)) (Get-KitReadmeValues $manifest $components)
        $text | Should Match 'No DBA scripts in this kit'
        $text | Should Match '1\. `kits\\Install-Api\.ps1'
        $text | Should Match 'not for production'
    }
    It 'refuses a template token without a value' {
        { Expand-KitTemplate 'a {{KIT_NAME}} {{UNKNOWN}}' @{ KIT_NAME = 'k' } } | Should Throw 'tokens without a value: {{UNKNOWN}}'
    }
}

Describe 'Test-KitOwnedFolder' {
    BeforeEach { $script:work = New-TestFolder }
    AfterEach { Remove-TestFolder $script:work }

    It 'recognises only a folder whose manifest.json this builder wrote' {
        Test-KitOwnedFolder $script:work | Should Be $false
        Write-Ascii (Join-Path $script:work 'manifest.json') 'not json'
        Test-KitOwnedFolder $script:work | Should Be $false
        Write-Ascii (Join-Path $script:work 'manifest.json') '{ "name": "x" }'
        Test-KitOwnedFolder $script:work | Should Be $false
        Write-Ascii (Join-Path $script:work 'manifest.json') '{ "kitFormat": 1 }'
        Test-KitOwnedFolder $script:work | Should Be $true
    }
}

Describe 'New-DeployKit.ps1 -DryRun' {
    BeforeEach { $script:work = New-TestFolder }
    AfterEach { Remove-TestFolder $script:work }

    It 'prints the plan and creates nothing' {
        $output = Join-Path $script:work 'out'
        $result = Invoke-KitScript @('-DryRun', '-Sha', $Commit, '-OutputRoot', $output, '-Component', 'api,web')
        $result.Code | Should Be $ExitOk
        $result.Text | Should Match ([regex]::Escape((Join-Path $output $ShortName)))
        $result.Text | Should Match 'Components: api, web'
        $result.Text | Should Match 'Dry run: nothing was built or written'
        Test-Path -LiteralPath $output | Should Be $false
    }
    It 'exits 2 for an unknown component and for a value that is not a commit' {
        (Invoke-KitScript @('-DryRun', '-Sha', $Commit, '-Component', 'nope')).Code | Should Be $ExitUsage
        (Invoke-KitScript @('-DryRun', '-Sha', 'main')).Code | Should Be $ExitUsage
    }
}

# The entry point the prep-deploy skill, MODULE.md and the kit README name: tools\build.ps1 -Kit [-KitComponent].
Describe 'tools\build.ps1 -Kit' {
    It 'resolves -Kit -KitComponent -DryRun to the builder plan, runs no CI step and writes nothing' {
        $result = Invoke-KitScript @('-Kit', '-KitComponent', 'api,web', '-DryRun') $BuildScript
        $result.Code | Should Be $ExitOk
        $result.Text | Should Match 'New-DeployKit\.ps1 -Component api,web -DryRun'
        $result.Text | Should Match 'Components: api, web'
        $result.Text | Should Match 'Dry run: nothing was built or written'
        $result.Text | Should Not Match 'scrub sweep'
    }
    It 'fails when the builder refuses the components' {
        $result = Invoke-KitScript @('-Kit', '-KitComponent', 'nope', '-DryRun') $BuildScript
        $result.Code | Should Be $ExitFailed
        $result.Text | Should Match 'no kit was handed over'
    }
    It 'refuses -KitComponent without -Kit and -DryRun without -Pack or -Kit' {
        (Invoke-KitScript @('-KitComponent', 'api', '-Target', 'None') $BuildScript).Text | Should Match '-KitComponent is an option of -Kit'
        (Invoke-KitScript @('-DryRun') $BuildScript).Code | Should Be $ExitFailed
    }
}
