using System.Buffers.Text;
using System.Security.Cryptography;

namespace Codelab.Attachments.Core.Storage;

/// <summary>
/// Stored-file identifiers: 128 random bits, base64url without padding (22 characters of <c>A-Z a-z 0-9 - _</c>).
/// Opaque, unguessable, safe in a URL and a file name, and never derived from the client's file name. An id that
/// does not have this exact shape is refused before any path is built from it.
/// </summary>
public static class AttachmentFileIds
{
    /// <summary>The random bytes behind an id.</summary>
    public const int RandomBytes = 16;

    /// <summary>The length of an id in characters.</summary>
    public static readonly int Length = Base64Url.GetEncodedLength(RandomBytes);

    /// <summary>A new random id.</summary>
    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>True when <paramref name="fileId"/> has the exact shape of an id this service issues.</summary>
    public static bool IsWellFormed(string? fileId) =>
        fileId is not null && fileId.Length == Length && fileId.All(IsIdCharacter);

    private static bool IsIdCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_';
}
