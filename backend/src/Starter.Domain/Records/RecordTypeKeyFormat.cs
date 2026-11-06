using Starter.Domain.Lookups;

namespace Starter.Domain.Records;

/// <summary>
/// The shape of a record type key (<c>{recordType}</c> in the record, queue and attachment routes, and the
/// <see cref="RecordRef.RecordType"/> every port takes): kebab-case ASCII, starting with a letter, at most
/// <see cref="MaxLength"/> characters. One owner for the rule; the kebab check itself is the kernel's
/// (<see cref="LookupLimits.IsKebab"/>), so record type keys and attachable record types share one shape.
/// </summary>
public static class RecordTypeKeyFormat
{
    /// <summary>The longest record type key (the record type columns of the Records and Attachments tables hold this many characters).</summary>
    public const int MaxLength = 64;

    /// <summary>True when <paramref name="recordType"/> is a well-formed record type key.</summary>
    public static bool IsValid(string? recordType) => LookupLimits.IsKebab(recordType, MaxLength);

    /// <summary>Refuses a malformed record type key with a message that states the rule.</summary>
    /// <param name="recordType">The key to check.</param>
    /// <param name="parameterName">The caller's parameter name, for the exception.</param>
    /// <exception cref="ArgumentException">The key is not kebab-case or is longer than <see cref="MaxLength"/>.</exception>
    public static string EnsureValid(string? recordType, string parameterName)
    {
        if (!IsValid(recordType))
        {
            throw new ArgumentException(
                $"A record type key is kebab-case ASCII starting with a letter, at most {MaxLength} characters.", parameterName);
        }

        return recordType!;
    }
}
