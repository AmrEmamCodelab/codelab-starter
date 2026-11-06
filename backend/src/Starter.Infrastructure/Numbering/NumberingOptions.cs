using Microsoft.Extensions.Options;

namespace Starter.Infrastructure.Numbering;

/// <summary>Request numbering settings (section <c>Numbering</c>), validated on start.</summary>
public sealed class NumberingOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Numbering";

    /// <summary>The default time zone: Riyadh (UTC+3, no daylight saving), as a Windows id.</summary>
    public const string DefaultTimeZoneId = "Arab Standard Time";

    /// <summary>
    /// The time zone whose calendar year a number carries, so a request made at 00:30 on 1 January Riyadh time is
    /// numbered in the new year although it is still 31 December in UTC. A Windows or IANA id.
    /// </summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;
}

/// <summary>Start-up validation of <see cref="NumberingOptions"/>: the time zone must exist on this machine.</summary>
public sealed class NumberingOptionsValidator : IValidateOptions<NumberingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, NumberingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return !string.IsNullOrWhiteSpace(options.TimeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZoneId, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{NumberingOptions.SectionName}:{nameof(options.TimeZoneId)} '{options.TimeZoneId}' is not a time zone known to this machine.");
    }
}
