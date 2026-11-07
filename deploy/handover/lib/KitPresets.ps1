# Presets (-Preset): a named closure, so a full handover never depends on someone remembering every project.
# 'Full' is the backend, the services (attachment-service, integration-gateway, ops-console) and the frontends
# (starter-web, ops-web) with the Codelab npm packages they build against. A preset also demands that every
# package-lock.json in the source tree is an install root of the kit, so no npm closure is left out.

function Get-KitPresetNames { return @($script:KitPresets.Keys | Sort-Object) }

# Expands -Preset into source-relative entries. Entries missing from the source root (an optional module removed
# by init, or packages\ in a fork) are returned in Skipped, never silently dropped.
function Resolve-KitPreset([string] $SourceRoot, [string] $Name) {
    $empty = [pscustomobject]@{ Name = $null; Projects = @(); NpmProjects = @(); Skipped = @() }
    if (-not $Name) { return $empty }
    $key = @($script:KitPresets.Keys | Where-Object { $_ -ieq $Name }) | Select-Object -First 1
    if (-not $key) { Stop-KitUsage "Unknown preset '$Name' (known: $((Get-KitPresetNames) -join ', '))." }
    $preset = $script:KitPresets[$key]
    $skipped = @()
    $projects = @()
    foreach ($entry in $preset.Projects) {
        if (Test-Path -LiteralPath (Join-Path $SourceRoot $entry) -PathType Leaf) { $projects += $entry } else { $skipped += $entry.Replace('\', '/') }
    }
    $npm = @()
    foreach ($entry in $preset.NpmProjects) {
        if (Test-Path -LiteralPath (Join-Path (Join-Path $SourceRoot $entry) $script:PackageJsonFile) -PathType Leaf) { $npm += $entry } else { $skipped += $entry.Replace('\', '/') }
    }
    return [pscustomobject]@{ Name = $key; Projects = $projects; NpmProjects = $npm; Skipped = $skipped }
}

# Preset entries first (they carry the build order), then explicit ones; case-insensitive de-duplication.
function Join-KitEntryLists([string[]] $First, [string[]] $Second) {
    $seen = @{}
    $joined = @()
    foreach ($entry in @($First) + @($Second)) {
        if (-not $entry) { continue }
        $key = $entry.Replace('\', '/').TrimEnd('/').ToLowerInvariant()
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $joined += $entry
    }
    return $joined
}

# Source-relative package-lock.json files that no install root covers. $SourceFiles: Get-KitSourceFiles output.
function Get-KitUncoveredLockFiles([string[]] $SourceFiles, [string[]] $InstallRoots) {
    $covered = @{}
    foreach ($root in @($InstallRoots)) {
        $lock = $script:PackageLockFile
        if ($root) { $lock = "$($root.Replace('\', '/'))/$($script:PackageLockFile)" }
        $covered[$lock.ToLowerInvariant()] = $true
    }
    $suffix = '/' + $script:PackageLockFile
    return @($SourceFiles | Where-Object {
        ($_ -ieq $script:PackageLockFile -or $_.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) -and
        -not $covered.ContainsKey($_.ToLowerInvariant())
    } | Sort-Object)
}
