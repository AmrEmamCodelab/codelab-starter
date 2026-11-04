using Codelab.Integration.Certificates;
using Microsoft.Extensions.Options;

namespace Codelab.Integration.Outbound;

/// <summary>
/// Validates every named <see cref="OutboundClientOptions"/> at start-up (ValidateOnStart) and on every read, so a
/// misconfigured client fails the boot instead of its first call.
/// </summary>
public sealed class OutboundClientOptionsValidator : IValidateOptions<OutboundClientOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OutboundClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        ValidateAddress(options, failures);
        ValidateRetry(options.Retry, failures);
        ValidateTimeouts(options.Timeout, failures);
        OutboundCertificateOptionsValidation.Validate(options.Certificate, failures);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures.Select(failure => $"Outbound client '{name}': {failure}"));
    }

    private static void ValidateAddress(OutboundClientOptions options, List<string> failures)
    {
        if (options.BaseAddress is null)
        {
            failures.Add("BaseAddress is required.");
            return;
        }

        if (!options.BaseAddress.IsAbsoluteUri)
        {
            failures.Add("BaseAddress must be an absolute URI.");
            return;
        }

        var isHttps = options.BaseAddress.Scheme == Uri.UriSchemeHttps;
        var isAllowedHttp = options.AllowPlainHttp && options.BaseAddress.Scheme == Uri.UriSchemeHttp;
        if (!isHttps && !isAllowedHttp)
        {
            failures.Add("BaseAddress must use https (set AllowPlainHttp only for a reviewed plain-text upstream).");
        }
    }

    private static void ValidateRetry(OutboundRetryOptions retry, List<string> failures)
    {
        if (retry.MaxRetries < 0 || retry.MaxRetries > OutboundDefaults.MaxRetriesCeiling)
        {
            failures.Add($"Retry.MaxRetries must be between 0 and {OutboundDefaults.MaxRetriesCeiling}.");
        }

        if (retry.BaseDelay < TimeSpan.Zero || retry.MaxDelay < retry.BaseDelay)
        {
            failures.Add("Retry.BaseDelay must not be negative and Retry.MaxDelay must not be shorter than it.");
        }
    }

    private static void ValidateTimeouts(OutboundTimeoutOptions timeout, List<string> failures)
    {
        if (timeout.AttemptTimeout < OutboundDefaults.MinimumTimeout)
        {
            failures.Add($"Timeout.AttemptTimeout must be at least {OutboundDefaults.MinimumTimeout}.");
        }

        if (timeout.TotalTimeout < timeout.AttemptTimeout)
        {
            failures.Add("Timeout.TotalTimeout must not be shorter than Timeout.AttemptTimeout.");
        }
    }
}
