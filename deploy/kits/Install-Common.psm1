#Requires -Version 5.1
# Install-Common.psm1: the generic deploy-kit module (build plan phase 6; MODULE.md in this folder).
# Every Install-*.ps1 kit (Attachment now; Api, Web, Gateway and Ops in phase 8) is built on these functions:
#   Merge-AppSettings      keeps every server value, adds only the keys new in the package template
#   Backup-Deployment      timestamped copy of the site folder (+ config files) before any change
#   Test-KitPrerequisites  IIS, WebAdministration, the ASP.NET Core Module and the ASP.NET Core runtime
#   Get-KitIisPlan         the IIS actions an install needs (pure: same state in, same plan out)
#   Set-KitIisApplication  applies that plan (app pool + site or child application), idempotent
#   Copy-KitPayload        robocopy mirror of the package that never touches server settings files or logs
#   Write-KitLog           one console + log-file line per step
# Windows PowerShell 5.1 compatible. Every function supports -WhatIf; under -WhatIf nothing is written.
Set-StrictMode -Version Latest
# Every failure (a cmdlet error or a .NET exception) stops the function that hit it; a kit never continues half-done.
$ErrorActionPreference = 'Stop'

# --- Enums (stored and compared as integers; explicit values) ------------------------------------------------------
enum KitLogLevel {
    Info = 0
    Step = 1
    Success = 2
    Warning = 3
    Error = 4
}

enum KitIisAction {
    None = 0
    CreateFolder = 1
    CreateAppPool = 2
    UpdateAppPool = 3
    CreateSite = 4
    UpdateSite = 5
    CreateApplication = 6
    UpdateApplication = 7
}

# --- Constants ----------------------------------------------------------------------------------------------------
# Configuration key paths use the .NET configuration separator and its case-insensitive matching.
$script:ConfigPathSeparator      = ':'
$script:JsonIndentUnit           = '  '
$script:JsonEmptyObject          = '{}'
$script:JsonEmptyArray           = '[]'
$script:JsonNull                 = 'null'
$script:JsonTrue                 = 'true'
$script:JsonFalse                = 'false'
$script:JsonRoundTripFormat      = 'R'
$script:JsonDateFormat           = 'o'
$script:JsonUnicodeEscapeFormat  = '\u{0:x4}'
$script:JsonFirstPrintableChar   = 0x20
$script:Utf8BomBytes             = [byte[]](0xEF, 0xBB, 0xBF)
$script:WindowsNewLine           = "`r`n"
$script:UnixNewLine              = "`n"
$script:TempFileSuffix           = '.kit-tmp'
$script:JsonErrorReasonPattern   = '^(.*?\(\d+\))'
$script:JsonErrorGenericReason   = 'the parser refused it'
$script:PsCustomObjectTypeName   = 'System.Management.Automation.PSCustomObject'

# Files a payload copy never writes: the server owns its settings (Merge-AppSettings patches them) and its logs.
$script:ServerSettingsFilePatterns = @('appsettings.json', 'appsettings.*.json')
$script:AppOfflineFileName       = 'app_offline.htm'
$script:LogsFolderName           = 'logs'

# robocopy: exit codes 0-7 are success variants (bit flags); 8 and above mean at least one copy failed.
$script:RobocopyExe              = 'robocopy.exe'
$script:RobocopyFailureExitCode  = 8
$script:RobocopyRetryCount       = 3
$script:RobocopyRetryWaitSeconds = 2
$script:RobocopyQuietSwitches    = @('/NFL', '/NDL', '/NJH', '/NJS', '/NP')
$script:RobocopyCopyAllSwitch    = '/E'
$script:RobocopyMirrorSwitch     = '/MIR'
$script:RobocopyExcludeFiles     = '/XF'
$script:RobocopyExcludeDirs      = '/XD'

# Backups: <BackupRoot>\<Name>-<yyyyMMdd-HHmmss>[-n]\{site,config,backup.json}
$script:BackupTimestampFormat    = 'yyyyMMdd-HHmmss'
$script:BackupSiteFolder         = 'site'
$script:BackupConfigFolder       = 'config'
$script:BackupManifestFile       = 'backup.json'
$script:BackupFirstSuffix        = 2
$script:IisConfigRelativePath    = 'System32\inetsrv\config\applicationHost.config'

# Logging.
$script:LogTimestampFormat       = 'yyyy-MM-ddTHH:mm:ss.fffzzz'
$script:LogPrefixes = @{
    [KitLogLevel]::Info    = '[i] '
    [KitLogLevel]::Step    = '[..]'
    [KitLogLevel]::Success = '[OK]'
    [KitLogLevel]::Warning = '[!] '
    [KitLogLevel]::Error   = '[!!]'
}
$script:LogColors = @{
    [KitLogLevel]::Info    = 'Gray'
    [KitLogLevel]::Step    = 'Cyan'
    [KitLogLevel]::Success = 'Green'
    [KitLogLevel]::Warning = 'Yellow'
    [KitLogLevel]::Error   = 'Red'
}

# Prerequisites.
$script:DefaultRuntimeMajor      = 10
$script:AspNetCoreFrameworkName  = 'Microsoft.AspNetCore.App'
$script:DotnetSharedRelative     = 'dotnet\shared'
$script:AspNetCoreModuleRelative = 'System32\inetsrv\aspnetcorev2.dll'
$script:IisRegistryPath          = 'HKLM:\SOFTWARE\Microsoft\InetStp'
$script:IisMajorVersionValue     = 'MajorVersion'
$script:IisServiceName           = 'W3SVC'
$script:WebAdministrationModule  = 'WebAdministration'
$script:CheckAdministrator       = 'Administrator'
$script:CheckIis                 = 'IIS'
$script:CheckWebAdministration   = 'WebAdministration'
$script:CheckAspNetCoreModule    = 'AspNetCoreModuleV2'
$script:CheckAspNetCoreRuntime   = 'AspNetCoreRuntime'

