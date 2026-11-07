using System.Text.Json;

namespace Codelab.Integration.Gateway;

/// <summary>
/// The JSON settings of the relay DTOs: web defaults (camelCase members, case-insensitive reading, numbers as
/// numbers). The gateway's minimal APIs use the same defaults, so both sides agree without attributes.
/// </summary>
public static class GatewayJson
{
    /// <summary>The shared, read-only serializer options.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
