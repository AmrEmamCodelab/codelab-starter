using System.Globalization;
using Microsoft.Extensions.Options;
using Starter.Application.Numbering;

namespace Starter.Infrastructure.Numbering;

/// <summary>
/// The <see cref="IRequestNumberAllocator"/>: validates the prefix, takes the year from <see cref="TimeProvider"/> in
/// <see cref="NumberingOptions.TimeZoneId"/>, draws the next value of the prefix-and-year sequence and formats it with
/// <see cref="RequestNumberFormat"/>.
/// </summary>
public sealed class RequestNumberAllocator(
    INumberSequences sequences, TimeProvider timeProvider, IOptions<NumberingOptions> options) : IRequestNumberAllocator
{
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

    /// <inheritdoc />
    public async Task<string> NextAsync(string prefix, CancellationToken cancellationToken)
    {
        RequestNumberFormat.EnsureValidPrefix(prefix);
        var year = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), _timeZone).Year;
        var counter = await sequences.NextValueAsync(SequenceName(prefix, year), cancellationToken);
        return RequestNumberFormat.Format(prefix, year, counter);
    }

    /// <summary>The sequence of <paramref name="prefix"/> in <paramref name="year"/>: <c>PREFIX_YYYY</c>.</summary>
    /// <exception cref="ArgumentException">The prefix is not valid (so a name is always a safe SQL identifier).</exception>
    public static string SequenceName(string prefix, int year)
    {
        RequestNumberFormat.EnsureValidPrefix(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(year, RequestNumberFormat.MinYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, RequestNumberFormat.MaxYear);
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}_{year}");
    }
}