# IIS.
$script:IisRootAppPath           = '/'
$script:IisPathSeparator         = '/'
$script:IisAppPoolsDrive         = 'IIS:\AppPools'
$script:IisSitesDrive            = 'IIS:\Sites'
$script:DefaultManagedRuntime    = ''          # "No Managed Code": ASP.NET Core runs out of the CLR-less pool.
$script:DefaultPipelineMode      = 'Integrated'
$script:DefaultIdentityType      = 'ApplicationPoolIdentity'
$script:PoolPropertyRuntime      = 'managedRuntimeVersion'
$script:PoolPropertyPipeline     = 'managedPipelineMode'
$script:PoolPropertyIdentity     = 'processModel.identityType'
$script:SitePropertyPath         = 'physicalPath'
$script:SitePropertyPool         = 'applicationPool'
$script:NoPortGiven              = 0
$script:MinPort                  = 1
$script:MaxPort                  = 65535
$script:InvalidAppPathSegments   = @('.', '..')
$script:UnknownStateNote         = 'IIS state not read (not elevated): planned as a fresh server'

# ==================================================================================================================
# Write-KitLog
# ==================================================================================================================
function Write-KitLog {
    <#
    .SYNOPSIS
        Writes one kit log line to the console and, when -LogPath is given, appends it to that file (UTF-8).
    .DESCRIPTION
        Under -WhatIf the console line is still shown and the file is not touched. Set a kit-wide default once with
        $PSDefaultParameterValues['Write-KitLog:LogPath'] = <path>.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true, Position = 0)] [AllowEmptyString()] [string] $Message,
        [KitLogLevel] $Level = [KitLogLevel]::Info,
        [string] $LogPath
    )
    $line = '{0} {1}' -f $script:LogPrefixes[$Level], $Message
    Write-Host $line -ForegroundColor $script:LogColors[$Level]
    if ([string]::IsNullOrWhiteSpace($LogPath) -or $WhatIfPreference) { return }
    $stamp = (Get-Date).ToString($script:LogTimestampFormat, [Globalization.CultureInfo]::InvariantCulture)
    $folder = Split-Path -Parent $LogPath
    if ($folder -and -not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force -WhatIf:$false | Out-Null }
    $entry = '{0} {1} {2}{3}' -f $stamp, $Level.ToString().ToUpperInvariant(), $Message, [Environment]::NewLine
    [IO.File]::AppendAllText($LogPath, $entry, (New-Object Text.UTF8Encoding($false)))
}

# ==================================================================================================================
# JSON helpers (Windows PowerShell 5.1: ConvertFrom-Json keeps property order; ConvertTo-Json does not format, so the
# writer below is used for deterministic, indented, order-preserving output that keeps Arabic text unescaped)
# ==================================================================================================================
function Test-KitJsonObject([object] $Value) {
    return ($null -ne $Value -and $Value.GetType().FullName -eq $script:PsCustomObjectTypeName)
}

function Test-KitJsonArray([object] $Value) {
    return ($null -ne $Value -and $Value -isnot [string] -and $Value -is [System.Collections.IList])
}

function ConvertTo-KitJsonString([string] $Text) {
    $builder = New-Object Text.StringBuilder
    [void] $builder.Append('"')
    foreach ($ch in $Text.ToCharArray()) {
        switch ($ch) {
            '"'     { [void] $builder.Append('\"'); continue }
            '\'     { [void] $builder.Append('\\'); continue }
            "`n"    { [void] $builder.Append('\n'); continue }
            "`r"    { [void] $builder.Append('\r'); continue }
            "`t"    { [void] $builder.Append('\t'); continue }
            "`b"    { [void] $builder.Append('\b'); continue }
            "`f"    { [void] $builder.Append('\f'); continue }
            default {
                if ([int] $ch -lt $script:JsonFirstPrintableChar) { [void] $builder.Append(($script:JsonUnicodeEscapeFormat -f [int] $ch)) }
                else { [void] $builder.Append($ch) }
            }
        }
    }
    [void] $builder.Append('"')
    return $builder.ToString()
}

function ConvertTo-KitJsonScalar([object] $Value) {
    $invariant = [Globalization.CultureInfo]::InvariantCulture
    if ($null -eq $Value) { return $script:JsonNull }
    if ($Value -is [string]) { return ConvertTo-KitJsonString $Value }
    if ($Value -is [bool]) { if ($Value) { return $script:JsonTrue } else { return $script:JsonFalse } }
    if ($Value -is [double] -or $Value -is [single]) { return $Value.ToString($script:JsonRoundTripFormat, $invariant) }
    if ($Value -is [datetime]) { return ConvertTo-KitJsonString $Value.ToString($script:JsonDateFormat, $invariant) }
    if ($Value.GetType().IsPrimitive -or $Value -is [decimal]) { return ([IFormattable] $Value).ToString($null, $invariant) }
    return ConvertTo-KitJsonString ([string] $Value)
}

# Serializes a parsed JSON value. -Compress gives one line (used for comparisons); otherwise two-space indentation.
function ConvertTo-KitJson([object] $Value, [string] $NewLine = $script:UnixNewLine, [int] $Depth = 0, [switch] $Compress) {
    $indent = if ($Compress) { '' } else { $script:JsonIndentUnit * ($Depth + 1) }
    $closingIndent = if ($Compress) { '' } else { $script:JsonIndentUnit * $Depth }
    $separator = if ($Compress) { ',' } else { ',' + $NewLine }
    $open = if ($Compress) { '' } else { $NewLine }
    $colon = if ($Compress) { ':' } else { ': ' }
    if ($Value -is [System.Collections.IDictionary]) { $Value = [pscustomobject] $Value }
    if (Test-KitJsonObject $Value) {
        $members = @($Value.PSObject.Properties)
        if ($members.Count -eq 0) { return $script:JsonEmptyObject }
        $parts = foreach ($member in $members) {
            $indent + (ConvertTo-KitJsonString $member.Name) + $colon + (ConvertTo-KitJson -Value $member.Value -NewLine $NewLine -Depth ($Depth + 1) -Compress:$Compress)
        }
        return '{' + $open + (@($parts) -join $separator) + $open + $closingIndent + '}'
    }
    if (Test-KitJsonArray $Value) {
        if ($Value.Count -eq 0) { return $script:JsonEmptyArray }
        $parts = foreach ($item in $Value) {
            $indent + (ConvertTo-KitJson -Value $item -NewLine $NewLine -Depth ($Depth + 1) -Compress:$Compress)
        }
        return '[' + $open + (@($parts) -join $separator) + $open + $closingIndent + ']'
    }
    return ConvertTo-KitJsonScalar $Value
}

