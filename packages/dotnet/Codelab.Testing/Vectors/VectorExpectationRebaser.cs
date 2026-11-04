using System.Text;

namespace Codelab.Testing.Vectors;

/// <summary>
/// Decides what a pinned expected value becomes when the run uses another protocol prefix. The default rebases
/// canonical values (fields whose name contains <c>Canonical</c>) by swapping the prefix, in plain text or inside
/// base64 (<c>…Base64</c> fields), and skips every other field: a signature or an encrypted id over different bytes
/// cannot be derived from the pinned one, so the cross-language check compares those between C# and TypeScript.
/// </summary>
public class VectorExpectationRebaser
{
    /// <summary>The marker of a canonical field name.</summary>
    public const string CanonicalMarker = "Canonical";

    /// <summary>The suffix of a base64-encoded field name.</summary>
    public const string Base64Suffix = "Base64";

    /// <summary>The default rebaser.</summary>
    public static VectorExpectationRebaser Default { get; } = new();

    /// <summary>The expected value for <paramref name="context"/>'s prefix, or null to skip the field.</summary>
    public virtual string? Rebase(string field, string pinnedValue, VectorRunContext context)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(pinnedValue);
        ArgumentNullException.ThrowIfNull(context);
        if (!field.Contains(CanonicalMarker, StringComparison.Ordinal))
        {
            return null;
        }

        return field.EndsWith(Base64Suffix, StringComparison.Ordinal)
            ? RebaseBase64(pinnedValue, context)
            : pinnedValue.Replace(context.PinnedPrefix, context.Prefix, StringComparison.Ordinal);
    }

    // Byte-level, so canonical bytes that are not valid UTF-8 survive unchanged around the prefix.
    private static string RebaseBase64(string pinnedValue, VectorRunContext context)
    {
        var bytes = Convert.FromBase64String(pinnedValue);
        var from = Encoding.UTF8.GetBytes(context.PinnedPrefix);
        var to = Encoding.UTF8.GetBytes(context.Prefix);
        var rebased = new List<byte>(bytes.Length);
        var index = 0;
        while (index < bytes.Length)
        {
            if (bytes.AsSpan(index).StartsWith(from))
            {
                rebased.AddRange(to);
                index += from.Length;
                continue;
            }

            rebased.Add(bytes[index]);
            index++;
        }

        return Convert.ToBase64String([.. rebased]);
    }
}
