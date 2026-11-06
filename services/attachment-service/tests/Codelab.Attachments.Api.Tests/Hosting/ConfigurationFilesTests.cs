using System.Text.Json;
using System.Xml.Linq;
using Codelab.Attachments.Api.Endpoints;
using Codelab.Security.Uploads;
using Microsoft.Extensions.Configuration;

namespace Codelab.Attachments.Api.Tests.Hosting;

/// <summary>The committed host files: no key material, an IIS ceiling that fits a legal upload, the dev launch profile.</summary>
public sealed class ConfigurationFilesTests
{
    private const string ProjectFolder = "src/Codelab.Attachments.Api";
    private const string ServiceFolder = "services/attachment-service";
    private const string LocalSecretsPattern = "appsettings.*.local.json";
    private const string NeverCopy = "Never";

    private static readonly string[] KeyMaterialNames = ["MaterialBase64", "PrivateKeyPem", "PublicKeyPem", "Keys", "TrustedServices", "ApiKey", "Secret", "Password"];

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void Committed_settings_carry_no_key_material(string fileName)
    {
        var keys = AllKeys(JsonDocument.Parse(File.ReadAllText(ProjectFile(fileName))).RootElement, prefix: string.Empty).ToList();

        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, key => KeyMaterialNames.Any(name => key.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_iis_request_limit_fits_the_upload_ceiling_of_the_shipped_settings()
    {
        var options = new UploadSafetyOptions();
        new ConfigurationBuilder().AddJsonFile(ProjectFile("appsettings.json")).Build().GetSection(UploadSafetyOptions.SectionName).Bind(options);
        var webConfig = XDocument.Load(ProjectFile("web.config"));

        var limit = long.Parse(webConfig.Descendants("requestLimits").Single().Attribute("maxAllowedContentLength")!.Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(limit >= UploadBodyCeiling.For(options), $"web.config maxAllowedContentLength {limit} is below the upload ceiling {UploadBodyCeiling.For(options)}.");
    }

    [Fact]
    public void The_iis_template_removes_webdav_so_delete_reaches_the_service()
    {
        var webConfig = XDocument.Load(ProjectFile("web.config"));

        Assert.Contains(webConfig.Descendants("modules").Elements("remove"), element => (string?)element.Attribute("name") == "WebDAVModule");
        Assert.Contains(webConfig.Descendants("aspNetCore"), element => ((string?)element.Attribute("arguments"))?.Contains("Codelab.Attachments.Api.dll", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void The_development_profile_listens_on_5185()
    {
        using var launch = JsonDocument.Parse(File.ReadAllText(ProjectFile("Properties/launchSettings.json")));

        var profile = launch.RootElement.GetProperty("profiles").GetProperty("Development");

        Assert.Equal("http://localhost:5185", profile.GetProperty("applicationUrl").GetString());
    }

    [Fact]
    public void The_gitignored_local_secrets_are_never_published()
    {
        var project = XDocument.Load(ProjectFile("Codelab.Attachments.Api.csproj"));

        Assert.Contains(project.Descendants("Content"), element => (string?)element.Attribute("Update") == LocalSecretsPattern
            && (string?)element.Attribute("CopyToPublishDirectory") == NeverCopy);
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
            foreach (var nested in AllKeys(property.Value, key))
            {
                yield return nested;
            }
        }
    }

    private static string ProjectFile(string relativePath)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, ServiceFolder, ProjectFolder, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(folder.FullName, ProjectFolder, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{ProjectFolder}/{relativePath} was not found above the test output folder.");
    }
}
