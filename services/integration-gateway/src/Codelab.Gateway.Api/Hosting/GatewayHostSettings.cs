using Codelab.Integration.Gateway;

namespace Codelab.Gateway.Api.Hosting;

/// <summary>
/// The gateway's own settings outside the profile sections: its product identity (ADR-003, the log and trace name
/// comes from configuration so a fork renames it with the rest of its settings) and its request ceiling.
/// </summary>
public static class GatewayHostSettings
{
    /// <summary>The configuration key of the service name.</summary>
    public const string ServiceNameKey = "Gateway:ServiceName";

    /// <summary>The service name when none is configured.</summary>
    public const string DefaultServiceName = "starter-gateway";

    /// <summary>
    /// The largest request body the host accepts, in bytes: the larger of the relay request ceilings. The service
    /// assertion's <c>MaxBodyBytes</c> and the IIS <c>maxAllowedContentLength</c> ship with the same value.
    /// </summary>
    public const int MaxRequestBodyBytes = GatewayRelayLimits.MaxSsoTokenFormBytes > GatewayRelayLimits.MaxSmsRequestBytes
        ? GatewayRelayLimits.MaxSsoTokenFormBytes
        : GatewayRelayLimits.MaxSmsRequestBytes;

    /// <summary>The configured service name, trimmed, or <see cref="DefaultServiceName"/>.</summary>
    public static string ServiceNameFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[ServiceNameKey];
        return string.IsNullOrWhiteSpace(configured) ? DefaultServiceName : configured.Trim();
    }

    /// <summary>
    /// The profile keys enabled in <c>Codelab:Integration:Gateway:Profiles</c>, read while services are registered:
    /// only an enabled profile registers (and validates) its upstream client.
    /// </summary>
    public static IReadOnlySet<string> EnabledProfilesFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new GatewayOptions();
        configuration.GetSection(GatewayOptions.SectionName).Bind(options);
        return options.Profiles.ToHashSet(StringComparer.Ordinal);
    }
}
