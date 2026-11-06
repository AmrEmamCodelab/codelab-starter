namespace Codelab.Attachments.Api.Hosting;

/// <summary>
/// The service's product identity (ADR-003): its log and trace name comes from configuration, so a fork renames it
/// with the rest of its settings and no code names the product.
/// </summary>
public static class AttachmentsHostSettings
{
    /// <summary>The configuration key of the service name.</summary>
    public const string ServiceNameKey = "Attachments:ServiceName";

    /// <summary>The service name when none is configured.</summary>
    public const string DefaultServiceName = "starter-attachments";

    /// <summary>The configured service name, trimmed, or <see cref="DefaultServiceName"/>.</summary>
    public static string ServiceNameFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[ServiceNameKey];
        return string.IsNullOrWhiteSpace(configured) ? DefaultServiceName : configured.Trim();
    }
}
