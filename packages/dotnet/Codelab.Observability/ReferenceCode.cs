namespace Codelab.Observability;

/// <summary>
/// The short code a user reads out to the helpdesk: the first 10 hex characters of the correlation id, uppercase,
/// grouped five and five (<c>9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b</c> becomes <c>9C1E5-B7A20</c>). 40 bits find one
/// request in the retained log and are short enough to read over the phone; the full id stays on the response and
/// the log line. This is the single server-side owner of the rule; the SPA derives the same code.
/// </summary>
public static class ReferenceCode
{
    /// <summary>The length of a server-minted correlation id (a W3C trace id).</summary>
    public const int CorrelationIdLength = 32;

    /// <summary>The length of each of the two groups.</summary>
    public const int GroupLength = 5;

    /// <summary>The separator between the two groups.</summary>
    public const char GroupSeparator = '-';

    /// <summary>
    /// The reference code for <paramref name="correlationId"/>. Anything that is not exactly 32 hex characters is
    /// returned UNCHANGED (the same instance), so an id this host did not mint is still quotable as itself.
    /// </summary>
    public static string From(string correlationId)
    {
        ArgumentNullException.ThrowIfNull(correlationId);

        if (correlationId.Length != CorrelationIdLength || !correlationId.All(char.IsAsciiHexDigit))
        {
            return correlationId;
        }

        return string.Concat(
            correlationId.AsSpan(0, GroupLength).ToString().ToUpperInvariant(),
            GroupSeparator.ToString(),
            correlationId.AsSpan(GroupLength, GroupLength).ToString().ToUpperInvariant());
    }
}
