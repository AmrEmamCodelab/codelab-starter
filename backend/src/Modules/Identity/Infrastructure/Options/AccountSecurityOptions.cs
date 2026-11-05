using Microsoft.Extensions.Options;
using Starter.Modules.Identity.Application.Activation;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Options;

/// <summary>
/// The basic-login lockout (section <c>Identity:Lockout</c>). On by default: a missing section must not leave
/// passwords open to unlimited guessing.
/// </summary>
public sealed class LockoutOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Identity:Lockout";

    /// <summary>The default number of consecutive wrong passwords that lock an account.</summary>
    public const int DefaultMaxFailedAttempts = 5;

    /// <summary>The default lockout length, in minutes.</summary>
    public const int DefaultLockoutMinutes = 15;

    /// <summary>The largest accepted <see cref="MaxFailedAttempts"/> (a higher value is no lockout at all).</summary>
    public const int MaxFailedAttemptsCeiling = 100;

    /// <summary>The longest accepted lockout, in minutes (one day).</summary>
    public const int LockoutMinutesCeiling = 1440;

    /// <summary>Consecutive wrong passwords that lock the account (1 to <see cref="MaxFailedAttemptsCeiling"/>).</summary>
    public int MaxFailedAttempts { get; set; } = DefaultMaxFailedAttempts;

    /// <summary>How long a lockout lasts, in minutes (1 to <see cref="LockoutMinutesCeiling"/>).</summary>
    public int LockoutMinutes { get; set; } = DefaultLockoutMinutes;

    /// <summary>The Domain view of these settings.</summary>
    public LockoutPolicy ToPolicy() => new(MaxFailedAttempts: MaxFailedAttempts, Duration: TimeSpan.FromMinutes(LockoutMinutes));
}

/// <summary>The password rule an activation enforces (section <c>Identity:Password</c>).</summary>
public sealed class PasswordPolicyOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Identity:Password";

    /// <summary>The default shortest password, in characters.</summary>
    public const int DefaultMinLength = 12;

    /// <summary>The default fewest distinct characters.</summary>
    public const int DefaultMinDistinctCharacters = 5;

    /// <summary>The shortest password (at least <see cref="IdentityLimits.PasswordMinLengthFloor"/>, at most <see cref="IdentityLimits.PasswordMaxLength"/>).</summary>
    public int MinLength { get; set; } = DefaultMinLength;

    /// <summary>The fewest distinct characters (1 to <see cref="MinLength"/>).</summary>
    public int MinDistinctCharacters { get; set; } = DefaultMinDistinctCharacters;

    /// <summary>The Application view of these settings.</summary>
    public PasswordPolicy ToPolicy() => new(MinLength: MinLength, MinDistinctCharacters: MinDistinctCharacters);
}

/// <summary>The activation links (section <c>Identity:Activation</c>).</summary>
public sealed class ActivationOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Identity:Activation";

    /// <summary>The default link lifetime, in hours (three days).</summary>
    public const int DefaultTicketLifetimeHours = 72;

    /// <summary>
    /// The longest link lifetime, in hours (seven days, the Codelab.Security ticket default maximum; a longer value
    /// also needs <c>Codelab:Security:Tickets:MaxLifetimeSeconds</c> raised, which this module does not do).
    /// </summary>
    public const int TicketLifetimeHoursCeiling = 168;

    /// <summary>How long an activation link works, in hours (1 to <see cref="TicketLifetimeHoursCeiling"/>).</summary>
    public int TicketLifetimeHours { get; set; } = DefaultTicketLifetimeHours;

    /// <summary>The link lifetime.</summary>
    public TimeSpan TicketLifetime => TimeSpan.FromHours(TicketLifetimeHours);
}

/// <summary>Start-up validation of <see cref="LockoutOptions"/>: both values positive and within their ceilings.</summary>
public sealed class LockoutOptionsValidator : IValidateOptions<LockoutOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, LockoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.MaxFailedAttempts is >= 1 and <= LockoutOptions.MaxFailedAttemptsCeiling
            && options.LockoutMinutes is >= 1 and <= LockoutOptions.LockoutMinutesCeiling
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{LockoutOptions.SectionName}: {nameof(options.MaxFailedAttempts)} must be 1 to {LockoutOptions.MaxFailedAttemptsCeiling} and {nameof(options.LockoutMinutes)} 1 to {LockoutOptions.LockoutMinutesCeiling}.");
    }
}

/// <summary>Start-up validation of <see cref="PasswordPolicyOptions"/>: a minimum length no lower than the floor.</summary>
public sealed class PasswordPolicyOptionsValidator : IValidateOptions<PasswordPolicyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PasswordPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MinLength is < IdentityLimits.PasswordMinLengthFloor or > IdentityLimits.PasswordMaxLength)
        {
            return ValidateOptionsResult.Fail(
                $"{PasswordPolicyOptions.SectionName}:{nameof(options.MinLength)} must be {IdentityLimits.PasswordMinLengthFloor} to {IdentityLimits.PasswordMaxLength}.");
        }

        return options.MinDistinctCharacters >= 1 && options.MinDistinctCharacters <= options.MinLength
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{PasswordPolicyOptions.SectionName}:{nameof(options.MinDistinctCharacters)} must be 1 to {nameof(options.MinLength)}.");
    }
}

/// <summary>Start-up validation of <see cref="ActivationOptions"/>: a lifetime of 1 to <see cref="ActivationOptions.TicketLifetimeHoursCeiling"/> hours.</summary>
public sealed class ActivationOptionsValidator : IValidateOptions<ActivationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ActivationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.TicketLifetimeHours is >= 1 and <= ActivationOptions.TicketLifetimeHoursCeiling
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{ActivationOptions.SectionName}:{nameof(options.TicketLifetimeHours)} must be 1 to {ActivationOptions.TicketLifetimeHoursCeiling}.");
    }
}
