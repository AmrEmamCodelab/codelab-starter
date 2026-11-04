using System.Globalization;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Headers;

/// <summary>API security headers (<c>Codelab:Security:Headers</c>).</summary>
public sealed class SecurityHeaderOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.Headers;

    /// <summary>Write the headers (default on).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>HSTS.</summary>
    public HstsOptions Hsts { get; set; } = new();

    /// <summary>Path prefixes that get the baseline but not the strict JSON CSP (tool UIs that render HTML).</summary>
    public IList<string> RelaxedPathPrefixes { get; init; } = ["/hangfire", "/swagger", "/scalar", "/openapi"];
}

/// <summary>Strict-Transport-Security, sent only over HTTPS and never to an excluded host.</summary>
public sealed class HstsOptions
{
    /// <summary>The minimum max-age the browser preload list accepts.</summary>
    public const int PreloadMinimumDays = 365;

    /// <summary>Send HSTS (default on).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>max-age in days.</summary>
    public int MaxAgeDays { get; set; } = PreloadMinimumDays;

    /// <summary>includeSubDomains (default on).</summary>
    public bool IncludeSubDomains { get; set; } = true;

    /// <summary>preload (default off: it is hard to undo).</summary>
    public bool Preload { get; set; }

    /// <summary>Hosts that never receive HSTS (local development).</summary>
    public IList<string> ExcludedHosts { get; init; } = ["localhost", "127.0.0.1", "[::1]"];

    /// <summary>The header value.</summary>
    public string ToHeaderValue()
    {
        var value = "max-age=" + ((long)TimeSpan.FromDays(MaxAgeDays).TotalSeconds).ToString(CultureInfo.InvariantCulture);
        if (IncludeSubDomains)
        {
            value += "; includeSubDomains";
        }

        return Preload ? value + "; preload" : value;
    }

    /// <summary>Why the policy is incoherent (preload requirements), or null.</summary>
    public string? Incoherence()
    {
        if (!Preload)
        {
            return null;
        }

        if (!IncludeSubDomains)
        {
            return $"{SecurityHeaderOptions.SectionName}:Hsts:Preload requires IncludeSubDomains.";
        }

        return MaxAgeDays < PreloadMinimumDays
            ? $"{SecurityHeaderOptions.SectionName}:Hsts:Preload requires MaxAgeDays of at least {PreloadMinimumDays}."
            : null;
    }
}

/// <summary>Validates the HSTS policy at start.</summary>
internal sealed class SecurityHeaderOptionsValidator : IValidateOptions<SecurityHeaderOptions>
{
    public ValidateOptionsResult Validate(string? name, SecurityHeaderOptions options) =>
        options.Hsts.Incoherence() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
