namespace Codelab.Integration.Gateway;

/// <summary>Reads a response body up to a byte limit, so an upstream cannot make the caller buffer without bound.</summary>
internal static class BoundedContentReader
{
    private const int ChunkBytes = 8192;

    /// <summary>The body bytes, or null when the body is larger than <paramref name="maxBytes"/>.</summary>
    internal static async Task<byte[]?> ReadAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkBytes];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
