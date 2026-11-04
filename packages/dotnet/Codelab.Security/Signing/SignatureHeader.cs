using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Codelab.Security.Canonical;

namespace Codelab.Security.Signing;

/// <summary>
/// A parsed request proof header (<c>X-{Prefix}-Signature</c>): <c>1.{unixSeconds}.{nonce}.{mac}</c>, where the nonce
/// is 22 and the MAC 43 characters of unpadded base64url and the timestamp is 1-10 ASCII digits with no leading zero.
/// The parser is total and cheap: the length is checked before anything is scanned, and it never throws.
/// </summary>
public sealed class SignatureHeader
{
    /// <summary>The scheme version field.</summary>
    public const string SchemeVersion = "1";

    /// <summary>The field separator.</summary>
    public const char Separator = '.';

    /// <summary>Nonce length (16 random bytes as unpadded base64url).</summary>
    public const int NonceLength = 22;

    /// <summary>MAC length.</summary>
    public const int MacLength = HmacSignature.MacCharacters;

    /// <summary>Most timestamp digits.</summary>
    public const int MaxTimestampDigits = 10;

    /// <summary>The largest representable timestamp.</summary>
    public const long MaxTimestamp = 9_999_999_999L;

    /// <summary>Shortest well-formed header.</summary>
    public const int MinHeaderLength = 1 + 1 + 1 + 1 + NonceLength + 1 + MacLength;

    /// <summary>Longest well-formed header.</summary>
    public const int MaxHeaderLength = 1 + 1 + MaxTimestampDigits + 1 + NonceLength + 1 + MacLength;

    private const int FieldCount = 4;
    private const int TimestampField = 1;
    private const int NonceField = 2;
    private const int MacField = 3;

    private SignatureHeader(string timestamp, long timestampUnixSeconds, string nonce, string mac)
    {
        Timestamp = timestamp;
        TimestampUnixSeconds = timestampUnixSeconds;
        Nonce = nonce;
        Mac = mac;
    }

    /// <summary>The timestamp exactly as sent (it is a signed field).</summary>
    public string Timestamp { get; }

    /// <summary>The timestamp as Unix seconds.</summary>
    public long TimestampUnixSeconds { get; }

    /// <summary>The nonce.</summary>
    public string Nonce { get; }

    /// <summary>The MAC.</summary>
    public string Mac { get; }

    /// <summary>Parses a presented header; false for anything not exactly four well-formed fields.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out SignatureHeader? header)
    {
        header = null;
        if (value is null || value.Length is < MinHeaderLength or > MaxHeaderLength)
        {
            return false;
        }

        var fields = value.Split(Separator);
        return fields.Length == FieldCount
            && string.Equals(fields[0], SchemeVersion, StringComparison.Ordinal)
            && TryCreate(fields[TimestampField], fields[NonceField], fields[MacField], out header);
    }

    /// <summary>Creates a header from its fields; false when any field is malformed.</summary>
    public static bool TryCreate(string? timestamp, string? nonce, string? mac, [NotNullWhen(true)] out SignatureHeader? header)
    {
        header = null;
        if (timestamp is null || nonce is null || mac is null || !IsTimestamp(timestamp)
            || !Base64UrlText.IsUnpadded(nonce, NonceLength) || !Base64UrlText.IsUnpadded(mac, MacLength))
        {
            return false;
        }

        // IsTimestamp already proved at most ten unsigned ASCII digits; NumberStyles.None refuses signs and spaces.
        var seconds = long.Parse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture);
        header = new SignatureHeader(timestamp, seconds, nonce, mac);
        return true;
    }

    /// <summary>Creates a header from a Unix timestamp; false when it is out of range or a field is malformed.</summary>
    public static bool TryCreate(long timestampUnixSeconds, string? nonce, string? mac, [NotNullWhen(true)] out SignatureHeader? header)
    {
        header = null;
        return timestampUnixSeconds is >= 0 and <= MaxTimestamp
            && TryCreate(timestampUnixSeconds.ToString(CultureInfo.InvariantCulture), nonce, mac, out header);
    }

    /// <summary>The wire form.</summary>
    public string Format() => string.Join(Separator, SchemeVersion, Timestamp, Nonce, Mac);

    /// <inheritdoc />
    public override string ToString() => $"{nameof(SignatureHeader)} {{ Timestamp = {Timestamp}, Nonce = {Nonce}, Mac = [redacted] }}";

    private static bool IsTimestamp(string value)
    {
        if (value.Length is < 1 or > MaxTimestampDigits || (value[0] == '0' && value.Length > 1))
        {
            return false;
        }

        return value.All(char.IsAsciiDigit);
    }
}

/// <summary>The response signature header (<c>X-{Prefix}-Response-Signature</c>): <c>1.{mac}</c>.</summary>
public static class ResponseSignatureHeader
{
    /// <summary>The exact header length.</summary>
    public const int HeaderLength = 1 + 1 + SignatureHeader.MacLength;

    private const string VersionPrefix = SignatureHeader.SchemeVersion + ".";

    /// <summary>Parses a presented header into its MAC; false for anything else.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out string? mac)
    {
        mac = null;
        if (value is null || value.Length != HeaderLength || !value.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = value[VersionPrefix.Length..];
        if (!Base64UrlText.IsUnpadded(candidate, SignatureHeader.MacLength))
        {
            return false;
        }

        mac = candidate;
        return true;
    }

    /// <summary>The wire form of <paramref name="mac"/>.</summary>
    /// <exception cref="ArgumentException">The MAC is not 43 characters of unpadded base64url.</exception>
    public static string Format(string mac)
    {
        ArgumentNullException.ThrowIfNull(mac);
        return Base64UrlText.IsUnpadded(mac, SignatureHeader.MacLength)
            ? VersionPrefix + mac
            : throw new ArgumentException($"A response MAC must be {SignatureHeader.MacLength} characters of unpadded base64url.", nameof(mac));
    }
}
