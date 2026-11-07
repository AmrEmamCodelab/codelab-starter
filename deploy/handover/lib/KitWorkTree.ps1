# A clean copy of the source tree in %TEMP%: restores and offline builds run there, never in the caller's tree,
# so the caller's obj/ and node_modules/ are left alone.

function Test-KitExcludedPath([string] $RelativePath) {
    foreach ($segment in $RelativePath.Split('/')) {
        if ($script:BuildOutputFolders -contains $segment) { return $true }
    }
    return $false
}

function Get-KitFilesFromGit([string] $SourceRoot) {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) { return $null }
    $listing = Get-KitNativeOutput $git.Source @('-C', $SourceRoot, '-c', 'core.quotepath=off', 'ls-files', '--cached', '--others', '--exclude-standard')
    if ($null -eq $listing) { return $null }
    return @($listing -split "`r?`n" | Where-Object { $_ })
}

function Get-KitFilesFromDisk([string] $SourceRoot) {
    return @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Force |
        ForEach-Object { Get-KitRelativePath $_.FullName $SourceRoot } |
        Where-Object { -not (Test-KitExcludedPath $_) })
}

# Forward-slash relative paths of the source files: git's tracked + untracked-not-ignored list when git can list
# them, otherwise every file outside the build-output folders. Files deleted in the working tree are dropped.
# The git list is not filtered by folder name: git's ignore rules already drop build output, and a tracked source
# folder may be called bin (packages/npm/ng-tools/src/bin holds the CLI entry points).
function Get-KitSourceFiles([string] $SourceRoot) {
    $files = Get-KitFilesFromGit $SourceRoot
    if ($null -eq $files) { $files = Get-KitFilesFromDisk $SourceRoot }
    return @($files | Where-Object { Test-Path -LiteralPath (Join-Path $SourceRoot $_) -PathType Leaf })
}

function Copy-KitFile([string] $Source, [string] $Destination) {
    $folder = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-KitFolderContents([string] $Source, [string] $Destination) {
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File -Force) {
        Copy-KitFile $file.FullName (Join-Path $Destination (Get-KitRelativePath $file.FullName $Source))
    }
}

# Copies the source files (and the feed, which git ignores, when it sits inside the source root) to a new temp folder.
function New-KitWorkTree([string] $SourceRoot, [string] $FeedPath) {
    $workTree = New-KitTempFolder $script:WorkTreePrefix
    try {
        foreach ($relative in @(Get-KitSourceFiles $SourceRoot)) {
            Copy-KitFile (Join-Path $SourceRoot $relative) (Join-Path $workTree $relative)
        }
        if ($FeedPath -and (Test-Path -LiteralPath $FeedPath) -and (Test-KitPathInside $FeedPath $SourceRoot)) {
            Copy-KitFolderContents $FeedPath (Join-Path $workTree (Get-KitRelativePath $FeedPath $SourceRoot))
        }
    }
    catch {
        Remove-KitTempFolder $workTree
        throw
    }
    return $workTree
}
