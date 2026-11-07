# Constants shared by New-HandoverKit.ps1 and Test-HandoverKit.ps1 (ADR-008). Every name, path and default lives
# here; the scripts and the other lib files never repeat a literal.

# --- Kit identity ------------------------------------------------------------------------------------------------
$script:KitKind                = 'codelab-handover-kit'
$script:KitSchemaVersion       = 1
$script:KitManifestFile        = 'manifest.json'
$script:KitNuGetConfigFile     = 'nuget.config'
$script:KitOfflineFile         = 'OFFLINE.md'
$script:KitPrerequisitesFile   = 'PREREQUISITES.md'
$script:KitNuGetDir            = 'nuget'
$script:KitNpmCacheDir         = 'npm-cache'
$script:KitNpmFilesDir         = 'npm-files'
$script:KitSourcesDir          = 'sources'
$script:KitBuildOfflineFile    = 'BUILD-OFFLINE.md'
$script:KitPrerequisitesDataFile = 'prerequisites.json'
# Every top-level entry a kit may contain. -Force clears a previous kit only when nothing else is in the folder.
$script:KitOwnedEntries        = @(
    $script:KitManifestFile, $script:KitNuGetConfigFile, $script:KitOfflineFile, $script:KitPrerequisitesFile,
    $script:KitNuGetDir, $script:KitNpmCacheDir, $script:KitNpmFilesDir, $script:KitSourcesDir,
    $script:KitBuildOfflineFile, $script:KitPrerequisitesDataFile)

# --- Exit codes --------------------------------------------------------------------------------------------------
$script:ExitSuccess            = 0
$script:ExitFailure            = 1
$script:ExitUsage              = 2
$script:UsageErrorMarker       = 'CodelabHandoverKitUsage'

# --- Toolchain resolution (same order as tools/build.ps1) --------------------------------------------------------
$script:DotnetOverrideEnvVar   = 'CODELAB_DOTNET'
$script:PrivateDotnetPath      = 'E:\codelab-tools\dotnet10\dotnet.exe'
$script:NodeDirOverrideEnvVar  = 'CODELAB_NODE_DIR'
$script:PrivateNodeDir         = 'E:\codelab-tools\node24'
$script:NodeExeName            = 'node.exe'
$script:NpmCmdName             = 'npm.cmd'

# --- Repository layout -------------------------------------------------------------------------------------------
$script:DefaultFeedRelative    = 'feed'
$script:FeedNuGetSubdir        = 'nuget'
$script:GlobalJsonFile         = 'global.json'
$script:GlobalJsonSdkKey       = 'sdk'
$script:GlobalJsonVersionKey   = 'version'
$script:GlobalJsonRollForwardKey = 'rollForward'
$script:NvmrcFile              = '.nvmrc'
$script:RepoNuGetConfigFile    = 'nuget.config'
$script:PackageJsonFile        = 'package.json'
$script:PackageLockFile        = 'package-lock.json'
$script:DotnetProjectExtensions = @('.csproj', '.slnx', '.sln')
# Source zips (c): each child folder of these package roots becomes one zip; a whole-folder root becomes one zip.
$script:PackageSourceGroups    = @('packages\dotnet', 'packages\npm')
$script:PackageSourceFolders   = @('packages\vectors')
$script:ZipExtension           = '.zip'
# Folders never copied into a work tree or a source zip when git cannot list the files.
$script:BuildOutputFolders     = @('bin', 'obj', 'node_modules', 'dist', '.git', '.vs', 'TestResults', '.angular')

# --- NuGet -------------------------------------------------------------------------------------------------------
$script:NuGetPackagesEnvVar    = 'NUGET_PACKAGES'
$script:KitNuGetSourceKey      = 'codelab-handover'
$script:FeedNuGetSourceKey     = 'codelab-local'
$script:NuGetOrgSourceKey      = 'nuget.org'
$script:NuGetOrgSourceUrl      = 'https://api.nuget.org/v3/index.json'
$script:NuGetOrgProtocol       = '3'
$script:CodelabPackagePattern  = 'Codelab.*'
$script:AnyPackagePattern      = '*'
# A folder source in the global-packages layout needs only these files per {id}/{version} folder.
$script:NuGetFeedFilePatterns  = @('*.nupkg', '*.nupkg.sha512', '*.nuspec', '.nupkg.metadata')

