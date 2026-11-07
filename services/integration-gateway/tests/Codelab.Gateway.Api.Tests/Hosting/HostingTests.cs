using System.Text.Json;
using System.Xml.Linq;
using Codelab.Gateway.Api.Hosting;
using Codelab.Integration.Gateway;
using Codelab.Security.PublicIds;
using Codelab.Security.ServiceAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Codelab.Gateway.Api.Tests.Hosting;

public sealed class GatewayHostSettingsTests
{
    [Theory]
    [InlineData(null, GatewayHostSettings.DefaultServiceName)]
    [InlineData("  ", GatewayHostSettings.DefaultServiceName)]
    [InlineData(" fork-gateway ", "fork-gateway")]
    public void The_service_name_comes_from_configuration(string? configured, string expected)
    {
        var configuration = Configuration(new() { [GatewayHostSettings.ServiceNameKey] = configured });

        Assert.Equal(expected, GatewayHostSettings.ServiceNameFrom(configuration));
    }

    [Fact]
    public void The_enabled_profiles_are_read_from_the_shared_section()
    {
        var configuration = Configuration(new()
        {
            [$"{GatewayOptions.SectionName}:Profiles:0"] = GatewayProfileKey.Sms,
        });

        Assert.Equal([GatewayProfileKey.Sms], GatewayHostSettings.EnabledProfilesFrom(configuration));
    }

    [Fact]
    public void The_request_ceiling_covers_both_relay_bodies()
    {
        Assert.True(GatewayHostSettings.MaxRequestBodyBytes >= GatewayRelayLimits.MaxSsoTokenFormBytes);
        Assert.True(GatewayHostSettings.MaxRequestBodyBytes >= GatewayRelayLimits.MaxSmsRequestBytes);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}

public sealed class HostBuilderExtensionTests
{
    [Fact]
    public void The_ephemeral_ring_is_random_per_call()
    {
        var first = EphemeralPublicIdRing.CreateSettings();
        var second = EphemeralPublicIdRing.CreateSettings();

        Assert.Equal(EphemeralPublicIdRing.KeyId, first[$"{PublicIdKeyringOptions.SectionName}:ActiveKeyId"]);
        Assert.NotEqual(first.Values.Last(), second.Values.Last());
    }

    [Fact]
    public void A_configured_publicid_ring_is_refused()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([new($"{PublicIdKeyringOptions.SectionName}:ActiveKeyId", "k1")]);

        Assert.Throws<InvalidOperationException>(() => builder.AddEphemeralPublicIdRing());
    }

    [Fact]
    public void The_local_secrets_file_is_added_only_in_development()
    {
        var development = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        var production = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        var developmentSources = development.Configuration.Sources.Count;
        var productionSources = production.Configuration.Sources.Count;

        development.AddLocalSecretsFile();
        production.AddLocalSecretsFile();

        Assert.Equal(developmentSources + 1, development.Configuration.Sources.Count);
        Assert.Equal(productionSources, production.Configuration.Sources.Count);
        Assert.Equal("appsettings.Development.local.json", LocalSecretsFile.FileNameFor(Environments.Development));
    }
}

/// <summary>The committed host files: no key material, one request ceiling everywhere, the dev launch profile.</summary>
public sealed class ConfigurationFilesTests
{
    private const string ProjectFolder = "src/Codelab.Gateway.Api";
    private const string ServiceFolder = "services/integration-gateway";

    private static readonly string[] KeyMaterialNames = ["MaterialBase64", "PrivateKeyPem", "PublicKeyPem", "TrustedServices", "AuthHeaderValue", "ApiKey", "Secret", "Password"];

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
    public void The_shipped_settings_enable_no_profile_so_a_server_install_chooses()
    {
        var options = new GatewayOptions();
        new ConfigurationBuilder().AddJsonFile(ProjectFile("appsettings.json")).Build().GetSection(GatewayOptions.SectionName).Bind(options);

        Assert.Empty(options.Profiles);
    }

    [Fact]
    public void Iis_the_assertion_and_the_host_share_one_request_ceiling()
    {
        var server = new ServiceAssertionServerOptions();
        new ConfigurationBuilder().AddJsonFile(ProjectFile("appsettings.json")).Build().GetSection(ServiceAssertionServerOptions.SectionName).Bind(server);
        var limit = int.Parse(XDocument.Load(ProjectFile("web.config")).Descendants("requestLimits").Single().Attribute("maxAllowedContentLength")!.Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(GatewayHostSettings.MaxRequestBodyBytes, limit);
        Assert.Equal(GatewayHostSettings.MaxRequestBodyBytes, server.MaxBodyBytes);
        Assert.Equal(GatewayDefaults.Audience, server.Audience);
    }

    [Fact]
    public void The_iis_template_starts_the_gateway()
    {
        var webConfig = XDocument.Load(ProjectFile("web.config"));

        Assert.Contains(webConfig.Descendants("aspNetCore"), element => ((string?)element.Attribute("arguments"))?.Contains("Codelab.Gateway.Api.dll", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void The_development_profile_listens_on_5187()
    {
        using var launch = JsonDocument.Parse(File.ReadAllText(ProjectFile("Properties/launchSettings.json")));

        Assert.Equal("http://localhost:5187", launch.RootElement.GetProperty("profiles").GetProperty("Development").GetProperty("applicationUrl").GetString());
    }

    [Fact]
    public void The_gitignored_local_secrets_are_never_published()
    {
        var project = XDocument.Load(ProjectFile("Codelab.Gateway.Api.csproj"));

        Assert.Contains(project.Descendants("Content"), element => (string?)element.Attribute("Update") == "appsettings.*.local.json"
            && (string?)element.Attribute("CopyToPublishDirectory") == "Never");
    }

    [Fact]
    public void The_source_holds_no_other_credential_scheme_and_no_hand_made_http_client()
    {
        var sources = Directory.EnumerateFiles(Path.GetDirectoryName(ProjectFile("Program.cs"))!, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();

        Assert.NotEmpty(sources);
        Assert.DoesNotContain(sources, source => source.Contains("AddJwtBearer", StringComparison.Ordinal)
            || source.Contains("AddCookie(", StringComparison.Ordinal)
            || source.Contains("new HttpClient(", StringComparison.Ordinal)
            || source.Contains("ServerCertificateCustomValidationCallback", StringComparison.Ordinal));
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
        foreach (var start in new[] { AppContext.BaseDirectory, SourceDirectory() })
        {
            for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
            {
                foreach (var candidate in new[] { Path.Combine(folder.FullName, ServiceFolder, ProjectFolder, relativePath), Path.Combine(folder.FullName, ProjectFolder, relativePath) })
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        throw new FileNotFoundException($"{ProjectFolder}/{relativePath} was not found above the test output or source folder.");
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
