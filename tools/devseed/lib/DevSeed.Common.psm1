<#
    Shared helpers of the tools/devseed scripts (Windows PowerShell 5.1): cryptographic randomness, generated
    passwords, and reading and writing the gitignored appsettings.*.local.json files. Nothing here prints a value.
#>
Set-StrictMode -Version Latest

$PasswordLength = 24
$LowerChars = 'abcdefghijkmnopqrstuvwxyz'
$UpperChars = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
$DigitChars = '23456789'
$SymbolChars = '!#%*+-=?@_'
$UInt32Bytes = 4
$JsonDepth = 20

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
        $value = [System.BitConverter]::ToUInt32((Get-RandomBytes $UInt32Bytes), 0)
    } while ($value -ge $limit)
    return [int] ($value % [uint32] $Exclusive)
}

function Get-RandomChar([string] $Alphabet) {
    return $Alphabet[(Get-RandomIndex $Alphabet.Length)]
}

# A fresh password of $PasswordLength characters with at least one lower, upper, digit and symbol, shuffled.
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

function Test-Container($Value) {
    return ($Value -is [System.Collections.IDictionary]) -or ($Value -is [System.Management.Automation.PSCustomObject])
}

function Test-Member($Container, [string] $Name) {
    if ($Container -is [System.Collections.IDictionary]) { return $Container.Contains($Name) }
    return $null -ne $Container.PSObject.Properties[$Name]
}

function Get-MemberValue($Container, [string] $Name) {
    if (-not (Test-Container $Container) -or -not (Test-Member $Container $Name)) { return $null }
    if ($Container -is [System.Collections.IDictionary]) { return , $Container[$Name] }
    return , $Container.PSObject.Properties[$Name].Value
}

# Sets $Container.$Name (an ordered dictionary or a JSON object), adding the member when it is missing.
function Set-MemberValue($Container, [string] $Name, $Value) {
    if ($Container -is [System.Collections.IDictionary]) { $Container[$Name] = $Value; return }
    if (Test-Member $Container $Name) { $Container.$Name = $Value; return }
    $Container | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
}

# The JSON object in $File, or $null when the file is unreadable or holds something else.
function Read-JsonObject([string] $File) {
    try {
        $value = [System.IO.File]::ReadAllText($File, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    } catch {
        return $null
    }
    if (-not ($value -is [System.Management.Automation.PSCustomObject])) { return $null }
    return $value
}

# Writes $Document as UTF-8 JSON without a byte-order mark.
function Write-JsonFile([string] $File, $Document) {
    $json = $Document | ConvertTo-Json -Depth $JsonDepth
    [System.IO.File]::WriteAllText($File, $json, (New-Object System.Text.UTF8Encoding $false))
}

function ConvertTo-Comparable($Value) {
    if ($null -eq $Value) { return '' }
    return ($Value | ConvertTo-Json -Depth $JsonDepth -Compress)
}

Export-ModuleMember -Function Get-RandomBytes, Get-RandomIndex, Get-RandomChar, New-Password, Test-Container, Test-Member,
    Get-MemberValue, Set-MemberValue, Read-JsonObject, Write-JsonFile, ConvertTo-Comparable
