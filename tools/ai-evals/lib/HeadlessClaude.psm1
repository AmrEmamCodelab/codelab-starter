# HeadlessClaude.psm1: throwaway workspaces and unattended Claude Code runs (`claude -p`) for the kit evals and the
# pipeline proofs (Windows PowerShell 5.1). Contract (tools/ai-evals/MODULE.md):
#   New-ThrowawayWorkspace -Source <repo> -Destination <dir under %TEMP%> [-NodeModules Offline|None]
#   Invoke-HeadlessClaude  -WorkRoot -Prompt -TranscriptPath -MaxTurns -TimeoutMinutes [-HookLogPath]
#                          -> @{ ExitCode; TranscriptPath; DurationSeconds; SkillsInvoked[]; TimedOut; Summary }
#   Remove-ThrowawayWorkspace -Path <dir>   (deletes ONLY a directory New-ThrowawayWorkspace created: marker-verified)
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------
$script:MarkerFileName = '.codelab-throwaway-workspace.json'
$script:MarkerTool = 'codelab-ai-evals'
$script:MarkerSchemaVersion = 1
$script:SnapshotTag = 'eval-base'
$script:SnapshotBranch = 'main'
$script:SnapshotAuthorName = 'codelab-evals'
$script:SnapshotAuthorEmail = 'evals@codelab.invalid'
$script:SnapshotMessage = 'eval snapshot of the source working tree'
$script:ArtifactsFolderName = 'artifacts'
$script:NpmLogFileName = 'npm-ci.log'
$script:GitExecutable = 'git'
$script:RobocopyExecutable = 'robocopy'
$script:NpmExecutable = 'npm.cmd'
$script:CmdExecutable = 'cmd.exe'
$script:TaskKillExecutable = 'taskkill.exe'
$script:DefaultNodeDirectory = 'E:\codelab-tools\node24'
$script:DefaultDotnetPath = 'E:\codelab-tools\dotnet10\dotnet.exe'
$script:DefaultClaudeCommand = 'claude'
$script:ClaudePathVariable = 'CODELAB_CLAUDE'
$script:DotnetVariable = 'CODELAB_DOTNET'
$script:HookLogVariable = 'CODELAB_HOOK_LOG'
$script:EvalRunVariable = 'CODELAB_EVAL_RUN'
$script:PathVariable = 'PATH'
$script:RobocopyFailureExitCode = 8          # robocopy: 0-7 are success variants, 8 and above are failures
$script:MillisecondsPerMinute = 60000
$script:NpmTimeoutMinutes = 20
$script:GitTimeoutMinutes = 10
$script:StderrSuffix = '.stderr.log'
$script:PromptFileSuffix = '.prompt.txt'
$script:ResultTextLimit = 2000
$script:NodeModulesModes = @('Offline', 'None')
$script:PermissionModes = @('acceptEdits', 'dontAsk', 'bypassPermissions', 'auto', 'manual', 'plan')
$script:DefaultPermissionMode = 'acceptEdits'

# Directory names never copied into a workspace (build output, caches, git and installed packages). A tracked file
# under one of them (packages/npm/ng-tools/src/bin/*) is restored by the reconcile pass from `git ls-files`.
$script:ExcludedDirectoryNames = @('.git', 'bin', 'obj', 'artifacts', 'publish', 'node_modules', '.angular', 'dist',
    'TestResults', 'coverage', '.vs')
# Repo-relative directories never copied (agent memory and handoffs stay with the owner).
$script:ExcludedRelativeDirectories = @('.claude\memory', '.claude\handoffs')
# Key material, local secrets and the owner's local settings never leave the source tree.
$script:ExcludedFilePatterns = @('*.pfx', '*.p12', '*.key', '*.pem', 'appsettings.*.local.json', '.env', '.env.*',
    'settings.local.json', '*.binlog')

