# The deployables a deploy kit can carry (tools/kit/New-DeployKit.ps1). Data only: the builder reads it with
# Import-PowerShellDataFile, and the tests check every entry. Paths are relative to the repo root; a .NET project is
# found by ProjectPattern under ProjectRoot (exactly one match), so a renamed fork project still resolves.
#
#   Name          the -Component value and the order key (Order: kit order, the README's install order).
#   Folder        the kit folder the payload lands in (publish/deploy-<sha>/<Folder>).
#   Parts         what is built into that folder: Kind 1 = dotnet publish, Kind 2 = npm web build (KitPartKind).
#                 SubFolder '' puts the part in Folder itself; ops carries api\ and web\ (Install-Ops.ps1 contract).
#   InstallScript the deploy/kits script that installs the folder (the kit refuses a component without one).
#   Default       $true when New-DeployKit.ps1 builds it without -Component.
@{
    Components = @(
        @{
            Name = 'api'; Order = 1; Folder = 'api'; InstallScript = 'Install-Api.ps1'; Default = $true
            Parts = @(@{ Kind = 1; SubFolder = ''; ProjectRoot = 'backend/src/Starter.Api'; ProjectPattern = 'Starter.Api.csproj' })
        },
        @{
            Name = 'attachment'; Order = 2; Folder = 'attachment'; InstallScript = 'Install-Attachment.ps1'; Default = $true
            Parts = @(@{ Kind = 1; SubFolder = ''; ProjectRoot = 'services/attachment-service/src'; ProjectPattern = '*.Api.csproj' })
        },
        @{
            Name = 'gateway'; Order = 3; Folder = 'gateway'; InstallScript = 'Install-Gateway.ps1'; Default = $false
            Parts = @(@{ Kind = 1; SubFolder = ''; ProjectRoot = 'services/integration-gateway/src'; ProjectPattern = '*.Api.csproj' })
        },
        @{
            Name = 'web'; Order = 4; Folder = 'web'; InstallScript = 'Install-Web.ps1'; Default = $true
            Parts = @(@{ Kind = 2; SubFolder = ''; Workspace = 'frontend/starter-web' })
        },
        @{
            Name = 'ops'; Order = 5; Folder = 'ops'; InstallScript = 'Install-Ops.ps1'; Default = $false
            Parts = @(
                @{ Kind = 1; SubFolder = 'api'; ProjectRoot = 'services/ops-console/src'; ProjectPattern = '*.Api.csproj' },
                @{ Kind = 2; SubFolder = 'web'; Workspace = 'services/ops-console/web/ops-web' }
            )
        }
    )
}
