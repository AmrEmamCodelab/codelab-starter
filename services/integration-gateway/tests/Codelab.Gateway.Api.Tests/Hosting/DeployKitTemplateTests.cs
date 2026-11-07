using System.Runtime.CompilerServices;
using System.Text.Json;
using Codelab.Gateway.Api.Hosting;
using Codelab.Gateway.Api.Sms;
using Codelab.Gateway.Api.Sso;
using Codelab.Integration.Gateway;
using Codelab.Security.ServiceAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Codelab.Gateway.Api.Tests.Hosting;

/// <summary>
/// The production deploy kit's settings (deploy/kits/templates/gateway) against this host: the template, layered on the
/// package appsettings.json the way the server reads them, enables the profiles the host reads, carries the audience
/// the API signs for, and, once the operator values are filled, passes the host's own start-up validators. Every
/// path the kit requires exists in those files, so a renamed host setting fails here instead of on a server.
/// </summary>
public sealed class DeployKitTemplateTests
{
    private const string TemplateFile = "deploy/kits/templates/gateway/appsettings.template.json";
    private const string RequirementsFile = "deploy/kits/templates/gateway/required-settings.json";
    private const string PackageSettingsFile = "services/integration-gateway/src/Codelab.Gateway.Api/appsettings.json";
    private const string ApiSettingsFile = "backend/src/Starter.Api/appsettings.json";
    private const string ApiGatewayAudienceKey = GatewayOptions.SectionName + ":Audience";
    private const string RequirementWildcard = "*";
    private const string FirstItem = "0";

    [Fact]
    public void The_template_enables_the_sso_profile_as_the_host_reads_it()
    {
        var configuration = KitConfiguration(filled: false);

        Assert.Equal([GatewayProfileKey.Sso], GatewayHostSettings.EnabledProfilesFrom(configuration));
    }

    [Fact]
    public void The_template_audience_is_the_one_the_api_signs_for()
    {
        var configuration = KitConfiguration(filled: false);
        var api = new ConfigurationBuilder().AddJsonFile(RepoFile(ApiSettingsFile)).Build();

        Assert.Equal(GatewayDefaults.Audience, configuration[$"{ServiceAssertionServerOptions.SectionName}:Audience"]);
        Assert.Equal(GatewayDefaults.Audience, api[ApiGatewayAudienceKey]);
    }

    [Fact]
    public void The_unfilled_template_stops_the_start_on_the_sso_upstream()
    {
        var result = new SsoRelayOptionsValidator(Production()).Validate(name: null, Bind<SsoRelayOptions>(KitConfiguration(filled: false), SsoRelayOptions.SectionName));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(SsoRelayOptions.TokenEndpoint), StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(SsoRelayOptions.JwksUri), StringComparison.Ordinal));
    }

    [Fact]
    public void The_filled_template_passes_the_host_validators_of_both_profiles()
    {
        var configuration = KitConfiguration(filled: true);

        var sso = new SsoRelayOptionsValidator(Production()).Validate(name: null, Bind<SsoRelayOptions>(configuration, SsoRelayOptions.SectionName));
        var sms = new SmsProviderOptionsValidator(Production()).Validate(name: null, Bind<SmsProviderOptions>(configuration, SmsProviderOptions.SectionName));

        Assert.True(sso.Succeeded, sso.FailureMessage);
        Assert.True(sms.Succeeded, sms.FailureMessage);
        Assert.True(GatewayHostSettings.EnabledProfilesFrom(configuration).SetEquals([GatewayProfileKey.Sso, GatewayProfileKey.Sms]));
    }

    [Fact]
    public void Every_path_the_kit_requires_exists_in_the_files_the_host_reads()
    {
        var configuration = KitConfiguration(filled: false);
        using var requirements = JsonDocument.Parse(File.ReadAllText(RepoFile(RequirementsFile)));

        var paths = requirements.RootElement.GetProperty("requirements").EnumerateArray().Select(requirement => requirement.GetProperty("path").GetString()!).ToList();

        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.True(configuration.GetSection(path.Replace(RequirementWildcard, FirstItem, StringComparison.Ordinal)).Exists(), $"{path} is not in the template or the package settings."));
        Assert.Contains($"{SsoRelayOptions.SectionName}:{nameof(SsoRelayOptions.TokenEndpoint)}", paths);
        Assert.Contains($"{SsoRelayOptions.SectionName}:{nameof(SsoRelayOptions.JwksUri)}", paths);
        Assert.Contains($"{SmsProviderOptions.SectionName}:{nameof(SmsProviderOptions.UrlTemplate)}", paths);
    }

    /// <summary>The package appsettings.json, then the kit template, then (when filled) sample operator values.</summary>
    private static IConfigurationRoot KitConfiguration(bool filled)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(RepoFile(PackageSettingsFile))
            .AddJsonFile(RepoFile(TemplateFile));
        if (filled)
        {
            builder.AddInMemoryCollection(OperatorValues());
        }

        return builder.Build();
    }

    private static Dictionary<string, string?> OperatorValues() => new(StringComparer.OrdinalIgnoreCase)
    {
        [$"{GatewayOptions.SectionName}:Profiles:1"] = GatewayProfileKey.Sms,
        [$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:KeyId"] = "sample-key",
        [$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:PublicKeyPem"] = "sample-not-key-material",
        [$"{SsoRelayOptions.SectionName}:{nameof(SsoRelayOptions.TokenEndpoint)}"] = "https://idp.example.test/token",
        [$"{SsoRelayOptions.SectionName}:{nameof(SsoRelayOptions.JwksUri)}"] = "https://idp.example.test/jwks",
        [$"{SmsProviderOptions.SectionName}:{nameof(SmsProviderOptions.UrlTemplate)}"] = "https://sms.example.test/send?sender={sender}",
        [$"{SmsProviderOptions.SectionName}:{nameof(SmsProviderOptions.BodyTemplate)}"] = """{"to":"{to}","text":"{text}","lang":"{language}"}""",
    };

    private static T Bind<T>(IConfiguration configuration, string section)
        where T : new()
    {
        var options = new T();
        configuration.GetSection(section).Bind(options);
        return options;
    }

    private static HostingEnvironment Production() => new() { EnvironmentName = Environments.Production };

    /// <summary>A repository file, found above this source file (when the compiler kept its path) or above the build output.</summary>
    private static string RepoFile(string relativePath, [CallerFilePath] string thisFile = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(thisFile), AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var folder = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start); folder is not null; folder = folder.Parent)
            {
                var candidate = Path.Combine(folder.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException($"{relativePath} not found above {thisFile} or {AppContext.BaseDirectory}.");
    }
}
