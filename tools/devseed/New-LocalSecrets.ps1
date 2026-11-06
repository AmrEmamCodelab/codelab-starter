<#
.SYNOPSIS
    Writes the developer's local secrets files for the API and the Attachment service with fresh random values.

.DESCRIPTION
    Creates backend/src/Starter.Api/appsettings.Development.local.json and
    services/attachment-service/src/Codelab.Attachments.Api/appsettings.Development.local.json (both gitignored by
    appsettings.*.local.json; loaded in Development only, right after appsettings.Development.json). Every value is
    generated on this machine at run time, never copied from a fixture: the break-glass administrator password, the
    Development user's password, the session token signing key, the Codelab.Security PublicId, request-signing,
    sealed-ticket, upload-ticket and upload-receipt key rings, the service-to-service ES256 key pair and the
    observability user-hash key. Both ticket rings are enabled, so account activation links, the SSO login mode and
    attachment upload tickets work locally.

    The API's own ticket ring (Codelab:Security:Tickets: SSO sign-in cookies, SSO exchange tickets holding refresh
    tokens, account activation) never leaves the API file. Upload tickets have a dedicated ring
    (Codelab:Security:UploadTickets) and only that ring is given to the Attachment service, so a compromised service
    cannot open or forge the API's SSO or activation tickets.

    The JSON shape is a contract (the Playwright check, init.ps1 and run-local read it).
    API file:
      Identity:BreakGlass:UserName / Password
      Identity:DevUsers:0:UserName / Password
      Identity:Jwt:SigningKey
      Codelab:Security:PublicIds:ActiveKeyId / Keys[0]:KeyId / Keys[0]:MaterialBase64
      Codelab:Security:RequestSigning:ActiveKeyId / Keys[0]:KeyId / Keys[0]:MaterialBase64
      Codelab:Security:Tickets:Enabled / ActiveKeyId / Keys[0]:KeyId / Keys[0]:MaterialBase64        (API only)
      Codelab:Security:UploadTickets:Enabled / ActiveKeyId / Keys[0]:KeyId / Keys[0]:MaterialBase64  (upload tickets)
      Codelab:Security:Uploads:ReceiptsEnabled / ActiveKeyId / Keys[0]:KeyId / Keys[0]:MaterialBase64
      Codelab:Security:ServiceAssertions:Client:ServiceName / Audience / KeyId / PrivateKeyPem (PKCS#8)
      Observability:UserIdHash:Key
    Attachment service file:
      Codelab:Security:Tickets            (the API's UploadTickets ring, the service's only ticket ring: the API seals
                                           upload tickets, the service opens them; never the API's Tickets ring)
      Codelab:Security:Uploads            (the API's receipt ring: the service signs, the API verifies)
      Codelab:Security:ServiceAssertions:Server:Audience / TrustedServices[0]:ServiceName / KeyId / PublicKeyPem
      Attachments:Storage:RootPath / Attachments:Cors:AllowedOrigins

    Existing values are never changed without -Force. When a file exists, only the keys it lacks are added (an API
    file from before the Attachment service gets the receipt ring and the client key; its passwords, sessions and
    PublicIds keep working). The service file always takes the shared rings from the API file, and a mismatch between
    two existing files is reported, not repaired. A service file that holds key material of the API's own ticket ring
    (a file from before the dedicated upload-ticket ring) is reported too; -Force replaces it. Prints file paths, the added key paths and the break-glass user name;
    never prints a password or a key.

.PARAMETER Path
    The API file. Defaults to the API's local secrets file; tests point it at a temp folder.

.PARAMETER ServicePath
    The Attachment service file. Defaults to the service's local secrets file. When its folder does not exist (the
    service is not in this repository) the service file is skipped with a warning.

.PARAMETER StorageRoot
    The service's file store (Attachments:Storage:RootPath) for a new service file; created when missing.
    Defaults to %LOCALAPPDATA%\codelab-starter\attachments.

.PARAMETER Force
    Regenerate both files (every value is new: existing sessions, PublicIds, tickets and receipts stop working).

.EXAMPLE
    powershell -File tools\devseed\New-LocalSecrets.ps1

.NOTES
    Exit codes: 0 written, patched or already complete; 1 refused (an existing file is not a JSON object, or the
    API's client key is not a PKCS#8 P-256 key); 2 usage.
#>
[CmdletBinding()]
param(
    [string] $Path,
    [string] $ServicePath,
    [string] $StorageRoot,
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
$JsonDepth = 20
$LowerChars = 'abcdefghijkmnopqrstuvwxyz'
$UpperChars = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
$DigitChars = '23456789'
$SymbolChars = '!#%*+-=?@_'
$DefaultRelativePath = 'backend\src\Starter.Api\appsettings.Development.local.json'
$DefaultServiceRelativePath = 'services\attachment-service\src\Codelab.Attachments.Api\appsettings.Development.local.json'
$DefaultStorageRelativePath = 'codelab-starter\attachments'
$TicketsSwitch = 'Enabled'
$ReceiptsSwitch = 'ReceiptsEnabled'
$ApiTicketRing = 'Tickets'
$UploadTicketRing = 'UploadTickets'
$ServiceTicketRing = 'Tickets'
$ReceiptRing = 'Uploads'
# Rings both files must hold identically: the API file's section and the service file's section.
$SharedRings = @(
    @{ Api = $UploadTicketRing; Service = $ServiceTicketRing },
    @{ Api = $ReceiptRing; Service = $ReceiptRing }
)

# Service-to-service contract (phase 6 contract 2): the API calls the Attachment service.
$ApiServiceName = 'starter-api'
$AttachmentsAudience = 'starter-attachments'
$WebOrigin = 'http://localhost:4280'

# ES256 (P-256) key encoding. EccPublicBlob = 8-byte BCRYPT_ECCKEY_BLOB header, then X and Y (32 bytes each); the
# SubjectPublicKeyInfo of a P-256 key is a fixed DER prefix followed by the uncompressed point (0x04 || X || Y).
$EccBlobHeaderBytes = 8
$P256CoordinateBytes = 32
$SpkiP256Prefix = '3059301306072a8648ce3d020106082a8648ce3d03010703420004'
$HexDigitsPerByte = 2
$HexBase = 16
$PemLineLength = 64
$PemPrivateLabel = 'PRIVATE KEY'
$PemPublicLabel = 'PUBLIC KEY'

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

# A key ring with its on-switch first (Tickets:Enabled, Uploads:ReceiptsEnabled), the same shape as the options.
function New-SwitchedRing([string] $SwitchName) {
    $ring = [ordered]@{}
    $ring[$SwitchName] = $true
    foreach ($entry in (New-KeyRing).GetEnumerator()) { $ring[$entry.Key] = $entry.Value }
    return $ring
}

function New-Account([string] $Name) {
    $account = [ordered]@{}
    $account['UserName'] = $Name
    $account['Password'] = New-Password
    return $account
}

function ConvertTo-Pem([string] $Label, [byte[]] $Der) {
    $base64 = [Convert]::ToBase64String($Der)
    $lines = for ($offset = 0; $offset -lt $base64.Length; $offset += $PemLineLength) {
        $base64.Substring($offset, [Math]::Min($PemLineLength, $base64.Length - $offset))
    }
    return "-----BEGIN $Label-----`n" + ($lines -join "`n") + "`n-----END $Label-----"
}

function ConvertFrom-Pem([string] $Pem, [string] $Label) {
    $match = [regex]::Match($Pem, "-----BEGIN $Label-----(?<body>[A-Za-z0-9+/=\s]+)-----END $Label-----")
    if (-not $match.Success) { return $null }
    return , [Convert]::FromBase64String(($match.Groups['body'].Value -replace '\s', ''))
}

function ConvertFrom-Hex([string] $Hex) {
    $bytes = New-Object byte[] ($Hex.Length / $HexDigitsPerByte)
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($Hex.Substring($i * $HexDigitsPerByte, $HexDigitsPerByte), $HexBase) }
    return , $bytes
}

function Get-PublicKeyPem([System.Security.Cryptography.CngKey] $Key) {
    $blob = $Key.Export([System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    $point = New-Object byte[] ($HexDigitsPerByte * $P256CoordinateBytes)
    [Array]::Copy($blob, $EccBlobHeaderBytes, $point, 0, $point.Length)
    $prefix = ConvertFrom-Hex $SpkiP256Prefix
    return ConvertTo-Pem $PemPublicLabel ([byte[]] ($prefix + $point))
}

function New-ServiceKeyPair {
    # A fresh ephemeral P-256 key; PowerShell 5.1 (.NET Framework) has no PEM export, so CNG exports PKCS#8 DER.
    $parameters = New-Object System.Security.Cryptography.CngKeyCreationParameters
    $parameters.ExportPolicy = [System.Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
    $key = [System.Security.Cryptography.CngKey]::Create([System.Security.Cryptography.CngAlgorithm]::ECDsaP256, [NullString]::Value, $parameters)
    try {
        $der = $key.Export([System.Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
        return @{ PrivateKeyPem = (ConvertTo-Pem $PemPrivateLabel $der); PublicKeyPem = (Get-PublicKeyPem $key) }
    } finally {
        $key.Dispose()
    }
}

# The public key PEM of an existing PKCS#8 P-256 private key, or $null when it is not one.
function Get-PublicKeyPemOf($PrivateKeyPem) {
    if (-not ($PrivateKeyPem -is [string])) { return $null }
    $der = ConvertFrom-Pem $PrivateKeyPem $PemPrivateLabel
    if ($null -eq $der) { return $null }
    try {
        $key = [System.Security.Cryptography.CngKey]::Import($der, [System.Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
    } catch {
        return $null
    }
    try {
        if ($key.Algorithm.Algorithm -ne [System.Security.Cryptography.CngAlgorithm]::ECDsaP256.Algorithm) { return $null }
        return Get-PublicKeyPem $key
    } finally {
        $key.Dispose()
    }
}

function New-ApiDocument {
    $clientKey = New-ServiceKeyPair
    return [ordered]@{
        Identity      = [ordered]@{
            BreakGlass = (New-Account $BreakGlassUserName)
            DevUsers   = @((New-Account $DevUserName))
            Jwt        = [ordered]@{ SigningKey = (New-KeyBase64) }
        }
        Codelab       = [ordered]@{
            Security = [ordered]@{
                PublicIds         = (New-KeyRing)
                RequestSigning    = (New-KeyRing)
                Tickets           = (New-SwitchedRing $TicketsSwitch)
                UploadTickets     = (New-SwitchedRing $TicketsSwitch)
                Uploads           = (New-SwitchedRing $ReceiptsSwitch)
                ServiceAssertions = [ordered]@{
                    Client = [ordered]@{
                        ServiceName   = $ApiServiceName
                        Audience      = $AttachmentsAudience
                        KeyId         = (New-KeyId)
                        PrivateKeyPem = $clientKey.PrivateKeyPem
                    }
                }
            }
        }
        Observability = [ordered]@{ UserIdHash = [ordered]@{ Key = (New-KeyBase64) } }
    }
}

# The service document derived from the final API document: the API's upload-ticket ring (as the service's only
# ticket ring; never the API's own Tickets ring), the receipt ring, and the API's client key id with the public half
# of its private key.
function New-ServiceDocument($Api, [string] $PublicKeyPem, [string] $Storage) {
    $security = $Api.Codelab.Security
    return [ordered]@{
        Codelab     = [ordered]@{
            Security = [ordered]@{
                Tickets           = (Get-MemberValue $security $UploadTicketRing)
                Uploads           = (Get-MemberValue $security $ReceiptRing)
                ServiceAssertions = [ordered]@{
                    Server = [ordered]@{
                        Audience        = $AttachmentsAudience
                        TrustedServices = @([ordered]@{
                                ServiceName  = $ApiServiceName
                                KeyId        = $security.ServiceAssertions.Client.KeyId
                                PublicKeyPem = $PublicKeyPem
                            })
                    }
                }
            }
        }
        Attachments = [ordered]@{
            Storage = [ordered]@{ RootPath = $Storage }
            Cors    = [ordered]@{ AllowedOrigins = @($WebOrigin) }
        }
    }
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

# Adds to $Target every key of $Source it lacks (recursing into objects present on both sides); never changes a value
# $Target already has. Returns the added key paths.
function Add-MissingKeys($Target, $Source, [string] $Prefix) {
    $added = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $Source.GetEnumerator()) {
        $keyPath = if ($Prefix) { "${Prefix}:$($entry.Key)" } else { $entry.Key }
        if (-not (Test-Member $Target $entry.Key)) {
            if ($Target -is [System.Collections.IDictionary]) { $Target[$entry.Key] = $entry.Value }
            else { $Target | Add-Member -MemberType NoteProperty -Name $entry.Key -Value $entry.Value }
            $added.Add($keyPath)
            continue
        }
        $existing = Get-MemberValue $Target $entry.Key
        if ((Test-Container $existing) -and ($entry.Value -is [System.Collections.IDictionary])) {
            foreach ($addedPath in (Add-MissingKeys $existing $entry.Value $keyPath)) { $added.Add($addedPath) }
        }
    }
    return , $added
}

function Read-JsonObject([string] $File) {
    try {
        $value = [System.IO.File]::ReadAllText($File, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    } catch {
        return $null
    }
    if (-not ($value -is [System.Management.Automation.PSCustomObject])) { return $null }
    return $value
}

function Write-JsonFile([string] $File, $Document) {
    $json = $Document | ConvertTo-Json -Depth $JsonDepth
    [System.IO.File]::WriteAllText($File, $json, (New-Object System.Text.UTF8Encoding $false))
}

function ConvertTo-Comparable($Value) {
    if ($null -eq $Value) { return '' }
    return ($Value | ConvertTo-Json -Depth $JsonDepth -Compress)
}

function Resolve-Target([string] $Given, [string] $RelativeDefault) {
    if ([string]::IsNullOrWhiteSpace($Given)) {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $Given = Join-Path $repoRoot $RelativeDefault
    }
    return [System.IO.Path]::GetFullPath($Given)
}

function Exit-Refused([string] $Message) {
    Write-Host "Refused: $Message"
    exit $ExitRefused
}

# The existing document to patch, or $null for a new file (always $null with -Force).
function Get-ExistingDocument([string] $File) {
    if ($Force -or -not (Test-Path -LiteralPath $File)) { return $null }
    $document = Read-JsonObject $File
    if ($null -eq $document) { Exit-Refused "'$File' exists but is not a JSON object; fix it or pass -Force to regenerate both files." }
    return $document
}

# Writes $Fresh when there is no existing document, else adds the keys it lacks; returns the final document.
function Save-Secrets([string] $File, $Existing, $Fresh, [string] $Label) {
    if ($null -eq $Existing) {
        Write-JsonFile $File $Fresh
        Write-Host "Wrote the $Label local secrets: $File"
        return $Fresh
    }
    $added = Add-MissingKeys $Existing $Fresh ''
    if ($added.Count -eq 0) {
        Write-Host "The $Label local secrets are complete: $File"
        return $Existing
    }
    Write-JsonFile $File $Existing
    Write-Host "Added to the $Label local secrets ($File): $($added -join ', ')"
    return $Existing
}

# Warns when two existing files disagree on a shared value (never repairs: that would change an existing value).
function Test-SharedValues($Api, $Service, [string] $PublicKeyPem) {
    $apiSecurity = $Api.Codelab.Security
    $serviceSecurity = $Service.Codelab.Security
    $problems = New-Object System.Collections.Generic.List[string]
    foreach ($ring in $SharedRings) {
        if ((ConvertTo-Comparable (Get-MemberValue $apiSecurity $ring.Api)) -ne (ConvertTo-Comparable (Get-MemberValue $serviceSecurity $ring.Service))) {
            $problems.Add("Codelab:Security:$($ring.Api) (API file) differs from Codelab:Security:$($ring.Service) (service file)")
        }
    }
    $clientKeyId = Get-MemberValue $apiSecurity.ServiceAssertions.Client 'KeyId'
    $server = Get-MemberValue (Get-MemberValue $serviceSecurity 'ServiceAssertions') 'Server'
    $trustedServices = Get-MemberValue $server 'TrustedServices'
    $trusted = $trustedServices | Where-Object {
        (Get-MemberValue $_ 'ServiceName') -eq $ApiServiceName -and (Get-MemberValue $_ 'KeyId') -eq $clientKeyId -and (Get-MemberValue $_ 'PublicKeyPem') -eq $PublicKeyPem
    }
    if (@($trusted).Count -eq 0) { $problems.Add("the service does not trust the API's client key ($ApiServiceName)") }
    foreach ($problem in $problems) {
        Write-Warning "$problem; the API cannot use the Attachment service. Pass -Force to regenerate both files."
    }
    Test-ApiRingIsolation $apiSecurity $serviceSecurity
}

# The key materials of a ring (Keys[*]:MaterialBase64), skipping blanks.
function Get-RingMaterials($Ring) {
    $keys = Get-MemberValue $Ring 'Keys'
    if ($null -eq $keys) { return , @() }
    return , @($keys | ForEach-Object { Get-MemberValue $_ 'MaterialBase64' } | Where-Object { $_ -is [string] -and $_ -ne '' })
}

function Test-SharesMaterial($First, $Second) {
    $firstMaterials = Get-RingMaterials $First
    foreach ($material in (Get-RingMaterials $Second)) {
        if ($firstMaterials -contains $material) { return $true }
    }
    return $false
}

# Warns when the API's own ticket ring (SSO and activation tickets) is reachable from the service: the service file's
# ticket ring, or the API's upload-ticket ring the service is given, holds a key of it.
function Test-ApiRingIsolation($ApiSecurity, $ServiceSecurity) {
    $apiRing = Get-MemberValue $ApiSecurity $ApiTicketRing
    $exposed = (Test-SharesMaterial $apiRing (Get-MemberValue $ServiceSecurity $ServiceTicketRing)) -or
        (Test-SharesMaterial $apiRing (Get-MemberValue $ApiSecurity $UploadTicketRing))
    if ($exposed) {
        Write-Warning ("The Attachment service holds a key of the API's own ticket ring (Codelab:Security:$ApiTicketRing), " +
            "so it could open or forge SSO and activation tickets. Pass -Force to regenerate both files.")
    }
}

$apiTarget = Resolve-Target $Path $DefaultRelativePath
if (-not (Test-Path -LiteralPath (Split-Path -Parent $apiTarget) -PathType Container)) {
    Write-Error "The folder of '$apiTarget' does not exist."
    exit $ExitUsage
}

$api = Save-Secrets $apiTarget (Get-ExistingDocument $apiTarget) (New-ApiDocument) 'API'
Write-Host "Break-glass user name: $BreakGlassUserName (password in the file, never printed)"

$serviceTarget = Resolve-Target $ServicePath $DefaultServiceRelativePath
if (-not (Test-Path -LiteralPath (Split-Path -Parent $serviceTarget) -PathType Container)) {
    Write-Warning "The Attachment service folder of '$serviceTarget' does not exist; its local secrets were not written."
    exit $ExitWritten
}

$publicKeyPem = Get-PublicKeyPemOf $api.Codelab.Security.ServiceAssertions.Client.PrivateKeyPem
if ($null -eq $publicKeyPem) {
    Exit-Refused "Codelab:Security:ServiceAssertions:Client:PrivateKeyPem in '$apiTarget' is not a PKCS#8 P-256 key; fix it or pass -Force to regenerate both files."
}

if ([string]::IsNullOrWhiteSpace($StorageRoot)) { $StorageRoot = Join-Path $env:LOCALAPPDATA $DefaultStorageRelativePath }
$storage = [System.IO.Path]::GetFullPath($StorageRoot)
$service = Save-Secrets $serviceTarget (Get-ExistingDocument $serviceTarget) (New-ServiceDocument $api $publicKeyPem $storage) 'Attachment service'
Test-SharedValues $api $service $publicKeyPem

# Only the store this script chose is created; a RootPath the developer set is theirs to manage.
$rootPath = Get-MemberValue (Get-MemberValue $service.Attachments 'Storage') 'RootPath'
if (($rootPath -eq $storage) -and -not (Test-Path -LiteralPath $storage)) {
    New-Item -ItemType Directory -Path $storage -Force | Out-Null
    Write-Host "Created the attachment store: $storage"
}
exit $ExitWritten
