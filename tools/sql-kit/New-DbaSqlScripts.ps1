#Requires -Version 5.1
<#
.SYNOPSIS
    Writes the DBA migration scripts of a release: one NN-<Context>.sql per DbContext of the API, plus sql-scripts.json.

.DESCRIPTION
    1. Builds the API host (Release) into a private work folder (ArtifactsPath), never into the source tree's bin/obj,
       so a running local stack is not disturbed.
    2. Lists every DbContext the host registers (dotnet ef dbcontext list) and orders them the way the API migrates
       (SqlKit.psm1 Get-DbaContextOrder).
    3. For each context runs dotnet ef migrations script --idempotent (no database is opened) and reshapes it with
       ConvertTo-DbaSqlScript: each step in its own batch through EXEC(N'...'), quotes doubled, GO separators,
       SQLCMD-mode guard, one transaction per migration.
    4. Writes <OutputPath>\NN-<Context>.sql (UTF-8 with BOM, CRLF) and <OutputPath>\sql-scripts.json (the order, the
       context and the migration ids of every file). The work folder is removed afterwards.

    The DBA flow is in deploy/sql/README.md. tools/kit/New-DeployKit.ps1 calls this script for the kit's sql\ folder.

.PARAMETER OutputPath
    The folder the scripts go to. Created when missing; refused when it already holds files, unless -Force (which
    replaces only NN-*.sql files and sql-scripts.json).
.PARAMETER StartupProject
    The host project. Default backend\src\Starter.Api\Starter.Api.csproj under the repository root.
.PARAMETER EfToolPath
    dotnet-ef (version 10, matching Microsoft.EntityFrameworkCore). Default: $env:CODELAB_DOTNET_EF, else dotnet-ef
    on PATH.
.PARAMETER SourceRevision
    Written into each file's header. Default: git rev-parse --short HEAD (empty outside git).
.PARAMETER Force
    Allow a non-empty -OutputPath.
.PARAMETER KeepWorkFolder
    Keep the build output and the raw EF scripts (their path is printed) for diagnosis.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\sql-kit\New-DbaSqlScripts.ps1 -OutputPath $env:TEMP\release-sql
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $OutputPath,
    [string] $StartupProject,
    [string] $EfToolPath,
    [string] $SourceRevision,
    [switch] $Force,
    [switch] $KeepWorkFolder
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Constants ----------------------------------------------------------------------------------------------------
$RepositoryRoot          = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).ProviderPath
$DefaultStartupRelative  = 'backend\src\Starter.Api\Starter.Api.csproj'
$DotnetOverrideEnvVar    = 'CODELAB_DOTNET'
$PrivateDotnetPath       = 'E:\codelab-tools\dotnet10\dotnet.exe'   # the same resolution as tools/build.ps1
$EfToolOverrideEnvVar    = 'CODELAB_DOTNET_EF'
$DefaultDotnet           = 'dotnet'
$DefaultEfTool           = 'dotnet-ef'
$EfVersionHint           = '10.0.14'                                 # Microsoft.EntityFrameworkCore in Directory.Packages.props
$BuildConfiguration      = 'Release'
# dotnet ef builds the host (Program.cs up to Build()) to find the contexts. Only Development builds it without server
# secrets (Production refuses at build time without Observability:UserIdHash:Key). No database is opened either way.
$HostEnvironment         = 'Development'
$EfWarningPrefix         = 'warn:'
$ManifestFileName        = 'sql-scripts.json'
$ScriptFilePattern       = '^\d{2}-.+\.sql$'
$WorkFolderPrefix        = 'codelab-sql-kit-'
$RawFolderName           = 'raw'
$ArtifactsFolderName     = 'artifacts'
$RawExtension            = '.sql'
$ManifestJsonDepth       = 6
$ManifestSchemaVersion   = 1
$ExitOk                  = 0
$GitShortRevisionArgs    = @('rev-parse', '--short', 'HEAD')
# MSBuild reads environment variables as properties: these move bin/obj of every project into the work folder.
$ArtifactsPathVariable   = 'ArtifactsPath'
$UseArtifactsVariable    = 'UseArtifactsOutput'
$TrueValue               = 'true'
$EnvironmentVariables    = @('ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT')

