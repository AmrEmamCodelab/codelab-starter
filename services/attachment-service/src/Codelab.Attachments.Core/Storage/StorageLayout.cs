using System.Globalization;
using System.Text.Json;
using Codelab.Security.Uploads;

namespace Codelab.Attachments.Core.Storage;

/// <summary>The three paths of one stored file: the content, its sidecar metadata, and the staged content before the move.</summary>
internal sealed record StoredFilePaths(string Content, string Metadata, string StagedContent);

/// <summary>
/// The on-disk layout: <c>&lt;root&gt;/&lt;yyyy&gt;/&lt;MM&gt;/&lt;fileId&gt;</c> (no extension, so nothing on the share is
/// ever executable by name), <c>&lt;fileId&gt;.meta.json</c> beside it, and <c>.partial</c> files while staging. Every
/// path is resolved with <see cref="FileNames.TryResolveWithin"/>, so nothing is ever read or written outside the root.
/// </summary>
internal static class StorageLayout
{
    internal const string YearFormat = "yyyy";
    internal const string MonthFormat = "MM";
    internal const string MetadataSuffix = ".meta.json";
    internal const string StagedSuffix = ".partial";

    /// <summary>The name stored when nothing usable is left of the client's file name.</summary>
    internal const string FallbackFileName = "attachment";

    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);

    /// <summary>The paths of <paramref name="fileId"/> in the month folder of <paramref name="storedAt"/>.</summary>
    internal static StoredFilePaths PathsFor(string root, string fileId, DateTimeOffset storedAt)
    {
        var year = storedAt.ToString(YearFormat, CultureInfo.InvariantCulture);
        var month = storedAt.ToString(MonthFormat, CultureInfo.InvariantCulture);
        return PathsIn(root, Path.Combine(year, month), fileId);
    }

    /// <summary>The paths of <paramref name="fileId"/> in <paramref name="monthFolder"/> (relative to the root).</summary>
    /// <exception cref="InvalidOperationException">A path would leave the root (never for a well-formed id).</exception>
    internal static StoredFilePaths PathsIn(string root, string monthFolder, string fileId)
    {
        var content = ResolveWithin(root, Path.Combine(monthFolder, fileId));
        return new StoredFilePaths(content, ResolveWithin(root, Path.Combine(monthFolder, fileId + MetadataSuffix)), content + StagedSuffix);
    }

    /// <summary>The month folders under <paramref name="root"/> (relative paths), newest first.</summary>
    internal static IEnumerable<string> MonthFolders(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return NumberedFolders(root, YearFormat.Length)
            .SelectMany(year => NumberedFolders(Path.Combine(root, year), MonthFormat.Length).Select(month => Path.Combine(year, month)));
    }

    internal static async Task WriteMetadataAsync(string path, AttachmentFileMetadata metadata, CancellationToken cancellationToken)
    {
        var staged = path + StagedSuffix;
        var moved = false;
        try
        {
            await using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, MetadataJson, cancellationToken);
            }

            File.Move(staged, path);
            moved = true;
        }
        finally
        {
            if (!moved)
            {
                File.Delete(staged);
            }
        }
    }

    internal static async Task<AttachmentFileMetadata?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<AttachmentFileMetadata>(stream, MetadataJson, cancellationToken);
    }

    private static string ResolveWithin(string root, string relativePath) =>
        FileNames.TryResolveWithin(root, relativePath, out var fullPath)
            ? fullPath
            : throw new InvalidOperationException("A storage path resolved outside the storage root.");

    private static IEnumerable<string> NumberedFolders(string parent, int digits) =>
        Directory.EnumerateDirectories(parent)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.Length == digits && name.All(char.IsAsciiDigit))
            .OrderByDescending(name => name, StringComparer.Ordinal);
}
