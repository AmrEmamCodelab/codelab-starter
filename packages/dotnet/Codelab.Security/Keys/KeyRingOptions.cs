namespace Codelab.Security.Keys;

/// <summary>One key of a ring: an id that travels (or is looked up) and its base64 material (at least 32 bytes).</summary>
public sealed class KeyEntryOptions
{
    /// <summary>The key id.</summary>
    public string? KeyId { get; set; }

    /// <summary>The key material, base64. Supplied from the environment or a secret store, never from a committed file.</summary>
    public string? MaterialBase64 { get; set; }
}

/// <summary>
/// A rotating key ring: every listed key still verifies or decrypts, and <see cref="ActiveKeyId"/> is the one new
/// values are written with. Rotation is "add the new key, make it active, remove the old one after its values expire".
/// </summary>
public class KeyRingOptions
{
    /// <summary>The key new values are written with; must name a member of <see cref="Keys"/>.</summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>The ring.</summary>
    public IList<KeyEntryOptions> Keys { get; init; } = [];
}
