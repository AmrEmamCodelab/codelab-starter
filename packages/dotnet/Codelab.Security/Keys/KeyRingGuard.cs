using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Keys;

/// <summary>Key-ring guard switches (<c>Codelab:Security:KeyGuards</c>).</summary>
public sealed class KeyGuardOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.KeyGuards;

    /// <summary>
    /// Refuse every key whose digest is on the published-material denylist. On by default; it may be switched off
    /// only in the Development environment (a start-up check refuses it anywhere else).
    /// </summary>
    public bool ValidateNotPublishedMaterial { get; set; } = true;
}

/// <summary>
/// The fail-fast check every key ring passes at start: the structural rules of <see cref="KeyMaterialValidator"/>,
/// then the published-material denylist. A boot configured with a harvest-source key or a starter test fixture key
/// fails here with the setting named and the value never echoed.
/// </summary>
public sealed class KeyRingGuard
{
    private readonly PublishedMaterialDenylist _denylist;
    private readonly bool _checkPublished;

    /// <summary>Creates the guard.</summary>
    public KeyRingGuard(PublishedMaterialDenylist denylist, bool checkPublished)
    {
        ArgumentNullException.ThrowIfNull(denylist);
        _denylist = denylist;
        _checkPublished = checkPublished;
    }

    /// <summary>Checks a ring structurally, then against the denylist.</summary>
    public KeyMaterialResult Check(KeyRingOptions ring, string sectionPath)
    {
        var structure = KeyMaterialValidator.ValidateRing(ring, sectionPath);
        return structure.IsValid ? CheckNotPublished(ring.Keys, sectionPath) : structure;
    }

    /// <summary>Checks loose key values (e.g. PEM text) against the denylist only.</summary>
    public KeyMaterialResult CheckNotPublished(IEnumerable<KeyEntryOptions> keys, string sectionPath)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (!_checkPublished)
        {
            return KeyMaterialResult.Ok();
        }

        foreach (var key in keys)
        {
            if (key.MaterialBase64 is { Length: > 0 } material && _denylist.IsPublished(material))
            {
                return KeyMaterialResult.Fail(
                    $"{sectionPath}:Keys[{key.KeyId}] is PUBLISHED key material (a harvest-source key or a starter test fixture key). Every structural check passes on it, and anyone with the source can mint and read what it protects. Generate a fresh key (init.ps1) and supply it from the environment or a secret store.");
            }
        }

        return KeyMaterialResult.Ok();
    }
}

/// <summary>Builds the <see cref="KeyRingGuard"/> from the environment and <see cref="KeyGuardOptions"/>.</summary>
internal static class KeyRingGuardFactory
{
    internal static KeyRingGuard Create(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<KeyGuardOptions>>().Value;
        var denylist = services.GetService<PublishedMaterialDenylist>() ?? PublishedMaterialDenylist.Embedded;
        return new KeyRingGuard(denylist, checkPublished: options.ValidateNotPublishedMaterial);
    }
}

/// <summary>Refuses <c>ValidateNotPublishedMaterial = false</c> outside Development.</summary>
internal sealed class KeyGuardOptionsValidator(IHostEnvironment environment) : IValidateOptions<KeyGuardOptions>
{
    public ValidateOptionsResult Validate(string? name, KeyGuardOptions options) =>
        options.ValidateNotPublishedMaterial || environment.IsDevelopment()
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{KeyGuardOptions.SectionName}:{nameof(KeyGuardOptions.ValidateNotPublishedMaterial)} may be false only in Development; environment '{environment.EnvironmentName}' must refuse published key material.");
}
