namespace Codelab.Identity.ActiveDirectory;

/// <summary>
/// Opens one LDAP connection. The production factory uses <c>System.DirectoryServices.Protocols</c>; a test registers
/// its own (in memory, no network) after <c>AddCodelabActiveDirectory</c>, or before it (the default is a TryAdd).
/// </summary>
public interface ILdapConnectionFactory
{
    /// <summary>Opens a connection to <paramref name="endpoint"/>. A connect failure may surface here or from the first <see cref="ILdapConnection.Bind"/>.</summary>
    ILdapConnection Open(LdapEndpoint endpoint);
}

/// <summary>One LDAP conversation: bind, then search as the bound identity. Every directory fault is an <see cref="LdapOperationException"/>.</summary>
public interface ILdapConnection : IDisposable
{
    /// <summary>A simple bind. Throws <see cref="LdapOperationException"/> with the LDAP result code when the directory refuses it.</summary>
    void Bind(LdapBindRequest request);

    /// <summary>The entries matching <paramref name="request"/>, in directory order. Empty means the directory matched nobody.</summary>
    IReadOnlyList<LdapEntry> Search(LdapSearchRequest request);

    /// <summary>
    /// The authorization identity of the bound user (RFC 4532 "Who am I?"): Active Directory answers
    /// <c>u:NETBIOS\sam</c>, other servers may answer <c>dn:...</c>. Null means this connection cannot ask; the
    /// sign-in then falls back to a weaker check (the found entry's <c>userPrincipalName</c> must be absent or equal the
    /// bind name). Throws <see cref="LdapOperationException"/> for a directory fault.
    /// </summary>
    string? WhoAmI() => null;
}

/// <summary>Where to connect and how long to wait.</summary>
public sealed record LdapEndpoint(string Host, int Port, bool UseLdaps, TimeSpan Timeout);

/// <summary>The bind name and the secret of a simple bind. <see cref="ToString"/> never prints the secret.</summary>
public sealed record LdapBindRequest(string BindName, string Password)
{
    /// <summary>The bind name only.</summary>
    public override string ToString() => $"{nameof(LdapBindRequest)} {{ {nameof(BindName)}: {BindName} }}";
}

/// <summary>A subtree search: where, what and which attributes to return.</summary>
public sealed record LdapSearchRequest(string BaseDn, string Filter, IReadOnlyList<string> Attributes, TimeSpan Timeout);

/// <summary>One search result: its DN and the first value of each returned attribute.</summary>
public sealed record LdapEntry(string DistinguishedName, IReadOnlyDictionary<string, string> Attributes)
{
    /// <summary>The value of <paramref name="name"/> (attribute names compare case-insensitively), or null.</summary>
    public string? Attribute(string name)
    {
        if (Attributes.TryGetValue(name, out var value))
        {
            return value;
        }

        return Attributes.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }
}

/// <summary>
/// A directory fault. <see cref="ResultCode"/> is the LDAP result code the directory answered with (see
/// <see cref="LdapResultCodes"/>), or null for a fault below LDAP (no route, TLS refused). The message names the
/// endpoint and the operation, never a credential.
/// </summary>
public sealed class LdapOperationException : Exception
{
    /// <summary>A fault with no result code.</summary>
    public LdapOperationException()
    {
    }

    /// <summary>A fault with no result code.</summary>
    public LdapOperationException(string message)
        : base(message)
    {
    }

    /// <summary>A fault with no result code, wrapping <paramref name="innerException"/>.</summary>
    public LdapOperationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>A fault the directory answered with <paramref name="resultCode"/>.</summary>
    /// <param name="message">Technical, safe to log: endpoint and operation, never a credential.</param>
    /// <param name="resultCode">The LDAP result code, or null below LDAP.</param>
    /// <param name="serverMessage">The directory's diagnostic text (Active Directory puts its sub-code there, <c>data 52e</c>).</param>
    /// <param name="innerException">The framework exception.</param>
    public LdapOperationException(string message, int? resultCode, string? serverMessage, Exception? innerException = null)
        : base(message, innerException)
    {
        ResultCode = resultCode;
        ServerMessage = serverMessage;
    }

    /// <summary>The LDAP result code, or null for a fault below LDAP.</summary>
    public int? ResultCode { get; }

    /// <summary>The directory's diagnostic message, when it sent one.</summary>
    public string? ServerMessage { get; }
}
