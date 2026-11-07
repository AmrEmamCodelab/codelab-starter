namespace Codelab.Integration.Gateway;

/// <summary>Bounds the gateway and the relay client agree on.</summary>
public static class GatewayRelayLimits
{
    private const int KiB = 1024;

    /// <summary>The largest OIDC token request form the gateway accepts, in bytes (16 KiB).</summary>
    public const int MaxSsoTokenFormBytes = 16 * KiB;

    /// <summary>The most fields an OIDC token request form may carry.</summary>
    public const int MaxSsoTokenFormFields = 32;

    /// <summary>The largest response body the relay client reads (and the gateway relays), in bytes (1 MiB).</summary>
    public const int MaxResponseBytes = KiB * KiB;

    /// <summary>The longest SMS recipient number, in characters.</summary>
    public const int MaxSmsRecipientLength = 32;

    /// <summary>The longest SMS text, in characters.</summary>
    public const int MaxSmsTextLength = 1600;

    /// <summary>The largest SMS relay request body the gateway accepts, in bytes (16 KiB).</summary>
    public const int MaxSmsRequestBytes = 16 * KiB;
}

/// <summary>Defaults of the gateway contracts.</summary>
public static class GatewayDefaults
{
    /// <summary>
    /// The default service-assertion audience of the gateway: the relay client puts it in <c>aud</c>, and the
    /// gateway's <c>Codelab:Security:ServiceAssertions:Server:Audience</c> must equal it.
    /// </summary>
    public const string Audience = "integration-gateway";
}

/// <summary>The SMS languages, as their integer values (the same values as the application's language enum).</summary>
public static class GatewayLanguages
{
    /// <summary>Arabic.</summary>
    public const int Arabic = 1;

    /// <summary>English.</summary>
    public const int English = 2;

    /// <summary>True when <paramref name="language"/> is a known language value.</summary>
    public static bool IsKnown(int language) => language is Arabic or English;
}
