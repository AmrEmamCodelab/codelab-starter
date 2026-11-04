# Path resolution and argument validation. Every check here raises a usage error (exit ExitUsage).

function Get-KitFullPath([string] $Path, [string] $BasePath) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Get-KitTrimmedPath([string] $Path) {
    return $Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

# True when $Path is $Parent or below it (case-insensitive, Windows file system).
function Test-KitPathInside([string] $Path, [string] $Parent) {
    $child = Get-KitTrimmedPath ([IO.Path]::GetFullPath($Path))
    $root = Get-KitTrimmedPath ([IO.Path]::GetFullPath($Parent))
    if ($child.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $child.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

# Path of $Path relative to $Root with forward slashes (the manifest and zip entry format).
function Get-KitRelativePath([string] $Path, [string] $Root) {
    $child = Get-KitTrimmedPath ([IO.Path]::GetFullPath($Path))
    $base = Get-KitTrimmedPath ([IO.Path]::GetFullPath($Root))
    if ($child.Equals($base, [StringComparison]::OrdinalIgnoreCase)) { return '' }
    return $child.Substring($base.Length + 1).Replace('\', '/')
}

function Resolve-KitDotnetProjects([string] $SourceRoot, [string[]] $Projects) {
    $resolved = @()
    foreach ($project in @($Projects | Where-Object { $_ })) {
        $full = Get-KitFullPath $project $SourceRoot
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { Stop-KitUsage "Project not found: '$project' (paths are relative to $SourceRoot)." }
        if ($script:DotnetProjectExtensions -notcontains [IO.Path]::GetExtension($full).ToLowerInvariant()) {
            Stop-KitUsage "'$project' is not a project or solution ($($script:DotnetProjectExtensions -join ', '))."
        }
        if (-not (Test-KitPathInside $full $SourceRoot)) { Stop-KitUsage "Project '$project' is outside the source root $SourceRoot." }
        $resolved += [pscustomobject]@{ Full = $full; Relative = (Get-KitRelativePath $full $SourceRoot) }
    }
    return $resolved
}

function Get-KitWorkspacePatterns([string] $PackageJsonPath) {
    $json = Read-KitJsonDictionary $PackageJsonPath
    if (-not $json.ContainsKey($script:WorkspacesKey)) { return @() }
    $workspaces = $json[$script:WorkspacesKey]
    if ($workspaces -is [Collections.IDictionary]) {
        if ($workspaces.Contains($script:WorkspacePackagesKey)) { return @($workspaces[$script:WorkspacePackagesKey]) }
        return @()
    }
    return @($workspaces)
}

# Matches a workspace glob ('packages/npm/*', 'apps/**', 'tools/cli') against a forward-slash relative path.
function Test-KitWorkspaceMatch([string] $Pattern, [string] $RelativePath) {
    $trimmed = $Pattern.TrimStart('.', '/').TrimEnd('/')
    $regex = '^' + [regex]::Escape($trimmed).Replace('\*\*', '.*').Replace('\*', '[^/]*') + '$'
    return ($RelativePath -match $regex)
}

# The folder `npm ci` must run in for an npm project: the project itself when it has a lock file, otherwise the
# nearest ancestor (inside the source root) whose lock file and workspaces cover it.
function Resolve-KitNpmInstallRoot([string] $ProjectDir, [string] $SourceRoot) {
    $current = $ProjectDir
    while ($current -and (Test-KitPathInside $current $SourceRoot)) {
        if (Test-Path -LiteralPath (Join-Path $current $script:PackageLockFile)) {
            if ($current -eq $ProjectDir) { return $current }
            $relative = Get-KitRelativePath $ProjectDir $current
            $patterns = Get-KitWorkspacePatterns (Join-Path $current $script:PackageJsonFile)
            foreach ($pattern in $patterns) { if (Test-KitWorkspaceMatch $pattern $relative) { return $current } }
            Stop-KitUsage "'$ProjectDir' has no $($script:PackageLockFile) and is not a workspace of '$current'."
        }
        $current = Split-Path -Parent $current
    }
    Stop-KitUsage "No $($script:PackageLockFile) found for '$ProjectDir'; npm ci needs a lock file."
}

function Resolve-KitNpmProjects([string] $SourceRoot, [string[]] $NpmProjects) {
    $resolved = @()
    foreach ($folder in @($NpmProjects | Where-Object { $_ })) {
        $full = Get-KitTrimmedPath (Get-KitFullPath $folder $SourceRoot)
        if (-not (Test-Path -LiteralPath (Join-Path $full $script:PackageJsonFile) -PathType Leaf)) {
            Stop-KitUsage "npm project '$folder' has no $($script:PackageJsonFile) (paths are relative to $SourceRoot)."
        }
        if (-not (Test-KitPathInside $full $SourceRoot)) { Stop-KitUsage "npm project '$folder' is outside the source root $SourceRoot." }
        $installRoot = Resolve-KitNpmInstallRoot $full $SourceRoot
        $resolved += [pscustomobject]@{
            Full = $full; Relative = (Get-KitRelativePath $full $SourceRoot)
            InstallRoot = (Get-KitRelativePath $installRoot $SourceRoot)
        }
    }
    return $resolved
}

# Refuses an unsafe output folder. Returns $true when a previous kit (identified by its manifest) must be cleared.
function Assert-KitOutputPath([string] $OutputPath, [string] $SourceRoot, [bool] $Force) {
    if ((Get-KitTrimmedPath $OutputPath) -eq (Get-KitTrimmedPath ([IO.Path]::GetPathRoot($OutputPath)))) {
        Stop-KitUsage "OutputPath '$OutputPath' is a drive root."
    }
    if ((Test-KitPathInside $OutputPath $SourceRoot) -or (Test-KitPathInside $SourceRoot $OutputPath)) {
        Stop-KitUsage "OutputPath '$OutputPath' must not be inside the source root or contain it ($SourceRoot)."
    }
    if (Test-Path -LiteralPath $OutputPath -PathType Leaf) { Stop-KitUsage "OutputPath '$OutputPath' is a file." }
    if (-not (Test-Path -LiteralPath $OutputPath)) { return $false }
    $entries = @(Get-ChildItem -LiteralPath $OutputPath -Force)
    if ($entries.Count -eq 0) { return $false }
    if (-not $Force) { Stop-KitUsage "OutputPath '$OutputPath' is not empty; pass -Force to replace a previous kit." }
    if (-not (Test-KitIsKitFolder $OutputPath)) {
        Stop-KitUsage "OutputPath '$OutputPath' is not empty and holds no handover kit manifest; -Force only replaces a previous kit."
    }
    $foreign = @($entries | Where-Object { $script:KitOwnedEntries -notcontains $_.Name })
    if ($foreign.Count -gt 0) {
        Stop-KitUsage "OutputPath '$OutputPath' holds entries that are not part of a kit: $(($foreign | ForEach-Object Name) -join ', ')."
    }
    return $true
}

# Removes only the entries a kit owns from a folder Assert-KitOutputPath accepted.
function Clear-KitPreviousOutput([string] $OutputPath) {
    foreach ($name in $script:KitOwnedEntries) {
        $path = Join-Path $OutputPath $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}
