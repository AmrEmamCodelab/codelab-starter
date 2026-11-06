using System.Globalization;

namespace Starter.Application.Numbering;

/// <summary>
/// Allocates human-readable reference numbers <c>PREFIX-YYYY-NNN</c> (for example <c>CU-2026-001</c>): one counter per
/// prefix and year, starting at 1 each year, never reused.
/// </summary>
/// <remarks>
/// A number is taken when <see cref="NextAsync"/> returns, independently of the caller's transaction: a caller that
/// then fails its own save leaves a gap, never a duplicate. The year is the calendar year in the configured time zone
/// (<c>Numbering:TimeZoneId</c>).
/// </remarks>
public interface IRequestNumberAllocator
{
    /// <summary>The next number for <paramref name="prefix"/> in the current year.</summary>
    /// <param name="prefix">Two to six uppercase ASCII letters (<see cref="RequestNumberFormat.IsValidPrefix"/>).</param>
    /// <param name="cancellationToken">Cancels the allocation.</param>
    /// <exception cref="ArgumentException">The prefix is not valid.</exception>
    Task<string> NextAsync(string prefix, CancellationToken cancellationToken);
}

/// <summary>The <c>PREFIX-YYYY-NNN</c> format: one owner for the prefix rule and the padding.</summary>
public static class RequestNumberFormat
{
    /// <summary>The shortest prefix.</summary>
    public const int PrefixMinLength = 2;

    /// <summary>The longest prefix.</summary>
    public const int PrefixMaxLength = 6;

    /// <summary>The counter is zero-padded to at least this many digits and grows beyond it (1000 after 999).</summary>
    public const int MinimumCounterDigits = 3;

    /// <summary>The separator between prefix, year and counter.</summary>
    public const char Separator = '-';

    /// <summary>The first year a number can carry (the year is always four digits).</summary>
    public const int MinYear = 1000;

    /// <summary>The last year a number can carry.</summary>
    public const int MaxYear = 9999;

    private const string YearFormat = "D4";

    private static readonly string CounterFormat = "D" + MinimumCounterDigits.ToString(CultureInfo.InvariantCulture);

    /// <summary>True when <paramref name="prefix"/> is 2-6 uppercase ASCII letters.</summary>
    public static bool IsValidPrefix(string? prefix) =>
        prefix is { Length: >= PrefixMinLength and <= PrefixMaxLength } && prefix.All(char.IsAsciiLetterUpper);

    /// <summary>Refuses an invalid prefix with a message that states the rule.</summary>
    /// <exception cref="ArgumentException">The prefix is not 2-6 uppercase ASCII letters.</exception>
    public static void EnsureValidPrefix(string? prefix)
    {
        if (!IsValidPrefix(prefix))
        {
            throw new ArgumentException(
                $"A request number prefix is {PrefixMinLength}-{PrefixMaxLength} uppercase ASCII letters.", nameof(prefix));
        }
    }

    /// <summary>Formats <paramref name="counter"/> of <paramref name="prefix"/> in <paramref name="year"/>.</summary>
    /// <exception cref="ArgumentException">The prefix is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The year is not four digits or the counter is below 1.</exception>
    public static string Format(string prefix, int year, long counter)
    {
        EnsureValidPrefix(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(year, MinYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, MaxYear);
        ArgumentOutOfRangeException.ThrowIfLessThan(counter, 1);
        return string.Concat(
            prefix,
            Separator.ToString(),
            year.ToString(YearFormat, CultureInfo.InvariantCulture),
            Separator.ToString(),
            counter.ToString(CounterFormat, CultureInfo.InvariantCulture));
    }
}
