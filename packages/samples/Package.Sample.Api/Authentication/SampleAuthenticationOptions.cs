using Microsoft.Extensions.Options;

namespace Package.Sample.Api.Authentication;

/// <summary>
/// The sample's bearer sessions (section <c>Sample:Authentication</c>). It stands in for the starter's session issuer
/// (phase 2): HMAC-signed tokens carrying <c>sub</c>, <c>sid</c>, <c>skid</c> and <c>sver</c>. The signing key comes
/// from the environment or a secret store, never from a committed file.
/// </summary>
public sealed class SampleAuthenticationOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Sample:Authentication";

    /// <summary>The shortest token signing key accepted, in bytes.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>The session lifetime when none is configured, in minutes.</summary>
    public const int DefaultSessionLifetimeMinutes = 30;

    /// <summary>The token issuer.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The token audience.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Base64 of the HMAC-SHA256 token signing key (at least <see cref="MinimumKeyBytes"/> bytes).</summary>
    public string SigningKeyBase64 { get; set; } = string.Empty;

    /// <summary>How long a session lives, in minutes; a revoked session id is remembered this long.</summary>
    public int SessionLifetimeMinutes { get; set; } = DefaultSessionLifetimeMinutes;

    /// <summary><see cref="SessionLifetimeMinutes"/> as a duration.</summary>
    public TimeSpan SessionLifetime => TimeSpan.FromMinutes(SessionLifetimeMinutes);

    /// <summary>The decoded signing key, or null when it is not valid base64.</summary>
    public byte[]? SigningKeyBytes()
    {
        var buffer = new byte[SigningKeyBase64.Length];
        return Convert.TryFromBase64String(SigningKeyBase64, buffer, out var written) ? buffer[..written] : null;
    }
}

/// <summary>Start-up validation of <see cref="SampleAuthenticationOptions"/>: every problem in one message.</summary>
public sealed class SampleAuthenticationOptionsValidator : IValidateOptions<SampleAuthenticationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SampleAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        Require(problems, options.Issuer, nameof(options.Issuer));
        Require(problems, options.Audience, nameof(options.Audience));
        if ((options.SigningKeyBytes()?.Length ?? 0) < SampleAuthenticationOptions.MinimumKeyBytes)
        {
            problems.Add($"{SampleAuthenticationOptions.SectionName}:{nameof(options.SigningKeyBase64)} must be base64 of at least {SampleAuthenticationOptions.MinimumKeyBytes} random bytes.");
        }

        if (options.SessionLifetimeMinutes <= 0)
        {
            problems.Add($"{SampleAuthenticationOptions.SectionName}:{nameof(options.SessionLifetimeMinutes)} must be positive.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private static void Require(List<string> problems, string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{SampleAuthenticationOptions.SectionName}:{key} is required.");
        }
    }
}
