namespace Starter.Infrastructure.Security;

/// <summary>Reads a request body that must fit a ceiling, whether or not it declared its length (chunked bodies do not).</summary>
public static class BoundedBody
{
    /// <summary>
    /// The whole of <paramref name="body"/>, or null as soon as it proves longer than <paramref name="maxBytes"/>
    /// (at most <paramref name="maxBytes"/> + 1 bytes are read, so an oversized body is never buffered whole).
    /// </summary>
    public static async Task<ReadOnlyMemory<byte>?> ReadAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        var buffer = new byte[maxBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
        {
            length += read;
        }

        // Spelled out: a bare null here would convert to an empty Memory<byte> through byte[], not to a null result.
        if (length > maxBytes)
        {
            return null;
        }

        return new ReadOnlyMemory<byte>(buffer, 0, length);
    }
}
