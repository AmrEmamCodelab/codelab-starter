using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;

namespace Codelab.OpsConsole.Api.Tests.Hosting;

/// <summary>The committed host files: no key material, Windows sign-in forwarded by IIS, the dev launch profile on 5188.</summary>
public sealed class ConfigurationFilesTests
{
    private const string ProjectFolder = "src/Codelab.OpsConsole.Api";
    private const string ServiceFolder = "services/ops-console";

    private static readonly string[] KeyMaterialNames = ["MaterialBase64", "PrivateKeyPem", "PublicKeyPem", "KeyId", "ApiKey", "Secret", "Password"];

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void Committed_settings_carry_no_key_material(string fileName)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(ProjectFile(fileName)));
        var keys = AllKeys(settings.RootElement, prefix: string.Empty).ToList();

        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, key => KeyMaterialNames.Any(name => key.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_shipped_settings_leave_the_dev_operator_off_and_the_deployment_values_as_gaps()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(ProjectFile("appsettings.json")));
        var console = settings.RootElement.GetProperty("Codelab").GetProperty("OpsConsole");

        Assert.False(console.GetProperty("DevOperator").GetProperty("Enabled").GetBoolean());
        Assert.Equal(string.Empty, console.GetProperty("OperatorGroup").GetString());
        Assert.Equal(string.Empty, console.GetProperty("Api").GetProperty("BaseUrl").GetString());
    }

    [Fact]
    public void The_iis_template_forwards_windows_sign_in_and_admits_only_reads()
    {
        var webConfig = XDocument.Load(ProjectFile("web.config"));

        var module = webConfig.Descendants("aspNetCore").Single();
        Assert.Equal("true", (string?)module.Attribute("forwardWindowsAuthToken"));
        Assert.Contains("Codelab.OpsConsole.Api.dll", (string?)module.Attribute("arguments"), StringComparison.Ordinal);
        Assert.Equal("GET,HEAD", (string?)webConfig.Descendants("handlers").Elements("add").Single().Attribute("verb"));
    }

    [Fact]
    public void The_development_profile_listens_on_5188()
    {
        using var launch = JsonDocument.Parse(File.ReadAllText(ProjectFile("Properties/launchSettings.json")));

        var profile = launch.RootElement.GetProperty("profiles").GetProperty("Development");

        Assert.Equal("http://localhost:5188", profile.GetProperty("applicationUrl").GetString());
    }

    private static IEnumerable<string> AllKeys(JsonElement element, string prefix)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in element.EnumerateObject())
        {
            var key = prefix.Length == 0 ? property.Name : prefix + ":" + property.Name;
            yield return key;
            foreach (var child in AllKeys(property.Value, key))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// A file of the host project: next to this source file's own path when the compiler kept it (the build output may
    /// live anywhere, such as an artifacts folder), otherwise searched above the output and the working folder.
    /// </summary>
    private static string ProjectFile(string relativePath, [CallerFilePath] string thisFile = "")
    {
        var fromSource = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile) ?? ".", "..", "..", "..", ProjectFolder, relativePath));
        if (File.Exists(fromSource))
        {
            return fromSource;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
            {
                var candidate = Path.Combine(folder.FullName, ServiceFolder, ProjectFolder, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException($"{ProjectFolder}/{relativePath} not found from {thisFile} or above {AppContext.BaseDirectory}.");
    }
}
