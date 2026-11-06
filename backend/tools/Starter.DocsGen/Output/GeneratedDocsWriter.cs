using System.Text;

namespace Starter.DocsGen.Output;

/// <summary>Why a generated file drifted.</summary>
internal enum DriftKind
{
    /// <summary>The file is not in the output folder.</summary>
    Missing = 1,

    /// <summary>The file's bytes differ from what the code generates.</summary>
    Changed = 2,

    /// <summary>The output folder holds a file DocsGen does not generate.</summary>
    Unexpected = 3,
}

/// <summary>Display text of <see cref="DriftKind"/>.</summary>
internal static class DriftKinds
{
    /// <summary>The word printed before the file name.</summary>
    public static string Describe(DriftKind kind) => kind switch
    {
        DriftKind.Missing => "missing:",
        DriftKind.Changed => "changed:",
        DriftKind.Unexpected => "unexpected:",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown drift kind."),
    };
}

/// <summary>One drifting file.</summary>
internal sealed record FileDrift(string FileName, DriftKind Kind);

/// <summary>The result of <see cref="GeneratedDocsWriter.Compare"/>.</summary>
internal sealed record DriftReport(IReadOnlyList<string> UpToDate, IReadOnlyList<FileDrift> Drifts)
{
    /// <summary>True when nothing drifted.</summary>
    public bool IsClean => Drifts.Count == 0;
}

/// <summary>The result of <see cref="GeneratedDocsWriter.Write"/>.</summary>
internal sealed record WriteReport(IReadOnlyList<string> Written, IReadOnlyList<string> Unchanged, IReadOnlyList<string> Unexpected);

/// <summary>
/// Writes the generated files into one folder (UTF-8 without BOM, exactly the rendered bytes) or compares the folder
/// with them. Document names are relative to the folder and may hold one or more <c>/</c>-separated subfolders
/// (<c>workflows/service-request.md</c>). It never deletes: a file DocsGen does not generate, in the folder itself or
/// in a subfolder it writes to (a workflow sheet whose record type is gone), is reported, not removed.
/// </summary>
internal sealed class GeneratedDocsWriter(string outputDirectory)
{
    private const char DocumentSeparator = '/';

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Compares the folder with <paramref name="documents"/> (file name to content).</summary>
    public DriftReport Compare(IReadOnlyDictionary<string, string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var upToDate = new List<string>();
        var drifts = new List<FileDrift>();
        foreach (var (fileName, content) in documents.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var path = PathOf(fileName);
            if (!File.Exists(path))
            {
                drifts.Add(new FileDrift(fileName, DriftKind.Missing));
            }
            else if (IsCurrent(path, content))
            {
                upToDate.Add(fileName);
            }
            else
            {
                drifts.Add(new FileDrift(fileName, DriftKind.Changed));
            }
        }

        drifts.AddRange(UnexpectedFiles(documents).Select(fileName => new FileDrift(fileName, DriftKind.Unexpected)));
        return new DriftReport(upToDate, drifts);
    }

    /// <summary>Writes every file whose bytes changed; creates the folder when needed.</summary>
    public WriteReport Write(IReadOnlyDictionary<string, string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();
        var unchanged = new List<string>();
        foreach (var (fileName, content) in documents.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var path = PathOf(fileName);
            if (File.Exists(path) && IsCurrent(path, content))
            {
                unchanged.Add(fileName);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Utf8NoBom.GetBytes(content));
            written.Add(fileName);
        }

        return new WriteReport(written, unchanged, UnexpectedFiles(documents));
    }

    private static bool IsCurrent(string path, string content) =>
        File.ReadAllBytes(path).AsSpan().SequenceEqual(Utf8NoBom.GetBytes(content));

    private string PathOf(string documentName) =>
        Path.Combine([outputDirectory, .. documentName.Split(DocumentSeparator)]);

    /// <summary>The files of the folder and of every subfolder a document lives in that no document names.</summary>
    private List<string> UnexpectedFiles(IReadOnlyDictionary<string, string> documents)
    {
        var folders = documents.Keys
            .Select(name => name.LastIndexOf(DocumentSeparator) is var at and >= 0 ? name[..at] : string.Empty)
            .Append(string.Empty)
            .Distinct(StringComparer.Ordinal);
        return [.. folders
            .SelectMany(folder => FilesIn(folder))
            .Where(name => !documents.ContainsKey(name))
            .Order(StringComparer.Ordinal)];
    }

    private IEnumerable<string> FilesIn(string folder)
    {
        var directory = folder.Length == 0 ? outputDirectory : PathOf(folder);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var prefix = folder.Length == 0 ? string.Empty : folder + DocumentSeparator;
        return Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().Select(name => prefix + name);
    }
}
