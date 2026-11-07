namespace Codelab.Gateway.Api.Relay;

/// <summary>
/// Reads a body into memory up to a ceiling, for both directions: the caller's relay request and the upstream's
/// answer. A body above the ceiling is never buffered whole: reading stops one byte past it.
/// </summary>
internal static class BoundedBody
{
    private const int ChunkBytes = 8 * 1024;

    /// <summary>The whole stream, or null when it holds more than <paramref name="maxBytes"/> bytes.</summary>
    public static async Task<byte[]?> ReadAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkBytes];
        while (true)
        {
            var room = maxBytes + 1 - (int)buffer.Length;
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, room)), cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
            {
                return null;
            }
        }
    }

    /// <summary>The whole content, or null when it is longer than <paramref name="maxBytes"/> (declared or read).</summary>
    public static async Task<byte[]?> ReadAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        return await ReadAsync(stream, maxBytes, cancellationToken);
    }
}
