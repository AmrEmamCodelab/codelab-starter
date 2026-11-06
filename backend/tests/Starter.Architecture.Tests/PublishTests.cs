using System.Xml.Linq;
using Starter.Architecture.Tests.Support;

namespace Starter.Architecture.Tests;

/// <summary>
/// The host never publishes the gitignored per-machine secrets (appsettings.*.local.json, written by
/// tools/devseed/New-LocalSecrets.ps1): a publish folder or zip must not carry the dev key rings.
/// </summary>
public sealed class PublishTests
{
    private const string SourceFolder = "backend/src";
    private const string ProjectExtension = ".csproj";
    private const string LocalSecretsPattern = "appsettings.*.local.json";
    private const string NeverCopy = "Never";

    [Fact]
    public void The_gitignored_local_secrets_are_never_published()
    {
        var hostFolder = StarterSolution.Host.GetName().Name!;
        var path = Path.Combine(StarterSolution.RepositoryRootPath, SourceFolder, hostFolder, hostFolder + ProjectExtension);

        var project = XDocument.Load(path);

        Assert.Contains(project.Descendants("Content"), element => (string?)element.Attribute("Update") == LocalSecretsPattern
            && (string?)element.Attribute("CopyToPublishDirectory") == NeverCopy);
    }
}