# The parser error without the file text it quotes (settings files can hold secrets; logs must not).
function Get-KitJsonErrorReason([string] $Message) {
    $match = [regex]::Match($Message, $script:JsonErrorReasonPattern)
    if ($match.Success) { return $match.Groups[1].Value }
    return $script:JsonErrorGenericReason
}

# Reads a JSON settings file: @{ Value; HasBom; NewLine }. The root must be an object.
function Read-KitJsonFile([string] $Path) {
    $full = Resolve-KitFullPath $Path
    $bytes = [IO.File]::ReadAllBytes($full)
    $hasBom = $bytes.Length -ge $script:Utf8BomBytes.Length -and $bytes[0] -eq $script:Utf8BomBytes[0] -and
        $bytes[1] -eq $script:Utf8BomBytes[1] -and $bytes[2] -eq $script:Utf8BomBytes[2]
    $text = [IO.File]::ReadAllText($full, (New-Object Text.UTF8Encoding($false)))
    try { $value = ConvertFrom-Json -InputObject $text -ErrorAction Stop }
    catch { throw "'$Path' is not valid JSON (comments and trailing commas are not supported): $(Get-KitJsonErrorReason $_.Exception.Message)" }
    if (-not (Test-KitJsonObject $value)) { throw "'$Path' must hold a JSON object at the root." }
    $newLine = if ($text.Contains($script:WindowsNewLine)) { $script:WindowsNewLine } else { $script:UnixNewLine }
    return [pscustomobject]@{ Value = $value; HasBom = $hasBom; NewLine = $newLine }
}

function Join-KitConfigPath([string] $Parent, [string] $Name) {
    if ([string]::IsNullOrEmpty($Parent)) { return $Name }
    return $Parent + $script:ConfigPathSeparator + $Name
}

# Every leaf as "A:B:C" -> compact JSON. Arrays are leaves (a server array is one value); an empty object is a leaf.
function Get-KitJsonLeaves([object] $Node, [string] $Prefix, [hashtable] $Into) {
    if (Test-KitJsonObject $Node) {
        $members = @($Node.PSObject.Properties)
        if ($members.Count -eq 0) { if ($Prefix) { $Into[$Prefix] = $script:JsonEmptyObject }; return }
        foreach ($member in $members) { Get-KitJsonLeaves -Node $member.Value -Prefix (Join-KitConfigPath $Prefix $member.Name) -Into $Into }
        return
    }
    $Into[$Prefix] = ConvertTo-KitJson -Value $Node -Compress
}

# Adds the template members the server lacks, recursing into objects both sides have. Never changes a server value.
function Add-KitMissingMembers([object] $Server, [object] $Template, [string] $Prefix,
                               [System.Collections.Generic.List[string]] $Added,
                               [System.Collections.Generic.List[string]] $Conflicts) {
    foreach ($member in @($Template.PSObject.Properties)) {
        $path = Join-KitConfigPath $Prefix $member.Name
        $existing = $Server.PSObject.Properties[$member.Name]
        if ($null -eq $existing) {
            $Server | Add-Member -NotePropertyName $member.Name -NotePropertyValue $member.Value
            $Added.Add($path)
            continue
        }
        $templateIsObject = Test-KitJsonObject $member.Value
        $serverIsObject = Test-KitJsonObject $existing.Value
        if ($templateIsObject -and $serverIsObject) {
            Add-KitMissingMembers -Server $existing.Value -Template $member.Value -Prefix $path -Added $Added -Conflicts $Conflicts
        }
        elseif ($templateIsObject -ne $serverIsObject) { $Conflicts.Add($path) }
    }
}

