using Microsoft.Extensions.Configuration.Memory;

namespace Starter.DevIdp.Hosting;

/// <summary>
/// The tool's <c>appsettings.Development.json</c>, compiled in as an embedded resource (see the csproj for why it is
/// not a copied file). It is added as the LOWEST-priority configuration source, so environment variables, the command
/// line and test overrides all win over it.
/// </summary>
internal static class DevIdpEmbeddedSettings
{
    /// <summary>The manifest resource name set in the csproj.</summary>
    public const string ResourceName = "Starter.DevIdp.appsettings.Development.json";

    /// <summary>Inserts the embedded settings before every other source of <paramref name="configuration"/>.</summary>
    public static void AddAsDefaults(IConfigurationBuilder configuration)
    {
        var values = Read();
        configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = values });
    }

    /// <summary>The embedded settings as flat configuration pairs.</summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> Read()
    {
        using var stream = typeof(DevIdpEmbeddedSettings).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is missing from the build.");
        return new ConfigurationBuilder().AddJsonStream(stream).Build().AsEnumerable().ToList();
    }
}
