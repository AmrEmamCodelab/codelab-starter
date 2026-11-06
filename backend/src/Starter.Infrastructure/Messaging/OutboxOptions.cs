using Microsoft.Extensions.Options;

namespace Starter.Infrastructure.Messaging;

/// <summary>The outbox drain switches (section <c>Messaging:Outbox</c>), validated on start.</summary>
public sealed class OutboxOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Messaging:Outbox";

    /// <summary>The default number of messages one claim takes.</summary>
    public const int DefaultBatchSize = 50;

    /// <summary>The largest allowed <see cref="BatchSize"/>.</summary>
    public const int MaxBatchSize = 500;

    /// <summary>
    /// Deliver outbox messages. On by default; when false a drain pass does nothing and messages stay Pending (they
    /// are still written), so switching it back on delivers the backlog.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Messages per claim, 1 to <see cref="MaxBatchSize"/>; a pass claims up to <see cref="OutboxDrainSchedule.MaxBatchesPerPass"/> batches.</summary>
    public int BatchSize { get; set; } = DefaultBatchSize;
}

/// <summary>Start-up validation of <see cref="OutboxOptions"/>.</summary>
public sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.BatchSize is < 1 or > OutboxOptions.MaxBatchSize
            ? ValidateOptionsResult.Fail($"{OutboxOptions.SectionName}:{nameof(options.BatchSize)} must be between 1 and {OutboxOptions.MaxBatchSize}; it is {options.BatchSize}.")
            : ValidateOptionsResult.Success;
    }
}
