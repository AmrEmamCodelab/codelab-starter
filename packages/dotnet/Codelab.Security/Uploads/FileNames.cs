namespace Codelab.Security.Uploads;

/// <summary>
/// File-name normalisation and path containment for uploads. Windows ignores trailing dots and spaces
/// (<c>evil.exe.</c> opens as <c>evil.exe</c>), so decisions are made on the normalised name; an alternate data stream
/// suffix (<c>a.pdf:x.exe</c>) is refused outright; only the leaf of a client-supplied path is ever kept.
/// </summary>
public static class FileNames
{
    private const char AlternateStreamSeparator = ':';

    /// <summary>The name with trailing whitespace and dots removed.</summary>
    public static string Normalize(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var end = fileName.Length;
        while (end > 0 && (char.IsWhiteSpace(fileName[end - 1]) || fileName[end - 1] == '.'))
        {
            end--;
        }

        return fileName[..end];
    }

    /// <summary>The extension of the normalised name (with the dot), or empty; empty for a name carrying a stream suffix.</summary>
    public static string NormalizedExtension(string fileName)
    {
        var normalized = Normalize(fileName);
        return normalized.Contains(AlternateStreamSeparator, StringComparison.Ordinal) ? string.Empty : Path.GetExtension(normalized);
    }

    /// <summary>
    /// The leaf name of a client-supplied file name (both separators honoured, so <c>..\..\x.pdf</c> and
    /// <c>../x.pdf</c> become <c>x.pdf</c>), normalised; null when nothing usable remains.
    /// </summary>
    public static string? SafeLeafName(string? clientFileName)
    {
        if (string.IsNullOrWhiteSpace(clientFileName))
        {
            return null;
        }

        var leaf = Normalize(clientFileName.Replace('\\', '/').Split('/')[^1]);
        return leaf.Length == 0 || leaf.Contains(AlternateStreamSeparator, StringComparison.Ordinal) ? null : leaf;
    }

    /// <summary>True when the normalised extension is in <paramref name="blockedExtensions"/> (a name merely containing one is fine).</summary>
    public static bool IsBlockedExtension(string fileName, IEnumerable<string> blockedExtensions)
    {
        ArgumentNullException.ThrowIfNull(blockedExtensions);
        var extension = NormalizedExtension(fileName);
        return extension.Length > 0 && blockedExtensions.Any(blocked => string.Equals(blocked, extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> under <paramref name="root"/>; false when the result escapes the root
    /// (<c>..</c>, an absolute path, a different drive) or is the root itself.
    /// </summary>
    public static bool TryResolveWithin(string root, string relativePath, out string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(relativePath);
        var fullRoot = Path.GetFullPath(root);
        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var contained = !Path.IsPathRooted(relativePath) && candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        fullPath = contained ? candidate : string.Empty;
        return contained;
    }
}