# The unattended allowlist used with the default acceptEdits mode: file edits inside the workspace are accepted, these
# commands run, anything else is denied (no one answers a prompt in a headless run).
# Every rule names its executable first: no rule starts with a wildcard, none allows the PowerShell call operator over
# any command ("& *") and none allows a write or environment cmdlet (Pester pins this). The private toolchain is
# allowed by its exact folder, bare or through the call operator; node and dotnet also resolve from the child PATH.
$script:DefaultAllowedTools = @(
    'Read', 'Glob', 'Grep', 'Edit', 'Write', 'Skill', 'Task', 'Agent', 'TodoWrite',
    'Bash(node *)', 'Bash(npm *)', 'Bash(npx *)', 'Bash(git *)', 'Bash(dotnet *)',
    'Bash(ls *)', 'Bash(cat *)', 'Bash(find *)', 'Bash(grep *)', 'Bash(head *)', 'Bash(tail *)', 'Bash(wc *)',
    'Bash(mkdir *)', 'Bash(/e/codelab-tools/*)', 'Bash(E:/codelab-tools/*)',
    'PowerShell(node *)', 'PowerShell(npm *)', 'PowerShell(npx *)', 'PowerShell(git *)', 'PowerShell(dotnet *)',
    'PowerShell(E:\codelab-tools\*)', 'PowerShell(& E:\codelab-tools\*)', "PowerShell(& 'E:\codelab-tools\*)",
    'PowerShell(& "E:\codelab-tools\*)', 'PowerShell(Get-ChildItem *)', 'PowerShell(Get-Content *)',
    'PowerShell(Select-String *)', 'PowerShell(Test-Path *)', 'PowerShell(Get-Command *)'
)
# Never in a run, in every permission mode (deny wins over allow): the full CI (owner decision: no full CI inside
# phases), pushes, deletes and history-destroying git commands, and handing a command to another shell.
$script:DefaultDisallowedTools = @(
    'Bash(*build.ps1*)', 'PowerShell(*build.ps1*)', 'Bash(git push*)', 'PowerShell(git push*)',
    'Bash(rm *)', 'Bash(rmdir *)', 'Bash(git clean*)', 'Bash(git reset --hard*)', 'Bash(cmd *)', 'Bash(powershell *)',
    'Bash(pwsh *)', 'PowerShell(Remove-Item *)', 'PowerShell(rm *)', 'PowerShell(rmdir *)', 'PowerShell(del *)',
    'PowerShell(rd *)', 'PowerShell(ri *)', 'PowerShell(erase *)', 'PowerShell(git clean*)',
    'PowerShell(git reset --hard*)', 'PowerShell(cmd *)', 'PowerShell(Invoke-Expression *)', 'PowerShell(iex *)',
    'PowerShell(Start-Process *)'
)

# Stream-json transcript vocabulary (claude -p --output-format stream-json --verbose).
$script:EventTypeAssistant = 'assistant'
$script:EventTypeResult = 'result'
$script:ContentTypeToolUse = 'tool_use'
$script:SkillToolName = 'Skill'
$script:SkillInputKeys = @('skill', 'command', 'name')
$script:ToolUseMarker = '"tool_use"'
$script:ResultMarker = '"result"'

# ---------------------------------------------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------------------------------------------

# Returns a property of a PSCustomObject or hashtable, or $null when it is absent (safe under StrictMode).
function Get-ObjectValue {
    param([object] $InputObject, [Parameter(Mandatory = $true)][string] $Name)
    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) { return $InputObject[$Name] }
        return $null
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

