using Codelab.Gateway.Api.Relay;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Sso;

/// <summary>
/// The SSO relay profile's upstream (<see cref="SectionName"/>): the OIDC provider's token endpoint and JWKS document,
/// and how long the JWKS document is cached. Bound and validated at start only when the profile is enabled. The
/// gateway holds no client secret: the token request arrives complete from the backend and is passed through.
/// </summary>
internal sealed class SsoRelayOptions : UpstreamOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Gateway:Sso";

    /// <summary>The provider's token endpoint (absolute; https outside Development).</summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>The provider's JWKS document (absolute; https outside Development).</summary>
    public Uri? JwksUri { get; set; }

    /// <summary>
    /// How long a fetched JWKS document is served from memory. Zero fetches it on every request. At most
    /// <see cref="SsoRelayDefaults.MaxJwksCacheDuration"/>, so a rotated signing key reaches the backend within a day.
    /// </summary>
    public TimeSpan JwksCacheDuration { get; set; } = SsoRelayDefaults.JwksCacheDuration;
}

/// <summary>Defaults and bounds of <see cref="SsoRelayOptions"/>.</summary>
internal static class SsoRelayDefaults
{
    /// <summary>The default JWKS cache duration.</summary>
    public static readonly TimeSpan JwksCacheDuration = TimeSpan.FromMinutes(10);

    /// <summary>The longest JWKS cache duration.</summary>
    public static readonly TimeSpan MaxJwksCacheDuration = TimeSpan.FromHours(24);
}

/// <summary>Validates <see cref="SsoRelayOptions"/> at start (registered only when the SSO profile is enabled).</summary>
internal sealed class SsoRelayOptionsValidator(IHostEnvironment environment) : IValidateOptions<SsoRelayOptions>
{
    public ValidateOptionsResult Validate(string? name, SsoRelayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var isDevelopment = environment.IsDevelopment();
        var problems = new List<string?>
        {
            UpstreamAddress.Problem(options.TokenEndpoint, SectionName(nameof(SsoRelayOptions.TokenEndpoint)), isDevelopment),
            UpstreamAddress.Problem(options.JwksUri, SectionName(nameof(SsoRelayOptions.JwksUri)), isDevelopment),
        };
        if (options.JwksCacheDuration < TimeSpan.Zero || options.JwksCacheDuration > SsoRelayDefaults.MaxJwksCacheDuration)
        {
            problems.Add($"{SectionName(nameof(SsoRelayOptions.JwksCacheDuration))} must be between 00:00:00 and {SsoRelayDefaults.MaxJwksCacheDuration}.");
        }

        var failures = problems.OfType<string>().ToList();
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string SectionName(string setting) => SsoRelayOptions.SectionName + ":" + setting;
}
