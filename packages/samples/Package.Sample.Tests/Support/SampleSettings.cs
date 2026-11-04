using Codelab.Observability;
using Codelab.Security;
using Package.Sample.Api.Archive;
using Package.Sample.Api.Authentication;

namespace Package.Sample.Tests.Support;

/// <summary>
/// The configuration a Production boot of the sample needs on top of its appsettings.json: the key material that a
/// real deployment takes from the environment, plus test switches (no log file).
/// </summary>
internal static class SampleSettings
{
    public const string PublicIdKeyId = "k1";
    public const string SigningKeyId = "s1";
    public const string CallingService = "records-worker";
    public const string CallingServiceKeyId = "w1";
    public const string Audience = "package-sample";

    public static Dictionary<string, string?> Production(SampleKeys keys)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{LogFileSettings.KeyPrefix}:{nameof(LogFileSettings.Enabled)}"] = bool.FalseString,
            [UserIdHashSettings.KeyName] = keys.UserHashKey,
            [$"{SampleAuthenticationOptions.SectionName}:{nameof(SampleAuthenticationOptions.SigningKeyBase64)}"] = keys.TokenSigningKey,
            [$"{SecuritySections.ServiceAssertionServer}:TrustedServices:0:ServiceName"] = CallingService,
            [$"{SecuritySections.ServiceAssertionServer}:TrustedServices:0:KeyId"] = CallingServiceKeyId,
            [$"{SecuritySections.ServiceAssertionServer}:TrustedServices:0:PublicKeyPem"] = keys.ServicePublicKeyPem,
        };
        SetRing(settings, SecuritySections.PublicIds, PublicIdKeyId, keys.PublicIdKey);
        SetRing(settings, SecuritySections.RequestSigning, SigningKeyId, keys.SigningMasterKey);
        return settings;
    }

    public static void SetRing(IDictionary<string, string?> settings, string section, string keyId, string material)
    {
        settings[$"{section}:ActiveKeyId"] = keyId;
        settings[$"{section}:Keys:0:KeyId"] = keyId;
        settings[$"{section}:Keys:0:MaterialBase64"] = material;
    }

    public static void PointArchiveAt(IDictionary<string, string?> settings, Uri baseAddress) =>
        settings[$"{ArchiveClient.SectionName}:BaseAddress"] = baseAddress.ToString();
}