# Full path with a trailing separator removed, for prefix comparisons.
function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.Length -gt 3) { $full = $full.TrimEnd('\', '/') }
    return $full
}

# True when $Path is $Root itself or lies below it (case-insensitive, separator-aware).
function Test-PathIsUnder {
    param([Parameter(Mandatory = $true)][string] $Path, [Parameter(Mandatory = $true)][string] $Root)
    $normalizedPath = Get-NormalizedPath $Path
    $normalizedRoot = Get-NormalizedPath $Root
    if ($normalizedPath.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    $prefix = $normalizedRoot.TrimEnd('\') + '\'
    return $normalizedPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Get-DefaultAllowedRoot { return (Get-NormalizedPath ([IO.Path]::GetTempPath())) }

# Quotes one argument for a Windows command line (CommandLineToArgvW rules).
function ConvertTo-QuotedArgument {
    param([AllowEmptyString()][string] $Argument)
    if ($Argument.Length -gt 0 -and $Argument -notmatch '[\s"]') { return $Argument }
    $backslash = [char] '\'
    $builder = New-Object System.Text.StringBuilder
    [void] $builder.Append('"')
    $backslashes = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq $backslash) { $backslashes++; continue }
        if ($character -eq '"') {
            [void] $builder.Append($backslash, (2 * $backslashes + 1))
            [void] $builder.Append('"')
        } else {
            if ($backslashes -gt 0) { [void] $builder.Append($backslash, $backslashes) }
            [void] $builder.Append($character)
        }
        $backslashes = 0
    }
    if ($backslashes -gt 0) { [void] $builder.Append($backslash, (2 * $backslashes)) }
    [void] $builder.Append('"')
    return $builder.ToString()
}

function ConvertTo-ProcessArgumentString {
    param([string[]] $ArgumentList)
    if ($null -eq $ArgumentList) { return '' }
    return (@($ArgumentList | ForEach-Object { ConvertTo-QuotedArgument $_ }) -join ' ')
}

# Runs a process with stdout to $LogPath and stderr to $LogPath.stderr.log, optional stdin text, extra environment
# variables for the child only, and a timeout that kills the whole process tree.
function Invoke-LoggedProcess {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [string[]] $ArgumentList = @(),
        [Parameter(Mandatory = $true)][string] $WorkingDirectory,
        [Parameter(Mandatory = $true)][string] $LogPath,
        [Parameter(Mandatory = $true)][double] $TimeoutMinutes,
        [string] $StandardInputText,
        [hashtable] $Environment = @{}
    )
    $logFolder = Split-Path -Parent $LogPath
    if ($logFolder -and -not (Test-Path -LiteralPath $logFolder)) { New-Item -ItemType Directory -Path $logFolder -Force | Out-Null }
    $errorLogPath = $LogPath + $script:StderrSuffix

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = ConvertTo-ProcessArgumentString $ArgumentList
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    foreach ($name in $Environment.Keys) { $startInfo.EnvironmentVariables[$name] = [string] $Environment[$name] }

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($startInfo)
    # FileShare.Read: a live transcript or build log can be followed while the process runs.
    $outStream = New-Object IO.FileStream($LogPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $errStream = New-Object IO.FileStream($errorLogPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $timedOut = $false
    try {
        $outCopy = $process.StandardOutput.BaseStream.CopyToAsync($outStream)
        $errCopy = $process.StandardError.BaseStream.CopyToAsync($errStream)
        if ($StandardInputText) {
            $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($StandardInputText)
            $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        }
        $process.StandardInput.Close()
        $timeoutMilliseconds = [int] [Math]::Ceiling($TimeoutMinutes * $script:MillisecondsPerMinute)
        if (-not $process.WaitForExit($timeoutMilliseconds)) {
            $timedOut = $true
            & $script:TaskKillExecutable /PID $process.Id /T /F 2>$null | Out-Null
            $process.WaitForExit()
        }
        $process.WaitForExit()
        [void] $outCopy.Wait()
        [void] $errCopy.Wait()
        $exitCode = $process.ExitCode
    } finally {
        $outStream.Dispose()
        $errStream.Dispose()
        $process.Dispose()
        $stopwatch.Stop()
    }
    return [pscustomobject]@{
        ExitCode        = $exitCode
        TimedOut        = $timedOut
        DurationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 1)
        LogPath         = $LogPath
        ErrorLogPath    = $errorLogPath
    }
}

# Runs git in a directory with a fixed identity and byte-exact line endings; throws on failure.
function Invoke-WorkspaceGit {
    param([Parameter(Mandatory = $true)][string] $Directory, [Parameter(Mandatory = $true)][string[]] $GitArguments)
    $ErrorActionPreference = 'Continue'   # git writes warnings to stderr; the exit code decides
    $arguments = @('-C', $Directory, '-c', "user.name=$script:SnapshotAuthorName", '-c',
        "user.email=$script:SnapshotAuthorEmail", '-c', 'core.autocrlf=false', '-c', 'commit.gpgsign=false') + $GitArguments
    $output = & $script:GitExecutable @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArguments -join ' ') failed in ${Directory}: $($output | Out-String)" }
    return $output
}

# ---------------------------------------------------------------------------------------------------------------
# Throwaway workspace
# ---------------------------------------------------------------------------------------------------------------

# The robocopy argument list for the bulk copy (pure; the reconcile pass restores tracked files under excluded names).
function Get-WorkspaceCopyArguments {
    param([Parameter(Mandatory = $true)][string] $Source, [Parameter(Mandatory = $true)][string] $Destination)
    $excludedDirectories = @($script:ExcludedDirectoryNames) +
        @($script:ExcludedRelativeDirectories | ForEach-Object { Join-Path $Source $_ })
    $excludedFiles = @($script:ExcludedFilePatterns) + @($script:MarkerFileName)
    return @($Source, $Destination, '/E', '/XJ', '/R:1', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/XD') +
        $excludedDirectories + @('/XF') + $excludedFiles
}

# Why $Destination cannot hold a new workspace, or $null when it can.
function Get-WorkspaceDestinationRefusal {
    param([Parameter(Mandatory = $true)][string] $Source, [Parameter(Mandatory = $true)][string] $Destination,
        [Parameter(Mandatory = $true)][string] $AllowedRoot)
    $normalizedDestination = Get-NormalizedPath $Destination
    $normalizedRoot = Get-NormalizedPath $AllowedRoot
    if ($normalizedDestination.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return "the destination is the allowed root itself ($normalizedRoot); use a new sub-folder"
    }
    if (-not (Test-PathIsUnder -Path $normalizedDestination -Root $normalizedRoot)) {
        return "the destination $normalizedDestination is not under the allowed root $normalizedRoot (%TEMP%)"
    }
    if (Test-PathIsUnder -Path $normalizedDestination -Root $Source) {
        return "the destination $normalizedDestination is inside the source repository"
    }
    if (Test-PathIsUnder -Path $Source -Root $normalizedDestination) {
        return "the source repository is inside the destination $normalizedDestination"
    }
    if (Test-Path -LiteralPath $normalizedDestination) {
        if (@(Get-ChildItem -LiteralPath $normalizedDestination -Force).Count -gt 0) {
            return "the destination $normalizedDestination exists and is not empty"
        }
    }
    return $null
}

function New-WorkspaceMarker {
    param([Parameter(Mandatory = $true)][string] $Path, [Parameter(Mandatory = $true)][string] $Source)
    $marker = [ordered]@{
        tool          = $script:MarkerTool
        schemaVersion = $script:MarkerSchemaVersion
        id            = [guid]::NewGuid().ToString('N')
        path          = (Get-NormalizedPath $Path)
        source        = (Get-NormalizedPath $Source)
        createdUtc    = [DateTime]::UtcNow.ToString('o')
    }
    $json = $marker | ConvertTo-Json -Depth 3
    [IO.File]::WriteAllText((Join-Path $Path $script:MarkerFileName), $json, (New-Object Text.UTF8Encoding($false)))
    return [pscustomobject] $marker
}

# Why $Path must not be deleted, or $null when it is a workspace this module created (marker present, valid, and
# naming this very path; under the allowed root; not a drive root).
function Get-WorkspaceRemovalRefusal {
    param([Parameter(Mandatory = $true)][string] $Path, [Parameter(Mandatory = $true)][string] $AllowedRoot)
    $normalized = Get-NormalizedPath $Path
    if ([IO.Path]::GetPathRoot($normalized).TrimEnd('\').Equals($normalized.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        return "$normalized is a drive root"
    }
    $normalizedRoot = Get-NormalizedPath $AllowedRoot
    if ($normalized.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-PathIsUnder -Path $normalized -Root $normalizedRoot)) {
        return "$normalized is not below the allowed root $normalizedRoot"
    }
    $item = Get-Item -LiteralPath $normalized -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { return "$normalized does not exist" }
    if (-not $item.PSIsContainer) { return "$normalized is not a directory" }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return "$normalized is a junction or link" }
    $markerPath = Join-Path $normalized $script:MarkerFileName
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) { return "$normalized has no workspace marker" }
    try {
        $marker = [IO.File]::ReadAllText($markerPath) | ConvertFrom-Json
    } catch {
        return "$normalized has an unreadable workspace marker"
    }
    if ((Get-ObjectValue $marker 'tool') -ne $script:MarkerTool) { return "$normalized has a foreign workspace marker" }
    $markerPathValue = [string] (Get-ObjectValue $marker 'path')
    if (-not $markerPathValue -or -not $markerPathValue.Equals($normalized, [StringComparison]::OrdinalIgnoreCase)) {
        return "the workspace marker in $normalized names another path ($markerPathValue): it was copied, not created here"
    }
    return $null
}

