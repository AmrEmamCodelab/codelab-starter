using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Codelab.Security.Cors;

/// <summary>
/// CORS for the SPA (<c>Codelab:Security:Cors</c>). Empty <see cref="AllowedOrigins"/> means same-origin only (no
/// policy is granted). The registered policy always exposes the response signature header and allows the request
/// proof header, so signing works cross-origin.
/// </summary>
public sealed class CodelabCorsOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.Cors;

    /// <summary>The policy name registered by AddCodelabSecurity (also the default policy).</summary>
    public const string PolicyName = "codelab";

    /// <summary>Exact origins (scheme://host[:port]); "*" is refused with credentials and outside Development.</summary>
    public IList<string> AllowedOrigins { get; init; } = [];

    /// <summary>Allow cookies and the Authorization header cross-origin (default on).</summary>
    public bool AllowCredentials { get; set; } = true;

    /// <summary>Extra request headers allowed beyond Authorization, Content-Type and the proof header.</summary>
    public IList<string> AllowedHeaders { get; init; } = [];

    /// <summary>Extra response headers exposed beyond the response signature header.</summary>
    public IList<string> ExposedHeaders { get; init; } = [];

    /// <summary>Preflight cache duration in seconds.</summary>
    public int PreflightMaxAgeSeconds { get; set; } = 600;
}

/// <summary>
/// The CORS validator: refuses a wildcard origin together with credentials (browsers refuse it, and a reflecting
/// implementation would grant every site the user's session), any wildcard outside Development, a non-HTTPS origin
/// outside Development, and anything that is not a bare origin.
/// </summary>
public static class CorsOriginValidator
{
    /// <summary>The wildcard origin.</summary>
    public const string Wildcard = "*";

    /// <summary>Every problem with <paramref name="options"/> (empty when valid).</summary>
    public static IReadOnlyList<string> Validate(CodelabCorsOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        foreach (var origin in options.AllowedOrigins)
        {
            if (Problem(origin, options.AllowCredentials, isDevelopment) is { } problem)
            {
                problems.Add($"{CodelabCorsOptions.SectionName}:AllowedOrigins '{origin}': {problem}");
            }
        }

        return problems;
    }

    private static string? Problem(string origin, bool allowCredentials, bool isDevelopment)
    {
        if (origin == Wildcard)
        {
            return allowCredentials ? "a wildcard origin cannot be combined with credentials." : isDevelopment ? null : "a wildcard origin is allowed only in Development.";
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != origin.TrimEnd('/') || origin.EndsWith('/'))
        {
            return "an origin is scheme://host[:port] with no path, query or trailing slash.";
        }

        return uri.Scheme == Uri.UriSchemeHttps || isDevelopment ? null : "only https origins are allowed outside Development.";
    }

    /// <summary>Configures the named policy from the options (only when origins are configured).</summary>
    internal static void Configure(CorsOptions cors, CodelabCorsOptions options, SecurityProtocol protocol)
    {
        if (options.AllowedOrigins.Count == 0)
        {
            return;
        }

        var policy = new CorsPolicyBuilder()
            .WithOrigins([.. options.AllowedOrigins])
            .WithMethods([.. Canonical.CanonicalText.Methods])
            .WithHeaders([HeaderNames.Authorization, HeaderNames.ContentType, protocol.SignatureHeaderName, .. options.AllowedHeaders])
            .WithExposedHeaders([protocol.ResponseSignatureHeaderName, .. options.ExposedHeaders])
            .SetPreflightMaxAge(TimeSpan.FromSeconds(options.PreflightMaxAgeSeconds));
        policy = options.AllowCredentials ? policy.AllowCredentials() : policy.DisallowCredentials();
        var built = policy.Build();
        cors.AddPolicy(CodelabCorsOptions.PolicyName, built);
        cors.AddDefaultPolicy(built);
    }
}

/// <summary>Runs <see cref="CorsOriginValidator"/> at start.</summary>
internal sealed class CodelabCorsOptionsValidator(IHostEnvironment environment) : IValidateOptions<CodelabCorsOptions>
{
    public ValidateOptionsResult Validate(string? name, CodelabCorsOptions options)
    {
        var problems = CorsOriginValidator.Validate(options, environment.IsDevelopment());
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
