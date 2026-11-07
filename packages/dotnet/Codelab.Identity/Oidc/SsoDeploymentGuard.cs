using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Identity.Oidc;

/// <summary>
/// The deployment rules for OIDC single sign-on, as one pure function: the problems that keep a host from completing a
/// sign-in (empty when it may start). Run at start through <c>ValidateOnStart</c>.
/// <para>
/// Rules while <see cref="OidcOptions.Enabled"/>: <c>Authority</c>, <c>RedirectUri</c>, <c>AuthorizationEndpoint</c>,
/// <c>TokenEndpoint</c> and <c>EndSessionEndpoint</c> are absolute http(s) URIs (and <c>PostLogoutRedirectUri</c> when
/// set; <c>TokenEndpoint</c> only with the direct back channel, the gateway holds it in relay mode); outside Development
/// every one of them is https; <c>BackChannel</c> is defined, and is <c>GatewayRelay</c> outside Development when the
/// deployment declares <c>NoEgress</c>; <c>ClientId</c> is set; the scopes include <c>openid</c>; signing keys exist
/// (<c>JwksUri</c> in Jwks mode with the direct back channel, at least one parseable pinned key in Pinned mode); every allowed
/// algorithm is asymmetric (<c>none</c> and HS* refused); clock skew and back-channel timeout are in range; the enums and
/// claim names are defined. A message names keys, never values: the client secret lives in this section.
/// </para>
/// </summary>
public static class SsoDeploymentGuard
{
    /// <summary>The largest allowed clock skew, in seconds.</summary>
    public const int MaxClockSkewSeconds = 300;

    /// <summary>The longest allowed back-channel timeout, in seconds.</summary>
    public const int MaxBackchannelTimeoutSeconds = 120;

    /// <summary>The problems that keep this deployment from starting; empty when it may.</summary>
    /// <param name="options">The bound section (an absent section binds to defaults, which are disabled).</param>
    /// <param name="isDevelopment">Whether the host runs in Development (plain http endpoints allowed there only).</param>
    public static IReadOnlyList<string> Check(OidcOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return [];
        }

