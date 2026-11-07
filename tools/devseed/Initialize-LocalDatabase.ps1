<#
.SYNOPSIS
    Creates the developer's local database: every DbContext migration and the break-glass administrator with a fresh
    one-time password, printed once.

.DESCRIPTION
    init.ps1 Step 4 (after New-LocalSecrets.ps1). Uses the API's own Development path, so there is one migration and
    seeding code path, not a second one here: the API is built to a temp artifacts folder (never its bin folder, so a
    running API is not disturbed) and started once on a free loopback port with Database:ApplyMigrationsOnStartup=true
    (every module's IModuleDatabase migrates, lookups and the permission catalog come from the migrations) and a freshly
    generated Identity:BreakGlass:Password (the account seeder creates the break-glass administrator). When the API
    answers /health/live the startup tasks have run; the script checks the break-glass row, stops the process it
    started (never another one), saves the password as Identity:BreakGlass:Password in the local secrets file (the
    configured source: the seeder re-applies the configured password on every start) and prints it once.

    The connection string (with the database set) is saved as ConnectionStrings:Starter in the same gitignored local
    secrets file, after a successful run and on the already-initialized skip, so the API and run-local use the database
    init created instead of the appsettings.Development.json default. The one-shot start reads a temporary copy of the
    secrets file in the build output; that copy is deleted once the API has stopped, on success and on failure.

    The API assembly references no seed code: this script and the local secrets file are the dev seed.

    Idempotent: a database that already holds the break-glass administrator is reported and skipped (nothing is
    built, started or printed). Only a SQL Server on this machine is accepted (LocalDB, ., (local), localhost,
    127.0.0.1, this computer's name); a deployed database is created by the DBA kit, never by this script.

.PARAMETER ConnectionString
    The SQL Server connection string. Its database name is used unless -DatabaseName is given.

.PARAMETER DatabaseName
    The database to create or check (letters, digits and underscores, starting with a letter). Overrides the
    connection string's database.

.PARAMETER SecretsPath
    The API's local secrets file. Defaults to backend\src\Starter.Api\appsettings.Development.local.json. It must exist
    (run New-LocalSecrets.ps1 first); the one-shot API start uses its keys.

.PARAMETER ArtifactsPath
    Where the API is built. Defaults to %TEMP%\codelab-devseed-database.

.PARAMETER Dotnet
    The dotnet executable. Defaults to $env:CODELAB_DOTNET, then dotnet on PATH.

.PARAMETER TimeoutSeconds
    How long the one-shot API start may take. Default 300.

.EXAMPLE
    powershell -File tools\devseed\Initialize-LocalDatabase.ps1 -ConnectionString "Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true" -DatabaseName StarterDev

.NOTES
    Exit codes: 0 created, or already initialized and skipped; 1 refused (a server not on this machine, no local
    secrets file, a failed build or start); 2 usage.
#>
[CmdletBinding()]
param(
    [string] $ConnectionString,
    [string] $DatabaseName,
    [string] $SecretsPath,
    [string] $ArtifactsPath,
    [string] $Dotnet,
    [int] $TimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\DevSeed.Common.psm1') -Force

$ExitDone = 0
$ExitRefused = 1
$ExitUsage = 2

$DatabaseNamePattern = '^[A-Za-z][A-Za-z0-9_]{0,99}$'
$LocalHostNames = @('(localdb)', '.', '(local)', 'localhost', '127.0.0.1', '::1')
$ServerProtocolPrefixes = '^(tcp|np|lpc|admin):'
$ServerNameSeparators = @('\', ',')
$MasterDatabase = 'master'
# SqlConnectionStringBuilder keywords (PowerShell reads a dictionary's properties as keys, so the builder is indexed).
$CatalogKey = 'Initial Catalog'
$DataSourceKey = 'Data Source'
$BreakGlassUserName = 'breakglass'
# Identity module schema (IdentityDbContext.SchemaName, TableNames.UserAccounts, UserAccount.IsBreakGlass).
$AccountsTable = '[identity].[UserAccounts]'
$BreakGlassColumn = 'IsBreakGlass'
$SqlTimeoutSeconds = 30
# Where the connection string is saved in the local secrets file (the API's ConnectionStrings:Starter).
$ConnectionStringsSection = 'ConnectionStrings'
$ConnectionStringName = 'Starter'

$ApiProjectRelativePath = 'backend\src\Starter.Api\Starter.Api.csproj'
$ApiAssemblyName = 'Starter.Api'
$DefaultSecretsRelativePath = 'backend\src\Starter.Api\appsettings.Development.local.json'
$SecretsFileName = 'appsettings.Development.local.json'
$DefaultArtifactsFolder = 'codelab-devseed-database'
$BuildConfiguration = 'debug'
$DotnetEnvironmentVariable = 'CODELAB_DOTNET'
$DefaultDotnet = 'dotnet'
$LivePath = '/health/live'
$LoopbackAddress = '127.0.0.1'
$PollIntervalMilliseconds = 1000
$HttpTimeoutMilliseconds = 2000
$HttpOk = 200
$LogTailLines = 40
$StopTimeoutMilliseconds = 10000

# The one-shot API start's configuration (environment variables win over every appsettings file).
$EnvironmentName = 'Development'
$Settings = [ordered]@{
    Environment     = 'ASPNETCORE_ENVIRONMENT'
    Urls            = 'ASPNETCORE_URLS'
    Connection      = 'ConnectionStrings__Starter'
    Migrate         = 'Database__ApplyMigrationsOnStartup'
    BreakGlassUser  = 'Identity__BreakGlass__UserName'
    BreakGlassLogin = 'Identity__BreakGlass__Password'
}

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Exit-Usage([string] $Message) {
    [Console]::Error.WriteLine("Usage: $Message")
    exit $ExitUsage
}

function Exit-Refused([string] $Message) {
    Write-Host "Refused: $Message"
    exit $ExitRefused
}

# The connection string with the database set, or a usage exit.
function Resolve-Connection {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) { Exit-Usage '-ConnectionString is required.' }
    try {
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
    } catch {
        Exit-Usage "-ConnectionString is not a valid SQL Server connection string."
    }
    if (-not [string]::IsNullOrWhiteSpace($DatabaseName)) { $builder[$CatalogKey] = $DatabaseName }
    if ($builder[$CatalogKey] -notmatch $DatabaseNamePattern) {
        Exit-Usage "the database name '$($builder[$CatalogKey])' must match $DatabaseNamePattern (pass -DatabaseName)."
    }
    if (-not (Test-LocalServer $builder[$DataSourceKey])) {
        Exit-Refused "'$($builder[$DataSourceKey])' is not a SQL Server on this machine; deployed databases are created by the DBA kit."
    }
    return $builder
}

# True when the data source names this machine (LocalDB, ., (local), localhost, loopback or the computer name).
function Test-LocalServer([string] $DataSource) {
    $server = ($DataSource -replace $ServerProtocolPrefixes, '').Trim()
    foreach ($separator in $ServerNameSeparators) { $server = $server.Split($separator)[0] }
    $local = @($LocalHostNames + $env:COMPUTERNAME) | ForEach-Object { $_.ToLowerInvariant() }
    return $local -contains $server.Trim().ToLowerInvariant()
}

function Invoke-Scalar([string] $Connection, [string] $Sql, [hashtable] $Parameters = @{}) {
    $sqlConnection = New-Object System.Data.SqlClient.SqlConnection $Connection
    try {
        $sqlConnection.Open()
        $command = $sqlConnection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = $SqlTimeoutSeconds
        foreach ($name in $Parameters.Keys) { [void] $command.Parameters.AddWithValue($name, $Parameters[$name]) }
        return $command.ExecuteScalar()
    } finally {
        $sqlConnection.Dispose()
    }
}

function Get-MasterConnection($Builder) {
    $master = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $Builder.ConnectionString
    $master[$CatalogKey] = $MasterDatabase
    return $master.ConnectionString
}

function Test-DatabaseExists($Builder) {
    $id = Invoke-Scalar (Get-MasterConnection $Builder) 'SELECT DB_ID(@name)' @{ '@name' = $Builder[$CatalogKey] }
    return -not ($id -is [System.DBNull] -or $null -eq $id)
}

# True when the database exists and holds the break-glass administrator.
function Test-Initialized($Builder) {
    if (-not (Test-DatabaseExists $Builder)) { return $false }
    return (Get-BreakGlassCount $Builder) -gt 0
}

# Creates the empty database when it is missing: the API's Codelab.Security SQL stores prepare their schema in a hosted
# service that starts before the migrations, so the database must exist first.
function New-EmptyDatabase($Builder) {
    if (Test-DatabaseExists $Builder) { return }
    [void] (Invoke-Scalar (Get-MasterConnection $Builder) 'DECLARE @sql nvarchar(max) = N''CREATE DATABASE '' + QUOTENAME(@name); EXEC (@sql);' @{ '@name' = $Builder[$CatalogKey] })
    Write-Host "Created the empty database '$($Builder[$CatalogKey])'."
}

function Get-BreakGlassCount($Builder) {
    $table = Invoke-Scalar $Builder.ConnectionString 'SELECT OBJECT_ID(@table, N''U'')' @{ '@table' = $AccountsTable }
    if ($table -is [System.DBNull] -or $null -eq $table) { return 0 }
    return [int] (Invoke-Scalar $Builder.ConnectionString "SELECT COUNT(*) FROM $AccountsTable WHERE $BreakGlassColumn = 1")
}

function Get-MigrationHistoryCount($Builder) {
    return [int] (Invoke-Scalar $Builder.ConnectionString "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'%MigrationsHistory%'")
}

function Resolve-Dotnet {
    if (-not [string]::IsNullOrWhiteSpace($Dotnet)) { return $Dotnet }
    $configured = [Environment]::GetEnvironmentVariable($DotnetEnvironmentVariable)
    if (-not [string]::IsNullOrWhiteSpace($configured)) { return $configured }
    return $DefaultDotnet
}

function Get-FreePort {
    $listener = New-Object System.Net.Sockets.TcpListener ([System.Net.IPAddress]::Loopback), 0
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

function Test-Live([string] $Url) {
    try {
        $request = [System.Net.WebRequest]::Create($Url)
        $request.Timeout = $HttpTimeoutMilliseconds
        $response = $request.GetResponse()
        try { return [int] $response.StatusCode -eq $HttpOk } finally { $response.Dispose() }
    } catch {
        return $false
    }
}

function Write-LogTail([string] $File) {
    if (Test-Path -LiteralPath $File) { Get-Content -LiteralPath $File -Tail $LogTailLines | ForEach-Object { Write-Host "  $_" } }
}

# Builds the API into the artifacts folder and returns the folder holding Starter.Api.dll.
function Build-Api([string] $DotnetExe, [string] $Artifacts) {
    $project = Join-Path $RepoRoot $ApiProjectRelativePath
    Write-Host "Building the API into $Artifacts ..."
    & $DotnetExe build $project -c $BuildConfiguration --artifacts-path $Artifacts -nologo -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { Exit-Refused "the API build failed (exit $LASTEXITCODE)." }
    $output = Join-Path $Artifacts "bin\$ApiAssemblyName\$BuildConfiguration"
    if (-not (Test-Path -LiteralPath (Join-Path $output "$ApiAssemblyName.dll"))) { Exit-Refused "the API build left no $ApiAssemblyName.dll in $output." }
    return $output
}

# Starts the built API once with $Environment (restoring this process's variables afterwards); returns the process.
function Start-Api([string] $DotnetExe, [string] $Output, [hashtable] $Environment, [string] $LogBase) {
    $saved = @{}
    foreach ($name in $Environment.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $Environment[$name])
    }
    try {
        return Start-Process -FilePath $DotnetExe -ArgumentList "`"$ApiAssemblyName.dll`"" -WorkingDirectory $Output -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput "$LogBase.out.log" -RedirectStandardError "$LogBase.err.log"
    } finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
}

# Waits for /health/live (startup tasks done) or the process to end; returns $true when the API came up.
function Wait-Live($Process, [string] $Url) {
    $deadline = [System.Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if ($Process.HasExited) { return $false }
        if (Test-Live $Url) { return $true }
        Start-Sleep -Milliseconds $PollIntervalMilliseconds
    }
    return $false
}

function Stop-Api($Process) {
    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force
        [void] $Process.WaitForExit($StopTimeoutMilliseconds)
    }
}

function Save-BreakGlassPassword([string] $File, [string] $Password) {
    $document = Read-JsonObject $File
    if ($null -eq $document) { Exit-Refused "'$File' is not a JSON object; the break-glass password was not saved." }
    $identity = Get-MemberValue $document 'Identity'
    if ($null -eq $identity) { Set-MemberValue $document 'Identity' ([pscustomobject] @{}); $identity = $document.Identity }
    $breakGlass = Get-MemberValue $identity 'BreakGlass'
    if ($null -eq $breakGlass) { Set-MemberValue $identity 'BreakGlass' ([pscustomobject] @{}); $breakGlass = $identity.BreakGlass }
    Set-MemberValue $breakGlass 'UserName' (Get-BreakGlassUserName $document)
    Set-MemberValue $breakGlass 'Password' $Password
    Write-JsonFile $File $document
}

# Saves the connection string as ConnectionStrings:Starter in the local secrets file (gitignored).
function Save-ConnectionString([string] $File, [string] $Connection) {
    $document = Read-JsonObject $File
    if ($null -eq $document) { Exit-Refused "'$File' is not a JSON object; the connection string was not saved." }
    $section = Get-MemberValue $document $ConnectionStringsSection
    if ($null -eq $section) { Set-MemberValue $document $ConnectionStringsSection ([pscustomobject] @{}); $section = Get-MemberValue $document $ConnectionStringsSection }
    Set-MemberValue $section $ConnectionStringName $Connection
    Write-JsonFile $File $document
    Write-Host "Saved the connection string as ${ConnectionStringsSection}:$ConnectionStringName in $File."
}

# Deletes the one-shot start's copy of the secrets file (it holds every local key and password).
function Remove-SecretsCopy([string] $File) {
    if (Test-Path -LiteralPath $File) { Remove-Item -LiteralPath $File -Force }
}

function Get-BreakGlassUserName($Document) {
    $configured = Get-MemberValue (Get-MemberValue (Get-MemberValue $Document 'Identity') 'BreakGlass') 'UserName'
    if ($configured -is [string] -and $configured -ne '') { return $configured }
    return $BreakGlassUserName
}

$builder = Resolve-Connection
if ([string]::IsNullOrWhiteSpace($SecretsPath)) { $SecretsPath = (Join-Path $RepoRoot $DefaultSecretsRelativePath) }
$secretsFile = [System.IO.Path]::GetFullPath($SecretsPath)
$secrets = if (Test-Path -LiteralPath $secretsFile) { Read-JsonObject $secretsFile } else { $null }
if ($null -eq $secrets) { Exit-Refused "no local secrets file at '$secretsFile'; run tools\devseed\New-LocalSecrets.ps1 first." }
if ($TimeoutSeconds -le 0) { Exit-Usage '-TimeoutSeconds must be positive.' }

$database = $builder[$CatalogKey]
try {
    $initialized = Test-Initialized $builder
} catch {
    Exit-Refused "cannot reach '$($builder[$DataSourceKey])': $($_.Exception.Message)"
}
if ($initialized) {
    Write-Host "Database '$database' on '$($builder[$DataSourceKey])' already holds the break-glass administrator; skipped (no password printed)."
    Save-ConnectionString $secretsFile $builder.ConnectionString
    exit $ExitDone
}

$dotnetExe = Resolve-Dotnet
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) { $ArtifactsPath = Join-Path ([System.IO.Path]::GetTempPath()) $DefaultArtifactsFolder }
$artifacts = [System.IO.Path]::GetFullPath($ArtifactsPath)
$output = Build-Api $dotnetExe $artifacts
$newBreakGlassPassword = (New-Password)
$userName = Get-BreakGlassUserName $secrets
$baseUrl = "http://${LoopbackAddress}:$(Get-FreePort)"
$environment = @{
    $Settings.Environment      = $EnvironmentName
    $Settings.Urls             = $baseUrl
    $Settings.Connection       = $builder.ConnectionString
    $Settings.Migrate          = 'true'
    $Settings.BreakGlassUser   = $userName
    $Settings.BreakGlassLogin  = $newBreakGlassPassword
}
$logBase = Join-Path $artifacts "initialize-$database"
# The one-shot start reads exactly this secrets file (the content root is the build output); the copy is removed in
# the finally block, whatever happens.
$copiedSettingsFile = Join-Path $output $SecretsFileName
$live = $false
$process = $null
try {
    Copy-Item -LiteralPath $secretsFile -Destination $copiedSettingsFile -Force
    New-EmptyDatabase $builder
    Write-Host "Applying every migration to '$database' and creating the break-glass administrator (API on $baseUrl) ..."
    $process = Start-Api $dotnetExe $output $environment $logBase
    $live = Wait-Live $process "$baseUrl$LivePath"
} finally {
    if ($null -ne $process) { Stop-Api $process }
    Remove-SecretsCopy $copiedSettingsFile
}
if (-not $live) {
    Write-Host 'The API did not come up; its log ends with:'
    Write-LogTail "$logBase.out.log"
    Write-LogTail "$logBase.err.log"
    Exit-Refused "the one-shot API start failed or timed out after $TimeoutSeconds s (logs: $logBase.*.log)."
}
if ((Get-BreakGlassCount $builder) -eq 0) { Exit-Refused "the API started but '$database' holds no break-glass administrator." }

Save-BreakGlassPassword $secretsFile $newBreakGlassPassword
Save-ConnectionString $secretsFile $builder.ConnectionString
Write-Host "Database '$database' created: $(Get-MigrationHistoryCount $builder) migration history tables, break-glass administrator '$userName'."
Write-Host "One-time break-glass password (shown once; saved as Identity:BreakGlass:Password in $secretsFile): $newBreakGlassPassword"
exit $ExitDone