function Test-ThrowawayWorkspace {
    param([Parameter(Mandatory = $true)][string] $Path, [string] $AllowedRoot = (Get-DefaultAllowedRoot))
    return ($null -eq (Get-WorkspaceRemovalRefusal -Path $Path -AllowedRoot $AllowedRoot))
}

function Test-ExcludedFileName {
    param([Parameter(Mandatory = $true)][string] $FileName)
    foreach ($pattern in @($script:ExcludedFilePatterns) + @($script:MarkerFileName)) { if ($FileName -like $pattern) { return $true } }
    return $false
}

# Copies every tracked or untracked-not-ignored file the bulk copy skipped (excluded directory names).
function Copy-MissingTrackedFiles {
    param([Parameter(Mandatory = $true)][string] $Source, [Parameter(Mandatory = $true)][string] $Destination)
    $ErrorActionPreference = 'Continue'
    $listing = & $script:GitExecutable -C $Source ls-files -co --exclude-standard 2>$null
    if ($LASTEXITCODE -ne 0) { return 0 }
    $restored = 0
    foreach ($relative in @($listing)) {
        if (-not $relative) { continue }
        $windowsRelative = $relative -replace '/', '\'
        $target = Join-Path $Destination $windowsRelative
        if (Test-Path -LiteralPath $target) { continue }
        if (Test-ExcludedFileName (Split-Path -Leaf $windowsRelative)) { continue }   # key material even when not ignored
        $origin = Join-Path $Source $windowsRelative
        if (-not (Test-Path -LiteralPath $origin -PathType Leaf)) { continue }   # deleted in the working tree
        $targetFolder = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetFolder)) { New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null }
        Copy-Item -LiteralPath $origin -Destination $target
        $restored++
    }
    return $restored
}

