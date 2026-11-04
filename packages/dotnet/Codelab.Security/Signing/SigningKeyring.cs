using System.Diagnostics.CodeAnalysis;

namespace Codelab.Security.Signing;

/// <summary>The master signing keys, by key id.</summary>
public interface ISigningKeyring
{
    /// <summary>The key id new sessions are bound to (their <c>skid</c> claim).</summary>
    string ActiveKeyId { get; }

    /// <summary>The master key for <paramref name="keyId"/>, when it is in the ring.</summary>
    bool TryGet(string? keyId, [NotNullWhen(true)] out byte[]? masterKey);
}

/// <summary>The configured master key ring. Built only from validated options.</summary>
public sealed class SigningKeyring : ISigningKeyring
{
    private readonly Dictionary<string, byte[]> _entries;

    private SigningKeyring(Dictionary<string, byte[]> entries, string activeKeyId)
    {
        _entries = entries;
        ActiveKeyId = activeKeyId;
    }

    /// <inheritdoc />
    public string ActiveKeyId { get; }

    /// <summary>The key ids in the ring.</summary>
    public IReadOnlyCollection<string> KeyIds => _entries.Keys;

    /// <summary>Builds the ring, or throws when the options do not pass the structural check.</summary>
    /// <exception cref="InvalidOperationException">The options are invalid (the message names the setting).</exception>
    public static SigningKeyring Create(RequestSigningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var verdict = options.ValidateStructure();
        if (!verdict.IsValid)
        {
            throw new InvalidOperationException(verdict.Error);
        }

        var entries = options.Keys.ToDictionary(
            key => key.KeyId!,
            key => Convert.FromBase64String(key.MaterialBase64!),
            StringComparer.Ordinal);
        return new SigningKeyring(entries, options.ActiveKeyId!);
    }

    /// <inheritdoc />
    public bool TryGet(string? keyId, [NotNullWhen(true)] out byte[]? masterKey)
    {
        masterKey = null;
        return !string.IsNullOrEmpty(keyId) && _entries.TryGetValue(keyId, out masterKey);
    }
}