function Test-KitUnderAddedPath([string] $Path, [string[]] $AddedPaths) {
    foreach ($added in $AddedPaths) {
        if ($Path -eq $added -or $Path.StartsWith($added + $script:ConfigPathSeparator, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

# Proves the merged text parses to the server values plus the added keys only.
function Assert-KitMergePreservesServer([object] $Original, [string] $MergedText, [string[]] $AddedPaths, [string] $Path) {
    $before = @{}; $after = @{}
    Get-KitJsonLeaves -Node $Original -Prefix '' -Into $before
    Get-KitJsonLeaves -Node (ConvertFrom-Json -InputObject $MergedText) -Prefix '' -Into $after
    foreach ($key in $before.Keys) {
        # An empty server section that received new keys stops being an empty-object leaf.
        $filledEmptySection = $before[$key] -eq $script:JsonEmptyObject -and
            @($AddedPaths | Where-Object { $_.StartsWith($key + $script:ConfigPathSeparator, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
        if ($before[$key] -ne $after[$key] -and -not $filledEmptySection -and -not (Test-KitUnderAddedPath $key $AddedPaths)) {
            throw "Merging '$Path' would change the server value '$key'; nothing was written."
        }
    }
    foreach ($key in $after.Keys) {
        if (-not $before.ContainsKey($key) -and -not (Test-KitUnderAddedPath $key $AddedPaths)) {
            throw "Merging '$Path' would add the unexpected key '$key'; nothing was written."
        }
    }
}

# .NET file APIs resolve relative paths against the process folder, not the PowerShell location: resolve first.
function Resolve-KitFullPath([string] $Path) {
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

# Writes through a temp file in the same folder and swaps it in, so a failure never leaves a half-written file.
function Write-KitTextFile([string] $Path, [string] $Text, [bool] $WithBom) {
    $full = Resolve-KitFullPath $Path
    $temp = $full + $script:TempFileSuffix
    [IO.File]::WriteAllText($temp, $Text, (New-Object Text.UTF8Encoding($WithBom)))
    if ([IO.File]::Exists($full)) { [IO.File]::Replace($temp, $full, [NullString]::Value) }
    else { [IO.File]::Move($temp, $full) }
}

# ==================================================================================================================
# Merge-AppSettings
# ==================================================================================================================
function Merge-AppSettings {
    <#
    .SYNOPSIS
        Adds to a server settings file only the keys that are new in the package template. Never overwrites.
    .DESCRIPTION
        Objects are merged key by key (case-insensitive, like .NET configuration); scalars and arrays are values, so a
        server array is kept whole. A key the server has with another shape (object vs value) is kept and reported in
        Conflicts. New keys are appended at the end of their section; the server's key order, encoding (BOM or not)
        and line endings are kept. A missing server file is created from the template. Before writing, the merged
        text is re-parsed and compared leaf by leaf with the server file; any difference other than the added keys
        throws and nothing is written. Formatting is normalized (two-space indentation); comments are not supported.
    .OUTPUTS
        [pscustomobject] Path, Created, AddedKeys (string[]), Conflicts (string[]), Written.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)] [string] $TemplatePath,
        [Parameter(Mandatory = $true)] [string] $ServerPath
    )
    if (-not (Test-Path -LiteralPath $TemplatePath -PathType Leaf)) { throw "Template settings file '$TemplatePath' does not exist." }
    $template = Read-KitJsonFile $TemplatePath
    $created = -not (Test-Path -LiteralPath $ServerPath -PathType Leaf)
    $added = New-Object 'System.Collections.Generic.List[string]'
    $conflicts = New-Object 'System.Collections.Generic.List[string]'
    if ($created) {
        $server = [pscustomobject]@{ Value = [pscustomobject]@{}; HasBom = $template.HasBom; NewLine = $template.NewLine }
        $original = [pscustomobject]@{}
    }
    else {
        $server = Read-KitJsonFile $ServerPath
        $original = (Read-KitJsonFile $ServerPath).Value
    }
    Add-KitMissingMembers -Server $server.Value -Template $template.Value -Prefix '' -Added $added -Conflicts $conflicts
    $result = [pscustomobject]@{
        Path = $ServerPath; Created = $created; AddedKeys = [string[]] $added.ToArray()
        Conflicts = [string[]] $conflicts.ToArray(); Written = $false
    }
    foreach ($conflict in $conflicts) { Write-KitLog "$(Split-Path -Leaf $ServerPath): '$conflict' has another shape on the server; the server value is kept." -Level Warning }
    if ($added.Count -eq 0) { Write-KitLog "$(Split-Path -Leaf $ServerPath): no new keys; kept as it is." -Level Success; return $result }
    $text = (ConvertTo-KitJson -Value $server.Value -NewLine $server.NewLine) + $server.NewLine
    Assert-KitMergePreservesServer -Original $original -MergedText $text -AddedPaths $result.AddedKeys -Path $ServerPath
    $action = if ($created) { 'Create from the package template' } else { "Add $($added.Count) new key(s): $($added -join ', ')" }
    if ($PSCmdlet.ShouldProcess($ServerPath, $action)) {
        Write-KitTextFile -Path $ServerPath -Text $text -WithBom $server.HasBom
        $result.Written = $true
        foreach ($key in $added) { Write-KitLog "$(Split-Path -Leaf $ServerPath): added $key" -Level Success }
    }
    return $result
}

# ==================================================================================================================
# robocopy
# ==================================================================================================================
# robocopy reads a trailing backslash before the closing quote as an escaped quote, so folder paths are trimmed.
function ConvertTo-KitRobocopyPath([string] $Path) {
    $full = Resolve-KitFullPath $Path
    if ($full.Length -gt [IO.Path]::GetPathRoot($full).Length) { return $full.TrimEnd('\') }
    return $full
}

function Invoke-KitRobocopy([string] $Source, [string] $Destination, [bool] $Mirror, [string[]] $ExcludeFiles, [string[]] $ExcludeDirectories) {
    $Source = ConvertTo-KitRobocopyPath $Source
    $Destination = ConvertTo-KitRobocopyPath $Destination
    $mode = if ($Mirror) { $script:RobocopyMirrorSwitch } else { $script:RobocopyCopyAllSwitch }
    $arguments = @($Source, $Destination, $mode, "/R:$($script:RobocopyRetryCount)", "/W:$($script:RobocopyRetryWaitSeconds)") + $script:RobocopyQuietSwitches
    if (@($ExcludeFiles).Count -gt 0) { $arguments += @($script:RobocopyExcludeFiles) + $ExcludeFiles }
    if (@($ExcludeDirectories).Count -gt 0) { $arguments += @($script:RobocopyExcludeDirs) + $ExcludeDirectories }
    & $script:RobocopyExe @arguments | Out-Null
    $exitCode = $LASTEXITCODE
    if ($exitCode -ge $script:RobocopyFailureExitCode) { throw "robocopy '$Source' -> '$Destination' failed (exit $exitCode)." }
    return $exitCode
}

# ==================================================================================================================
# Backup-Deployment
# ==================================================================================================================
function Get-KitBackupPath([string] $BackupRoot, [string] $Name, [datetime] $Timestamp) {
    $stem = '{0}-{1}' -f $Name, $Timestamp.ToString($script:BackupTimestampFormat, [Globalization.CultureInfo]::InvariantCulture)
    $candidate = Join-Path $BackupRoot $stem
    $suffix = $script:BackupFirstSuffix
    while (Test-Path -LiteralPath $candidate) { $candidate = Join-Path $BackupRoot ('{0}-{1}' -f $stem, $suffix); $suffix++ }
    return $candidate
}

function Backup-Deployment {
    <#
    .SYNOPSIS
        Copies the deployed folder (logs excluded) and the given config files to <BackupRoot>\<Name>-<timestamp>.
    .DESCRIPTION
        Layout: site\ (the folder), config\ (each -ConfigPath file, plus applicationHost.config with -IncludeIisConfig)
        and backup.json. Returns the backup path; under -WhatIf returns the path it would use and creates nothing.
        Returns $null when there is nothing to back up (a first install: the folder is missing and no config is given).
        Rollback is a copy of site\ back over the folder.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)] [string] $SourcePath,
        [Parameter(Mandatory = $true)] [string] $BackupRoot,
        [Parameter(Mandatory = $true)] [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')] [string] $Name,
        [string[]] $ConfigPath = @(),
        [switch] $IncludeIisConfig,
        [string[]] $ExcludeDirectories = @($script:LogsFolderName),
        [datetime] $Timestamp = (Get-Date)
    )
    $configFiles = @($ConfigPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($IncludeIisConfig) { $configFiles += Join-Path $env:windir $script:IisConfigRelativePath }
    foreach ($file in $configFiles) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Config file '$file' to back up does not exist." } }
    $hasSite = Test-Path -LiteralPath $SourcePath -PathType Container
    if (-not $hasSite -and $configFiles.Count -eq 0) { Write-KitLog "Nothing to back up: '$SourcePath' does not exist (first install)." -Level Info; return $null }
    $backupPath = Get-KitBackupPath -BackupRoot $BackupRoot -Name $Name -Timestamp $Timestamp
    if (-not $PSCmdlet.ShouldProcess($backupPath, "Back up '$SourcePath'")) { return $backupPath }
    New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
    if ($hasSite) { Invoke-KitRobocopy -Source $SourcePath -Destination (Join-Path $backupPath $script:BackupSiteFolder) -Mirror $false -ExcludeFiles @() -ExcludeDirectories $ExcludeDirectories | Out-Null }
    $configFolder = Join-Path $backupPath $script:BackupConfigFolder
    if ($configFiles.Count -gt 0) { New-Item -ItemType Directory -Path $configFolder -Force | Out-Null }
    foreach ($file in $configFiles) { Copy-Item -LiteralPath $file -Destination $configFolder -Force }
    $manifest = [ordered]@{
        source = $SourcePath; createdAt = $Timestamp.ToString($script:JsonDateFormat, [Globalization.CultureInfo]::InvariantCulture)
        siteCopied = $hasSite; configFiles = [object[]] @($configFiles | ForEach-Object { [string] $_ })
    }
    Write-KitTextFile -Path (Join-Path $backupPath $script:BackupManifestFile) -Text ((ConvertTo-KitJson -Value $manifest) + $script:UnixNewLine) -WithBom $false
    Write-KitLog "Backed up '$SourcePath' to '$backupPath'." -Level Success
    return $backupPath
}

# ==================================================================================================================
# Copy-KitPayload
# ==================================================================================================================
function Copy-KitPayload {
    <#
    .SYNOPSIS
        Mirrors a package folder onto the deployed folder with robocopy, never writing or deleting the server's
        appsettings.json / appsettings.*.json, app_offline.htm or logs\ (excluded files are not purged by /MIR).
    .DESCRIPTION
        -NoPurge copies without deleting destination files the package no longer has. Add more server-owned names with
        -ExcludeFiles / -ExcludeDirectories (robocopy matches them by name at any depth). Returns the copy summary;
        under -WhatIf nothing is copied (Applied = $false).
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)] [string] $SourcePath,
        [Parameter(Mandatory = $true)] [string] $DestinationPath,
        [string[]] $ExcludeFiles = @(),
        [string[]] $ExcludeDirectories = @(),
        [switch] $NoPurge
    )
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Container)) { throw "Package folder '$SourcePath' does not exist." }
    $files = @($script:ServerSettingsFilePatterns) + @($script:AppOfflineFileName) + @($ExcludeFiles)
    $folders = @($script:LogsFolderName) + @($ExcludeDirectories)
    $summary = [pscustomobject]@{
        Source = $SourcePath; Destination = $DestinationPath; Mirror = (-not $NoPurge)
        ExcludedFiles = [string[]] $files; ExcludedDirectories = [string[]] $folders; ExitCode = $null; Applied = $false
    }
    $verb = if ($NoPurge) { 'Copy' } else { 'Mirror' }
    if ($PSCmdlet.ShouldProcess($DestinationPath, "$verb '$SourcePath' (server settings and logs kept)")) {
        $summary.ExitCode = Invoke-KitRobocopy -Source $SourcePath -Destination $DestinationPath -Mirror (-not $NoPurge) -ExcludeFiles $files -ExcludeDirectories $folders
        $summary.Applied = $true
        Write-KitLog "Package copied to '$DestinationPath'." -Level Success
    }
    return $summary
}

# ==================================================================================================================
# Test-KitPrerequisites
# ==================================================================================================================
function Get-KitPrerequisiteFacts {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $iisMajor = $null
    $iisKey = Get-ItemProperty -Path $script:IisRegistryPath -Name $script:IisMajorVersionValue -ErrorAction SilentlyContinue
    if ($null -ne $iisKey) { $iisMajor = [int] $iisKey.($script:IisMajorVersionValue) }
    # The 64-bit Program Files also from a 32-bit PowerShell (where ProgramFiles points to the x86 folder).
    $programFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
    $sharedRoot = Join-Path (Join-Path $programFiles $script:DotnetSharedRelative) $script:AspNetCoreFrameworkName
    $runtimes = @()
    if (Test-Path -LiteralPath $sharedRoot) { $runtimes = @(Get-ChildItem -LiteralPath $sharedRoot -Directory | ForEach-Object { $_.Name }) }
    return [pscustomobject]@{
        IsAdministrator           = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        IisMajorVersion           = $iisMajor
        IisServicePresent         = ($null -ne (Get-Service -Name $script:IisServiceName -ErrorAction SilentlyContinue))
        WebAdministrationPresent  = ($null -ne (Get-Module -ListAvailable -Name $script:WebAdministrationModule))
        AspNetCoreModulePresent   = (Test-Path -LiteralPath (Join-Path $env:windir $script:AspNetCoreModuleRelative))
        AspNetCoreRuntimeVersions = [string[]] $runtimes
    }
}

function New-KitCheck([string] $Name, [bool] $Passed, [string] $Detail) {
    return [pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail }
}

function Get-KitRuntimeMajor([string] $Version) {
    $parsed = $null
    $core = ($Version -split '-')[0]
    if ([version]::TryParse($core, [ref] $parsed)) { return $parsed.Major }
    return -1
}

function Test-KitPrerequisites {
    <#
    .SYNOPSIS
        Checks the server can host the kit: IIS (W3SVC + InetStp), the WebAdministration module, the ASP.NET Core
        Module V2 and an ASP.NET Core runtime of -RuntimeMajor (both come with the .NET Hosting Bundle), and with
        -RequireAdministrator an elevated session.
    .DESCRIPTION
        Read-only. -Facts takes a facts object (the shape Get-KitPrerequisiteFacts returns) instead of probing this
        machine; the tests use it. Each failed check is logged as an error with what to install.
    .OUTPUTS
        [pscustomobject] Passed (bool), Checks (Name, Passed, Detail)[].
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [ValidateRange(1, 99)] [int] $RuntimeMajor = $script:DefaultRuntimeMajor,
        [switch] $RequireAdministrator,
        [pscustomobject] $Facts
    )
    if ($null -eq $Facts) { $Facts = Get-KitPrerequisiteFacts }
    $checks = New-Object 'System.Collections.Generic.List[object]'
    if ($RequireAdministrator) {
        $checks.Add((New-KitCheck $script:CheckAdministrator ([bool] $Facts.IsAdministrator) 'Run the kit from an elevated (Run as administrator) PowerShell.'))
    }
    $iisOk = [bool] $Facts.IisServicePresent -and $null -ne $Facts.IisMajorVersion
    $checks.Add((New-KitCheck $script:CheckIis $iisOk 'Install the Web Server (IIS) role with the W3SVC service.'))
    $checks.Add((New-KitCheck $script:CheckWebAdministration ([bool] $Facts.WebAdministrationPresent) 'Install the IIS Management Scripts and Tools feature (WebAdministration module).'))
    $checks.Add((New-KitCheck $script:CheckAspNetCoreModule ([bool] $Facts.AspNetCoreModulePresent) "Install the .NET $RuntimeMajor Hosting Bundle (ASP.NET Core Module V2), then restart IIS."))
    $matching = @($Facts.AspNetCoreRuntimeVersions | Where-Object { (Get-KitRuntimeMajor $_) -eq $RuntimeMajor })
    $runtimeDetail = if ($matching.Count -gt 0) { "Found $($script:AspNetCoreFrameworkName) $($matching -join ', ')." } else { "Install the .NET $RuntimeMajor Hosting Bundle ($($script:AspNetCoreFrameworkName) $RuntimeMajor.x)." }
    $checks.Add((New-KitCheck $script:CheckAspNetCoreRuntime ($matching.Count -gt 0) $runtimeDetail))
    foreach ($check in $checks) {
        if ($check.Passed) { Write-KitLog "Prerequisite $($check.Name): present." -Level Success }
        else { Write-KitLog "Prerequisite $($check.Name) missing: $($check.Detail)" -Level Error }
    }
    $failed = @($checks | Where-Object { -not $_.Passed })
    return [pscustomobject]@{ Passed = ($failed.Count -eq 0); Checks = [object[]] $checks.ToArray() }
}