function New-ThrowawayWorkspace {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $Source,
        [Parameter(Mandatory = $true)][string] $Destination,
        [ValidateSet('Offline', 'None')][string] $NodeModules = 'Offline',
        [string] $AllowedRoot = (Get-DefaultAllowedRoot),
        [string] $NodeDirectory = $script:DefaultNodeDirectory
    )
    $sourcePath = Get-NormalizedPath $Source
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) { throw "Source $sourcePath does not exist." }
    $refusal = Get-WorkspaceDestinationRefusal -Source $sourcePath -Destination $Destination -AllowedRoot $AllowedRoot
    if ($refusal) { throw "New-ThrowawayWorkspace refused: $refusal." }
    $destinationPath = Get-NormalizedPath $Destination
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    # The marker goes first, so a failed copy below can still be cleaned up by Remove-ThrowawayWorkspace.
    $marker = New-WorkspaceMarker -Path $destinationPath -Source $sourcePath

    $copyArguments = Get-WorkspaceCopyArguments -Source $sourcePath -Destination $destinationPath
    & $script:RobocopyExecutable @copyArguments | Out-Null
    if ($LASTEXITCODE -ge $script:RobocopyFailureExitCode) { throw "robocopy failed with exit code $LASTEXITCODE." }
    $restored = Copy-MissingTrackedFiles -Source $sourcePath -Destination $destinationPath

    Invoke-WorkspaceGit -Directory $destinationPath -GitArguments @('init', '-q', '-b', $script:SnapshotBranch) | Out-Null
    $excludeFile = Join-Path $destinationPath '.git\info\exclude'
    Add-Content -LiteralPath $excludeFile -Value @("/$script:MarkerFileName", "/$script:ArtifactsFolderName/") -Encoding ASCII
    Invoke-WorkspaceGit -Directory $destinationPath -GitArguments @('add', '-A') | Out-Null
    Invoke-WorkspaceGit -Directory $destinationPath -GitArguments @('commit', '-q', '-m', $script:SnapshotMessage) | Out-Null
    Invoke-WorkspaceGit -Directory $destinationPath -GitArguments @('tag', $script:SnapshotTag) | Out-Null

    $npmExitCode = $null
    if ($NodeModules -eq 'Offline') {
        $npmLog = Join-Path (Join-Path $destinationPath $script:ArtifactsFolderName) $script:NpmLogFileName
        $environment = @{ $script:PathVariable = "$NodeDirectory;$env:PATH" }
        $npm = Join-Path $NodeDirectory $script:NpmExecutable
        if (-not (Test-Path -LiteralPath $npm)) { $npm = $script:NpmExecutable }
        $run = Invoke-LoggedProcess -FilePath $npm -ArgumentList @('ci', '--offline', '--no-audit', '--no-fund') `
            -WorkingDirectory $destinationPath -LogPath $npmLog -TimeoutMinutes $script:NpmTimeoutMinutes -Environment $environment
        $npmExitCode = $run.ExitCode
        if ($run.ExitCode -ne 0 -or $run.TimedOut) {
            throw "npm ci --offline failed (exit $($run.ExitCode), timed out: $($run.TimedOut)); log $npmLog. The workspace $destinationPath still carries its marker: remove it with Remove-ThrowawayWorkspace."
        }
    }
    return [pscustomobject]@{
        Path          = $destinationPath
        MarkerId      = $marker.id
        BaseRef       = $script:SnapshotTag
        ArtifactsPath = (Join-Path $destinationPath $script:ArtifactsFolderName)
        NodeModules   = $NodeModules
        NpmExitCode   = $npmExitCode
        RestoredFiles = $restored
    }
}

function Remove-ThrowawayWorkspace {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string] $Path, [string] $AllowedRoot = (Get-DefaultAllowedRoot))
    $refusal = Get-WorkspaceRemovalRefusal -Path $Path -AllowedRoot $AllowedRoot
    if ($refusal) { throw "Remove-ThrowawayWorkspace refused: $refusal." }
    $normalized = Get-NormalizedPath $Path
    $ErrorActionPreference = 'Continue'
    # rmdir /s /q removes junctions and symlinks as links: it never deletes the folder a link points to (npm
    # workspace links, or a link an agent created inside the workspace).
    & $script:CmdExecutable /d /c rmdir /s /q "`"$normalized`"" 2>&1 | Out-Null
    if (Test-Path -LiteralPath $normalized) { throw "Remove-ThrowawayWorkspace could not delete $normalized completely." }
}

