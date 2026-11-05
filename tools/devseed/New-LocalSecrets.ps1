<#
.SYNOPSIS
    Writes the developer's local secrets file for the API with fresh random values.

.DESCRIPTION
    Creates backend/src/Starter.Api/appsettings.Development.local.json (gitignored; loaded in Development only, right
    after appsettings.Development.json). Every value is generated on this machine at run time, never copied from a
    fixture: the break-glass administrator password, the Development user's password, the session token signing key,
    the Codelab.Security PublicId, request-signing and sealed-ticket key rings, and the observability user-hash key.
    The ticket ring is enabled, so account activation links and the SSO login mode work locally.

    The JSON shape is a contract (the Playwright check and init.ps1 read it):
      Identity:BreakGlass:UserName / Password
      Identity:DevUsers:0:UserName / Password
      Identity:Jwt:SigningKey
      Codelab:Security:PublicIds:ActiveKeyId / Keys[0]:KeyId / Keys[0]:MaterialBase64
      Codelab:Security:RequestSigning:ActiveKeyId / Keys[0]:KeyId / Keys[0]:MaterialBase64
      Codelab:Security:Tickets:Enabled / ActiveKeyId / Keys[0]:KeyId / Keys[0]:MaterialBase64
      Observability:UserIdHash:Key

    An existing file is never overwritten unless -Force is given. Prints the break-glass user name and the file path;
    never prints a password or a key.

.PARAMETER Path
    The file to write. Defaults to the API's local secrets file; tests point it at a temp folder.

.PARAMETER Force
    Overwrite an existing file (every value is regenerated: existing sessions and PublicIds stop working).

.EXAMPLE
    powershell -File tools\devseed\New-LocalSecrets.ps1

.NOTES
    Exit codes: 0 written, 1 refused (the file exists and -Force was not given), 2 usage.
#>
[CmdletBinding()]
param(
    [string] $Path,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitWritten = 0
$ExitRefused = 1
$ExitUsage = 2

$BreakGlassUserName = 'breakglass'
$DevUserName = 'dev.viewer'
$PasswordLength = 24
$KeyBytes = 32
$KeyIdRandomBytes = 3
$KeyIdPrefix = 'd'
$JsonDepth = 10
$LowerChars = 'abcdefghijkmnopqrstuvwxyz'
$UpperChars = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
$DigitChars = '23456789'
$SymbolChars = '!#%*+-=?@_'
$DefaultRelativePath = 'backend\src\Starter.Api\appsettings.Development.local.json'

$script:Random = [System.Security.Cryptography.RandomNumberGenerator]::Create()

function Get-RandomBytes([int] $Count) {
    $bytes = New-Object byte[] $Count
    $script:Random.GetBytes($bytes)
    return , $bytes
}

function Get-RandomIndex([int] $Exclusive) {
    # Rejection sampling over one 32-bit value keeps every index equally likely.
    $limit = [uint32]::MaxValue - ([uint32]::MaxValue % [uint32] $Exclusive)
    do {
        $value = [System.BitConverter]::ToUInt32((Get-RandomBytes 4), 0)
    } while ($value -ge $limit)
    return [int] ($value % [uint32] $Exclusive)
}

function Get-RandomChar([string] $Alphabet) {
    return $Alphabet[(Get-RandomIndex $Alphabet.Length)]
}

function New-Password {
    $all = $LowerChars + $UpperChars + $DigitChars + $SymbolChars
    $chars = New-Object System.Collections.Generic.List[char]
    foreach ($class in @($LowerChars, $UpperChars, $DigitChars, $SymbolChars)) { $chars.Add((Get-RandomChar $class)) }
    while ($chars.Count -lt $PasswordLength) { $chars.Add((Get-RandomChar $all)) }
    for ($i = $chars.Count - 1; $i -gt 0; $i--) {
        $j = Get-RandomIndex ($i + 1)
        $swap = $chars[$i]; $chars[$i] = $chars[$j]; $chars[$j] = $swap
    }
    return -join $chars
}

function New-KeyBase64 { return [Convert]::ToBase64String((Get-RandomBytes $KeyBytes)) }

function New-KeyId { return $KeyIdPrefix + (((Get-RandomBytes $KeyIdRandomBytes) | ForEach-Object { $_.ToString('x2') }) -join '') }

function New-KeyRing {
    $keyId = New-KeyId
    return [ordered]@{
        ActiveKeyId = $keyId
        Keys        = @([ordered]@{ KeyId = $keyId; MaterialBase64 = (New-KeyBase64) })
    }
}

function New-TicketRing {
    $ring = [ordered]@{ Enabled = $true }
    foreach ($entry in (New-KeyRing).GetEnumerator()) { $ring[$entry.Key] = $entry.Value }
    return $ring
}

function New-Account([string] $Name) {
    $account = [ordered]@{}
    $account['UserName'] = $Name
    $account['Password'] = New-Password
    return $account
}

function New-SecretsDocument {
    return [ordered]@{
        Identity      = [ordered]@{
            BreakGlass = (New-Account $BreakGlassUserName)
            DevUsers   = @((New-Account $DevUserName))
            Jwt        = [ordered]@{ SigningKey = (New-KeyBase64) }
        }
        Codelab       = [ordered]@{
            Security = [ordered]@{
                PublicIds      = (New-KeyRing)
                RequestSigning = (New-KeyRing)
                Tickets        = (New-TicketRing)
            }
        }
        Observability = [ordered]@{ UserIdHash = [ordered]@{ Key = (New-KeyBase64) } }
    }
}

if ([string]::IsNullOrWhiteSpace($Path)) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $Path = Join-Path $repoRoot $DefaultRelativePath
}

$target = [System.IO.Path]::GetFullPath($Path)
$folder = Split-Path -Parent $target
if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
    Write-Error "The folder of '$target' does not exist."
    exit $ExitUsage
}

if ((Test-Path -LiteralPath $target) -and -not $Force) {
    Write-Host "Refused: '$target' already exists. Pass -Force to regenerate every value (existing sessions and PublicIds stop working)."
    exit $ExitRefused
}

$json = (New-SecretsDocument) | ConvertTo-Json -Depth $JsonDepth
[System.IO.File]::WriteAllText($target, $json, (New-Object System.Text.UTF8Encoding $false))

Write-Host "Wrote local secrets: $target"
Write-Host "Break-glass user name: $BreakGlassUserName (password in the file, never printed)"
exit $ExitWritten
