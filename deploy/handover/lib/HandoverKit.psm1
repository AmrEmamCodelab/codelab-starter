# Handover kit library (ADR-008). Imported by New-HandoverKit.ps1, Test-HandoverKit.ps1 and the tests.
Set-StrictMode -Version Latest

$libraryParts = @(
    'KitConstants.ps1', 'KitProcess.ps1', 'KitPaths.ps1', 'KitManifest.ps1', 'KitWorkTree.ps1',
    'KitNuGet.ps1', 'KitNpm.ps1', 'KitSources.ps1', 'KitPrerequisites.ps1', 'KitPresets.ps1')
foreach ($part in $libraryParts) { . (Join-Path $PSScriptRoot $part) }

Export-ModuleMember -Function *