# ==================================================================================================================
# IIS: state (read-only), plan (pure) and apply
# ==================================================================================================================
# Plain string join: Join-Path needs the IIS: drive to exist, which it does only after WebAdministration loads.
function Join-KitIisPath([string] $Drive, [string] $Name) {
    return $Drive + '\' + $Name
}

function ConvertTo-KitAppPath([string] $AppPath) {
    $trimmed = $AppPath.Trim().Trim($script:IisPathSeparator)
    if ([string]::IsNullOrEmpty($trimmed)) { return $script:IisRootAppPath }
    foreach ($segment in $trimmed.Split($script:IisPathSeparator)) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $script:InvalidAppPathSegments -contains $segment -or $segment.IndexOfAny([char[]] '\:*?"<>|') -ge 0) {
            throw "AppPath '$AppPath' is not a valid IIS application path (use '/' or '/name')."
        }
    }
    return $script:IisPathSeparator + $trimmed
}

function Test-KitSamePath([string] $Left, [string] $Right) {
    if ($null -eq $Left -or $null -eq $Right) { return $false }
    $normalize = { param($p) [Environment]::ExpandEnvironmentVariables($p).TrimEnd('\', '/') }
    return [string]::Equals((& $normalize $Left), (& $normalize $Right), [StringComparison]::OrdinalIgnoreCase)
}

function Get-KitIisState([string] $SiteName, [string] $AppPath, [string] $AppPoolName, [string] $PhysicalPath) {
    $state = [pscustomobject]@{ Known = $false; PhysicalPathExists = (Test-Path -LiteralPath $PhysicalPath); Pool = $null; Site = $null; Application = $null }
    try {
        Import-Module $script:WebAdministrationModule -ErrorAction Stop -Verbose:$false
        $pool = Get-Item -LiteralPath (Join-KitIisPath $script:IisAppPoolsDrive $AppPoolName) -ErrorAction SilentlyContinue
        if ($null -ne $pool) {
            $state.Pool = [pscustomobject]@{
                ManagedRuntimeVersion = [string] $pool.managedRuntimeVersion; ManagedPipelineMode = [string] $pool.managedPipelineMode
                IdentityType = [string] $pool.processModel.identityType
            }
        }
        $site = Get-Item -LiteralPath (Join-KitIisPath $script:IisSitesDrive $SiteName) -ErrorAction SilentlyContinue
        if ($null -ne $site) { $state.Site = [pscustomobject]@{ PhysicalPath = [string] $site.physicalPath; ApplicationPool = [string] $site.applicationPool } }
        if ($null -ne $site -and $AppPath -ne $script:IisRootAppPath) {
            $app = Get-WebApplication -Site $SiteName -Name $AppPath.TrimStart($script:IisPathSeparator) -ErrorAction SilentlyContinue
            if ($null -ne $app) { $state.Application = [pscustomobject]@{ PhysicalPath = [string] $app.physicalPath; ApplicationPool = [string] $app.applicationPool } }
        }
        $state.Known = $true
    }
    catch { Write-KitLog "IIS state could not be read ($($_.Exception.Message)); planning as if nothing exists." -Level Warning }
    return $state
}

function New-KitIisStep([KitIisAction] $Action, [string] $Target, [string[]] $Changes) {
    return [pscustomobject]@{ Step = 0; Action = $Action.ToString(); ActionId = [int] $Action; Target = $Target; Changes = [string[]] @($Changes); Applied = $false }
}

function Get-KitDrift([object] $Current, [hashtable] $Desired, [string[]] $Order) {
    $changes = foreach ($name in $Order) {
        $have = [string] $Current.($Desired[$name].Property)
        $want = [string] $Desired[$name].Value
        if ($have -ne $want) { "$($Desired[$name].Label): '$have' -> '$want'" }
    }
    return [string[]] @($changes)
}

function Get-KitIisPlan {
    <#
    .SYNOPSIS
        Returns the ordered IIS actions that bring the app pool and the site (AppPath '/') or child application to
        the desired state. Pure when -CurrentState is given: the same state always gives the same plan, which is
        what -WhatIf prints. Without -CurrentState it reads IIS read-only (WebAdministration).
    .OUTPUTS
        [pscustomobject[]] Step, Action (KitIisAction name), ActionId (int), Target, Changes (string[]), Applied.
        A single 'None' step when everything is already in place.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)] [string] $SiteName,
        [string] $AppPath = $script:IisRootAppPath,
        [Parameter(Mandatory = $true)] [string] $AppPoolName,
        [Parameter(Mandatory = $true)] [string] $PhysicalPath,
        [int] $Port = $script:NoPortGiven,
        [AllowEmptyString()] [string] $ManagedRuntimeVersion = $script:DefaultManagedRuntime,
        [string] $PipelineMode = $script:DefaultPipelineMode,
        [string] $IdentityType = $script:DefaultIdentityType,
        [pscustomobject] $CurrentState
    )
    $appPath = ConvertTo-KitAppPath $AppPath
    if ($null -eq $CurrentState) { $CurrentState = Get-KitIisState -SiteName $SiteName -AppPath $appPath -AppPoolName $AppPoolName -PhysicalPath $PhysicalPath }
    $isRoot = $appPath -eq $script:IisRootAppPath
    $steps = New-Object 'System.Collections.Generic.List[object]'
    if (-not $CurrentState.PhysicalPathExists) { $steps.Add((New-KitIisStep CreateFolder $PhysicalPath @("create '$PhysicalPath'"))) }

    $poolTarget = Join-KitIisPath $script:IisAppPoolsDrive $AppPoolName
    $desiredPool = @{
        Runtime  = @{ Property = 'ManagedRuntimeVersion'; Label = $script:PoolPropertyRuntime;  Value = $ManagedRuntimeVersion }
        Pipeline = @{ Property = 'ManagedPipelineMode';   Label = $script:PoolPropertyPipeline; Value = $PipelineMode }
        Identity = @{ Property = 'IdentityType';          Label = $script:PoolPropertyIdentity; Value = $IdentityType }
    }
    $poolOrder = @('Runtime', 'Pipeline', 'Identity')
    if ($null -eq $CurrentState.Pool) {
        $settings = foreach ($name in $poolOrder) { "$($desiredPool[$name].Label) '$($desiredPool[$name].Value)'" }
        $steps.Add((New-KitIisStep CreateAppPool $poolTarget @($settings)))
    }
    else {
        $poolChanges = @(Get-KitDrift $CurrentState.Pool $desiredPool $poolOrder)
        if ($poolChanges.Count -gt 0) { $steps.Add((New-KitIisStep UpdateAppPool $poolTarget $poolChanges)) }
    }

    $siteTarget = Join-KitIisPath $script:IisSitesDrive $SiteName
    $appTarget = if ($isRoot) { $siteTarget } else { $siteTarget + $appPath.Replace($script:IisPathSeparator, '\') }
    $appSettings = @("$($script:SitePropertyPath) '$PhysicalPath'", "$($script:SitePropertyPool) '$AppPoolName'")
    if ($null -eq $CurrentState.Site -and -not $CurrentState.Known) {
        # IIS could not be read (not elevated): show what a fresh server needs, never refuse a -WhatIf preview.
        $kind = if ($isRoot) { [KitIisAction]::CreateSite } else { [KitIisAction]::CreateApplication }
        $steps.Add((New-KitIisStep $kind $appTarget (@($appSettings) + $script:UnknownStateNote)))
    }
    elseif ($null -eq $CurrentState.Site) {
        if (-not $isRoot) { throw "Site '$SiteName' does not exist: a child application ('$appPath') goes under an existing site." }
        if ($Port -lt $script:MinPort -or $Port -gt $script:MaxPort) { throw "Site '$SiteName' does not exist: give -Port ($($script:MinPort)-$($script:MaxPort)) to create it." }
        $steps.Add((New-KitIisStep CreateSite $siteTarget (@("port $Port") + $appSettings)))
    }
    else {
        $current = if ($isRoot) { $CurrentState.Site } else { $CurrentState.Application }
        $target = $appTarget
        $changes = @()
        if ($null -ne $current) {
            if (-not (Test-KitSamePath $current.PhysicalPath $PhysicalPath)) { $changes += "$($script:SitePropertyPath): '$($current.PhysicalPath)' -> '$PhysicalPath'" }
            if ($current.ApplicationPool -ne $AppPoolName) { $changes += "$($script:SitePropertyPool): '$($current.ApplicationPool)' -> '$AppPoolName'" }
        }
        if ($null -eq $current) { $steps.Add((New-KitIisStep CreateApplication $target $appSettings)) }
        elseif ($changes.Count -gt 0) {
            $kind = if ($isRoot) { [KitIisAction]::UpdateSite } else { [KitIisAction]::UpdateApplication }
            $steps.Add((New-KitIisStep $kind $target $changes))
        }
    }
    if ($steps.Count -eq 0) { $steps.Add((New-KitIisStep None $siteTarget @('already in place'))) }
    for ($index = 0; $index -lt $steps.Count; $index++) { $steps[$index].Step = $index + 1 }
    return [object[]] $steps.ToArray()
}

function Invoke-KitIisAction([object] $Step, [hashtable] $Desired) {
    Import-Module $script:WebAdministrationModule -ErrorAction Stop -Verbose:$false
    $poolPath = Join-KitIisPath $script:IisAppPoolsDrive $Desired.AppPoolName
    switch ([KitIisAction] $Step.ActionId) {
        ([KitIisAction]::CreateFolder) { New-Item -ItemType Directory -Path $Desired.PhysicalPath -Force | Out-Null }
        ([KitIisAction]::CreateAppPool) { New-WebAppPool -Name $Desired.AppPoolName | Out-Null; Set-KitPoolProperties $poolPath $Desired }
        ([KitIisAction]::UpdateAppPool) { Set-KitPoolProperties $poolPath $Desired }
        ([KitIisAction]::CreateSite) { New-Website -Name $Desired.SiteName -Port $Desired.Port -PhysicalPath $Desired.PhysicalPath -ApplicationPool $Desired.AppPoolName | Out-Null }
        ([KitIisAction]::CreateApplication) {
            New-WebApplication -Site $Desired.SiteName -Name $Desired.AppPath.TrimStart($script:IisPathSeparator) -PhysicalPath $Desired.PhysicalPath -ApplicationPool $Desired.AppPoolName | Out-Null
        }
        { $_ -eq [KitIisAction]::UpdateSite -or $_ -eq [KitIisAction]::UpdateApplication } {
            Set-ItemProperty -LiteralPath $Step.Target -Name $script:SitePropertyPath -Value $Desired.PhysicalPath
            Set-ItemProperty -LiteralPath $Step.Target -Name $script:SitePropertyPool -Value $Desired.AppPoolName
        }
    }
}

function Set-KitPoolProperties([string] $PoolPath, [hashtable] $Desired) {
    Set-ItemProperty -LiteralPath $PoolPath -Name $script:PoolPropertyRuntime -Value $Desired.ManagedRuntimeVersion
    Set-ItemProperty -LiteralPath $PoolPath -Name $script:PoolPropertyPipeline -Value $Desired.PipelineMode
    Set-ItemProperty -LiteralPath $PoolPath -Name $script:PoolPropertyIdentity -Value $Desired.IdentityType
}

function Set-KitIisApplication {
    <#
    .SYNOPSIS
        Creates or updates the app pool and the site (AppPath '/') or child application so they match the parameters.
        Idempotent: a second run plans 'None'. Under -WhatIf returns the plan with Applied = $false and changes nothing.
    .OUTPUTS
        The plan from Get-KitIisPlan, with Applied set on the steps that ran.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)] [string] $SiteName,
        [string] $AppPath = $script:IisRootAppPath,
        [Parameter(Mandatory = $true)] [string] $AppPoolName,
        [Parameter(Mandatory = $true)] [string] $PhysicalPath,
        [int] $Port = $script:NoPortGiven,
        [AllowEmptyString()] [string] $ManagedRuntimeVersion = $script:DefaultManagedRuntime,
        [string] $PipelineMode = $script:DefaultPipelineMode,
        [string] $IdentityType = $script:DefaultIdentityType
    )
    $appPath = ConvertTo-KitAppPath $AppPath
    $desired = @{
        SiteName = $SiteName; AppPath = $appPath; AppPoolName = $AppPoolName; PhysicalPath = $PhysicalPath; Port = $Port
        ManagedRuntimeVersion = $ManagedRuntimeVersion; PipelineMode = $PipelineMode; IdentityType = $IdentityType
    }
    $state = Get-KitIisState -SiteName $SiteName -AppPath $appPath -AppPoolName $AppPoolName -PhysicalPath $PhysicalPath
    if (-not $state.Known -and -not $WhatIfPreference) { throw 'IIS configuration could not be read; run the kit from an elevated PowerShell on a server with IIS. Nothing was changed.' }
    $plan = Get-KitIisPlan -SiteName $SiteName -AppPath $appPath -AppPoolName $AppPoolName -PhysicalPath $PhysicalPath -Port $Port `
        -ManagedRuntimeVersion $ManagedRuntimeVersion -PipelineMode $PipelineMode -IdentityType $IdentityType -CurrentState $state -WhatIf:$false
    foreach ($step in $plan) {
        $detail = "$($step.Action): $($step.Changes -join '; ')"
        if ($step.ActionId -eq [int] [KitIisAction]::None) { Write-KitLog "IIS $($step.Target): already in place." -Level Success; continue }
        if ($PSCmdlet.ShouldProcess($step.Target, $detail)) {
            Invoke-KitIisAction -Step $step -Desired $desired
            $step.Applied = $true
            Write-KitLog "IIS $($step.Target): $detail" -Level Success
        }
    }
    return $plan
}

Export-ModuleMember -Function Merge-AppSettings, Backup-Deployment, Test-KitPrerequisites, Get-KitIisPlan, Set-KitIisApplication, Copy-KitPayload, Write-KitLog