# ---------------------------------------------------------------------------------------------------------------
# Headless Claude
# ---------------------------------------------------------------------------------------------------------------

function Resolve-ClaudeCommand {
    param([string] $ClaudePath)
    if ($ClaudePath) { return $ClaudePath }
    $fromEnvironment = [Environment]::GetEnvironmentVariable($script:ClaudePathVariable)
    if ($fromEnvironment) { return $fromEnvironment }
    $command = Get-Command $script:DefaultClaudeCommand -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) { throw "claude was not found on PATH; set $script:ClaudePathVariable." }
    return $command.Source
}

# The claude argument list (pure). The prompt is sent on stdin, never on the command line.
function Get-HeadlessClaudeArguments {
    param(
        [Parameter(Mandatory = $true)][int] $MaxTurns,
        [string] $PermissionMode = $script:DefaultPermissionMode,
        [string[]] $AllowedTools = $script:DefaultAllowedTools,
        [string] $Model
    )
    if ($script:PermissionModes -notcontains $PermissionMode) { throw "Unknown permission mode '$PermissionMode'." }
    if ($MaxTurns -lt 1) { throw 'MaxTurns must be at least 1.' }
    $arguments = @('-p', '--output-format', 'stream-json', '--verbose', '--max-turns', [string] $MaxTurns,
        '--permission-mode', $PermissionMode, '--no-session-persistence', '--strict-mcp-config')
    if ($PermissionMode -ne 'bypassPermissions' -and $AllowedTools -and $AllowedTools.Count -gt 0) {
        $arguments += @('--allowedTools') + $AllowedTools
    }
    $arguments += @('--disallowedTools') + $script:DefaultDisallowedTools   # deny rules hold in every mode
    if ($Model) { $arguments += @('--model', $Model) }
    return $arguments
}

