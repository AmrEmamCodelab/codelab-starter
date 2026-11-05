# Shared by the fork-model PowerShell entry points (init.ps1, tools\remove-module.ps1, tools\upstream-merge.ps1,
# tools\fork\Test-ForkModel.ps1). They only bind parameters and call the Node engine tools\fork\fork.mjs, which holds
# all logic and its unit tests. Windows PowerShell 5.1 compatible (no &&, ?:, ??).

Set-StrictMode -Version Latest

$ForkNodeDirEnvVar     = 'CODELAB_NODE_DIR'
$ForkPrivateNodeDir    = 'E:\codelab-tools\node24'
$ForkNodeExecutable    = 'node.exe'
$ForkCliRelativePath   = 'tools\fork\fork.mjs'
$ForkBuildCommandEnv   = 'CODELAB_FORK_BUILD_COMMAND'
$ForkExitUsage         = 2

# Puts the pinned Node first on PATH for this process (ADR-005), the same way tools\build.ps1 does:
# $env:CODELAB_NODE_DIR, else the private install, else node from PATH. Returns the node executable to run.
function Use-ForkNode {
    $override = [Environment]::GetEnvironmentVariable($ForkNodeDirEnvVar)
    $nodeDir = $null
    if ($override) {
        if (-not (Test-Path -LiteralPath (Join-Path $override $ForkNodeExecutable))) {
            throw "$ForkNodeDirEnvVar points to '$override', which has no $ForkNodeExecutable."
        }
        $nodeDir = $override
    }
    elseif (Test-Path -LiteralPath (Join-Path $ForkPrivateNodeDir $ForkNodeExecutable)) { $nodeDir = $ForkPrivateNodeDir }
    if ($nodeDir) {
        $env:PATH = "$nodeDir$([IO.Path]::PathSeparator)$env:PATH"
        return (Join-Path $nodeDir $ForkNodeExecutable)
    }
    $onPath = Get-Command $ForkNodeExecutable -ErrorAction SilentlyContinue
    if (-not $onPath) { throw "node not found: set $ForkNodeDirEnvVar, install to $ForkPrivateNodeDir, or put node on PATH." }
    return $onPath.Source
}

# PowerShell 5.1 passes '-Name A,B' to a -File script as the single string 'A,B'; this splits it back into a list.
function Split-ForkList([string[]] $Values) {
    return @($Values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# Runs the fork engine with the given arguments; its exit code is left in $LASTEXITCODE. Call it as a statement (not
# assigned or piped) so node writes straight to the console.
function Invoke-ForkCli([string] $RepoRoot, [string[]] $Arguments) {
    try { $node = Use-ForkNode }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        $global:LASTEXITCODE = $ForkExitUsage
        return
    }
    & $node (Join-Path $RepoRoot $ForkCliRelativePath) @Arguments
}
