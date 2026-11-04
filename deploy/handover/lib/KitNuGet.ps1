# NuGet half of the closure (a): restore every project into one packages folder, trim it to a folder feed and write
# the kit nuget.config (<clear/> + that one folder source, no nuget.org, no package source mapping).

function ConvertTo-KitXmlText([string] $Value) {
    return [Security.SecurityElement]::Escape($Value)
}

# The kit's nuget.config: one folder source and nothing else. $SourceValue is './nuget' inside the kit.
function New-KitNuGetConfigXml([string] $SourceValue) {
    $key = ConvertTo-KitXmlText $script:KitNuGetSourceKey
    $value = ConvertTo-KitXmlText $SourceValue
    return @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <!-- Codelab handover kit (ADR-008): the full dependency closure as one folder source. No online source. -->
  <packageSources>
    <clear />
    <add key="$key" value="$value" />
  </packageSources>
</configuration>
"@
}

# The nuget.config used while BUILDING the kit: Codelab.* only from the Codelab feed, everything else from
# nuget.org (the same mapping as the repo's nuget.config, with an absolute feed path).
function New-KitRestoreConfigXml([string] $FeedNuGetPath) {
    $feedKey = ConvertTo-KitXmlText $script:FeedNuGetSourceKey
    $orgKey = ConvertTo-KitXmlText $script:NuGetOrgSourceKey
    return @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="$feedKey" value="$(ConvertTo-KitXmlText $FeedNuGetPath)" />
    <add key="$orgKey" value="$(ConvertTo-KitXmlText $script:NuGetOrgSourceUrl)" protocolVersion="$($script:NuGetOrgProtocol)" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="$feedKey">
      <package pattern="$(ConvertTo-KitXmlText $script:CodelabPackagePattern)" />
    </packageSource>
    <packageSource key="$orgKey">
      <package pattern="$(ConvertTo-KitXmlText $script:AnyPackagePattern)" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
}

function Test-KitNuGetFeedFile([string] $Name) {
    foreach ($pattern in $script:NuGetFeedFilePatterns) { if ($Name -like $pattern) { return $true } }
    return $false
}

# Keeps only the files a folder source needs in every {id}/{version} folder (the .nupkg, its hash and nuspec),
# dropping the extracted package contents restore wrote next to them.
function Optimize-KitNuGetFolder([string] $PackagesDir) {
    foreach ($idDir in Get-ChildItem -LiteralPath $PackagesDir -Directory -Force) {
        foreach ($versionDir in Get-ChildItem -LiteralPath $idDir.FullName -Directory -Force) {
            foreach ($entry in Get-ChildItem -LiteralPath $versionDir.FullName -Force) {
                if ($entry.PSIsContainer -or -not (Test-KitNuGetFeedFile $entry.Name)) {
                    Remove-Item -LiteralPath $entry.FullName -Recurse -Force
                }
            }
        }
    }
}

# 'Name=Value' MSBuild properties become -p: arguments.
function ConvertTo-KitPropertyArguments([string[]] $Properties) {
    return @($Properties | Where-Object { $_ } | ForEach-Object { '-p:' + $_ })
}

function Get-KitDotnetEnvironment([string] $PackagesDir) {
    $variables = @{}
    foreach ($name in $script:QuietDotnetEnv.Keys) { $variables[$name] = $script:QuietDotnetEnv[$name] }
    $variables[$script:NuGetPackagesEnvVar] = $PackagesDir
    return $variables
}

# Restores each project of the work tree into $PackagesDir (also NUGET_PACKAGES, so MSBuild SDK packages land there).
# Returns the relative paths of the projects that failed.
function Invoke-KitNuGetRestore([string] $Dotnet, [string] $WorkTree, [object[]] $Projects, [string] $PackagesDir, [string] $ConfigPath, [string[]] $Properties) {
    $failed = @()
    $variables = Get-KitDotnetEnvironment $PackagesDir
    foreach ($project in $Projects) {
        $arguments = @('restore', (Join-Path $WorkTree $project.Relative), '--packages', $PackagesDir, '--configfile', $ConfigPath) +
            (ConvertTo-KitPropertyArguments $Properties)
        $code = Invoke-KitWithEnvironment $variables { Invoke-KitNative $Dotnet $arguments $WorkTree }
        if ($code -ne $script:ExitSuccess) { $failed += $project.Relative }
    }
    return $failed
}
