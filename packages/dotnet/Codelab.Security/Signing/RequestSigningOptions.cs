using Codelab.Security.Keys;

namespace Codelab.Security.Signing;

/// <summary>
/// Request signing (<c>Codelab:Security:RequestSigning</c>): the mode, the audience every proof is bound to, the
/// accepted proof age and the master key ring the per-session keys are derived from (the token's <c>skid</c> claim
/// names the ring entry).
/// </summary>
public sealed class RequestSigningOptions : KeyRingOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.RequestSigning;

    /// <summary>The default accepted proof age, both directions.</summary>
    public const int DefaultMaxAgeSeconds = 120;

    /// <summary>Off (default), Report or Enforce.</summary>
    public SecurityEnforcementMode Mode { get; set; } = SecurityEnforcementMode.Off;

    /// <summary>The deployment's audience (a signed field): separates one deployment's signatures from another's.</summary>
    public string? Audience { get; set; }

    /// <summary>The accepted proof age in seconds, in both directions; also sizes the replay retention.</summary>
    public int MaxAgeSeconds { get; set; } = DefaultMaxAgeSeconds;

    /// <summary>The largest request body hashed for a proof; a bigger signed body is refused 413.</summary>
    public int MaxSignedBodyBytes { get; set; } = 1_048_576;

    /// <summary>The structural verdict (audience, age, ring) used at start and by the key endpoint.</summary>
    public KeyMaterialResult ValidateStructure()
    {
        if (string.IsNullOrWhiteSpace(Audience))
        {
            return KeyMaterialResult.Fail($"{SectionName}:{nameof(Audience)} is required while the mode is not Off; an empty audience verifies on both ends and separates nothing.");
        }

        if (MaxAgeSeconds <= 0 || MaxSignedBodyBytes <= 0)
        {
            return KeyMaterialResult.Fail($"{SectionName}:{nameof(MaxAgeSeconds)} and {nameof(MaxSignedBodyBytes)} must be positive.");
        }

        return KeyMaterialValidator.ValidateRing(this, SectionName);
    }
}

/// <summary>Response signing (<c>Codelab:Security:ResponseSigning</c>). Requires request signing to be on.</summary>
public sealed class ResponseSigningOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.ResponseSigning;

    /// <summary>Off (default), Report or Enforce. Any mode other than Off signs responses to verified requests.</summary>
    public SecurityEnforcementMode Mode { get; set; } = SecurityEnforcementMode.Off;

    /// <summary>A response body at least this large is logged (it was buffered in full to be hashed).</summary>
    public int LargeBodyWarningBytes { get; set; } = 1_048_576;
}