Import-Module (Join-Path $PSScriptRoot 'SqlKit.psm1') -Force -Verbose:$false

function Resolve-SqlKitDotnet {
    $override = [Environment]::GetEnvironmentVariable($DotnetOverrideEnvVar)
    if (-not [string]::IsNullOrWhiteSpace($override)) { return $override }
    if (Test-Path -LiteralPath $PrivateDotnetPath -PathType Leaf) { return $PrivateDotnetPath }
    return $DefaultDotnet
}

function Resolve-SqlKitEfTool([string] $Requested) {
    $candidates = @($Requested, [Environment]::GetEnvironmentVariable($EfToolOverrideEnvVar)) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return (Resolve-Path -LiteralPath $candidate).ProviderPath }
        throw "dotnet-ef '$candidate' does not exist."
    }
    $command = Get-Command $DefaultEfTool -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { return $command.Source }
    throw "dotnet-ef was not found. Install it outside the repository, for example: dotnet tool install dotnet-ef --version $EfVersionHint --tool-path <folder>, then pass -EfToolPath <folder>\dotnet-ef.exe or set $EfToolOverrideEnvVar."
}

function Assert-SqlKitOutputPath([string] $Path, [bool] $AllowFiles) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $existing = @(Get-ChildItem -LiteralPath $Path -Force)
    if ($existing.Count -eq 0) { return }
    if (-not $AllowFiles) { throw "-OutputPath '$Path' is not empty; give an empty folder or -Force (replaces only NN-*.sql and $ManifestFileName)." }
    $foreign = @($existing | Where-Object { $_.PSIsContainer -or -not ($_.Name -match $ScriptFilePattern -or $_.Name -eq $ManifestFileName) })
    if ($foreign.Count -gt 0) { throw "-OutputPath '$Path' holds other files ($(@($foreign | ForEach-Object { $_.Name }) -join ', ')); -Force replaces only NN-*.sql and $ManifestFileName." }
}

# Runs a native command with the work-folder environment; returns its output lines, throws on a non-zero exit.
function Invoke-SqlKitNative([string] $FilePath, [string[]] $Arguments, [string] $What) {
    # Windows PowerShell 5.1 turns each stderr line into an error record; with Stop the first warning would end the run.
    $ErrorActionPreference = 'Continue'
    $output = & $FilePath @Arguments 2>&1 | ForEach-Object { [string] $_ }
    if ($LASTEXITCODE -ne $ExitOk) {
        $output | ForEach-Object { Write-Host $_ }
        throw "$What failed (exit $LASTEXITCODE)."
    }
    return @($output)
}

function Get-SqlKitSourceRevision {
    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $git) { return '' }
    $revision = & $git.Source -C $RepositoryRoot @GitShortRevisionArgs 2>$null
    if ($LASTEXITCODE -ne $ExitOk) { return '' }
    return ([string] $revision).Trim()
}

function Set-SqlKitEnvironment([hashtable] $Values) {
    $previous = @{}
    foreach ($name in $Values.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $Values[$name])
    }
    return $previous
}

# --- Run ------------------------------------------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($StartupProject)) { $StartupProject = Join-Path $RepositoryRoot $DefaultStartupRelative }
if (-not (Test-Path -LiteralPath $StartupProject -PathType Leaf)) { throw "Startup project '$StartupProject' does not exist." }
$StartupProject = (Resolve-Path -LiteralPath $StartupProject).ProviderPath
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
Assert-SqlKitOutputPath -Path $OutputPath -AllowFiles $Force.IsPresent
$dotnet = Resolve-SqlKitDotnet
$efTool = Resolve-SqlKitEfTool $EfToolPath
if ([string]::IsNullOrWhiteSpace($SourceRevision)) { $SourceRevision = Get-SqlKitSourceRevision }

