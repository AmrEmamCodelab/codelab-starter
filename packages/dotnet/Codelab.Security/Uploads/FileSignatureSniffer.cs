namespace Codelab.Security.Uploads;

/// <summary>
/// Magic-byte checks: a file is accepted only when its (normalised) extension is allowed AND its leading bytes match
/// that extension's signature, so a renamed executable or a PDF named <c>.png</c> is refused. The decision is made on
/// the last extension segment of the normalised name (trailing dots and spaces removed). Streams are read from offset
/// zero when seekable and restored to their position; forward-only streams are read once.
/// </summary>
public sealed class FileSignatureSniffer
{
    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[][] ZipSignatures = [[0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06]];

    /// <summary>Every signature the sniffer knows, by extension (lower case, with the dot).</summary>
    public static readonly IReadOnlyDictionary<string, byte[][]> KnownSignatures = new Dictionary<string, byte[][]>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = [Pdf],
        [".png"] = [Png],
        [".jpg"] = [Jpeg],
        [".jpeg"] = [Jpeg],
        [".zip"] = ZipSignatures,
    };

    private readonly HashSet<string> _allowed;

    /// <summary>Creates a sniffer for <paramref name="allowedExtensions"/> (each must be one of <see cref="KnownSignatures"/>).</summary>
    /// <exception cref="ArgumentException">An extension has no known signature.</exception>
    public FileSignatureSniffer(IEnumerable<string> allowedExtensions)
    {
        ArgumentNullException.ThrowIfNull(allowedExtensions);
        _allowed = new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase);
        var unknown = _allowed.FirstOrDefault(extension => !KnownSignatures.ContainsKey(extension));
        if (unknown is not null)
        {
            throw new ArgumentException($"No magic-byte signature is known for '{unknown}'.", nameof(allowedExtensions));
        }
    }

    /// <summary>True when the normalised extension of <paramref name="fileName"/> is allowed.</summary>
    public bool IsAllowedExtension(string fileName) => _allowed.Contains(FileNames.NormalizedExtension(fileName));

    /// <summary>True when the extension is allowed and the content starts with its signature.</summary>
    public bool HasValidSignature(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = FileNames.NormalizedExtension(fileName);
        return _allowed.Contains(extension) && StartsWithAny(content, KnownSignatures[extension]);
    }

    /// <summary>True when the content starts with a zip local-file or empty-archive signature.</summary>
    public static bool IsZipContainer(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return StartsWithAny(content, ZipSignatures);
    }

    private static bool StartsWithAny(Stream content, byte[][] signatures)
    {
        var header = ReadHeader(content, signatures.Max(signature => signature.Length));
        return signatures.Any(signature => header.AsSpan().StartsWith(signature));
    }

    private static byte[] ReadHeader(Stream content, int length)
    {
        var original = content.CanSeek ? content.Position : (long?)null;
        if (original is not null)
        {
            content.Position = 0;
        }

        try
        {
            var header = new byte[length];
            var filled = 0;
            int read;
            while (filled < length && (read = content.Read(header, filled, length - filled)) > 0)
            {
                filled += read;
            }

            return header[..filled];
        }
        finally
        {
            if (original is { } position)
            {
                content.Position = position;
            }
        }
    }
}
