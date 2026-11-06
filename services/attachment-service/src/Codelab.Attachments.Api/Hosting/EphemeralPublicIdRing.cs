using System.Security.Cryptography;
using Codelab.Security.PublicIds;

namespace Codelab.Attachments.Api.Hosting;

/// <summary>
/// The service holds no key that could read an API identifier: nothing it receives is a PublicId (file ids are its own
/// random ids, record identity travels sealed inside the ticket subject). Codelab.Security still requires a PublicId
/// ring at start, so the host gives it a random ring that lives only in this process. A configured
/// <c>Codelab:Security:PublicIds</c> section is refused, so an operator cannot copy the API's ring here by mistake.
/// </summary>
public static class EphemeralPublicIdRing
{
    /// <summary>The key id of the in-process ring (<c>[a-z0-9]{1,8}</c>).</summary>
    public const string KeyId = "proc";

    private const int MaterialBytes = 32;

    /// <summary>Adds a fresh random PublicId ring as the last configuration source.</summary>
    /// <exception cref="InvalidOperationException">A PublicId ring is configured.</exception>
    public static IHostApplicationBuilder AddEphemeralPublicIdRing(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Configuration.GetSection(PublicIdKeyringOptions.SectionName).GetChildren().Any())
        {
            throw new InvalidOperationException(
                $"{PublicIdKeyringOptions.SectionName} must not be configured for the attachment service: it never reads an API identifier. Remove the section.");
        }

        builder.Configuration.AddInMemoryCollection(CreateSettings());
        return builder;
    }

    /// <summary>The settings of a new random ring (a new key on every call).</summary>
    public static IReadOnlyDictionary<string, string?> CreateSettings()
    {
        var section = PublicIdKeyringOptions.SectionName;
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{section}:{nameof(PublicIdKeyringOptions.ActiveKeyId)}"] = KeyId,
            [$"{section}:{nameof(PublicIdKeyringOptions.Keys)}:0:{nameof(Codelab.Security.Keys.KeyEntryOptions.KeyId)}"] = KeyId,
            [$"{section}:{nameof(PublicIdKeyringOptions.Keys)}:0:{nameof(Codelab.Security.Keys.KeyEntryOptions.MaterialBase64)}"] =
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(MaterialBytes)),
        };
    }
}
