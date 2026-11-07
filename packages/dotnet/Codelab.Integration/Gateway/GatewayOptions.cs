using Codelab.Integration.Certificates;
using Codelab.Integration.Outbound;
using Microsoft.Extensions.Options;

namespace Codelab.Integration.Gateway;

/// <summary>
/// The integration gateway section (<see cref="SectionName"/>), shared by both sides:
/// <list type="bullet">
/// <item>On the gateway: <see cref="Profiles"/>, the keys of the enabled relay profiles.</item>
/// <item>On a caller (the API): <see cref="BaseUrl"/>, <see cref="Audience"/> and the outbound policy
/// (<see cref="Retry"/>, <see cref="Timeout"/>, <see cref="Certificate"/>) of the <see cref="GatewayRelayClient"/>.</item>
/// </list>
/// </summary>
public sealed class GatewayOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Codelab:Integration:Gateway";

    /// <summary>The gateway's absolute base address (https outside Development). Caller side.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>The service-assertion audience of the gateway (<c>aud</c>). Caller side.</summary>
    public string Audience { get; set; } = GatewayDefaults.Audience;

    /// <summary>The keys of the enabled profiles (<see cref="GatewayProfileKey"/>). Gateway side; a caller may list them for documentation.</summary>
    public IList<string> Profiles { get; init; } = [];

    /// <summary>The relay client's retry policy. Relay POSTs are never retried (they are not idempotent).</summary>
    public OutboundRetryOptions Retry { get; set; } = new();

    /// <summary>The relay client's attempt and total timeouts.</summary>
    public OutboundTimeoutOptions Timeout { get; set; } = new();

    /// <summary>How the relay client trusts the gateway's certificate (validation is always on).</summary>
    public OutboundCertificateOptions Certificate { get; set; } = new();
}

/// <summary>Validates the profile list: well-formed keys, no duplicates. Registered by both sides.</summary>
internal sealed class GatewayOptionsValidator : IValidateOptions<GatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        foreach (var key in options.Profiles.Where(key => !GatewayProfileKey.IsValid(key)))
        {
            problems.Add($"{GatewayOptions.SectionName}:Profiles: '{key}' is not a valid profile key (lower-case letters, digits and hyphens, at most {GatewayProfileKey.MaxLength}).");
        }

        foreach (var key in options.Profiles.GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            problems.Add($"{GatewayOptions.SectionName}:Profiles lists '{key}' more than once.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>
/// Validates the caller side: an absolute <c>BaseUrl</c> without query or fragment, <c>https</c> outside
/// Development, and an audience.
/// </summary>
internal sealed class GatewayClientOptionsValidator(bool isDevelopment) : IValidateOptions<GatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (BaseUrlProblem(options.BaseUrl) is { } baseUrlProblem)
        {
            problems.Add(baseUrlProblem);
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            problems.Add($"{GatewayOptions.SectionName}:Audience is required.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private string? BaseUrlProblem(Uri? baseUrl)
    {
        const string setting = GatewayOptions.SectionName + ":BaseUrl";
        if (baseUrl is null || !baseUrl.IsAbsoluteUri)
        {
            return $"{setting} is required and must be an absolute URL.";
        }

        if (baseUrl.Query.Length > 0 || baseUrl.Fragment.Length > 0)
        {
            return $"{setting} must not carry a query or a fragment.";
        }

        if (baseUrl.Scheme == Uri.UriSchemeHttps || (isDevelopment && baseUrl.Scheme == Uri.UriSchemeHttp))
        {
            return null;
        }

        return isDevelopment
            ? $"{setting} must use http or https."
            : $"{setting} must use https outside Development (the S2S assertion and the relayed secrets need TLS).";
    }
}
