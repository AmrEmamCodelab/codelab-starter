using System.Globalization;
using System.Text;

namespace Codelab.Security.Canonical;

/// <summary>
/// <c>{Prefix}-CANON-v1</c>: the one way the package turns a list of fields into bytes that get hashed, signed or
/// bound as AAD (request and response signatures, session-key derivation, PublicId AAD and key derivation, tickets,
/// receipts).
/// <para>
/// <b>Format.</b> Every element (the version tag, the profile, the field count and each field) is written as
/// <c>{utf8ByteLength}:{value}\n</c>. Lengths are UTF-8 byte counts in invariant digits, so the boundaries are
/// unambiguous whatever the fields contain (delimiters, newlines, NUL, Arabic) and identical in every culture.
/// The TypeScript twin in <c>@codelab/ng-security</c> produces the same bytes; the golden vectors pin both.
/// </para>
/// </summary>
public sealed class CanonicalFraming
{
    private const char LengthSeparator = ':';
    private const char Terminator = '\n';

    private readonly string _version;

    /// <summary>Creates the framing for <paramref name="protocol"/> (its <see cref="SecurityProtocol.CanonicalVersion"/>).</summary>
    public CanonicalFraming(SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        _version = protocol.CanonicalVersion;
    }

    /// <summary>
    /// Frames <paramref name="fields"/> under <paramref name="profile"/>. Two profiles never collide: the same field
    /// values framed under different profiles produce different bytes.
    /// </summary>
    /// <exception cref="ArgumentNullException">The profile, the field array or any field is null.</exception>
    public string Build(string profile, params string[] fields) => Compose(_version, profile, fields);

    /// <summary>The UTF-8 bytes of <see cref="Build"/>: what actually gets hashed or signed.</summary>
    public byte[] BuildUtf8(string profile, params string[] fields) => Encoding.UTF8.GetBytes(Build(profile, fields));

    /// <summary>The framing with an explicit version tag (used for server-internal keys that are prefix-independent).</summary>
    internal static string Compose(string version, string profile, string[] fields)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(fields);

        var builder = new StringBuilder();
        Append(builder, version);
        Append(builder, profile);
        Append(builder, fields.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var field in fields)
        {
            // A null field is a programming error: treating it as "" would let two call sites share bytes.
            ArgumentNullException.ThrowIfNull(field, nameof(fields));
            Append(builder, field);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture))
            .Append(LengthSeparator)
            .Append(value)
            .Append(Terminator);
}
