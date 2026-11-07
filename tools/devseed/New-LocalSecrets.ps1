<#
.SYNOPSIS
    Writes the developer's local secrets files for the API and every service with fresh random values.

.DESCRIPTION
    Creates the gitignored appsettings.Development.local.json of the API, the Attachment service, the integration
    gateway and the Ops Console API (gitignored by appsettings.*.local.json; loaded in Development only, right after
    appsettings.Development.json). Every value is generated on this machine at run time, never copied from a fixture:
    the break-glass administrator password, the Development user's password, the session token signing key, the
    Codelab.Security PublicId, request-signing, sealed-ticket, upload-ticket and upload-receipt key rings, one
    service-to-service ES256 key pair per calling service (the API; the Ops Console API) and the observability
    user-hash key. Both ticket rings are enabled, so account activation links, the SSO login mode and attachment upload
    tickets work locally.

    The API's own ticket ring (Codelab:Security:Tickets: SSO sign-in cookies, SSO exchange tickets holding refresh
    tokens, account activation) never leaves the API file. Upload tickets have a dedicated ring
    (Codelab:Security:UploadTickets) and only that ring is given to the Attachment service, so a compromised service
    cannot open or forge the API's SSO or activation tickets.

    Service-to-service trust (ES256 service assertions): the API signs with its client key (one key, two audiences:
    the Attachment service and the gateway); both trust its public half. The Ops Console API signs with its own key;
    the API trusts its public half and names it in Codelab:Ops:TrustedCallerKeyId. The Attachment service and the
    gateway call nobody, so they hold no private key.

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
      Codelab:Security:ServiceAssertions:Server:Audience / TrustedServices[0] (Ops Console API)  (unless ops is skipped)
      Codelab:Ops:TrustedCallerKeyId                                                              (unless ops is skipped)
      Observability:UserIdHash:Key
    Attachment service file:
      Codelab:Security:Tickets            (the API's UploadTickets ring, the service's only ticket ring: the API seals
                                           upload tickets, the service opens them; never the API's Tickets ring)
      Codelab:Security:Uploads            (the API's receipt ring: the service signs, the API verifies)
      Codelab:Security:ServiceAssertions:Server:Audience / TrustedServices[0]:ServiceName / KeyId / PublicKeyPem
      Attachments:Storage:RootPath / Attachments:Cors:AllowedOrigins
    Gateway file:
      Codelab:Security:ServiceAssertions:Server:Audience / TrustedServices[0] (the API's client key, public half)
    Ops Console API file:
      Codelab:Security:ServiceAssertions:Client:ServiceName / Audience / KeyId / PrivateKeyPem (PKCS#8)

    Existing values are never changed without -Force. When a file exists, only the keys it lacks are added. A
    disagreement between two existing files (a shared ring, a trusted public key) is reported, not repaired; -Force
    regenerates every file. Prints file paths, the added key paths and the break-glass user name; never prints a
    password or a key.

.PARAMETER Path
    The API file. Defaults to the API's local secrets file; tests point it at a temp folder.

.PARAMETER ServicePath
    The Attachment service file. When its folder does not exist (the service is not in this repository) it is skipped
    with a warning.

.PARAMETER GatewayPath
    The integration gateway file. When its folder does not exist it is skipped with a warning.

.PARAMETER OpsPath
    The Ops Console API file. When its folder does not exist it is skipped with a warning.

.PARAMETER StorageRoot
    The Attachment service's file store (Attachments:Storage:RootPath) for a new service file; created when missing.
    Defaults to %LOCALAPPDATA%\codelab-starter\attachments.

.PARAMETER SkipService
    Services removed from this project (init.ps1 passes its unticked optional modules): attachments, sms, ops, gateway.
    A skipped service gets no file, and the API file gets no trust for it. sms changes nothing here (the SMS provider
    credential is never generated); gateway skips the gateway file (a project without SSO and SMS). A comma-separated
    list is accepted (powershell -File passes one string).

.PARAMETER Force
    Regenerate every file (every value is new: existing sessions, PublicIds, tickets and receipts stop working).

.EXAMPLE
    powershell -File tools\devseed\New-LocalSecrets.ps1
    powershell -File tools\devseed\New-LocalSecrets.ps1 -Force -SkipService attachments,ops

.NOTES
    Exit codes: 0 written, patched or already complete; 1 refused (an existing file is not a JSON object, or a
    client key is not a PKCS#8 P-256 key); 2 usage.
#>
[CmdletBinding()]
param(
    [string] $Path,
    [string] $ServicePath,
    [string] $GatewayPath,
    [string] $OpsPath,
    [string] $StorageRoot,
    [string[]] $SkipService = @(),
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\DevSeed.Common.psm1') -Force

$ExitWritten = 0
$ExitRefused = 1
$ExitUsage = 2

$BreakGlassUserName = 'breakglass'
$DevUserName = 'dev.viewer'
$KeyBytes = 32
$KeyIdRandomBytes = 3
$KeyIdPrefix = 'd'
$DefaultRelativePath = 'backend\src\Starter.Api\appsettings.Development.local.json'
$DefaultServiceRelativePath = 'services\attachment-service\src\Codelab.Attachments.Api\appsettings.Development.local.json'
$DefaultGatewayRelativePath = 'services\integration-gateway\src\Codelab.Gateway.Api\appsettings.Development.local.json'
$DefaultOpsRelativePath = 'services\ops-console\src\Codelab.OpsConsole.Api\appsettings.Development.local.json'
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

# Service-to-service contract: service names and assertion audiences (the services' appsettings.json and kits).
$ApiServiceName = 'starter-api'
$ApiAudience = 'starter-api'
$AttachmentsAudience = 'starter-attachments'
$GatewayAudience = 'integration-gateway'
$OpsServiceName = 'starter-ops-console'
$WebOrigin = 'http://localhost:4280'

# The -SkipService ids (init.ps1's optional module ids, plus the gateway).
$ServiceIds = [ordered]@{ Attachments = 'attachments'; Sms = 'sms'; Ops = 'ops'; Gateway = 'gateway' }
$SkipListSeparator = ','

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

# A client key section (Codelab:Security:ServiceAssertions:Client) with a fresh key pair.
function New-ClientSection([string] $ServiceName, [string] $Audience) {
    $pair = New-ServiceKeyPair
    return [ordered]@{ ServiceName = $ServiceName; Audience = $Audience; KeyId = (New-KeyId); PrivateKeyPem = $pair.PrivateKeyPem }
}

# A server section (Codelab:Security:ServiceAssertions:Server) trusting one caller (@{ KeyId; PublicKeyPem }).
function New-ServerSection([string] $Audience, [string] $CallerName, $Caller) {
    return [ordered]@{
        Audience        = $Audience
        TrustedServices = @([ordered]@{ ServiceName = $CallerName; KeyId = $Caller.KeyId; PublicKeyPem = $Caller.PublicKeyPem })
    }
}

# The API document; $OpsCaller (@{ KeyId; PublicKeyPem } or $null) is the Ops Console API it trusts.
function New-ApiDocument($OpsCaller) {
    $assertions = [ordered]@{ Client = (New-ClientSection $ApiServiceName $AttachmentsAudience) }
    $codelab = [ordered]@{
        Security = [ordered]@{
            PublicIds         = (New-KeyRing)
            RequestSigning    = (New-KeyRing)
            Tickets           = (New-SwitchedRing $TicketsSwitch)
            UploadTickets     = (New-SwitchedRing $TicketsSwitch)
            Uploads           = (New-SwitchedRing $ReceiptsSwitch)
            ServiceAssertions = $assertions
        }
    }
    if ($null -ne $OpsCaller) {
        $assertions['Server'] = New-ServerSection $ApiAudience $OpsServiceName $OpsCaller
        $codelab['Ops'] = [ordered]@{ TrustedCallerKeyId = $OpsCaller.KeyId }
    }
    return [ordered]@{
        Identity      = [ordered]@{
            BreakGlass = (New-Account $BreakGlassUserName)
            DevUsers   = @((New-Account $DevUserName))
            Jwt        = [ordered]@{ SigningKey = (New-KeyBase64) }
        }
        Codelab       = $codelab
        Observability = [ordered]@{ UserIdHash = [ordered]@{ Key = (New-KeyBase64) } }
    }
}

function New-OpsDocument {
    return [ordered]@{
        Codelab = [ordered]@{ Security = [ordered]@{ ServiceAssertions = [ordered]@{ Client = (New-ClientSection $OpsServiceName $ApiAudience) } } }
    }
}

# The gateway trusts the API's client key ($ApiCaller: @{ KeyId; PublicKeyPem }).
function New-GatewayDocument($ApiCaller) {
    return [ordered]@{
        Codelab = [ordered]@{ Security = [ordered]@{ ServiceAssertions = [ordered]@{ Server = (New-ServerSection $GatewayAudience $ApiServiceName $ApiCaller) } } }
    }
}

# The service document derived from the final API document: the API's upload-ticket ring (as the service's only
# ticket ring; never the API's own Tickets ring), the receipt ring, and the API's client key id with the public half
# of its private key.
function New-ServiceDocument($Api, $ApiCaller, [string] $Storage) {
    $security = $Api.Codelab.Security
    return [ordered]@{
        Codelab     = [ordered]@{
            Security = [ordered]@{
                Tickets           = (Get-MemberValue $security $UploadTicketRing)
                Uploads           = (Get-MemberValue $security $ReceiptRing)
                ServiceAssertions = [ordered]@{ Server = (New-ServerSection $AttachmentsAudience $ApiServiceName $ApiCaller) }
            }
        }
        Attachments = [ordered]@{
            Storage = [ordered]@{ RootPath = $Storage }
            Cors    = [ordered]@{ AllowedOrigins = @($WebOrigin) }
        }
    }
}

# Adds to $Target every key of $Source it lacks (recursing into objects present on both sides); never changes a value
# $Target already has. Returns the added key paths.
function Add-MissingKeys($Target, $Source, [string] $Prefix) {
    $added = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $Source.GetEnumerator()) {
        $keyPath = if ($Prefix) { "${Prefix}:$($entry.Key)" } else { $entry.Key }
        if (-not (Test-Member $Target $entry.Key)) {
            Set-MemberValue $Target $entry.Key $entry.Value
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

function Exit-Usage([string] $Message) {
    [Console]::Error.WriteLine("Usage: $Message")
    exit $ExitUsage
}

# The lower-case skip ids; an unknown id is a usage error.
function Get-SkippedServices {
    $known = @($ServiceIds.Values)
    $ids = @($SkipService | ForEach-Object { $_ -split $SkipListSeparator } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
    foreach ($id in $ids) {
        if ($known -notcontains $id) { Exit-Usage "-SkipService '$id' is not one of: $($known -join ', ')." }
    }
    return , $ids
}

# The existing document to patch, or $null for a new file (always $null with -Force).
function Get-ExistingDocument([string] $File) {
    if ($Force -or -not (Test-Path -LiteralPath $File)) { return $null }
    $document = Read-JsonObject $File
    if ($null -eq $document) { Exit-Refused "'$File' exists but is not a JSON object; fix it or pass -Force to regenerate every file." }
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

# The service file's target when its folder exists, else $null (with a warning: that service is not in this repository).
function Resolve-ServiceTarget([string] $Given, [string] $RelativeDefault, [string] $Label) {
    $target = Resolve-Target $Given $RelativeDefault
    if (Test-Path -LiteralPath (Split-Path -Parent $target) -PathType Container) { return $target }
    Write-Warning "The $Label folder of '$target' does not exist; its local secrets were not written."
    return $null
}

function Get-AssertionSection($Document, [string] $Name) {
    $assertions = Get-MemberValue (Get-MemberValue (Get-MemberValue $Document 'Codelab') 'Security') 'ServiceAssertions'
    return Get-MemberValue $assertions $Name
}

# The caller identity (@{ KeyId; PublicKeyPem }) of a document's client key; refused when it is not a P-256 key.
function Get-Caller($Document, [string] $File) {
    $client = Get-AssertionSection $Document 'Client'
    $publicKeyPem = Get-PublicKeyPemOf (Get-MemberValue $client 'PrivateKeyPem')
    if ($null -eq $publicKeyPem) {
        Exit-Refused "Codelab:Security:ServiceAssertions:Client:PrivateKeyPem in '$File' is not a PKCS#8 P-256 key; fix it or pass -Force to regenerate every file."
    }
    return @{ KeyId = (Get-MemberValue $client 'KeyId'); PublicKeyPem = $publicKeyPem }
}

# True when the server section of $Document trusts $CallerName with exactly $Caller's key.
function Test-Trusts($Document, [string] $CallerName, $Caller) {
    $trustedServices = Get-MemberValue (Get-AssertionSection $Document 'Server') 'TrustedServices'
    $trusted = $trustedServices | Where-Object {
        (Get-MemberValue $_ 'ServiceName') -eq $CallerName -and (Get-MemberValue $_ 'KeyId') -eq $Caller.KeyId -and (Get-MemberValue $_ 'PublicKeyPem') -eq $Caller.PublicKeyPem
    }
    return @($trusted).Count -gt 0
}

function Write-TrustWarning([string] $Problem, [string] $Consequence) {
    Write-Warning "$Problem; $Consequence. Pass -Force to regenerate every file."
}

# Warns when two existing files disagree on a shared value (never repairs: that would change an existing value).
function Test-SharedValues($Api, $Service, $ApiCaller) {
    $apiSecurity = $Api.Codelab.Security
    $serviceSecurity = $Service.Codelab.Security
    $consequence = 'the API cannot use the Attachment service'
    foreach ($ring in $SharedRings) {
        if ((ConvertTo-Comparable (Get-MemberValue $apiSecurity $ring.Api)) -ne (ConvertTo-Comparable (Get-MemberValue $serviceSecurity $ring.Service))) {
            Write-TrustWarning "Codelab:Security:$($ring.Api) (API file) differs from Codelab:Security:$($ring.Service) (service file)" $consequence
        }
    }
    if (-not (Test-Trusts $Service $ApiServiceName $ApiCaller)) { Write-TrustWarning "the service does not trust the API's client key ($ApiServiceName)" $consequence }
    Test-ApiRingIsolation $apiSecurity $serviceSecurity
}

# Warns when the API does not trust the Ops Console API's key, or names another key as its Ops caller.
function Test-OpsTrust($Api, $OpsCaller) {
    $consequence = 'the Ops Console cannot call the API'
    if (-not (Test-Trusts $Api $OpsServiceName $OpsCaller)) { Write-TrustWarning "the API does not trust the Ops Console API's client key ($OpsServiceName)" $consequence }
    $trustedCaller = Get-MemberValue (Get-MemberValue $Api.Codelab 'Ops') 'TrustedCallerKeyId'
    if ($trustedCaller -ne $OpsCaller.KeyId) { Write-TrustWarning "Codelab:Ops:TrustedCallerKeyId (API file) is not the Ops Console API's key id" $consequence }
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
            "so it could open or forge SSO and activation tickets. Pass -Force to regenerate every file.")
    }
}

function Save-AttachmentSecrets($Api, $ApiCaller) {
    $target = Resolve-ServiceTarget $ServicePath $DefaultServiceRelativePath 'Attachment service'
    if ($null -eq $target) { return }
    $root = if ([string]::IsNullOrWhiteSpace($StorageRoot)) { Join-Path $env:LOCALAPPDATA $DefaultStorageRelativePath } else { $StorageRoot }
    $storage = [System.IO.Path]::GetFullPath($root)
    $service = Save-Secrets $target (Get-ExistingDocument $target) (New-ServiceDocument $Api $ApiCaller $storage) 'Attachment service'
    Test-SharedValues $Api $service $ApiCaller

    # Only the store this script chose is created; a RootPath the developer set is theirs to manage.
    $rootPath = Get-MemberValue (Get-MemberValue $service.Attachments 'Storage') 'RootPath'
    if (($rootPath -eq $storage) -and -not (Test-Path -LiteralPath $storage)) {
        New-Item -ItemType Directory -Path $storage -Force | Out-Null
        Write-Host "Created the attachment store: $storage"
    }
}

function Save-GatewaySecrets($ApiCaller) {
    $target = Resolve-ServiceTarget $GatewayPath $DefaultGatewayRelativePath 'integration gateway'
    if ($null -eq $target) { return }
    $gateway = Save-Secrets $target (Get-ExistingDocument $target) (New-GatewayDocument $ApiCaller) 'integration gateway'
    if (-not (Test-Trusts $gateway $ApiServiceName $ApiCaller)) {
        Write-TrustWarning "the gateway does not trust the API's client key ($ApiServiceName)" 'the API cannot use the gateway'
    }
}

# Writes or completes the Ops Console API file; returns its caller identity, or $null when it is not written.
function Save-OpsSecrets {
    $target = Resolve-ServiceTarget $OpsPath $DefaultOpsRelativePath 'Ops Console API'
    if ($null -eq $target) { return $null }
    $ops = Save-Secrets $target (Get-ExistingDocument $target) (New-OpsDocument) 'Ops Console API'
    return Get-Caller $ops $target
}

$skipped = Get-SkippedServices
$apiTarget = Resolve-Target $Path $DefaultRelativePath
if (-not (Test-Path -LiteralPath (Split-Path -Parent $apiTarget) -PathType Container)) {
    Exit-Usage "The folder of '$apiTarget' does not exist."
}

# The API file is checked before anything is written; the Ops Console key comes first: the API file trusts its public half.
$apiExisting = Get-ExistingDocument $apiTarget
$opsCaller = if ($skipped -contains $ServiceIds.Ops) { $null } else { Save-OpsSecrets }
$api = Save-Secrets $apiTarget $apiExisting (New-ApiDocument $opsCaller) 'API'
Write-Host "Break-glass user name: $BreakGlassUserName (password in the file, never printed)"
if ($null -ne $opsCaller) { Test-OpsTrust $api $opsCaller }

$apiCaller = Get-Caller $api $apiTarget
if ($skipped -notcontains $ServiceIds.Attachments) { Save-AttachmentSecrets $api $apiCaller }
if ($skipped -notcontains $ServiceIds.Gateway) { Save-GatewaySecrets $apiCaller }
exit $ExitWritten
