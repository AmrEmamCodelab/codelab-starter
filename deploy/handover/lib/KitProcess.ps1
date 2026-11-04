# Toolchain resolution, native process calls and scoped environment overrides.

# A usage error (bad argument, unsafe output folder) is an exception marked in its Data; the scripts exit ExitUsage on it.
function Stop-KitUsage([string] $Message) {
    $exception = [System.InvalidOperationException]::new($Message)
    $exception.Data[$script:UsageErrorMarker] = $true
    throw $exception
}

function Test-KitUsageError([System.Exception] $Exception) {
    for ($current = $Exception; $null -ne $current; $current = $current.InnerException) {
        if ($current.Data.Contains($script:UsageErrorMarker)) { return $true }
    }
    return $false
}

# -File passes '-Projects a,b' as one string; every value is split on commas and trimmed.
function Split-KitListArgument([string[]] $Values) {
    return @($Values | Where-Object { $_ } | ForEach-Object { $_.Split(',') } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Resolve-KitDotnet {
    $override = [Environment]::GetEnvironmentVariable($script:DotnetOverrideEnvVar)
    if ($override) {
        if (-not (Test-Path -LiteralPath $override)) {
            Stop-KitUsage "$($script:DotnetOverrideEnvVar) points to '$override', which does not exist."
        }
        return $override
    }
    if (Test-Path -LiteralPath $script:PrivateDotnetPath) { return $script:PrivateDotnetPath }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    Stop-KitUsage "dotnet not found: set $($script:DotnetOverrideEnvVar), install to $($script:PrivateDotnetPath), or put dotnet on PATH."
}

# Puts the private Node first on PATH for this process when it exists (CODELAB_NODE_DIR, then the private folder);
# otherwise the node on PATH is used. Returns the npm command to call.
function Resolve-KitNpm {
    $dir = [Environment]::GetEnvironmentVariable($script:NodeDirOverrideEnvVar)
    if (-not $dir -and (Test-Path -LiteralPath (Join-Path $script:PrivateNodeDir $script:NodeExeName))) {
        $dir = $script:PrivateNodeDir
    }
    if ($dir) {
        if (-not (Test-Path -LiteralPath (Join-Path $dir $script:NpmCmdName))) {
            Stop-KitUsage "No $($script:NpmCmdName) in '$dir' ($($script:NodeDirOverrideEnvVar))."
        }
        $env:PATH = $dir + [IO.Path]::PathSeparator + $env:PATH
        return (Join-Path $dir $script:NpmCmdName)
    }
    $onPath = Get-Command $script:NpmCmdName -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    Stop-KitUsage "npm not found: set $($script:NodeDirOverrideEnvVar) or put Node on PATH."
}

# Runs a native command in a working folder; returns ONLY its exit code (output goes to the host).
function Invoke-KitNative([string] $Exe, [string[]] $Arguments, [string] $WorkingDirectory) {
    Write-Host "> [$WorkingDirectory] $Exe $($Arguments -join ' ')" -ForegroundColor DarkGray
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    Push-Location -LiteralPath $WorkingDirectory
    try {
        & $Exe @Arguments | Out-Host
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
        $ErrorActionPreference = $previousPreference
    }
}

# Runs a native command and returns its trimmed standard output (null when it fails).
function Get-KitNativeOutput([string] $Exe, [string[]] $Arguments) {
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Exe @Arguments 2>$null
        if ($LASTEXITCODE -ne $script:ExitSuccess) { return $null }
        return (($output | Out-String).Trim())
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

# Runs $Action with the given environment variables set (a null value removes the variable), then restores them.
function Invoke-KitWithEnvironment([hashtable] $Variables, [scriptblock] $Action) {
    $saved = @{}
    foreach ($name in $Variables.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $Variables[$name])
    }
    try {
        return (& $Action)
    }
    finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
}

# Writes UTF-8 text without a byte-order mark (Windows PowerShell 5.1 Out-File would add one).
function Write-KitText([string] $Path, [string] $Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function New-KitTempFolder([string] $Prefix) {
    $path = Join-Path ([IO.Path]::GetTempPath()) ($Prefix + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

# Removes a temp folder this run created. Never called on a path the run did not create.
function Remove-KitTempFolder([string] $Path) {
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function New-KitEmptyPackagesFolder { return (New-KitTempFolder $script:EmptyPackagesPrefix) }

function New-KitNpmCacheCopyFolder { return (New-KitTempFolder $script:NpmCacheCopyPrefix) }

# The environment Test-HandoverKit.ps1 gives every child process: no reachable proxy, no proxy bypass, an empty
# NUGET_PACKAGES folder and the quiet dotnet settings.
function Get-KitOfflineEnvironment([string] $EmptyPackagesDir) {
    $variables = @{}
    foreach ($name in $script:QuietDotnetEnv.Keys) { $variables[$name] = $script:QuietDotnetEnv[$name] }
    $variables[$script:NuGetPackagesEnvVar] = $EmptyPackagesDir
    foreach ($name in $script:ProxyEnvVars) { $variables[$name] = $script:UnroutableProxy }
    foreach ($name in $script:NoProxyEnvVars) { $variables[$name] = $null }
    return $variables
}

# Names the scripts share with the library (kit layout and exit codes), so neither script repeats a literal.
function Get-KitLayout {
    return [pscustomobject]@{
        ExitSuccess = $script:ExitSuccess; ExitFailure = $script:ExitFailure; ExitUsage = $script:ExitUsage
        NuGetDir = $script:KitNuGetDir; NpmCacheDir = $script:KitNpmCacheDir; NpmFilesDir = $script:KitNpmFilesDir
        NuGetConfigFile = $script:KitNuGetConfigFile; OfflineFile = $script:KitOfflineFile
        PrerequisitesFile = $script:KitPrerequisitesFile; RelativeNuGetSource = './' + $script:KitNuGetDir
        PackageJsonFile = $script:PackageJsonFile; NpmBuildScript = $script:NpmBuildScript; NpmTestScript = $script:NpmTestScript; NpmScriptsKey = $script:NpmScriptsKey
        DefaultFeed = $script:DefaultFeedRelative; FeedNuGetSubdir = $script:FeedNuGetSubdir
    }
}