        var problems = new List<string>();
        CheckBackChannel(problems, options, isDevelopment);
        CheckUris(problems, options, isDevelopment);
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            problems.Add(Key(nameof(OidcOptions.ClientId), "must be set"));
        }

        if (!options.EffectiveScopes.Contains(OidcProtocol.OpenIdScope, StringComparer.Ordinal))
        {
            problems.Add(Key(nameof(OidcOptions.Scopes), $"must include {OidcProtocol.OpenIdScope}"));
        }

        CheckSigningKeys(problems, options, isDevelopment);
        CheckAlgorithms(problems, options);
        CheckRanges(problems, options);
        CheckClaims(problems, options.Claims);
        return problems;
    }

    private static void CheckBackChannel(List<string> problems, OidcOptions options, bool isDevelopment)
    {
        if (!Enum.IsDefined(options.BackChannel))
        {
            problems.Add(Key(nameof(OidcOptions.BackChannel), $"must be {(int)OidcBackChannel.Direct} ({OidcBackChannel.Direct}) or {(int)OidcBackChannel.GatewayRelay} ({OidcBackChannel.GatewayRelay})"));
        }
        else if (options.NoEgress && options.BackChannel == OidcBackChannel.Direct && !isDevelopment)
        {
            problems.Add(Key(
                nameof(OidcOptions.BackChannel),
                $"must be {(int)OidcBackChannel.GatewayRelay} ({OidcBackChannel.GatewayRelay}) outside Development: {nameof(OidcOptions.NoEgress)} declares that this backend cannot reach the provider"));
        }
    }

    private static bool UsesDirectBackChannel(OidcOptions options) => options.BackChannel != OidcBackChannel.GatewayRelay;

    private static void CheckUris(List<string> problems, OidcOptions options, bool isDevelopment)
    {
        CheckUri(problems, nameof(OidcOptions.Authority), options.Authority, isDevelopment);
        CheckUri(problems, nameof(OidcOptions.RedirectUri), options.RedirectUri, isDevelopment);
        CheckUri(problems, nameof(OidcOptions.AuthorizationEndpoint), options.AuthorizationEndpoint, isDevelopment);
        if (UsesDirectBackChannel(options))
        {
            CheckUri(problems, nameof(OidcOptions.TokenEndpoint), options.TokenEndpoint, isDevelopment);
        }

        CheckUri(problems, nameof(OidcOptions.EndSessionEndpoint), options.EndSessionEndpoint, isDevelopment);
        if (!string.IsNullOrWhiteSpace(options.PostLogoutRedirectUri))
        {
            CheckUri(problems, nameof(OidcOptions.PostLogoutRedirectUri), options.PostLogoutRedirectUri, isDevelopment);
        }
    }

    private static void CheckUri(List<string> problems, string key, string? value, bool isDevelopment)
    {
        var isHttp = Uri.TryCreate(value, UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        if (!isHttp)
        {
            problems.Add(Key(key, "must be an absolute http(s) URI"));
        }
        else if (!isDevelopment && uri!.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add(Key(key, "must use https outside Development"));
        }
    }

    private static void CheckSigningKeys(List<string> problems, OidcOptions options, bool isDevelopment)
    {
        switch (options.SigningKeyMode)
        {
            case SigningKeyMode.Jwks when UsesDirectBackChannel(options):
                CheckUri(problems, nameof(OidcOptions.JwksUri), options.JwksUri, isDevelopment);
                break;
            case SigningKeyMode.Jwks:
                break;
            case SigningKeyMode.Pinned:
                CheckPinnedKeys(problems, options.PinnedKeys);
                break;
            default:
                problems.Add(Key(nameof(OidcOptions.SigningKeyMode), $"must be one of {string.Join(", ", Enum.GetNames<SigningKeyMode>())}"));
                break;
        }
    }

    private static void CheckPinnedKeys(List<string> problems, IList<OidcPinnedKey> keys)
    {
        if (keys.Count == 0)
        {
            problems.Add(Key(nameof(OidcOptions.PinnedKeys), "must hold at least one public key in Pinned mode"));
        }

        for (var index = 0; index < keys.Count; index++)
        {
            if (!PinnedSigningKeySource.TryRead(keys[index], out _, out var problem))
            {
                problems.Add(Key(string.Create(CultureInfo.InvariantCulture, $"{nameof(OidcOptions.PinnedKeys)}:{index}"), $"is unusable: {problem}"));
            }
        }
    }

    private static void CheckAlgorithms(List<string> problems, OidcOptions options)
    {
        var refused = options.EffectiveAllowedAlgorithms.Where(algorithm => !OidcAlgorithms.IsAsymmetric(algorithm)).ToArray();
        if (refused.Length > 0)
        {
            problems.Add(Key(nameof(OidcOptions.AllowedAlgorithms), $"may hold only asymmetric algorithms ({string.Join(", ", OidcAlgorithms.Asymmetric)}); none and HMAC are refused"));
        }
    }

    private static void CheckRanges(List<string> problems, OidcOptions options)
    {
        if (options.ClockSkewSeconds is < 0 or > MaxClockSkewSeconds)
        {
            problems.Add(Key(nameof(OidcOptions.ClockSkewSeconds), $"must be 0 to {MaxClockSkewSeconds}"));
        }

        if (options.BackchannelTimeoutSeconds is < 1 or > MaxBackchannelTimeoutSeconds)
        {
            problems.Add(Key(nameof(OidcOptions.BackchannelTimeoutSeconds), $"must be 1 to {MaxBackchannelTimeoutSeconds}"));
        }

        if (!Enum.IsDefined(options.MatchBy))
        {
            problems.Add(Key(nameof(OidcOptions.MatchBy), $"must be one of {string.Join(", ", Enum.GetNames<LoginMatchField>())}"));
        }
    }

    private static void CheckClaims(List<string> problems, OidcClaimMap? claims)
    {
        if (claims is null)
        {
            problems.Add(Key(nameof(OidcOptions.Claims), "must be set"));
            return;
        }

        foreach (var (name, value) in ClaimNames(claims))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add(Key($"{nameof(OidcOptions.Claims)}:{name}", "must name a claim"));
            }
        }
    }

    private static IEnumerable<(string Name, string Value)> ClaimNames(OidcClaimMap claims) =>
    [
        (nameof(OidcClaimMap.Email), claims.Email),
        (nameof(OidcClaimMap.EmailVerified), claims.EmailVerified),
        (nameof(OidcClaimMap.LoginName), claims.LoginName),
        (nameof(OidcClaimMap.Upn), claims.Upn),
        (nameof(OidcClaimMap.DisplayName), claims.DisplayName),
    ];

    private static string Key(string key, string rule) => $"{OidcOptions.SectionName}:{key} {rule}";
}

/// <summary>Runs <see cref="SsoDeploymentGuard"/> when the options are first read (at start, through ValidateOnStart).</summary>
internal sealed class OidcOptionsValidator(IHostEnvironment environment) : IValidateOptions<OidcOptions>
{
    public ValidateOptionsResult Validate(string? name, OidcOptions options)
    {
        var problems = SsoDeploymentGuard.Check(options, environment.IsDevelopment());
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