# The child environment (pure): node24 then the private .NET 10 SDK first on PATH (so plain node and dotnet resolve to them), CODELAB_DOTNET, the hook log and the eval-run flag.
function Get-HeadlessEnvironment {
    param(
        [string] $HookLogPath,
        [string] $NodeDirectory = $script:DefaultNodeDirectory,
        [string] $DotnetPath = $script:DefaultDotnetPath,
        [string] $CurrentPath = $env:PATH,
        [hashtable] $ExtraEnvironment = @{}
    )
    $environment = @{
        $script:PathVariable    = "$NodeDirectory;$([IO.Path]::GetDirectoryName($DotnetPath));$CurrentPath"
        $script:DotnetVariable  = $DotnetPath
        $script:EvalRunVariable = '1'
    }
    if ($HookLogPath) { $environment[$script:HookLogVariable] = $HookLogPath }
    foreach ($name in $ExtraEnvironment.Keys) { $environment[$name] = [string] $ExtraEnvironment[$name] }
    return $environment
}

# Reads a stream-json transcript: skills invoked through the Skill tool (in call order, distinct), tool-use counts and
# the final result event. Lines that are not JSON are counted, never fatal.
function Get-HeadlessTranscriptSummary {
    param([Parameter(Mandatory = $true)][string] $TranscriptPath)
    $skills = New-Object System.Collections.Generic.List[string]
    $toolCounts = @{}
    $unparsed = 0
    $result = $null
    if (Test-Path -LiteralPath $TranscriptPath -PathType Leaf) {
        foreach ($line in [IO.File]::ReadLines($TranscriptPath)) {
            if (-not $line.Contains($script:ToolUseMarker) -and -not $line.Contains($script:ResultMarker)) { continue }
            try { $event = $line | ConvertFrom-Json } catch { $unparsed++; continue }
            $type = Get-ObjectValue $event 'type'
            if ($type -eq $script:EventTypeResult) { $result = $event; continue }
            if ($type -ne $script:EventTypeAssistant) { continue }
            $content = Get-ObjectValue (Get-ObjectValue $event 'message') 'content'
            foreach ($item in @($content)) {
                if ((Get-ObjectValue $item 'type') -ne $script:ContentTypeToolUse) { continue }
                $toolName = [string] (Get-ObjectValue $item 'name')
                if ($toolCounts.ContainsKey($toolName)) { $toolCounts[$toolName]++ } else { $toolCounts[$toolName] = 1 }
                if ($toolName -ne $script:SkillToolName) { continue }
                $skill = ConvertTo-SkillName (Get-ObjectValue $item 'input')
                if ($skill -and -not $skills.Contains($skill)) { $skills.Add($skill) }
            }
        }
    }
    $resultText = [string] (Get-ObjectValue $result 'result')
    if ($resultText.Length -gt $script:ResultTextLimit) { $resultText = $resultText.Substring(0, $script:ResultTextLimit) }
    return [pscustomobject]@{
        SkillsInvoked     = @($skills)
        ToolUseCounts     = $toolCounts
        UnparsedLines     = $unparsed
        HasResult         = ($null -ne $result)
        ResultSubtype     = [string] (Get-ObjectValue $result 'subtype')
        IsError           = [bool] (Get-ObjectValue $result 'is_error')
        NumTurns          = Get-ObjectValue $result 'num_turns'
        TotalCostUsd      = Get-ObjectValue $result 'total_cost_usd'
        PermissionDenials = @(Get-ObjectValue $result 'permission_denials').Where({ $null -ne $_ }).Count
        ResultText        = $resultText
    }
}

