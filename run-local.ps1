<#
.SYNOPSIS
    Runs the starter locally: the API (Development, http://localhost:5180), the Attachment service (Development,
    http://localhost:5185), the web app (http://localhost:4280) and, when it is installed or reachable, Seq
    (http://localhost:5341 or $env:CODELAB_SEQ_URL).

.DESCRIPTION
    Writes the missing local secrets (tools\devseed\New-LocalSecrets.ps1 adds missing keys only and never changes a
    value), starts LocalDB (MSSQLLocalDB), builds and starts the API and then the Attachment service with the private
    .NET SDK, starts the web app with the private Node, health-checks each in that order, then prints the URLs and the
    break-glass user. The processes keep running after this script returns; their
    PIDs and logs are under %TEMP%\codelab-starter. -Stop stops only those recorded PIDs.

    It never installs or deletes anything: run npm install at the repo root before the first web start.
    The logic lives in tools\run-local (Node, unit-tested); see tools\run-local\README.md.

    Exit codes: 0 ok, 1 refused or failed (already running, a port in use, a failed health check), 2 usage.

.PARAMETER Stop
    Stops the processes a previous run started (only the recorded PIDs, and only while they are still those processes).

.PARAMETER NoWeb
    Starts the API only.

.PARAMETER NoSeq
    Does not probe or start Seq.

.PARAMETER NoAttachments
    Does not start the Attachment service (attachment uploads and downloads then fail until it runs).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -Stop
#>
[CmdletBinding()]
param(
    [switch] $Stop,
    [switch] $NoWeb,
    [switch] $NoSeq,
    [switch] $NoAttachments
)

$ErrorActionPreference = 'Stop'
$privateNodeDir = if ($env:CODELAB_NODE_DIR) { $env:CODELAB_NODE_DIR } else { 'E:\codelab-tools\node24' }
$node = Join-Path $privateNodeDir 'node.exe'
if (-not (Test-Path $node)) {
    Write-Error "The private Node was not found at $node (set CODELAB_NODE_DIR)."
    exit 2
}
$env:PATH = "$privateNodeDir;$env:PATH"

$arguments = @((Join-Path $PSScriptRoot 'tools\run-local\run-local.mjs'), '--root', $PSScriptRoot)
if ($Stop) { $arguments += '--stop' }
if ($NoWeb) { $arguments += '--no-web' }
if ($NoSeq) { $arguments += '--no-seq' }
if ($NoAttachments) { $arguments += '--no-attachments' }

& $node @arguments
exit $LASTEXITCODE
