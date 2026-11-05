using Microsoft.Extensions.Options;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Import;

namespace Starter.Modules.Users.Infrastructure.Options;

/// <summary>
/// Activation links (section <c>Users:Activation</c>). Optional at start so a host without the web app (a job host, a
/// test host) still boots; while it is missing, creating a password-bound account or committing one answers
/// <see cref="UsersErrorCodes.ActivationLinkUnconfigured"/> and nothing is created. Every deployment kit sets it.
/// </summary>
public sealed class ActivationOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Users:Activation";

    /// <summary>
    /// The web app's absolute http(s) origin and base path (for example <c>http://localhost:4280</c>); the link is
    /// <c>{LinkBaseUrl}/activate#ticket=...</c>. No query and no fragment.
    /// </summary>
    public string? LinkBaseUrl { get; set; }

    /// <summary>The Application view of these settings (call after validation).</summary>
    public ActivationLinkSettings ToSettings() =>
        new(string.IsNullOrWhiteSpace(LinkBaseUrl) ? null : new Uri(LinkBaseUrl, UriKind.Absolute));
}

/// <summary>The import limits an administrator may tune (section <c>Users:Import</c>), each within <see cref="ImportLimits"/>.</summary>
public sealed class ImportOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Users:Import";

    /// <summary>The most data rows a sheet may have (1 to <see cref="ImportLimits.MaxRowsCeiling"/>).</summary>
    public int MaxRows { get; set; } = ImportLimits.DefaultMaxRows;

    /// <summary>How long a previewed batch may be committed, in minutes (1 to <see cref="ImportLimits.MaxBatchLifetimeMinutes"/>).</summary>
    public int BatchLifetimeMinutes { get; set; } = ImportLimits.DefaultBatchLifetimeMinutes;

    /// <summary>The Application view of these settings.</summary>
    public ImportSettings ToSettings() => new(MaxRows, TimeSpan.FromMinutes(BatchLifetimeMinutes));
}

/// <summary>Start-up validation of <see cref="ActivationOptions"/>: when set, an absolute http(s) URL without query or fragment.</summary>
public sealed class ActivationOptionsValidator : IValidateOptions<ActivationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ActivationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.LinkBaseUrl))
        {
            return ValidateOptionsResult.Success;
        }

        return Uri.TryCreate(options.LinkBaseUrl, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
               && uri.Query.Length == 0
               && uri.Fragment.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{ActivationOptions.SectionName}:{nameof(ActivationOptions.LinkBaseUrl)} must be an absolute http or https URL without query or fragment (the web app's origin).");
    }
}

/// <summary>Start-up validation of <see cref="ImportOptions"/>: both values within their ceilings.</summary>
public sealed class ImportOptionsValidator : IValidateOptions<ImportOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (options.MaxRows < 1 || options.MaxRows > ImportLimits.MaxRowsCeiling)
        {
            problems.Add($"{ImportOptions.SectionName}:{nameof(ImportOptions.MaxRows)} must be between 1 and {ImportLimits.MaxRowsCeiling}.");
        }

        if (options.BatchLifetimeMinutes < 1 || options.BatchLifetimeMinutes > ImportLimits.MaxBatchLifetimeMinutes)
        {
            problems.Add($"{ImportOptions.SectionName}:{nameof(ImportOptions.BatchLifetimeMinutes)} must be between 1 and {ImportLimits.MaxBatchLifetimeMinutes}.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
