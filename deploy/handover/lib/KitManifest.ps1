# manifest.json: identifies a kit (kind + schemaVersion) and lists the SHA-256 of every other file in it.

function Read-KitManifest([string] $KitPath) {
    $path = Join-Path $KitPath $script:KitManifestFile
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try {
        return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json)
    }
    catch {
        Write-Warning "Unreadable $($script:KitManifestFile) in '$KitPath': $($_.Exception.Message)"
        return $null
    }
}

function Test-KitIsKitFolder([string] $KitPath) {
    $manifest = Read-KitManifest $KitPath
    if ($null -eq $manifest) { return $false }
    return (($manifest.PSObject.Properties.Name -contains 'kind') -and $manifest.kind -eq $script:KitKind)
}

function Get-KitFileHashText([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm $script:HashAlgorithm).Hash.ToLowerInvariant()
}

# Every file under the kit except the manifest itself, sorted ordinally by its forward-slash relative path.
function Get-KitFileEntries([string] $KitPath) {
    $entries = foreach ($file in Get-ChildItem -LiteralPath $KitPath -Recurse -File -Force) {
        $relative = Get-KitRelativePath $file.FullName $KitPath
        if ($relative -eq $script:KitManifestFile) { continue }
        [pscustomobject][ordered]@{ path = $relative; size = $file.Length; sha256 = (Get-KitFileHashText $file.FullName) }
    }
    $list = [Collections.Generic.List[object]]::new()
    foreach ($entry in @($entries)) { if ($entry) { $list.Add($entry) } }
    $list.Sort([Comparison[object]] { param($a, $b) [string]::CompareOrdinal($a.path, $b.path) })
    return $list.ToArray()
}

# Writes manifest.json; $Details (an ordered hashtable) is stored under the identity fields and before 'files'.
function Write-KitManifest([string] $KitPath, [Collections.IDictionary] $Details) {
    $manifest = [ordered]@{ kind = $script:KitKind; schemaVersion = $script:KitSchemaVersion }
    foreach ($key in $Details.Keys) { $manifest[$key] = $Details[$key] }
    $manifest['files'] = @(Get-KitFileEntries $KitPath)
    $json = $manifest | ConvertTo-Json -Depth $script:JsonDepth
    Write-KitText (Join-Path $KitPath $script:KitManifestFile) $json
    return $manifest
}

# Returns the list of integrity problems (empty when every listed file is present, unchanged, and nothing extra).
function Test-KitManifestIntegrity([string] $KitPath) {
    $problems = [Collections.Generic.List[string]]::new()
    $manifest = Read-KitManifest $KitPath
    if ($null -eq $manifest -or -not (Test-KitIsKitFolder $KitPath)) {
        $problems.Add("No $($script:KitKind) $($script:KitManifestFile) in '$KitPath'.")
        return $problems.ToArray()
    }
    if ($manifest.schemaVersion -ne $script:KitSchemaVersion) {
        $problems.Add("Unsupported schemaVersion $($manifest.schemaVersion) (expected $($script:KitSchemaVersion)).")
    }
    $listed = @{}
    foreach ($entry in @($manifest.files)) {
        $listed[$entry.path] = $true
        $full = Join-Path $KitPath ($entry.path.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { $problems.Add("missing: $($entry.path)"); continue }
        if ((Get-KitFileHashText $full) -ne $entry.sha256) { $problems.Add("changed: $($entry.path)") }
    }
    foreach ($file in Get-ChildItem -LiteralPath $KitPath -Recurse -File -Force) {
        $relative = Get-KitRelativePath $file.FullName $KitPath
        if ($relative -ne $script:KitManifestFile -and -not $listed.ContainsKey($relative)) { $problems.Add("not listed: $relative") }
    }
    return $problems.ToArray()
}