# The skill name of a Skill tool input: '/new-lookup', 'new-lookup' and 'plugin:new-lookup args' all give 'new-lookup'.
function ConvertTo-SkillName {
    param([object] $SkillInput)
    foreach ($key in $script:SkillInputKeys) {
        $value = [string] (Get-ObjectValue $SkillInput $key)
        if (-not $value) { continue }
        $name = ($value.Trim().TrimStart('/') -split '\s+')[0]
        $colon = $name.LastIndexOf(':')
        if ($colon -ge 0) { $name = $name.Substring($colon + 1) }
        return $name.ToLowerInvariant()
    }
    return $null
}

function Invoke-HeadlessClaude {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $WorkRoot,
        [Parameter(Mandatory = $true)][string] $Prompt,
        [Parameter(Mandatory = $true)][string] $TranscriptPath,
        [Parameter(Mandatory = $true)][int] $MaxTurns,
        [Parameter(Mandatory = $true)][double] $TimeoutMinutes,
        [string] $HookLogPath,
        [string] $ClaudePath,
        [string] $PermissionMode = $script:DefaultPermissionMode,
        [string[]] $AllowedTools = $script:DefaultAllowedTools,
        [string] $Model,
        [string] $NodeDirectory = $script:DefaultNodeDirectory,
        [string] $DotnetPath = $script:DefaultDotnetPath,
        [hashtable] $ExtraEnvironment = @{}
    )
    if (-not (Test-Path -LiteralPath $WorkRoot -PathType Container)) { throw "WorkRoot $WorkRoot does not exist." }
    if ($HookLogPath) {
        $hookFolder = Split-Path -Parent $HookLogPath
        if ($hookFolder -and -not (Test-Path -LiteralPath $hookFolder)) { New-Item -ItemType Directory -Path $hookFolder -Force | Out-Null }
    }
    $claude = Resolve-ClaudeCommand -ClaudePath $ClaudePath
    $arguments = Get-HeadlessClaudeArguments -MaxTurns $MaxTurns -PermissionMode $PermissionMode -AllowedTools $AllowedTools -Model $Model
    $environment = Get-HeadlessEnvironment -HookLogPath $HookLogPath -NodeDirectory $NodeDirectory -DotnetPath $DotnetPath `
        -ExtraEnvironment $ExtraEnvironment
    $run = Invoke-LoggedProcess -FilePath $claude -ArgumentList $arguments -WorkingDirectory $WorkRoot -LogPath $TranscriptPath `
        -TimeoutMinutes $TimeoutMinutes -StandardInputText $Prompt -Environment $environment
    $summary = Get-HeadlessTranscriptSummary -TranscriptPath $TranscriptPath
    return [pscustomobject]@{
        ExitCode        = $run.ExitCode
        TranscriptPath  = $TranscriptPath
        DurationSeconds = $run.DurationSeconds
        SkillsInvoked   = @($summary.SkillsInvoked)
        TimedOut        = $run.TimedOut
        Summary         = $summary
    }
}

Export-ModuleMember -Function New-ThrowawayWorkspace, Remove-ThrowawayWorkspace, Test-ThrowawayWorkspace, Invoke-HeadlessClaude,
    Get-HeadlessTranscriptSummary, Get-HeadlessClaudeArguments, Get-HeadlessEnvironment, Get-WorkspaceCopyArguments,
    Get-WorkspaceDestinationRefusal, Get-WorkspaceRemovalRefusal, Invoke-LoggedProcess, ConvertTo-ProcessArgumentString,
    ConvertTo-SkillName, Test-PathIsUnder, Get-ObjectValue