# --- npm ---------------------------------------------------------------------------------------------------------
$script:NpmFileProtocol        = 'file:'
$script:NpmTarballExtension    = '.tgz'
# npm writes these into its cache folder on every run; they are not part of the closure.
$script:NpmCacheNoise          = @('_logs', '_update-notifier-last-checked')
$script:PlaywrightPackages     = @('@playwright/test', 'playwright', 'playwright-core')
$script:NpmLockPackagesKey     = 'packages'
$script:WorkspacesKey          = 'workspaces'
$script:WorkspacePackagesKey   = 'packages'
$script:NpmNodeModulesPrefix   = 'node_modules/'
$script:NpmLockResolvedKey     = 'resolved'
$script:NpmLockVersionKey      = 'version'
$script:HashtableJsonMinVersion = 6
$script:NpmQuietArguments      = @('--no-audit', '--no-fund', '--update-notifier=false')
# Re-links bins after a build (no install scripts run, nothing is fetched).
$script:NpmRelinkArguments     = @('rebuild', '--ignore-scripts', '--offline')

# --- Offline simulation (Test-HandoverKit.ps1) -------------------------------------------------------------------
# Nothing listens on the discard port, so any HTTP request through this proxy fails at once.
$script:UnroutableProxy        = 'http://127.0.0.1:9'
$script:ProxyEnvVars           = @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'npm_config_proxy', 'npm_config_https_proxy')
$script:NoProxyEnvVars         = @('NO_PROXY', 'no_proxy', 'npm_config_noproxy')
$script:QuietDotnetEnv         = @{
    'DOTNET_CLI_TELEMETRY_OPTOUT'               = '1'
    'DOTNET_NOLOGO'                             = '1'
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE'         = '1'
    'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE' = '1'
}
$script:NpmBuildScript         = 'build'
$script:NpmTestScript          = 'test'
$script:NpmScriptsKey          = 'scripts'

# --- Temp folders (always created fresh and removed by the script that created them) ------------------------------
$script:WorkTreePrefix         = 'codelab-handover-work-'
$script:EmptyPackagesPrefix    = 'codelab-handover-nuget-'
$script:NpmCacheCopyPrefix     = 'codelab-handover-npmcache-'

# --- Text ----------------------------------------------------------------------------------------------------------
$script:NoPlaywrightText       = 'none locked'
$script:HashAlgorithm          = 'SHA256'
$script:JsonDepth              = 10

# --- Handover folder files (deploy/handover, next to the scripts) -----------------------------------------------
$script:HandoverDir            = Split-Path -Parent $PSScriptRoot
$script:BuildOfflineSourceFile = Join-Path $script:HandoverDir $script:KitBuildOfflineFile
$script:PrerequisitesCatalogFile = Join-Path $script:HandoverDir $script:KitPrerequisitesDataFile

# --- Prerequisites catalog (prerequisites.json): installers are listed, not shipped (ADR-008, P10) ---------------
$script:PrerequisitesSchemaVersion = 1
$script:PrerequisitesDecisionListed = 'listed'
$script:PrerequisitePinGlobalJson = 'globalJson'
$script:PrerequisitePinNvmrc   = 'nvmrc'
$script:PrerequisitePinLockPackage = 'lockPackage'
$script:Sha256HexPattern       = '^[0-9a-f]{64}$'
$script:HttpsScheme            = 'https://'
$script:NodeVersionPrefix      = 'v'
$script:VersionSeparator       = '.'

# --- Presets: named closures (-Preset). Entries are source-relative; an entry missing in a fork (a removed optional
# module, or packages\ after init) is skipped and recorded in the manifest. npm projects are listed in build order:
# the Codelab packages first, because the apps import their dist\ output. ----------------------------------------
$script:KitPresetFull          = 'Full'
$script:KitPresets             = @{
    $script:KitPresetFull = [pscustomobject]@{
        Projects    = @(
            'codelab-starter.slnx',
            'services\attachment-service\Codelab.Attachments.slnx',
            'services\integration-gateway\Codelab.Gateway.slnx',
            'services\ops-console\Codelab.OpsConsole.slnx')
        NpmProjects = @(
            'packages\npm\ng-tools',
            'packages\npm\ng-security',
            'packages\npm\ng-dga',
            'frontend\starter-web',
            'services\ops-console\web\ops-web',
            'packages\samples\ng-security-consumer')
    }
}
