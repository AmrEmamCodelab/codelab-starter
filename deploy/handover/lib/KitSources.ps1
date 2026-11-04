# Codelab package source zips (c): every folder under packages\dotnet and packages\npm, plus packages\vectors,
# as sources\<source-relative folder>.zip. Entry names keep the source-relative path, so extracting every zip at
# one root rebuilds packages\ as it is in the starter. Build output (bin, obj, node_modules, dist) is left out.

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Source-relative package folders to zip: each child of a group folder, then each whole folder that exists.
function Get-KitPackageSourceFolders([string] $SourceRoot) {
    $folders = @()
    foreach ($group in $script:PackageSourceGroups) {
        $groupPath = Join-Path $SourceRoot $group
        if (-not (Test-Path -LiteralPath $groupPath)) { continue }
        $folders += @(Get-ChildItem -LiteralPath $groupPath -Directory | Sort-Object Name |
            ForEach-Object { Get-KitRelativePath $_.FullName $SourceRoot })
    }
    foreach ($folder in $script:PackageSourceFolders) {
        if (Test-Path -LiteralPath (Join-Path $SourceRoot $folder)) { $folders += $folder.Replace('\', '/') }
    }
    return $folders
}

# Folders the contract names that do not exist yet (reported in the manifest, never silently skipped).
function Get-KitMissingSourceFolders([string] $SourceRoot) {
    $all = @($script:PackageSourceGroups) + @($script:PackageSourceFolders)
    return @($all | Where-Object { -not (Test-Path -LiteralPath (Join-Path $SourceRoot $_)) } | ForEach-Object { $_.Replace('\', '/') })
}

function New-KitZip([string] $ZipPath, [string] $SourceRoot, [string[]] $RelativeFiles) {
    $folder = Split-Path -Parent $ZipPath
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $RelativeFiles) {
            $full = Join-Path $SourceRoot ($relative.Replace('/', '\'))
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $full, $relative, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        $archive.Dispose()
    }
}

# Writes one zip per package folder; returns the zips' kit-relative paths.
function New-KitSourceZips([string] $SourceRoot, [string] $KitPath, [string[]] $SourceFiles) {
    $written = @()
    foreach ($folder in Get-KitPackageSourceFolders $SourceRoot) {
        $prefix = $folder + '/'
        $files = @($SourceFiles | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } | Sort-Object)
        if ($files.Count -eq 0) { continue }
        $zipRelative = "$($script:KitSourcesDir)/$folder$($script:ZipExtension)"
        New-KitZip (Join-Path $KitPath ($zipRelative.Replace('/', '\'))) $SourceRoot $files
        $written += $zipRelative
    }
    return $written
}