$work = Join-Path ([IO.Path]::GetTempPath()) ($WorkFolderPrefix + [guid]::NewGuid().ToString('N'))
$raw = Join-Path $work $RawFolderName
New-Item -ItemType Directory -Path $raw -Force | Out-Null
$dotnetFolder = Split-Path -Parent $dotnet
$environment = @{ $ArtifactsPathVariable = (Join-Path $work $ArtifactsFolderName); $UseArtifactsVariable = $TrueValue }
foreach ($name in $EnvironmentVariables) { $environment[$name] = $HostEnvironment }
# dotnet-ef starts 'dotnet' itself: the resolved SDK goes first on PATH for this run.
if ($dotnetFolder) { $environment['PATH'] = $dotnetFolder + [IO.Path]::PathSeparator + $env:PATH; $environment['DOTNET_ROOT'] = $dotnetFolder }
$previous = Set-SqlKitEnvironment $environment
try {
    Write-Host "Building $StartupProject ($BuildConfiguration) into $work ..."
    Invoke-SqlKitNative $dotnet @('build', $StartupProject, '-c', $BuildConfiguration, '-nologo', '-v', 'q') 'dotnet build' | Out-Null
    $common = @('--project', $StartupProject, '--startup-project', $StartupProject, '--configuration', $BuildConfiguration, '--no-build')
    $listed = Invoke-SqlKitNative $efTool (@('dbcontext', 'list', '--json', '--prefix-output') + $common) 'dotnet ef dbcontext list'
    $contexts = @(Get-DbaContextOrder -Contexts @(ConvertFrom-EfContextList -OutputLines $listed))
    if ($contexts.Count -eq 0) {
        $warnings = @($listed | Where-Object { $_.StartsWith($EfWarningPrefix, [StringComparison]::Ordinal) }) -join [Environment]::NewLine
        throw "dotnet ef found no DbContext: the host did not build its services. $warnings"
    }
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    if ($Force) { Get-ChildItem -LiteralPath $OutputPath -File | Where-Object { $_.Name -match $ScriptFilePattern -or $_.Name -eq $ManifestFileName } | Remove-Item -Force }

    $files = New-Object 'System.Collections.Generic.List[object]'
    $position = 0
    foreach ($context in $contexts) {
        $position++
        $rawFile = Join-Path $raw ($context.Name + $RawExtension)
        Invoke-SqlKitNative $efTool (@('migrations', 'script', '--idempotent', '--context', $context.FullName, '--output', $rawFile) + $common) "dotnet ef migrations script ($($context.Name))" | Out-Null
        $sql = [IO.File]::ReadAllText($rawFile, (New-Object Text.UTF8Encoding($false)))
        $shaped = ConvertTo-DbaSqlScript -Sql $sql -ContextName $context.Name -SourceRevision $SourceRevision
        $fileName = Get-DbaScriptFileName -Position $position -ContextName $context.Name
        [IO.File]::WriteAllText((Join-Path $OutputPath $fileName), $shaped.Text, (New-Object Text.UTF8Encoding($true)))
        $files.Add([ordered]@{ position = $position; file = $fileName; context = $context.FullName; migrations = @($shaped.MigrationIds) })
        Write-Host ("  {0}  {1} migration(s), {2} step(s)" -f $fileName, $shaped.MigrationIds.Count, $shaped.BlockCount)
    }
    $manifest = [ordered]@{ schemaVersion = $ManifestSchemaVersion; sourceRevision = $SourceRevision; files = $files.ToArray() }
    [IO.File]::WriteAllText((Join-Path $OutputPath $ManifestFileName), (ConvertTo-Json -InputObject $manifest -Depth $ManifestJsonDepth), (New-Object Text.UTF8Encoding($false)))
    Write-Host "Wrote $($files.Count) script(s) and $ManifestFileName to $OutputPath. DBA flow: deploy/sql/README.md."
}
finally {
    Set-SqlKitEnvironment $previous | Out-Null
    if ($KeepWorkFolder) { Write-Host "Work folder kept: $work" }
    else { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
