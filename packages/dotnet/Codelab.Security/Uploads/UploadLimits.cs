using System.Security.Cryptography;
using Codelab.Security.Keys;

namespace Codelab.Security.Uploads;

/// <summary>Upload limits and receipts (<c>Codelab:Security:Uploads</c>).</summary>
public sealed class UploadSafetyOptions : KeyRingOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.Uploads;

    /// <summary>Extensions accepted (each must have a known magic-byte signature).</summary>
    public IList<string> AllowedExtensions { get; init; } = [".pdf", ".png", ".jpg", ".jpeg"];

    /// <summary>Extensions always refused, whatever the allowlist says.</summary>
    public IList<string> BlockedExtensions { get; init; } = [".exe", ".dll", ".bat", ".cmd", ".ps1", ".js", ".vbs", ".msi", ".scr", ".com"];

    /// <summary>The largest single file in bytes.</summary>
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>The most files in one request.</summary>
    public int MaxFileCount { get; set; } = 10;

    /// <summary>The largest total upload in bytes.</summary>
    public long MaxTotalBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>Sign upload receipts (the receipt key ring is then required).</summary>
    public bool ReceiptsEnabled { get; set; }
}

/// <summary>The verdict of a cap check.</summary>
public enum UploadCapVerdict
{
    /// <summary>Within every cap.</summary>
    Accepted = 0,

    /// <summary>One file too many.</summary>
    TooManyFiles = 1,

    /// <summary>The current file passed the per-file cap.</summary>
    FileTooLarge = 2,

    /// <summary>The request passed the total cap.</summary>
    TotalTooLarge = 3,
}

/// <summary>
/// Counts files and bytes while an upload streams in, so a cap is enforced as soon as it is crossed (not after the
/// whole body was written to disk). One instance per request; not thread-safe.
/// </summary>
public sealed class UploadCapCounter(UploadSafetyOptions options)
{
    private long _fileBytes;
    private long _totalBytes;

    /// <summary>Files started so far.</summary>
    public int FileCount { get; private set; }

    /// <summary>Starts a new file.</summary>
    public UploadCapVerdict BeginFile()
    {
        ArgumentNullException.ThrowIfNull(options);
        if (FileCount >= options.MaxFileCount)
        {
            return UploadCapVerdict.TooManyFiles;
        }

        FileCount++;
        _fileBytes = 0;
        return UploadCapVerdict.Accepted;
    }

    /// <summary>Records <paramref name="byteCount"/> more bytes of the current file.</summary>
    public UploadCapVerdict Record(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        _fileBytes += byteCount;
        _totalBytes += byteCount;
        if (_fileBytes > options.MaxFileBytes)
        {
            return UploadCapVerdict.FileTooLarge;
        }

        return _totalBytes > options.MaxTotalBytes ? UploadCapVerdict.TotalTooLarge : UploadCapVerdict.Accepted;
    }
}

/// <summary>SHA-256 of uploaded content (lower-case hex), computed while streaming.</summary>
public static class UploadDigest
{
    /// <summary>The lower-case hex SHA-256 of the remaining content of <paramref name="content"/>.</summary>
    public static async Task<string> ComputeSha256HexAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var digest = await SHA256.HashDataAsync(content, cancellationToken);
        return Convert.ToHexStringLower(digest);
    }
}
