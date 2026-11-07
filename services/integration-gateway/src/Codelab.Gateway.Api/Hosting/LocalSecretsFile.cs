using Microsoft.Extensions.Configuration.Json;

namespace Codelab.Gateway.Api.Hosting;

/// <summary>
/// The developer's local secrets file, <c>appsettings.Development.local.json</c> next to the host (gitignored). It holds
/// the trusted caller's public key (<c>Codelab:Security:ServiceAssertions:Server:TrustedServices</c>) and, when the SMS
/// profile is used locally, the provider credential (see services/integration-gateway/MODULE.md). Loaded in
/// Development only, right after <c>appsettings.Development.json</c>, so environment variables and the command line
/// still win.
/// </summary>
public static class LocalSecretsFile
{
    private const string FilePrefix = "appsettings.";
    private const string LocalSuffix = ".local.json";
    private const string JsonSuffix = ".json";

    /// <summary>The local secrets file name for <paramref name="environmentName"/>.</summary>
    public static string FileNameFor(string environmentName) => FilePrefix + environmentName + LocalSuffix;

    /// <summary>Adds the local secrets file when the environment is Development (optional: absent is fine).</summary>
    public static IHostApplicationBuilder AddLocalSecretsFile(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Environment.IsDevelopment())
        {
            return builder;
        }

        var environmentName = builder.Environment.EnvironmentName;
        var environmentFile = FilePrefix + environmentName + JsonSuffix;
        var source = new JsonConfigurationSource { Path = FileNameFor(environmentName), Optional = true, ReloadOnChange = false };
        var sources = builder.Configuration.Sources;
        var after = sources.Select((candidate, index) => (candidate, index))
            .LastOrDefault(entry => entry.candidate is JsonConfigurationSource json && string.Equals(json.Path, environmentFile, StringComparison.OrdinalIgnoreCase));
        sources.Insert(after.candidate is null ? sources.Count : after.index + 1, source);
        return builder;
    }
}
