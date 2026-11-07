using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using Starter.Modules.Ops.Domain.Certificates;

namespace Starter.Modules.Ops.Infrastructure.Options;

/// <summary>The bounds of <see cref="OpsOptions"/>; a value outside them stops the host at start, naming the key.</summary>
public sealed class OpsOptionsValidator : IValidateOptions<OpsOptions>
{
    /// <summary>The longest probe timeout in seconds.</summary>
    public const int MaxProbeTimeoutSeconds = 30;

    /// <summary>The widest search margin in minutes (one day).</summary>
    public const int MaxSearchMarginMinutes = 1440;

    /// <summary>The longest sighting retention in days.</summary>
    public const int MaxRetentionDays = 365;

    /// <summary>The longest component or certificate name.</summary>
    public const int MaxNameLength = 64;

    private const string Section = OpsOptions.SectionName;

    private const char Hyphen = '-';

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [.. RangeFailures(options), .. ProbeFailures(options), .. CertificateFailures(options)];
        if (!IsHttpUrl(options.IdentityProviderJwksUrl, allowEmpty: true))
        {
            failures.Add($"{Section}:{nameof(OpsOptions.IdentityProviderJwksUrl)} must be an absolute http(s) URL or empty.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>True when <paramref name="value"/> is lowercase letters, digits and single hyphens, starting with a letter.</summary>
    public static bool IsKebabName(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxNameLength || !char.IsAsciiLetterLower(value[0]) || value[^1] == Hyphen)
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == Hyphen)
            && !value.Contains($"{Hyphen}{Hyphen}", StringComparison.Ordinal);
    }

    private static IEnumerable<string> RangeFailures(OpsOptions options)
    {
        var thresholds = new CertificateThresholds(WarningDays: options.CertificateWarningDays, CriticalDays: options.CertificateCriticalDays);
        if (!thresholds.IsValid)
        {
            yield return $"{Section}: {nameof(OpsOptions.CertificateCriticalDays)} and {nameof(OpsOptions.CertificateWarningDays)} must satisfy 1 <= critical < warning <= {CertificateThresholds.MaxWarningDays}.";
        }

        if (options.ProbeTimeoutSeconds is < 1 or > MaxProbeTimeoutSeconds)
        {
            yield return $"{Section}:{nameof(OpsOptions.ProbeTimeoutSeconds)} must be 1 to {MaxProbeTimeoutSeconds}.";
        }

        if (options.SearchMarginMinutes is < 1 or > MaxSearchMarginMinutes)
        {
            yield return $"{Section}:{nameof(OpsOptions.SearchMarginMinutes)} must be 1 to {MaxSearchMarginMinutes}.";
        }

        if (options.ReferenceCodeRetentionDays is < 1 or > MaxRetentionDays)
        {
            yield return $"{Section}:{nameof(OpsOptions.ReferenceCodeRetentionDays)} must be 1 to {MaxRetentionDays}.";
        }
    }

    private static IEnumerable<string> ProbeFailures(OpsOptions options)
    {
        const string key = $"{Section}:{nameof(OpsOptions.ReadinessProbes)}";
        foreach (var probe in options.ReadinessProbes)
        {
            if (!IsKebabName(probe.Name) || !IsHttpUrl(probe.Url, allowEmpty: false))
            {
                yield return $"{key}: every entry needs a lowercase kebab Name and an absolute http(s) Url.";
            }
        }

        if (HasDuplicates(options.ReadinessProbes.Select(probe => probe.Name)))
        {
            yield return $"{key}: names must be unique.";
        }
    }

    private static IEnumerable<string> CertificateFailures(OpsOptions options)
    {
        const string key = $"{Section}:{nameof(OpsOptions.Certificates)}";
        foreach (var certificate in options.Certificates)
        {
            if (!IsKebabName(certificate.Name))
            {
                yield return $"{key}: every entry needs a lowercase kebab Name.";
            }

            if (string.IsNullOrWhiteSpace(certificate.Thumbprint) == string.IsNullOrWhiteSpace(certificate.Path))
            {
                yield return $"{key}: '{certificate.Name}' needs exactly one of Thumbprint and Path.";
            }

            if (!Enum.TryParse<StoreName>(certificate.StoreName, ignoreCase: true, out _)
                || !Enum.TryParse<StoreLocation>(certificate.StoreLocation, ignoreCase: true, out _))
            {
                yield return $"{key}: '{certificate.Name}' has an unknown StoreName or StoreLocation.";
            }
        }

        if (HasDuplicates(options.Certificates.Select(certificate => certificate.Name)))
        {
            yield return $"{key}: names must be unique.";
        }
    }

    private static bool HasDuplicates(IEnumerable<string?> names)
    {
        var list = names.ToList();
        return list.Distinct(StringComparer.Ordinal).Count() != list.Count;
    }

    private static bool IsHttpUrl(string? value, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return allowEmpty;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
