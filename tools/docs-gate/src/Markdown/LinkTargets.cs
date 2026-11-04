using System.Text.RegularExpressions;

namespace Starter.Tools.DocsGate.Markdown;

/// <summary>Decides whether a link target is a local path and resolves it.</summary>
internal static partial class LinkTargets
{
    private const char FragmentStart = '#';
    private const char QueryStart = '?';
    private const char RootRelativeStart = '/';
    private const string ProtocolRelativeStart = "//";

    /// <summary>
    /// The full path a local target points at, or null when the target is not a local path:
    /// URLs with a scheme (https:, mailto:, ...), protocol-relative URLs and same-page anchors (#section).
    /// A leading '/' is relative to <paramref name="root"/>; anything else to <paramref name="documentDirectory"/>.
    /// The fragment and query are dropped (anchors inside files are not checked in v0) and %-escapes are decoded.
    /// </summary>
    public static string? ResolveLocalPath(string root, string documentDirectory, string target)
    {
        if (target[0] == FragmentStart
            || target.StartsWith(ProtocolRelativeStart, StringComparison.Ordinal)
            || SchemeRegex().IsMatch(target))
        {
            return null;
        }

        var path = Uri.UnescapeDataString(StripSuffix(target));
        if (path.Length == 0)
        {
            return null;
        }

        var baseDirectory = path[0] == RootRelativeStart ? root : documentDirectory;
        var relative = path.TrimStart(RootRelativeStart).Replace(RootRelativeStart, Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(baseDirectory, relative));
    }

    private static string StripSuffix(string target)
    {
        var end = target.IndexOfAny([FragmentStart, QueryStart]);
        return end < 0 ? target : target[..end];
    }

    // RFC 3986 scheme. Also matches a Windows drive ("C:"), which is never a portable link and is skipped.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex SchemeRegex();
}
